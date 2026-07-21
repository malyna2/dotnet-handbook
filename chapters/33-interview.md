# Chapter 33: Interview Questions & How to Answer Them

_⏱️ Estimated read time: ~34 min ·     6706 words (study pace)_

This chapter is a recall-and-rehearse bank. Every topic here is taught in depth earlier in the book; the goal now is to turn that knowledge into crisp spoken answers under pressure. Read a question, cover the answer, and say your version out loud. If it comes out rambling, tighten it. Each section starts with a *Revise* pointer to the chapter(s) that teach the material. **Red flag** lines show the wrong answer interviewers hear from juniors — if your spoken version sounds like one, go back and re-read.

**Interview strategy in five habits:**

1. **Clarify before you answer.** A ten-second "Do you mean X or Y?" beats two minutes solving the wrong problem. Interviewers score you on scoping, not mind-reading.
2. **Think out loud.** Silence reads as "stuck." Narrate your reasoning even when you're confident — it lets the interviewer follow, hint, and give partial credit.
3. **Structure the answer.** Lead with the one-sentence conclusion, then support it. "Use `ValueTask` when the result is usually synchronous — here's why…" is stronger than building to a mystery reveal.
4. **Admit unknowns cleanly.** "I haven't used that, but I'd expect it works like X because…" shows honesty plus reasoning. Bluffing is the fastest way to fail a senior loop.
5. **For behavioral questions, use STAR** — Situation, Task, Action, Result. Keep Situation short, spend your words on *your* Action, and always land a measurable Result.

---

## How to Approach Any Interview

*Revise: Ch. 17 — Soft Skills & Engineering Practices*

**How do you handle a question you don't know the answer to?**
State what you do know, reason from first principles toward a plausible answer, and be explicit about the boundary: "I know GC has generations; I'm less sure of the exact LOH threshold, but I'd reason it's large because compaction is expensive." That earns more than silence or a confident wrong guess.

**A candidate says "it depends" — is that a good answer?**
Only if you then say *what* it depends on and pick a default. "It depends on read/write ratio: read-heavy, I'd cache; write-heavy, I'd skip the cache to avoid invalidation pain." Naming the trade-off axis is the senior signal.

**How do you show seniority beyond just knowing facts?**
Talk about trade-offs, failure modes, operability, and cost — not just the happy path. Juniors describe how a thing works; seniors describe when *not* to use it and what breaks it at 3 a.m.

---

## Diagnosing a Performance Problem (a worked methodology)

*Revise: Ch. 15 — Performance & Optimization · Ch. 13 — Observability*

This is a flagship section because "the app is slow, what do you do?" is asked in almost every senior loop. Recite this as a repeatable method, not a grab-bag of tricks.

**Walk me through how you diagnose a slow endpoint.**
1. **Reproduce and quantify first.** "Slow" is not a number. Get a percentile (p95/p99 latency), a throughput figure, and the conditions (which endpoint, which payload, under what load). Never optimize on a vibe.
2. **Measure before you guess.** The cardinal rule: *measure, don't guess.* The bottleneck is almost never where intuition points. Establish a baseline metric so you can prove any fix actually helped.
3. **Classify the bottleneck.** Decide which resource is saturated: CPU, memory/GC, disk I/O, network, database, or lock contention. Each has a different toolset and fix.
4. **Go from cheap metrics to expensive profilers.** Start with always-on signals (APM dashboards, `dotnet-counters` for CPU/GC/thread-pool/request rate), then reach for `dotnet-trace` (CPU sampling), `dotnet-dump` (heap/leaks), and DB query plans only once you've narrowed the suspect.
5. **Find the bottleneck, fix one thing, verify.** Change a single variable, re-measure against the baseline, and confirm the win before moving on. Then repeat.

**Red flag:** "I'd add caching and make everything async" — naming fixes before measuring anything is optimizing on a guess.

**How do you tell if it's CPU-bound vs waiting?**
Check CPU utilization while the endpoint is slow. High CPU with low throughput → CPU-bound (hot loop, serialization, regex, crypto). Low CPU but high latency → you're *waiting* (DB, downstream HTTP, lock, exhausted thread pool). `dotnet-counters` showing a growing thread-pool queue with idle CPU is the classic sync-over-async / thread-starvation fingerprint.

**Which tools, concretely, in a .NET app?**
- `dotnet-counters monitor` — live CPU %, GC gen counts, allocation rate, thread-pool queue length, requests/sec. First stop, zero setup.
- `dotnet-trace` — sampled CPU profile to find hot methods without a full profiler.
- `dotnet-dump` / `dotnet-gcdump` — heap snapshot for leaks and retention analysis.
- APM (Application Insights, OpenTelemetry, Datadog) — distributed traces to see *which hop* in a request eats the time.
- DB: `EXPLAIN`/`EXPLAIN ANALYZE` (Postgres), actual execution plan (SQL Server), and the slow-query log.

