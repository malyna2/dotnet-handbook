# Chapter 21: Background Processing, Scheduling & the Actor Model

_⏱️ Estimated read time: ~28 min ·     3883 words (study pace)_

Almost every non-trivial system does work that no user is waiting on: sending emails, retrying failed payments, rebuilding search indexes, aggregating metrics, cleaning up expired data. The naive approach - do it inline on the request thread - couples user-facing latency to work that has no business being on the hot path, and it silently loses that work whenever a request is cancelled or a pod restarts.

This chapter is about doing that work *deliberately*. We move from the humble in-process background loop, through dedicated job frameworks like Hangfire and Quartz.NET, and finally into the actor model and Microsoft Orleans - a paradigm that reframes how you think about concurrency and stateful services entirely. The through-line is a single question that separates mid-level from senior engineering: **not "how do I run this in the background?" but "what happens when it fails, when it runs twice, and when I have ten copies of my service running at once?"**

---

## Part A — Background Processing in .NET

### `IHostedService` and `BackgroundService`

The .NET Generic Host owns a collection of `IHostedService` instances. When the host starts, it calls `StartAsync` on each; when it stops, it calls `StopAsync`. This is the foundational hook for anything that needs to live for the lifetime of your application - a message consumer, a polling loop, a cache warmer.

```csharp
public interface IHostedService
{
    Task StartAsync(CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);
}
```

Implementing this raw is fiddly: `StartAsync` is expected to *return quickly* (the host awaits it before considering itself started), so you cannot simply `await` a long loop inside it. You would have to spin up a `Task`, stash it in a field, wire up a `CancellationTokenSource`, and join it in `StopAsync`. That boilerplate is exactly what `BackgroundService` exists to eliminate.

```csharp
public abstract class BackgroundService : IHostedService, IDisposable
{
    protected abstract Task ExecuteAsync(CancellationToken stoppingToken);
    // StartAsync stores the Task returned by ExecuteAsync; StopAsync
    // signals the token and awaits that Task (up to the shutdown timeout).
}
```

You override one method, `ExecuteAsync`, and treat the supplied `stoppingToken` as your signal to wind down.

> **Key mental model:** `ExecuteAsync` runs on a background flow, not a request. There is no ambient `HttpContext`, no scoped services unless you create a scope, and no per-request lifetime. A singleton `BackgroundService` that needs a scoped `DbContext` **must** open its own scope per unit of work.

### The Worker Service template

.NET ships a project template for exactly this: `dotnet new worker`. It produces a console app whose `Program.cs` builds a host and registers a single worker. It is the right starting point for a standalone processor - a queue consumer, a scheduled batch job runner, an ETL pipeline - that has no HTTP surface.

Here is a realistic worker that drains an in-memory channel of outbound emails. Note how it acquires a fresh DI scope for each item and never lets one poison message kill the loop.

```csharp
public sealed class EmailDispatchWorker : BackgroundService
{
    private readonly ChannelReader<EmailJob> _reader;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<EmailDispatchWorker> _logger;

    public EmailDispatchWorker(
        Channel<EmailJob> channel,
        IServiceScopeFactory scopeFactory,
        ILogger<EmailDispatchWorker> logger)
    {
        _reader = channel.Reader;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // ReadAllAsync honours the token and completes gracefully on shutdown.
        await foreach (var job in _reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var sender = scope.ServiceProvider.GetRequiredService<IEmailSender>();
                await sender.SendAsync(job, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break; // shutting down - stop cleanly
            }
            catch (Exception ex)
            {
                // One bad message must not tear down the whole worker.
                _logger.LogError(ex, "Failed to dispatch email {JobId}", job.Id);
            }
        }
    }
}
```

Registration is one line, and `System.Threading.Channels` gives you a bounded, back-pressured, thread-safe hand-off between producers (say, a controller) and this consumer:

```csharp
builder.Services.AddSingleton(_ =>
    Channel.CreateBounded<EmailJob>(new BoundedChannelOptions(1000)
    {
        FullMode = BoundedChannelFullMode.Wait
    }));
builder.Services.AddHostedService<EmailDispatchWorker>();
```

