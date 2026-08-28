# Chapter 33: Real-World Scenarios & Architectural Decisions

_⏱️ Estimated read time: ~65 min ·    11463 words (study pace)_

Every senior engineer eventually learns that the hard part of the job is not writing code — it is deciding what to do when the code you already shipped meets reality. Reality shows up as a traffic spike you did not plan for, a "successful" request that silently lost data, a p99 latency graph that looks like a seismograph, and a dependency that vanishes at the worst possible moment. This chapter is a war-room playbook. Each scenario is a story you could plausibly live through on a production on-call rotation, framed around one question: *how do you react, and what architectural decision does that push you toward?*

Treat this as a reference you can open under pressure and as an interview crib sheet. The earlier chapters gave you the building blocks — scaling and cloud (Ch. 10), containers and Kubernetes (Ch. 11), data at scale (Ch. 23), messaging and distributed patterns (Ch. 9), distributed theory and SRE (Ch. 21), the runtime and GC (Ch. 2), and performance (Ch. 15). Here we do not re-teach those; we put them to work under fire and reason about the trade-offs. Each of the twelve scenarios below follows the same shape: the situation, how you notice it, how to stop the bleeding, the root causes, the durable fix and its trade-offs, and how to talk about it in an interview.

## The incident cheat-card

This is the page to open at 3 a.m. — one row per scenario, each row expanded in full in the scenario it points to.

| Symptom | Scarcest resource right now | First three actions |
|---|---|---|
| p95/p99 climbs, then errors; DB CPU pinned; connection pool exhausted; health checks flap (Scenario 1) | The primary database | 1. Scale out the stateless tier. 2. Feature-flag off non-critical load. 3. Serve from cache/CDN and rate-limit at the edge — fast 429s, not slow failures. |
| "It said it worked" tickets; DB and broker disagree; downstream saw events with no upstream record (Scenario 2) | An accurate list of affected records | 1. Reconcile the two stores to enumerate the gap. 2. Recover from the durable source (payment records, events). 3. Disable the fire-and-forget path. |
| Periodic p99 spikes with a flat p50; % Time in GC high; Gen 2 count and LOH climbing (Scenario 3) | Heap headroom | 1. Confirm it's really GC with `dotnet-counters`. 2. Switch to Server GC with background collection. 3. Raise a too-tight container memory limit. |
| Publishes hang; thread-pool starvation spreads to unrelated endpoints; retries storm the dead broker (Scenario 4) | Request threads | 1. Trip the circuit breaker — fail fast, stop blocking. 2. Buffer locally via the outbox; keep accepting orders. 3. Back off with jitter to kill the retry storm. |
| Primary unreachable or corrupt; replicas faithfully mirrored the damage (Scenario 5) | The last restorable backup | 1. Stop writes — fence the primary. 2. Pick the recovery target and locate the backup chain. 3. Restore to a *new* instance; state the RPO gap to stakeholders now. |
| A field rename in another language's service silently breaks deserialization in production (Scenario 6) | A written contract per boundary | 1. Map every cross-language boundary: who calls whom, sync or async, payload. 2. Flag shared-database couplings as debt. 3. Standardize one integration style per boundary type. |
| Sawtooth working set; `OOMKilled` (exit 137) every few hours — time kills it, not traffic (Scenario 7) | Memory headroom before the next kill | 1. Confirm leak vs. plateau vs. mis-set limit. 2. Buy time with a rolling restart / higher limit. 3. Take two gcdumps an hour apart and diff them. |
| Ship date next week; "security" is a checkbox on someone's ticket (Scenario 8) | Review time before the ship date | 1. Walk the non-negotiables in priority order. 2. Test object-level access control — can Alice fetch Bob's order? 3. Scan dependencies and the repo history for leaked secrets. |
| An erasure request citing GDPR; a junior just logged the full user object, PII included (Scenario 9) | Knowing where the PII actually lives | 1. Classify the fields and find every copy. 2. Stop the log leak — scrub at the boundary. 3. Erase via crypto-shred plus purge; loop in legal/privacy. |
| An advisory names a package four levels down your graph; did it ever reach a build? (Scenario 10) | An answer in the next 30 minutes | 1. Grep committed lockfiles across all repos, branches and tags. 2. Check SBOMs and restore logs for actual builds. 3. If it executed anywhere, rotate every credential that machine could see. |
| The agent sent data to an address nobody recognises; every step in the log looks permitted (Scenario 11) | The ability to say whose data left | 1. Disable the outbound tool, not the assistant. 2. Scope exposure from tool-call logs. 3. Trace back to the poisoned document and purge the index. |
| Egress up 280%, nothing broke, no alert fired, signups flat (Scenario 12) | Cost velocity you are not measuring | 1. Characterise the traffic before blocking anything. 2. Cache hard at the edge and strip unknown query parameters. 3. Scale back what auto-scaled up and stayed. |

---

## Scenario 1 — Black Friday: traffic is 5× and the shop is falling over

### The scenario

It is 08:00 on Black Friday. Marketing sent a push notification to two million customers at once. Your e-commerce API normally serves 3,000 requests/second; it is now taking 15,000. The product pages are timing out, the checkout button spins forever, and the on-call channel is filling with screenshots. The CEO is asking, in all caps, whether "we are losing money right now." You are.

### Symptoms / how you notice

- p95/p99 latency climbs first, then error rate; throughput plateaus below demand because the system is saturated, not because traffic stopped.
- Database CPU pinned at 100%, or connection-pool exhaustion errors: `The connection pool has been exhausted` / `Timeout expired... getting a connection from the pool`.
- Thread-pool starvation: request queue depth grows, `ThreadPool` injection lags, everything gets slower at once.
- Health checks flap, pods get killed and rescheduled, making things *worse* right when load is highest.

### Immediate response (stop the bleeding)

Do these roughly in order — the first three buy you the most time for the least risk:

1. **Scale out the stateless tier.** If your app servers are stateless (they must be — see below), add instances. Manual override the autoscaler's max if it is capping you.
2. **Shed non-critical load with feature flags.** Turn off recommendations, "customers also bought," live inventory counts, wish-list syncing, personalized banners. Every one of those is a database call you do not need during a stampede.
3. **Turn on / warm the caches and CDN.** Serve product pages and catalog data from cache with a short TTL. Push static and semi-static content to the CDN so it never touches your origin.
4. **Rate-limit and load-shed at the edge.** Better to serve 12,000 requests well and reject 3,000 with a fast `429 + Retry-After` than to serve all 15,000 badly and collapse.
5. **Protect the database.** Cap the connection pool, add read replicas for read traffic, and move checkout to a queue (below). The DB is almost always the real bottleneck.
6. **Freeze deploys.** No configuration changes, no "quick fixes" to prod during the incident unless they are on this list.

> **Rule of thumb: in a stampede, protect the scarcest resource — almost always the primary database — and reject early rather than fail late.**

### Root causes

The traffic was foreseeable; the fragility was architectural. Common culprits:

- **Stateful app servers** (in-memory session, sticky affinity) that cannot scale horizontally without losing user state.
- **Synchronous checkout** that holds a DB transaction open across payment, inventory, and email — one slow dependency stalls the whole pipeline.
- **No caching layer**, so every product view is a fresh query.
- **Unbounded fan-out to the database**, with a connection pool smaller than the number of concurrent requests, causing pool exhaustion and cascading timeouts.
- **No capacity plan and no load test** — nobody knew the ceiling until they hit it.

### The fix & architectural options (with trade-offs)

**Make the app tier stateless** so autoscaling actually works. Push session state to a distributed cache (Redis) or a signed token; never rely on a specific instance. This is the precondition for everything else (Ch. 10).

**Layer your caching.** Think in tiers, cheapest and closest first:

| Layer | What it holds | TTL / invalidation | Trade-off |
|---|---|---|---|
| CDN / edge | Static assets, cacheable product pages | Minutes; purge on publish | Huge offload; risk of stale prices |
| In-memory (per instance) | Hot config, small lookups | Seconds | Fast, but N copies to invalidate |
| Distributed (Redis) | Product data, sessions, rendered fragments | Seconds–minutes | Shared, one place to invalidate; network hop + a new dependency |
| DB read replicas | Everything read-heavy | Replica lag | Scales reads; eventual consistency |

**Queue-based load leveling for writes.** Checkout should *accept* the order fast, enqueue the heavy work (payment capture, inventory decrement, fulfillment, email), and return "order received." A durable queue absorbs the spike; consumers drain it at a sustainable rate (Ch. 9). The user sees an instant confirmation page; the order finalizes asynchronously.

**Graceful degradation and load shedding.** Design tiers of service: core (browse, add to cart, checkout) must never go down; everything else is expendable. A load shedder that drops the bottom 20% of non-critical traffic keeps the top 80% healthy.

**Rate limiting.** ASP.NET Core has built-in rate limiting; use a fixed-window or token-bucket limiter per client, and always return `Retry-After`:

```csharp
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddTokenBucketLimiter("checkout", o =>
    {
        o.TokenLimit = 100;
        o.TokensPerPeriod = 100;
        o.ReplenishmentPeriod = TimeSpan.FromSeconds(1);
        o.QueueLimit = 0; // shed immediately rather than build a backlog
    });
});
```

**Fix connection-pool exhaustion deliberately.** The instinct to "raise Max Pool Size" is usually wrong — you just move the pileup from the app to the database. The pool exists to *protect* the DB. Size it to what the database can actually serve, keep transactions short, never do I/O or `await` external calls while holding a connection, and use a queue to smooth the write rate. If 500 app instances each open 100 connections, your database sees 50,000 connections and dies; a connection multiplexer/proxy (e.g. PgBouncer for PostgreSQL) or a smaller per-instance pool is the fix.

**The oversell problem — "selling what you don't have."** Under load, two customers read "1 left in stock" at the same moment and both check out. Without coordination you sold two of one item. Options:

