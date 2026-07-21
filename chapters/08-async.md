# Chapter 8: Asynchronous & Concurrent Programming

_⏱️ Estimated read time: ~33 min ·     4888 words (study pace)_

Few topics separate a mid-level .NET developer from a senior one as sharply as a genuine understanding of asynchrony. Almost everyone can sprinkle `async` and `await` on a method until the compiler stops complaining. Far fewer can explain what those keywords actually *do*, why a stray `.Result` can freeze a web server solid, or when reaching for a thread actively makes things slower.

This chapter builds that understanding from the ground up. We will start with the *why*, then peel back the compiler magic, and finally work our way up to production-grade patterns for cancellation, throttling, streaming, and thread safety. By the end you should be able to reason about async code the way the runtime does.

## Why Async Exists: I/O-Bound vs CPU-Bound Work

Every unit of work your program performs falls into one of two camps.

**CPU-bound work** keeps a processor core busy: hashing a password, resizing an image, summing a billion numbers. The only way to do more CPU-bound work at once is to use more cores.

**I/O-bound work** spends almost all its time *waiting* for something outside the CPU: a database query, an HTTP call, reading a file from disk. During that wait, no core is doing anything useful on your behalf. The bytes are in flight somewhere on a network or spinning up from a disk.

Here is the analogy I keep coming back to. Imagine a restaurant kitchen with one chef (a thread). A customer orders a slow-braised dish that needs 40 minutes in the oven. A *synchronous* chef would stand and stare at the oven for 40 minutes, doing nothing, while a queue of hungry customers forms. An *asynchronous* chef puts the dish in the oven, sets a timer, and immediately starts prepping the next order. When the timer dings, the chef comes back to finish the braise.

Async programming is that timer. It lets a single thread kick off an I/O operation, walk away to do other work, and be notified when the result is ready. **The central insight: async is primarily about not wasting threads while waiting for I/O. It is not, by itself, about doing multiple things at once.**

This is why the naïve mental model — "async means it runs on another thread" — is wrong and will lead you astray. A well-written async I/O call uses *no* thread at all while it waits. The braise cooking in the oven does not require a chef standing next to it.

### Threads Are Expensive

Why do we care so much about not wasting threads? Because threads are a scarce, heavyweight resource.

Each managed thread reserves 1 MB of stack memory by default. Creating one involves the OS. Worse, having many threads means the OS scheduler must constantly *context-switch* between them — saving and restoring register state, invalidating CPU caches. A server that spins up one thread per concurrent request will collapse under a few thousand connections, spending more time switching than working.

Consider a web server handling 10,000 concurrent requests, each waiting on a slow database. Thread-per-request means 10,000 threads and roughly 10 GB of stack space, most of it doing nothing but waiting. Async lets those same 10,000 requests share a handful of threads, because a thread waiting on the database is released back to do other work.

### The Thread Pool

Manually creating threads is rarely the right move. .NET maintains a **thread pool**: a managed set of reusable worker threads. When you need a thread to run a short piece of work, you borrow one from the pool; when done, it returns for reuse. This amortizes creation cost and caps the total thread count.

The thread pool also has a *hill-climbing* algorithm that slowly injects new threads when work is backing up. This slow injection is the reason sync-over-async deadlocks and starvation are so vicious, as we will see.

> **Best practice:** Do not create raw `Thread` objects for ordinary work. Use the thread pool (via `Task.Run` for CPU-bound work) or, better, true async I/O that uses no thread at all while waiting.

## Tasks: The Promise of a Future Result

Before `async`/`await`, .NET introduced the **Task Parallel Library (TPL)** and the `Task` type. A `Task` represents an operation that will complete in the future — it is a *promise* of a result (or of a failure). `Task<TResult>` carries a value; non-generic `Task` represents a `void`-returning operation.

A `Task` has state: it can be running, `RanToCompletion`, `Faulted` (it threw), or `Canceled`. You can attach *continuations* — code to run when it finishes. In fact, `async`/`await` is largely syntactic sugar over this continuation machinery, which is exactly what we will unpack next.

## async/await, Deeply