**What are the usual culprits you look for?**
- **N+1 queries** — a loop issuing one query per row. Fix with a join / `Include` / batched load.
- **Missing index** — a seek turned into a full scan; the query plan shows it instantly.
- **Sync-over-async** (`.Result`, `.Wait()`) — starves the thread pool, tanks throughput under load.
- **Excess allocations / GC pressure** — high gen-0 rate and frequent gen-2 collections; fix with pooling, `Span`, fewer LINQ allocations in hot paths.
- **Chatty network calls** — many small serial round-trips; batch or parallelize them.
- **No caching** — recomputing or re-fetching identical results every request.

**Why "measure, don't guess" so emphatically?**
Because developers reliably optimize the wrong thing. The 20% of code you assume is hot is usually cheap, while the real cost hides in a serializer, a logging call, or a chatty ORM. A profiler removes ego from the decision and gives you a number to defend the fix.

> **Follow-up:** *The p99 is bad but p50 is fine — what does that tell you?* Something intermittent: GC pauses, lock contention, a cold cache, a slow downstream that only some requests hit, or connection-pool exhaustion under burst. Median-fine/tail-bad points at contention or resource limits, not raw algorithmic cost.

**The DB is the bottleneck — now what?**
Pull the execution plan for the slow query. Look for scans that should be seeks (missing/unusable index), bad join order from stale statistics, or a query returning far more rows than needed. Then consider indexing, query rewrite, pagination, caching, or read replicas — in that order of cheapness.

---

## C# Language

*Revise: Ch. 1 — C# Language Mastery*

**Value type vs reference type — the practical difference?**
Value types (`struct`, `int`, `enum`) hold their data inline and are copied on assignment; reference types (`class`, arrays, `string`) hold a reference to heap data, so assignment copies the pointer, not the object. Value types typically live on the stack or inline within their container; reference types live on the heap. This drives copy semantics, equality defaults, and allocation behavior.

**Red flag:** "Value types live on the stack, reference types on the heap" stated as an absolute — a struct field inside a class lives on the heap; the real difference is copy semantics.

**What is boxing and why does it cost?**
Boxing wraps a value type in a heap object so it can be treated as `object` or an interface reference; unboxing extracts it back. It costs a heap allocation plus a copy, and adds GC pressure in hot paths. Generics and `Span` largely eliminate the need. (See the Runtime & Memory chapter.)

**Why are strings immutable, and what's the consequence?**
A `string`'s contents never change; "modifying" one produces a new string. This makes strings safe to share and hash, and safe as dictionary keys, but naive concatenation in a loop allocates repeatedly — use `StringBuilder` or `string.Create`/interpolation for hot paths.

**`ref` vs `out`?**
Both pass by reference. `ref` requires the variable to be initialized *before* the call and the method may read it; `out` requires the method to assign it before returning and the caller need not initialize it. Use `out` for "return extra values" (`TryParse`), `ref` for "read and modify in place."

**`IEnumerable<T>` vs `IQueryable<T>`?**
`IEnumerable` executes in memory with LINQ-to-Objects — the whole sequence is pulled and filtered client-side. `IQueryable` builds an expression tree that a provider (EF Core) translates to SQL, so filtering happens in the database. Accidentally forcing `IQueryable` to `IEnumerable` early (e.g. calling `.ToList()` then `.Where()`) drags the whole table into memory.

**What is deferred execution?**
LINQ query operators don't run when defined — they run when enumerated (`foreach`, `ToList`, `Count`). The query captures variables by reference, so results reflect state at *enumeration* time, and enumerating twice runs it twice. Materialize with `ToList()` when you need a stable snapshot or to avoid re-querying.

**Delegates vs events?**
A delegate is a type-safe function pointer you can invoke and reassign. An `event` is a restricted wrapper over a delegate that only lets external code subscribe (`+=`) and unsubscribe (`-=`) — they can't invoke it or clear other subscribers. Events are the safe public surface for the observer pattern.

**What does a closure capture — the value or the variable?**
The variable, not its value at capture time. This bites in loops:

```csharp
var actions = new List<Action>();
for (int i = 0; i < 3; i++)
    actions.Add(() => Console.Write(i));
foreach (var a in actions) a();   // prints 333 (pre-C# 5 foreach) — here: 333
```

