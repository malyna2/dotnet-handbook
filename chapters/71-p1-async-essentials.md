# Part 1 · Module 1: Async Essentials

> **What this module makes you able to do.** Write and review async request code that loses no exceptions, threads or sockets: say where an exception from `async void` or `Task.WhenAll` goes, why `.Result` slows every endpoint, what a `CancellationToken` actually stops, and why an `HttpClient` must be reused.

**Time:** reading ≈ 35 min; hands-on ≈ 1 h 35 min — the four experiments 40 min, the questions 15, the check at work 30, the chapter exercise 10.

## Covers

- what `await` does with the thread, and why "async runs on another thread" is the wrong model;
- why a `try/catch` around an `async void` call never runs, and why the exception ends the process;
- what `await Task.WhenAll` throws when several tasks fail, and how to log all of them;
- why `.Result` and `.Wait()` slow down every endpoint even when nothing deadlocks;
- what a `CancellationToken` stops, and why it has to be passed all the way down;
- why `new HttpClient()` per request exhausts sockets, and what to use instead.

## The mechanism to explain without notes

**`await` hands the thread back and parks the rest of the method on the task; the task is the only thing that carries the result or the exception back.**

At an `await` on an unfinished task, the compiler-generated state machine registers "run the rest of me" as a continuation and returns to its caller, so the thread goes back to the pool. No thread waits for the I/O. When the task completes, the continuation is queued to a pool thread (or posted to a captured `SynchronizationContext`, which ASP.NET Core doesn't have), and there `await` returns the value or rethrows the exception. Every trap in *Covers* follows from that:

- **The returned `Task` is the only channel for the exception.** `async void` returns none, so the exception is rethrown on the `SynchronizationContext`, or, in ASP.NET Core, workers and console apps, on a thread-pool thread where nothing catches it and the process dies. `Task.WhenAll` stores every failure in its task, and `await` rethrows only one of them.
- **A continuation needs a free pool thread.** `.Result` holds a pool thread for the whole wait. Under load every thread is held, the continuations that would release them wait in the pool's queue, and the pool adds threads only gradually: every endpoint slows down while the CPU idles.
- **Stopping is cooperative.** A `CancellationToken` is a flag with a list of callbacks. It stops only the calls it reaches: each API you pass it to checks it, or registers a callback that cancels its I/O.
- **Sockets are pooled like threads.** The connection pool lives in the handler under `HttpClient`. A new client per request brings a new pool, so every request opens a TCP connection, and every closed one holds its local port for a while.

## Read (≈ 35 min)

1. [Chapter 8: Why Async Exists: I/O-Bound vs CPU-Bound Work](#why-async-exists-io-bound-vs-cpu-bound-work) and [Tasks: The Promise of a Future Result](#tasks-the-promise-of-a-future-result): the thread pool, and why async is about not holding threads.
2. [Chapter 8: async/await, Deeply](#asyncawait-deeply): what `await` does step by step, the state machine, and the *Pay attention* callout on why an `async void` exception kills the process.
3. [Chapter 8: SynchronizationContext and ConfigureAwait](#synchronizationcontext-and-configureawait): where a continuation runs, and why ASP.NET Core has no context.
4. [Chapter 8: The Sync-Over-Async Deadlock](#the-sync-over-async-deadlock): the classic deadlock, then the *Pay attention* callout on starvation, which hurts ASP.NET Core too.
5. [Chapter 8: CancellationToken: Cooperative Cancellation](#cancellationtoken-cooperative-cancellation) and [Chapter 3: CancellationToken Propagation](#cancellationtoken-propagation): what a token is, and what passing it down buys a web API.
6. [Chapter 8: Composing Concurrent Work: WhenAll and WhenAny](#composing-concurrent-work-whenall-and-whenany): start first, await together; the *Pay attention* callout says which exception `await` throws.
7. [Chapter 3: IHttpClientFactory & Resilience with Polly](#ihttpclientfactory-resilience-with-polly): the handler that owns the connections, and typed clients.
8. [Chapter 20: Keep-Alive, Connection Pooling, and Socket Exhaustion](#keep-alive-connection-pooling-and-socket-exhaustion): `TIME_WAIT`, and the ceiling it puts on new connections per second.
9. [Chapter 51: Case 3 — Intermittent timeouts under load, with every dashboard green](#case-3-intermittent-timeouts-under-load-with-every-dashboard-green): the same bug on App Service, as SNAT port exhaustion.
10. [Chapter 34: Diagnosing a Performance Problem (a worked methodology)](#diagnosing-a-performance-problem-a-worked-methodology): how starvation looks from the outside, and how to tell it from a slow dependency.

## Prove it

Four programs, one per trap. Predict each output before you run it: the gap between the prediction and the output is what this module is for.

**1. An `async void` exception kills the process.**

`verify/path/AsyncVoid/Program.cs` · run it from `verify/path` with `dotnet run --project AsyncVoid`:

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

- **The exception is thrown before the first `await`, and the caller still can't catch it.** The compiler moves the whole method body into the state machine, inside its own `try/catch`, which hands the exception to the method builder. The call returns normally.
- **The bottom frames are the rethrow on the pool.** `ThreadPoolWorkQueue.Dispatch` is a thread-pool thread with no caller above it, so the exception is unhandled and the runtime ends the process. In a web app, every in-flight request dies with it.

**2. `await Task.WhenAll` throws one exception.**

`verify/path/WhenAll/Program.cs` · run it from `verify/path` with `dotnet run --project WhenAll`:

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

- **`await` rethrows one exception; the task holds both.** `await` unwraps on purpose, so async code reads like synchronous code, where a call throws one exception. `.Wait()` and `.Result` throw the `AggregateException` wrapper instead.
- **Which one you get is not stable.** Over plain `Task`s it was the first to fail; over `Task<int>`, the first in argument order. Don't depend on either: keep the `WhenAll` task in a variable and log `all.Exception?.InnerExceptions`. `Exception` is `null` when the task was cancelled rather than faulted, so the `!` above is safe only for a fault.

**3. Sync-over-async starves the pool.** Run it twice: with `-- await`, then with `-- block`.

`verify/path/Starvation/Program.cs` · run it from `verify/path` with `dotnet run --project Starvation -- await`, then `-- block`:

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

The run was on 4 vCPUs, so 200 requests; your machine's core count sets the number, and your seconds will differ. What to notice:

- **Same work, ten times slower.** Awaiting, 200 one-second requests finish in 1.1 s on 3 pool threads: nobody holds a thread while waiting. Blocking, the same work takes 11.6 s, and the pool has to grow to 68 threads.
- **The unrelated request is the outage.** It waited 10.5 s for a thread, queued behind the blocked work. In production every endpoint slows down, including the ones that never block.
- **The CPU had nothing to do.** The blocking run used 0.46 s of CPU in 11.7 s of wall time on 4 cores: the threads were waiting, not working. Low CPU, high latency everywhere and a climbing thread count is how starvation looks from the outside, and why it is so often blamed on the database.
- **It ends only because the pool keeps adding threads, slowly.** How fast it adds them, and why raising the minimum only moves the cliff, is Part 2 material.

**4. A new `HttpClient` per request leaves a socket behind every time.**

`verify/path/HttpClientPerRequest/Program.cs` · run it from `verify/path` with `dotnet run --project HttpClientPerRequest`:

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
- **A client per request: 500 sockets in `TIME_WAIT`.** Each client opened a connection and closed it on `Dispose`. The side that closes keeps the socket, and its local port, in `TIME_WAIT`: 60 s on Linux.
- **At production rates the ports run out.** Ports come back only as fast as `TIME_WAIT` expires, which caps new connections per second to one destination; on App Service the cap is far lower, 128 SNAT ports per instance and destination, each reclaimed four minutes after its connection closes. A test suite never reaches either rate; a traffic peak does.

Then do the chapter exercise: [Chapter 8](#chapter-8-asynchronous-concurrent-programming), *Exercises*, *Find the bug*, a report endpoint with `.Result`, `.Wait()` and `Parallel.ForEach`. Name every defect, and the one that causes the outage, before you open the answer.

## Three questions

**1.** A `try/catch` wraps a call to an `async void` method that throws on its first line, before any `await`. Why doesn't the catch run, and why does the whole process die instead of one request failing?

<details>
<summary>Answer</summary>

- **The method body never runs outside the state machine.** The compiler moves it into `MoveNext`, wrapped in its own `try/catch`, so every exception is caught there and handed to the method builder, even one thrown before the first `await`. The call returns normally.
- **`async Task` has somewhere to put it.** Its builder stores the exception in the returned `Task`, and the caller sees it when it awaits.
- **`async void` doesn't.** Its builder rethrows the exception on the captured `SynchronizationContext`. When there is none (ASP.NET Core, workers, console apps), it rethrows it on a thread-pool thread. Nothing above a pool work item catches it: the exception is unhandled, and the runtime ends the process, with every request in it.

Fix: return `Task`. Hand real fire-and-forget work to a queue or a `BackgroundService` that logs failures. Keep `async void` for event handlers, with a `try/catch` around their whole body.
</details>

**2.** `await Task.WhenAll(a, b)`, and both fail. Why does the catch see one exception, why does `.Wait()` on the same task throw something else, and how do you log both?

<details>
<summary>Answer</summary>

- **The `WhenAll` task stores both exceptions**, in an `AggregateException`.
- **`await` throws one on purpose.** It rethrows the first stored exception, with its original stack trace, so async code reads like synchronous code: one call, one exception. Which one is "first" depends on the overload and on timing, so never depend on it.
- **`.Wait()` and `.Result` throw the wrapper**: the `AggregateException` itself.

To log both, keep the `WhenAll` task in a variable and, in the `catch`, log `task.Exception?.InnerExceptions`. `Exception` is `null` when the task was cancelled rather than faulted.
</details>

**3.** `.Result` in a request handler and `new HttpClient()` per request both pass every test and fail only under load. Which finite resource does each exhaust, and why only at high concurrency?

<details>
<summary>Answer</summary>

- **`.Result` exhausts pool threads.** It holds a pool thread for the whole I/O wait. At low concurrency there are spare threads. At high concurrency every thread is held, and the continuations and timer callbacks that would release them wait in the queue behind new requests. The pool adds threads only gradually, so latency spreads to every endpoint while the CPU stays low.
- **`new HttpClient()` per request exhausts local ports.** Each client brings its own handler and connection pool, so each request opens a TCP connection. Disposing the client closes it, and the socket then holds a local port in `TIME_WAIT` (60 s on Linux). Once connections open faster than ports come back (on App Service: 128 SNAT ports per instance and destination, each reclaimed four minutes after close), new connections wait and time out.

A test makes a handful of requests, one after another, so neither resource runs out.
</details>

## Check at work

**Inspect.** Search your service for `async void`, `.Result`, `.Wait()`, `.GetAwaiter().GetResult()` and `new HttpClient(`. Sort every hit into one of three bins: an event handler, start-up code, or a request or message path, which is a latent outage. Then follow one endpoint's `await` chain from the action down to its database or HTTP call: a `CancellationToken` that stops halfway leaves everything below it uncancellable.

**Measure.** Check that something charts the service's thread-pool queue length and thread count: `dotnet.thread_pool.queue.length` and `dotnet.thread_pool.thread.count` in an APM on .NET 9+ (check it plots the value, not a rate), `ThreadPool Queue Length` and `ThreadPool Thread Count` in `dotnet-counters` (on .NET 9 and 10 with `--counters 'EventCounters\System.Runtime'`; [Chapter 34](#diagnosing-a-performance-problem-a-worked-methodology) explains why). Without them, starvation looks exactly like a slow database. Read both at your traffic peak: a queue that grows while the thread count climbs is the starvation fingerprint.
