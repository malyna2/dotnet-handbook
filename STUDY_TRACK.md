# Study Track: Closing Common Middle-Level Gaps

> A working file, like [`PRACTICE_ROADMAP.md`](PRACTICE_ROADMAP.md), not a chapter. It is the shortest path through this handbook for four topics where middle-level .NET developers most often have gaps. Take them in this order: **async internals → messaging guarantees → indexes and execution plans → code review**. The rest of the book is under [Later](#later).

**How to use it.** One topic at a time:

1. Read the sections in the order listed.
2. Run the experiment and compare your output with the expected one.
3. Answer the three questions without notes, then open the answers.
4. Do the check at work.

Before an interview, reread [Pay attention to](#pay-attention-to).

| Topic | Reading | Hands-on | With a second read |
|---|---|---|---|
| [1. Async internals](#1-async-internals) | 40 min | 1 h 35 min | ≈ 2 h 55 min |
| [2. Messaging guarantees](#2-messaging-guarantees) | 45 min | 1 h 20 min | ≈ 2 h 50 min |
| [3. Indexes and execution plans](#3-indexes-and-execution-plans) | 40 min | 4 h 55 min | ≈ 6 h 15 min |
| [4. Code review](#4-code-review) | 30 min | 2 h 25 min | ≈ 3 h 25 min |
| **Total** | **≈ 2 h 35 min** (cap: 10 h) | **≈ 10 h 15 min** | **≈ 15 h 25 min: about three weeks at 5 hours a week** |

*Reading* is the build's own formula (prose at ~200 words a minute, code at ~60), summed over the listed sections. *Hands-on* is the experiments, exercises, questions and check at work, broken down under each topic. *With a second read* counts every section twice, once before the experiment and once after, plus the hands-on time.

## Running the experiments

Each topic has a *Prove it* program of at most 30 lines. All of them live in [`verify/study-track/`](verify/study-track/), where `verify.sh` checks that the code printed here is the code that runs, and a test runs every program and checks its output. You need the .NET 10 SDK; the last two programs also need Docker.

```bash
cd verify/study-track
dotnet run --project AsyncVoid
dotnet run --project WhenAll
dotnet run --project Starvation -- await
dotnet run --project Starvation -- block
dotnet run --project HttpClientPerRequest
dotnet run --project CheckThenAct
ACCEPT_EULA=Y docker compose up -d      # SQL Server and the Service Bus emulator; both have EULAs
dotnet run --project PeekLock
dotnet run --project SeekVsScan
docker compose down
```

On Windows PowerShell, set the variable first: `$env:ACCEPT_EULA="Y"; docker compose up -d`.

The expected outputs below are copied from [`verify/study-track/reference-runs/`](verify/study-track/reference-runs/). Every file there starts with the environment it ran on: 4 vCPU Xeon, 16 GB RAM, Ubuntu 24.04, .NET 10.0.12, SQL Server 2022 CU27, Service Bus emulator 2.0.1. Counts, exception types and plan shapes should match on your machine. Seconds won't. Only Linux x64 was run.

---

## 1. Async internals

Covers:
- why an `async void` exception can't be caught;
- what `await Task.WhenAll` throws;
- how sync-over-async starves the thread pool;
- why a new `HttpClient` per request exhausts sockets.

### The mechanism to explain without notes

**`await` hands the thread back and parks the rest of the method on the task; the task is the only thing that carries the result or the exception back.**

At an `await` on an unfinished task, the compiler-generated state machine registers "run the rest of me" as a continuation and returns to its caller, so the thread goes back to the pool. When the task completes, the continuation is queued to a pool thread, or posted to the captured `SynchronizationContext`. There `GetResult()` returns the value or rethrows the first stored exception. Every trap in this topic follows from two facts:

- **The returned `Task` is the only channel for the exception.** Take it away (`async void`) and the method builder has nowhere to put the exception but the `SynchronizationContext`. When there is none — ASP.NET Core, workers, console apps — it goes to a thread-pool thread, where nobody catches it and the process dies.
- **A continuation needs a free pool thread.** Block threads with `.Result`, and the work that would unblock them waits in the queue while the pool adds threads only gradually.

Sockets follow the same rule as threads: they are pooled in the handler underneath `HttpClient`, and a new client per request bypasses the pool.

**Time:** reading ≈ 40 min; hands-on ≈ 1 h 35 min — four experiments 40 min, the exercise 10, the questions 15, the check at work 30.

### Read (≈ 40 min)

1. [Ch 8 · Why Async Exists: I/O-Bound vs CPU-Bound Work](chapters/08-async.md#why-async-exists-io-bound-vs-cpu-bound-work) and [Tasks: The Promise of a Future Result](chapters/08-async.md#tasks-the-promise-of-a-future-result).
2. [Ch 8 · async/await, Deeply](chapters/08-async.md#asyncawait-deeply): the state machine, and the `async void` pitfall at its end.
3. [Ch 8 · SynchronizationContext and ConfigureAwait](chapters/08-async.md#synchronizationcontext-and-configureawait).
4. [Ch 8 · The Sync-Over-Async Deadlock](chapters/08-async.md#the-sync-over-async-deadlock): the last two paragraphs are the starvation mechanism.
5. [Ch 8 · Composing Concurrent Work: WhenAll and WhenAny](chapters/08-async.md#composing-concurrent-work-whenall-and-whenany).
6. [Ch 5 · The Mechanics That Bite](chapters/05-patterns.md#the-mechanics-that-bite): `ExceptionDispatchInfo`, the tool `await` uses to rethrow with the original stack trace.
7. [Ch 3 · IHttpClientFactory & Resilience with Polly](chapters/03-aspnetcore.md#ihttpclientfactory--resilience-with-polly) — up to *Resilience with Polly*.
8. [Ch 20 · Keep-Alive, Connection Pooling, and Socket Exhaustion](chapters/20-networking.md#keep-alive-connection-pooling-and-socket-exhaustion).
9. [Ch 51 · Case 3 — Intermittent timeouts under load, with every dashboard green](chapters/51-azure-casebook.md#case-3--intermittent-timeouts-under-load-with-every-dashboard-green): the same bug on App Service, as SNAT port exhaustion.
10. [Ch 34 · Diagnosing a Performance Problem](chapters/34-interview.md#diagnosing-a-performance-problem-a-worked-methodology): how starvation looks from the outside (low CPU, high latency).

**Do:** [Ch 8 · Exercises](chapters/08-async.md#exercises). Answer *Find the bug* before you open the answer.

### Prove it

**1a. An `async void` exception kills the process.** Predict both outputs first.

```csharp
// Prove it: an exception from an async void method cannot reach its caller, and it ends the process.
try
{
    await SaveAsync(-1);                       // async Task: the exception travels inside the Task
}
catch (ArgumentOutOfRangeException e)
{
    Console.WriteLine($"async Task: the caller caught {e.GetType().Name}");
}

try
{
    Save(-1);                                  // async void: there is no Task to carry the exception
    Console.WriteLine("async void: the call returned normally and the catch below never ran");
}
catch (ArgumentOutOfRangeException e)
{
    Console.WriteLine($"async void: the caller caught {e.GetType().Name}");   // never printed
}

await Task.Delay(1000);                        // the process dies in here, on a thread-pool thread
Console.WriteLine("still alive");              // never printed

static async Task SaveAsync(int id) { ArgumentOutOfRangeException.ThrowIfNegative(id); await Task.Delay(10); }

static async void Save(int id) { ArgumentOutOfRangeException.ThrowIfNegative(id); await Task.Delay(10); }
```

```text
async Task: the caller caught ArgumentOutOfRangeException
async void: the call returned normally and the catch below never ran
Unhandled exception. System.ArgumentOutOfRangeException: id ('-1') must be a non-negative value. (Parameter 'id')
Actual value was -1.
   at System.ArgumentOutOfRangeException.ThrowNegative[T](T value, String paramName)
   at System.ArgumentOutOfRangeException.ThrowIfNegative[T](T value, String paramName)
   at Program.<<Main>$>g__Save|0_1(Int32 id) in …/AsyncVoid/Program.cs:line 26
   at System.Threading.Tasks.Task.<>c.<ThrowAsync>b__124_1(Object state)
   at System.Threading.ThreadPoolWorkQueue.Dispatch()
   at System.Threading.PortableThreadPool.WorkerThread.WorkerThreadStart()
   at System.Threading.Thread.StartCallback()
```

The process exits with code 134 on Linux; it is non-zero everywhere. What to notice:

- **The exception is thrown before the first `await`, and the caller still can't catch it.** The compiler wraps the whole method body in the state machine's own `try/catch`. That catch hands the exception to the builder, so the call returns normally.
- **The bottom frames are the builder rethrowing on the pool.** `Task.ThrowAsync` runs on `ThreadPoolWorkQueue.Dispatch`, a thread-pool thread with no caller above it, so the exception is unhandled. This is `AsyncVoidMethodBuilder.SetException` in the runtime source.

**1b. `await Task.WhenAll` throws one exception.**

```csharp
// Prove it: await Task.WhenAll rethrows ONE exception; the WhenAll task holds all of them.
Task first = Fail(300, "A (listed first, fails last)");
Task second = Fail(50, "B (listed second, fails first)");
Task all = Task.WhenAll(first, second);

try
{
    await all;
}
catch (Exception e)
{
    Console.WriteLine($"await threw:   {e.GetType().Name}: {e.Message}");
    Console.WriteLine($"all.Exception: {all.Exception!.InnerExceptions.Count} inner exceptions");
    foreach (Exception inner in all.Exception.InnerExceptions)
        Console.WriteLine($"  - {inner.Message}");
}

try { all.Wait(); }                             // the blocking API throws the wrapper instead
catch (AggregateException e) { Console.WriteLine($".Wait() threw: AggregateException with {e.InnerExceptions.Count} inner exceptions"); }

try { await Task.WhenAll(FailTyped(300, "A"), FailTyped(50, "B")); }
catch (Exception e) { Console.WriteLine($"WhenAll over Task<int> threw: {e.Message} (argument order this time)"); }

static async Task Fail(int ms, string name) { await Task.Delay(ms); throw new InvalidOperationException(name); }
static async Task<int> FailTyped(int ms, string name) { await Task.Delay(ms); throw new InvalidOperationException(name); }
```

```text
await threw:   InvalidOperationException: B (listed second, fails first)
all.Exception: 2 inner exceptions
  - B (listed second, fails first)
  - A (listed first, fails last)
.Wait() threw: AggregateException with 2 inner exceptions
WhenAll over Task<int> threw: A (argument order this time)
```

What to notice:

- **`await` rethrows the first exception in the task's list.** Since .NET 8, `WhenAll` over plain `Task`s lists failures in the order the tasks *fail*; the .NET 7 source walked them in argument order. Over `Task<T>`, it is still argument order. Don't depend on either: log `all.Exception?.InnerExceptions`.
- **`.Exception` can be null.** When the `WhenAll` task was cancelled rather than faulted, `all.Exception` is `null`, so the `!` above is only safe for a fault.

**1c. Sync-over-async starves the pool.** Run it twice: `-- await`, then `-- block`.

```csharp
using System.Diagnostics;

// Prove it: blocking on async work starves the thread pool; awaiting it does not.
// Run twice: `dotnet run -- await` and `dotnet run -- block`.
bool block = args.FirstOrDefault() == "block";
int requests = 50 * Environment.ProcessorCount;
var clock = Stopwatch.StartNew();

Task[] work = Enumerable.Range(0, requests).Select(_ => Task.Run(async () =>
{
    if (block) Task.Delay(1000).Wait();        // sync-over-async: the thread waits for the "I/O"
    else await Task.Delay(1000);               // async: the thread goes back to the pool
})).ToArray();

var probe = Stopwatch.StartNew();
await Task.Run(() => { });                     // one tiny unrelated request, queued behind them
Console.WriteLine($"a tiny unrelated request waited {probe.ElapsedMilliseconds} ms for a thread");

int peakThreads = 0;
while (!work.All(t => t.IsCompleted))
{
    peakThreads = Math.Max(peakThreads, ThreadPool.ThreadCount);
    await Task.Delay(50);
}
Console.WriteLine($"{requests} requests of 1 s each with {(block ? ".Wait()" : "await")}: " +
    $"done in {clock.Elapsed.TotalSeconds:F1} s, peak pool threads {peakThreads}");
```

```text
a tiny unrelated request waited 6 ms for a thread
200 requests of 1 s each with await: done in 1.1 s, peak pool threads 3

a tiny unrelated request waited 10542 ms for a thread
200 requests of 1 s each with .Wait(): done in 11.6 s, peak pool threads 68
```

What to notice:

- **Same work, ten times slower.** Awaiting, 200 one-second requests finish in 1.1 s on 3 threads: nobody holds a thread while waiting. Blocking, the same work takes 11.6 s, and the pool has to grow to 68 threads.
- **The unrelated request is the outage.** It waited 10.5 s, queued behind the blocked work. In production, every endpoint slows down, including ones that never block.
- **The CPU had nothing to do.** The whole blocking run used 0.46 s of CPU in 11.7 s of wall time on 4 cores: the threads were waiting, not working. Low CPU with high latency is how starvation looks from the outside.
- **Why it ends at all.** Since .NET 6, a pool thread that blocks in `Task.Wait` (which `.Result` and `.GetAwaiter().GetResult()` also use) tells the pool, which compensates.
  - On .NET 10, once the pool is past its minimum (one thread per core by default), it adds up to one more thread per core with no delay.
  - After that it adds one thread at a time. The pause before each starts at 25 ms, grows by 25 ms with every further batch of one-thread-per-core, and stops growing at 250 ms (`PortableThreadPool.Blocking.cs`).
  - Blocking the pool isn't told about — `Thread.Sleep`, `SemaphoreSlim.Wait`, a contended `lock` — gets only the starvation detector. That adds about one thread per half second, and slower when the CPU is busy.

**1d. A new `HttpClient` per request leaves a socket behind every time.**

```csharp
using System.Net.NetworkInformation;

// Prove it: a new HttpClient per request opens (and closes) one TCP connection per request, and
// every closed connection then sits in TIME_WAIT. A shared client reuses its pooled connections.
int port = Random.Shared.Next(20_000, 30_000);     // a fresh port, so earlier runs don't count
var builder = WebApplication.CreateSlimBuilder();
builder.Logging.ClearProviders();
builder.WebHost.UseUrls($"http://127.0.0.1:{port}");
var server = builder.Build();
server.MapGet("/", () => "ok");
await server.StartAsync();

var shared = new HttpClient();
await Measure("one shared HttpClient", () => shared.GetStringAsync($"http://127.0.0.1:{port}/"));
await Measure("new HttpClient per request", async () =>
{
    using var perRequest = new HttpClient();
    return await perRequest.GetStringAsync($"http://127.0.0.1:{port}/");
});

async Task Measure(string label, Func<Task<string>> call)
{
    int before = SocketsInTimeWait();
    for (int i = 0; i < 500; i++) await call();
    Console.WriteLine($"{label,-27} 500 requests, new sockets in TIME_WAIT: {SocketsInTimeWait() - before}");
}

int SocketsInTimeWait() => IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpConnections()
    .Count(c => c.State == TcpState.TimeWait && c.RemoteEndPoint.Port == port);
```

```text
one shared HttpClient       500 requests, new sockets in TIME_WAIT: 0
new HttpClient per request  500 requests, new sockets in TIME_WAIT: 500
```

What to notice:

- **One shared client: zero new sockets.** It reused one pooled connection for all 500 requests.
- **A client per request: 500 sockets in `TIME_WAIT`.** Each client opened a connection and closed it on `Dispose`. The side that closes keeps the socket, and its local port, in `TIME_WAIT` — 60 s on Linux, fixed in the kernel (`TCP_TIMEWAIT_LEN`).
- **At production rates the ports run out.** On App Service the limit arrives much sooner: 128 preallocated SNAT ports per instance ([Ch 51 Case 3](chapters/51-azure-casebook.md#case-3--intermittent-timeouts-under-load-with-every-dashboard-green)).

### Three questions

**1.** A `try/catch` wraps a call to an `async void` method that throws on its first line, before any `await`. Why doesn't the catch run, and why does the whole process die instead of one request failing?

<details>
<summary>Answer</summary>

- **The method body never runs outside the state machine.** The compiler moves it into `MoveNext`, wrapped in its own `try/catch`, so any exception is caught there and handed to the method builder — even one thrown before the first `await`. The call returns normally.
- **`async Task` has somewhere to put it.** Its builder stores the exception in the returned `Task`, and the caller sees it when it awaits.
- **`async void` doesn't.** `AsyncVoidMethodBuilder.SetException` posts a rethrow to the captured `SynchronizationContext`. When there is none (ASP.NET Core, workers, console, isolated Azure Functions), it queues the rethrow to the thread pool. An exception escaping a pool work item has no request boundary above it: it is unhandled, and the runtime ends the process.

Fix: return `Task`. Hand real fire-and-forget work to a queue or `BackgroundService` that observes failures. Keep `async void` for event handlers, with a `try/catch` around their whole body.
</details>

**2.** `await Task.WhenAll(a, b)`, and both fail. Why does the catch see one exception, why does `.Wait()` on the same task throw something else, and which one does `await` give you?

<details>
<summary>Answer</summary>

- **The `WhenAll` task stores both exceptions.**
- **`await` throws one on purpose.** It calls `GetResult()`, which rethrows only the first stored exception through `ExceptionDispatchInfo`, so async code reads like synchronous code: one exception, with its original stack trace.
- **`.Wait()` and `.Result` throw the wrapper.** They throw the `AggregateException` itself.
- **"First" means first in the list.** Over plain `Task`s since .NET 8, that is the first task to fail. Over `Task<T>`, it is the first in argument order.

Never depend on which one you get: keep the `WhenAll` task in a variable and log `task.Exception?.InnerExceptions`. It is `null` when the task was cancelled, not faulted.
</details>

**3.** `.Result` in a request handler and `new HttpClient()` per request both pass every test and fail only under load. Which finite resource does each exhaust, and why only at high concurrency?

<details>
<summary>Answer</summary>

- **`.Result` exhausts pool threads.** It pins a pool thread for the whole I/O wait. At low concurrency there are spare threads. At high concurrency every thread is blocked, and the continuations and timer callbacks that would unblock them sit in the queue. The pool adds threads only gradually: a burst of one per core, then one at a time with pauses of up to 250 ms. Throughput collapses to the injection rate, latency spreads to every endpoint, and CPU stays low.
- **`new HttpClient()` per request exhausts local ports.** Each request gets a new handler, so a new connection pool and a new TCP connection. The connection closes on dispose, and the socket then stays in `TIME_WAIT` holding a local port (60 s on Linux). Once new connections per minute approach the number of ports — or 128 SNAT ports per App Service instance — new connections wait and time out.

A test makes a handful of requests, one after another, so neither resource runs out.
</details>

### Check at work

**Inspect.** Search your service for `async void`, `.Result`, `.Wait()`, `.GetAwaiter().GetResult()` and `new HttpClient(`. Sort every hit into one of three bins:

- an event handler;
- startup code;
- a request or message path — a latent outage.

Then check that something charts the thread-pool queue length and thread count for the service. These are `dotnet.thread_pool.queue.length` and `dotnet.thread_pool.thread.count` on .NET 9+, and the `threadpool-queue-length` and `threadpool-thread-count` counters on .NET 8. Without them, starvation looks exactly like a slow database.

---

## 2. Messaging guarantees

Covers:
- Service Bus peek-lock and redelivery;
- message ordering and sessions;
- the transactional outbox and the dual-write problem;
- long-running operations: 202, a queue and a worker.

### The mechanism to explain without notes

**A message leaves the queue only when a consumer settles it while still holding its lock; every other path is a redelivery.**

Peek-lock hides a message from other receivers for the lock duration (1 minute by default, 5 at most). `Complete` inside the lock deletes it. Anything else — a crash, a thrown exception (abandon), a lost settlement, an expired lock — makes it visible again with `DeliveryCount + 1`. After `MaxDeliveryCount` (10 by default) it is dead-lettered. Delivery is therefore at-least-once by construction. Duplicates and reordering are normal, and only an **idempotent effect** makes them harmless: deduplicate on a key that is claimed atomically with the effect.

The producer has the mirror-image problem. A database commit and a broker publish are two independent operations, so no code ordering makes "saved ⇔ published" hold. The only fix is to write the message into the *same database transaction* as the data (the **outbox**) and publish it after the commit — at-least-once again.

**Time:** reading ≈ 45 min; hands-on ≈ 1 h 20 min — the emulator and the experiment 25 min, the exercise 10, the questions 15, the check at work 30.

### Read (≈ 45 min)

1. [Ch 9 · Competing Consumers](chapters/09-messaging.md#competing-consumers), [Dead-Letter Queues (DLQ)](chapters/09-messaging.md#dead-letter-queues-dlq) and [Message Ordering](chapters/09-messaging.md#message-ordering).
2. [Ch 9 · Delivery Guarantees](chapters/09-messaging.md#delivery-guarantees), including *Why Exactly-Once Is (Almost) a Myth* and *Deduplication*.
3. [Ch 9 · Idempotent Consumers](chapters/09-messaging.md#idempotent-consumers) and [The Outbox Pattern](chapters/09-messaging.md#the-outbox-pattern).
4. [Ch 50 · Service Bus](chapters/50-azure-in-depth.md#service-bus): the peek-lock diagram, lock duration, sessions, duplicate detection, processor defaults.
5. [Ch 51 · Case 5 — Customers charged twice](chapters/51-azure-casebook.md#case-5--customers-charged-twice-the-batch-that-outlived-its-locks) and [Case 6 — 40,000 messages in the dead-letter queue](chapters/51-azure-casebook.md#case-6--40000-messages-in-the-dead-letter-queue-and-nobody-knew).
6. [Ch 3 · Idempotency Keys: Making POST Retry-Safe](chapters/03-aspnetcore.md#idempotency-keys-making-post-retry-safe). Read it for the mechanics: why the claim must be inserted *before* the effect, under a unique index, in the same transaction. The same reasoning applies to a message ID.
7. [Ch 33 · Scenario 2 — The lost write](chapters/33-scenarios.md#scenario-2--the-lost-write-the-user-got-200-but-the-data-never-saved): dual write, the outbox, "202 with a status URL".
8. [Ch 23 · Change Data Capture (CDC)](chapters/23-data-scale.md#change-data-capture-cdc), including *Transactional Outbox vs. CDC*: Debezium as the outbox relay.
9. [Ch 22 · `IHostedService` and `BackgroundService`](chapters/22-background-actors.md#ihostedservice-and-backgroundservice), [The outbox-driven worker](chapters/22-background-actors.md#the-outbox-driven-worker) and [Scaling workers, at-least-once delivery, and idempotency](chapters/22-background-actors.md#scaling-workers-at-least-once-delivery-and-idempotency).
10. [Ch 26 · Tie it together: offload the heavy work](chapters/26-realworld-essentials.md#tie-it-together-offload-the-heavy-work) and [Ch 51 · Case 11 — Large uploads fail at almost exactly four minutes](chapters/51-azure-casebook.md#case-11--large-uploads-fail-at-almost-exactly-four-minutes), the 230-second front-end limit.

The book has no single section on the full 202 pattern. Its shape is the answer to question 3 below, after Microsoft's *Asynchronous Request-Reply* pattern in the Azure Architecture Center.

**Do:** [Ch 51 · Exercises](chapters/51-azure-casebook.md#exercises), *Find the bug* 2: the batch that outlives its locks, verified against the same emulator as below.

### Prove it

**2. Peek-lock is a lease.** Needs `docker compose up -d` (see [Running the experiments](#running-the-experiments)). It takes about 10 seconds.

```csharp
using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;

// Prove it: peek-lock is a lease, not a hand-over. A handler that outlives the lock gets the same
// message again. Needs the Service Bus emulator: `docker compose up -d` in this folder.
const string Emulator = "Endpoint=sb://localhost;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;";
string queue = "payments-" + Guid.NewGuid().ToString("N")[..8];
var admin = new ServiceBusAdministrationClient(Emulator.Replace("sb://localhost", "sb://localhost:5300"));
await admin.CreateQueueAsync(new CreateQueueOptions(queue) { LockDuration = TimeSpan.FromSeconds(5) });

await using var client = new ServiceBusClient(Emulator);
await client.CreateSender(queue).SendMessageAsync(new ServiceBusMessage("charge order 42") { MessageId = "order-42" });
ServiceBusReceiver receiver = client.CreateReceiver(queue);                // peek-lock is the default mode

ServiceBusReceivedMessage first = await receiver.ReceiveMessageAsync();
Console.WriteLine($"received {first.MessageId}, DeliveryCount={first.DeliveryCount}, locked for 5 s");
await Task.Delay(TimeSpan.FromSeconds(8));                                 // the "charge" takes 8 s
ServiceBusReceivedMessage second = await receiver.ReceiveMessageAsync(TimeSpan.FromSeconds(10));
Console.WriteLine($"received {second.MessageId} again, DeliveryCount={second.DeliveryCount}");

try { await receiver.CompleteMessageAsync(first); }
catch (ServiceBusException e) when (e.Reason == ServiceBusFailureReason.MessageLockLost)
{
    Console.WriteLine("completing the first copy threw MessageLockLost; its charge already happened");
}
await receiver.CompleteMessageAsync(second);
Console.WriteLine($"completed the second copy; message still in the queue: {await receiver.PeekMessageAsync() is not null}");
```

```text
received order-42, DeliveryCount=1, locked for 5 s
received order-42 again, DeliveryCount=2
completing the first copy threw MessageLockLost; its charge already happened
completed the second copy; message still in the queue: False
```

What to notice:

- **The same `MessageId` came back with `DeliveryCount=2`.** The lock expired at 5 s while the handler was still "charging", so Service Bus made the message visible again.
- **Completing the first copy fails, but its side effect already happened.** In production, that is the duplicate charge.
- **Duplicate detection would not have helped.** It drops a second *send* with the same `MessageId`; this is one message delivered twice.
- **Lock renewal removes this trigger, not the others.** `ServiceBusProcessor` renews the lock while your handler runs (`MaxAutoLockRenewalDuration`, 5 minutes by default). Crashes and lost settlements still redeliver, so the handler must be idempotent anyway.
- **The numbers are scaled down** so the run takes seconds: a 5-second lock and an 8-second "charge". Azure's default lock is 1 minute, and 5 minutes is the maximum.

### Three questions

**1.** Your handler charged the card, then `CompleteMessageAsync` threw `MessageLockLost`. What happened, what happens next, and why doesn't Service Bus duplicate detection protect you?

<details>
<summary>Answer</summary>

**What happened.** The lock — a lease of `LockDuration`, 1 minute by default — expired before settlement. Either the handler ran longer than the lock, or the message waited, already locked, in a prefetch buffer or a received batch, whose locks all start at receive time.

**What happens next.** Service Bus has made the message visible again. A receiver gets it with `DeliveryCount + 1` and charges again, unless the handler is idempotent. After `MaxDeliveryCount` (10 by default) attempts it is dead-lettered.

**Why duplicate detection doesn't help.** It discards a newly *sent* message whose `MessageId` was already seen within its window: a guard against a producer that retries. A redelivery is the same message delivered again, which duplicate detection never sees.

**Fix:**
- use `ServiceBusProcessor` with lock auto-renewal, and no oversized batches or prefetch;
- make the handler idempotent: pass a payment idempotency key downstream, and claim the message ID in the same transaction as the effect.
</details>

**2.** Events are enqueued in order, yet the consumer applies `OrderShipped` before `OrderPaid`. How, and how do sessions fix it — at what cost?

<details>
<summary>Answer</summary>

**How.** FIFO enqueue isn't ordered processing:
- competing consumers — several instances, or `MaxConcurrentCalls > 1` — handle messages concurrently, and they finish in any order;
- an abandoned or lock-expired message is redelivered behind later ones;
- prefetch buffers interleave messages across receivers.

**Sessions.** The sender sets `SessionId` (say, the order ID) on a session-enabled queue or subscription. A receiver accepts a session and holds an exclusive lock on all of its messages, current and future, and receives them in order. There is one receiver per session at a time, with many sessions in parallel.

**Costs:**
- throughput per key is one consumer;
- a failing message blocks its session: an abandoned message is served again by the next receive, until it is dead-lettered (head-of-line blocking);
- a session-enabled entity accepts only messages that carry a `SessionId`, and sessions are chosen when the entity is created;
- a hot key becomes a hot session.

**When strict order isn't needed**, carry a version or sequence number, and make the consumer ignore anything older than the state it already has.
</details>

**3.** A `POST` must save an order, publish `OrderPlaced`, and start a ten-minute document generation. Why is "save, then publish, then generate, then return 200" wrong, and what does the robust design look like end to end?

<details>
<summary>Answer</summary>

**Three failures:**
1. **A dual write.** The commit and the publish are separate. A crash or a failed publish between them leaves an order nobody hears about, or an event for an order that rolled back. An in-process retry doesn't help if the process died.
2. **Ten minutes inside an HTTP request.** Front ends cut it (App Service: 230 s). Clients retry, which duplicates the work, and a thread and a connection are held the whole time.
3. **A 200 that is only a hope.** It promises work that happens only if nothing crashes.

**The robust shape:**
1. One database transaction writes the order, an outbox row and a job row (`Pending`).
2. The response is `202 Accepted` with `Location: /jobs/{id}` and `Retry-After`.
3. After the commit, a relay publishes the outbox rows at-least-once: a polling worker, or Debezium's outbox event router reading the table.
4. A worker takes the job from a queue, runs it idempotently (deduplicated on the job ID) and updates the job's status.
5. `GET /jobs/{id}` returns `200` with the status while the job runs, and `303 See Other` to the result when it is done.
6. Clients send an `Idempotency-Key` with the `POST`.
</details>

### Check at work

**Ask your team lead:** "When one of our Service Bus consumers gets the same message twice, what stops the effect from happening twice? Where exactly is that check, and does it commit in the same transaction as the effect?"

If the answer involves Debezium, ask what it reads: an outbox table, which is the outbox pattern, or raw table changes, which is CDC with no designed contract.

---

## 3. Indexes and execution plans

Covers:
- the leftmost-prefix rule for composite indexes;
- seek versus scan: sargability and implicit conversion;
- N+1 through lazy loading.

### The mechanism to explain without notes

**An index is a copy of some columns kept sorted by its key, left to right. A seek jumps to one contiguous range of that order; a scan reads all of it.**

The optimizer can seek only when the predicate pins a *leftmost prefix* of the key, using the stored values themselves. Two things defeat it:

- **A predicate on a non-leading column.** It has no contiguous range: the rows for one date are scattered across every city's block of a `(City, CreatedAt)` index.
- **A function or a conversion applied to the column** — `LOWER(Email)`, `CONVERT_IMPLICIT(nvarchar, Email)`. It asks about values the index doesn't store.

Both scan. N+1 is the opposite failure: every plan is a cheap seek, and the cost is the number of round trips, which no single plan can show.

**Time:** reading ≈ 40 min; hands-on ≈ 4 h 55 min — the experiment 15 min, the Chapter 4 exercise 10, the lab subset 3 h 45 min, the questions 15, the check at work 30.

### Read (≈ 40 min)

1. [Ch 4 · Loading Related Data: Eager, Lazy, Explicit](chapters/04-data.md#loading-related-data-eager-lazy-explicit), [The N+1 Problem — Seeing It and Killing It](chapters/04-data.md#the-n1-problem--seeing-it-and-killing-it) and [Projections](chapters/04-data.md#projections-select-only-what-you-need).
2. [Ch 4 · Indexes: Clustered, Non-Clustered, Covering](chapters/04-data.md#indexes-clustered-non-clustered-covering) and [Execution Plans](chapters/04-data.md#execution-plans).
3. [Ch 4 · Choosing an Index Type](chapters/04-data.md#choosing-an-index-type) (the leftmost-prefix rule), [Reading EXPLAIN](chapters/04-data.md#reading-explain), [When the Plan Is Wrong](chapters/04-data.md#when-the-plan-is-wrong), [Finding the Queries Worth Looking At](chapters/04-data.md#finding-the-queries-worth-looking-at) and [The .NET Side](chapters/04-data.md#the-net-side). These use PostgreSQL. The B-tree rules transfer to SQL Server unchanged; the storage model (no clustered index, row versions) does not.
4. [Ch 37 · Goal and the senior signal it trains](chapters/37-lab-execution-plans.md#goal-and-the-senior-signal-it-trains), [Setup](chapters/37-lab-execution-plans.md#setup) and [Level 1](chapters/37-lab-execution-plans.md#level-1--the-code-decides-the-sql-rungs-14). Then, only after your own attempt, its *Hints and answers* for rungs 1, 3, 4 and 6, *Level 3 — the server's view* (N+1 in `pg_stat_statements`) and *Level 3 — the SQL Server track*.

**Do:**
- [Ch 4 · Exercises](chapters/04-data.md#exercises): the nested N+1.
- From [Lab 37](https://github.com/malyna2/dotnet-handbook/tree/main/labs/37-execution-plans) (≈ 3 h 45 min):
  - setup;
  - Level 1 (rungs 1–4: N+1, a cartesian explosion from two `Include`s, a function on a column, an implicit conversion);
  - rung 6 from Level 2 (column order);
  - SQL Server script 1 (`nvarchar` against `varchar`).

  Everything else in the lab is under [Later](#later). Your results belong in your own portfolio repo, not in this one (Chapter 36's portfolio rule).

### Prove it

**3. Seek or scan.** Needs `docker compose up -d`. Before you run it, predict SEEK or SCAN for each of the five queries.

```csharp
using System.Data;
using Microsoft.Data.SqlClient;

// Prove it: which predicates can SEEK an index and which must SCAN it. Needs a SQL Server:
// `docker compose up -d` in this folder. Prints each query's index operator and logical reads.
using var db = new SqlConnection("Server=localhost,14330;Database=tempdb;User Id=sa;Password=Emulator-Only-Passw0rd!;TrustServerCertificate=true");
db.Open();
new SqlCommand("""
    DROP TABLE IF EXISTS dbo.Customers;
    CREATE TABLE dbo.Customers (Id int IDENTITY PRIMARY KEY, Email varchar(100) NOT NULL, City varchar(50) NOT NULL, CreatedAt datetime2 NOT NULL);
    INSERT dbo.Customers (Email, City, CreatedAt) SELECT CONCAT('user', value, '@example.com'), CONCAT('City', value % 200), DATEADD(minute, -value, '2026-01-01') FROM GENERATE_SERIES(1, 200000);
    CREATE INDEX IX_Email ON dbo.Customers (Email); CREATE INDEX IX_City_CreatedAt ON dbo.Customers (City, CreatedAt);
    SET STATISTICS PROFILE ON; SET STATISTICS IO ON;
    """, db).ExecuteNonQuery();
string reads = "?";
db.InfoMessage += (_, e) => { if (e.Message.Contains("logical reads")) reads = e.Message.Split("logical reads ")[1].Split(',')[0]; };
Plan("Email = @p", SqlDbType.NVarChar, "user42@example.com");     // a C# string is sent as nvarchar
Plan("Email = @p", SqlDbType.VarChar, "user42@example.com");
Plan("LOWER(Email) = @p", SqlDbType.VarChar, "user42@example.com");
Plan("City = @p AND CreatedAt >= '2025-12-31'", SqlDbType.VarChar, "City7");
Plan("CreatedAt >= @p", SqlDbType.DateTime2, new DateTime(2025, 12, 31));
void Plan(string where, SqlDbType type, object value)
{
    using var command = new SqlCommand($"SELECT Id FROM dbo.Customers WHERE {where}", db);
    command.Parameters.Add(new SqlParameter("@p", type) { Value = value });
    var ops = new List<string>();
    using (var reader = command.ExecuteReader())
        do while (reader.Read()) if (reader.FieldCount > 2 && reader.GetString(2).Contains("Index")) ops.Add(reader.GetString(2).Trim()); while (reader.NextResult());
    Console.WriteLine($"WHERE {where} (@p {type}): {reads} logical reads\n    {string.Join(" ", ops).Replace("[tempdb].[dbo].[Customers].", "")}");
}
```

```text
WHERE Email = @p (@p NVarChar): 888 logical reads
    |--Index Scan(OBJECT:([IX_Email]),  WHERE:(CONVERT_IMPLICIT(nvarchar(100),[Email],0)=[@p]))
WHERE Email = @p (@p VarChar): 3 logical reads
    |--Index Seek(OBJECT:([IX_Email]), SEEK:([Email]=[@p]) ORDERED FORWARD)
WHERE LOWER(Email) = @p (@p VarChar): 888 logical reads
    |--Index Scan(OBJECT:([IX_Email]),  WHERE:(lower([Email])=[@p]))
WHERE City = @p AND CreatedAt >= '2025-12-31' (@p VarChar): 3 logical reads
    |--Index Seek(OBJECT:([IX_City_CreatedAt]), SEEK:([City]=[@p] AND [CreatedAt] >= '2025-12-31 00:00:00.0000000') ORDERED FORWARD)
WHERE CreatedAt >= @p (@p DateTime2): 712 logical reads
    |--Index Scan(OBJECT:([IX_City_CreatedAt]),  WHERE:([CreatedAt]>=[@p]))
```

What to notice — three scans, each for a different reason:

1. **The conversion lands on the column.** `nvarchar` outranks `varchar` in data-type precedence, so SQL Server converts the *column*: `CONVERT_IMPLICIT(…,[Email],0)`. Under a SQL collation, `varchar` and `nvarchar` sort differently, so the `varchar` order of the index can't answer the question: 888 reads instead of 3.
   - SQL collations include this container's default and, per Microsoft's `CREATE DATABASE` documentation, the default for a new Azure SQL database.
   - The companion run [`collation.txt`](verify/study-track/reference-runs/collation.txt) shows the same parameter still seeking (3 reads) under a Windows collation, through a computed range (`GetRangeThroughConvert`). That is why this bug shows up on some databases and not on others.
   - The actual plan flags it: `<PlanAffectingConvert ConvertIssue="Seek Plan" …>`.
2. **A function on the column.** `lower([Email])` is a value the index doesn't store, so: 888 reads.
3. **The index is used but *scanned*.** `CreatedAt` alone isn't a leftmost prefix of `(City, CreatedAt)`, so SQL Server reads the whole index: 712 reads. "The query uses the index" is not "the query seeks the index". With `City` pinned first, both columns appear in `SEEK:`: 3 reads.

**The fix in .NET:** send the parameter as `varchar`.

- EF Core: `.IsUnicode(false).HasMaxLength(100)`. EF Core 10's `ToQueryString()` then shows `DECLARE @email varchar(100)`, where the default mapping shows `nvarchar(4000)`.
- Dapper: `new DbString { Value = email, IsAnsi = true, Length = 100 }`.
- Raw ADO.NET: `SqlDbType.VarChar`.

### Three questions

**1.** An index on `(City, CreatedAt)`. Why does `WHERE CreatedAt >= @d` scan, while `WHERE City = @c AND CreatedAt >= @d` seeks? And why would `(CreatedAt, City)` be worse for the second query?

<details>
<summary>Answer</summary>

**Why the first scans and the second seeks.** The index is sorted by `City`, then by `CreatedAt` within each city. One city is a contiguous block with its dates in order, so the engine seeks to `(City, start date)` and reads forward. A date range on its own is spread across every city's block: there is no single range to seek, so it scans (712 reads against 3 in the experiment).

**Why `(CreatedAt, City)` is worse.** The date range on the leading column *is* contiguous, but it holds every city's rows for that period, and `City` can only be checked row by row as a residual predicate. Chapter 37's rung 6 measured it: 6,386 buffers against 6.

**The rule:** equality columns first, then the range or sort column. PostgreSQL 18's skip scan softens the leftmost-prefix rule only when the leading column has few distinct values.
</details>

**2.** The same query seeks when you run it in SSMS with a literal, and scans when the application sends it. The column is `varchar`. Why, and what are the fixes in Dapper and EF Core?

<details>
<summary>Answer</summary>

**Why.**
- SqlClient and Dapper send a C# `string` as `nvarchar` unless told otherwise. So does EF Core for a property not mapped as non-Unicode.
- `nvarchar` has higher data-type precedence, so SQL Server converts the *column*, and `CONVERT_IMPLICIT` lands on the column side.
- Under a SQL collation — the default for new Azure SQL databases and for en-US installations — `varchar` and `nvarchar` sort differently. The index's order can't answer the converted comparison, so it scans: 888 reads against 3.
- Under a Windows collation the optimizer can still compute a seek range, which is why it "works on the other database".

**Fixes.** Send `varchar`:
- Dapper: `DbString { IsAnsi = true, Length = 100 }`;
- ADO.NET: `SqlDbType.VarChar`;
- EF Core: map the property `.IsUnicode(false).HasMaxLength(100)`;
- or make the column `nvarchar`.

**Finding it in production:** plans in Query Store with `CONVERT_IMPLICIT` on a column, or the `PlanAffectingConvert` warning.
</details>

**3.** An endpoint makes 1 + N queries, and every individual plan is an index seek under a millisecond. Why is it slow, why can't any single plan show the problem, and how do you find it?

<details>
<summary>Answer</summary>

**Why it is slow.** The cost is in the round trips: N network round trips, N commands, N rounds of materialisation, N borrowings of a pooled connection. That is latency × N.

**Why no plan shows it.** Each statement really is cheap, so each plan looks perfect. The problem is the *number* of statements per request, which no plan contains. Lazy loading hides it: a proxy's navigation getter issues a query on first access, so a loop over parents issues one query per parent.

**Finding it — count statements:**
- EF Core command logging in development;
- spans per request in APM or OpenTelemetry;
- call counts in `pg_stat_statements` (Chapter 37: 250 calls against 5) or in Query Store.

**Fix:** `Include`, a projection, or a split query, and lazy loading turned off.
</details>

### Check at work

**Inspect.** Take your service's most expensive query from Query Store (or your APM's slowest dependency) and open its *actual* plan. Name every index operator a seek or a scan. For each scan, decide which of the experiment's three causes it is — or whether the scan is simply right, because the query needs most of the table.

---

## 4. Code review

Covers: leaving substantive review comments, and the habits that make you less dependent on others — vague tasks, estimates, digging on your own, asking questions.

### The mechanism to explain without notes

**A substantive review comment is a prediction: under condition X, this line does Y, which costs Z. It comes with a fix and a severity label.**

To find those predictions, ask four questions of every changed line:

- What if it runs **twice**?
- What if it runs **concurrently**?
- What if it runs **slowly, or under load**?
- What if it **fails halfway**?

Every trap in this track answers one of them:

| Trap | Question it answers |
|---|---|
| `async void` | fails halfway, and nobody hears |
| `.Result` and `new HttpClient()` | under load |
| Lock expiry and check-then-act dedup | twice, concurrently |
| Dual write | fails halfway |
| N+1 | at scale |

The same idea applies to your own work: remove uncertainty while it is cheap.
- A vague task becomes a written problem statement with your questions attached.
- An estimate becomes a range with the assumption that would change it.
- Being stuck becomes a 30-minute timebox, then a question that shows what you tried.

**Time:** reading ≈ 30 min; hands-on ≈ 2 h 25 min — the experiment 10 min, the review practice 1 h, the questions 15, the check at work (two real reviews) 1 h.

### Read (≈ 30 min)

1. [Ch 17 · 17.3 Code Review Mastery](chapters/17-softskills.md#173-code-review-mastery): how to write the comment, Conventional Comments labels.
2. [Ch 18 · Judging AI-generated code: a reviewer's rubric](chapters/18-ai-native.md#judging-ai-generated-code-a-reviewers-rubric). Read it as the checklist of *what to look for*; the defects are the same in human-written code.
3. [Ch 17 · 17.1 From Solving Tickets to Creating Leverage](chapters/17-softskills.md#171-from-solving-tickets-to-creating-leverage).
4. [Ch 17 · 17.4 Estimation & Planning](chapters/17-softskills.md#174-estimation--planning) and [17.6 Methodical Debugging & Problem Solving](chapters/17-softskills.md#176-methodical-debugging--problem-solving), including the 30-minute rule.
5. [Ch 17 · Disagreeing productively and managing up](chapters/17-softskills.md#disagreeing-productively-and-managing-up) and [Running meetings that don't waste an hour × N people](chapters/17-softskills.md#running-meetings-that-dont-waste-an-hour--n-people).
6. [Ch 17 · What would you do — the estimate](chapters/17-softskills.md#what-would-you-do--the-estimate) and [What would you do — the review](chapters/17-softskills.md#what-would-you-do--the-review).
7. Optional: [Ch 61 · The One-Page Problem Statement](chapters/61-discovery-and-diagnosis.md#the-one-page-problem-statement). It is written for consultants, but it is the best tool in the book for a vague ticket.

> **Erratum (to be corrected in Chapter 17):** the chapter's model comment says an `async void` method "will swallow exceptions". It won't: experiment 1a shows it ends the process.

**Do — review before you read the answers.** Treat each sample as a pull request. Write your comments with labels, using the four questions, then compare with the answer:

- Chapter 4's *Find the bug*;
- Chapter 8's *Find the bug* and *What would you do*;
- Chapter 51's two *Find the bug* samples;
- the `CheckThenAct` handler below.

Keep score: the defects you found, the ones you missed, and any `blocking:` you gave to something that wasn't. The Code Review Gym lab (M5 in [`PRACTICE_ROADMAP.md`](PRACTICE_ROADMAP.md)) will turn this into a scored exercise.

### Prove it

**4. Review first, then run.** Write your review comment on `CheckThenAct` before running it.

```csharp
using System.Collections.Concurrent;

// Prove it: review the two handlers below BEFORE running this. Both "deduplicate" a message
// that the broker delivered twice; the two copies are being processed at the same time.
int charges = 0;

var seen = new ConcurrentDictionary<string, bool>();
async Task CheckThenAct(string messageId)
{
    if (seen.ContainsKey(messageId)) return;        // check …
    await ChargeCardAsync();                        // … the effect …
    seen[messageId] = true;                         // … then remember the id
}

var claimed = new ConcurrentDictionary<string, bool>();
async Task ClaimFirst(string messageId)
{
    if (!claimed.TryAdd(messageId, true)) return;  // one atomic step: only one copy can win
    await ChargeCardAsync();
}

await Task.WhenAll(CheckThenAct("order-42"), CheckThenAct("order-42"));
Console.WriteLine($"check, then act: the card was charged {charges} time(s)");

charges = 0;
await Task.WhenAll(ClaimFirst("order-42"), ClaimFirst("order-42"));
Console.WriteLine($"claim first:     the card was charged {charges} time(s)");

async Task ChargeCardAsync() { await Task.Delay(100); Interlocked.Increment(ref charges); }
```

```text
check, then act: the card was charged 2 time(s)
claim first:     the card was charged 1 time(s)
```

What to notice:

- **A thread-safe dictionary doesn't make the handler safe.** Both copies passed `ContainsKey` before either recorded the ID. Every *call* is thread-safe; the *check-then-act sequence* is not atomic.
- **`TryAdd` makes check and claim one atomic step.**
- **In a database, the claim is an insert under a unique key.** It goes in the same transaction as the effect: the second copy hits the unique violation and never reaches the effect, and if the effect fails, the rollback releases the claim ([Ch 3](chapters/03-aspnetcore.md#idempotency-keys-making-post-retry-safe) shows the full pattern).
- **A comment that lands** reads: "blocking: two concurrent deliveries of the same message both pass `ContainsKey` and both charge. Claim the ID atomically — a unique-key insert in the same transaction as the charge — before doing the work."

### Three questions

**1.** Why is `if (await AlreadyProcessed(id)) return; await Process(); await MarkProcessed(id);` wrong even in a single-instance consumer, and what makes the fixed version correct?

<details>
<summary>Answer</summary>

**It is check-then-act.** Two copies can run concurrently even with one instance:
- a processor with `MaxConcurrentCalls > 1`;
- a redelivery after lock expiry, overlapping a slow first attempt.

Both run the check before either marks, so both process.

**It also loses the mark.** A crash between `Process` and `MarkProcessed` leaves no mark, so the redelivery processes the message again.

**Fix: make the claim atomic and part of the effect's transaction.** Insert the message ID into a table with a unique key, in the same database transaction as the business change. The second copy hits the unique violation and stops; if the effect fails, the rollback releases the claim. For an external effect, such as a payment API, also pass an idempotency key downstream.
</details>

**2.** A PR wraps a Service Bus handler's body in `catch (Exception) { }` "so poison messages stop retrying". What do you comment, at what severity, and why?

<details>
<summary>Answer</summary>

**Severity: `blocking:`.**

**Why.** With `AutoCompleteMessages` (the processor's default), a handler that returns normally completes the message. Swallowing every exception turns every failure into a success: a transient timeout becomes a lost payment, with no retry, no dead-letter entry and no alert. It "fixes" poison messages by deleting good ones too.

**What to ask for instead:**
- catch the permanent failures — deserialisation, validation, "the order no longer exists" — and call `DeadLetterMessageAsync` with a reason a human can act on;
- let transient exceptions propagate, so the message is retried up to `MaxDeliveryCount`;
- alert on the dead-letter count.

**References:** Chapter 50 (*Service Bus*), Chapter 51 (Case 6).
</details>

**3.** A ticket says "Add caching to the product page". Why is opening the Redis docs the wrong first move, and what do you send before writing any code?

<details>
<summary>Answer</summary>

**Why not Redis yet.** The ticket is solution-shaped: the problem behind it is unknown. Is the page slow? Measured how, and against what target? Or is the database too expensive, or overloaded? A cache can hide a missing index (Chapter 4's *What would you do*) and adds a staleness rule nobody has agreed to.

**What to send** before any code: a five-line problem statement — what is slow, how it was measured, the target, and the constraints (how stale may prices be?) — plus your two or three questions. Add an estimate as a range, with the assumption that would change it, or offer a timeboxed spike to find out.

**Why it pays.** The cheapest moment to discover you understood the task differently is before the code exists, and a written assumption protects your estimate later. Doing this in the open is also how you ask good questions in meetings: bring the question you prepared from the agenda, not the one you thought of afterwards.
</details>

### Check at work

**Do.** In the next two pull requests you review, run the four questions over every changed line. Leave at least one comment of the form *condition → mechanism → cost → fix*, with a Conventional Comments label. Afterwards, ask each author one question: "Was that comment clear enough to act on without asking me anything?"

---

## Pay attention to

The traps behind the most common wrong answers, for rereading before an interview.

| Trap | Why it bites | The fix |
|---|---|---|
| `async void` for fire-and-forget | No `Task`, so the builder rethrows on the `SynchronizationContext` or a pool thread: unhandled, the process exits. The caller's `try/catch` never sees it, even for a throw before the first `await`. | Return `Task`. Real fire-and-forget goes through a queue or `BackgroundService` that observes failures. `async void` only for event handlers, with a `try/catch` around the whole body. |
| `await Task.WhenAll(...)` shows one failure | `await` rethrows only the first stored exception: for plain tasks since .NET 8 the first to fail, for `Task<T>` the first in argument order. The others never reach the log. | Keep the task in a variable; log `task.Exception?.InnerExceptions`. |
| `.Result`, `.Wait()`, `.GetAwaiter().GetResult()` on a request path | Each call pins a pool thread for the whole wait. After the first burst the pool adds threads with pauses of up to 250 ms each, so every request queues — unrelated ones too — while the CPU stays low. In the experiment: 11.6 s instead of 1.1 s, at 0.46 s of CPU. | Async all the way down. Watch the thread count and the queue length in `dotnet-counters`. |
| `new HttpClient()` per call | Every new handler is a new connection pool, so every call opens a TCP connection. Each closed socket holds a port in `TIME_WAIT` (60 s on Linux); App Service gives an instance 128 SNAT ports. | `IHttpClientFactory` or a typed client, or one long-lived client with `PooledConnectionLifetime`. |
| Lazy loading in a loop | Every navigation access is a query. Each plan is cheap; the round trips aren't, and no single plan shows them. | `Include` or a projection; count statements per request; turn lazy loading off. |
| "The queue is FIFO, so processing is ordered" | Competing consumers, concurrency above 1, redelivery after an abandon or an expired lock, and prefetch all reorder. | Sessions (`SessionId` = the entity's ID) where order matters, or version checks that drop stale updates. |
| A handler that runs longer than the lock | The lock is a lease from receive time (1 minute by default, 5 at most). When it expires the message is redelivered while you're still working, and `Complete` throws `MessageLockLost`. | `ServiceBusProcessor` with lock auto-renewal, no oversized batches or prefetch, and an idempotent handler. |
| Long work inside an HTTP request | Front ends cut it (App Service at 230 s); clients retry and duplicate the work. | `202 Accepted` with a `Location` status URL and `Retry-After`; a durable queue and a worker; an idempotency key on the `POST`. |
| Save to the database, then publish | Two systems, no shared transaction: a crash in between loses or orphans the event. | An outbox row in the same transaction; a relay or Debezium's outbox event router publishes it at-least-once; consumers deduplicate. |
| Check-then-act deduplication | Two concurrent copies both pass the check, so the effect runs twice. | Claim first: insert the ID under a unique key, in the same transaction as the effect. |
| A composite index in the wrong order | The index is sorted left to right. Without the leading column there is no contiguous range, so it scans; with a range first, the equality column becomes a row-by-row filter. | Equality columns first, then the range or sort column; check that both appear in the seek predicate. |
| A function or a conversion on the column | `LOWER(Email)`, or a `varchar` column compared with an `nvarchar` parameter, asks about values the index doesn't store, so it scans (888 reads against 3). A Windows collation can still range-seek the conversion. | Compare the raw column with a parameter of the right type (`IsUnicode(false)`, `DbString { IsAnsi = true }`); a case-insensitive collation instead of `LOWER`. |
| A review comment without a condition | "This could be a problem" gets ignored. | Condition → mechanism → cost → fix, labelled `blocking:`, `suggestion:` or `nit:`. |
| Starting a vague ticket as written | The wrong interpretation surfaces after the code exists, when it costs the most. | A five-line problem statement with your questions; an estimate as a range with its assumption; a timeboxed spike. |

---

## Later

Everything else, in the order worth reading it. Reading times use the build's formula; the lab's time comes from its own time budget.

**1. The rest of the chapters this track already uses.**
- **Chapter 8.** `ValueTask`, `CancellationToken`, async streams, the TPL, `Channels`, thread safety: ≈ 15 min.
- **Chapter 9.** The brokers compared, MassTransit, sagas, resilience, distributed caching: ≈ 30 min.
- **Chapter 4.** Change tracking, split and compiled queries, bulk writes, cascades, transactions and isolation, deadlocks, Dapper, caching, concurrency: ≈ 50 min.
- **Lab 37.** Rungs 5 and 7–9, the rest of Level 3, *Break it* and the write-up: ≈ 6 h of hands-on work.
- **Chapter 50.** *App Service* and *Azure Functions*.
- **Chapter 51.** Cases 4, 7 and 14, then the rest.
- **Chapter 17.** The rest.

**2. Middle-level chapters not on this track.**
- Chapter 1 (C#), Chapter 2 (runtime, DI lifetimes, the Generic Host), Chapter 3 (the rest), Chapter 7 (testing).
- Chapter 13 (observability), Chapter 14 (security), Chapter 15 (profiling with `dotnet-counters`).
- Chapter 16, Chapter 20 (the rest), Chapter 22 (the rest of Part A), Chapter 26 (dates, money, strings), Chapter 5 (in parts).
- Chapter 33 (Scenarios 1, 3, 4 and 7), then Chapter 34 as a self-test, then Appendices A and B.

**3. Toward senior, later.**
- Chapter 6 (architecture: CQRS first), Chapter 21 (distributed theory), Chapter 23 (data at scale), Chapter 24 (schema evolution), Chapter 25 (advanced testing).
- Chapter 27 (system design), Chapter 30 (legacy), Chapters 10–12 (cloud, containers, CI/CD), Chapter 31 (Linux).
- Chapter 35 (supply chain), Chapter 18 (the rest), Chapter 32 (capstone), Chapter 36 (story bank), Chapter 63 (estimates for clients).

**4. Not needed now.**
- Chapter 19 (building AI systems), Chapter 28 (compliance and FinOps), Chapter 29 (frontend).
- Chapters 60–62 and 64–65 (the consulting part).

**Coming labs.** When the Practice Gym adds them (see [`PRACTICE_ROADMAP.md`](PRACTICE_ROADMAP.md)), two of them belong on this track:
- **M3, Idempotent Messaging End to End**, replaces the hands-on part of topic 2.
- **M5, The Code Review Gym**, replaces the review practice in topic 4.