Let's start with a method a beginner would recognize.

```csharp
public async Task<int> GetUserAgeAsync(int userId)
{
    User user = await _repository.GetUserAsync(userId);   // I/O: database
    int age = CalculateAge(user.BirthDate);               // CPU: trivial
    return age;
}
```

To truly understand this, you have to accept that **`await` is not a blocking wait.** The word is misleading. A better name would be "yield-until-ready." Here is what actually happens.

### What `await` Actually Does

When execution reaches `await _repository.GetUserAsync(userId)`, the runtime:

1. Calls `GetUserAsync`, which starts the database operation and immediately returns a `Task<User>` that is *not yet complete*.
2. Checks whether that task is already finished. If it somehow is (cached result), execution continues straight through with no suspension — a fast path worth knowing about.
3. If it is not complete, the method **suspends**. It hooks up a *continuation* — "when this task finishes, run the rest of `GetUserAgeAsync`" — and **returns to its caller**. The thread is now free.

That last point is the crux. The thread that called `GetUserAgeAsync` is not blocked. It returns all the way up and goes off to do other work (serve another request, keep the UI responsive). No thread is parked waiting on the database.

When the database responds, the I/O completion mechanism schedules the continuation. A thread — possibly a *different* one — picks up right after the `await`, with `user` populated, and runs `CalculateAge`.

### The Compiler-Generated State Machine

How can a method "pause in the middle" and "resume later, possibly on another thread"? Local variables live on the stack, and the stack is gone once the method returns. The answer: the C# compiler rewrites your async method into a **state machine** — a struct that captures all the local state on the heap so it can be suspended and resumed.

Roughly, the compiler transforms our method into something like this (simplified and cleaned up for readability):

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

Study this, because it explains almost every surprising behavior of async code:

- **Your locals became fields** (`user`, `userAwaiter`, `userId`). They are "hoisted" onto the state-machine object so they survive suspension. This is why an `async` method allocates when it actually suspends.
- **`MoveNext` runs in pieces.** First call runs up to the first incomplete `await`, then returns. Each resumption re-enters `MoveNext`, jumps via `state` to where it left off, and continues to the next await or the end.
- **`GetResult()` is where exceptions surface.** If the awaited task faulted, `GetResult` rethrows. That is why exceptions from awaited operations appear at the `await` line, cleanly, without an `AggregateException` wrapper.
- **`AwaitUnsafeOnCompleted` is the continuation hookup.** It registers `MoveNext` to be called again when the awaited task completes — and this is exactly where `SynchronizationContext` (coming up) gets captured.
- **The `builder`** is what produces the `Task` your caller received and ultimately completes it with a result or exception.

An `awaitable` is anything exposing this shape — a `GetAwaiter()` method returning a type with `IsCompleted`, `OnCompleted`, and `GetResult`. That is why you can `await` a `Task`, a `ValueTask`, and even custom types. The compiler only cares about the pattern, not the concrete type.

> **Key takeaway:** `await` splits a method into segments joined by continuations. Between segments, the calling thread is free. There is no magic background thread pumping your async method — it is cooperative suspension and resumption driven by I/O completions.

## SynchronizationContext and ConfigureAwait

We said the continuation might run on a *different* thread. But sometimes it *must* run on a *specific* thread. Consider a desktop UI: only the UI thread is allowed to touch UI controls. If your continuation updates a label, it had better run on the UI thread.

`SynchronizationContext` is the abstraction that captures "where should this continuation run?" When you `await`, the runtime captures the *current* `SynchronizationContext` (or, if none, the current `TaskScheduler`). When the awaited task completes, the continuation is *posted back* to that captured context.

- **WPF / WinForms:** there is a UI `SynchronizationContext` that marshals continuations back to the single UI thread. This is what lets you write `await FetchAsync(); label.Text = result;` and have the label update safely.
- **ASP.NET Core:** there is **no** `SynchronizationContext`. Continuations simply run on any available thread pool thread. (Classic ASP.NET *did* have one, tied to the request — a common source of the deadlocks discussed below.)
- **Console apps:** no context by default; continuations run on the thread pool.

