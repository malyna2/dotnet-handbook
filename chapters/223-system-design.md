# Chapter 23: System Design

Zoom out from a single function to an entire service serving millions of users. System design has no single right answer — it's about making and *justifying* trade-offs. Both in interviews and in real architecture reviews, a repeatable process keeps you from flailing.

This chapter gives you that process and the small set of building blocks almost every design is assembled from, then runs two worked examples through it: a URL shortener (a read-heavy key-value service) and a rate limiter (shared state across many instances). It is deliberately short, because each building block is taught in depth elsewhere in the book — caching and replication in [Chapter 18: Data in Depth](#chapter-18-data-in-depth), queues in [Chapter 11: Messaging and Background Work](#chapter-11-messaging-and-background-work), load balancers and CDNs in [Chapter 26: Delivery and Platform](#chapter-26-delivery-and-platform), consistency and sagas in [Chapter 20: Distributed Systems](#chapter-20-distributed-systems). What this chapter adds is the thread that ties them together: **numbers first, then the architecture those numbers justify.**

## A Repeatable Approach

1. **Clarify requirements.** Never design against a vague prompt. Separate *functional* requirements (what it does) from *non-functional* ones (how well: latency, availability, consistency, durability). Ask: how many users? Read-heavy or write-heavy? Is stale data acceptable?

2. **Estimate scale (back-of-the-envelope).** Turn "millions of users" into numbers. Daily active users, requests per second, storage per year, bandwidth. These numbers decide your architecture — a system doing 10 requests/second and one doing 100,000 are fundamentally different machines.

3. **Define the APIs.** A few endpoint signatures nail down the contract and surface hidden requirements. `POST /shorten`, `GET /{code}`.

4. **Design the data model.** What entities, what access patterns, SQL or NoSQL? Access patterns should *drive* the schema, not the other way around.

5. **Sketch high-level components.** Boxes and arrows: clients, load balancer, app servers, caches, databases, queues. Show the request flow.

6. **Identify and address bottlenecks.** Where does it break under load? Single database? Add read replicas and a cache. Hot path? Add a CDN. Traffic spikes? Add a queue to absorb bursts.

## The Building Blocks

Most designs are a handful of components arranged around the request flow. Know what each one buys and what it costs:

- **Load balancer** — spreads incoming requests across many identical app servers, enabling *horizontal scaling* and removing single points of failure. The traffic cop of your system ([Chapter 26](#load-balancers-reverse-proxies-api-gateways-and-cdns) covers L4 vs L7 and reverse proxies).
- **Cache** (e.g., Redis) — an in-memory store for hot data, turning slow database reads into microsecond lookups. The 80/20 rule applies: a small cache of the most-requested data absorbs most of the load. Watch for cache invalidation and staleness ([Chapter 7](#chapter-7-data-access) teaches the basics, [Chapter 18](#chapter-18-data-in-depth) the depth).
- **CDN** — geographically distributed edge servers that serve static assets (images, JS, video) close to users, cutting latency and offloading your origin.
- **Database with replicas** — a primary handles writes; read replicas handle reads. Since most systems are read-heavy, this scales reads dramatically. The cost is *replication lag* — replicas are slightly behind (eventual consistency).
- **Message queue** (e.g., RabbitMQ, Kafka) — decouples producers from consumers. The web request drops a job on the queue and returns instantly; workers process asynchronously. Absorbs traffic spikes and smooths load. This is the background-work pattern of [Chapter 11](#chapter-11-messaging-and-background-work), applied at architecture scale.
- **Object storage** (e.g., S3, Azure Blob) — cheap, durable, effectively infinite storage for large blobs. Don't put user-uploaded videos in your relational database; put a URL there and the bytes in object storage.

## Scaling Patterns

When step 6 finds a bottleneck, the fix is almost always one of five moves:

- **Vertical scaling** — a bigger machine. Simple, but has a hard ceiling and a single point of failure.
- **Horizontal scaling** — more machines behind a load balancer. Nearly unlimited, but requires your app servers to be *stateless* (no session data stored locally) so any server can handle any request — the property [the 12-factor app](#the-12-factor-app) is built around.
- **Caching** — the highest-leverage move for read-heavy systems.
- **Database scaling** — replicas for read throughput; *sharding* (partitioning data across databases by some key) for write throughput when one database can't hold the load. [Chapter 18](#chapter-18-data-in-depth) covers both, including replication lag and resharding.
- **Asynchronous processing** — push slow work (emails, image processing, analytics) off the request path onto queues and workers.

## Worked Example: A URL Shortener

Let's tie it together. Design a service that turns long URLs into short codes (like `bit.ly`).

**1. Requirements.** Functional: create a short code for a URL; redirect a short code to the original. Non-functional: very read-heavy (redirects vastly outnumber creations), low-latency redirects, high availability (a down redirect breaks every published link).

**2. Scale estimate.** Say 100 million new URLs per month — roughly 40 writes/second. If reads are 100× writes, that's ~4,000 reads/second. Storage: 100M/month × 12 × several years × ~500 bytes ≈ low terabytes. Modest writes, heavy reads — this shape *screams* "cache the reads."

**3. API.**
```
POST /shorten   { "url": "https://very/long/url" }  ->  { "code": "aZ3x9" }
GET  /{code}                                         ->  301 redirect to original
```

**4. Data model.** A single mapping: `code -> longUrl` (plus metadata like created-at, owner). The only access patterns are "look up by code" and "insert." This is a perfect key-value workload — a NoSQL store or a well-indexed SQL table both work.

**5. Generating the code.** Take an auto-incrementing ID and **Base62-encode** it (`0-9`, `a-z`, `A-Z`). Base62 packs ~62³ ≈ 238,000 codes into 3 characters and ~62⁷ ≈ 3.5 trillion into 7 — plenty, and short. This is exactly the "choose the encoding for the constraint" thinking from [Big-O](#big-o-the-language-of-how-bad-does-this-get) made concrete: a base conversion.

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

(Fill the buffer from the end and slice — building most-significant-digit-first with `Insert(0, …)` in a loop would be exactly the accidental O(n²) that [Chapter 2](#chapter-2-data-structures-and-algorithms-essentials) warns about.)

**6. Components and flow.**
- A **load balancer** fronts several stateless app servers.
- On `POST /shorten`: get the next ID, Base62-encode it, store `code -> url`, return the code.
- On `GET /{code}`: **check the cache first** (Redis). On a hit — the overwhelming common case — redirect immediately. On a miss, read the database, populate the cache, then redirect. This is the hash-lookup pattern of [Chapter 2](#core-data-structures-and-their-net-types), scaled to a distributed system: the cache *is* a giant dictionary.
- The database uses **read replicas** so redirect reads that miss the cache still scale.

**7. Bottlenecks.**
- *Redirect latency* — solved by the cache; hot links live in memory.
- *Single database for writes* — 40 writes/second is trivial for one primary, so no sharding needed yet. (Knowing *not* to over-engineer is as senior as knowing how to shard.)
- *Availability* — multiple app servers plus replicas remove single points of failure; object storage or a CDN isn't needed since there are no large assets.

Notice how the estimate in step 2 justified every later decision. That traceability — from numbers to architecture — is what a good design review looks for.

## Worked Example: A Rate Limiter

Rate limiting protects a service from abuse and overload — "at most N requests per user per minute." A clean, common algorithm is the **token bucket**: each user has a bucket that refills at a steady rate; each request spends a token; an empty bucket means rejection. It uses the same primitives as the shortener — a per-user counter in a fast store like Redis, checked and updated on each request. At scale you'd run this in a shared cache so all app servers agree on the count, illustrating again how a humble data structure (a counter map) becomes a distributed system component.

Run it through the same six steps and the interesting decisions surface on their own: the *requirement* "work across many app instances" is what forces the shared store; the *bottleneck* is that store becoming hot; and the *trade-off* nobody mentions until asked is whether to fail open or fail closed when the store is down. The interview answer below walks the steps. [Chapter 26](#chapter-26-delivery-and-platform) covers rate limiting as it is actually deployed — at the edge, with ASP.NET Core's limiter, and against clients that are trying to get around it.

## Interview Questions

Use one structure for every design prompt: **Requirements → Scale estimate → API → Data model → Components → Bottlenecks & trade-offs.** Talk through it out loud; the interviewer wants your reasoning, not a memorized diagram.

**Design a URL shortener.**
- *Requirements:* shorten a long URL to a short code, redirect on visit; optional analytics and expiry. Reads ≫ writes.
- *Scale:* assume read-heavy (100:1). Short code needs enough space — base62 of 7 chars ≈ 3.5 trillion.
- *API:* `POST /shorten {url}` → short code; `GET /{code}` → 301/302 redirect.
- *Data:* key-value `code → longUrl` (+ owner, createdAt, expiry). A KV store or indexed table.
- *Components:* code generation (counter+base62, or hash with collision check), a write path, and a heavily cached read path (redirects served from cache/CDN).
- *Bottlenecks:* the redirect read path — cache aggressively; code-generation uniqueness — use a distributed counter or check-and-retry. 301 vs 302 affects caching and analytics.

**Design a rate limiter.**
- *Requirements:* cap requests per client (per API key/IP) per window; reject or throttle over-limit; work across many app instances.
- *Algorithm:* token bucket (allows bursts, refills at a steady rate) or sliding window (smoother, more accurate). Name the trade-off.
- *Data:* per-client counter/tokens in a fast shared store (Redis) so all instances agree; atomic increment/Lua script to avoid races.
- *Components:* middleware that checks-and-decrements before handling; return `429 Too Many Requests` with `Retry-After`.
- *Bottlenecks:* the shared store becomes hot — mitigate with local pre-checks or sharded counters; clock skew for windows; fail-open vs fail-closed when the store is down.

**Design the checkout for an online shop.**
- *Requirements:* create an order from a cart, reserve inventory, take payment, confirm — reliably and idempotently.
- *Scale:* spiky (sales/launches); correctness on money and stock is non-negotiable.
- *API:* `POST /checkout` with an **idempotency key** (retries must not double-charge — [Chapter 20](#chapter-20-distributed-systems)); returns order status.
- *Data:* orders, order items, inventory, payments — with a rowversion for [optimistic concurrency](#concurrency-optimistic-vs-pessimistic) on stock.
- *Components:* validate cart & price → reserve inventory (optimistic concurrency or reservation) → charge via payment gateway → confirm order. Use a **saga** with compensations (release inventory if payment fails) and a **transactional outbox** to emit "order placed" events reliably ([Chapter 20](#chapter-20-distributed-systems) teaches both).
- *Bottlenecks:* inventory contention on hot items (optimistic retry, queueing), payment gateway latency/failure (timeouts, idempotent retries, circuit breaker), and exactly-once semantics (idempotency keys end-to-end).