| Approach | How it works | Trade-off |
|---|---|---|
| Optimistic concurrency | Version/row-version on stock; decrement fails if changed | Simple; retries/failures under contention |
| Atomic decrement | `UPDATE stock SET qty = qty - 1 WHERE id = @id AND qty > 0` (rows-affected = success) | Correct and cheap; a DB hotspot on popular SKUs |
| Inventory reservation | Reserve stock for N minutes at add-to-cart / checkout start; confirm or expire | Prevents oversell + good UX; needs a reaper for expired holds |
| Oversell + reconcile | Accept the order, sort out shortfalls after (backorder/refund) | Max throughput; angry customers, ops burden |

For high-value or scarce goods, **reservation** is the senior answer; for commodity stock, an atomic conditional decrement is often enough. Whatever you choose, the decrement must be atomic — never read-then-write across a network round trip.

### How to prevent it

- **Capacity planning and load testing *before* the event.** Load-test to failure (k6, NBomber, Azure Load Testing), find the ceiling, and know which resource breaks first. "We can do 9,000 rps before the DB saturates" is a plan; "we think we'll be fine" is not.
- **Pre-provision / pre-warm.** Autoscaling has a cold-start lag; scale up ahead of a *known* spike and warm caches and connection pools.
- **Feature-flag the non-critical surface** in advance so shedding load is a toggle, not a deploy.
- **Game-day the failure** — run a load test that simulates the push notification and rehearse the runbook.

> **In an interview:** "I separate what must stay up from what's optional. First I make the app tier stateless so I can scale it horizontally and put load-shedding and rate limiting at the edge, returning fast 429s instead of failing slowly. Then I protect the database — the usual bottleneck — with layered caching, read replicas, and queue-based load leveling so checkout accepts orders fast and finalizes them asynchronously. For inventory I use an atomic conditional decrement or a reservation window to avoid overselling. And critically, I load-test to failure and pre-provision *before* Black Friday — you can't autoscale your way out of a design that isn't horizontally scalable."

---

## Scenario 2 — The lost write: the user got 200 but the data never saved

### The scenario

A customer swears they placed an order. They have the confirmation screen. Support has the screenshot. But there is no order in the database, no charge, nothing. Multiply by a few hundred and you have a support fire and a trust problem. The logs show the request returned `200 OK`. So where did the data go?

### Symptoms / how you notice

- "It said it worked but it didn't" tickets that you cannot reproduce.
- A message was published to the broker but the local DB row is missing (or vice versa) — the two stores disagree.
- Downstream services processed an event that has no matching record upstream.
- Reconciliation reports (if you have them) show counts drifting apart between services.

### Immediate response (stop the bleeding)

1. **Quantify the gap.** Run a reconciliation query across the two stores (e.g. payment records vs. orders) to find the exact set of affected entities. You cannot fix what you cannot enumerate.
2. **Recover from a durable source.** If you emitted events or wrote a log, replay it to rebuild the missing rows. If payment succeeded but the order is missing, the payment record *is* your source of truth for recovery.
3. **Stop making it worse.** If the cause is a "return 200 then do work in the background" fire-and-forget path, disable that path or make it synchronous until fixed.
4. **Communicate.** Tell support what is affected and give customers a definitive answer.

### Root causes

Almost always a **dual-write problem**: a single request must update two systems that do not share a transaction — for example, "write the order to the database *and* publish an `OrderPlaced` message to the broker." There is no distributed transaction between your DB and your queue, so any of these happens:

- DB commit succeeds, broker publish fails → downstream never hears about the order.
- Broker publish succeeds, DB commit fails/rolls back → downstream acts on an order that does not exist.
- The process crashes *between* the two writes.

The other classic is **"return 200, then do the work."** Handing the caller a success response before the work is durably committed means a crash, a pool timeout, or an unhandled exception in the background silently drops the write. **A `200` should mean "I have durably accepted this," not "I intend to try."**

### The fix & architectural options (with trade-offs)

**The Transactional Outbox.** The core trick: only write to *one* store transactionally — your database — and record the intent to publish in the *same* transaction, in an `outbox` table. A separate relay reads the outbox and publishes to the broker, marking rows sent. Now the DB write and the "I will publish" record commit atomically; the actual publish becomes a retryable, at-least-once background job. (Ch. 9 covers the table and publisher mechanics; Ch. 22 shows the background relay itself.)

If the relay crashes after publishing but before marking a row processed, it republishes on restart — hence **at-least-once**, and hence consumers must **deduplicate**. That is the whole game: you trade the impossible "exactly-once delivery" for "at-least-once delivery + idempotent consumers," which together give **effectively-once processing**.