> **Best practice:** Prefer a bounded channel over an unbounded one. An unbounded queue turns a downstream slowdown into an out-of-memory crash. Bounded + `Wait` applies back-pressure to producers, which is almost always what you want.

### The outbox-driven worker

In-memory channels have a fatal flaw for anything that matters: **if the process dies, the queue dies with it.** When a user places an order, you write the order to the database *and* you want to publish an `OrderPlaced` event - and doing that as two separate operations means a crash between them loses one side. That is the dual-write problem, and the **transactional outbox** pattern solves it by making the "I need to publish X" fact part of the *same database transaction* as the business change. Chapter 9 covers the mechanics - the outbox table, the atomic commit, and MassTransit's built-in support. Here the point is the other half of the pattern: the background worker that actually drains the table.

The worker polls unprocessed rows and publishes them, marking each as done only after the broker acknowledges:

```csharp
protected override async Task ExecuteAsync(CancellationToken stoppingToken)
{
    using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
    while (await timer.WaitForNextTickAsync(stoppingToken))
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();

        var batch = await db.OutboxMessages
            .Where(m => m.ProcessedOnUtc == null)
            .OrderBy(m => m.OccurredOnUtc)
            .Take(50)
            .ToListAsync(stoppingToken);

        foreach (var msg in batch)
        {
            await bus.PublishAsync(msg.Type, msg.Payload, stoppingToken);
            msg.ProcessedOnUtc = DateTime.UtcNow;
        }
        await db.SaveChangesAsync(stoppingToken);
    }
}
```

`PeriodicTimer` (introduced in .NET 6) is the idiomatic modern loop timer: it is awaitable, allocation-light, cancellation-aware, and it does not stack up ticks if an iteration runs long.

### Graceful shutdown and the `CancellationToken`

When Kubernetes sends `SIGTERM`, or an operator hits Ctrl+C, the host begins a *graceful* shutdown: it signals the `stoppingToken`, then waits up to a timeout (default 30 seconds) for `ExecuteAsync` to complete. Work that ignores the token is work that gets **killed mid-flight** when the timeout expires.

Two obligations fall on you:

1. **Observe the token** in every loop and every `await` that supports it, so you stop *starting* new work promptly.
2. **Finish or safely abandon in-flight work** before the timeout, so you don't leave half-completed operations.

```csharp
builder.Services.Configure<HostOptions>(o =>
    o.ShutdownTimeout = TimeSpan.FromSeconds(60)); // give long units room to drain
```

> **Pitfall:** Passing `CancellationToken.None` to your database and HTTP calls "so they don't get interrupted" is the wrong instinct. It means a shutdown must wait the full timeout and then hard-kill anyway. Thread the real token through, and design each unit of work to be *resumable* rather than uninterruptible.

### Scaling workers, at-least-once delivery, and idempotency

The moment you run more than one instance of your service - and in any serious deployment you will, for availability alone - two copies of that outbox worker are polling the same table. Both may grab the same row. Your message gets published twice.

You cannot engineer this possibility away entirely. Distributed systems give you **at-least-once** delivery as the practical default; exactly-once is a comforting fiction that, when you look closely, is always at-least-once plus idempotent processing (Chapter 9 explains why). So the senior move is to stop fighting duplicates and instead make processing **idempotent** - safe to run more than once with the same net effect. The implementation - dedupe on a natural or supplied idempotency key, with a unique index as your backstop - is covered in Chapter 20; apply it to every handler a worker runs.

For the polling contention itself, options range from a `SELECT ... FOR UPDATE SKIP LOCKED` (PostgreSQL) to claiming rows with an atomic `UPDATE ... SET LockedBy = @me WHERE ...`, to simply electing a single leader (covered in Part B) so only one instance polls at all. The right answer depends on throughput, but the principle is constant: **assume duplicates and design so they don't hurt.**

---

## Part B — Scheduling & Job Frameworks

Workers are great at "keep processing whatever shows up." They are clumsy at "run this at 02:00 every night," "run this once in 15 minutes," or "let me see which jobs failed last week." That is the domain of scheduling frameworks.

### Cron, briefly

