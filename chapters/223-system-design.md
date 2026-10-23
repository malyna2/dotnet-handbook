# Chapter 23: System Design

A design prompt gives you forty-five minutes and one vague sentence: "design a URL shortener", "design checkout". There is no single right answer, and nobody is grading the diagram. In an interview or an architecture review, people check three things: that every box traces back to a requirement or a number, that you know what each box costs, and that you can say what breaks first when the numbers grow.

The thread through all of it: **numbers first, then the architecture those numbers justify, then the failure each choice brings in.**

## A Repeatable Approach

Use the same seven steps every time; each produces what the next one needs.

1. **Clarify requirements.** Separate *functional* requirements (what it does) from *non-functional* ones (how well), and turn the second kind into numbers. Availability: 99.9% allows about 43 minutes of downtime in a 30-day month, 99.99% about 4.3. Latency is a percentile: "p99 under 200 ms for redirects". Durability: may an acknowledged write be lost? Consistency, per operation: may a user read stale data, and for how long? [Chapter 25](#alerting-slis-slos-slas-and-error-budgets) turns these into SLOs. Ask how many users, read-heavy or write-heavy, and what must never happen.
2. **Estimate scale.** Peak requests per second, storage per year, bandwidth, concurrency: the next section.
3. **Define the API.** A few signatures pin down the contract and surface hidden requirements: who calls, what is idempotent, what returns `202`.
4. **Design the data model** from the **access patterns**: which queries run on every request, keyed by what. They choose the store and the keys, not the other way round.
5. **Sketch the high-level design** along the request path, showing the read path and the write path.
6. **Go deep on the hard parts**, the two or three places the design can be wrong: name the options, choose one, say what you gave up.
7. **Say how you'd operate it**: what you'd monitor, and what happens when each dependency fails.

In a 45-minute interview, a workable split is about 5–8 minutes for requirements, 5 for estimates, 5–8 for the API and data model, 10 for the high-level design, 15 for the hard parts and 5 for failure. It is a suggestion; what matters is that the hard parts get the most time, because that is where seniority shows.

> **Pay attention.** **Why requirements come first even when the prompt looks obvious.** Every later choice is a trade-off, and you can only judge a trade-off against a requirement. "Should redirects be cached at the CDN?" has no answer until you know whether per-click analytics are required: a redirect served from the CDN never reaches your servers, so it can't be counted. Skipping requirements doesn't save five minutes; it removes anything to justify later choices against. The fix: before the first box, write down three to five functional requirements and the non-functional targets as numbers, and ask which matter most.

## Back-of-the-Envelope Estimation

Estimation finds the **order of magnitude**, and the order of magnitude decides the architecture. At 10 requests per second, one small instance and one database will do. At 10,000 you need caching, replicas and probably asynchronous writes. At a million you are partitioning everything. Say your assumptions out loud so the listener can correct them, and round hard: "about 4,000 per second" beats "3,858 per second", which claims a precision the inputs never had.

### The constants worth knowing by heart

| Quantity | Exact | Use it as |
|---|---|---|
| Seconds in a day | 86,400 | ≈ 10⁵ |
| Seconds in a 30-day month | 2,592,000 | ≈ 2.5 × 10⁶ |
| Seconds in a year | 31,536,000 | ≈ 3 × 10⁷ |
| 1 million requests per day | 11.6 per second | ≈ 12 per second |
| 2¹⁰, 2²⁰, 2³⁰, 2⁴⁰ bytes | KiB, MiB, GiB, TiB | ≈ 10³, 10⁶, 10⁹, 10¹² |
| 1 Gbit/s | 125 MB/s | 8 bits per byte |
| 99.9% / 99.99% availability | 43.2 / 4.3 minutes down per 30-day month | 8.8 hours / 53 minutes a year |

Dividing by 10⁵ instead of 86,400 understates the rate by about 15%, well inside the error of every other input.

### From users to requests per second

The chain is: **daily active users × actions per user per day ÷ 86,400 = average requests per second**, times a **peak factor**, because traffic is never flat. A daily cycle puts the busiest hour at a small multiple of the average; a flash sale or a push to every user can put one minute at a far larger one. Ask for the peak factor or assume one and say so; on a running system, read it off your dashboards.

An illustrative estimate (assumptions for the exercise, not measurements): 10 million daily users × 20 reads each is 200 million reads a day, ÷ 10⁵ ≈ **2,000 reads/s** on average, × 3 for the peak ≈ **6,000–7,000 reads/s**. One write per user a day is 10 million writes, ≈ **300 writes/s** at peak. The 20:1 read-to-write ratio matters more than either number: caching and read replicas will pay off, and the write path can stay simple.

### Storage per year

**Writes per day × bytes per record × days of retention × copies you run**, plus indexes. 10 million records a day at about 1 KB is 10 GB a day, roughly 3.65 TB a year before replicas and indexes. The number answers three questions: does a year fit on one node, can old data move to a cheaper tier, and which part is really blobs that belong in object storage with only a URL in the database ([Chapter 15](#file-and-blob-storage-stream-dont-buffer))?

### Bandwidth

**Peak requests per second × response size.** 7,000 responses a second of 10 KB is about 70 MB/s (560 Mbit/s) of egress, which app servers can serve. At 500 KB per image it would be 3.5 GB/s, which belongs on a CDN and is a cost question as much as a capacity one ([Chapter 31](#part-b-cloud-cost-finops)).

### Concurrency: Little's law

The one queueing formula every designer uses: **L = λ × W**. In a steady state, the average number of items inside a system (L) equals their arrival rate (λ) times the average time each spends inside (W), whatever the arrival pattern.

Applied to a service: 7,000 requests per second at 100 ms each means about **700 requests in flight** across the fleet. If each holds a database connection for 20 ms, about 7,000 × 0.02 = **140 connections are busy** on average. Set that against the pool size and the database's connection limit ([Connection Management Under Load](#connection-management-under-load) in Chapter 18) and you know whether the design fits before anything is built.

> **Pay attention.** **Why a slow dependency exhausts a pool that was "big enough".** Little's law has two inputs, and people only watch the first. A downstream call that normally takes 20 ms slows to 2 s. The arrival rate hasn't changed, but W grew a hundredfold, so the number of requests holding a connection or a thread grew a hundredfold too: 140 busy connections become 14,000 wanted ones. The pool runs dry, new requests queue behind it, their latency grows, and the queue feeds itself. Nothing was "overloaded" in the usual sense. The fix is to bound W, not to raise the pool: a timeout on every call, a circuit breaker that fails fast once the dependency is clearly unhealthy, and a concurrency limit per dependency ([Chapter 20](#detecting-failure-and-retrying-without-making-it-worse) builds that pipeline).

### How many servers?

**Peak requests per second ÷ what one instance sustains within the latency target**, plus headroom for losing a zone and for deploys. The divisor can't be estimated on a whiteboard: a cached endpoint and a report generator differ by orders of magnitude on the same hardware. Take it from a load test ([Load Testing](#load-testing-proving-it-under-pressure) in Chapter 25), or state it as an assumption. The headroom is design, not padding: in three zones that must survive losing one, two zones carry the peak, so you need 1.5 times the instances.

### Latency numbers every programmer should know

The table below gives the orders of magnitude Peter Norvig and Jeff Dean made famous. Use it for **ratios between operations**, not as measurements of your environment.

| Operation | Order of magnitude |
|---|---|
| L1 cache reference | ~1 ns |
| Main memory reference | ~100 ns |
| Compress 1 KB with a fast codec (Snappy) | ~1–2 µs |
| Random read from an SSD | ~tens of µs |
| Round trip within one datacenter | ~500 µs |
| Disk seek on a spinning drive | ~milliseconds |
| Round trip California → Netherlands → California | ~150 ms |

*Source: the constants in Colin Scott's "Numbers Every Programmer Should Know By Year" (github.com/colin-scott/interactive_latencies), which reproduces Norvig's and Dean's figures and extrapolates some of them by year. Your region and network will differ: measure the one hop your design depends on.*

What the ratios buy you in a design:

- **A Redis hit is a network round trip.** Redis answers in microseconds, but the caller pays the round trip, the same order as a primary-key read from a database with the row in memory. A distributed cache takes load off the database and keeps latency predictable; it doesn't make one read a thousand times faster. That needs an **in-process** cache, a memory reference instead of a round trip, which is why two-level caching (in-process L1, Redis L2: `HybridCache` in [Chapter 7](#invalidation-and-stampede)) pays off for very hot keys.
- **Sequential calls add up, and distance multiplies them.** Five sequential calls in a region cost a few milliseconds; five across an ocean cost most of a second. Keep a request's dependencies in its region and run independent calls in parallel.
- **Budget the latency.** A p99 target of 200 ms is split across hops: edge, app, cache, database, each downstream call. A hop that can't fit its share moves off the request path.

> **Pay attention.** **Why fan-out makes the tail the common case.** Suppose one backend call is slow 1% of the time, and a request waits for 100 such calls in parallel. The request is fast only if all 100 are fast: 0.99¹⁰⁰ ≈ 37%, so **63% of requests** wait for at least one slow call. The p99 of a part has become the median of the whole. Dean and Barroso describe this in *The Tail at Scale*. The fixes go after the mechanism: fewer calls per request, a timeout that returns partial results where the product allows it, and **hedged requests** for idempotent reads (send a second copy once the first has taken longer than the p95, and take whichever answers first).

## The Building Blocks

Most designs are a handful of components arranged around the request path. For each one, know what it buys, what it costs, and where this book teaches it. The Azure column gives one way to run each block, not a recommendation; [Chapter 28](#mapping-aws-and-azure-services) maps the AWS equivalents.

| Block | Buys | Costs | Taught in | One Azure realization |
|---|---|---|---|---|
| **Load balancer** (L4/L7) | Horizontal scaling; no single instance is a point of failure | A hop; requires stateless app servers | [Chapter 26](#load-balancers-reverse-proxies-api-gateways-and-cdns) | Front Door (global), Application Gateway (regional), built into App Service and Container Apps |
| **CDN** | Latency at the edge; traffic that never reaches the origin | Invalidation; cache-key discipline | [Chapter 26](#cdns-and-caching-headers) | Front Door |
| **Distributed cache** | Read throughput, predictable latency | Staleness, invalidation, stampedes, one more dependency | [Chapter 7](#the-cache-aside-pattern), [Chapter 18](#redis-in-practice-key-design-data-types-and-eviction) | Azure Managed Redis |
| **Relational database + replicas** | Transactions, constraints, joins; replicas scale reads | One write primary; replication lag | [Chapter 7](#chapter-7-data-access), [Chapter 18](#scaling-reads-with-replication) | Azure SQL Database, Azure Database for PostgreSQL |
| **Document / key-value store** | Horizontal scale by partition key | Per-partition limits, no cross-partition transactions, a price per operation | [Chapter 18](#nosql-and-polyglot-persistence), [Chapter 29](#cosmos-db-for-nosql) | Cosmos DB |
| **Queue / broker** | Decoupling, absorbing spikes, retries | Eventual consistency, duplicates, dead letters to operate | [Chapter 11](#chapter-11-messaging-and-background-work), [Chapter 20](#chapter-20-distributed-systems) | Service Bus |
| **Event log** | High-throughput streams, replay, many independent readers | Partition count fixes parallelism and order | [Chapter 11](#chapter-11-messaging-and-background-work), [Chapter 29](#messaging-service-bus-event-hubs-and-event-grid) | Event Hubs |
| **Object storage** | Cheap, durable storage for large blobs | Not queryable; access must be authorized per object or container | [Chapter 15](#file-and-blob-storage-stream-dont-buffer) | Blob Storage |
| **Search index** | Full-text search and relevance | A second copy of the data to keep in sync | [Chapter 18](#change-data-capture-cdc) | Azure AI Search |

Under the table: the app tier must be **stateless** for the load balancer to help ([the 12-factor app](#the-12-factor-app)), and every arrow is a network call that can be slow, fail, or succeed without telling you ([Chapter 20](#chapter-20-distributed-systems)).

## Scaling Patterns

When step 6 finds a bottleneck, the fix is almost always one of five moves, roughly in order of cost:

- **Vertical scaling**: a bigger machine; no code changes, a hard ceiling, and underrated.
- **Caching**: the highest-leverage move for read-heavy data that tolerates some staleness.
- **Horizontal scaling of the app tier**: more stateless instances behind a load balancer.
- **Database scaling**: replicas for reads; *sharding* by a key for writes and data size ([Chapter 18](#partitioning-and-sharding), including replication lag and resharding).
- **Asynchronous processing**: slow work moves off the request path onto queues and workers.

**What usually breaks first** as traffic grows, as a rule of thumb: connection pools and the database's write primary; then hot keys and hot partitions; then shared single-threaded components (one Redis node, one sequence, one lock); then third-party limits, since payment and SMS providers throttle you long before your servers are busy. "What breaks first?" checks that you walk the request path looking for **the resource with the least headroom**, not that you recite this list.

## How to Read the Worked Designs

The four designs share one shape: **requirements and targets → estimates → API → data model → high-level diagram → hard parts and trade-offs → what you'd monitor → how it fails.** Every number in them is an **illustrative assumption** of the kind you'd state in an interview, not a measurement of a real system. Their C# and SQL were compiled and run against .NET 10, PostgreSQL 17 and Redis 7.4.

## Worked Design 1: A URL Shortener

Turn a long URL into a short code (like `bit.ly`) and redirect anyone who visits it. It looks trivial, which makes it a good test: the interest is in the ID generator, the read path under a viral link, and the abuse nobody mentions.

**Requirements and targets.** Functional: create a code for a URL; redirect a code; optionally aliases, expiry and click counts. Non-functional: the redirect path is everything: fast (say p99 under 50 ms at the edge) and highly available, because a failed redirect breaks every published link. Creation can be slower and less available. Codes must not be guessable if links may point at private documents.

**Estimates (illustrative).** 100 million new links a month is about 100 × 10⁶ ÷ 2.5 × 10⁶ ≈ **40 writes per second**. At 100 redirects per link-creation, that is about **4,000 redirects per second** on average, perhaps 12,000 at peak. Storage: 100 million a month for 5 years is 6 billion rows; at about 500 bytes each that is **3 TB**. Writes are trivial and reads are heavy: this shape calls for caching the reads and keeping the write path boring.

**API.**

```
POST /links        { "url": "https://very/long/url", "alias": null, "expiresAt": null }
                   -> 201 { "code": "q7Rk2xVa", "shortUrl": "https://sho.rt/q7Rk2xVa" }
GET  /{code}       -> 302 Location: https://very/long/url     (or 301; see below)
                   -> 404 if unknown or expired
```

**Data model.** One table, `links`: `code` as the primary key, `long_url`, the owner, `created_at` and `expires_at`. One read pattern (by code) and one write pattern (insert): a key-value workload that a relational table keyed by code serves as well as a document store partitioned by code.

**High-level design.**

```
                         ┌──────────── read path (≈ 99%) ─────────────┐
client ──► CDN / edge ──►│ app (stateless) ──► L1 in-process ──► Redis │──► DB replica
              │          └────────────────────────────────────────────┘
              │          ┌──────────── write path ────────────┐
              └─────────►│ app ──► code generator ──► DB primary │
                         └────────────────────────────────────┘
   click events ──► Event Hubs / Kafka ──► analytics consumer   (off the redirect path)
```

### Generating the code

This is the hard part interviewers come for, and there are four real options.

| Option | How | Good | Bad |
|---|---|---|---|
| Sequence + Base62 | A database sequence, encoded in Base62 | Short, no collisions, trivial | **Enumerable**: anyone can walk every link; one sequence is a shared write point |
| Hash of the URL | First characters of a hash of the URL | The same URL gets the same code | Collisions must be checked anyway; truncating the hash removes its guarantees |
| Random code | 7–8 random Base62 characters; the primary key rejects a collision and you draw again | Unguessable; no coordination between instances | A retry path; the code space must stay sparse |
| Pre-allocated ranges | Each instance leases a block of IDs (say 1,000) from a counter row, then hands them out from memory | No round trip per link; no hot counter | Enumerable unless mixed; a crash wastes the rest of a block (harmless) |

Base62 (`0-9`, `a-z`, `A-Z`) packs about 62⁷ ≈ 3.5 trillion codes into 7 characters and 62⁸ ≈ 218 trillion into 8. Encoding a number is a base conversion, the "choose the encoding for the constraint" thinking from [Big-O](#big-o-the-language-of-how-bad-does-this-get):

```csharp
public static class Base62
{
    private const string Alphabet =
        "0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";

    public static string Encode(long id)
    {
        if (id == 0) return "0";
        Span<char> buf = stackalloc char[11];   // 62^11 > long.MaxValue
        var i = buf.Length;
        while (id > 0)
        {
            buf[--i] = Alphabet[(int)(id % 62)];
            id /= 62;
        }
        return new string(buf[i..]);
    }
}
```

(Fill the buffer from the end and slice. Building the digits most-significant first with `Insert(0, …)` in a loop would be exactly the accidental O(n²) that [Chapter 2](#chapter-2-data-structures-and-algorithms-essentials) warns about.)

When links can point at private documents, choose **random codes** and let the database decide uniqueness:

```csharp
public static class ShortCode
{
    private const string Alphabet =
        "0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";

    // 62^8 ≈ 2.2 × 10^14 codes, drawn from a CSPRNG: nothing to enumerate.
    public static string NewRandom(int length = 8) =>
        RandomNumberGenerator.GetString(Alphabet, length);
}

public async Task<string> CreateAsync(string longUrl, CancellationToken ct)
{
    for (var attempt = 0; attempt < 3; attempt++)
    {
        var link = new Link { Code = ShortCode.NewRandom(), LongUrl = longUrl, CreatedAt = DateTimeOffset.UtcNow };
        _db.Links.Add(link);
        try
        {
            await _db.SaveChangesAsync(ct);   // the primary key on Code arbitrates
            return link.Code;
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            _db.ChangeTracker.Clear();        // collided: forget the entity, draw again
        }
    }
    throw new InvalidOperationException("Three collisions in a row: the code space is too full.");
}
```

`IsUniqueViolation` checks the provider's error code (`23505` on PostgreSQL, 2601 or 2627 on SQL Server), as in [Chapter 20](#idempotency-keys-making-post-retry-safe).

> **Pay attention.** **Why a random code needs a retry loop and not a "does it exist?" check.** A new random code collides with probability equal to the fraction of the space already used: with 6 billion links in 62⁸ codes, about 0.003% per insert, so retries cost almost nothing. "SELECT, see nothing, INSERT" adds a round trip and still races, because two instances can draw the same code and both see nothing. The primary key is the only arbiter that can't race: insert, and react to the violation. Watch the collision rate; when it rises as the space fills, add a character.

### Hot keys and the read path

Redirects are a cache-aside read ([Chapter 7](#the-cache-aside-pattern)): Redis first; on a miss read a replica, store the result with a jittered TTL, redirect. Traffic concentrates on recent and popular links: if about 10 million distinct links are clicked a day at about 500 bytes per entry (illustrative), the hot set is about 5 GB, one Redis node.

The failure that needs a design is the **hot key**. One link goes viral and a large share of all redirects ask for one code. In a Redis cluster a key lives on exactly one shard, so that shard takes the whole spike while the others idle, and adding shards doesn't help. Three things do:

- **An in-process L1 cache** with a short TTL (seconds) in each app instance. A viral code is then served from memory by every instance, and Redis sees at most one request per instance per TTL. This is the two-level shape from the latency section, and `HybridCache` provides it.
- **Caching at the edge.** A redirect the CDN answers never reaches you. A 301 is cacheable by default (RFC 9110 lists it as heuristically cacheable) and browsers keep it, so you lose click counts and can't change or expire the target later. A 302 is not cacheable unless you add `Cache-Control`. The usual compromise is a 302 with a short `max-age`: the edge absorbs the spike, and the destination can still change within that time.
- **Negative caching.** Scanners and typos ask for codes that don't exist, and every one of those misses goes to the database. Cache "not found" for a short time too, or the cheapest attack on you is a list of random codes.

### Counting clicks without slowing redirects

The redirect must not wait for analytics. Put each click into a bounded in-memory buffer that a background worker drains onto a log (Event Hubs or Kafka) for a consumer to aggregate, so a slow pipeline drops clicks instead of slowing redirects. That is a deliberate **at-most-once** choice, fine for analytics. If clicks bill a customer, they need the outbox and at-least-once delivery from [Chapter 11](#the-transactional-outbox).

**What you'd monitor.** Redirect p99 at the edge and at the origin; cache hit rate per layer (a falling Redis hit rate is the early warning); the 404 rate (a sudden rise means scanning); collision retries per minute; database CPU and replica lag.

**How it fails.**

- *Redis is down:* every redirect becomes a database read. Protect the replicas with a concurrency limit and a timeout and keep serving L1 hits, so failure is partial instead of total.
- *The primary is down:* creation fails, redirects carry on from caches and replicas. Say it explicitly, because it is the design choice: **reads are more available than writes**.
- *A region is down:* the read path can run in two regions (Front Door, a geo-replica), with creation in one; creating in two would need region-prefixed codes or ranges.
- *Abuse:* a shortener is a gift to phishing. Scan new URLs asynchronously, have a takedown path, and rate-limit creation per account, which is the next design.

## Worked Design 2: A Distributed Rate Limiter

"At most N requests per client per period", enforced across every instance of an API. [Chapter 26](#rate-limiting-against-someone-who-is-trying) treats rate limiting as an operational control: what to key on, where in the network to put it, and how attackers get around it. This design is the system behind a limiter that holds across many instances.

**Requirements and targets.** Limit each API key by tier (say 600 requests a minute with short bursts for the standard tier); answer an over-limit request with `429` and `Retry-After`; hold the limit across all instances; add little latency to every request; never become the reason the API is down.

**Estimates (illustrative).** At 20,000 requests per second across 30 instances, a shared check on every request is 20,000 store operations per second on the request path. A million keys active a day at about 100 bytes each is about 100 MB of state, which is small. The real costs are a round trip per request and a shared store that the whole API now depends on.

**API and data model.** No public API: middleware in front of every endpoint, plus policies (tier, limit, period) that operators change without a deploy. The contract with clients is `429 Too Many Requests` with `Retry-After` ([Chapter 5](#status-codes-and-headers-that-matter)). The state is one small record per key and policy (tokens left and the last refill time, or a count per window), short-lived with a TTL: a fit for Redis, not for a relational table.

**High-level design.** Three layers: a coarse limiter at the edge (CDN, WAF or API gateway, per IP or key); an in-process limiter in each instance for self-protection; and a shared limiter that runs one atomic script per request against Redis, answers `429` when denied, and fails open or closed by policy when Redis is slow or down.

### Choosing the algorithm

| Algorithm | State per key | Behaviour |
|---|---|---|
| Fixed window | One counter | Simple; up to 2× the limit across a window boundary |
| Sliding window log | A timestamp per request | Exact; memory grows with the limit |
| Sliding window counter | Two counters, or a few segments | Close to exact; smooths the boundary |
| Token bucket | Tokens + last refill time | Allows bursts up to the bucket size and a steady rate after that; usually the right default for APIs |
| Concurrency limit | In-flight count | Bounds work running at once, not the rate; the right tool for expensive endpoints |

### What ASP.NET Core's limiter covers, and what it doesn't

`Microsoft.AspNetCore.RateLimiting` ([Chapter 5](#cross-cutting-http-concerns) introduces it) gives you fixed window, sliding window, token bucket and concurrency limiters, partitioned by any key you compute from the request:

```csharp
builder.Services.AddRateLimiter(options =>
{
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(http =>
    {
        string? key = http.Request.Headers["X-Api-Key"];
        // Partition only on keys you issued: each distinct key creates and caches a limiter.
        string partition = key is not null && ApiKeys.IsIssued(key) ? key : "anonymous";
        return RateLimitPartition.GetTokenBucketLimiter(partition, _ => new TokenBucketRateLimiterOptions
        {
            TokenLimit = 20,                                // the burst a client may spend at once
            TokensPerPeriod = 10,                           // the sustained rate: 10 per second
            ReplenishmentPeriod = TimeSpan.FromSeconds(1),
            QueueLimit = 0,                                 // reject at once, don't queue
        });
    });
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;   // the default is 503
    options.OnRejected = (context, _) =>
    {
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out TimeSpan retryAfter))
            context.HttpContext.Response.Headers.RetryAfter =
                ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
        return ValueTask.CompletedTask;
    };
});
app.UseRateLimiter();
```

Against a burst of 30 requests per key, this admits 20 and rejects 10 with `429` and `Retry-After: 1`, one bucket per issued key. Three details are easy to miss. **The default rejection status is `503`** (set in `RateLimiterOptions`), so set `429` yourself. **`Retry-After` is sent only if you copy it** from the lease metadata in `OnRejected`; a concurrency limiter has none to copy. **Every distinct partition key creates and caches a limiter**, so keying on raw client input lets a client make you allocate without bound, as the docs warn; here unknown keys share one `anonymous` bucket.

What it doesn't do is why this design exists: **its state lives in the process.** With 30 instances behind a load balancer, a client gets up to 30 times the limit, unevenly, and a restart refills the bucket. That makes it right for **self-protection** (one instance refuses more than it can handle) and wrong for a **contract** such as "600 a minute per key", which needs a shared counter.

### A shared token bucket in Redis

The shared version keeps the bucket in Redis and updates it with a Lua script, so that the read, the refill, the decision and the write happen as **one atomic step**:

```csharp
public sealed class RedisTokenBucket(IConnectionMultiplexer redis)
{
    // KEYS[1] = the bucket; ARGV = capacity, tokens per second, cost of this request.
    // Atomic: Redis runs a script to completion before it serves any other command.
    private const string Script = """
        local t = redis.call('TIME')                       -- the server's clock, not the caller's
        local now = t[1] * 1000 + math.floor(t[2] / 1000)  -- milliseconds
        local capacity, rate, cost = tonumber(ARGV[1]), tonumber(ARGV[2]), tonumber(ARGV[3])
        local b = redis.call('HMGET', KEYS[1], 'tokens', 'ts')
        local tokens = tonumber(b[1]) or capacity
        local ts = tonumber(b[2]) or now
        tokens = math.min(capacity, tokens + (now - ts) * rate / 1000)
        local allowed = tokens >= cost
        if allowed then tokens = tokens - cost end
        redis.call('HSET', KEYS[1], 'tokens', tokens, 'ts', now)
        redis.call('PEXPIRE', KEYS[1], math.ceil(capacity / rate * 1000) + 1000)
        return allowed and 1 or 0
        """;

    public async Task<bool> TryAcquireAsync(string clientId, int capacity, double perSecond)
    {
        RedisResult result = await redis.GetDatabase().ScriptEvaluateAsync(
            Script, [new RedisKey($"rl:{clientId}")], [capacity, perSecond, 1]);
        return (int)result == 1;
    }
}
```

The script reads **Redis's own clock** with `TIME`, so thirty instances with slightly different clocks can't disagree about the refill ([Chapter 20](#distributed-time-why-you-cant-trust-the-clock) explains why app clocks can't be trusted). The key **expires** once a full bucket would have refilled, so idle clients cost no memory, and one key per call keeps the script on one Redis Cluster slot.

> **Pay attention.** **Why "GET, compare, INCR" lets the whole burst through.** The obvious limiter reads the count, compares it with the limit, then increments. Between the read and the increment, every other instance reads the same old count, so all of them allow the request. In a run against a local Redis 7.4, 200 concurrent requests against a limit of 10 got nearly all 200 through with GET-compare-INCR, and exactly 10 through with the script. The fix is to make the decision a single atomic operation on the server: a script, or `INCR` first and compare the *returned* value. The second form has a trap of its own. The fixed-window idiom is `INCR` and then `EXPIRE`, and a crash between those two commands leaves a counter with no TTL that never resets, so the client stays blocked forever. Set the expiry inside the same script, or create the key with its TTL in one command.

### Latency, the hot key, and failing open

Every request now pays a datacenter round trip to Redis. **Local leases** cut it: each instance takes tokens in batches (say 10) and spends them from memory, at the price of accuracy, since tokens leased to an idle instance are wasted. **A local pre-check**, an in-process limiter set a little above the per-instance share, rejects obvious floods before they reach Redis. A single huge tenant is a hot key again; splitting its bucket into N sub-buckets, each with 1/N of the rate and picked at random, spreads the load for a little precision.

Then decide what happens when Redis is slow or down, as a **policy per limit**:

- **Fail open** for fairness and abuse limits on ordinary endpoints: a Redis outage shouldn't take down the API the limiter protects, and the in-process limiter remains as each instance's backstop.
- **Fail closed** for limits that protect money: paid SMS, LLM calls (*Denial of wallet* in [Chapter 26](#chapter-26-delivery-and-platform)).

Either way, give the Redis call a short timeout and a circuit breaker, or a struggling Redis becomes a latency problem for the whole API. **On Azure,** API Management's `rate-limit-by-key` policy ([Chapter 29](#api-management)) does this at the gateway and is often enough; build your own when the limit depends on state the gateway can't see, such as the tenant's plan or the cost of the operation.

**What you'd monitor.** `429`s per key and tier (a sudden rise for one partner usually means a bug in their retry loop); the p99 of the script call; Redis CPU, which is one core because Redis executes commands on a single thread ([Chapter 18](#redis-in-practice-key-design-data-types-and-eviction)); and fail-open events, which should be zero on a normal day.

**How it fails.** A Redis outage drops every limit to its policy. Using the client's clock shows up as limits that drift per instance. A bad policy change can lock out the largest customer, so roll policies out like code: staged, observed, reversible.

## Worked Design 3: A Notification Service

Other services want to tell a user something: "your order shipped", "your password was reset", "this week's offers". The notification service decides whether, where and when, and delivers through email, SMS, push and an in-app inbox, each via a provider you don't control. [Chapter 15](#notifications-push-sms-and-webhooks) covers the integrations; this design is the system around them.

**Requirements and targets.** Accept a request (recipient, type, data); apply preferences per channel and type, and quiet hours in the user's time zone; render templates; deliver per channel; record status, including provider callbacks. Non-functional: **no user sees the same notification twice** (except security codes, where a duplicate beats a missing one); transactional messages never wait behind marketing; one provider's outage doesn't stop other channels; senders get `202` at once.

**Estimates (illustrative).** 5 million transactional notifications a day is about 50 a second. A campaign to 10 million users within an hour is 10 × 10⁶ ÷ 3,600 ≈ **2,800 a second**, over fifty times the average, all at once. Your servers can absorb that; the providers may not, because they enforce per-account sending rates. **The bottleneck is someone else's limit**, so look up your accounts' limits before promising a campaign window. Storage: 20 million delivery rows a day at about 500 bytes is about 10 GB a day, kept for perhaps 90 days.

**API.**

```
POST /notifications
     Idempotency-Key: 8c1f…            (the caller's event id)
     { "userId": 42, "type": "order-shipped", "data": { "orderId": "…" }, "priority": "transactional" }
     -> 202 { "id": "…", "status": "accepted" }
GET  /notifications/{id}               -> per-channel status: queued | sent | delivered | failed | suppressed
POST /providers/{name}/callbacks       <- delivery receipts and bounces (signature-verified)
```

Many producers instead publish a domain event (`OrderShipped`) that the service subscribes to; the idempotency rules below cover both entry points.

**Data model.** A `notifications` row per request (deduplicated on the idempotency key), a `deliveries` row per channel and recipient carrying the send state, and small, cacheable preferences and templates.

**High-level design.**

```
producers ──► POST /notifications ─┐
domain events (topic) ─────────────┴─► intake ──► fan-out worker
                                                   │ preferences, quiet hours, render
                                                   ▼
                     ┌───────────────────────┬───────────────────────┬───────────────────────┐
                     ▼                       ▼                       ▼                       ▼
             email (high | bulk)     sms (high | bulk)       push (high | bulk)      in-app inbox (DB)
                     │                       │                       │
               email worker            sms worker              push worker   ──► providers (rate-limited,
                     │                       │                       │             circuit breaker each)
                     └──────── provider callbacks ──► status store ◄─┘
```

### Fan-out, isolation and priority

The fan-out worker turns one request into one delivery per channel, each on **that channel's own queue**. The separation is the design: with one shared queue, failing SMS messages retry at the head while emails wait behind them, so one provider's outage stops every channel. With a queue per channel, the SMS queue backs up and email carries on: the bulkhead pattern at queue level.

Priority needs the same treatment, for the same reason: a queue is FIFO. A campaign that enqueues 10 million messages at 09:00 puts a password reset requested at 09:01 behind all of them, and a "priority" field on the message changes nothing. Separate queues (or subscriptions) for transactional and bulk traffic do, with transactional consumers holding a reserved share of each provider's rate.

On Azure, a Service Bus topic with a subscription per channel and priority, filtered on message properties, gives this shape ([Chapter 29](#service-bus)); workers are `BackgroundService`s or Functions that scale on queue length.

### Retries and idempotency at a boundary you don't own

Delivery is at-least-once all the way through: the intake may see the same event twice, the queue redelivers after a lock expires, and the worker crashes after the provider accepted the message. The defences come in layers.

**Deduplicate at intake** on a key derived from the source: the caller's idempotency key, or the event ID plus the recipient plus the channel. The database decides, as in [Chapter 11](#idempotent-consumers):

```sql
CREATE TABLE deliveries (
    dedupe_key   text        PRIMARY KEY,   -- source event id + recipient + channel
    user_id      bigint      NOT NULL,
    channel      text        NOT NULL,      -- email | sms | push | inapp
    status       text        NOT NULL DEFAULT 'pending',  -- pending | sending | sent | failed
    attempts     integer     NOT NULL DEFAULT 0,
    locked_until timestamptz,
    provider_id  text
);

-- The claim: only the first copy of an event gets a row back.
INSERT INTO deliveries (dedupe_key, user_id, channel)
VALUES ('evt-81f2:user-42:email', 42, 'email')
ON CONFLICT (dedupe_key) DO NOTHING
RETURNING dedupe_key;
```

A second copy of the same event returns no row, and the worker drops it.

**Claim with a lease before sending.** A worker moves the delivery to `sending` only if no other worker holds a live lease, and a crashed worker's lease expires, so the row can be reclaimed:

```sql
UPDATE deliveries
SET    status = 'sending', attempts = attempts + 1,
       locked_until = now() + interval '2 minutes'
WHERE  dedupe_key = 'evt-81f2:user-42:email'
  AND  (status = 'pending' OR (status = 'sending' AND locked_until < now()))
RETURNING attempts;
```

**Forward an idempotency key to the provider when it accepts one**, so a resend after a timeout is deduplicated on their side, as with the payment provider in [Chapter 20](#idempotency-keys-making-post-retry-safe). Without one, a window stays open: the provider accepts, the worker dies before recording `sent`, the lease expires, and the message goes out twice. Choose per channel which way to err: resend a one-time security code; mark a marketing SMS `unknown` after a timeout and don't retry it.

**Classify failures before retrying.** A provider `429` or `5xx` is transient: back off with jitter and honour `Retry-After`. A malformed number, a hard bounce or a revoked push token is permanent: mark it failed, **suppress** the address, never retry; retrying those is how senders lose their email reputation. What keeps failing goes to a dead-letter queue someone watches ([Chapter 11](#dead-letter-queues-dlq)).

### Preferences, quiet hours and time

Evaluate preferences at fan-out time, so a user who opts out at 09:00 doesn't get the campaign queued at 08:00 and sent at 09:30. Quiet hours need the user's **IANA time zone** and a conversion at send time, because "not between 22:00 and 08:00" moves with daylight saving ([Chapter 15](#store-and-compute-in-utc-always)). A delivery inside quiet hours is scheduled, not dropped: Service Bus scheduled messages (`ScheduledEnqueueTime`) need no polling job. "At most three pushes an hour" is Design 2's rate limiter with a different key.

**What you'd monitor.** **Age of the oldest message** per channel and priority: length says how much work waits, age says whether users wait. Also provider errors by code, end-to-end delivery latency as a percentile, the dead-letter count, email bounce and complaint rates, and the share suppressed.

**How it fails.** *A provider is down:* its circuit breaker opens, its queue grows, other channels are unaffected; failing over to a second provider reopens the duplicate window, so do it per channel, deliberately. *A campaign floods the system:* bulk queues drain at the providers' pace while transactional queues keep their share. *A template breaks:* its messages are dead-lettered with a readable reason; fix and resubmit, and intake deduplication stops repeats.

## Worked Design 4: Checkout with Payment

Turn a cart into a paid order: reserve stock, take payment through a provider, confirm, trigger fulfilment and the receipt. Here correctness is not negotiable: every step crosses a boundary that can fail ambiguously, and the charge can't be undone by a rollback. [Chapter 20](#sagas-long-running-distributed-transactions) teaches the patterns; this design puts them together.

**Requirements and targets.** Functional: order from a cart at a final price; reserve stock; authorize and capture payment; confirm or cancel; notify. Non-functional, in priority order: **never charge twice for one order**; **never sell stock that doesn't exist** (or decide explicitly how much overselling is acceptable); survive a crash at any step without manual repair; answer within a couple of seconds, with "pending" if payment takes longer.

**Estimates (illustrative).** 50,000 orders a day is under one a second, which any database handles. The number that matters is the flash sale: 100 times the average for ten minutes is about 60 orders a second, **all for the same ten products**. The problem is not throughput but **contention** on the same few rows.

**API.**

```
POST /checkout
     Idempotency-Key: 5f3b8a1e-…        (generated once per checkout attempt, reused on every retry)
     { "cartId": "…", "paymentMethodId": "…" }
     -> 201 { "orderId": "…", "status": "confirmed" }
     -> 202 { "orderId": "…", "status": "pending" }  Location: /orders/{id}
GET  /orders/{id}                       -> pending | confirmed | cancelled (with reason)
POST /payments/webhook                  <- provider events: authorized, captured, failed (signature-verified)
```

The `202` branch is the async request-reply contract from [Chapter 11](#async-request-reply-202-a-status-resource-and-retry-after): when the payment provider takes longer than the request can wait, the client polls the order instead of holding a connection open.

**Data model.** Money is stored in minor units with its currency ([Chapter 15](#store-money-as-minor-units-and-always-with-its-currency)). The constraints do most of the correctness work:

```sql
CREATE TABLE orders (
    id              uuid        PRIMARY KEY,
    customer_id     bigint      NOT NULL,
    idempotency_key text        NOT NULL,
    status          text        NOT NULL,   -- pending | confirmed | cancelled
    total_minor     bigint      NOT NULL,   -- money in minor units, with its currency
    currency        char(3)     NOT NULL,
    created_at      timestamptz NOT NULL DEFAULT now(),
    UNIQUE (customer_id, idempotency_key)
);

CREATE TABLE payments (
    id            uuid        PRIMARY KEY,  -- also sent to the provider as its idempotency key
    order_id      uuid        NOT NULL REFERENCES orders (id),
    status        text        NOT NULL,     -- requested | authorized | captured | failed | unknown
    provider_ref  text        UNIQUE,
    amount_minor  bigint      NOT NULL,
    updated_at    timestamptz NOT NULL DEFAULT now()
);

-- At most one payment per order that is live, or might be: a second charge can't even be recorded.
CREATE UNIQUE INDEX one_live_payment_per_order ON payments (order_id)
    WHERE status IN ('requested', 'authorized', 'captured', 'unknown');
```

Each constraint answers one failure. The unique `(customer_id, idempotency_key)` turns a retried checkout into a replay. The partial unique index means a second payment can't be recorded while the first is live or unknown, so a path that tries to charge again fails on insert instead of at the card; a failed payment doesn't count, so the customer can retry with another card. Beside them: `stock (sku, available)` with `CHECK (available >= 0)` as the last defence against overselling, `reservations` keyed by `(order_id, sku)` with an expiry, and an outbox table ([Chapter 11](#the-transactional-outbox)).

**High-level design.**

```
client ──► POST /checkout (idempotency key)
             │  one transaction: order (pending) + reservations + stock decrement + outbox row
             ▼
        Orders DB ──► outbox relay ──► bus ──► payment worker ──► payment provider
                                                   │                  │
                                                   │   webhook ◄──────┘ (authorized / failed)
                                                   ▼
                                    order: confirmed ──► outbox ──► OrderConfirmed
                                       │ or cancelled + stock released     │
                                       ▼                                   ▼
                              reservation expiry job              fulfilment, notifications (Design 3)
```

### Idempotency, end to end

Three keys protect three boundaries, and each one closes a different double:

1. **Client → checkout.** The client generates the `Idempotency-Key` once per checkout attempt and reuses it on every retry. The unique constraint on the order makes a retry find the existing order and return its current state. [Chapter 20](#idempotency-keys-making-post-retry-safe) walks through the claim-before-effect ordering and the `409` for a request still in flight.
2. **Payment worker → provider.** The `payments.id` goes to the provider as *its* idempotency key. The worker can be redelivered the same `ChargePayment` command (at-least-once, [Chapter 11](#idempotent-consumers)), and the provider deduplicates the charge.
3. **Provider → webhook.** Providers retry webhooks, so the handler deduplicates on the provider's event ID, verifies the signature against the raw body ([Chapter 15](#notifications-push-sms-and-webhooks)), and applies only status transitions that move forward: a late `authorized` must not overwrite `captured`.

### The ambiguous payment

The provider call times out. Did the charge happen? You don't know, and **both easy answers are wrong**. Marking it failed and releasing the stock gives the classic incident: charged, order cancelled, support ticket in the morning. Retrying with a *new* payment record charges twice. The correct answer is a state for "we don't know yet":

- Record the payment as `unknown`. The partial unique index keeps it live, so no second charge can start.
- Resolve it from the provider's side: query by your idempotency key, or wait for the webhook, which arrives either way. Show "payment processing", not an error.
- Run a scheduled **reconciliation** against the provider's report: the backstop for every mismatch the online path misses.

Card payments are usually two steps: **authorize** (reserve the amount) and **capture** (take it). Authorizing at checkout and capturing at shipment makes a cancellation before shipping a void, not a refund.

### Stock under contention

The reservation is one SQL statement that decrements only if enough stock remains and records the reservation in the same statement:

```sql
WITH taken AS (
    UPDATE stock SET available = available - 2
    WHERE  sku = 'TSHIRT-M' AND available >= 2
    RETURNING sku
)
INSERT INTO reservations (order_id, sku, quantity, expires_at)
SELECT '6f1c2a0e-0000-4000-8000-000000000001', sku, 2, now() + interval '15 minutes'
FROM   taken;
-- INSERT 0 1: reserved.   INSERT 0 0: not enough stock, nothing changed.
```

There is no read-modify-write window: the check and the decrement are one row operation, and concurrent buyers queue on the row lock; in PostgreSQL a waiting update re-checks `available >= 2` against the row as the previous buyer left it. Against PostgreSQL 17, 30 concurrent single-unit reservations on 10 units produced exactly 10 reservations and stock 0. A retry for the same order hits the reservation's primary key, and the error rolls back the whole statement, decrement included ([Chapter 7](#concurrency-optimistic-vs-pessimistic) compares this with concurrency tokens and locks).

> **Pay attention.** **Why `ON CONFLICT DO NOTHING` here oversells.** Adding it to the insert looks like making the reservation idempotent. But the `UPDATE` in the CTE runs whether or not the insert keeps its row, so a retry decrements the stock a second time and records nothing. Verified against PostgreSQL 17: stock 3, a retried reservation of 2 for an order that already had one, and stock dropped to 1 with no new reservation. Idempotency here comes from the *error*, which rolls back the statement, or from checking for the reservation inside the same transaction first. Swallowing a conflict is only safe when everything the statement did depends on the row being kept.

Correct, but the flash sale still serializes every buyer of one product on one row. When that row is the bottleneck, there are three ways out, each with a cost:

- **Split the counter** into N rows per product, each with a share of the units; a buyer picks one at random and moves on if it is empty. Contention drops N-fold; "sold out" gets fuzzier near the end.
- **Admit through a queue**: a waiting room lets buyers in at the rate checkout can process. Fair and calm, but a place in the queue doesn't guarantee a product.
- **Pre-check in Redis**: decrement a counter there first, then confirm in the database, which still decides; a reconciliation step repairs the counter when the two disagree.

Reservations expire (15 minutes here, illustrative) so abandoned checkouts return their stock; the job that releases them and cancels the order is one of the saga's compensations.

### The saga and its compensations

The flow spans the order database, the payment provider and fulfilment, so there is no single transaction. It is a saga ([Chapter 20](#sagas-long-running-distributed-transactions)), usually **orchestrated** because it has branches and timeouts: a MassTransit state machine, or Durable Functions on Azure.

| Step | Compensation | Can it be undone? |
|---|---|---|
| Create order + reserve stock | Release reservation, cancel order | Yes, locally |
| Authorize payment | Void the authorization | Yes, until capture |
| Capture payment | Refund | Yes, but visible to the customer and possibly charged a fee |
| Send confirmation email | None | **No** |
| Ship | Return process | Not in any practical sense |

Order the steps so that everything that can still fail comes **before** anything that can't be undone. The confirmation email waits for the payment, and capture waits until the order ships. Every message between the steps goes through the outbox, so a crash between "order saved" and "payment requested" can't lose the request.

**What you'd monitor.** **Orders stuck in `pending`** and payments in `unknown`, by age; payment success rate by provider response code; the oldest unpublished outbox row; reservation expiries per hour (a spike means checkout fails after reserving); and one invariant that must stay zero: orders with more than one captured payment, found by reconciliation.

**How it fails.** *The payment provider is down:* the circuit breaker opens, and you either stop selling or accept orders as `pending` and charge on recovery, decided in advance. *The broker is down:* the outbox fills and nothing is lost; orders wait in `pending`. *The order database is down:* checkout is down, deliberately: the **CP choice** ([Chapter 20](#cap-and-pacelc-the-physics-of-distributed-state)), because for money and stock refusing beats answering wrong.

## Practice

**Design one of these yourself (60–90 min)**, written down in the shape of the worked designs.

- **A. Live score updates.** Push score changes for live matches to up to 2 million concurrently connected web and mobile clients within 2 seconds of the event. (Starting points: [Chapter 22](#real-time-websockets-vs-sse-vs-long-polling) on WebSockets, SSE and SignalR, and Little's law for the connection count.)
- **B. Reminders.** Users schedule reminders, one-off or recurring, in their own time zone. Each must fire within a minute of its time, exactly once, through the notification service from Design 3.

**Grade it with this checklist.** Grade it a day later, or have someone else do it:

- [ ] Functional requirements listed; non-functional ones as numbers.
- [ ] Each estimate shows its assumption and arithmetic, and changes something in the design.
- [ ] The API names its idempotent operations and what returns `202`.
- [ ] Keys and partition keys follow from the access patterns.
- [ ] Every box in the diagram exists because of a requirement or a number.
- [ ] At least one hard part names a rejected alternative and why.
- [ ] Every cross-service step states its delivery guarantee.
- [ ] The "what breaks first at ×10" answer names a specific resource and the number that runs out.
- [ ] Each dependency has a failure line: what the user sees when it is down.
- [ ] At least three signals to monitor, one of them measuring waiting (age, lag).

**Evidence to keep**, in your own public portfolio repo, not in this one: the design write-up and your graded checklist. Designs of a real employer's system stay private; publish a version about an invented system.

## Three questions

**1.** Your estimate says 3,000 requests per second on average. A teammate proposes sizing the fleet for exactly that. What is missing from the sizing, and how do you put a number on each missing part?

<details>
<summary>Answer</summary>

- **The peak.** The average hides the daily cycle and event spikes. Take the peak factor from your own traffic (busiest minute over average) or state one, and size for the peak.
- **Losing a zone.** Three zones that must survive losing one need two zones to carry the peak: 1.5 times the instances.
- **Slowdowns and retries.** When a dependency slows, Little's law raises in-flight requests with latency while retries add arrivals. Bound both with timeouts and limits instead of buying for them, and keep headroom for deploys.
- **Per-instance capacity comes from a load test** at the latency target. Fleet size is peak ÷ that capacity × the zone factor, plus a margin you can explain.
</details>

**2.** A celebrity shares one of your short links. Redis cluster CPU is at 100% on one shard and under 10% on the others, and redirect latency climbs. A colleague proposes adding shards. Why won't that help, and what will?

<details>
<summary>Answer</summary>

- **One key lives on one shard.** Redis Cluster maps each key to a hash slot owned by one shard. More shards spread more *keys*; one key's load stays on one shard.
- **Serve it before Redis.** An in-process L1 cache with a TTL of seconds turns thousands of requests a second into at most one Redis call per instance per TTL; `HybridCache` adds stampede protection.
- **Or before you.** A short `Cache-Control: max-age` on the 302 lets the CDN answer most redirects.
- **For keys that must stay in Redis**, store copies under several key names on different slots and read one at random: dearer writes, spread reads.
</details>

**3.** The payment provider call timed out after 30 seconds. The code marked the payment failed, cancelled the order and released the stock. The next morning the customer writes in: charged, no order. Walk through what went wrong and redesign that path.

<details>
<summary>Answer</summary>

- **A timeout is not a failure.** It means "unknown": the request may never have arrived, may have succeeded with the response lost, or may still be running. Treating unknown as failed is the bug.
- **Redesign.** Record the payment as `unknown` (live under the one-live-payment index, so nothing can charge again) and keep the reservation. Resolve it by querying the provider with the idempotency key you sent, or from the webhook. Show "payment processing".
- **Close the loop.** Reconciliation against the provider's report alerts on any captured payment without a confirmed order; with authorize-then-capture, an order you do cancel needs a void, not a refund.
</details>

## Decide

The notification service from Design 3 needs a store for deliveries. The working assumptions (illustrative): about 20 million delivery rows a day; each row is written once and updated two or three times as its status changes; the in-app inbox reads the latest 50 rows for one user; rows are deleted after 90 days; the support team occasionally searches by recipient. Three proposals:

1. **Azure SQL Database** (or PostgreSQL): one table, partitioned by day, with an index on `(user_id, created_at)`.
2. **Cosmos DB**, partitioned by `/userId`, with a 90-day TTL on the container.
3. **Table Storage**, partitioned by user, with a nightly job that deletes old rows.

<details>
<summary>How a senior engineer weighs it</summary>

**What each costs.**

- *Relational:* familiar, the unique `dedupe_key` is enforced for free, and support queries are easy. About 230 inserts a second on average plus two or three times as many updates fits one well-provisioned database, though campaign peaks run far higher. Retention is cheap only if old day-partitions are dropped; a daily `DELETE` of 20 million rows is a load problem of its own. Scaling writes past one primary means sharding.
- *Cosmos DB:* "this user's latest deliveries" is a single-partition query, writes scale out across partitions, and TTL removes expired items with no job to write ([Chapter 29](#indexing-change-feed-ttl)). `id` is unique only **within a logical partition**, which is enough when the partition key is the user. Costs: every write and update is billed in RUs, so price the update rate first; a user receiving a flood is a hot partition; cross-user support queries fan out across partitions.
- *Table Storage:* the cheapest per operation, with key access only: no secondary indexes, no TTL, and searching by anything but the key is a scan.

**What decides it here:** access is by user, campaign write volume is the risk, retention by age is required, and uniqueness is needed only per user.

**The choice.** Cosmos DB on `/userId` with TTL, if pricing the real update rate confirms the RU cost, with support search fed from the change feed into a separate index.

**What would change it.** A tenth of the volume, or a team without Cosmos DB experience: the relational option with daily partitions is simpler and fast enough. Cross-user reporting at scale belongs in an analytics store either way.
</details>

## Interview Questions

Interviewers rarely want the diagram. They probe how you get from a vague prompt to a defensible decision, and what you do when they change the numbers. Use the seven steps for every prompt and think out loud.

**"Where would you start?"**
With questions, not boxes: users, functions, and non-functional targets as numbers. Then a quick estimate, because the numbers pick the architecture.

**Red flag:** drawing a microservice diagram before asking how many users there are.

**"Where does that number come from?"**
Show the chain (users × actions ÷ 86,400 × peak factor) with its assumptions, and say which decision the number drives: "at 40 writes a second one primary is plenty, so no sharding".

**"Traffic goes up ten times. What breaks first?"**
Walk the request path and find the resource with the least headroom. Usually the database: its connections (Little's law: in-flight = rate × time held) or its write primary. Then hot keys, then single-threaded shared components, then third-party rate limits. Say what you would measure to find out, and the cheapest fix for each, in order.

**Red flag:** "we'd just add more servers" — the app tier is rarely what runs out.

**"And a hundred times?"**
Now the shape changes: partitioning by tenant, user or key, asynchronous writes behind queues, more cache layers, perhaps several regions. Name each one's cost: cross-partition queries, eventual consistency, operational load.

**"What happens when this dependency is down?"**
For each box: what the user sees, whether data is lost, how it recovers. Separate read availability from write availability, and name the deliberate choice: fail open or closed, serve stale or refuse.

**"How do you know it's working?"**
SLIs tied to the requirements (p99 latency, error rate, freshness), plus signals that measure *waiting*: queue age, replication lag, the oldest outbox row ([Chapter 25](#red-and-use-two-methods-for-choosing-what-to-measure)).

**"Why not just use Kafka, Cosmos DB or microservices?"**
Every component earns its place through a requirement or a number. "Not yet, and here is the number at which I'd add it" is the senior answer.

**Red flag:** choosing a technology because it is what large companies use.

**"Design a URL shortener / a rate limiter / notifications / checkout."**
The four worked designs above are the answers, and each one's *hard parts* are what the follow-up questions go after: enumerable codes and hot keys; per-process limiters and atomic counters; duplicates at a provider you don't control; the `unknown` payment and the step that can't be undone.

**Red flag:** "wrap checkout in a distributed transaction" — the payment provider is not part of your transaction, and avoiding two-phase commit across services is why sagas exist.

## Sources & Further Reading

- **Alex Xu, *System Design Interview – An Insider's Guide*** (Volumes 1 and 2) — the step-by-step interview structure and many worked prompts.
- **Martin Kleppmann, *Designing Data-Intensive Applications*** — the depth behind replication, partitioning, consistency and stream processing.
- **Colin Scott, "Numbers Every Programmer Should Know By Year"** (github.com/colin-scott/interactive_latencies) — the latency constants used above, crediting Peter Norvig's *Teach Yourself Programming in Ten Years* and Jeff Dean's talks.
- **Jeffrey Dean and Luiz André Barroso, "The Tail at Scale"** (*Communications of the ACM*, 2013) — fan-out, tail latency and hedged requests.
- **John D. C. Little, "A Proof for the Queuing Formula: L = λW"** (*Operations Research*, 1961).
- **ASP.NET Core documentation, "Rate limiting middleware"** (the `dotnet/AspNetCore.Docs` repository) and the `RateLimiterOptions` source in `dotnet/aspnetcore`.
- **RFC 9110, *HTTP Semantics*** — status codes, including which redirects are heuristically cacheable.
- **Redis documentation** on scripting with Lua (`EVAL`) and the `TIME` command.
