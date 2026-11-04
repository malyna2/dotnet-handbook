# Appendix: Interview Priority Map

> **What this page is for.** The thirteen topic areas a Regular (mid-level) .NET interview probes hardest, ranked, with where the book teaches each one, the wrong answer that usually ends the conversation, and the questions to expect. Every section of the book that teaches one of them carries a **🎯 Interview** callout naming its priority and linking back here. Read the chapter first; use this page to rehearse.

**The marker.** A callout that starts with **🎯 Interview · P1** or **🎯 Interview · P2** marks a section an interviewer is likely to test.

- **P1** topics come up in nearly every backend .NET interview and are where a wrong answer costs the most: SQL written by hand, security, messaging, memory, ASP.NET Core and async code. Prepare these until you can explain the mechanism without notes.
- **P2** topics separate a good Regular candidate from an average one and dominate the second half of a loop: architecture, REST design, EF Core in depth, Azure and delivery, testing, production incidents and the interview itself.

**How to use it.** For each topic: read the sections linked under *Where it is taught*, then answer the questions out loud before you open the answers. An answer you can't give in two minutes, mechanism first, is a section to reread. The answers here are short on purpose; the chapter holds the reasoning and the code.

## P1-1. SQL Written by Hand

**Where it is taught.** [Window Functions](#window-functions) (ranking, running totals, top N per group), [CROSS APPLY and OUTER APPLY](#cross-apply-and-outer-apply), [Grouping and Aggregates](#grouping-and-aggregates), [Subqueries, EXISTS and CTEs](#subqueries-exists-and-ctes), [Indexes: Clustered, Non-Clustered, Covering](#indexes-clustered-non-clustered-covering), [Execution Plans](#execution-plans), [Parameter Sniffing](#parameter-sniffing), [Isolation Levels](#isolation-levels) and [Deadlocks](#deadlocks) in Chapter 7; the hands-on plans in [Chapter 19: The Slow-Query Lab](#chapter-19-the-slow-query-lab-reading-execution-plans).

**Why it is asked.** Many interviews include a query written live, with no ORM to hide behind, and the follow-ups go straight to why it is slow.

**The usual wrong answer.** Filtering on `ROW_NUMBER()` in the same `WHERE` that computes it, or "add an index on every column in the `WHERE`". The row number doesn't exist until `SELECT` runs, so it needs an outer query; and an index serves only its leftmost prefix, so column order decides whether it seeks at all.

**Questions to expect.**

**Write the top three orders per customer. What changes if ties must all be kept, and what if customers without orders must appear?**

<details>
<summary>Answer</summary>

`ROW_NUMBER() OVER (PARTITION BY CustomerId ORDER BY Total DESC, Id)` in a CTE, then `WHERE Rn <= 3` outside it. Keeping ties means `RANK` (or `DENSE_RANK`) instead of `ROW_NUMBER`. Customers with no orders need the customer table on the outside: `OUTER APPLY (SELECT TOP (3) … ORDER BY Total DESC)` on SQL Server, `LEFT JOIN LATERAL … LIMIT 3` on PostgreSQL.

</details>

**An index exists on `(City, CreatedAt)`. Which of `City = @c`, `CreatedAt >= @d`, and `City = @c AND CreatedAt >= @d` can seek, and why?**

<details>
<summary>Answer</summary>

The first and the third. The index is sorted by `City`, then by `CreatedAt` within each city, so a seek needs a contiguous range of that order. `CreatedAt >= @d` alone is spread across every city's block, so it scans. Equality columns go first, then the one range or sort column.

</details>

**A stored procedure is fast for most customers and slow for one, and it is fast when you run it in SSMS. What is going on?**

<details>
<summary>Answer</summary>

Parameter sniffing: the cached plan was compiled for the first caller's value and is reused for everyone, which hurts when the data is skewed. SSMS has different `SET` options, so it gets its own plan. Confirm with the compiled and runtime values in the actual plan, then fix with `OPTION (RECOMPILE)`, `OPTIMIZE FOR`, a forced plan in Query Store, or SQL Server 2022's parameter sensitive plan optimization, not by flushing the cache.

</details>

**What is the default isolation level in SQL Server, and does a `SELECT` block behind an `UPDATE`?**

<details>
<summary>Answer</summary>

Read Committed. With `READ_COMMITTED_SNAPSHOT` off (the default on a SQL Server you install) the read takes shared locks and waits for the writer; with it on (the default in Azure SQL Database) it reads the last committed row version and doesn't wait. Neither stops a lost update.

</details>

## P1-2. Security

**Where it is taught.** [Authentication vs. Authorization](#authentication-vs-authorization), [Policy-based and role-based authorization](#policy-based-and-role-based-authorization), [OAuth 2.0, OpenID Connect, and JWTs](#oauth-20-openid-connect-and-jwts), [A01: Broken Access Control](#a01-broken-access-control), [A03: Injection](#a03-injection), [Cross-Site Scripting (XSS)](#cross-site-scripting-xss), [Anti-Forgery / CSRF](#anti-forgery-csrf), [CORS Is Not Access Control](#cors-is-not-access-control) and [Secrets Management](#secrets-management) in Chapter 12; `401` versus `403` in [Status Codes and Headers That Matter](#status-codes-and-headers-that-matter).

**Why it is asked.** Every API a Regular developer ships authenticates someone and authorizes something, and the bugs here are breaches, not slow pages.

**The usual wrong answer.** "The JWT is secure because it's encoded" and "we enabled CORS, so we're protected from CSRF". A JWT payload is Base64URL: signed, readable by anyone. CORS is a browser rule about reading responses; a cross-site form `POST` with a cookie still reaches the server.

**Questions to expect.**

**`401` or `403`: which one, when?**

<details>
<summary>Answer</summary>

`401 Unauthorized` means "I don't know who you are": no credentials, or invalid ones, and it carries a `WWW-Authenticate` header. `403 Forbidden` means "I know who you are, and you may not do this". For a resource that belongs to another user or tenant, `404` hides that it exists.

</details>

**What is the difference between an access token and an ID token, and which one does your API accept?**

<details>
<summary>Answer</summary>

OAuth 2.0 issues the access token, meant for the API (its `aud`), carrying scopes. OpenID Connect adds the ID token, meant for the client, describing who signed in. The API accepts only access tokens, validated for issuer, audience, lifetime and signature; an ID token sent as a bearer token must be rejected.

</details>

**`GET /orders/42` returns any user's order to any signed-in user. What is this called and how do you fix it in ASP.NET Core?**

<details>
<summary>Answer</summary>

An IDOR, broken object-level authorization: authentication passed, but nobody checked ownership. Filter by owner in the query (`Where(o => o.Id == id && o.CustomerId == userId)`) or use a resource-based authorization handler, and return `404` when it doesn't match.

</details>

**Is `FromSql($"… WHERE Email = {email}")` safe? And `FromSqlRaw("… WHERE Email = '" + email + "'")`?**

<details>
<summary>Answer</summary>

The first is safe: `FromSql` takes a `FormattableString` and turns each interpolated value into a `DbParameter`. The second is SQL injection: the string is built before EF Core ever sees it. With `FromSqlRaw`, pass values as parameters, never concatenated.

</details>

## P1-3. Messaging and Distributed Systems

**Where it is taught.** [Peek-Lock: The Acknowledgement Is a Lease](#peek-lock-the-acknowledgement-is-a-lease), [Dead-Letter Queues (DLQ)](#dead-letter-queues-dlq), [Message Ordering](#message-ordering), [Apache Kafka — The Distributed Log](#apache-kafka-the-distributed-log), [Idempotent Consumers](#idempotent-consumers) and [The Transactional Outbox](#the-transactional-outbox) in Chapter 11; [Change Data Capture (CDC)](#change-data-capture-cdc) in Chapter 18; [Sagas: Long-Running Distributed Transactions](#sagas-long-running-distributed-transactions) and [Detecting Failure and Retrying Without Making It Worse](#detecting-failure-and-retrying-without-making-it-worse) in Chapter 20; Service Bus in [Chapter 29: Azure in Depth](#chapter-29-azure-in-depth-for-net-developers).

**Why it is asked.** Most systems a Regular developer joins already have a broker, and nearly every production bug in them is a duplicate, a lost message or a message out of order.

**The usual wrong answer.** "The broker guarantees exactly-once" and "we check Redis to skip duplicates". Delivery is at-least-once; a check that lives in another system than the effect, or that runs before the work instead of being the work's first write, lets duplicates through.

**Questions to expect.**

**A consumer charges a card and then crashes before completing the message. What happens, and how do you make that safe?**

<details>
<summary>Answer</summary>

The lock expires and the message is delivered again, up to `MaxDeliveryCount` times, then dead-lettered. Make the consumer idempotent: record the message's key under a unique constraint in the same transaction as the effect, and pass the same key to the payment provider as its idempotency key, since the provider's call can't join your transaction.

</details>

**Why can't you save an order and then publish `OrderPlaced` in the same method, and what replaces it?**

<details>
<summary>Answer</summary>

It's a dual write: the database commit and the broker publish can't be atomic, so a crash between them loses the event or publishes one for an order that rolled back. The Transactional Outbox writes the event to an outbox table in the same transaction and a relay publishes it afterwards; CDC (Debezium reading the transaction log) is the other way to get the same guarantee.

</details>

**Messages for one order must be processed in order. How in Kafka, and how in Service Bus?**

<details>
<summary>Answer</summary>

Kafka orders only within a partition, so use the order ID as the message key: the same key always lands in the same partition, consumed by one member of the consumer group. Service Bus orders within a session: set `SessionId` to the order ID on a session-enabled entity, and one receiver holds each session at a time.

</details>

**What do you do with the messages in a dead-letter queue?**

<details>
<summary>Answer</summary>

Alert on its depth, read the dead-letter reason, fix the cause (a bug, a poison payload, a missing dependency), then resubmit deliberately, idempotently and at a controlled rate, or discard with a record. A DLQ nobody watches is data loss with a delay.

</details>

## P1-4. Memory and the GC

**Where it is taught.** [Garbage Collection](#garbage-collection) (generations, finalization) and [IDisposable, IAsyncDisposable, and the Dispose Pattern](#idisposable-iasyncdisposable-and-the-dispose-pattern) in Part 1; [readonly struct and ref struct](#readonly-struct-and-ref-struct); [Garbage Collection — in depth](#garbage-collection-in-depth) (LOH), [Memory and Allocations: The Quiet Performance Killer](#memory-and-allocations-the-quiet-performance-killer) (`Span<T>`, `ArrayPool`) and [Profiling: Finding the Bottleneck in a Running System](#profiling-finding-the-bottleneck-in-a-running-system) in Chapter 17; the step-by-step leak investigation in [Scenario 7](#scenario-7-the-slow-leak-memory-keeps-growing-until-the-pod-is-oom-killed) of Chapter 35.

**Why it is asked.** "Memory keeps growing until the pod restarts" is one of the most common production tickets, and the interviewer wants a method, not a guess.

**The usual wrong answer.** "The GC frees everything, so .NET can't leak" and "call `GC.Collect()`". A managed leak is an object still reachable from a root (a static collection, an event subscription, a long-lived closure), and the GC is right to keep it.

**Questions to expect.**

**Walk me through investigating an OOM in production.**

<details>
<summary>Answer</summary>

Confirm the trend with `dotnet-counters` (GC heap size, Gen 2 size, LOH size, allocation rate) and rule out native or unmanaged growth. Take two `dotnet-gcdump` snapshots some minutes apart, compare which types grew, and follow the retention path to the root that keeps them alive; use `dotnet-dump` when you also need stacks or native memory. Fix the root (unsubscribe, bound the cache, dispose), then prove it with the same counters.

</details>

**What does `GC.SuppressFinalize(this)` do in `Dispose`, and when does a class need a finalizer at all?**

<details>
<summary>Answer</summary>

It removes the object from the finalization queue, so it is collected in one pass instead of surviving into the next generation to have its finalizer run. A class needs a finalizer only if it directly owns an unmanaged handle; wrapping one in a `SafeHandle` means even that rarely applies.

</details>

**Why can't a `Span<T>` be a field of a class or live across an `await`?**

<details>
<summary>Answer</summary>

It is a `ref struct` that may point into the stack, and the stack frame can be gone by the time the heap object or the resumed state machine uses it. A class field and an `await` both move the value to the heap, so the compiler forbids them. Use `Memory<T>` there.

</details>

## P1-5. ASP.NET Core

**Where it is taught.** [Ordering is everything](#ordering-is-everything), [IHttpClientFactory & Resilience with Polly](#ihttpclientfactory-resilience-with-polly), [Cross-Cutting HTTP Concerns](#cross-cutting-http-concerns) (CORS, rate limiting) and [Behind a Reverse Proxy: Forwarded Headers](#behind-a-reverse-proxy-forwarded-headers) in Chapter 5; [Service lifetimes](#service-lifetimes), [Captive dependencies — the classic DI bug](#captive-dependencies-the-classic-di-bug) and [The Options pattern and binding](#the-options-pattern-and-binding) in Chapter 3; [What to Surface: ProblemDetails](#what-to-surface-problemdetails) in Chapter 9.

**Why it is asked.** It is the framework of the job; the interviewer checks you understand the pipeline you configure every day rather than copy it.

**The usual wrong answer.** "Middleware order doesn't matter much" and "inject a `DbContext` into the `BackgroundService` constructor". `UseAuthorization` before `UseAuthentication` sees every user as anonymous; a scoped service in a singleton is a captive dependency that lives forever.

**Questions to expect.**

**Put these in order and say why: authentication, routing, CORS, exception handling, authorization, rate limiting, endpoints.**

<details>
<summary>Answer</summary>

Exception handling first, so it wraps everything; routing, so later middleware knows the endpoint and its metadata; CORS, so preflights are answered before auth rejects them; authentication, to set `HttpContext.User`; authorization, which needs both the user and the endpoint; endpoints last. Rate limiting goes after routing, so it can read the endpoint's policy; put it after authentication when it partitions by user, before it when it must shield the authentication work from a flood partitioned by IP. Forwarded headers go before all of them when the app runs behind a proxy.

</details>

**How do you use a scoped `DbContext` from a `BackgroundService`?**

<details>
<summary>Answer</summary>

Inject `IServiceScopeFactory`, and in each unit of work `CreateAsyncScope()` and resolve the context from that scope. The hosted service is a singleton, so a constructor-injected scoped service would become captive and be shared across every iteration.

</details>

**`IOptions`, `IOptionsSnapshot` or `IOptionsMonitor`?**

<details>
<summary>Answer</summary>

`IOptions<T>` is read once and is a singleton. `IOptionsSnapshot<T>` is scoped and re-reads per request, so it can't go into a singleton. `IOptionsMonitor<T>` is a singleton that always returns the current value and raises `OnChange`, which is the one a singleton or a background service needs.

</details>

**Behind a load balancer, `Request.Scheme` is `http` and every client IP is the proxy's. Why?**

<details>
<summary>Answer</summary>

TLS ended at the proxy, which forwarded the original scheme and client IP in `X-Forwarded-Proto` and `X-Forwarded-For`, and ASP.NET Core ignores those headers until `UseForwardedHeaders` runs, and then accepts them only from known proxies (loopback by default). Enable it first in the pipeline with the proxy's network in `KnownIPNetworks`.

</details>

## P1-6. Async and Concurrency

**Where it is taught.** [Why Async Exists: I/O-Bound vs CPU-Bound Work](#why-async-exists-io-bound-vs-cpu-bound-work), [SynchronizationContext and ConfigureAwait](#synchronizationcontext-and-configureawait), [The Sync-Over-Async Deadlock](#the-sync-over-async-deadlock), [CancellationToken: Cooperative Cancellation](#cancellationtoken-cooperative-cancellation), [Composing Concurrent Work: WhenAll and WhenAny](#composing-concurrent-work-whenall-and-whenany) and [Thread Safety: Sharing State Correctly](#thread-safety-sharing-state-correctly) in Chapter 4; [The Thread Pool Under Load](#the-thread-pool-under-load), [Task vs ValueTask](#task-vs-valuetask) and [The TPL: Parallelism for CPU-Bound Work](#the-tpl-parallelism-for-cpu-bound-work) in Chapter 17; [DbContext Lifetime and Connection Pooling](#dbcontext-lifetime-and-connection-pooling) in Chapter 7.

**Why it is asked.** Async bugs pass every test and fail under load, so interviewers probe whether you know the mechanism behind them.

**The usual wrong answer.** "`async` makes code run on another thread" and "wrap I/O in `Task.Run` to make it faster". An `await` on I/O holds no thread at all; `Task.Run` around I/O only moves the wait to a pool thread.

**Questions to expect.**

**Latency jumps, CPU is at 20%, and the database is healthy. What do you suspect and how do you confirm it?**

<details>
<summary>Answer</summary>

Thread pool starvation, usually from sync-over-async (`.Result`, `.Wait()`) blocking pool threads while the pool adds new ones slowly. Confirm with `dotnet-counters`: ThreadPool Queue Length growing and Thread Count climbing step by step; a `dotnet-dump` with `parallelstacks` shows the blocked frames. The fix is async all the way down, not a larger minimum thread count.

</details>

**`Task.Run`, `async/await` or `Parallel.ForEach`: which for calling five HTTP APIs, and which for resizing a thousand images?**

<details>
<summary>Answer</summary>

Five HTTP calls are I/O: start them and `await Task.WhenAll(...)`, no extra threads. Resizing images is CPU work: `Parallel.ForEach` (or `Parallel.ForEachAsync` with a degree of parallelism) uses the cores. `Task.Run` is for moving CPU work off a thread that must stay free, such as a UI thread, not for I/O.

</details>

**Why can't you `await` inside a `lock`, and what do you use instead? And why can't two tasks share one `DbContext`?**

<details>
<summary>Answer</summary>

A `lock` is owned by a thread, and the code after `await` may resume on another one, so the compiler forbids it; use `SemaphoreSlim(1, 1)` with `await WaitAsync()` and `Release()` in a `finally`. A `DbContext` isn't thread-safe and allows one operation at a time, so parallel queries need one context each, created with `IDbContextFactory<T>`.

</details>

## P2-7. Architecture

**Where it is taught.** [Clean, Onion, and Hexagonal Architecture](#clean-onion-and-hexagonal-architecture) and [Mediator](#mediator) in Chapter 10; [Tactical Building Blocks](#tactical-building-blocks) (aggregates), [CQRS](#cqrs), [Pipeline Behaviours: Cross-Cutting Code Around Every Handler](#pipeline-behaviours-cross-cutting-code-around-every-handler), [Monolith vs Microservices vs Modular Monolith](#monolith-vs-microservices-vs-modular-monolith) and [Worked Example: Pulling Reporting Out of the Monolith](#worked-example-pulling-reporting-out-of-the-monolith) in Chapter 21; the strangler fig in [Chapter 24](#chapter-24-working-with-legacy-brownfield-code).

**Why it is asked.** The interviewer wants to hear that you choose structure for a reason and can name what it costs.

**The usual wrong answer.** "CQRS means two databases and event sourcing" and "every entity needs a generic repository". CQRS at its lightest is separate command and query handlers over one database; `DbContext` already is a unit of work with repositories.

**Questions to expect.**

**What is the dependency rule in Clean Architecture, and where does EF Core live?**

<details>
<summary>Answer</summary>

Source dependencies point inward: the domain depends on nothing, the application layer on the domain, and infrastructure (EF Core, HTTP clients, the broker) on the application layer's interfaces. EF Core sits in infrastructure; the core defines what it needs and infrastructure implements it.

</details>

**What is an aggregate root, and why should one transaction change only one aggregate?**

<details>
<summary>Answer</summary>

It is the entity through which every change to a cluster of objects passes, so it can enforce their invariants. The aggregate is the consistency boundary: what must be true together is changed together in one transaction, and anything across aggregates is eventually consistent, through domain events.

</details>

**Reports are slowing down the main database. Walk me through moving reporting out.**

<details>
<summary>Answer</summary>

Measure first; then go in reversible steps: a read replica, then a read model fed by the outbox or CDC, then, only if a team and a scaling need justify it, a separate service. Say what consistency the reports can live with, run old and new side by side behind a feature flag, and compare results before switching.

</details>

## P2-8. REST Design

**Where it is taught.** [HTTP Methods and Idempotency](#http-methods-and-idempotency), [Status Codes and Headers That Matter](#status-codes-and-headers-that-matter) and [REST and OpenAPI](#rest-and-openapi) in Chapter 5; [Pagination: Never Fetch an Unbounded Result Set](#pagination-never-fetch-an-unbounded-result-set) in Chapter 18; [Concurrency: Optimistic vs Pessimistic](#concurrency-optimistic-vs-pessimistic) (`ETag`, `If-Match`) in Chapter 7; [Idempotency Keys: Making POST Retry-Safe](#idempotency-keys-making-post-retry-safe) in Chapter 20; [API Versioning & Backward Compatibility](#api-versioning-backward-compatibility) in Chapter 22; [What to Surface: ProblemDetails](#what-to-surface-problemdetails) in Chapter 9.

**Why it is asked.** Designing an endpoint on a whiteboard is a common interview task, and every detail (method, status, header) is a chance to show you know the contract.

**The usual wrong answer.** "Return `200` with `{ "success": false }`" and "offset pagination is fine". Status codes are what clients, proxies and retries act on; `OFFSET` reads and discards every skipped row, and pages shift when rows are inserted.

**Questions to expect.**

**`POST /orders` succeeded. What do you return? And when do you return `409` versus `422`?**

<details>
<summary>Answer</summary>

`201 Created` with a `Location` header pointing to the new order, and usually its representation. `409 Conflict` when the request is valid but clashes with the current state (a duplicate, a version mismatch); `422 Unprocessable Content` when it is well-formed but semantically invalid. Errors go out as `ProblemDetails`.

</details>

**A client times out on a payment `POST` and retries. How do you avoid charging twice?**

<details>
<summary>Answer</summary>

The client sends an `Idempotency-Key`; the server stores the key with the request's hash and outcome under a unique constraint, atomically with the effect, and replays the stored response for a repeat. A repeat with a different body is rejected.

</details>

**Two users edit the same product. How does the API stop the second save overwriting the first?**

<details>
<summary>Answer</summary>

Optimistic concurrency: `GET` returns an `ETag` from the row version, the client sends it back in `If-Match`, and the update succeeds only if the version still matches; otherwise `412 Precondition Failed`. In EF Core the row version is a concurrency token, and a mismatch throws `DbUpdateConcurrencyException`.

</details>

## P2-9. EF Core in Depth

**Where it is taught.** Chapter 7: [The N+1 Problem — Seeing It and Killing It](#the-n1-problem-seeing-it-and-killing-it), [Projections: Select Only What You Need](#projections-select-only-what-you-need), [Split Queries](#split-queries), [AsNoTracking: Read-Only Speed](#asnotracking-read-only-speed), [Set-Based Updates and Deletes: ExecuteUpdate and ExecuteDelete](#set-based-updates-and-deletes-executeupdate-and-executedelete), [Concurrency: Optimistic vs Pessimistic](#concurrency-optimistic-vs-pessimistic), [Mixing Dapper and EF Core in One Transaction](#mixing-dapper-and-ef-core-in-one-transaction) and [Migrations in CI/CD](#migrations-in-cicd); [Migrations at Scale (Zero-Downtime)](#migrations-at-scale-zero-downtime) in Chapter 18.

**Why it is asked.** EF Core is where most .NET performance problems start, and the interviewer checks whether you know what SQL your LINQ sends.

**The usual wrong answer.** "`Include` causes N+1" and "call `Migrate()` on startup". N+1 comes from lazy loading or a query inside a loop; `Include` does the opposite, one query, and with several collections it multiplies rows instead.

**Questions to expect.**

**An endpoint `Include`s two collections and got slow. What is happening, and what are the two fixes?**

<details>
<summary>Answer</summary>

A cartesian explosion: one joined query returns every combination of the two collections' rows. `AsSplitQuery()` sends one query per collection (the queries can see different data if rows change between them, unless they run in a snapshot or serializable transaction), or a projection into a DTO selects only the columns the screen needs.

</details>

**How do you apply migrations in production with several instances, and how do you rename a column without downtime?**

<details>
<summary>Answer</summary>

As a separate deployment step, from an idempotent SQL script (`dotnet ef migrations script --idempotent`) or a migration bundle, not from `Migrate()` racing in every instance. The rename is expand/contract: add the new column, write both and backfill, move reads, and drop the old column in a later release.

</details>

**Can Dapper and EF Core share one transaction?**

<details>
<summary>Answer</summary>

Yes: open the transaction through EF Core, get its connection with `Database.GetDbConnection()` and the transaction with `GetDbTransaction()`, and pass that transaction to every Dapper call. Without the `transaction:` argument, the Dapper command fails or runs outside it.

</details>

## P2-10. Azure and DevOps

**Where it is taught.** Chapter 29: [Identity: Entra ID, Managed Identity and RBAC, Mechanically](#identity-entra-id-managed-identity-and-rbac-mechanically), [Key Vault](#key-vault), [Azure SQL Database](#azure-sql-database), [Azure Functions](#azure-functions), [Where Background Work Runs](#where-background-work-runs), [Networking for Application Developers](#networking-for-application-developers) and [Observability: Application Insights and KQL](#observability-application-insights-and-kql); [Health probes: liveness, readiness, startup](#health-probes-liveness-readiness-startup) and [Deployment Strategies](#deployment-strategies) in Chapter 26; [What CI/CD Actually Means](#what-cicd-actually-means) in Chapter 13; [Containerizing a .NET Application](#containerizing-a-net-application) in Chapter 14; rollback and down migrations in [Migrations at Scale (Zero-Downtime)](#migrations-at-scale-zero-downtime).

**Why it is asked.** Most Regular roles in .NET shops deploy to Azure, and the interviewer checks you can run what you build.

**The usual wrong answer.** "Store the connection string in an environment variable in the repo" and "a liveness probe should check the database". A managed identity removes the secret; a liveness probe that depends on the database restarts every pod when the database blips.

**Questions to expect.**

**How does an App Service app read a Key Vault secret without any credential in its configuration?**

<details>
<summary>Answer</summary>

Enable its managed identity, grant that identity a data-plane role on the vault (*Key Vault Secrets User*), and use `DefaultAzureCredential` (or a Key Vault reference in app settings). The platform issues the identity a token; no secret is stored anywhere.

</details>

**Liveness versus readiness: what does each check, and what goes wrong if you mix them up?**

<details>
<summary>Answer</summary>

Liveness asks "is this process broken beyond repair?" and checks only the process; failing it restarts the container. Readiness asks "can it serve now?" and may check dependencies; failing it takes the pod out of the load balancer. Database checks in liveness turn a database outage into a restart storm.

</details>

**A release broke production. Do you run the down migration?**

<details>
<summary>Answer</summary>

Usually not: redeploy the previous image, which works because the schema change was backward-compatible (expand/contract), and fix forward. A down migration that drops a column deletes the data written since the release; it is a development tool, not a rollback plan.

</details>

## P2-11. Testing

**Where it is taught.** Chapter 8: [The Testing Pyramid](#the-testing-pyramid), [Integration Testing](#integration-testing), [In-Memory vs Real Database](#in-memory-vs-real-database), [Testcontainers for .NET](#testcontainers-for-net), [Flaky Tests](#flaky-tests) and [Fake time with TimeProvider (.NET 8+)](#fake-time-with-timeprovider-net-8).

**Why it is asked.** The interviewer wants to know whether your tests would catch the bug that matters, or only pass.

**The usual wrong answer.** "We test the repository with EF Core InMemory." The InMemory provider isn't a relational database: no SQL, no constraints, no transactions, so a unique-index violation that fails in production passes the test.

**Questions to expect.**

**Unit or integration: where is the line, and what does `WebApplicationFactory` give you?**

<details>
<summary>Answer</summary>

A unit test exercises logic in memory with its collaborators faked; an integration test runs real infrastructure (HTTP pipeline, database). `WebApplicationFactory<TProgram>` hosts the real app in memory with a test `HttpClient`, so routing, filters, DI and serialization run as in production, and `ConfigureTestServices` swaps what must be swapped.

</details>

**How do you test code that depends on "now" or on a 30-second timeout?**

<details>
<summary>Answer</summary>

Inject `TimeProvider` instead of reading `DateTime.UtcNow` or calling `Task.Delay` directly, and use `FakeTimeProvider` in the test: set the time and `Advance` it, and timers fire without waiting.

</details>

**A test fails once in twenty runs on CI. What do you do?**

<details>
<summary>Answer</summary>

Treat it as a bug: quarantine visibly, reproduce by running it in a loop, and look for the usual causes: shared state between tests, real time, ordering assumptions, or waiting with `Thread.Sleep` instead of on a condition. Fix the cause; a retry only hides it.

</details>

## P2-12. Production and Incidents

**Where it is taught.** [Why Structured Beats String Logging](#why-structured-beats-string-logging) and [Correlation Across Services](#correlation-across-services) in Chapter 9; [OpenTelemetry: The Standard](#opentelemetry-the-standard) and [Alerting, SLIs, SLOs, SLAs, and Error Budgets](#alerting-slis-slos-slas-and-error-budgets) in Chapter 25; [The incident cheat-card](#the-incident-cheat-card) and the scenarios of [Chapter 35: Production Incidents](#chapter-35-production-incidents); the blameless post-mortem in [Chapter 16](#chapter-16-working-like-a-middle-developer).

**Why it is asked.** "Tell me about an incident" is close to universal, and the interviewer listens for order: stop the bleeding before finding the cause.

**The usual wrong answer.** Debugging live while users wait, and a post-mortem that names who made the mistake. Mitigate first (roll back, flip the flag, scale), communicate, then diagnose; the review is about the system that let the mistake through.

**Questions to expect.**

**Walk me through how you handle a production incident.**

<details>
<summary>Answer</summary>

Detect (an alert on a symptom users feel), communicate (an incident channel, one owner, a status update to support), mitigate (roll back or disable before understanding), diagnose with logs, traces and metrics joined by trace ID, fix properly, then a blameless post-mortem with owned action items and dates.

</details>

**How do you follow one request across three services?**

<details>
<summary>Answer</summary>

W3C Trace Context: the `traceparent` header carries the trace ID across HTTP and message hops, OpenTelemetry instruments the libraries and exports the spans, and structured logs include the trace ID, so one ID finds the trace and every log line of that request.

</details>

**What should page someone at 3 a.m.?**

<details>
<summary>Answer</summary>

A breach, or a fast burn, of an SLO that users feel: the error rate or latency of a user-facing journey, not CPU or a single failed health check. Everything else is a ticket or a dashboard.

</details>

## P2-13. The Interview Itself

**Where it is taught.** [How to Approach Any Interview](#how-to-approach-any-interview), [Answer First: The Shape of a Spoken Technical Answer](#answer-first-the-shape-of-a-spoken-technical-answer), [Behavioral / Seniority](#behavioral-seniority) and [Three Stories to Have Ready](#three-stories-to-have-ready) in Chapter 36; the story bank lab in [Chapter 37](#chapter-37-the-story-bank-evidence-portfolio).

**Why it is asked.** Half of a loop is how you explain, and behavioural rounds are often held in English even when the technical ones aren't.

**The usual wrong answer.** Building up to the conclusion over two minutes, and a story that says "we" throughout and ends without a result. Lead with the answer, defend it with the mechanism and the trade-off; in a story, say what *you* did and what changed.

**Questions to expect.**

**"Tell me about a mistake you made."**

<details>
<summary>Answer</summary>

A real mistake with consequences, told in STAR: the context in two sentences, what you decided and why it seemed right, how you found out, what you did to fix it, and what you changed so it can't happen again. The lesson is the result.

</details>

**"Tell me about a conflict with a colleague."**

<details>
<summary>Answer</summary>

A technical disagreement, both positions stated fairly, how you settled it with data or a small experiment, and how you committed to the outcome. The interviewer is listening for ego and for "disagree and commit".

</details>

**"Describe the hardest technical problem you solved."**

<details>
<summary>Answer</summary>

One problem, your method (reproduce, hypothesise, measure, fix, prevent) and a before-and-after number from your own work. Have it ready in English, out loud, in two minutes.

</details>