A cron expression is a compact schedule spec. The classic Unix form has five fields; Quartz.NET uses a six/seven-field variant that adds seconds (and optionally a year):

```
┌ minute (0-59)
│ ┌ hour (0-23)
│ │ ┌ day of month (1-31)
│ │ │ ┌ month (1-12)
│ │ │ │ ┌ day of week (0-6, Sun=0)
│ │ │ │ │
* * * * *      # every minute
0 3 * * *      # 03:00 every day
*/15 * * * *   # every 15 minutes
0 9 * * 1-5    # 09:00 Monday-Friday
```

> **Pitfall:** Cron runs in a specific time zone. Servers usually run UTC, but "midnight" to a business often means local wall-clock time - which drifts by an hour across daylight-saving transitions. Always be explicit about the zone, and be aware that a job scheduled for 02:30 local may run twice or zero times on DST change days.

### Hangfire

Hangfire's pitch is "background jobs backed by persistent storage, with almost no ceremony." You enqueue a job as a plain method call; Hangfire serializes it, stores it (SQL Server, PostgreSQL, Redis, and others), and a server component picks it up and runs it. Because the job lives in storage, it **survives process restarts** and retries automatically on failure - a categorical upgrade over an in-memory channel.

```csharp
builder.Services.AddHangfire(cfg => cfg
    .UsePostgreSqlStorage(c => c.UseNpgsqlConnection(connectionString)));
builder.Services.AddHangfireServer();

var app = builder.Build();
app.UseHangfireDashboard("/jobs"); // secure this in production!
```

Hangfire distinguishes several job kinds:

```csharp
// Fire-and-forget: runs once, as soon as a worker is free.
BackgroundJob.Enqueue<IEmailSender>(s => s.SendWelcome(userId));

// Delayed: runs once, after a delay.
BackgroundJob.Schedule<IInvoiceService>(
    s => s.ChargeAsync(orderId, CancellationToken.None),
    TimeSpan.FromMinutes(15));

// Continuation: runs after a parent job succeeds.
var parent = BackgroundJob.Enqueue<IReportBuilder>(r => r.Build(reportId));
BackgroundJob.ContinueJobWith<IEmailSender>(parent, s => s.SendReport(reportId));

// Recurring: cron-scheduled, identified by a stable key.
RecurringJob.AddOrUpdate<INightlyCleanup>(
    "nightly-cleanup",
    j => j.RunAsync(CancellationToken.None),
    "0 3 * * *",
    new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });
```

The recurring-job *key* (`"nightly-cleanup"`) is important: it makes the registration idempotent. Deploy ten instances that all call `AddOrUpdate` with the same key and you get one schedule, not ten. The dashboard at `/jobs` gives you a live view of enqueued, processing, succeeded, and failed jobs, with the ability to requeue failures by hand - invaluable operationally.

> **Best practice:** Enqueue *interface method calls* (`Enqueue<IEmailSender>(...)`), not concrete types. Hangfire resolves the implementation from DI at execution time, and your job code stays testable. Also keep job arguments small and serializable - pass an `orderId`, not a fat `Order` object graph.

Hangfire coordinates multiple servers automatically: any number of Hangfire servers can share one storage, and each job is executed by exactly one of them. That distributed coordination out of the box is a large part of its appeal.

### Quartz.NET

Quartz.NET is the .NET port of the venerable Java Quartz scheduler. It is lower-level and more explicit than Hangfire, separating three concepts cleanly:

- A **Job** is the unit of work (`IJob`).
- A **Trigger** decides *when* the job fires (simple interval, cron, calendar-based).
- The **Scheduler** binds jobs to triggers and runs them.

Quartz integrates with the Generic Host via `Quartz.Extensions.Hosting`.

```csharp
public sealed class ReindexJob : IJob
{
    private readonly ISearchIndexer _indexer;
    private readonly ILogger<ReindexJob> _logger;

    public ReindexJob(ISearchIndexer indexer, ILogger<ReindexJob> logger)
        => (_indexer, _logger) = (indexer, logger);

    public async Task Execute(IJobExecutionContext context)
    {
        _logger.LogInformation("Reindex starting at {Time}", DateTimeOffset.UtcNow);
        await _indexer.RebuildAsync(context.CancellationToken);
    }
}
```