**Idempotency keys and dedup.** Give every message (and every externally-triggered command) a stable ID. Consumers record processed IDs (an `inbox`/processed-messages table, backed by a unique index) and skip duplicates — Ch. 21 covers the implementation. For inbound HTTP writes, accept a client-supplied `Idempotency-Key` header (Stripe's model): the first request does the work and stores the result keyed by that value; retries with the same key return the stored result instead of doing the work twice.

**Sagas for multi-service consistency.** When a business transaction spans services (reserve inventory → charge card → create shipment), you cannot hold one ACID transaction across all of them. A **saga** is a sequence of local transactions, each with a compensating action to undo it if a later step fails (release the reservation, refund the charge). This is eventual consistency by design — the system is briefly inconsistent and converges (Ch. 9).

| Approach | Consistency | Coupling / complexity | When |
|---|---|---|---|
| Synchronous 2-phase-ish call chain | Strong-ish, but fragile | Tight; one slow service stalls all | Rarely — avoid distributed transactions |
| Outbox + events + dedup | Eventual, reliable | Moderate; needs relay + inbox | Default for event-driven writes |
| Orchestrated saga | Eventual, coordinated | Central orchestrator to reason about | Multi-step business workflows |
| Choreographed saga | Eventual, emergent | Loose; harder to trace end-to-end | Simple, few-step flows |
| Event sourcing | Strong per-aggregate, rebuildable | High; new mental model | Audit-critical, needs full history |

**Eventual vs. synchronous consistency** is the real decision. Synchronous is simpler to reason about but couples availability — if any participant is down, the write fails. Eventual consistency keeps you available and uses the outbox/saga machinery to converge; the cost is that for a short window the system is provably inconsistent, and your UI and business rules must tolerate that ("your order is being processed").

**The exactly-once myth.** There is no exactly-once *delivery* over an unreliable network — it is a theoretical impossibility. What you can build is exactly-once *processing effect*: at-least-once delivery + idempotent handlers. Any vendor claiming "exactly once" is doing dedup under the hood. Design for duplicates and you are safe; assume they can't happen and you will lose or double-apply data.

**Event sourcing angle.** If you store the *events* as the source of truth rather than current state, a "lost write" becomes far less likely and always recoverable — you can rebuild any projection by replaying the log, and you get a full audit trail for free. The cost is a genuinely different programming model (Ch. 9), so reach for it when auditability and reconstructability justify it, not by default.

### How to prevent it

- **Never dual-write.** One transactional store per write; propagate via outbox.
- **Make `200` mean durably committed.** If you must go async, return `202 Accepted` with a status URL, and back it with a durable queue/outbox — not a fire-and-forget `Task`.
- **Reconciliation jobs as a standing safety net.** A scheduled job that compares counts/checksums across services and alerts (or auto-heals) on drift. Even a perfect design benefits from a smoke detector.
- **Idempotency everywhere** writes can be retried — from the public API down to internal consumers.

> **In an interview:** "The root cause is almost always a dual-write — updating the database and the message broker without a shared transaction, so a crash between them loses or orphans data. The fix is the Transactional Outbox: write the business row and an outbox row in one DB transaction, then a relay publishes at-least-once and consumers dedup by message ID, giving effectively-once processing. Exactly-once delivery is a myth, so I design for duplicates instead of pretending they can't happen. And I never let a 200 mean 'I'll try later' — either it's durably committed, or I return 202 backed by a durable queue, plus a reconciliation job as a safety net."

---

## Scenario 3 — Stop-the-world: garbage collector pauses are causing latency spikes

### The scenario

A trading-adjacent API has a strict p99 SLA of 50 ms. Most of the time it sits at 8 ms. But every few seconds, one request in a hundred takes 300–800 ms for no obvious reason — no slow query, no downstream call, nothing in the trace. The spikes correlate with nothing the business logic does. They correlate perfectly with GC.

### Symptoms / how you notice

- Periodic latency spikes uncorrelated with request content; a "sawtooth" p99 while p50 is flat.
- `dotnet-counters` shows high **Gen 2 GC count**, rising **% Time in GC**, and a large/growing **LOH size**.
- Memory climbs then drops sharply (a full collection), repeatedly.
- CPU spikes during pauses even though the app "isn't doing anything."

### Immediate response (stop the bleeding)

1. **Confirm it's really GC.** Attach `dotnet-counters monitor -p <pid> System.Runtime` and watch `% Time in GC`, `Gen 2 GC Count`, `LOH Size`, and `Allocation Rate`. If GC time is single-digit percent, GC is *not* your problem — look elsewhere (lock contention, thread-pool starvation, a chatty dependency).
2. **Switch to Server GC** if you are on Workstation GC in a server workload — this is often a one-line, high-impact change (below).
3. **Ensure concurrent/background GC is on** so Gen 2 collections run mostly off the request path.
4. **Give it headroom.** If the container memory limit is so tight that GC runs constantly, raise it — GC frequency scales with how quickly you fill the heap.

### Root causes

- **Allocation pressure.** The app allocates too much, too fast. High allocation rate → frequent Gen 0/1 collections, and promotion of survivors into Gen 2, whose collections are the expensive, potentially stop-the-world ones (Ch. 2).
- **Large Object Heap (LOH) churn and fragmentation.** Objects ≥ 85,000 bytes (big arrays, large strings, buffers) go on the LOH, which is collected only during Gen 2 and historically not compacted — so it fragments, wasting memory and triggering more full collections.
- **Wrong GC mode.** Workstation GC in a multi-core server process serializes collection on one thread; Server GC uses a heap and thread per core and is built for throughput.
- **Concurrent GC disabled**, so Gen 2 collections block all application threads.
- **Midlife crisis:** objects that live "just long enough" to be promoted to Gen 2 but then die, forcing expensive Gen 2 work (e.g. items cached for a few seconds).

### The fix & architectural options (with trade-offs)

**Choose the right GC mode.** Configure it explicitly rather than relying on defaults:

```xml
<PropertyGroup>
  <ServerGarbageCollection>true</ServerGarbageCollection>
  <ConcurrentGarbageCollection>true</ConcurrentGarbageCollection>
</PropertyGroup>
```

| Mode | Behaviour | Best for | Cost |
|---|---|---|---|
| Workstation GC | Single managed heap, minimal footprint | Desktop apps, low-core/memory containers | Poor throughput under server load |
| Server GC | Heap + GC thread per core, parallel | High-throughput services | Higher memory + CPU baseline |
| Concurrent/Background GC | Gen 2 runs alongside app threads | Latency-sensitive services | Slightly more CPU/memory |

**Reduce allocations — the real fix.** GC tuning caps the pain; *not allocating* removes it. Concretely (Ch. 15):

- **Pool reusable buffers** with `ArrayPool<T>.Shared` instead of `new byte[...]` per request. This is the single biggest win for LOH churn.
- **Use `Span<T>`/`Memory<T>`** and `stackalloc` to slice and parse without intermediate arrays and substrings.
- **Prefer `struct`** for small, short-lived values to keep them off the heap — but measure; large structs copied around can be *worse* than a class.
- **Cache and reuse** big buffers rather than allocating a fresh 100 KB array per call.
- **Avoid hidden allocations:** boxing value types, LINQ in hot paths, closures capturing variables, `string` concatenation in loops (use `StringBuilder` or interpolation handlers), `params` arrays, and `async` state machines over trivial work.

```csharp
// Before: allocates a fresh buffer per call — straight onto the LOH, then GC churn.
byte[] buffer = new byte[128 * 1024];

// After: rent from the pool, return in finally.
byte[] buffer = ArrayPool<byte>.Shared.Rent(128 * 1024);
try
{
    // use buffer[..length]
}
finally
{
    ArrayPool<byte>.Shared.Return(buffer);
}
```

**Tame the LOH.** Pool large buffers so you stop allocating them at all; keep large objects long-lived and reused. If fragmentation is unavoidable, you can request LOH compaction *once* (it is expensive — do not do it every collection):

```csharp
GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
GC.Collect(); // deliberate, rare, e.g. after a large batch job — never in the hot path
```

**DATAS on .NET 8+.** Modern .NET ships *Dynamic Adaptation To Application Sizes* for Server GC — it adapts heap count/size to the live workload, which helps memory footprint in containers and can reduce over-allocation. It is on by default in .NET 9; you can toggle it via `System.GC.DynamicAdaptationMode` / `DOTNET_GCDynamicAdaptationMode`. Know it exists and measure whether it helps your workload rather than flipping it blindly.

**Diagnose properly, not by guessing:**

- `dotnet-counters` for live GC/allocation counters.
- `dotnet-gcdump` for a heap snapshot — *what* is on the heap and what roots it.
- `dotnet-trace` with the GC/allocation providers, or a profiler's allocation view, to find the **allocation hot paths** — the few call sites responsible for most garbage. Fix those; ignore the rest.

**When GC is NOT your real problem.** The most senior move here is refusing to tune GC when GC is innocent. If `% Time in GC` is low, your spikes are something else wearing a GC costume: thread-pool starvation from sync-over-async, `SemaphoreSlim`/lock contention, a downstream call with a fat tail, JIT/cold-start on first hit, or container CPU throttling. Measure first; a week spent shaving allocations to fix a latency spike caused by a blocking `.Result` call is a week wasted.

### How to prevent it

- **Set an allocation budget for hot paths** and enforce it with benchmarks (BenchmarkDotNet reports bytes allocated) in CI.
- **Load-test with GC metrics captured** so a regression in allocation rate is visible before production.
- **Pick Server + background GC intentionally** for services and document why.
- **Watch container memory limits** — GC frequency is a function of headroom; a too-tight limit manufactures GC pressure.

> **In an interview:** "First I confirm it's actually GC with dotnet-counters — if % time in GC is low, the spikes are thread-pool starvation or contention wearing a GC mask, and I chase that instead. If it is GC, I make sure I'm on Server GC with background collection so Gen 2 doesn't stop the world, then I attack the real cause: allocation pressure. I pool buffers with ArrayPool, use Span and structs to cut per-request garbage, and kill LOH churn since large arrays trigger expensive Gen 2 collections. GC tuning caps the symptom; reducing allocations removes it. And I know exactly-once GC tricks like LOH compaction are last resorts, not hot-path tools."

---

## Scenario 4 — The broker is down: a critical dependency has failed

### The scenario

Your order service publishes every order to RabbitMQ, where fulfillment, billing, and notifications consume it. At 14:20 the broker cluster becomes unreachable — a network partition, a failed upgrade, does not matter. Suddenly every publish call hangs. Threads pile up waiting on the broker, the thread pool starves, health checks fail, and an outage that started in *one* dependency is now taking down the *entire* order API. A single failed component is metastasizing into a full outage.

### Symptoms / how you notice

- Publish/consume calls time out or hang; connection counts to the broker collapse.
- Request threads block on the dead dependency → thread-pool starvation → the *whole* service slows, not just the broker path.
- Retries pile on retries — a **retry storm** — hammering the recovering dependency and keeping it down.
- Cascading readiness failures: dependent services mark themselves unhealthy and get restarted, amplifying the outage.

### Immediate response (stop the bleeding)

1. **Fail fast, stop blocking.** The worst outcome is threads hanging on a dead dependency. Trip the circuit breaker so calls fail immediately instead of waiting on timeouts.
2. **Buffer locally instead of publishing.** If you already have the Outbox from Scenario 2, you are saved: keep writing orders + outbox rows to your *own* database; the relay simply can't drain to the broker yet and will catch up when it returns. The broker being down becomes a *delay*, not a *data-loss* event.
3. **Degrade gracefully.** Keep accepting orders (core function); let the async, broker-dependent steps lag. Show the user "order received, processing."
4. **Kill the retry storm.** Back off aggressively; do not let every instance retry in lockstep.
5. **Isolate the blast radius.** Ensure the broker failure cannot consume all threads/connections needed by unrelated endpoints (bulkheads, below).

### Root causes

- **No isolation between a dependency and the caller** — a slow/dead dependency is allowed to consume all the caller's threads and connections.
- **Unbounded, synchronous, un-timed calls** to the dependency.
- **Naive retries** (immediate, unlimited, synchronized) that turn a blip into a storm and a recovery into a re-outage.
- **No local durable buffer**, so when the broker is down, writes are simply lost.

### The fix & architectural options (with trade-offs)

**Circuit breaker + fallback (Polly).** Wrap the dependency in a circuit breaker so that after a threshold of failures, calls short-circuit for a cool-off period and you serve a fallback instead of hanging — and give every call a timeout so nothing waits indefinitely. Ch. 21 builds the full `Microsoft.Extensions.Resilience` / Polly pipeline (retry + breaker + layered timeouts) and explains why the ordering of strategies matters. The decision that is specific to *this* incident is what the fallback should be — and for publishing, the answer is the outbox buffer below.

**Retry with exponential backoff *and jitter*.** Backoff alone is not enough: if every instance retries on the same schedule, they synchronize into coordinated waves. Jitter randomizes the delay so load spreads out. Also make retries **idempotent** and **bounded** — retrying a non-idempotent write is how you double-charge a customer.

**Bulkheads to contain the blast radius.** Named after ship compartments: partition your resources so one failing dependency cannot drown the rest. Give the broker path its own bounded concurrency limiter / connection pool; when it saturates, only *that* path degrades while the checkout and browse paths keep their own capacity. This is the difference between "the broker is down" and "the whole service is down."

**The outbox as a buffer / local durable queue.** This is the key architectural insight linking Scenarios 2 and 4: **if you write to your own durable store first and relay to the broker asynchronously, the broker being down cannot lose data or block the request path.** Orders accumulate in the outbox; when the broker recovers, the relay drains the backlog. Your availability is decoupled from the broker's. When the queue itself is the thing that is down, a local durable buffer (the outbox, or a local disk-backed queue) is the only way to keep accepting work without loss.

**Dead-letter queues (DLQ).** For messages that repeatedly fail to process (poison messages, or a downstream that is down), route them to a DLQ after N attempts instead of blocking the main queue or infinitely retrying. Then alert, inspect, fix, and replay. A DLQ keeps one bad message from stalling the whole pipeline.

**Health checks & readiness — get this right.** Distinguish **liveness** (is the process alive? restart if not) from **readiness** (can it serve traffic right now?). A subtle but critical decision: **a non-critical dependency being down should not fail your readiness probe**, or Kubernetes will pull a perfectly serviceable pod out of rotation and make the outage worse. Readiness should reflect *your* ability to serve, degraded or not (Ch. 11, Ch. 21).

```csharp
builder.Services.AddHealthChecks()
    .AddCheck("self", () => HealthCheckResult.Healthy(), tags: ["live"])
    // Broker is degradable, not fatal: report Degraded, keep serving.
    .AddRabbitMQ(tags: ["ready-optional"]);
```

**Idempotent recovery when it comes back.** When the broker returns, the relay republishes buffered messages at-least-once, and consumers dedup (Scenario 2). Recovery must be safe to run repeatedly — the whole system should converge cleanly no matter how many duplicates the recovery produces.

| Strategy | Protects against | Cost / trade-off |
|---|---|---|
| Circuit breaker | Hanging on a dead dependency; retry storms | Fallback path must exist and be meaningful |
| Retry + backoff + jitter | Transient blips | Only safe for idempotent ops; adds latency |
| Bulkhead | One dependency starving all resources | Lower peak utilization per partition |
| Outbox / local buffer | Data loss + blocking when broker is down | Extra store + relay + dedup complexity |
| Dead-letter queue | Poison messages stalling the pipeline | Needs monitoring + replay tooling |
| Graceful degradation | Total outage from partial failure | Product must accept reduced function |

**Avoiding cascading failure.** The through-line: a resilient system *contains* failure rather than propagating it. Fail fast (circuit breakers) so you do not exhaust threads; isolate (bulkheads) so one failure has a bounded blast radius; buffer (outbox) so downstream outages become delays, not losses; back off with jitter so recovery is not re-broken by a stampede; and degrade so partial capability beats zero. Design so that when — not if — a dependency dies, the rest of the system bends instead of snapping.

### How to prevent it

- **Every network call gets a timeout, a retry policy, and a circuit breaker.** No exceptions in a distributed system.
- **Bulkhead critical vs. non-critical dependencies** from day one.
- **Adopt the outbox for anything you cannot afford to lose** — it doubles as your broker-outage insurance.
- **Chaos-test dependency failure** (kill the broker in staging) and rehearse recovery, including DLQ replay.
- **Readiness probes that report degraded, not dead,** for optional dependencies.

> **In an interview:** "The failure I'm most afraid of isn't the broker dying — it's that death cascading into a full outage because threads hang on it and starve the pool. So I fail fast with a circuit breaker and timeouts, isolate it behind a bulkhead so it can't consume resources the rest of the service needs, and retry with exponential backoff plus jitter to avoid a retry storm on recovery. The architectural key is the Transactional Outbox: I write orders to my own database and relay to the broker asynchronously, so a broker outage becomes a delay, not data loss. Poison messages go to a dead-letter queue, and readiness probes report degraded rather than dead for optional dependencies so Kubernetes doesn't yank healthy pods."

## Scenario 5 — Disaster: the database is gone. How backups should really be done

### The scenario

It is 03:14. PagerDuty is screaming. The primary database instance is unreachable, and when the on-call DBA finally gets a console open, the data files are corrupt — a bad storage firmware update chewed through the volume. The read replica you were quietly proud of? It faithfully replicated the corruption within seconds. Now the only question that matters is the one nobody wants to answer out loud: *when was our last good, restorable backup, and how much data is between then and now?*

This is the moment that separates teams who *have* backups from teams who have *tested, restorable* backups. Those are not the same thing.

### Symptoms / how you notice

- The database is down and will not come back — corruption, deletion, a botched migration, ransomware, or a fat-fingered `DELETE` without a `WHERE`.
- Replicas mirror the damage. A logical corruption or an accidental `DROP TABLE` propagates to every synchronous and asynchronous replica almost instantly.
- Someone asks "can we just restore?" and the room goes quiet because nobody has actually done a restore drill in months.

> **Replication is not a backup.** Replication protects against *hardware* loss of one node. It does nothing against logical errors — a bad delete, a corrupt page, a malicious actor, a schema migration that truncates a column — because it dutifully copies those to every replica. Backups protect against *time*: they let you go back to a point before the mistake.

### Immediate response

1. **Stop writing.** If the primary is degraded but partially up, fence it off before more damage accrues. You cannot recover to a clean point if the corruption keeps advancing.
2. **Identify your recovery target.** For accidental data loss, the target is "one second before the bad statement." For hardware loss, the target is "latest consistent state."
3. **Locate the most recent restorable artifact** — the last full backup plus the chain of differential/log backups needed to roll forward.
4. **Restore to a *new* instance**, never over the damaged one. You may need the damaged instance for forensics, and restoring in place destroys your only other copy.
5. **Communicate RPO reality early.** If the last usable point is 40 minutes ago, tell stakeholders that 40 minutes of data is at risk *now*, not after a hopeful two-hour restore that might fail.

### Root causes

The disaster itself is rarely the interesting part. The *recovery pain* almost always traces to one of these:

- **No transaction-log backups**, so recovery granularity is "last nightly full" — up to 24 hours of loss.
- **Backups on the same storage/region** as the primary, so the event that killed the database killed the backups too.
- **Backups never tested.** They complete, the job goes green, and nobody ever proves a restore works until the night it must.
- **Retention too short** — the corruption started three days ago (a slow logical bug) but backups only go back 24 hours.

### The fix & options (with trade-offs)

You recover by walking the backup chain. But the real lesson is designing the strategy *before* the disaster. Start with the vocabulary a senior is expected to wield precisely.

**Backup types:**

| Type | What it captures | Restore cost | Storage cost |
|---|---|---|---|
| **Full** | Entire database as of a point in time | Fastest (single artifact) | Highest |
| **Differential** | Everything changed since the last *full* | Full + one diff | Medium, grows until next full |
| **Incremental** | Everything changed since the last backup of *any* kind | Full + every increment in order | Lowest, but longest chain |
| **Transaction log** | Every committed change (the log records) | Full + diff + replay logs to exact second | Small, frequent |

**Logical vs physical:**

- **Physical** (SQL Server `.bak`, Postgres base backup, filesystem/volume snapshot) copies the on-disk data pages. Fast to restore, but tied to engine version and platform.
- **Logical** (`pg_dump`, `mysqldump`, `bcp`) exports SQL statements / data rows. Portable across versions and even engines, great for a single table, but slow to restore a large database and does not support point-in-time replay.

Use physical for full-system disaster recovery; keep logical dumps around for portability and surgical single-object restores.

**Point-in-time recovery (PITR)** is the payoff of log backups: restore the last full backup, apply the differentials, then *replay the transaction log up to a specific timestamp* — say `2026-07-21 03:13:59`, one second before the bad `DELETE`. This is what turns "we lost a day" into "we lost four seconds."

**RPO and RTO — with numbers.** Two objectives, often confused:

- **RPO (Recovery Point Objective)** — how much *data* you can afford to lose, measured in time. RPO = 5 minutes means your backup cadence (log backups) must run at least every 5 minutes.
- **RTO (Recovery Time Objective)** — how long you can afford to be *down*. RTO = 1 hour means the entire restore-and-validate procedure must complete inside an hour.

Concretely: nightly full at 01:00, differentials every 6 hours, transaction-log backups every 5 minutes gives you **RPO ≈ 5 minutes**. Whether you hit **RTO** depends on how fast you can pull and replay those artifacts — which is exactly what restore drills measure.

**The 3-2-1 rule** is the durable baseline: **3** copies of the data, on **2** different media/storage types, with **1** copy offsite (different region/provider). A modern extension is **3-2-1-1-0**: one copy **immutable/offline**, and **0** errors verified by testing.

**Managed databases** do much of this for you, and a senior knows the defaults:

- **Azure SQL Database** takes automated full/differential/log backups continuously and supports PITR. Default retention is 7 days, configurable **1–35 days**, with optional long-term retention (weekly/monthly/yearly) for years.
- **Amazon RDS / Aurora** takes automated backups with PITR, retention configurable up to **35 days**, plus manual snapshots you retain indefinitely.
- **PostgreSQL self-managed**: enable **continuous archiving** (WAL archiving / `archive_command`, or tools like pgBackRest / Barman). A base backup plus archived WAL segments gives you PITR to any second in the retained window.

> Even with a managed database, **you own the recovery drill and the retention policy.** The cloud stores the bytes; it does not know that your compliance rule needs 7 years or that your RPO is 1 minute. Configure it deliberately.

**Ransomware and immutability.** Attackers now target backups first. Defend with **immutable / WORM (write-once-read-many) storage** — Azure Blob immutability policies, S3 Object Lock in compliance mode — so even an admin credential cannot delete or encrypt backups before the retention window expires. Keep at least one copy air-gapped or logically isolated in a separate account with separate credentials.

**Accidental `DELETE` recovery** is the everyday disaster. Options, fastest to slowest:

1. If you caught it in the same transaction — `ROLLBACK`. (You did wrap it in an explicit transaction, right?)
2. PITR to one second before the statement, restore to a side instance, export the affected rows, re-insert into production.
3. Logical dump of just that table from last night if PITR is unavailable.

### How to prevent it

- **Automate everything.** Manual backups are missed backups. Schedule and monitor them; alert on *missing* or *stale* backups, not just failed ones.
- **Test restores on a schedule** — monthly, into a scratch environment, timed against your RTO. Automate a "restore the latest backup and run a smoke query" pipeline.
- **Encrypt backups** (TDE / backup encryption) and manage the keys separately. A stolen backup is a data breach.
- **Set retention to match the *slow* disaster**, not just the fast one — logical corruption can lurk for days.
- **Store the recovery runbook where you can reach it when the database — and maybe the wiki — is down.**

> **An untested backup is not a backup; it is a hope.** The green checkmark on the backup job proves the *write* succeeded, not the *restore*. The only proof is a restore you performed on purpose, on a boring afternoon, before you ever needed it.

---

## Scenario 6 — Polyglot: the system is modules in different languages that must talk

### The scenario

Your platform is a .NET shop — mostly. But the fraud-scoring model lives in Python because that is where the data scientists work and where PyTorch runs. The legacy billing engine is a 15-year-old Java service nobody wants to rewrite. A new edge function is in Go because a partner shipped it that way. Now product wants real-time fraud scoring inside checkout, which means your ASP.NET Core checkout service has to call the Python model on the hot path — and it has to be fast, versioned, and observable. Welcome to the polyglot system.

### Symptoms / how you notice

- You reach for "just add a NuGet package" and realize the capability you need only exists as a Python library.
- Integration is happening by whatever was easiest: one service scrapes another's database, another parses a CSV drop, a third calls an undocumented HTTP endpoint that returns different JSON shapes on Tuesdays.
- A field rename in the Python service silently breaks .NET deserialization in production because there was no shared contract.

### Why polyglot happens

It is not (usually) architectural vanity. It is **teams** (different groups own different stacks), **ML in Python** (the ecosystem is simply there), **legacy** (rewriting a working billing engine is a bad bet), and **best-tool-for-the-job** (Go for a network proxy, Rust for a parser). The senior's job is not to eliminate polyglot — it is to make the *boundaries between languages* clean, contractual, and observable.

> In a polyglot system, **the contract at the boundary is the architecture.** The languages behind each boundary are implementation details. Invest in the contracts; treat the internals as replaceable.

### Immediate response (when integration is already a mess)

1. **Draw the boundaries.** For each cross-language call, write down: who calls whom, sync or async, and what the payload is.
2. **Find the shared-database couplings and flag them as debt** — they are the ones that will bite hardest.
3. **Pick one integration style per boundary type** and standardize, rather than one-off-ing each connection.

### The fix & options (with trade-offs)

Choose the integration style per boundary. The main options:

| Style | Best for | Coupling | Cross-language story | Watch out for |
|---|---|---|---|---|
| **REST / JSON over HTTP** | Public-ish APIs, low-frequency calls, human-debuggable | Loose | Universal; every language speaks it | No enforced schema unless you add OpenAPI; JSON is verbose/slow at scale |
| **gRPC + Protobuf** | High-throughput, low-latency internal calls (like the ML hot path) | Contract-first, tight on schema | Excellent — protoc generates clients for C#, Python, Go, Java | Binary (harder to eyeball); needs HTTP/2; browser support needs gRPC-Web |
| **Message broker / events (Kafka, RabbitMQ)** | Async work, decoupling, fan-out, buffering load spikes | Loose (temporal decoupling) | Good — any language with a client library | Eventual consistency; schema evolution across consumers; ordering/idempotency |
| **Shared database** | (anti-pattern) | Extremely tight | "Works" but couples internal schemas | **Avoid.** No encapsulation, no independent deploys, migrations break everyone |

**The shared-database anti-pattern** deserves a blunt statement: when two services read and write the same tables, you have not built two services — you have built one service with two deployment units and no encapsulation. A schema change to satisfy one service breaks the other. Ban it at the boundary; if two components need the same data, one owns it and exposes an API.

**Contract-first and schema/versioning across languages.** The strength of gRPC/Protobuf here is that a `.proto` file *is* the contract, checked into a shared repo, generating clients for every language. Protobuf's evolution rules (never reuse field numbers, add new fields as optional, don't change types) let a Python producer and a .NET consumer evolve independently — this is the schema-evolution discipline from **Chapter 24** applied across languages. For JSON boundaries, get the same discipline from **OpenAPI** with generated clients and a schema registry; for Kafka, an **Avro/Protobuf schema registry** enforces compatibility before a bad message ever ships.