### ConfigureAwait(false)

Posting back to a captured context has a cost, and in library code you usually do not need it — you are not touching UI. `ConfigureAwait(false)` says "I do not care what thread resumes me; do not bother marshaling back to the captured context."

```csharp
public async Task<byte[]> DownloadAndHashAsync(string url)
{
    byte[] data = await _httpClient.GetByteArrayAsync(url).ConfigureAwait(false);
    // Resumes on a thread pool thread, NOT the original context.
    return SHA256.HashData(data);
}
```

When and why to use it:

- **In library code (NuGet packages, shared internal libraries): use `ConfigureAwait(false)` on essentially every await.** You do not know your caller's context, you do not need it, and skipping the marshal is faster and — crucially — avoids contributing to deadlocks.
- **In UI event handlers:** do *not* use it when the code after the await touches UI, because you *need* to be back on the UI thread.
- **In ASP.NET Core:** it is largely a no-op for correctness since there is no `SynchronizationContext`. Many teams still add it out of habit or for the tiny perf win, but it is not required. Do not rely on it to "fix" anything there.

> **Pitfall:** `ConfigureAwait(false)` affects only the *single* await it is attached to, not the whole method. Every await makes its own capture decision. If you need it, apply it consistently.

## The Sync-Over-Async Deadlock

Now we can explain the single most infamous async bug, and why senior engineers flinch when they see `.Result` or `.Wait()`.

**Sync-over-async** means blocking a thread to wait for an async operation:

```csharp
// DANGER: do not do this
public string GetData()
{
    return GetDataAsync().Result;  // blocks the current thread
}
```

In a context that has a single-threaded `SynchronizationContext` (classic ASP.NET, or a WPF/WinForms UI thread), here is the fatal sequence:

1. The UI thread calls `GetData()`, which calls `GetDataAsync()`, then blocks on `.Result`. **The UI thread is now stuck, waiting.**
2. Inside `GetDataAsync`, an `await SomethingAsync()` runs. The runtime captures the UI `SynchronizationContext`.
3. `SomethingAsync` completes. Its continuation must be posted back to the captured context — **the UI thread**.
4. But the UI thread is blocked at step 1, waiting for `.Result`. It will never pump the message loop to run the continuation.
5. The continuation cannot run, so `GetDataAsync` never finishes, so `.Result` never returns. **Deadlock.** The thread is waiting for a result that can only be produced by that same thread.

It is a chef who refuses to leave the oven until the braise is done, but the braise needs the chef to take it out of the oven. Nobody moves.

Two things break the cycle, but only one is a real fix:

- **The real fix: async all the way down.** Do not block. Make the caller `async` and `await` the operation. `public async Task<string> GetData() => await GetDataAsync();`
- A partial mitigation is `ConfigureAwait(false)` inside `GetDataAsync`, so the continuation does not need the UI thread. But you cannot always control the whole call chain, and it does nothing for thread-pool starvation.

Even in ASP.NET Core, where there is no `SynchronizationContext` to deadlock on, sync-over-async is still harmful: it burns a thread-pool thread doing nothing but blocking. Under load, enough blocked threads exhaust the pool. New work waits for the pool's slow hill-climbing to add threads, latency spikes, and you get **thread-pool starvation** — a "soft deadlock" that looks like a mysterious hang.

> **Best practice:** Never block on async code with `.Result`, `.Wait()`, or `.GetAwaiter().GetResult()` in application code. Async all the way down. If you are at a hard sync boundary (a constructor, `Main` in old frameworks, an interface you cannot change), isolate it and understand the risk.

## Task vs ValueTask

`Task` is a class — a heap allocation. For a method called millions of times per second that *usually completes synchronously* (e.g., a cache hit), allocating a `Task` object each time is pure waste.

`ValueTask<T>` is a struct that can wrap *either* an already-available result *or* an underlying `Task` for the slow path. When the result is ready synchronously, no allocation occurs.