Each lambda closes over the *same* `i`, so all print its final value, `3`. Fix by copying into a loop-local: `int copy = i;` and capture `copy`. (Note: `foreach` variables are per-iteration since C# 5, but classic `for` loops still share the counter.)

**Red flag:** "The lambda captures the value of `i` at that moment" — it captures the variable, so every lambda sees the final value.

**Struct vs class — when do you reach for a struct?**
Use a `struct` for small (~16 bytes or less), immutable, value-semantic data that's short-lived, to avoid heap allocation — e.g. a `Point` or a `Money`. Use a `class` for anything with identity, large state, inheritance, or reference-sharing needs. Big mutable structs are a trap: they copy on every pass and cause subtle bugs.

**What do records give you?**
`record` types generate value-based equality, `ToString`, a deconstructor, and `with`-expression non-destructive copying. `record` is a reference type; `record struct` is a value type. Reach for them for immutable DTOs and domain values where "two instances with the same data are equal" is the semantics you want.

**What is `Span<T>` for?**
`Span<T>` is a stack-only view over contiguous memory — an array slice, a string slice, or stack/native memory — that lets you slice and process without allocating or copying. It's a `ref struct`, so it can't be boxed, stored on the heap, or used across `await`. Ideal for parsing and buffer work in hot paths. Use `Memory<T>` when you need the same idea across async boundaries.

**`IDisposable` and `using` — what problem do they solve?**
Deterministic cleanup of unmanaged or expensive resources (file handles, sockets, DB connections) at a known point, rather than waiting for the GC/finalizer. `using` guarantees `Dispose()` runs even on exception. Use `await using` with `IAsyncDisposable` for resources with async teardown.

**What actually happens on `await`?**
The compiler rewrites the method into a state machine. At an `await`, if the awaited task isn't complete, the method *returns* to its caller and registers a continuation; when the task finishes, the continuation resumes the method (by default back on the captured context). It's not a thread — no thread is blocked while awaiting truly async I/O. (See the Async chapter.)

**Red flag:** "`await` runs the method on a new background thread" — no thread is consumed at all during an awaited I/O wait.

> **Follow-up:** *Does `await` create a new thread?* No. For I/O it uses an I/O completion callback and no thread is consumed during the wait. A new thread only appears if you explicitly offload with `Task.Run`.

---

## .NET Runtime, GC & Memory

*Revise: Ch. 2 — .NET Runtime & Internals*

**How does the GC work, and what are generations?**
It's a tracing, generational, mark-and-sweep collector. Objects start in **gen 0**; survivors are promoted to **gen 1**, then **gen 2** (long-lived). Collections are generational because most objects die young — collecting gen 0 frequently and gen 2 rarely is cheap and effective. After a collection the heap is compacted to reduce fragmentation.

**Red flag:** "If memory is high, call `GC.Collect()`" — forcing collections fights the generational design and hides whatever is rooting the objects.

**What is the Large Object Heap?**
Objects ≥ 85,000 bytes go on the LOH, collected as part of gen 2. It isn't compacted by default (compaction of big blocks is expensive), so it can fragment. Frequent large allocations — big arrays, large buffers — are a common source of memory bloat; pool or reuse them.

**Server GC vs Workstation GC?**
Workstation GC is tuned for low latency on client apps: fewer heaps, runs on the app thread. Server GC uses one managed heap and GC thread per core for higher throughput, at the cost of more memory — the default for ASP.NET Core on multi-core servers. Pick server GC for throughput-oriented services, workstation for memory-constrained or latency-sensitive desktop scenarios.

**Managed vs unmanaged memory?**
Managed memory is the GC-tracked heap for .NET objects. Unmanaged memory is everything the GC doesn't know about — native handles, OS resources, `Marshal.AllocHGlobal`, interop buffers. Unmanaged resources need explicit release via `IDisposable`/finalizers because the GC won't reclaim them for you.

**Finalizers vs `IDisposable` — when each?**
`IDisposable.Dispose()` is deterministic cleanup you call (via `using`). A finalizer (`~T()`) is a GC-invoked safety net for unmanaged resources when someone forgets to dispose. Finalizers hurt: they delay reclamation (object survives an extra GC) and run on a single finalizer thread. Prefer `SafeHandle`/`IDisposable`; add a finalizer only when you directly hold unmanaged resources, and suppress it in `Dispose` via `GC.SuppressFinalize`.

**What causes a managed memory leak if the GC collects everything?**
Unintended references keeping objects alive: static collections that grow forever, event handlers never unsubscribed (subscriber pinned by publisher), captured closures, long-lived caches without eviction, and `IDisposable` objects never disposed. The GC can't collect what's still reachable.

**Red flag:** ".NET has a GC, so memory leaks aren't possible" — reachable-but-unwanted objects (static lists, event subscriptions) leak just fine.

**How do you find a leak in production?**
Watch the trend first — `dotnet-counters` or APM showing managed heap climbing without plateau. Then capture two heap snapshots over time (`dotnet-gcdump`), diff them to see which types are growing, and inspect the retention path (who holds the reference). The growing type plus its GC root usually names the bug.

**What's the real cost of boxing in a hot path?**
Each box is a heap allocation and a copy, feeding gen-0 GC. In a tight loop that turns into millions of tiny allocations and constant collections, which shows up as high allocation rate and GC time in counters. Avoid with generics, `Span`, and by not using non-generic collections like `ArrayList`.

---

## Async & Concurrency

*Revise: Ch. 8 — Asynchronous & Concurrent Programming*

**Async vs multithreading — what's the difference?**
Multithreading uses multiple threads to do work in parallel (CPU-bound). Async is about *not blocking* a thread while waiting for something else (I/O-bound) — one thread can serve many in-flight operations. Async ≠ parallel: `await` on a single call is still sequential; you get concurrency by starting multiple tasks before awaiting.

**Red flag:** "Async makes the code faster because it runs in parallel" — a single awaited call is just as slow; async buys scalability, not speed.

**`Task` vs `ValueTask` — when `ValueTask`?**
`Task` is a heap-allocated reference type; every async call allocates one. `ValueTask` avoids that allocation when the result is *often already available* synchronously (cache hits, buffered reads). Use it in hot, high-frequency APIs where most calls complete synchronously. Don't await a `ValueTask` twice or store it — it's single-consumption.

**What does `ConfigureAwait(false)` do and where?**
It tells the continuation not to resume on the captured synchronization context, resuming on a thread-pool thread instead. Use it in library code to avoid deadlocks and unnecessary context hops. In ASP.NET Core there's no sync context, so it matters less there, but it's still good hygiene for reusable libraries.

**Why does `.Result` deadlock?**
On a platform with a single-threaded sync context (classic UI, legacy ASP.NET), blocking on `.Result`/`.Wait()` holds that thread while the awaited continuation is queued to run *on the same thread* — mutual wait, deadlock. The fix is to be async all the way down and never block on async code. ASP.NET Core lacks that context so it deadlocks less, but sync-over-async still starves the thread pool.

**Red flag:** "Wrap it in `Task.Run(...).Result` to make it safe" — that just burns an extra thread; the fix is async all the way down.

**What is a `CancellationToken` for?**
Cooperative cancellation. You pass a token through async calls; a caller can request cancellation (timeout, user abort, request aborted), and well-behaved methods check `IsCancellationRequested` / pass the token onward, throwing `OperationCanceledException`. Always thread the token through to DB and HTTP calls so work actually stops.

**Red flag:** "Cancelling the token stops the operation immediately" — cancellation is cooperative; nothing stops unless the code observes the token.

**How do you make a class thread-safe?**
Options in rough order of preference: make it immutable (no shared mutable state, nothing to protect); confine mutation to one thread; use concurrent collections (`ConcurrentDictionary`); or guard shared state with a `lock`. Keep locked regions tiny, never `await` inside a `lock`, and always lock on a private dedicated object.

**`lock` vs `Interlocked`?**
`lock` (Monitor) gives mutual exclusion over a block of code — use it for multi-step invariants. `Interlocked` performs a single atomic operation (increment, compare-exchange) without a lock, which is far cheaper for a lone counter or flag. Reach for `Interlocked` when you're protecting one variable, `lock` when you're protecting an invariant across several.

**What is `IAsyncEnumerable<T>` for?**
Asynchronous streaming — `await foreach` over items produced with latency (paged API results, a query streamed row-by-row) without buffering the whole set in memory. It combines deferred, pull-based enumeration with async I/O, so you can start processing the first items before the last arrive.

> **Follow-up:** *You have 100 independent HTTP calls to make — how?* Start them all (`Select(x => CallAsync(x))`) and `await Task.WhenAll`, ideally with a `SemaphoreSlim` to cap concurrency so you don't exhaust sockets or hammer the downstream.

---

## ASP.NET Core & Web

*Revise: Ch. 3 — ASP.NET Core & Web APIs · Ch. 19 — Networking & Web Fundamentals*

**Explain the middleware pipeline.**
Middleware components form a chain; each gets the `HttpContext`, can act on the request, call `next()` to pass control down, and act on the response on the way back out — like nested layers. Order matters: exception handling first, then routing, auth (authentication before authorization), then endpoints. A component can short-circuit by not calling `next()`.

**DI lifetimes — the three, and the trap?**
**Singleton** (one instance for the app), **Scoped** (one per request), **Transient** (a new one each time). The trap is the **captive dependency**: injecting a Scoped (or Transient) service into a Singleton captures it for the app's lifetime, so a per-request service like `DbContext` leaks across requests and breaks. Never inject shorter-lived into longer-lived.

**Red flag:** "Make everything singleton, it's faster" — a captured `DbContext` then leaks across requests; that's a correctness bug, not an optimization.

**How does model binding work?**
ASP.NET Core maps incoming request data — route values, query string, form fields, JSON body, headers — onto action parameters and model properties by name, then runs validation attributes. You steer the source with `[FromBody]`, `[FromQuery]`, `[FromRoute]`, etc. Binding failures populate `ModelState`, which you check before acting.

**What are filters, and when over middleware?**
Filters run within the MVC action pipeline (authorization, resource, action, exception, result filters) and have access to MVC context like the action and model state. Use a filter for cross-cutting concerns that need MVC context — validation, action-level auth, result shaping. Use middleware for concerns that apply to *all* requests regardless of MVC — logging, compression, global exception handling.

**Minimal APIs vs controllers?**
Minimal APIs are lightweight endpoint definitions with less ceremony — great for small services and microservices. Controllers give more structure: attribute conventions, filters, model binding features, and familiar organization for large apps. Both share the same underlying routing and DI; pick by team size and app complexity, not performance.

**JWT vs cookie auth?**
Cookies are stateful-ish, browser-managed, sent automatically, and easy to revoke server-side — good for classic web apps (guard against CSRF). JWTs are self-contained bearer tokens carried in the `Authorization` header — stateless and ideal for APIs and cross-service auth, but hard to revoke before expiry, so keep them short-lived and pair with refresh tokens.

**Red flag:** "JWTs are secure because the payload is encrypted" — it's only Base64-encoded and *signed*; anyone can read the claims.

**REST: which status codes and idempotency?**
200 OK, 201 Created (with `Location`), 204 No Content, 400 Bad Request, 401 Unauthorized, 403 Forbidden, 404 Not Found, 409 Conflict, 422 Unprocessable, 500 Server Error. `GET`, `PUT`, `DELETE` are idempotent (repeating them yields the same state); `POST` is not. Idempotency matters for retries — clients retry on network failure, so unsafe non-idempotent operations need an idempotency key.

**What is CORS and why does it block things?**
CORS is a browser security mechanism: a page on origin A calling an API on origin B is blocked unless the API returns headers explicitly allowing origin A. It's enforced by the browser, not the server — so it protects users, and it's why your JS gets a CORS error while `curl` works fine. Configure allowed origins/methods/headers server-side; avoid `AllowAnyOrigin` with credentials.

**Red flag:** "CORS is server-side security that stops attackers calling the API" — it's a browser protection for users; any non-browser client bypasses it entirely.

---

## Entity Framework & Databases

*Revise: Ch. 4 — Data Access & Databases*

**What is change tracking?**
EF Core's `DbContext` snapshots loaded entities and tracks their state (Added/Modified/Deleted/Unchanged). On `SaveChanges` it generates the SQL for exactly the changes. It's convenient but costs memory and CPU proportional to tracked entities — a reason to disable it for read-only queries.

**When and why `AsNoTracking`?**
For read-only queries you won't update. It skips building the change-tracking snapshot, so it's faster and lighter. Use it for list/reporting/GET endpoints; keep tracking for the read-modify-save flow.

**What is the N+1 problem in EF?**
One query loads N parents, then accessing a navigation property fires one query per parent — N+1 round-trips. Caused by lazy loading in a loop or projecting navigations without including them. Fix with eager loading (`Include`), a projection (`Select`) that joins, or a split query — turning N+1 into 1 or 2 queries.

**Red flag:** "Make the loop parallel/async so the queries run faster" — parallel N+1 is still N+1 round-trips; the fix is fewer queries, not faster loops.

**Lazy vs eager vs explicit loading?**
**Eager** (`Include`) loads related data up front in the query. **Lazy** loads it on first access to the navigation (convenient, but the N+1 footgun). **Explicit** (`Load()`) loads related data on demand by an explicit call. Prefer eager or projection for predictable query counts; be wary of lazy loading in hot paths.

**Transactions and isolation levels — name them.**
From weakest to strongest: **Read Uncommitted** (dirty reads), **Read Committed** (default in many DBs; no dirty reads), **Repeatable Read** (no non-repeatable reads), **Serializable** (full isolation, no phantoms). Higher isolation means more locking/contention. Choose the weakest level that preserves correctness for the operation; snapshot isolation (MVCC) reduces reader-writer blocking.

**Clustered vs non-clustered index?**
A **clustered** index defines the physical row order of the table — one per table, usually the primary key. A **non-clustered** index is a separate structure with pointers back to the rows — many allowed. Clustered is great for range scans on the key; non-clustered covers other lookup columns. A "covering" index includes all columns a query needs so it never touches the table.

**When does an index hurt?**
Every index must be maintained on insert/update/delete and consumes storage, so over-indexing slows writes. Very low-cardinality columns (a boolean) rarely benefit. Index the columns you filter, join, and sort on — measure with query plans rather than indexing everything.

**Red flag:** "Indexes only help, so index every column" — every index is maintained on every write, taxing inserts and updates.

**What causes a database deadlock and how do you avoid it?**
Two transactions each hold a lock the other needs, in opposing order. Avoid by acquiring locks in a consistent order everywhere, keeping transactions short, using the lowest workable isolation level, and adding retry logic for the deadlock-victim error. Deadlocks are a design/ordering issue, not just bad luck.

**Optimistic vs pessimistic concurrency?**
**Optimistic**: assume conflicts are rare, don't lock; detect a conflict at save time via a version/rowversion column and retry if someone else changed the row. **Pessimistic**: lock the row on read so no one else can touch it until you're done. Optimistic scales better and is the default for web apps; pessimistic suits short, high-contention critical sections.

**When would you drop EF Core for Dapper?**
When you need tight control over SQL and maximum read performance — hot query paths, complex hand-tuned queries, bulk reads — and don't need change tracking or migrations. Many teams use both: EF for the write model and CRUD, Dapper for performance-critical reads. It's a per-query decision, not religion.

**What does ACID stand for?**
**Atomicity** (all-or-nothing), **Consistency** (valid state to valid state, constraints hold), **Isolation** (concurrent transactions don't corrupt each other), **Durability** (committed data survives crashes). Relational DBs give you these; distributed systems often trade some away.

**What is normalization and when do you denormalize?**
Normalization organizes data to eliminate redundancy (each fact stored once) — reduces update anomalies. You denormalize deliberately for read performance: duplicate or pre-join data to avoid expensive joins on hot read paths, accepting the cost of keeping copies in sync. Normalize by default, denormalize with evidence.

---

## Architecture & Design

*Revise: Ch. 5 — Design Patterns, Principles & Clean Code · Ch. 6 — Architecture & Application Design · Ch. 9 — Messaging & Distributed Systems (outbox, saga)*

**Explain SOLID with a one-liner each.**
- **S**RP — a class has one reason to change (split the class that both formats *and* saves a report).
- **O**CP — open to extension, closed to modification (add a new payment type via a new class, not by editing a `switch`).
- **L**SP — subtypes must be substitutable for their base without breaking callers (the `Square : Rectangle` trap).
- **I**SP — many small interfaces beat one fat one (don't force implementers to stub methods they don't use).
- **D**IP — depend on abstractions, not concretions (inject `IEmailSender`, not `SmtpClient`).

**DI vs IoC — are they the same?**
IoC (Inversion of Control) is the broad principle: the framework controls flow and creation, not your code. Dependency Injection is one specific way to apply it — supplying a class's dependencies from outside rather than having it `new` them. DI enables testability and swapping implementations.

**Is the repository pattern still worth it over EF Core?**
Contested. The argument *against*: `DbContext` is already a Unit of Work and `DbSet` is already a repository, so wrapping it adds a leaky abstraction. The argument *for*: a repository can centralize query logic, keep the domain persistence-ignorant, and simplify testing. Senior answer: don't add a generic repository reflexively; add task-specific repositories when they earn their keep, otherwise use EF directly.

**Red flag:** "Always wrap EF in a generic repository — it's best practice" — `DbContext` already is a unit of work and repository; the reflexive wrapper is a leaky layer.

**What is CQRS and when do you use it?**
Command Query Responsibility Segregation splits the write model (commands that change state) from the read model (queries), often with different shapes and even different stores. Use it when read and write workloads diverge sharply or you want optimized read projections. It adds complexity — don't apply it to simple CRUD.

**What is an aggregate in DDD?**
A cluster of domain objects treated as one consistency boundary, with a single **aggregate root** as the only entry point. Invariants hold within the aggregate, and you load/save it as a unit. Rule of thumb: keep aggregates small, reference other aggregates by ID, and enforce cross-aggregate consistency asynchronously.

**Microservices vs monolith — the trade-off?**
Monolith: simplest to build, deploy, and debug; one codebase, in-process calls, easy transactions — but scales and evolves as one unit. Microservices: independent deploy/scale/tech per service and team autonomy — but you pay with network latency, distributed transactions, operational complexity, and harder debugging. Most teams should start with a well-structured monolith.

**Red flag:** "Microservices are the modern way; monoliths are legacy" — splitting without a clear need yields a distributed monolith.

**Coupling and cohesion — define and relate.**
Cohesion is how focused a module is on a single responsibility (high is good). Coupling is how dependent modules are on each other (low is good). Aim for high cohesion, low coupling: modules that each do one thing well and interact through narrow, stable interfaces.

**What is idempotency at the system level, and why care?**
An operation is idempotent if doing it twice has the same effect as once. It matters because networks force retries — a client that times out will retry, and without idempotency you double-charge or duplicate an order. Implement with idempotency keys, upserts, or dedup on a unique constraint.

**What is eventual consistency?**
In a distributed system, replicas may temporarily disagree but converge to the same state given no new updates. You accept a window of staleness in exchange for availability and scale. It's the norm across service boundaries — design UIs and workflows to tolerate "not immediately visible."

**When would you NOT use microservices?**
Small teams, early-stage products, unclear domain boundaries, or when the operational maturity (CI/CD, observability, on-call) isn't there. Premature microservices give you a distributed monolith: all the network pain, none of the independence. Split only when a clear boundary and a scaling or team-autonomy need justify it.

**How do you handle the dual-write / lost-update problem across a DB and a message broker?**
Writing to the DB and publishing an event as two separate operations can partially fail (DB commits, publish fails → lost event). Solve with the **Transactional Outbox**: write the event to an outbox table in the *same* DB transaction as the state change, then a relay process reads the outbox and publishes reliably. This gives at-least-once delivery without distributed transactions.

**Red flag:** "Wrap the DB write and the publish in one transaction / try-catch" — the broker doesn't participate in your DB transaction, so a crash between the two still loses the event.

**What is a saga?**
A pattern for a long-running business transaction spanning multiple services without a distributed lock. Each step commits locally and publishes an event triggering the next; if a step fails, **compensating actions** undo the prior steps. Orchestration (a central coordinator) or choreography (services react to events) are the two flavors.

---

## Distributed Systems & Scaling

*Revise: Ch. 9 — Messaging & Distributed Systems · Ch. 20 — Distributed Systems Theory & Reliability Engineering*

**Explain the CAP theorem.**
Under a network **P**artition, a distributed system must choose between **C**onsistency (every read sees the latest write) and **A**vailability (every request gets a response). You can't have both during a partition. In practice systems are CP (refuse/stall to stay consistent) or AP (serve possibly-stale data to stay up); the choice is per-operation, and PACELC extends it to the latency trade-off when there's no partition.

**Red flag:** "You pick any two of C, A, and P" — partition tolerance isn't optional; the real choice is C vs A *during* a partition.

**How do you scale a web application?**
Vertical first (bigger box — simple, limited), then horizontal: run many stateless instances behind a load balancer. Add caching (in-memory, distributed), read replicas or sharding for the database, a CDN for static assets, and async processing via queues to smooth spikes. Statelessness is the enabler for horizontal scale.

**Why must services be stateless to scale horizontally?**
So any instance can handle any request and you can add/remove instances freely behind a load balancer. Session state kept in-process ties a user to one instance (sticky sessions) and breaks on scale-down or failover. Push state to a shared store (Redis, DB) or a signed token so instances stay interchangeable.

**Caching strategies and the hard part?**
Strategies: cache-aside (app loads on miss, most common), read-through/write-through, write-behind. The hard part is **invalidation** — knowing when cached data is stale. Tools: TTL expiry, event-driven eviction on write, and versioned keys. "There are only two hard things: cache invalidation and naming things." Also plan for stampedes (many misses at once) with locking or staggered TTLs.

**Why introduce a message queue?**
To decouple producer from consumer, absorb load spikes (buffering), enable async processing, and add resilience — if the consumer is down, messages wait. It also enables independent scaling of producers and consumers and retry/dead-letter handling. Cost: eventual consistency and added operational surface.

**Is exactly-once delivery real?**
Not in a strict end-to-end sense over an unreliable network. Practically you get **at-least-once** delivery plus **idempotent** consumers, which yields exactly-once *processing* — the effect happens once even if the message arrives twice. Design consumers to dedupe (idempotency keys, processed-message table).

**Red flag:** "Just configure the broker for exactly-once delivery" — no broker setting survives an unreliable network end-to-end; the guarantee comes from idempotent consumers.

**What is the circuit breaker pattern?**
A wrapper around a remote call that, after repeated failures, "trips" and fails fast for a cooldown period instead of hammering a struggling dependency — then allows a trial request (half-open) to test recovery. It protects both caller (no piling-up threads) and callee (room to recover). Pair with timeouts, retries with backoff, and bulkheads (Polly implements these).

**Traffic is about to spike 5x for a launch — what do you do?**
Load-test to find the current ceiling first. Then: scale out stateless tiers (and pre-warm/auto-scale), add caching to cut DB load, protect the database with read replicas and connection-pool limits, move non-critical work to queues, add rate limiting and graceful degradation, and set up a CDN. Have a rollback and a "shed load" plan. Verify with the load test, don't hope.

**A downstream dependency goes down — how does your service behave?**
It should degrade gracefully, not cascade-fail. Use timeouts (never wait forever), a circuit breaker to fail fast, retries with exponential backoff and jitter for transient blips, a fallback (cached/default response) where the business allows, and bulkheads to isolate the failure to one feature. The goal: your service stays up and honest about reduced functionality.

---

## Security

*Revise: Ch. 14 — Security*

**AuthN vs AuthZ?**
**Authentication** verifies *who you are* (login, token validation). **Authorization** verifies *what you're allowed to do* (roles, policies, resource ownership). AuthN comes first; a valid identity still needs an authorization check per action. Conflating them ("logged in = allowed") is a classic vulnerability.

**Red flag:** "If the user is authenticated, they can access the endpoint" — that's broken access control, OWASP's #1 risk.

**Name a few OWASP Top 10 risks.**
Broken access control, injection (SQL/command), cryptographic failures (weak/no encryption of secrets), insecure design, security misconfiguration, vulnerable/outdated components, identification/authentication failures, and SSRF. The theme: validate input, enforce access control server-side, encrypt secrets, and patch dependencies.

**How do you prevent SQL injection?**
Use parameterized queries / prepared statements (or an ORM that parameterizes) so user input is always data, never concatenated into SQL. Never build queries by string concatenation. Add least-privilege DB accounts and input validation as defense in depth. EF Core and Dapper parameterize by default — the risk is raw string SQL.

**Red flag:** "Sanitize the input by escaping quotes" — escaping-by-hand is a blocklist that always misses cases; parameterization makes input structurally data.

**How do you store passwords?**
Never plaintext or plain hash. Use a slow, salted, adaptive password hash — bcrypt, scrypt, Argon2, or PBKDF2 with a high work factor and a per-user salt. The salt defeats rainbow tables; the slowness defeats brute force. Increase the work factor over time. Never encrypt passwords (reversible) when you should hash them.

**Red flag:** "Encrypt them with AES" or "hash with MD5/SHA-256" — encryption is reversible, and fast hashes are exactly what brute-forcers want.

**How do you validate a JWT — what must you check?**
Verify the signature against the trusted key, then validate the claims: issuer, audience, expiry (`exp`) and not-before (`nbf`), and the signing algorithm (reject `none` and don't let the token pick the algorithm). Only then trust its claims. Skipping audience/expiry checks or trusting the header's alg are the common JWT vulnerabilities.

**Where do secrets go — not appsettings, then where?**
Out of source control and out of plain config: use a secrets manager / vault (Azure Key Vault, AWS Secrets Manager, HashiCorp Vault), environment variables injected at deploy, or user-secrets in local dev. Rotate them, scope access least-privilege, and never log them. A leaked connection string in Git is a breach.

---

## Testing

*Revise: Ch. 7 — Testing · Ch. 24 — Advanced & Specialized Testing*

**Unit vs integration test?**
A **unit test** exercises one small piece (a class/method) in isolation with dependencies mocked — fast, focused, pinpoints failures. An **integration test** exercises several components together, often with a real database or HTTP host, to catch wiring and contract issues unit tests miss. You need both; the classic pyramid has many unit, fewer integration, fewest end-to-end.

**Mock vs stub?**
A **stub** provides canned answers to make the test run (returns a fixed value). A **mock** additionally *verifies interactions* — that a method was called, with what arguments, how many times. Use a stub when you only need to supply data, a mock when the behavior under test *is* the interaction (e.g. "does it publish the event?"). Over-mocking couples tests to implementation.

**What is TDD, in one breath?**
Red-green-refactor: write a failing test for the next small behavior, write the minimum code to pass it, then refactor with the test as a safety net — repeat. It drives design toward testable, small units and gives you a regression suite for free. The discipline is writing the test *first*.

**How do you test async code?**
Make the test method `async Task` and `await` the operation — never block with `.Result` in tests (it hides exceptions and can deadlock). Assert on the awaited result or the thrown exception (`await Assert.ThrowsAsync`). For time-dependent code, inject a clock/`TimeProvider` rather than sleeping.

**What makes a good test?**
Fast, isolated/independent (no order dependence, no shared state), deterministic (no flakiness from time, randomness, or network), readable (Arrange-Act-Assert, one logical assertion of behavior), and testing *behavior not implementation* so refactors don't break it. A test you don't trust is worse than no test.

**Red flag:** "Good tests means 100% code coverage" — coverage proves code ran, not that behavior was asserted; a suite can hit every line yet catch nothing.

---

## System Design (mini)

*Revise: Ch. 26 — Data Structures, Algorithms & System Design Fundamentals · Ch. 32 — Real-World Scenarios & Architectural Decisions*

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
- *API:* `POST /checkout` with an **idempotency key** (retries must not double-charge); returns order status.
- *Data:* orders, order items, inventory, payments — with a rowversion for optimistic concurrency on stock.
- *Components:* validate cart & price → reserve inventory (optimistic concurrency or reservation) → charge via payment gateway → confirm order. Use a **saga** with compensations (release inventory if payment fails) and a **transactional outbox** to emit "order placed" events reliably.
- *Bottlenecks:* inventory contention on hot items (optimistic retry, queueing), payment gateway latency/failure (timeouts, idempotent retries, circuit breaker), and exactly-once semantics (idempotency keys end-to-end).

---

## Behavioral / Seniority

*Revise: Ch. 17 — Soft Skills & Engineering Practices*

Answer these with **STAR** and keep the spotlight on *your* actions and a concrete result. Have three or four real stories prepared that you can flex to different questions.

**Tell me about a hard bug you solved.**
Pick a genuinely tricky one — intermittent, distributed, or a heisenbug. Emphasize *method*: how you reproduced it, formed and tested hypotheses, used tooling (logs, profiler, dump), found root cause, and prevented recurrence (a test, a monitor). Result: the metric that improved. The story sells your debugging process, not luck.

**Describe a disagreement with a colleague.**
Show you can disagree on the technical merits and stay collaborative. Structure: the two positions and their trade-offs, how you sought data or a spike to decide, and how you committed to the outcome even if it wasn't your pick. Interviewers probe for ego and for "disagree and commit."

**Tell me about a bad technical decision you made.**
Own a real one, no humble-brags. Explain the context and why it seemed right, what went wrong, how you caught and corrected it, and the lesson you carry forward. This probes self-awareness and growth — the willingness to be wrong is a seniority signal.

**How do you mentor junior developers?**
Concrete examples: pairing, code review framed as teaching not gatekeeping, giving stretch tasks with a safety net, explaining the *why* behind feedback, and growing autonomy over time. Result: someone who leveled up. Shows you scale your impact through others, not just your own commits.

**How do you handle technical debt?**
Make it visible and quantify its cost (slower delivery, more bugs), then negotiate — pay it down opportunistically alongside feature work, reserve capacity each sprint, and fix high-interest debt first. Frame it to stakeholders in terms of risk and velocity, not purity. Shows pragmatism and business awareness.

**How do you push back on scope or an unrealistic deadline?**
Bring data, not complaints: present the estimate, the trade-offs, and options (cut scope, phase delivery, add risk-acceptance, or move the date). Let stakeholders choose with full information. The senior move is turning "no" into "here are the trade-offs — which do you want?"

> **Follow-up:** *Tell me about a time you had to make a decision without complete information.* Show how you bounded the risk: made a reversible choice, shipped small to learn, set a checkpoint to re-evaluate, and communicated the uncertainty rather than pretending certainty.

---

## Sources & Further Reading

- **Microsoft Learn** — the authoritative reference for C#, .NET runtime/GC, ASP.NET Core, and EF Core behavior (learn.microsoft.com). Cross-check any runtime specifics here against the current docs.
- **.NET diagnostics tooling docs** — `dotnet-counters`, `dotnet-trace`, `dotnet-dump`, `dotnet-gcdump` guides on Microsoft Learn for the performance methodology.
- **"Cracking the Coding Interview"** by Gayle Laakmann McDowell — interview strategy, behavioral framing, and algorithmic warm-ups.
- **"System Design Interview"** (Volumes 1 & 2) by Alex Xu — the structured approach behind the mini system-design section.
- **"Designing Data-Intensive Applications"** by Martin Kleppmann — deep background for the distributed-systems, consistency, and CAP material.
- **OWASP Top 10** (owasp.org) — the canonical web security risk list.

Practice out loud, time yourself, and remember: interviewers hire for *reasoning you can hear*, not just answers you happen to know.