**Service mesh / sidecars and Dapr.** As the number of polyglot services grows, cross-cutting concerns (mTLS, retries, service discovery) multiply across languages. A **service mesh** (Linkerd, Istio) pushes these into a sidecar so each language doesn't reimplement them. **Dapr** goes further: it exposes **building blocks** — service invocation, pub/sub, state management, secrets, bindings — over a local HTTP/gRPC API, so a Python service and a .NET service call the *same* Dapr sidecar API to publish an event or read state. That is genuinely valuable in polyglot shops: the integration primitives stop being language-specific.

**Observability across languages** is non-negotiable and easy to get wrong. Use **OpenTelemetry**: it has SDKs for .NET, Python, Go, Java, and propagates **W3C Trace Context** headers across service boundaries. Done right, a single distributed trace shows the checkout request entering the .NET service, hopping to the Python model, and back — one trace ID spanning three languages. Without it, cross-language debugging is guesswork. (See **Chapter 9** for messaging/gRPC mechanics.)

### A concrete example: .NET checkout calling a Python ML model via gRPC

Define the contract once:

```protobuf
// fraud.proto — shared, checked in, source of truth
syntax = "proto3";
package fraud;

service FraudScorer {
  rpc Score (ScoreRequest) returns (ScoreReply);
}

message ScoreRequest {
  string transaction_id = 1;
  double amount = 2;
  string currency = 3;
  string customer_id = 4;
  // New fields go here as higher numbers, optional — never reuse a number.
}

message ScoreReply {
  double risk_score = 1;   // 0.0–1.0
  bool   block = 2;
}
```