```csharp
public ValueTask<User> GetUserAsync(int id)
{
    if (_cache.TryGetValue(id, out User? cached))
        return new ValueTask<User>(cached);          // fast path, no allocation

    return new ValueTask<User>(LoadFromDbAsync(id));  // slow path wraps a Task
}
```

`ValueTask` comes with strict rules, because a struct-based awaitable is more fragile:

> **Pitfalls with ValueTask:**
> - **Do not await a `ValueTask` more than once.** Its backing resource may be recycled after the first consumption.
> - **Do not access `.Result` before it completes**, and do not use it concurrently.
> - **Do not store a `ValueTask` in a field or collection** for later. If you must keep it, call `.AsTask()` to convert to a real `Task`.

**Guidance:** Default to `Task`. It is simpler and forgiving. Reach for `ValueTask` only in hot, allocation-sensitive APIs where profiling shows `Task` allocation matters and the operation frequently completes synchronously. Most application code should never see a `ValueTask`.

## CancellationToken: Cooperative Cancellation

You cannot forcibly and safely kill a running operation in .NET — that would risk corrupt state. Instead, .NET uses **cooperative cancellation**: a `CancellationToken` is a signal that flows into an operation, and the operation *chooses* to observe it and stop.

The producer of the signal holds a `CancellationTokenSource`; consumers receive its `Token`.

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

Key mechanics:

- **Propagate the token.** Accept a `CancellationToken` parameter (conventionally last, often defaulted to `default`) and pass it into every async call you make. A token you receive but never forward is a bug.
- **Honor it.** In tight loops, call `token.ThrowIfCancellationRequested()`. Well-written framework APIs (HttpClient, EF Core, streams) already check the token you pass them.
- **Timeouts** are just a `CancellationTokenSource` constructed with a delay, or `CancelAfter`.
- **Linked tokens** combine multiple sources — e.g., "cancel if the request aborts *or* our 10-second budget expires":

```csharp
using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
    requestAborted, timeoutCts.Token);
await DoWorkAsync(linkedCts.Token); // cancels when EITHER source fires
```

Cancellation surfaces as `OperationCanceledException` (or its subtype `TaskCanceledException`). This is expected control flow, not an error — catch it where you want to react, and generally do not log it as a failure.

> **Pitfall:** Always `Dispose` your `CancellationTokenSource` (a `using` handles it). A `CancellationTokenSource(TimeSpan)` schedules a timer; failing to dispose leaks that timer registration.

## Composing Concurrent Work: WhenAll and WhenAny

Async shines when you run independent I/O *concurrently* rather than sequentially. The mistake below is depressingly common:

```csharp
// SLOW: three round-trips back to back, ~300ms total
var a = await GetAAsync();
var b = await GetBAsync();
var c = await GetCAsync();
```

If those calls do not depend on each other, start them all, then await together:

```csharp
// FAST: three round-trips in flight at once, ~100ms total
Task<A> ta = GetAAsync();
Task<B> tb = GetBAsync();
Task<C> tc = GetCAsync();
await Task.WhenAll(ta, tb, tc);
var result = new Combined(ta.Result, tb.Result, tc.Result); // safe: all completed
```

Note we *start* the tasks (no `await` yet) so they run concurrently, then `WhenAll` waits for all of them. After `WhenAll` completes successfully, reading `.Result` is safe and non-blocking.

### Exception Handling with WhenAll

Here is a subtle and important behavior. If *multiple* tasks passed to `WhenAll` fault, the returned task holds an `AggregateException` containing *all* of them. But `await` unwraps and rethrows only the **first** exception:

```csharp
try
{
    await Task.WhenAll(task1, task2, task3);
}
catch (Exception firstOnly)
{
    // Only the FIRST exception is thrown here.
    // To see all of them, inspect the WhenAll task itself:
}
```

To inspect every failure, examine the aggregated task:

```csharp
Task all = Task.WhenAll(task1, task2, task3);
try { await all; }
catch
{
    foreach (Exception ex in all.Exception!.InnerExceptions)
        _logger.LogError(ex, "A task failed");
}
```

`Task.WhenAny` returns as soon as the *first* task completes — useful for "first response wins" or implementing a timeout race. It returns the completed *task*, which you then await to get its result or observe its exception.