```csharp
builder.Services.AddQuartz(q =>
{
    var jobKey = new JobKey("reindex");
    q.AddJob<ReindexJob>(opts => opts.WithIdentity(jobKey));

    q.AddTrigger(t => t
        .ForJob(jobKey)
        .WithIdentity("reindex-nightly")
        .WithCronSchedule("0 0 2 * * ?", x => x.InTimeZone(TimeZoneInfo.Utc)));
});

// Waits for running jobs to finish on shutdown - graceful by default.
builder.Services.AddQuartzHostedService(o => o.WaitForJobsToComplete = true);
```

Quartz's cron format is the six-field Quartz variant (note the leading seconds field and the `?` in a day-of-week/day-of-month slot). Its trigger model is richer than Hangfire's: misfire policies (what to do if the scheduler was down when a trigger should have fired), calendars to exclude holidays, and priority ordering. With `AddPersistentStore` (an ADO.NET job store), Quartz persists schedules and, crucially, uses database locks so that in a clustered deployment **a given trigger fires on exactly one node**.

> Choose Quartz.NET when you need precise, complex scheduling semantics and are comfortable with more configuration. Choose Hangfire when you want fire-and-forget/delayed jobs plus a dashboard with minimal setup. They are not mutually exclusive - some systems run both.

### Hosted service vs Hangfire vs cloud scheduler

| Need | Reach for |
|------|-----------|
| Continuous processing loop, queue consumer | `BackgroundService` / Worker Service |
| Fire-and-forget & delayed jobs, retries, ops dashboard | Hangfire |
| Rich, precise cron/calendar scheduling with misfire handling | Quartz.NET |
| Trigger work without keeping a process alive; serverless | Cloud scheduler (Azure Functions Timer trigger, AWS EventBridge, Kubernetes CronJob) |

The cloud-scheduler option deserves emphasis. If your workload is "run this container for two minutes every hour," standing up an always-on host with an in-process scheduler is wasteful and adds an availability concern (the scheduler node must stay up). A managed cron trigger that spins your job up on demand is cheaper and more robust. The trade-off: you lose the shared in-memory state and the tight feedback of an embedded dashboard, and you take on the cloud provider's scheduling guarantees.

### Ensuring a job runs once across instances

This is the recurring headache of scheduled work in a scaled-out world. Three copies of your service, each with a Quartz scheduler, each firing the nightly cleanup at 02:00 - now it runs three times. Some approaches:

- **Persistent job store with clustering** (Quartz `AddPersistentStore` + `UseClustering`, or Hangfire's shared storage). The framework itself uses DB locks to ensure single execution. This is the simplest correct answer when available.
- **Distributed lock.** Have the job try to acquire a named lock (a row with a unique constraint, a Redis `SET NX PX`, a `SqlServerDistributedLock`); only the winner runs. Release on completion or let a TTL expire.

```csharp
public async Task RunAsync(CancellationToken ct)
{
    await using var handle = await _lockProvider.TryAcquireAsync(
        "nightly-cleanup", TimeSpan.Zero, ct);
    if (handle is null) return; // another instance holds it - stand down

    await DoCleanupAsync(ct);
}
```

- **Leader election.** Elect one instance as leader (via a lease in a coordination store like ZooKeeper, etcd, Consul, or a Kubernetes `Lease` object). Only the leader schedules. This centralizes the decision rather than racing per-job.

> **Best practice:** Give distributed locks a **timeout / TTL** shorter than your cron interval but longer than a normal run. A lock with no expiry, held by an instance that crashes mid-job, deadlocks your schedule forever.

---

## Part C — The Actor Model & Microsoft Orleans

Everything so far treats concurrency as a hazard to be managed with locks, idempotency keys, and database transactions. The actor model offers a different bargain: **structure your system so that shared mutable state, and therefore locks, largely disappear.**

### What is an actor?

An actor is a unit that combines three things:

1. **Private state** - no other actor can touch it directly.
2. **A mailbox** - an inbox of messages addressed to it.
3. **Single-threaded processing** - the actor handles one message at a time, to completion, before starting the next.

Because an actor processes its mailbox serially, the code inside it never runs concurrently with itself. There is *no data race on its state*, because there is never a second thread inside it. You get the correctness of a lock without writing a lock - the concurrency control is structural, baked into how messages are dispatched.

Actors communicate only by sending messages (asynchronously). One actor cannot reach into another and mutate it; it can only ask. This gives you a system of small, isolated, sequential islands that scale by *multiplying* rather than by *sharing*. The archetype is a bank account: model each account as an actor, and concurrent transfers against the same account serialize naturally through its mailbox - no `lock`, no optimistic-concurrency retry loop.

### Microsoft Orleans and "virtual actors" (grains)

Traditional actor frameworks (Erlang, Akka) make you manage actor lifecycles: create them, supervise them, dispose them, worry about where they live. Orleans, born at Microsoft Research and the engine behind Halo's cloud services, introduced the **virtual actor** to remove that burden. In Orleans, an actor is called a **grain**, and the model has a beautiful property: **grains always exist.**

You never create or destroy a grain. You simply address one by identity and call it:

```csharp
var account = client.GetGrain<IAccountGrain>("acct-42");
await account.Deposit(100m);
```

If grain `acct-42` is not currently in memory, the Orleans runtime **activates** it - instantiates it on some server and, if it has persistent state, loads that state. If a grain sits idle, the runtime **deactivates** it to reclaim memory. This activation lifecycle is automatic and invisible to your calling code. From the caller's perspective the grain is eternal; activation is just a caching detail.

Key guarantees the runtime provides:

- **Single-threaded per grain.** By default, only one call executes inside a given grain activation at a time - the same serial-mailbox guarantee, so grain state needs no locks.
- **Location transparency.** A grain reference works the same whether the grain lives on this server or another. The runtime handles routing.
- **Placement & clustering.** Orleans servers are called **silos**. A group of silos forms a **cluster**. The runtime places activations across silos, balances load, and - when a silo dies - reactivates its grains elsewhere. Your code doesn't change as you scale from one silo to fifty.
- **Persistence.** A grain can declare persistent state; the runtime loads it on activation and you write it back explicitly.

### A small grain example

An interface (the contract, marked with a key type) and an implementation:

```csharp
// Contract - shared between silo and clients.
public interface IAccountGrain : IGrainWithStringKey
{
    Task Deposit(decimal amount);
    Task<bool> Withdraw(decimal amount);
    Task<decimal> GetBalance();
}
```

```csharp
// Implementation, with runtime-managed persistent state.
public sealed class AccountGrain : Grain, IAccountGrain
{
    private readonly IPersistentState<AccountState> _state;

    public AccountGrain(
        [PersistentState("account", "accountStore")]
        IPersistentState<AccountState> state) => _state = state;

    public async Task Deposit(decimal amount)
    {
        _state.State.Balance += amount;
        await _state.WriteStateAsync(); // persist the mutation
    }

    public async Task<bool> Withdraw(decimal amount)
    {
        if (_state.State.Balance < amount) return false; // no lock needed:
        _state.State.Balance -= amount;                  // one call at a time
        await _state.WriteStateAsync();
        return true;
    }

    public Task<decimal> GetBalance() => Task.FromResult(_state.State.Balance);
}

[GenerateSerializer]
public sealed class AccountState
{
    [Id(0)] public decimal Balance { get; set; }
}
```

Notice there is not a single `lock`, `Interlocked`, or transaction in that `Withdraw` - yet two concurrent transfers against `acct-42` cannot corrupt the balance, because Orleans runs them one after another inside the one activation. That is the whole payoff of the model made concrete.

Hosting a silo is a few lines on the Generic Host (Orleans 7+ integrates directly):

```csharp
var builder = Host.CreateApplicationBuilder(args);
builder.UseOrleans(silo =>
{
    silo.UseLocalhostClustering()          // real deployments use Azure/ADO.NET clustering
        .AddMemoryGrainStorage("accountStore"); // real deployments use a durable store
});
using var host = builder.Build();
await host.RunAsync();
```

Swap `UseLocalhostClustering` for a clustering provider (Azure Table Storage, ADO.NET, Redis) and `AddMemoryGrainStorage` for a durable store, and the *same grain code* now runs across a multi-silo cluster with failover. That continuity from laptop to production cluster is Orleans' signature strength.

### When Orleans fits - and when it doesn't

Orleans shines when you have **many small, independently-addressable, stateful entities** with high throughput and low latency, where keeping state in memory (rather than round-tripping a database on every call) is a decisive win:

- **Gaming** - player sessions, matches, leaderboards (its origin story).
- **IoT** - a grain per device, holding the device's live state.
- **Real-time systems** - chat rooms, collaborative documents, live dashboards, ride-hailing dispatch.
- **Per-entity workflows** - a grain per order, per shopping cart, per user session, coordinating that entity's lifecycle.

It is a poor fit when:

- Your workload is **stateless request/response** over a shared database - a plain ASP.NET Core service is simpler and you gain nothing from grains.
- You need **set-based operations** - "sum every account's balance" fights the model, which is built around addressing entities one at a time, not scanning them.
- Your team is small and the operational cost of running a stateful cluster (clustering provider, storage, monitoring, understanding activation/placement) outweighs the benefit. Actors are a genuine paradigm shift with a real learning curve.

> **Pitfall:** A grain's single-threaded guarantee is per-activation, not global. If you make a hot "singleton" grain that every request must call, you have re-created a bottleneck - serialized through one mailbox. Model for *many* grains with well-distributed keys, not a few god-grains.

### Neighbors: Akka.NET and Dapr actors

Orleans is not the only actor game in .NET. **Akka.NET** is a faithful port of the JVM's Akka: it exposes classic actors with explicit lifecycles, hierarchical supervision (parents restart failed children per a strategy), and location transparency. It gives you more control - and more responsibility - than Orleans' virtual actors, and it's the natural choice if you want the traditional supervision-tree model or you're porting from Akka.

**Dapr** (Distributed Application Runtime) offers actors as one building block among many, delivered as a **language-agnostic sidecar**. Dapr's virtual-actor model is conceptually close to Orleans (turn-based single-threaded access, automatic activation, state persistence) but you interact with it over HTTP/gRPC from any language, and it slots into a broader platform of pub/sub, state stores, and service invocation. Choose Dapr when polyglot services and a portable, infrastructure-managed runtime matter more than the deep, .NET-native ergonomics Orleans provides.

---

## Wrapping Up

The arc of this chapter is a maturation in how you think about "later" work. A `BackgroundService` runs a loop; but the senior questions are about shutdown, duplicates, and scale-out. Job frameworks like Hangfire and Quartz.NET give you persistence, retries, cron, and - critically - single-execution guarantees across a cluster, so you stop hand-rolling schedulers. And the actor model, realized in Orleans' virtual grains, inverts the concurrency problem entirely: instead of guarding shared state with locks, you partition state into single-threaded islands where races cannot occur by construction. Reach for each when its shape matches your problem, and always design as if the process will crash mid-operation and run twice - because in production, eventually, it will.

## Sources & Further Reading

- Microsoft Learn — *Worker Services in .NET* and *Background tasks with hosted services in ASP.NET Core* (`IHostedService`, `BackgroundService`, graceful shutdown).
- Microsoft Learn — *Implement the outbox pattern* and .NET microservices architecture guidance (transactional outbox, at-least-once, idempotency).
- Microsoft Learn — *Microsoft Orleans documentation*: Overview, Grains, Grain persistence, Silos & clustering, and the "Hello World" / minimal application tutorials (https://learn.microsoft.com/dotnet/orleans/).
- Hangfire Documentation — Background Methods (fire-and-forget, delayed, recurring, continuations), Dashboard, and persistent storage providers (https://docs.hangfire.io/).
- Quartz.NET Documentation — Jobs and Triggers, Cron Triggers, and hosted-service integration (https://www.quartz-scheduler.net/documentation/).
- Akka.NET Documentation — Actors and supervision (https://getakka.net/).
- Dapr Documentation — Actors building block (https://docs.dapr.io/developing-applications/building-blocks/actors/).