The Python side implements the server (generated with `grpcio-tools`); the .NET side consumes a generated client:

```csharp
// .NET checkout service — generated client from fraud.proto
public sealed class FraudCheck
{
    private readonly FraudScorer.FraudScorerClient _client;

    public FraudCheck(FraudScorer.FraudScorerClient client) => _client = client;

    public async Task<bool> IsHighRiskAsync(Order order, CancellationToken ct)
    {
        var request = new ScoreRequest
        {
            TransactionId = order.Id.ToString(),
            Amount = (double)order.Total,
            Currency = order.Currency,
            CustomerId = order.CustomerId.ToString()
        };

        try
        {
            // Fail-open or fail-closed is a deliberate business decision.
            var reply = await _client.ScoreAsync(
                request,
                deadline: DateTime.UtcNow.AddMilliseconds(150),
                cancellationToken: ct);
            return reply.Block || reply.RiskScore > 0.85;
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.DeadlineExceeded)
        {
            // The model is slow. Do we block checkout or let it through?
            // Fail-open here: score asynchronously afterwards rather than lose the sale.
            return false;
        }
    }
}
```

Note the **deadline** and the explicit **fail-open decision** — on a synchronous cross-language hot-path call, you must decide what happens when the other language's service is slow or down. If scoring can tolerate latency, the better design is to move it **off the hot path entirely**: publish an `OrderPlaced` event to a broker, let the Python service consume and score asynchronously, and reserve the synchronous gRPC call for cases where the answer must gate the response.

### How to prevent the mess

- **One contract artifact per boundary**, versioned in source control, generating clients — never hand-write the other side.
- **Standardize the integration styles**: "internal high-throughput = gRPC, async work = Kafka, external = REST+OpenAPI." Fewer patterns, less glue.
- **OpenTelemetry from day one** so cross-language traces exist before you need to debug at 2 a.m.
- **Contract/compatibility tests in CI** (consumer-driven contract tests, schema-registry compatibility checks) so a Python-side rename fails the build, not production.

---

## Scenario 7 — The slow leak: memory keeps growing until the pod is OOM-killed

### The scenario

Your ASP.NET Core service runs fine for about six hours, then Kubernetes kills the pod with `OOMKilled` and restarts it. The graph of working-set memory is a perfect sawtooth: climb, climb, climb, crash, restart, repeat. Nothing crashes under load spikes — it is *time*, not traffic, that kills it. You have a memory leak. In a garbage-collected runtime.

### Symptoms / how you notice

- Steadily climbing **working set** / RSS that never comes back down, independent of load.
- `OOMKilled` events (exit code 137) and periodic restarts in Kubernetes; on bare metal, an eventual `OutOfMemoryException`.
- **Gen 2** and **LOH (Large Object Heap)** sizes growing across GCs — the collector runs but reclaims less each time.
- Latency creeping up as GC works harder to find nothing to free.

### What a managed "leak" actually is

The CLR's garbage collector frees objects that are **unreachable**. A "leak" in .NET is therefore never leaked memory in the C/C++ sense — it is memory the GC *cannot* free because something still holds a reference. **A managed memory leak is an unintentionally retained reference.** Find the reference, kill the leak.

### Root causes — the usual suspects

- **Static collections / caches without eviction.** A `static Dictionary<,>` or a `ConcurrentDictionary` used as a cache with no size cap or expiry grows forever. The single most common .NET leak.
- **Event handler subscriptions never unsubscribed.** `publisher.SomeEvent += handler;` makes the publisher hold a reference to the subscriber. If the publisher outlives the subscriber and you never `-=`, the subscriber (and everything it references) is pinned alive.
- **Captured closures** that capture more than intended — a lambda registered somewhere long-lived that closes over a big object graph.
- **`IDisposable` not disposed** — undisposed `HttpClient` instances (or, the inverse, creating a new `HttpClient` per request and exhausting sockets), unclosed streams, DB connections, timers.
- **Ever-growing in-memory state** — a `List<>` you keep appending to (audit buffer, "recent items") without bound.
- **Large-object retention** — holding references to big byte arrays / buffers that land on the LOH and fragment it.
- **DI captive dependencies** — a **singleton** that injects (and thus captures) a **scoped** or **transient** service, keeping per-request objects alive for the lifetime of the app. The DI container's scope validation catches many of these; respect it.

### Immediate response

1. **Confirm it is a leak, not just high-but-stable usage.** A service that climbs to 1.2 GB and *plateaus* is not leaking — it found its working set. A service that climbs *without bound* until OOM is leaking. Watch the trend over hours.
2. **Rule out the container limit as the actual bug.** Sometimes the app is healthy but the memory *limit* is set below its legitimate working set, and the GC (in .NET, which is container-limit-aware) is being forced to collect aggressively or the pod is killed prematurely. Check the limit against real steady-state usage before hunting a leak that isn't there.
3. **Buy time in production** with a rolling restart / higher limit, but treat that as triage, not a fix.

### How to FIND it

The workflow is: measure the trend, capture the heap, compare snapshots, follow the retention path.

- **Watch the counters.** `dotnet-counters monitor -p <pid>` shows GC heap size, Gen 0/1/2, LOH, and working set live. Growing Gen 2 + LOH across collections is the fingerprint.
- **Capture heap dumps.** `dotnet-gcdump collect -p <pid>` grabs a GC heap graph cheaply and safely in production. `dotnet-dump` grabs a full process dump for deeper analysis.
- **Compare two snapshots.** This is the key technique: take a gcdump, let the app run under steady load for an hour, take a second. **Diff them.** The types whose instance counts grew are your leak. One growing type name usually points straight at the offending collection.
- **Follow the retention path (dominators).** In Visual Studio's dump analysis, **dotMemory**, or PerfView, look at the **retention/root path**: what chain of references keeps the growing objects alive? That path names the exact static field, event, or cache holding them.

```bash
# Production-safe leak hunt on a .NET process
dotnet-counters monitor -p 1 --counters System.Runtime   # watch GC heap & LOH trend
dotnet-gcdump collect -p 1 -o /tmp/snap1.gcdump          # baseline
# ...wait an hour under normal load...
dotnet-gcdump collect -p 1 -o /tmp/snap2.gcdump          # compare snap1 vs snap2 in VS/dotMemory
```

### The fix — a leaking cache and its repair

The classic offender:

```csharp
// LEAK: unbounded static cache. Every unique key lives forever.
public static class PriceCache
{
    private static readonly ConcurrentDictionary<string, PriceQuote> _cache = new();

    public static PriceQuote Get(string symbol, Func<PriceQuote> load)
        => _cache.GetOrAdd(symbol, _ => load());
    // Nothing is ever removed. Distinct symbols (or worse, per-request keys) grow without bound.
}
```

The fix is a cache with **bounded size and expiry** — do not hand-roll eviction; use `MemoryCache`:

```csharp
public sealed class PriceCache
{
    private readonly IMemoryCache _cache;

    public PriceCache(IMemoryCache cache) => _cache = cache;

    public PriceQuote Get(string symbol, Func<PriceQuote> load)
        => _cache.GetOrCreate(symbol, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);
            entry.Size = 1;                       // requires SizeLimit on the cache
            return load();
        })!;
}

// Registration bounds total entries — eviction is automatic under pressure.
services.AddMemoryCache(o => o.SizeLimit = 10_000);
```

Other fixes by cause:

- **Events:** always pair `+=` with `-=` (unsubscribe in `Dispose`), or use **weak event patterns** where the publisher outlives subscribers.
- **`IDisposable`:** dispose deterministically with `using`; register disposables with the DI container so scope disposal cleans them up.
- **`HttpClient`:** use `IHttpClientFactory` — one correctly pooled handler, no per-request leak, no socket exhaustion.
- **Captive dependencies:** never inject a shorter-lived service into a singleton; inject a factory (`IServiceScopeFactory`) and create a scope per unit of work instead.

### How to prevent it

- **Every cache has a bound and an expiry.** No exceptions. An unbounded cache is a scheduled outage.
- **Enable DI scope validation** (`ValidateScopes = true`, on by default in Development) to catch captive dependencies at startup.
- **Load-test long enough to see the trend** — a 5-minute test never reveals a 6-hour leak. Run a soak test.
- **Alert on the *slope* of memory**, not just a threshold — a steady upward slope over hours is the earliest signal.
- **Set container limits from measured steady state**, with headroom, so the limit protects you without masking or manufacturing a "leak."

> The trap is treating rising memory as automatically a bug. **Distinguish three things:** a genuine leak (unbounded growth to OOM), healthy high-but-stable usage (grows then plateaus — the GC is caching and that is fine), and a mis-set container limit (the app is healthy; the ceiling is wrong). Diagnose which one you have *before* you start changing code.

---

## Scenario 8 — Hardened: the security measures that actually matter

### The scenario

A new service is going to production next week. The security review is a checkbox on someone's ticket, and the team's instinct is to bolt on "security" at the end — an auth middleware here, a firewall rule there. As the senior in the room, you are the person who decides what "secure enough to ship" means. This scenario is not a tutorial (see **Chapter 14** for the deep mechanics); it is the **prioritized list a senior insists on in every project**, and the judgment behind each item.

### The senior's non-negotiables

Frame the whole thing around **defense in depth**: no single control is trusted to be perfect, so you layer them until any single bypass is contained. What earns a place on the list, roughly in priority order:

| # | Control | What a senior insists on | Chapter cross-ref |
|---|---|---|---|
| 1 | **AuthN / AuthZ done right** | Real identity provider (OIDC), tokens fully validated, authorization checked **per resource**, not just per route. | Ch 14 |
| 2 | **Least privilege everywhere** | Minimum permissions for every service, DB user, and cloud role. No shared god-credentials. | Ch 14, 27 |
| 3 | **Secrets management** | **Zero secrets in code or config.** Vault-injected, rotated; scan history for leaked keys. | Ch 14 |
| 4 | **Injection defenses** | Parameterized queries / an ORM — *never* concatenated SQL. Validate and constrain input at the boundary. | Ch 14 |
| 5 | **Output encoding / XSS** | Context-aware encoding on output; `Html.Raw` is a red flag requiring justification. | Ch 14 |
| 6 | **TLS everywhere + HSTS** | HTTPS end to end, including service-to-service. No plaintext hops "because it's the private network." | Ch 10, 14 |
| 7 | **Dependency scanning & patching** | Automated SCA in CI; a transitive package is your attack surface. | Ch 14 |
| 8 | **Rate limiting + auth on *every* endpoint** | No unauthenticated endpoints "nobody knows about"; limits on auth, expensive, and public paths. | Ch 14 |
| 9 | **Security headers / CSP** | CSP, `nosniff`, frame protections — XSS mitigation in depth. | Ch 14 |
| 10 | **Logging / auditing without leaking** | Audit security events; **never log secrets, tokens, passwords, or full PII.** | Ch 9, 27 |
| 11 | **Threat modeling** | "What can go wrong here?" per trust boundary, before building. STRIDE as the checklist. | Ch 14 |
| 12 | **Zero-trust internal services** | Internal calls authenticate too. "Inside the network" is not a trust boundary. | Ch 10 |

### The OWASP Top 10 as a working checklist

Do not treat OWASP as a poster. Treat it as a review checklist you *walk* before shipping — **Chapter 14** covers every category with its .NET mitigation, so the review is a walk, not a study session. Most breaches are boring failures of those basics, not exotic zero-days.

> **Broken access control is consistently the #1 real-world vulnerability**, and the bug is almost never "we forgot auth" — it is "we authenticated the user but didn't check that *this* user owns *this* record." The one test always worth running by hand: can Alice fetch `/orders/{Bob's-id}`?

### Prompt injection — the new item on the list

If your service has an **AI feature** — an LLM summarizing user content, an agent calling tools — **prompt injection** joins the checklist: untrusted input (a user message, a fetched page, a document) can carry instructions that hijack the model. Treat model output as untrusted, never let it trigger privileged actions without validation, constrain tool permissions (least privilege again), and keep a human or a deterministic check before anything destructive. This is injection (item 4) wearing new clothes: *the prompt is now an input boundary.*

### The reasoning a senior brings

- **Security is prioritized, not exhaustive.** You cannot do everything; you do the highest-leverage things first. Access control and secrets management prevent more real breaches than any amount of exotic crypto.
- **Defense in depth means assuming each layer will fail.** Design so that a single bypass is contained.
- **It is cheaper early.** Threat-modeling a design costs an hour; retrofitting authorization into a shipped system costs a quarter.

---

## Scenario 9 — Custody: the special problems of storing user personal data

### The scenario

Your product now stores real people's data: names, emails, addresses, maybe health or payment information. A user emails "delete all my data" and cites GDPR. Legal asks "where does EU customer data physically live?" A junior just added `_logger.LogInformation("User {@User} logged in", user)` — dumping the full user object, PII included, into your log aggregator. Suddenly "just store it in a table" is not enough. Storing personal data is a distinct engineering discipline with its own hazards.

> **This is engineering guidance, not legal advice.** GDPR, CCPA, HIPAA and friends are legal frameworks; how they apply to your product is a question for your legal/privacy team. What follows is how a senior *engineer* translates those constraints into system design. (See **Chapter 28** for the PII/FinOps context.)

### The core concepts

**Chapter 28** covers the discipline in depth — classification (PII/PHI/special-category), data minimization, purpose limitation, consent. The triage-relevant core: you cannot protect, audit, or delete data you have not classified, and the strongest control is **not collecting the field at all**. Every PII field you hold is a liability that can be breached, subpoenaed, or mis-logged.

### Protecting the data at rest

Encryption in transit and at rest (TLS, TDE) is table stakes — and whole-database encryption only protects against stolen disks, not a compromised app. For the genuinely sensitive columns, add **field-level encryption** (keys the database itself doesn't hold) or **tokenization** (real values in a separate vault). The cryptographic mechanics — including why passwords are *hashed* while displayable PII is *encrypted* — are **Chapter 14**'s territory; the decision here is which fields get which treatment.

### The right-to-be-forgotten vs. backups problem

A deletion request seems simple until you remember **backups**: your immutable, 35-day-retention backups contain the user's data, and you (correctly) cannot edit them. The industry's answer is **crypto-shredding** — encrypt each user's PII with a per-user key and destroy the key to "forget" them; the ciphertext left in every table and backup becomes unrecoverable noise (mechanics in **Chapter 28**). Soft delete alone does **not** satisfy erasure — combine it (for referential integrity) with crypto-shred or hard-purge for the actual PII.

| Deletion approach | Satisfies erasure? | Handles backups? | Notes |
|---|---|---|---|
| `DELETE` the row | Live yes, backups no | ✗ | Backups still hold the data |
| Soft delete (`DeletedAt`) | ✗ | ✗ | A UX/integrity tool, not erasure |
| Anonymize in place | Live yes | ✗ | Overwrite PII with nulls/tombstones |
| **Crypto-shredding** | ✓ | ✓ | Destroy the per-user key |

### Retention, access, and audit

**Chapter 28** covers the mechanics — retention/purge jobs, audit trails, pseudonymization vs. anonymization. What matters in the room: unbounded retention is unbounded liability, and when a breach or insider-access question lands, the audit trail of *who* read *whose* PII, *when*, and *why* is the only thing that answers it.

### Data residency and logging pitfalls

- **Data residency.** Some data must physically stay in a region. That is an architecture constraint — regional deployments, region-pinned storage and backups — not a config flag; retrofitting it is a migration (**Chapters 10 and 27**).
- **PII in logs and traces — the everyday leak.** The most common accidental exposure is not a hacker; it is exactly the junior's log line above — a whole user object dumped into an aggregator with weak access controls. **Scrub at the boundary** (Chapter 13's what-not-to-log discipline) and treat logs and traces as PII surfaces subject to the same controls as the database.

### Breach response basics

Have a plan *before* the breach: detect, contain, assess scope (which data, whose), preserve evidence, and know your **notification obligations** — GDPR's tight timelines are exactly why the classification and audit trail above matter. You cannot notify the right people if you don't know what you held or who touched it.

### How to prevent the pain

- **Classify PII fields explicitly** at design time; you cannot protect or delete what you haven't labeled.
- **Minimize collection** — the cheapest, strongest control.
- **Design deletion in from the start** (per-user keys for crypto-shredding), not as a panicked retrofit at the first erasure request.
- **Automate retention/purge and PII-scrub logging** as platform defaults, so every service inherits them.
- **Bring legal/privacy in early** and treat GDPR/CCPA as *engineering requirements* — deletion, portability, consent, residency.

> A senior engineer treats personal data as **radioactive material**: valuable, useful, and dangerous to store. You minimize how much you hold, shield it (encryption, tokenization), track everyone who touches it (audit), plan its disposal (retention + crypto-shredding), and never let it leak into the places you weren't watching (logs, traces, backups). The regulations are just the legal encoding of that engineering discipline.

## Scenario 10 — Poisoned well: a dependency you never chose shipped a backdoor

### The scenario