> **Pitfall:** With `WhenAny`, the tasks that did *not* win keep running. If one later faults and you never observe it, you have an unobserved exception. Make sure you eventually await or otherwise account for the losers.

The classic "process results as they finish" idiom — loop, `WhenAny`, remove the winner from a list, repeat — is O(n²) over many tasks, because each iteration rescans the remaining set. .NET 9 replaces it with `Task.WhenEach`, which yields an `IAsyncEnumerable` of the tasks in completion order: `await foreach (var task in Task.WhenEach(tasks)) { ... }`. Prefer it whenever you want to consume completions as they arrive rather than wait for all of them.

### Throttling with SemaphoreSlim

Firing 10,000 HTTP calls with `Task.WhenAll` will melt the remote server and exhaust your sockets. A `SemaphoreSlim` acts as a bouncer, capping how many operations run concurrently:

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

> **Pitfall:** Release the semaphore in a `finally`. If an exception skips the `Release`, that slot is gone forever and you slowly deadlock as the pool of permits drains. Use `WaitAsync`, never the blocking `Wait`, inside async code.

## IAsyncEnumerable and Async Streams

Sometimes you want to consume results *as they arrive*, not wait for a whole collection. A `Task<List<T>>` gives you everything at once, after everything is done. `IAsyncEnumerable<T>` gives you items one at a time, each awaited — an async stream. Think of `Task<List<T>>` as waiting for the whole pizza to be baked and boxed, while `IAsyncEnumerable<T>` is a conveyor belt handing you each slice as it comes out.

You produce one with `async` + `yield return`:

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

The `[EnumeratorCancellation]` attribute lets a token passed via `WithCancellation` flow into the iterator's parameter:

```csharp
await foreach (var trade in source.ReadTradesAsync().WithCancellation(cancellationToken))
    Process(trade);
```

Async streams are ideal for paging through large datasets, reading from network sockets, or processing rows without loading everything into memory. Each `await foreach` iteration can suspend and free the thread just like a normal `await`.

## The TPL: Parallelism for CPU-Bound Work

Everything so far has been about *concurrency* for I/O — juggling waits. **Parallelism** is different: genuinely running CPU-bound work on multiple cores at once. Concurrency is one chef juggling many dishes; parallelism is many chefs.

### Task.Run — Offloading CPU Work

`Task.Run` schedules a delegate on the thread pool. Use it to move CPU-bound work off a thread that must stay responsive (like a UI thread):

```csharp
// In a UI event handler: keep the UI thread free during heavy computation
int result = await Task.Run(() => ComputeExpensiveThing(data));
```

> **Pitfall:** Do *not* wrap async I/O in `Task.Run` on a server. `await Task.Run(() => httpClient.GetAsync(url))` does not make I/O faster — it just burns an extra thread pool thread to babysit an operation that needed no thread at all. On ASP.NET Core, `Task.Run` for I/O is an anti-pattern that reduces scalability. Reserve `Task.Run` for real CPU-bound work, and generally only from client apps.

### Parallel.For / ForEach

For data-parallel CPU work, `Parallel` partitions a loop across cores:

```csharp
Parallel.ForEach(images, image =>
{
    image.Thumbnail = GenerateThumbnail(image); // CPU-bound, independent
});
```

`Parallel.ForEachAsync` (added in .NET 6) is the modern tool for running *asynchronous* work over a collection with a built-in concurrency limit — a cleaner alternative to the SemaphoreSlim pattern for many cases:

```csharp
await Parallel.ForEachAsync(
    urls,
    new ParallelOptions { MaxDegreeOfParallelism = 8 },
    async (url, ct) => await ProcessAsync(url, ct));
```

### When Parallelism Helps vs Hurts

Parallelism is not free, and applying it blindly can make code *slower*:

- **It helps** when work is CPU-bound, the items are independent, and each item does enough work to outweigh the coordination overhead.
- **It hurts** when the work is I/O-bound (you do not need cores to wait — use async concurrency instead), when items are tiny (scheduling overhead dominates), or when they share mutable state and force locking (contention serializes them anyway, plus lock overhead).

