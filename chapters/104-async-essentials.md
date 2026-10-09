# Chapter 4: Async Essentials

This chapter is about what `async` and `await` do to three things: the thread, the exception and the work that is waiting. Every async outage in production is one of those three going wrong. By the end you should be able to write and review async request code that loses no exceptions and holds no thread it doesn't need: say where an exception from `async void` or `Task.WhenAll` goes, why `.Result` slows down every endpoint even when nothing deadlocks, what a `CancellationToken` actually stops, and how to share state between threads without a race.

One mechanism ties it together: **`await` hands the thread back and parks the rest of the method on the task, and the task is the only thing that carries the result or the exception back.** The sections build that model in order. First why async exists and the thread pool it runs on, then what a `Task` is and what `await` does with it, down to the state machine the compiler generates. Then where the rest of the method runs when the task completes, and what happens when you block a thread to wait for it: the classic deadlock and its quieter, more common cousin, thread-pool starvation. Then how to stop work you no longer need (cancellation) and how to run several operations at once (`WhenAll`, `WhenAny`, throttling, async streams). Concurrent work means shared data, so the chapter ends with thread safety.

`ValueTask`, the TPL's parallel loops, Channels and the thread pool's injection rules build on this model and come in [Chapter 17: Runtime Internals and Performance](#chapter-17-runtime-internals-and-performance). [Chapter 5: HTTP and Web APIs](#chapter-5-http-and-web-apis) applies it to a web request.

## Why Async Exists: I/O-Bound vs CPU-Bound Work

**CPU-bound work** keeps a core busy: hashing a password, resizing an image, summing a billion numbers. Doing more of it at once needs more cores.

**I/O-bound work** spends almost all its time *waiting*: on a database query, an HTTP call, a file read. During the wait no core works on your behalf.

**Async is about not wasting threads while waiting for I/O**, not about doing several things at once. A well-written async I/O call uses *no* thread while it waits: the operating system completes the I/O and notifies the runtime. "Async means it runs on another thread" is the model to unlearn first.

### Threads Are Expensive

Each thread reserves address space for its own stack up front (megabytes; the default depends on the OS) and costs a kernel object, and every switch between threads saves registers and cools the CPU caches. One thread per request collapses at a few thousand concurrent connections. Async lets 10,000 requests waiting on a slow database share a handful of threads, because a thread that would have waited goes back to serve other work.

### The Thread Pool

`Task.Run`, timer callbacks and every `await` continuation run on the **thread pool**, a set of reusable worker threads whose size is managed, not fixed:

- **Up to its minimum** — one thread per core by default — it creates threads on demand.
- **Above the minimum it grows deliberately.** A *hill-climbing* algorithm adds or removes threads by watching throughput, and a *starvation detector* adds a thread when queued work hasn't moved for about half a second.

Deliberate growth is right for CPU work, where more threads than cores only add switching, and a disaster for blocked threads ([The Sync-Over-Async Deadlock](#the-sync-over-async-deadlock)).

> **Best practice.** Don't create raw `Thread` objects for ordinary work. Use the pool (`Task.Run` for CPU-bound work) or, better, true async I/O, which holds no thread while it waits.

## Tasks: The Promise of a Future Result

A `Task` is a promise of a future result or failure; `Task<TResult>` carries a value. A task ends `RanToCompletion`, `Faulted` (holding the exception) or `Canceled`, and *continuations* attached to it run when it does. `async`/`await` is largely syntax over that machinery.

## async/await, Deeply

```csharp
public async Task<int> GetUserAgeAsync(int userId)
{
    User user = await _repository.GetUserAsync(userId);   // I/O: database
    int age = CalculateAge(user.BirthDate);               // CPU: trivial
    return age;
}
```

**`await` is not a blocking wait.** A better name would be "yield until ready".

### What `await` Actually Does

When execution reaches `await _repository.GetUserAsync(userId)`:

1. `GetUserAsync` starts the database operation and returns a `Task<User>` that is *not yet complete*.
2. If the task happens to be complete already (a cached result), execution continues straight through, with no suspension.
3. Otherwise the method **suspends**: it registers a *continuation* — "when this task finishes, run the rest of `GetUserAgeAsync`" — and **returns to its caller**. The thread is free.

No thread is parked on the database. When the response arrives, the continuation is queued to the thread pool (or posted to a captured `SynchronizationContext`), and a thread — possibly a different one — resumes after the `await` with `user` populated.

### The Compiler-Generated State Machine

A method can pause and resume later on another thread because the compiler rewrites it into a **state machine** that keeps its locals in fields, not on the stack. Simplified:

```csharp
private struct GetUserAgeStateMachine : IAsyncStateMachine
{
    public int state;                                 // where we paused
    public AsyncTaskMethodBuilder<int> builder;       // drives the returned Task
    public UserRepository repository;
    public int userId;                                // hoisted parameter

    private TaskAwaiter<User> userAwaiter;            // hoisted local

    public void MoveNext()
    {
        int age;
        try
        {
            if (state == -1) // first entry
            {
                userAwaiter = repository.GetUserAsync(userId).GetAwaiter();
                if (!userAwaiter.IsCompleted)
                {
                    state = 0;
                    // Schedule MoveNext to run again when the task completes,
                    // then return control to the caller.
                    builder.AwaitUnsafeOnCompleted(ref userAwaiter, ref this);
                    return;
                }
            }
            else // state == 0: we were resumed after the await
            {
                state = -1;
            }

            User user = userAwaiter.GetResult(); // get result OR rethrow exception
            age = CalculateAge(user.BirthDate);
        }
        catch (Exception ex)
        {
            state = -2;
            builder.SetException(ex);   // faults the returned Task
            return;
        }

        state = -2;
        builder.SetResult(age);         // completes the returned Task
    }
}
```

This explains almost every surprising behavior of async code:

- **Locals became fields** (`userAwaiter`, `userId`), so they survive suspension — and an `async` method allocates when it actually suspends.
- **`MoveNext` runs in pieces.** Each call runs to the next incomplete `await` and returns; each resumption jumps via `state` to where it left off.
- **`GetResult()` is where exceptions surface**, rethrown at the `await` line without an `AggregateException` wrapper.
- **The whole body sits inside one `try/catch`.** Every exception, even one thrown before the first `await`, goes to `builder.SetException`, never straight to the caller.
- **`AwaitUnsafeOnCompleted` hooks up the continuation**, and is where the `SynchronizationContext` gets captured.
- **The `builder` produces the `Task` your caller received** and completes it with the result or the exception.

Anything with this shape is awaitable — a `GetAwaiter()` whose result has `IsCompleted`, `OnCompleted` and `GetResult` — which is why `Task`, `ValueTask` and custom types all work.

> **Pay attention.** **Why an `async void` exception can't be caught — and why it kills the process.**
>
> - **The exception has no channel.** `async void` returns no `Task`, so the exception has nowhere to travel. The state machine's `catch` still hands it to the builder, so the call returns normally and the caller's `try/catch` never runs.
> - **The builder throws it where nobody is listening.** `AsyncVoidMethodBuilder.SetException` rethrows it on the captured `SynchronizationContext` (a UI dispatcher). When there is none — ASP.NET Core, a worker service, a console app — it rethrows it on a thread-pool thread.
> - **That thread has no caller above it.** The exception is unhandled, and the runtime terminates the process: in a web app every request dies, not just the one that failed.
>
> Return `Task`. `async void` is legal only for event handlers, whose signature the framework fixes, and their body should catch everything itself.

## SynchronizationContext and ConfigureAwait

Some continuations *must* run on a specific thread: in a desktop UI, only the UI thread may touch controls. `SynchronizationContext` answers "where should this continuation run?". An `await` captures the current one (or, if there is none, the current `TaskScheduler`) and posts the continuation back to it.

- **WPF / WinForms:** a UI context marshals continuations to the UI thread, which makes `await FetchAsync(); label.Text = result;` safe.
- **ASP.NET Core:** there is **no** `SynchronizationContext`; continuations run on any pool thread. (Classic ASP.NET had one, tied to the request, and it caused the deadlocks below.)
- **Console apps:** no context by default; continuations run on the pool.

### ConfigureAwait(false)

`ConfigureAwait(false)` says "I don't care which thread resumes me; don't marshal back to the captured context":

```csharp
public async Task<byte[]> DownloadAndHashAsync(string url)
{
    byte[] data = await _httpClient.GetByteArrayAsync(url).ConfigureAwait(false);
    // Resumes on a thread pool thread, NOT the original context.
    return SHA256.HashData(data);
}
```

- **Library code: use it on essentially every `await`.** You don't know your caller's context and don't need it; skipping the marshal is faster and keeps your library out of the deadlock below.
- **UI event handlers: don't**, when the code after the `await` touches UI.
- **ASP.NET Core: it changes nothing for correctness** — there is no context to return to. Don't rely on it to fix anything there.

> **Pitfall.** `ConfigureAwait(false)` affects only the *single* `await` it is attached to. Every `await` makes its own capture decision, so apply it consistently.

> **.NET 8 — `ConfigureAwaitOptions`.** An overload `ConfigureAwait(ConfigureAwaitOptions)` takes a `[Flags]` enum:
>
> - `ContinueOnCapturedContext` — the old `true`;
> - `SuppressThrowing` — wait for completion without observing the exception, handy for a fire-and-forget you inspect elsewhere;
> - `ForceYielding` — always suspend, even if the task is already complete.
>
> `SuppressThrowing` works only on a plain `Task`. On a `Task<T>` it throws `ArgumentOutOfRangeException`, so cast to `Task` first. None of this exists on `ValueTask`.

## The Sync-Over-Async Deadlock

The single most infamous async bug is **sync-over-async**: blocking a thread to wait for an async operation.

```csharp
// DANGER: do not do this
public string GetData()
{
    return GetDataAsync().Result;  // blocks the current thread
}
```

Under a single-threaded `SynchronizationContext` (classic ASP.NET, or a WPF/WinForms UI thread) the sequence is fatal:

1. The UI thread calls `GetData()`, which calls `GetDataAsync()` and blocks on `.Result`.
2. Inside `GetDataAsync`, `await SomethingAsync()` captures the UI `SynchronizationContext`.
3. `SomethingAsync` completes; its continuation must be posted back to **the UI thread**.
4. The UI thread is blocked at step 1 and never pumps the message loop to run it.
5. So `GetDataAsync` never finishes, and `.Result` never returns. **Deadlock**: the thread waits for a result only it can produce.

Two things break the cycle, but only one is a real fix:

- **The real fix: async all the way down** — `public async Task<string> GetData() => await GetDataAsync();`
- A partial mitigation is `ConfigureAwait(false)` inside `GetDataAsync`, so the continuation doesn't need the UI thread. You can't always control the whole chain, and it does nothing for starvation.

> **Pay attention.** **Starvation without a deadlock: why `.Result` hurts ASP.NET Core too.**
>
> **The mechanism.** With no `SynchronizationContext`, the continuation needs *a* pool thread, not *your* thread. Under load every pool thread is blocked in `.Result`, and the continuations that would unblock them — and the timer and I/O callbacks behind those — wait in the pool's queue. Only new threads get them out, and the pool adds them deliberately:
>
> - **Blocking the pool is told about.** Since .NET 6, a pool thread that blocks in `Task.Wait` (which `.Result` and `.GetAwaiter().GetResult()` also use) notifies the pool, which compensates. On .NET 10 it adds up to one extra thread per core straight away. After that it adds threads one at a time, pausing 25 ms before each, 25 ms longer after every further batch of one thread per core, up to 250 ms per thread.
> - **Blocking it isn't told about** — `Thread.Sleep`, `SemaphoreSlim.Wait`, a contended `lock` — gets only the starvation detector: about one thread per half second.
>
> **What it looks like.** Throughput collapses to the injection rate, latency rises on *every* endpoint, and the CPU stays low because the threads are waiting, not working. In `dotnet-counters`, the thread-pool queue length grows while the thread count climbs steadily. The exercise at the end of this chapter measures it.

> **Best practice.** Never block on async code with `.Result`, `.Wait()` or `.GetAwaiter().GetResult()` in application code. At a hard sync boundary — a constructor, an interface you can't change — isolate the block and know what it costs.

## CancellationToken: Cooperative Cancellation

You can't safely kill a running operation, so cancellation is **cooperative**: a `CancellationToken` flows into an operation, which *chooses* to observe it and stop. Whoever can cancel holds the `CancellationTokenSource`; consumers receive its `Token`.

```csharp
public async Task<Report> GenerateReportAsync(CancellationToken cancellationToken)
{
    var rows = new List<Row>();
    await foreach (Row row in _db.StreamRowsAsync(cancellationToken))
    {
        cancellationToken.ThrowIfCancellationRequested(); // honor the signal
        rows.Add(Transform(row));
    }
    return new Report(rows);
}

// Caller with a timeout:
using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
try
{
    Report report = await GenerateReportAsync(cts.Token);
}
catch (OperationCanceledException)
{
    Console.WriteLine("Report generation timed out.");
}
```

- **Propagate the token** (conventionally the last parameter) to every async call you make.
- **Honor it.** In tight loops, call `token.ThrowIfCancellationRequested()`; framework APIs (HttpClient, EF Core, streams) check the token you pass them.
- **Timeouts** are a `CancellationTokenSource` constructed with a delay, or `CancelAfter`.
- **Linked tokens** combine sources — "cancel if the request aborts *or* our 10-second budget expires":

```csharp
using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
    requestAborted, timeoutCts.Token);
await DoWorkAsync(linkedCts.Token); // cancels when EITHER source fires
```

Cancellation surfaces as `OperationCanceledException` (or its subtype `TaskCanceledException`): expected control flow, not an error, so don't log it as a failure.

> **Pay attention.** **A token stops only the calls it reaches.**
>
> Cancellation is cooperative: `Cancel()` sets a flag and runs the callbacks registered on the token, nothing more. Code stops only where it checks the flag, or where an API you passed the token to registered a callback: SqlClient registers one that cancels the running command on the server, and `HttpClient` aborts the request. One method in the chain that takes no token, or doesn't forward it, leaves everything below it running to completion.
>
> Fix: accept a `CancellationToken` in every async method on a request path and forward it. Analyzer CA2016 flags a call that could take the token in scope but doesn't; in .NET 10 it is only a suggestion by default, so raise it to a warning in `.editorconfig`.

In a web API the framework hands every endpoint a token that trips when the client disconnects; [CancellationToken Propagation](#cancellationtoken-propagation) in Chapter 5 shows what passing it down buys, and where you must not.

> **Pitfall.** Dispose your `CancellationTokenSource` (a `using` does it). A `CancellationTokenSource(TimeSpan)` schedules a timer, and an undisposed one keeps that timer registered until it fires.

## Composing Concurrent Work: WhenAll and WhenAny

Async shines when independent I/O runs *concurrently* rather than back to back:

```csharp
// SLOW: three round-trips back to back, ~300ms total
var a = await GetAAsync();
var b = await GetBAsync();
var c = await GetCAsync();
```

If the calls don't depend on each other, start them all, then await them together:

```csharp
// FAST: three round-trips in flight at once, ~100ms total
Task<A> ta = GetAAsync();
Task<B> tb = GetBAsync();
Task<C> tc = GetCAsync();
await Task.WhenAll(ta, tb, tc);
var result = new Combined(ta.Result, tb.Result, tc.Result); // safe: all completed
```

The tasks *start* before anything is awaited, so they run concurrently; after a successful `WhenAll`, `.Result` doesn't block.

### Exception Handling with WhenAll

If several tasks passed to `WhenAll` fault, the returned task holds an `AggregateException` with *all* of them, but `await` rethrows only **one**. To see every failure, keep the `WhenAll` task and read it in the `catch`:

```csharp
Task all = Task.WhenAll(task1, task2, task3);
try
{
    await all;                                   // rethrows ONE of the exceptions
}
catch (Exception)
{
    if (all.Exception is { } failures)           // null if the tasks were cancelled, not faulted
        foreach (Exception ex in failures.InnerExceptions)
            _logger.LogError(ex, "A task failed");
    throw;
}
```

> **Pay attention.** **Which exception `await` throws, and why only one.**
>
> - **Only one, on purpose.** `await` calls `GetResult()`, which rethrows the *first* exception the task stores, through `ExceptionDispatchInfo`, so it keeps its original stack trace ([The Mechanics That Bite](#the-mechanics-that-bite) in Chapter 9 shows that tool). Async code should read like synchronous code, where a call throws one exception.
> - **The blocking calls throw the wrapper.** `.Wait()` and `.Result` throw the `AggregateException` itself.
> - **"First" depends on the overload.** Since .NET 8, `WhenAll` over plain `Task`s lists failures in the order the tasks *fail*; the .NET 7 source walked them in argument order. Over `Task<T>` it is still argument order.
>
> Never depend on which one you get: log `InnerExceptions`, as above.

`Task.WhenAny` completes when the *first* task does — "first response wins", or a timeout race — and returns that *task*, which you await for its result or exception.

> **Pitfall.** With `WhenAny`, the tasks that didn't win keep running. If one later faults and nobody observes it, its exception is lost. Await or otherwise account for the losers.

Processing results as they finish with a `WhenAny` loop is O(n²), since each iteration rescans the rest. .NET 9's `Task.WhenEach` yields tasks in completion order: `await foreach (var task in Task.WhenEach(tasks)) { ... }`.

### Throttling with SemaphoreSlim

Firing 10,000 HTTP calls with `Task.WhenAll` will melt the remote server and exhaust your sockets. A `SemaphoreSlim` caps how many run at once:

```csharp
public async Task<IReadOnlyList<Result>> FetchAllAsync(IEnumerable<string> urls)
{
    using var gate = new SemaphoreSlim(initialCount: 8); // max 8 in flight
    var tasks = urls.Select(async url =>
    {
        await gate.WaitAsync();            // acquire a slot (async, no blocking)
        try { return await FetchAsync(url); }
        finally { gate.Release(); }        // ALWAYS release
    });
    return await Task.WhenAll(tasks);
}
```

> **Pitfall.** Release the semaphore in a `finally`. If an exception skips the `Release`, that slot is gone for good, and the pool of permits slowly drains to a deadlock. Inside async code, use `WaitAsync`, never the blocking `Wait`.

## IAsyncEnumerable and Async Streams

`Task<List<T>>` delivers everything once everything is done; `IAsyncEnumerable<T>` delivers items one at a time as they arrive — an async stream, produced with `async` and `yield return`:

```csharp
public async IAsyncEnumerable<Trade> ReadTradesAsync(
    [EnumeratorCancellation] CancellationToken cancellationToken = default)
{
    await using var reader = await _source.OpenAsync(cancellationToken);
    while (await reader.ReadAsync(cancellationToken))
    {
        yield return Map(reader.Current); // one item, lazily, as it's read
    }
}

// Consume with await foreach:
await foreach (Trade trade in ReadTradesAsync(cancellationToken))
{
    Process(trade);
}
```

`[EnumeratorCancellation]` lets a token passed via `WithCancellation` flow into the iterator's parameter:

```csharp
await foreach (var trade in source.ReadTradesAsync().WithCancellation(cancellationToken))
    Process(trade);
```

Async streams fit paging through large data sets and processing rows without loading everything into memory; each iteration can suspend and free the thread like any `await`.

## Thread Safety: Sharing State Correctly

Everything above runs work concurrently: tasks started before they are awaited, continuations resuming on whichever pool thread is free. Add the parallel loops of [Chapter 17](#chapter-17-runtime-internals-and-performance) and two threads can touch the same data at the same moment.

### Race Conditions

```csharp
_counter++; // NOT atomic
```

It is *read, add one, write back*: if two threads interleave, both read `5` and both write `6`. Two increments, one counted — a **race condition**.

### lock

The `lock` statement lets only one thread at a time execute a critical section:

```csharp
private readonly Lock _sync = new();   // .NET 9+; lock a private object on older targets
private int _counter;

public void Increment()
{
    lock (_sync)      // mutual exclusion
    {
        _counter++;
    }
}
```

On .NET 9 and C# 13, lock a `System.Threading.Lock`, whose faster API the compiler uses; on older targets, a dedicated `object`.

> **Pitfall.**
> - **Never `lock` on `this`, a `Type` or a string.** External code might lock the same instance and deadlock you. Use a private, read-only, dedicated object.
> - **Never `await` inside a `lock`.** A lock is thread-affine — the same thread must release it — but the continuation after an `await` may run on another thread. The compiler forbids it anyway. For async mutual exclusion, use `SemaphoreSlim(1, 1)` with `WaitAsync`.
> - **Keep locked sections tiny** to minimize contention.

### Interlocked

For a single atomic operation, a lock is overkill. `Interlocked` provides lock-free atomic primitives backed by CPU instructions:

```csharp
Interlocked.Increment(ref _counter);          // atomic ++
Interlocked.Add(ref _total, amount);          // atomic +=
long snapshot = Interlocked.Read(ref _big);   // atomic 64-bit read on 32-bit
// Compare-and-swap: the foundation of many lock-free algorithms
Interlocked.CompareExchange(ref _state, newValue, comparand);
```

They are much faster than locks for one variable, and can't deadlock.

### Concurrent Collections

Don't wrap a `Dictionary` in your own locks when `System.Collections.Concurrent` has a purpose-built type:

- `ConcurrentDictionary<K,V>`, with atomic `GetOrAdd` and `AddOrUpdate`;
- `ConcurrentQueue<T>`, `ConcurrentStack<T>`, `ConcurrentBag<T>`;
- `BlockingCollection<T>` for producer/consumer (in async code, Channels, in [Chapter 17](#chapter-17-runtime-internals-and-performance), are usually better).

```csharp
var cache = new ConcurrentDictionary<int, User>();
User user = cache.GetOrAdd(id, key => LoadUser(key)); // thread-safe
```

> **Pitfall.** `GetOrAdd`'s value factory may run more than once under contention, though only one result is stored. Don't put expensive or side-effecting work in the factory without accounting for that.

### Volatile and Memory Barriers (Conceptual)

The CPU and the compiler *reorder* memory operations, and cores keep values in registers. Without synchronization, a write by one thread may become visible to another late, or out of order:

```csharp
// Thread A
_data = Load();
_ready = true;    // could be reordered/visible before _data on another thread!

// Thread B
if (_ready) Use(_data); // might see _ready == true but stale _data
```

A **memory barrier** prevents reordering across it and forces visibility; `Volatile.Read`/`Volatile.Write` (and the `volatile` keyword) insert one. You rarely need this level: `lock`, `Interlocked` and the concurrent collections establish the barriers for you. Hand-rolled lock-free code is expert territory and a classic source of bugs that appear only in production, under load.

> **Best practice.** Prefer high-level synchronization (`lock`, `Interlocked`, concurrent collections, immutable data) over manual memory barriers. Reach for `volatile` only when you understand the memory model, and document *why*.

## Prove it

Three programs, one per trap. Predict each output before you run it: the gap between the prediction and the output is what this chapter is for.

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
- **It ends only because the pool keeps adding threads, slowly.** How fast it adds them, and why raising the minimum only moves the cliff, is in [Chapter 17](#chapter-17-runtime-internals-and-performance), which reruns this program with the counters open.

Then do *Find the bug* under *Exercises* below: a report endpoint with `.Result`, `.Wait()` and `Parallel.ForEach`. Name every defect, and the one that causes the outage, before you open the answer.

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

**3.** `.Result` in a request handler passes every test and fails only under load. Which finite resource does it exhaust, and why only at high concurrency?

<details>
<summary>Answer</summary>

Pool threads. `.Result` holds a pool thread for the whole I/O wait. At low concurrency there are spare threads. At high concurrency every thread is held, and the continuations and timer callbacks that would release them wait in the queue behind new requests. The pool adds threads only gradually, so latency spreads to every endpoint while the CPU stays low.

A test makes a handful of requests, one after another, so the pool never runs dry. A new `HttpClient` per request fails the same way with a different resource, local ports: [Keep-Alive, Connection Pooling, and Socket Exhaustion](#keep-alive-connection-pooling-and-socket-exhaustion) in Chapter 5.
</details>

## Check at work

**Inspect.** Search your service for `async void`, `.Result`, `.Wait()` and `.GetAwaiter().GetResult()`. Sort every hit into one of three bins: an event handler, start-up code, or a request or message path, which is a latent outage. Then follow one endpoint's `await` chain from the action down to its database or HTTP call: a `CancellationToken` that stops halfway leaves everything below it uncancellable.

**Measure.** Check that something charts the service's thread-pool queue length and thread count: `dotnet.thread_pool.queue.length` and `dotnet.thread_pool.thread.count` in an APM on .NET 9+ (check it plots the value, not a rate), `ThreadPool Queue Length` and `ThreadPool Thread Count` in `dotnet-counters` (on .NET 9 and 10 with `--counters 'EventCounters\System.Runtime'`; [Diagnosing a Performance Problem](#diagnosing-a-performance-problem-a-worked-methodology) in Chapter 9 explains why). Without them, starvation looks exactly like a slow database. Read both at your traffic peak: a queue that grows while the thread count climbs is the starvation fingerprint.

## Exercises

### Find the bug

This handler compiles, passes its unit test, and takes the service down under load.

```csharp
[HttpGet("/reports/{id:int}")]
public IActionResult GetReport(int id)
{
    var report = _reportService.BuildReportAsync(id).Result;

    var recipients = _db.Subscribers
        .Where(s => s.ReportId == id)
        .ToList();

    Parallel.ForEach(recipients, r =>
    {
        _mailer.SendAsync(r.Email, report).Wait();
    });

    return Ok(report);
}
```

Name every defect you can see, then say which one causes the outage.

<details>
<summary>Answer</summary>

Four separate problems, in increasing order of severity:

1. **`.Result` and `.Wait()` are sync-over-async.** Each blocks a pool thread for the whole I/O wait.
2. **`Parallel.ForEach` over async work multiplies the blocking.** It is for CPU work: each iteration here blocks a pool thread in `.Wait()` for a whole email send, and `Parallel.ForEach` borrows more pool threads for more iterations, so one request blocks several threads. `Parallel.ForEachAsync` with a `MaxDegreeOfParallelism` is the tool for async fan-out.
3. **No `CancellationToken` anywhere.** If the client disconnects, every email still goes out.
4. **The outage is thread-pool starvation.** Each in-flight request holds one thread in `.Result` and several in `.Wait()`; the continuations that would release them queue behind new requests, and the pool adds threads only gradually ([The Sync-Over-Async Deadlock](#the-sync-over-async-deadlock)). Latency climbs on *every* endpoint while the CPU stays low — which is why it is so often misdiagnosed as a database problem.

The fix: `async Task<IActionResult>`, `await` throughout, `Parallel.ForEachAsync` with a `MaxDegreeOfParallelism`, and a `CancellationToken` threaded from the action signature down.

Verified in the repository (`verify/exercises/Ch08`): on 4 vCPU, 20 concurrent requests (200 ms of report I/O, then four 200 ms emails each) took **6 s** with this code against **0.8 s** with the fix; an unrelated request waited **1.8 s** for a thread, and the pool grew to **49 threads** against 5. A second pair of tests shows the fix stopping when the request is aborted, while the original has no token to stop it.
</details>

### What would you do

A colleague's PR adds `ConfigureAwait(false)` to every `await` in a new ASP.NET Core service, citing a blog post about deadlocks. It is 300 lines of diff across 40 files. What do you say in review?

<details>
<summary>How a senior engineer reasons about it</summary>

The technically correct observation: ASP.NET Core has no `SynchronizationContext`, so `ConfigureAwait(false)` changes nothing here. The deadlock it prevents is a WinForms, WPF or legacy ASP.NET problem. In a *library* such apps might consume it is good practice; in an ASP.NET Core service it is noise that makes future diffs harder to read.

But the comment that lands well doesn't stop there. Your colleague applied something diligently and is right about the phenomenon, wrong about whether this codebase has it. So explain the mechanism (what a `SynchronizationContext` is, and that ASP.NET Core doesn't install one), agree where the advice *does* apply, and let them decide whether to drop the change or keep it for a library project in the solution.

There is also proportion. If the team has an analyzer rule about it, this is a rule discussion, not a PR discussion. And if the diff is otherwise good, "unnecessary but harmless, let's not block on it" is legitimate: cargo-culted `ConfigureAwait(false)` costs readability, not correctness. [Chapter 16: Working Like a Middle Developer](#chapter-16-working-like-a-middle-developer) covers telling a blocking review comment from a nit.
</details>

## Interview Questions

**Async vs multithreading — what's the difference?**
Multithreading uses multiple threads to do work in parallel (CPU-bound). Async is about *not blocking* a thread while waiting for something else (I/O-bound) — one thread can serve many in-flight operations. Async ≠ parallel: `await` on a single call is still sequential; you get concurrency by starting multiple tasks before awaiting.

**Red flag:** "Async makes the code faster because it runs in parallel" — a single awaited call is just as slow; async buys scalability, not speed.

**`Task` vs `ValueTask` — when `ValueTask`?**
`Task` is a heap-allocated reference type; every async call allocates one. `ValueTask` avoids that allocation when the result is *often already available* synchronously (cache hits, buffered reads). Use it in hot, high-frequency APIs where most calls complete synchronously. Don't await a `ValueTask` twice or store it — it's single-consumption. [Task vs ValueTask](#task-vs-valuetask) in Chapter 17 has the rules.

**What does `ConfigureAwait(false)` do and where?**
It tells the continuation not to resume on the captured synchronization context, resuming on a thread-pool thread instead. Use it in library code to avoid deadlocks and unnecessary context hops. In ASP.NET Core there's no sync context, so it matters less there, but it's still good hygiene for reusable libraries.

**Why does `.Result` deadlock?**
On a platform with a single-threaded sync context (classic UI, legacy ASP.NET), blocking on `.Result`/`.Wait()` holds that thread while the awaited continuation is queued to run *on the same thread* — mutual wait, deadlock. The fix is to be async all the way down and never block on async code. ASP.NET Core has no synchronization context, so continuations run on any pool thread and this deadlock can't happen there; sync-over-async still blocks one pool thread per waiting request, which starves the thread pool under load.

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