09:12 on a Tuesday. A security advisory lands: a popular package published two malicious versions during an eleven-hour window two days ago. The maintainer's account was phished; the releases have been yanked. The package is not one you have ever heard of — it is four levels down your dependency graph, pulled in by a logging library you have used for years. The payload harvested environment variables and registry credentials from any machine that *built* against it.

Your CTO wants to know, in the next thirty minutes, whether you are affected.

### Symptoms / how you notice

You almost certainly do not notice this yourself. That is the defining property of the scenario. It arrives as:

- A GitHub Dependabot alert, a vendor advisory, or someone linking a blog post in a chat channel.
- Occasionally: unexplained outbound network connections from a build agent, or a registry token used from an IP you don't recognise.

By the time you know, the window has already closed or not — either way, the clock is on the answer, not the detection.

### Immediate response (stop the bleeding)

1. **Determine exposure, not impact.** The first question is narrow and answerable: *did the malicious version ever resolve in any build?* Grep your committed `packages.lock.json` files across every repository, and across release branches and tags, not just `main`. If you have lockfiles committed, this is one command and a couple of minutes. If you don't, you are re-resolving historical dependency graphs under time pressure — which is the real lesson of this scenario.
2. **Check builds, not just repos.** A lockfile says what *would* resolve. Restore logs and per-release SBOMs say what *did*. Query them for the package and version.
3. **Freeze the automation.** Pause dependency-update bots and any auto-merge, so you don't pull the bad version in *while investigating* it. This has happened to people.
4. **If it executed anywhere, treat every credential that machine could see as compromised.** Registry tokens, cloud credentials, signing keys, SSH keys, environment variables, anything in the runner's memory. Rotate them. Do not reason your way to "it probably didn't reach that one" — a credential harvester takes everything, and the reasoning that says otherwise is exactly what the attacker is relying on.
5. **Purge the caches.** Yanking removes it from the registry, not from your build agents, `~/.nuget/packages`, Docker layer caches, or internal mirrors. A "fixed" build that restores the malicious package from local disk is a common and demoralising outcome.

> **Rule of thumb: exposure is a query if you prepared, and an excavation if you didn't. The controls that make this survivable are all boring, and all installed months in advance.**

### Root causes

- **The graph is deeper than anyone's model of it.** Nobody chose the compromised package; it arrived transitively. Reviewing your direct dependencies would not have caught this.
- **Restore is not inert.** In .NET the execution vector is not an install script — NuGet has none — it is `build/*.props` and `*.targets` files that MSBuild imports, analyzers and source generators that run inside the compiler, and `dotnet tool` packages. You do not have to *call* the package for it to run; you have to *build*.
- **Build agents hold the good credentials.** A CI runner has registry tokens, cloud access, and often signing keys. It is a production machine with a shell exposed to anything in the dependency graph.
- **Floating versions and eager auto-merge** widen the window from "teams that upgraded deliberately" to "everyone who built."

### The fix & architectural options (with trade-offs)