> **Best practice:** Parallelism for CPU, async for I/O. Confusing the two — `Task.Run` around I/O, or `Parallel.ForEach` around network calls — is a hallmark of not-yet-senior code.

## System.Threading.Channels: Producer/Consumer Pipelines

When you have producers generating work and consumers processing it, and you want them decoupled with back-pressure, `System.Threading.Channels` is the modern, allocation-friendly answer. A `Channel<T>` is an async-aware, thread-safe queue.

```csharp
var channel = Channel.CreateBounded<WorkItem>(new BoundedChannelOptions(capacity: 100)
{
    FullMode = BoundedChannelFullMode.Wait // producers wait when full: back-pressure
});

// Producer
async Task ProduceAsync(CancellationToken ct)
{
    foreach (var item in GetWork())
        await channel.Writer.WriteAsync(item, ct); // awaits if channel is full
    channel.Writer.Complete();                      // signal: no more items
}

// Consumer(s)
async Task ConsumeAsync(CancellationToken ct)
{
    await foreach (WorkItem item in channel.Reader.ReadAllAsync(ct))
        await HandleAsync(item);
}

// Wire up several consumers reading the same channel:
var producer = ProduceAsync(cts.Token);
var consumers = Enumerable.Range(0, 4).Select(_ => ConsumeAsync(cts.Token));
await Task.WhenAll(consumers.Prepend(producer));
```

The magic is the **bounded** channel with `FullMode = Wait`: when consumers fall behind and the buffer fills, `WriteAsync` suspends the producer until space frees up. That is *back-pressure* — the system self-regulates instead of exploding memory by queueing unbounded work. `Complete()` on the writer lets `ReadAllAsync` finish gracefully when the well runs dry. This pattern underpins many high-throughput ingestion pipelines.

## Thread Safety: Sharing State Correctly

Concurrency and parallelism both raise the specter of two threads touching the same data at once. This is where subtle, non-deterministic bugs live.

### Race Conditions

Consider the world's most innocent-looking line:

```csharp
_counter++; // NOT atomic
```

This is really *read `_counter`, add one, write it back* — three steps. If two threads interleave, both read `5`, both compute `6`, both write `6`. Two increments, but the counter went up by one. That is a **race condition**: correctness depends on timing you do not control.

### lock

The `lock` statement ensures only one thread executes a critical section at a time, using a monitor on a private object:

```csharp
private readonly object _sync = new();
private int _counter;

public void Increment()
{
    lock (_sync)      // mutual exclusion
    {
        _counter++;
    }
}
```

> **Pitfalls with lock:**
> - **Never `lock` on `this`, a `Type`, or a string** — external code might lock the same instance and deadlock you. Use a private, readonly, dedicated object.
> - **Never `await` inside a `lock`.** A `lock` is thread-affine (the same thread must release it), but the continuation after an await may run on a different thread. The compiler forbids it anyway; if you need async mutual exclusion, use `SemaphoreSlim(1, 1)` with `WaitAsync`.
> - Keep locked sections tiny to minimize contention.

### Interlocked

For simple atomic operations, locks are overkill. `Interlocked` provides lock-free atomic primitives backed by CPU instructions:

```csharp
Interlocked.Increment(ref _counter);          // atomic ++
Interlocked.Add(ref _total, amount);          // atomic +=
long snapshot = Interlocked.Read(ref _big);   // atomic 64-bit read on 32-bit
// Compare-and-swap: the foundation of many lock-free algorithms
Interlocked.CompareExchange(ref _state, newValue, comparand);
```

These are dramatically faster than locks for single-variable updates and cannot deadlock.

### Concurrent Collections

Do not wrap a plain `Dictionary` or `List` in your own locks if a purpose-built type exists. The `System.Collections.Concurrent` namespace offers thread-safe collections tuned for concurrent access:

- `ConcurrentDictionary<K,V>` — with atomic `GetOrAdd`, `AddOrUpdate`.
- `ConcurrentQueue<T>`, `ConcurrentStack<T>`, `ConcurrentBag<T>`.
- `BlockingCollection<T>` for producer/consumer (though Channels is usually better for async).

```csharp
var cache = new ConcurrentDictionary<int, User>();
User user = cache.GetOrAdd(id, key => LoadUser(key)); // thread-safe
```

> **Pitfall:** `GetOrAdd`'s value factory may run more than once under contention (though only one result is stored). Do not put expensive or side-effecting work directly in the factory without accounting for that.

### Volatile and Memory Barriers (Conceptual)

Here we reach the deep end. On modern hardware, the CPU and compiler *reorder* memory operations for performance, and each core may cache values in registers. Without synchronization, a write by one thread may not become visible to another — or may appear out of order.

```csharp
// Thread A
_data = Load();
_ready = true;    // could be reordered/visible before _data on another thread!

// Thread B
if (_ready) Use(_data); // might see _ready == true but stale _data
```

A **memory barrier** (fence) is an instruction that prevents such reordering across it and forces visibility. `Volatile.Read`/`Volatile.Write` (and the `volatile` keyword) insert the appropriate barriers so that a read always sees the latest write and ordering is preserved around that variable.

The good news: **you rarely need to reason at this level directly.** `lock`, `Interlocked`, and the concurrent collections all establish the necessary barriers for you. Their internal synchronization guarantees that data written before releasing a lock is visible to the next thread that acquires it. Hand-rolled lock-free code using `volatile` is an expert endeavor and a frequent source of "works on my machine, fails in production under load" bugs.

> **Best practice:** Prefer high-level synchronization (`lock`, `Interlocked`, concurrent collections, immutable data) over manual memory barriers. Reach for `volatile` only when you deeply understand the memory model, and even then, document *why*.

## A Brief Note on Rx.NET

Everything above treats async as producing a *single* future value (`Task`) or a *pull-based* stream you iterate (`IAsyncEnumerable`). **Reactive Extensions (Rx.NET)** offers a different model: `IObservable<T>`, a *push-based* stream of events over time that you *subscribe* to.

Where `IAsyncEnumerable` is a conveyor belt you pull slices from at your own pace, `IObservable` is a firehose that pushes events at you whenever they occur — mouse moves, sensor readings, message-bus events. Rx's real power is its rich LINQ-style operators for *composing* event streams: `Throttle`, `Buffer`, `Merge`, `CombineLatest`, `Window`. Debouncing a search box as the user types is a classic one-liner in Rx.

Rx is a specialized tool. For request/response and ordinary async I/O, stick with `Task` and `await`. When you are genuinely modeling *streams of events over time* with complex temporal composition, Rx earns its place. Knowing it exists — and when *not* to reach for it — is itself a mark of seniority.

## Summary

- Async exists to avoid wasting threads while waiting on **I/O**; parallelism exists to use multiple cores for **CPU** work. Do not confuse them.
- `await` does not block; the compiler builds a **state machine** that suspends your method, frees the thread, and resumes via a **continuation** when the awaited task completes.
- `SynchronizationContext` decides where continuations resume. `ConfigureAwait(false)` opts out of marshaling back — essential in libraries, irrelevant to correctness in ASP.NET Core, dangerous to omit near UI code.
- Blocking on async with `.Result`/`.Wait()` causes **deadlocks** (single-threaded contexts) or **thread-pool starvation** (servers). Async all the way down.
- Prefer `Task`; use `ValueTask` only in profiled hot paths, and never await it twice.
- Propagate and honor `CancellationToken`; use timeouts and linked sources.
- Run independent I/O concurrently with `WhenAll`; throttle with `SemaphoreSlim` or `Parallel.ForEachAsync`; remember `WhenAll` hides all-but-one exception behind `await`.
- Stream with `IAsyncEnumerable`; build pipelines with `Channels` and back-pressure.
- Protect shared state with `lock`, `Interlocked`, and concurrent collections; leave manual memory barriers to the experts.

Master these, and asynchronous code stops being a source of mysterious hangs and becomes what it should be: a precise tool for building responsive, scalable systems.