The full treatment is [Chapter 35](#chapter-35-software-supply-chain-security); the incident-relevant subset, in order of value:

| Control | What it buys you in *this* incident | Cost |
|---|---|---|
| Committed lockfiles + `--locked-mode` | Turns exposure analysis into a grep; makes any graph change a reviewable diff | An afternoon; occasional friction on version bumps |
| Update cooldown (ignore releases < 3–7 days old) | You very likely never resolved it at all — most malicious releases are yanked within hours | ~15 minutes of Renovate config; slightly later patches |
| SBOM per release, stored | Answers "which deployed version contains it," including services nobody has touched in a year | A build step and somewhere to put them |
| Short-lived OIDC credentials in CI | Step 4 shrinks from "rotate everything" to "the token expired anyway" | A day of IAM work |
| Egress allowlist on build runners | A successful compromise becomes a failed exfiltration | Ongoing maintenance of the allowlist |
| Ephemeral runners | Malware cannot persist into the next build | Usually free on hosted runners |

**The trade-off worth naming:** a cooldown window means you also receive *security* patches a few days late. For most organizations this is the right trade — you are far likelier to be hit by a malicious release than by a vulnerability exploited within its first 72 hours — but it should be a deliberate decision, and you can exempt advisories you're actively tracking.

### How to prevent the pain

- Commit lockfiles today. It is the single highest-value thing in this scenario and it takes an afternoon.
- Add the cooldown window. Fifteen minutes.
- Reserve your ID prefix on nuget.org and configure `packageSourceMapping` — different attack, same afternoon.
- Write the response runbook *now*, while nothing is on fire, and make step one "grep the lockfiles."
- Rehearse it. A game day (Chapter 21) using a real advisory from last year will find that nobody knows which repos exist, which is the finding.

> **In an interview:** "First I'd scope exposure, not impact — grep committed lockfiles across all repos and branches for the affected version, then check SBOMs and restore logs to see whether it ever reached a build. If it executed on a runner, I'd assume every credential that machine could see is compromised and rotate, rather than reasoning about what the payload probably took. Then purge package and layer caches, because yanking doesn't clear them, and pin forward rather than rolling back — attackers backport. The reason I can answer the first question in minutes is that lockfiles are committed and SBOMs are stored per release; without those, the same incident is a week of archaeology. And the control that would most likely have prevented it entirely is a cooldown window on dependency updates, since these releases are usually pulled within hours."

---

## Scenario 11 — The agent leaked customer data through a tool call

### The scenario

You shipped an AI support assistant six weeks ago. It retrieves from your knowledge base and past tickets, and it has three tools: look up a customer's orders, search documentation, and send a follow-up email.

A customer support lead forwards you something odd: a follow-up email was sent to an address nobody recognises, and it contains a list of order references belonging to *other* customers. There is no bug in your code. The logs show the model called `send_email` with those arguments, and it called `get_orders` before that, and both calls succeeded because both were permitted.

Six weeks ago, someone opened a support ticket whose body contained instructions addressed to the assistant.

### Symptoms / how you notice

- An action the system took that no user requested — an email, an API call, a record change — with a plausible-looking audit trail behind it.
- Outbound requests to hosts that appear nowhere in your configuration (including image URLs rendered in a chat transcript — the browser fetches them, and the query string carries the payload).
- A spike in tool calls per conversation, or the same tool being called with arguments drawn from a different conversation's context.
- Frequently: nothing at all, until a human notices something that doesn't add up. This class of incident is under-detected because every individual step looks like normal operation.

### Immediate response (stop the bleeding)

1. **Disable the outbound tools first, not the assistant.** Killing `send_email` and any HTTP tool stops the exfiltration while leaving a degraded but useful product. Kill switches per tool — not just per feature — need to exist beforehand; if they don't, this is the moment you learn that.
2. **Scope the exposure from your tool-call logs.** Every tool invocation, its arguments, its result, and its conversation ID. This is the only record of what happened; provider logs will not have your arguments and your application logs may not have the model's. If you did not log tool calls with arguments, you cannot answer "whose data left," which is the question legal will ask.
3. **Find the payload.** Work backwards from the malicious tool call to the retrieved context of that turn, then to the source document. Expect it to be old — content-based injections sit in your corpus until something retrieves them.
4. **Purge and re-index.** Remove the poisoned document, then search the corpus for similar patterns; if one attacker seeded one, assume more.
5. **Treat it as a data breach and start that process** — Scenario 9's playbook. Whose data, how much, notification obligations. "An AI did it" changes nothing about the obligation.

> **Rule of thumb: an agent incident is contained by removing capability, not by fixing the prompt. Turn off the outbound tool, then investigate.**

### Root causes

- **The model has one channel.** Your system prompt, the user's message, and a retrieved ticket all arrive as tokens in the same context. There is no parameterization primitive that separates instructions from data — which is why this is not a bug you can fix the way you fix SQL injection.
- **The lethal trifecta was present**: access to private data (`get_orders`), exposure to untrusted content (retrieved tickets, written by anyone), and an egress channel (`send_email`). Any two would have been survivable. All three, in one context, is exploitable by design.
- **The tool authorized against the agent's identity, not the caller's.** `get_orders` ran with a service account that could read every customer, and the intended scoping ("only this customer's orders") lived in the prompt. The prompt is not an authorization boundary.
- **`send_email` accepted a free-form recipient.** An unconstrained egress parameter is an exfiltration API.

### The fix & architectural options (with trade-offs)

| Option | What it does | Trade-off |
|---|---|---|
| **Break the trifecta** — split into two agents: one with private data and no egress, one with egress and only trusted content | Removes the capability entirely; nothing to exploit | More architecture; some flows genuinely need both and must be redesigned |
| **Authorize every tool against the end user's identity, in code** | Holds even when the model is fully compromised — `get_orders` returns only *this* caller's orders | Requires threading caller identity through the agent; a real refactor if you didn't from day one |
| **Constrain egress**: fixed recipient (the ticket's own requester), host allowlist for HTTP, disable image/link rendering in transcripts | Cheapest effective control; kills the exfiltration path without touching the model | Loses some legitimate flexibility |
| **Human confirmation on irreversible actions**, showing the *actual* arguments | Catches what automation misses | Friction; and a confirmation summary written by the model being confirmed is theatre — show raw arguments |
| **Input filtering / injection classifiers on retrieved content** | Catches the obvious attempts | Probabilistic. Useful as a layer, never as the boundary |

**The trade-off worth naming:** the robust fix (splitting the agent, scoping the data tool) is architectural and takes weeks; the cheap fix (constraining the recipient, allowlisting hosts) takes a day and closes *this* hole. Do the cheap one immediately and schedule the architectural one — but be honest in the write-up that the cheap fix removed one exfiltration channel, not the class.

### How to prevent the pain

- **Run the trifecta check as a design review gate** for anything with tools and production data: private data, untrusted content, egress — which leg are we breaking? Three lines in the design doc.
- **Authorize in code, against the caller, on every tool.** Non-negotiable, and cheap if you do it from the start.
- **Log every tool call with arguments, results and conversation ID**, and treat those logs as sensitive data (they contain everything the model saw).
- **Per-tool kill switches**, tested.
- **Budget the loop** — max iterations, max tool calls, wall-clock timeout — so an injected instruction hits a wall.
- Full treatment in Chapter 19's *Securing AI features and agents*.

> **In an interview:** "I'd contain it by disabling the outbound tool rather than the assistant, then scope exposure from tool-call logs — which only works if you logged arguments, so that's a design decision made months earlier. The root cause isn't a bug in the prompt; it's that the agent had all three legs of the lethal trifecta: private data, untrusted content from retrieved tickets, and an egress tool with a free-form recipient. Prompt hardening can't fix that, because instructions and data are the same tokens. The durable fix is architectural — authorize the data tool against the end user's identity in code so it can only ever return that caller's orders, and constrain or remove the egress. And I'd treat it as a data breach from minute one, because it is one."

---

## Scenario 12 — The invisible customer: an AI crawler tripled the egress bill

### The scenario

Finance forwards last month's cloud invoice with a question mark. Egress is up 280%, the CDN bill has doubled, and the database tier was auto-scaled up twice — permanently, because nobody scaled it back. Nothing broke. No alert fired. Availability was 99.98% all month, latency is normal, and the product team reports no user complaints.

Traffic is up roughly 4×. Signups are flat.

### Symptoms / how you notice

- **Cost, weeks late.** This is the defining feature: every technical signal looks healthy, because the system did exactly what you built it to do — it scaled up and served the traffic.
- Request volume rising without any corresponding business metric moving.
- A very high ratio of HTML requests to asset requests (a browser fetches your CSS, fonts and images; a crawler usually doesn't).
- Cache hit ratio falling while traffic rises — the traffic is walking your entire URL space rather than clustering on popular pages.
- Requests spread evenly across the whole sitemap, including pages no human has visited in a year.

### Immediate response (stop the bleeding)

1. **Characterise the traffic before you block anything.** Group by user agent, ASN, and IP prefix; compare asset-to-HTML ratio and URL breadth against a known-human baseline. Rushing to block is how you de-index yourself from a search engine that sends you real customers.
2. **Cache harder at the edge, immediately.** For unidentified clients this is usually a one-line CDN change with a large effect and no risk of blocking a legitimate user. An origin that never sees the request costs nothing to serve.
3. **Fix the cache key if it's leaking.** If the traffic carries tracking or random query parameters and your CDN varies on them, every request is a miss and your CDN is faithfully forwarding all of it to your origin. Strip unknown parameters at the edge — this alone often resolves the incident.
4. **Rate limit unidentified clients at the CDN**, keyed on IPv6 **/64 prefix** rather than individual address (a single subscriber may hold billions of addresses).
5. **Scale the database back down** once origin load drops, and check for other resources that auto-scaled up and stayed there. This is frequently the largest line item and the easiest to forget.

> **Rule of thumb: an availability-preserving cost event has no alert unless you built one. Alert on rate of spend, not on monthly budget — a monthly alarm tells you about last night four weeks late.**

### Root causes

- **Automated traffic is roughly half the web now**, and a growing share is AI-related — training crawlers, retrieval fetchers answering a user's question, and agents browsing on someone's behalf. Sites with substantial text — docs, catalogs, listings — are exactly the target.
- **`robots.txt` is a request, not a control**, and compliance is inconsistent; some operators honour it for their crawler but not their retrieval fetcher.
- **Elastic infrastructure converts a capacity attack into a billing event.** The system stays up and you pay. Nothing pages.
- **The cost alerting was monthly and absolute**, so a 4× traffic change took four weeks to surface.
- Often: **a cache key including a client-controlled parameter**, turning every request into an origin hit.

### The fix & architectural options (with trade-offs)

| Option | Effect | Trade-off |
|---|---|---|
| **Aggressive edge caching for unauthenticated traffic** | Largest single win; origin cost approaches zero | Staleness; needs a real invalidation story |
| **Verified crawler allowlist** (reverse-DNS or published IP ranges) + challenge everything else | Keeps the crawlers that send you customers, prices the rest | Verification is per-operator work; needs maintenance |
| **Proof-of-work / challenge interstitial** for unidentified clients | Inverts the economics — cheap for one human, expensive across a million pages | Some accessibility and UX cost; can affect legitimate automation |
| **Serve a cheap representation to bots** (static, pre-rendered, no personalization) | Removes the database from the path entirely | Two rendering paths to maintain |
| **Block outright by ASN/UA** | Immediate | Brittle, easily evaded, and risks de-indexing — least favourite, most reached for |

**The strategic trade-off nobody frames explicitly:** some of this traffic may be *valuable*. A retrieval bot fetching your docs to answer a user's question may be sending you customers who never see a search result page. Decide your policy deliberately — which bots you want, which you're indifferent to, which are pure cost — before you tune the controls. That is a product decision, not an infrastructure one.

### How to prevent the pain

- **Alert on cost velocity** — spend per hour, or a day-over-day delta on egress and request volume — not on a monthly budget threshold.
- **Put a business metric next to the traffic metric** on the same dashboard. Requests up, signups flat, is the shape of this incident and it is instantly readable.
- **Make autoscaling scale down** and alert when a floor changes. Ratchets that only go up are a recurring, silent cost.
- **Normalize cache keys at the edge** and strip unknown query parameters, as a standing rule.
- **Know your egress paths** (Chapter 28) — the same inventory that serves FinOps answers this in minutes.

> **In an interview:** "The first thing I'd flag is that this is a cost incident with no availability signal, so the real failure is in the alerting — a monthly budget alarm surfaces it four weeks late, where a spend-velocity alert surfaces it the same day. Technically I'd characterise before blocking: user agent, ASN, asset-to-HTML ratio, URL breadth. Then the cheap high-value moves are edge caching for unauthenticated traffic and fixing any cache key that varies on a client-controlled parameter, which is often the whole problem. Blocking by user agent is the thing everyone reaches for and the least effective, since it's a string the client chooses — and it risks de-indexing you. Longer term it's a policy question, not just an engineering one: which crawlers do we actually want, given some of them send us customers?"

---

---

## Sources & Further Reading

*A note on Scenario 9:* the material on GDPR/CCPA/HIPAA is engineering guidance, **not legal advice** — consult your legal/privacy team for how these frameworks apply to your product.

**Scaling, reliability & the runtime (Scenarios 1–4)**
- Microsoft Learn — *.NET garbage collection fundamentals*, Server vs. Workstation GC, and DATAS; ASP.NET Core rate limiting, health checks, and `Microsoft.Extensions.Resilience` / Polly integration.
- Azure Architecture Center — *Cloud design patterns* (Transactional Outbox, Saga, Circuit Breaker, Bulkhead, Queue-Based Load Leveling, Rate Limiting, Health Endpoint Monitoring): https://learn.microsoft.com/azure/architecture/patterns/
- Azure & AWS Well-Architected Frameworks — Reliability and Performance Efficiency pillars; AWS Architecture Blog, *"Exponential Backoff And Jitter."*
- *"Release It!"* (2nd ed.), Michael Nygard — circuit breakers, bulkheads, timeouts, and stability patterns.
- Polly documentation — resilience strategies (retry, circuit breaker, timeout, hedging): https://www.pollydocs.org/

**Backups & disaster recovery**
- Microsoft Learn — *Automated backups and point-in-time restore in Azure SQL Database* (retention 1–35 days, PITR, long-term retention).
- Microsoft Learn — *SQL Server backup types* (full, differential, transaction log) and *Restore and recovery overview*.
- AWS Documentation — *Working with backups in Amazon RDS* (automated backups, PITR, snapshots, up to 35-day retention).
- PostgreSQL Documentation — *Continuous Archiving and Point-in-Time Recovery (PITR)* (WAL archiving, base backups); pgBackRest and Barman project docs.
- The **3-2-1 backup rule** — US-CERT / CISA guidance and industry practice (3 copies, 2 media, 1 offsite); "3-2-1-1-0" immutability extension.

**Polyglot integration**
- gRPC / Protocol Buffers official docs — `protobuf.dev` (proto3, field-number evolution rules) and `grpc.io`.
- Dapr Documentation — `docs.dapr.io` (building blocks: service invocation, pub/sub, state, secrets).
- OpenTelemetry Documentation — `opentelemetry.io` (multi-language SDKs, W3C Trace Context propagation).
- Martin Kleppmann — *Designing Data-Intensive Applications* (schema evolution, encoding, the shared-database anti-pattern, dataflow between services).

**Memory leaks & diagnostics**
- Microsoft Learn — *Diagnosing memory leaks in .NET* and the `dotnet-counters`, `dotnet-gcdump`, `dotnet-dump` tooling guides.
- Microsoft Learn — *Memory and span usage*, `IMemoryCache`, and Dependency Injection guidelines (service lifetimes, captive dependencies, scope validation).

**Security**
- OWASP — *Top 10 Web Application Security Risks* (`owasp.org/Top10`) and the OWASP Cheat Sheet Series.
- OWASP — *LLM Top 10 / prompt injection* guidance.
- Microsoft Learn — *ASP.NET Core security* (authentication, authorization, data protection, rate limiting, security headers).

**Supply chain, AI agents & abuse (Scenarios 10–12)**
- Chapter 35 of this book, and its sources — NuGet package source mapping and lockfiles, SLSA, Sigstore, SBOM formats, and the CRA timeline.
- OWASP — *Top 10 for LLM Applications* (prompt injection, excessive agency, sensitive information disclosure, unbounded consumption).
- Simon Willison's writing on the **lethal trifecta** (private data + untrusted content + exfiltration) — the clearest statement of why agent exfiltration is a capability problem rather than a prompting one.
- Cloudflare Radar and similar public traffic reports — the automated-versus-human traffic mix and AI crawler behaviour.

**Personal data / privacy engineering**
- EUR-Lex — *Regulation (EU) 2016/679 (GDPR)*, in particular Art. 5 (principles), Art. 17 (right to erasure), Art. 25 (data protection by design), Art. 32 (security of processing).
- California Civil Code — *CCPA / CPRA* (State of California, `oag.ca.gov/privacy/ccpa`).
- OWASP — *Cryptographic Storage* and *Password Storage* Cheat Sheets (hashing vs. encryption, Argon2/bcrypt/PBKDF2).
- NIST — *SP 800-122, Guide to Protecting the Confidentiality of PII*.
