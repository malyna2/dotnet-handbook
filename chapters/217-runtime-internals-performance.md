# Chapter 17: Runtime Internals and Performance

Every request a .NET service handles shares two runtime resources: a small pool of threads and a generational heap. Both are tuned for short work. A thread held while it waits, or an object kept a little too long, becomes a delay for every other request. This chapter makes you able to predict how a service behaves when its threads block or its heap churns, prove it from counters, a trace and a dump, and choose between tuning the runtime and changing the code, with the cost of each spelled out for the team that will live with the choice.

It builds on Part 1: the stack, the heap and the GC's generations from [Chapter 3](#chapter-3-how-net-runs-your-code), the state machine, the thread pool and sync-over-async from [Chapter 4](#chapter-4-async-essentials), and value types, boxing and closures from [Chapter 1](#chapter-1-c-essentials). The chapter opens with the discipline (measure first, then benchmark and profile), because everything after it is a lever you pull only when a measurement points at it. Then come the mechanisms those measurements point at: the GC in depth, allocations and the tools that avoid them, the thread pool and the memory model under load, `ValueTask`, parallelism and channels. Last come the parts of the runtime that decide startup and deployment: reflection and source generators, the JIT, ReadyToRun and Native AOT, and assembly loading. Data-access performance (EF Core, caching) is in [Chapter 18](#chapter-18-data-in-depth).

## The Golden Rule: Measure, Don't Guess

Human intuition about performance is famously, almost comically, unreliable. The CPU your code runs on is a machine of staggering complexity — out-of-order execution, multiple cache layers, branch predictors, a JIT compiler that rewrites your IL on the fly, and a garbage collector that pauses threads at moments you cannot predict by reading source code. Reasoning about all of this in your head is like trying to predict the weather by staring at a single cloud.

> **The single most important sentence in this chapter:** Measure first, optimize second. If you optimize before measuring, you are not engineering — you are gambling with your own time as the stake.

Donald Knuth's line, "premature optimization is the root of all evil," is quoted so often it has lost its teeth. People forget the surrounding sentence, which says we *should* forgo optimizations in "the critical 3%." The point is not that optimization is bad — it is that optimizing the wrong thing is worse than doing nothing, because it costs time, adds complexity, introduces bugs, and makes the code harder to read, all while the actual bottleneck sits untouched.

Think of it like triage in an emergency room. A patient walks in and you do not immediately start treating the visible bruise on their arm. You take vitals, identify the life-threatening problem, and treat *that*. Code is the same: the slow part is almost never where you think it is. The famous "90/10 rule" holds that 90% of execution time is spent in about 10% of the code. Your job is to find that 10% before you touch anything.

> **Best practice:** Establish a performance *budget* and a *baseline* before optimizing. "This endpoint must respond in under 200ms at the 95th percentile under 500 concurrent users" is a goal you can measure against. "Make it faster" is not.

A disciplined optimization workflow looks like this:

1. **Define the goal.** Latency? Throughput? Memory footprint? Startup time? These pull in different directions.
2. **Measure the current state.** Get a baseline number with a real tool.
3. **Identify the bottleneck.** Use a profiler to find where time and allocations actually go.
4. **Form a hypothesis and change one thing.** Only one.
5. **Measure again.** Did it actually improve? By how much? Is the improvement worth the added complexity?
6. **Repeat or stop.** Stop when you hit the budget. Do not gold-plate.

The next two sections equip you for steps 2, 3 and 5: benchmarking a piece of code you suspect, and profiling a running system to find what to suspect. The rest of the chapter is the mechanics behind step 4, so that when the profiler points somewhere you know which lever to pull.

## Benchmarking with BenchmarkDotNet

For measuring the performance of a small, isolated piece of code — a method, an algorithm, a serialization routine — the gold standard in .NET is **BenchmarkDotNet**. Writing a correct micro-benchmark by hand is deceptively hard. You have to account for JIT warmup, avoid dead-code elimination, run enough iterations for statistical significance, and isolate the code from GC noise. BenchmarkDotNet does all of this for you.

Here is a complete, runnable example comparing three ways to concatenate strings in a loop:

```csharp
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using System.Text;

[MemoryDiagnoser] // Reports allocations and GC collections
public class StringConcatBenchmarks
{
    [Params(100, 1000)] // Runs every benchmark for each value
    public int N;

    private string[] _parts = null!;

    [GlobalSetup]
    public void Setup() =>
        _parts = Enumerable.Range(0, N).Select(i => i.ToString()).ToArray();

    [Benchmark(Baseline = true)]
    public string NaiveConcat()
    {
        var result = string.Empty;
        foreach (var part in _parts)
            result += part; // Allocates a new string every iteration
        return result;
    }

    [Benchmark]
    public string StringBuilderConcat()
    {
        var sb = new StringBuilder();
        foreach (var part in _parts)
            sb.Append(part);
        return sb.ToString();
    }

    [Benchmark]
    public string StringJoin() => string.Join("", _parts);
}

public class Program
{
    public static void Main() => BenchmarkRunner.Run<StringConcatBenchmarks>();
}
```

A few things to note about *how* this is written, because the mechanics matter:

- **`[MemoryDiagnoser]`** is the attribute you will use in nearly every benchmark. It adds columns for bytes allocated per operation and the number of Gen0/Gen1/Gen2 garbage collections. Allocations are frequently the hidden cost, and this makes them visible.
- **Each benchmark returns a value.** Returning the result prevents the JIT from deciding the work is unused and eliminating it entirely (dead-code elimination). If your benchmark accidentally optimizes to nothing, you will measure the speed of returning zero.
- **`[Params]`** lets you sweep across input sizes so you can see how each approach *scales*, not just its performance at one point.
- **Setup is separate.** `[GlobalSetup]` runs once and is not measured; only the `[Benchmark]` bodies are timed.

### Reading the Results

BenchmarkDotNet prints a table that looks roughly like this (numbers illustrative):

```
| Method              | N    |         Mean |   Ratio |   Allocated |
|-------------------- |----- |-------------:|--------:|------------:|
| NaiveConcat         | 100  |     3.512 us |    1.00 |    50.42 KB |
| StringBuilderConcat | 100  |     0.681 us |    0.19 |     1.66 KB |
| StringJoin          | 100  |     0.402 us |    0.11 |     0.45 KB |
| NaiveConcat         | 1000 |   198.400 us |    1.00 |  4980.10 KB |
| StringBuilderConcat | 1000 |     6.240 us |    0.03 |    16.30 KB |
| StringJoin          | 1000 |     4.100 us |    0.02 |     4.30 KB |
```

Learn to read this table like a doctor reads a chart:

- **Mean** is the average time per operation. `us` is microseconds, `ns` nanoseconds, `ms` milliseconds — check the unit, it changes per table.
- **Ratio** compares each row to the `Baseline = true` benchmark. `0.03` means StringBuilder took 3% of the naive version's time — over 30x faster.
- **Allocated** is memory per operation. Notice the naive concat at N=1000 allocates nearly 5 MB to build one string, because every `+=` creates a brand-new string containing everything so far. This is a quadratic (O(n²)) allocation pattern hiding in innocent-looking code.
- BenchmarkDotNet also reports **error** and **standard deviation** columns (omitted above). If the error is large relative to the mean, your benchmark is noisy — close background apps and re-run.

> **Pitfall:** Never run benchmarks in Debug mode or under the debugger. BenchmarkDotNet will refuse and warn you, but the broader lesson holds for all measurement: **always measure Release builds.** Debug builds disable JIT optimizations and produce meaningless numbers.

> **Pitfall:** Micro-benchmarks measure code in isolation, with warm caches and no contention. A method that wins a micro-benchmark can lose in production where the CPU cache is cold and other threads compete. Micro-benchmarks answer "which algorithm is faster," not "why is my service slow." For the latter, you profile.

## Profiling: Finding the Bottleneck in a Running System

Benchmarking measures code you already suspect. **Profiling** tells you *what* to suspect. When a real service is slow, you attach a profiler and let it show you where time and memory actually go. .NET ships a superb set of free, cross-platform command-line diagnostic tools (installed via `dotnet tool install -g`), plus heavyweight GUI options.

Here is the toolbox and, crucially, *what each tool is for*:

| Tool | What it does | Reach for it when... |
|------|-------------|---------------------|
| **dotnet-counters** | Live, near-zero-overhead metrics: CPU, allocation rate, GC pauses, thread-pool queue, exceptions/sec, ASP.NET request rate. | You want a quick "vital signs" readout of a running process. First responder. |
| **dotnet-trace** | Samples every thread's stack and records runtime events over a window; produces a trace you analyze offline. | You need to know which *methods* consume CPU without installing a GUI on the server. |
| **dotnet-dump** | Captures and analyzes a process memory dump with SOS commands (`dumpheap`, `gcroot`). | You have a hang, a deadlock, or need to inspect the managed heap and object roots. |
| **dotnet-gcdump** | Captures a lightweight snapshot of the live GC heap for memory analysis. | You suspect a **memory leak** and want to see which types are accumulating. |
| **PerfView** | Powerful, free Windows ETW-based profiler for CPU, allocations, and GC. Steep learning curve, deep insight. | You need serious allocation and GC analysis on Windows. |
| **Visual Studio Profiler** | Integrated CPU usage, allocation, and DB tools with a friendly UI. | You are already in VS and want guided, visual analysis. |
| **JetBrains dotTrace / dotMemory** | Best-in-class commercial CPU (dotTrace) and memory (dotMemory) profilers with excellent visualizations. | You want the smoothest UX for timeline/call-tree analysis and memory snapshots with retention paths. |

A typical field workflow: start with **dotnet-counters** to confirm the symptom (Is CPU pegged? Is the allocation rate enormous? Are GC pauses long?). That reading tells you which deeper tool to reach for. High CPU → **dotnet-trace** or dotTrace to find the hot method. Growing memory → **dotnet-gcdump** or dotMemory to find the accumulating type. A hang → **dotnet-dump** to inspect thread stacks.

```bash
# Watch live vital signs of a running process (PID 12345)
dotnet-counters monitor -p 12345 --counters System.Runtime,Microsoft.AspNetCore.Hosting

# Collect a 20-second trace; it writes <process>_<timestamp>.nettrace for PerfView,
# Visual Studio, or speedscope after `dotnet-trace convert --format Speedscope`
dotnet-trace collect -p 12345 --duration 00:00:20

# Snapshot the heap to hunt a leak; open in dotMemory or PerfView
dotnet-gcdump collect -p 12345
```

> **Gotcha.** The default `dotnet-trace` session samples the stacks of *all* threads about 100 times a second, waiting ones included (on dotnet-trace 10 the profile is called `dotnet-sampled-thread-time`). That is what makes it good at a hang or thread-pool starvation, where blocked threads pile up on the same frame, and what misleads a CPU investigation: a thread parked in `Monitor.Wait` collects as many samples as one spinning in a hot loop. For CPU on Linux, dotnet-trace 10 adds `dotnet-trace collect-linux --profile cpu-sampling`, which samples through the kernel's `perf_events` and so sees only threads that are running (it needs admin rights); on Windows, PerfView's CPU stacks do the same.

> **Best practice:** Profile in an environment that resembles production as closely as you can — same runtime version, Release build, representative data volumes. Profiling a 10-row dev database will never reveal the query that dies at 10 million rows.

## Garbage Collection — in depth

[Chapter 3](#chapter-3-how-net-runs-your-code) taught the model: a tracing, generational, compacting collector; gen0, gen1 and gen2; what triggers a collection; finalization and why `GC.Collect()` is almost always wrong. This section is what that model does under a server's load: where large objects go, how the two GC flavours split the work across cores, and how a full collection avoids freezing the application.

### The Large Object Heap (LOH)

Objects **85,000 bytes or larger** are allocated on a separate **Large Object Heap** rather than in Gen 0. The threshold exists because compacting large objects — physically copying, say, a 10 MB array — is expensive, so historically the LOH was **not compacted** by default; it used a free-list allocator like traditional `malloc`, which means it can *fragment*: you may have plenty of free bytes total but no single contiguous block large enough for the next big array.

Crucially, the **LOH is collected only during Gen 2 collections**. So large, frequently-allocated buffers cause expensive full collections. The classic offender is repeatedly allocating large arrays or `MemoryStream` buffers.

```csharp
// Anti-pattern: churning the LOH
for (int i = 0; i < 1000; i++)
{
    var buffer = new byte[100_000]; // >85KB → LOH, triggers Gen 2 pressure
    Process(buffer);
}

// Better: pool and reuse large buffers
var pool = System.Buffers.ArrayPool<byte>.Shared;
for (int i = 0; i < 1000; i++)
{
    byte[] buffer = pool.Rent(100_000);
    try { Process(buffer.AsSpan(0, 100_000)); }
    finally { pool.Return(buffer); }
}
```

You *can* force LOH compaction on demand (`GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;` before a `GC.Collect()`), but this is a heavy hammer for exceptional cases, not routine use.

### Workstation vs Server GC

.NET ships two GC "flavors," and choosing correctly can dramatically change throughput and latency.

- **Workstation GC** is optimized for responsiveness on client apps. It uses a single GC heap and (in non-concurrent mode) collects on the thread that triggered it. Low memory overhead, good for desktop apps and low-core environments.
- **Server GC** creates **one heap and one dedicated GC thread per logical CPU** (up to a limit). Collections run in parallel across those threads, dramatically increasing throughput for multi-core server workloads. The cost is higher memory usage (multiple heaps, each with its own Gen 0 budget) and it assumes the process can dominate the machine.

```xml
<!-- In the .csproj -->
<PropertyGroup>
  <ServerGarbageCollection>true</ServerGarbageCollection>
  <ConcurrentGarbageCollection>true</ConcurrentGarbageCollection>
</PropertyGroup>
```

> **Best practice:** ASP.NET Core defaults to Server GC and it's usually correct for throughput-oriented services. Historically, Server GC's up-front per-core heaps wasted memory in **containers with tight limits or few cores**, and the standard workaround was switching those workloads to Workstation GC. **DATAS** (Dynamic Adaptation To Application Sizes) — opt-in in .NET 8, on by default with Server GC since .NET 9 — largely fixes this: it scales the number and size of GC heaps dynamically to the actual workload instead of committing a heap per core up front. On .NET 9+, measure before reaching for Workstation GC in small containers; DATAS is the intended fix, and the Workstation-in-tiny-containers trick is now mostly historical.

### Background (Concurrent) GC

The problem with Gen 2 collections is that scanning the whole heap can take a long time, and if all application threads are paused ("stop the world") for that duration, you get latency spikes. **Background GC** solves this for Gen 2: most of the Gen 2 collection runs *concurrently* on a background thread while your application threads keep running. Application threads are only paused for brief moments (to get a consistent snapshot and finalize the collection). Gen 0 and Gen 1 collections remain blocking, but they're fast enough that this is fine. Background GC is enabled by default and is why modern .NET can achieve low pause times even with large heaps.

## Memory and Allocations: The Quiet Performance Killer

In managed .NET, you do not manually free memory; the garbage collector does. That is a productivity feature and, at the same time, the source of the most common non-obvious performance problems. This section turns the GC's mechanics into the question a profiler makes you ask: which allocations can this hot path stop making?

### Why Allocations Cost

Allocating a small object on the managed heap is itself cheap: it is basically a pointer bump. The cost comes *later*, when the GC has to reclaim it, and a collection can pause your application threads (the generations are in [Chapter 3](#chapter-3-how-net-runs-your-code); the LOH, Server GC and DATAS in the section above).

So the real cost of allocations is **GC pressure**: the more garbage you produce, the more often the GC runs, the more CPU it burns, and the more latency spikes ("pauses") your users feel. A hot path that allocates a temporary object per iteration can trigger thousands of Gen0 collections per second. The allocation was cheap; the aggregate collection cost is not.

> **Mental model:** Every allocation is a small loan from the GC that must be repaid with interest at an unpredictable time. A little borrowing is fine. Borrowing millions of times per second means the collector is constantly working — and it does that work by stealing CPU cycles and occasionally freezing your threads.

The goal in hot paths is therefore not "never allocate" but "**do not allocate needlessly and repeatedly**." One lever is the type itself: a small `struct` used inline costs no heap allocation, and boxing it costs one every time ([Chapter 1](#chapter-1-c-essentials) covers both). Here are the other tools .NET gives you.

### Span<T>, Memory<T>, and stackalloc: Slicing Without Copying

`Span<T>` is a `ref struct` that represents a **contiguous region of memory**, a slice, whether that memory is a managed array, a `stackalloc` buffer or native memory. Because it is a *view* (a reference and a length), slicing allocates nothing and copies nothing: a slice is a new window over the *same* memory.

```csharp
int[] array = { 10, 20, 30, 40, 50 };
Span<int> span = array;
Span<int> middle = span.Slice(1, 3);   // view over {20, 30, 40}, no allocation
middle[0] = 99;                        // array[1] is now 99 — same memory
```

Consider parsing a comma-separated line. The naive approach with `Split` allocates an array plus a string for every field:

```csharp
// Allocates: a string[] and one string per part
string[] parts = line.Split(',');
int id = int.Parse(parts[0]);
```

The span-based approach parses in place with zero heap allocations:

```csharp
public static int ParseFirstField(ReadOnlySpan<char> line)
{
    int comma = line.IndexOf(',');
    ReadOnlySpan<char> firstField = comma >= 0 ? line[..comma] : line;
    return int.Parse(firstField); // int.Parse has a span overload — no substring allocated
}
```

`firstField` is just a reference and a length into the original string's characters. Many BCL APIs (`int.Parse`, `Utf8Formatter`, `Encoding`, `string.Create`) accept spans precisely so you can operate on slices without allocating substrings.

**`stackalloc`** allocates a buffer directly on the stack; assigned to a `Span<T>`, it gives you a fast scratch buffer the GC never sees:

```csharp
Span<byte> buffer = stackalloc byte[128];   // stack allocation, zero heap pressure
Utf8Formatter.TryFormat(12345, buffer, out int written, default);
// use buffer[..written] with no allocation
```

Because `Span<T>` is a `ref struct` ([Chapter 1](#chapter-1-c-essentials) explains the rule), it lives only on the stack: it cannot be a field of a class, be boxed, be stored in an array, or cross an `await` or a `yield`. When you need those capabilities, for example holding a buffer across an async call, use **`Memory<T>`**, its heap-storable cousin, and take a `Span<T>` from it (`memory.Span`) at the moment you actually touch the data.

> **Pitfall.** A `Span<T>` cannot cross an `await` or a `yield`, and cannot be captured in a lambda or stored on the heap. The compiler enforces this. If you fight the compiler here, reach for `Memory<T>` instead; the rule exists to keep stack-referencing spans safe.

> **Best practice.** Reach for `Span<T>` in parsing, serialization and buffer manipulation to remove intermediate allocations. Keep `stackalloc` sizes small and bounded: the stack is limited (typically about 1 MB), and overflowing it crashes the process with no catchable exception.

### Object Pooling: Reusing Instead of Reallocating

When you genuinely need buffers or objects repeatedly, the fastest allocation is the one you never make. **Pooling** rents an object from a shared reservoir, uses it, and returns it, instead of creating and discarding.

`ArrayPool<T>` is the workhorse for temporary arrays and buffers:

```csharp
public static void ProcessLargeBuffer(Stream stream, int size)
{
    byte[] buffer = ArrayPool<byte>.Shared.Rent(size);
    try
    {
        // Note: Rent may return a LARGER array than requested.
        int read = stream.Read(buffer, 0, size);
        // ... work with buffer[0..read] ...
    }
    finally
    {
        // Always return, even on exception. clearArray: true if it held secrets.
        ArrayPool<byte>.Shared.Return(buffer);
    }
}
```

Two rules define correct pool usage. First, **`Rent` may hand you an array bigger than you asked for** — always track the logical length yourself and never assume `buffer.Length == size`. Second, **always `Return` in a `finally`**; forgetting to return does not corrupt anything but silently defeats the pool by forcing new allocations.

For pooling richer objects (parsers, builders, DTOs), use `Microsoft.Extensions.ObjectPool`:

```csharp
var pool = new DefaultObjectPoolProvider().Create<StringBuilder>(
    new StringBuilderPooledObjectPolicy());

StringBuilder sb = pool.Get();
try
{
    sb.Append("reusable");
    // ... use sb ...
}
finally
{
    pool.Return(sb); // Policy resets it for the next caller
}
```

> **Best practice:** Pool only when profiling shows the allocation is a real cost. Pooling adds complexity and the danger of use-after-return bugs (using an object you already returned). It pays off for large or extremely frequent buffers — not for the occasional small object.

### StringBuilder and String Mechanics

Strings in .NET are immutable, so every "modification" creates a new string. As the earlier benchmark showed, concatenating in a loop with `+=` is quadratic in both time and allocations. `StringBuilder` maintains a growable internal buffer and only materializes the final string once, turning that O(n²) allocation storm into a linear one.

The nuance seniors know: for a **small, fixed number of concatenations**, `StringBuilder` is *slower* due to its own setup overhead. `"Hello, " + name + "!"` compiles to a single efficient `string.Concat` call — do not "optimize" it into a StringBuilder. Reach for StringBuilder when the number of appends is large or unbounded (loops). Also prefer **string interpolation** (`$"..."`) for readability; modern C# lowers it efficiently, and interpolated string handlers even avoid intermediate allocations in APIs like logging.

### Closures and LINQ in Hot Paths

LINQ is expressive and, in the vast majority of code, its cost is negligible and readability wins. But in a genuine hot path it hides allocations: each query allocates enumerator state machines, and any lambda that **captures** a variable allocates a closure object to hold the captured state.

```csharp
// In a tight loop, each captured 'threshold' can allocate a closure,
// and the LINQ chain allocates enumerators per call.
int threshold = GetThreshold();
var count = items.Where(x => x.Value > threshold).Count();

// A plain loop in a hot path: zero allocations, no delegate calls.
int count = 0;
foreach (var x in items)
    if (x.Value > threshold) count++;
```

> **Best practice:** Write LINQ by default — it is clearer and the cost rarely matters. Rewrite to explicit loops *only* in code a profiler has flagged as hot. This is measure-first in miniature: do not preemptively strip LINQ from your whole codebase because you read it is slow. Ninety percent of your code does not care.

## The Thread Pool Under Load

[Chapter 4](#chapter-4-async-essentials) showed why a blocked pool thread hurts: the continuation that would unblock it needs a pool thread too, and the pool adds threads deliberately. Under load the *rate* is the whole story, so here is the schedule. Up to its minimum, one thread per core by default, the pool creates threads as work arrives. Above the minimum, three mechanisms add threads, each slower than the one before:

- **Blocking compensation.** Since .NET 6, a pool thread that blocks in `Task.Wait` (which `.Result` and `.GetAwaiter().GetResult()` also use) tells the pool. On .NET 10 the pool adds up to one more thread per core at once, then one at a time: 25 ms before each, 25 ms longer after every further batch of one thread per core, never more than 250 ms (`PortableThreadPool.Blocking.cs` in dotnet/runtime).
- **The starvation detector.** Every 500 ms the gate thread adds one thread if queued work has not moved for 500 ms, but only while CPU use is below 80%. Above that, it waits the thread-count goal × 1 s. This is all that `Thread.Sleep`, `SemaphoreSlim.Wait` and a contended `lock` get, because they don't report the block.
- **Hill climbing.** It moves the thread count up and down, keeps the direction that raises completed work per second, and never adds a thread while CPU use is above 95%. It is built for CPU work and rescues no one who is blocked.

```
work queued ─▶ below the minimum? ──yes──▶ new thread now
                    │ no
                    ▼
     blocked in Task.Wait? ──yes──▶ compensation: +1/core at once, then 25…250 ms per thread
                    │ no (Sleep, SemaphoreSlim.Wait, lock)
                    ▼
     starvation detector: +1 per 500 ms, only while CPU < 80%
```

> **Pay attention.** **Why `ThreadPool.SetMinThreads` looks like a fix.**
>
> - **Below the minimum there is no schedule.** The pool creates a thread for queued work at once, without any of the delays above, so a minimum above the peak number of blocked calls makes starvation vanish from the graphs.
> - **Nothing stopped blocking.** Every blocked call still holds a thread and its stack. Hill climbing can no longer go below the new minimum, and the old outage returns the day concurrency passes the number you chose, with nothing in the code to say why.
>
> Treat a raised minimum as a stopgap sized from a measurement, with a comment that names the blocking call it covers. The fix is to stop blocking (`await`, `SemaphoreSlim.WaitAsync`).

### The memory model, in one paragraph

[Chapter 4](#chapter-4-async-essentials) introduced reordering and barriers; the rules themselves are short. Without synchronization, the JIT and the CPU may reorder memory accesses, and the JIT may merge two adjacent reads of the same field into one, so a `while (!_stop)` loop can spin forever in optimized code. `Volatile.Read` has acquire semantics and `Volatile.Write` has release semantics. Taking a `lock` is an acquire and releasing it a release; every `Interlocked` method is a full fence. Assigning a reference to a fully built object is a release, so another thread that sees the reference also sees the object's fields (dotnet/runtime's [`Memory-model.md`](https://github.com/dotnet/runtime/blob/main/docs/design/specs/Memory-model.md)). And `ConcurrentDictionary` makes each *call* atomic, not your check-then-act sequence of calls.

## Task vs ValueTask

Every `async` method that actually suspends builds a state machine ([Chapter 4](#chapter-4-async-essentials) shows the compiler's output), and its result travels in a `Task`. Usually that cost is noise. `Task` is a class: each one is a heap allocation, which is waste for a method called millions of times per second that *usually completes synchronously* (a cache hit). `ValueTask<T>` is a struct wrapping *either* an available result *or* a `Task` for the slow path; the synchronous path allocates nothing.

```csharp
public ValueTask<User> GetUserAsync(int id)
{
    if (_cache.TryGetValue(id, out User? cached))
        return new ValueTask<User>(cached);          // fast path, no allocation

    return new ValueTask<User>(LoadFromDbAsync(id));  // slow path wraps a Task
}
```

A struct-based awaitable is more fragile:

> **Pitfall.**
> - **Don't await a `ValueTask` more than once.** Its backing resource may be recycled after the first consumption.
> - **Don't read `.Result` before it completes**, and don't use it concurrently.
> - **Don't store a `ValueTask` in a field or a collection.** If you must keep it, call `.AsTask()`.

**Default to `Task`.** Use `ValueTask` only in hot paths where profiling shows the allocation matters and the operation usually completes synchronously. And keep the proportions in mind: a `ValueTask` saves one small allocation per call, while one `.Result` on a hot path can starve the whole pool (next section). Most of async performance is simply not blocking.

## The TPL: Parallelism for CPU-Bound Work

[Chapter 4](#chapter-4-async-essentials)'s async is *concurrency* for I/O: juggling waits. **Parallelism** is different: running CPU-bound work on several cores at once.

### Task.Run — Offloading CPU Work

`Task.Run` schedules a delegate on the thread pool. Use it to move CPU-bound work off a thread that must stay responsive, such as a UI thread:

```csharp
// In a UI event handler: keep the UI thread free during heavy computation
int result = await Task.Run(() => ComputeExpensiveThing(data));
```

> **Pitfall.** Don't wrap async I/O in `Task.Run` on a server. `await Task.Run(() => httpClient.GetAsync(url))` is no faster: the request is already on a pool thread, and the I/O needs no thread once started. You pay a queue hop and a thread switch for nothing.

### Parallel.For / ForEach

For data-parallel CPU work, `Parallel` partitions a loop across cores:

```csharp
Parallel.ForEach(images, image =>
{
    image.Thumbnail = GenerateThumbnail(image); // CPU-bound, independent
});
```

`Parallel.ForEachAsync` (.NET 6) runs *asynchronous* work over a collection with a built-in concurrency limit — often cleaner than the `SemaphoreSlim` pattern:

```csharp
await Parallel.ForEachAsync(
    urls,
    new ParallelOptions { MaxDegreeOfParallelism = 8 },
    async (url, ct) => await ProcessAsync(url, ct));
```

### When Parallelism Helps vs Hurts

- **It helps** when the work is CPU-bound, the items are independent, and each does enough work to outweigh the coordination.
- **It hurts** when the work is I/O-bound (use async instead), the items are tiny (scheduling dominates), or they share mutable state (contention serializes them anyway).

> **Best practice.** Parallelism for CPU, async for I/O. `Task.Run` around I/O, or `Parallel.ForEach` around network calls, is a hallmark of not-yet-senior code.

## System.Threading.Channels: Producer/Consumer Pipelines

When producers generate work and consumers process it, and you want them decoupled with back-pressure, use `System.Threading.Channels`. A `Channel<T>` is an async-aware, thread-safe queue:

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

The key is the **bounded** channel with `FullMode = Wait`: when the buffer fills, `WriteAsync` suspends the producer until there is space. That is *back-pressure* — the system regulates itself instead of queueing unbounded work in memory. `Complete()` lets `ReadAllAsync` finish when the work runs out.

## A Brief Note on Rx.NET

**Reactive Extensions (Rx.NET)** offers `IObservable<T>`, a *push-based* stream of events you subscribe to, with LINQ-style operators for composing events over time (`Throttle`, `Buffer`, `Merge`, `CombineLatest`); debouncing a search box is a one-liner. For request/response and ordinary async I/O, stick with `Task`. Rx earns its place only when you model *streams of events over time*.

## Attributes and Reflection

The last group of mechanisms decides how fast a process starts and what a deployment is allowed to contain. They hinge on one question: does the code discover types at runtime, or does the compiler know them at build time? That starts with metadata.

**Attributes** attach declarative metadata to code — classes, methods, properties, parameters. They do nothing by themselves; they're inert data compiled into the assembly, waiting to be read by **reflection** or by tooling.

```csharp
[AttributeUsage(AttributeTargets.Property)]
public sealed class DisplayNameAttribute : Attribute
{
    public string Name { get; }
    public DisplayNameAttribute(string name) => Name = name;
}

public class Product
{
    [DisplayName("Product Title")]
    public string Title { get; set; } = "";
}
```

**Reflection** is the API for inspecting types and metadata at runtime and even invoking members dynamically.

```csharp
var prop = typeof(Product).GetProperty(nameof(Product.Title))!;
var attr = prop.GetCustomAttribute<DisplayNameAttribute>();
Console.WriteLine(attr?.Name);   // "Product Title"

// Dynamic invocation
var product = Activator.CreateInstance<Product>();
prop.SetValue(product, "Widget");
Console.WriteLine(prop.GetValue(product));   // "Widget"
```

Reflection powers serializers, DI containers, ORMs, and validators. But it's **slow** compared to direct calls (it bypasses JIT optimizations and does runtime lookups) and it defeats trimming/AOT analysis because the linker can't see reflective usage statically.

> **Best practice:** Use reflection for configuration-time work (wiring up a container once at startup), not in hot paths. When you must reflect repeatedly, cache `PropertyInfo`/`MethodInfo` objects or compile delegates from expression trees. Better yet, consider source generators.

## Source Generators (Conceptual)

A **source generator** is a compiler plugin that runs *during compilation*, inspects your code (via the Roslyn syntax and semantic model), and *emits additional C# source* that gets compiled alongside your own. It's the modern answer to problems previously solved with runtime reflection or reflection-emit.

The mental model: instead of discovering metadata and building behavior at *runtime* (slow, AOT-hostile), a source generator does that discovery at *compile time* and writes plain code you could have written by hand. The result is faster, trimming-friendly, and debuggable.

```csharp
// You write a partial declaration with an attribute:
[JsonSerializable(typeof(Person))]
public partial class MyJsonContext : JsonSerializerContext { }

// System.Text.Json's source generator emits the other 'partial' half at compile time:
// fully typed, reflection-free serialization code for Person.
```

You don't usually write generators; you consume them. `System.Text.Json`, `LoggerMessage`, regex (`[GeneratedRegex]`), and many libraries ship generators that replace reflection with generated code. Knowing they exist explains *why* modern .NET can serialize JSON or match regex with zero runtime reflection and full Native AOT compatibility.

## From IL to Machine Code: the CLR and JIT

When you compile C#, the Roslyn compiler does *not* produce machine code. It produces **Intermediate Language (IL)** — a stack-based, CPU-agnostic bytecode — plus metadata, packaged into an assembly (a `.dll` or `.exe`). The actual translation to machine code happens at runtime, performed by the **CLR** (Common Language Runtime); the modern cross-platform implementation is called **CoreCLR**.

### JIT compilation

The **Just-In-Time (JIT) compiler** translates IL to native machine code **method by method, on first call**. When your program calls a method for the first time, the JIT compiles it, patches the call site to point at the compiled code, and future calls jump straight to native code. This "compile on demand" approach means you never pay to compile code paths you don't execute, and the JIT can optimize for the *actual* CPU it's running on (using AVX2 if present, for example).

The downside is **startup cost**: the first execution of each method includes compilation time. For a short-lived CLI tool or a serverless function with cold starts, this matters. .NET has several features to mitigate it.

### Tiered Compilation

Modern .NET uses **Tiered Compilation** to get the best of both worlds — fast startup *and* high steady-state throughput.

- **Tier 0** (the "quick JIT"): when a method is first called, the JIT compiles it quickly with minimal optimizations. Code is produced fast, so startup is snappy, but the code itself isn't as fast.
- **Tier 1** (the "optimizing JIT"): the runtime counts how often each method is called. Once a method crosses a **call-count threshold** (i.e., it's proven "hot"), the JIT recompiles it in the background with full optimizations, and swaps the new version in.

This means rarely-called methods stay cheaply-compiled (Tier 0) and never waste time on optimization, while hot loops get fully optimized. There's also **On-Stack Replacement (OSR)**, which handles the tricky case of a method with a long-running loop that's still executing when it becomes hot — OSR can swap the optimized code in *while the loop is running*, without waiting for the method to be re-entered.

```xml
<!-- Tiered compilation is ON by default. You can tune or disable it: -->
<PropertyGroup>
  <TieredCompilation>true</TieredCompilation>
  <!-- Tier-0 + Quick JIT for loops can hurt microbenchmarks;
       TieredPGO enables Profile-Guided Optimization -->
  <TieredPGO>true</TieredPGO>
</PropertyGroup>
```

**Dynamic PGO (Profile-Guided Optimization)**, on by default since .NET 8, takes this further: Tier 0 code is *instrumented* to record runtime behavior (which types actually flow through a virtual call, which branches are taken), and Tier 1 uses that profile to make smarter decisions — like **devirtualizing** a call it observed is almost always the same type, or reordering branches. This is optimization guided by how your program *actually* runs, which a static compiler can't match.

### ReadyToRun (R2R)

**ReadyToRun** is a form of ahead-of-time compilation that embeds *precompiled native code* alongside the IL in the assembly. At runtime, the CLR can use the native code directly instead of JIT-compiling from scratch, drastically improving startup. The trade-off is larger assemblies (they contain both IL and native code) and the native code is less optimized than Tier 1 (it's compiled without knowing the exact CPU or runtime profile). R2R code still gets *re-JITted* to Tier 1 if a method becomes hot — so you get fast startup from R2R *and* peak throughput from tiered recompilation. ASP.NET Core apps are often published with `<PublishReadyToRun>true</PublishReadyToRun>` for faster cold starts.

### Native AOT

**Native AOT (Ahead-Of-Time)** goes all the way: it compiles your entire application to a **single, self-contained native executable at build time**, with *no JIT and no IL at runtime*. The CLR's JIT is gone entirely; what ships is native machine code plus a minimal runtime (still including the GC).

Benefits:
- **Instant startup** — no JIT warm-up at all. Ideal for serverless, CLI tools, and microservices.
- **Small, self-contained deployment** — no framework install needed.
- **Lower memory footprint** and predictable performance.

Costs and constraints:
- **No runtime code generation.** Anything relying on `System.Reflection.Emit`, runtime IL generation, or loading assemblies dynamically won't work.
- **Limited reflection.** Because AOT trims aggressively and can't see code paths reached only via reflection, features like reflection-based serialization need special handling (source generators; [Chapter 5](#chapter-5-http-and-web-apis) shows `System.Text.Json`'s).
- **Whole-program compilation** with **trimming** is mandatory, which can break code that reflects over types the trimmer removed.

```xml
<PropertyGroup>
  <PublishAot>true</PublishAot>
</PropertyGroup>
<!-- dotnet publish -r linux-x64 -c Release -->
```

> **Best practice.** Native AOT shines where cold-start latency and footprint dominate: serverless functions, CLI tools, containers that scale from zero. It is usually *not* worth its constraints for a large monolith that starts once and runs for weeks. Match the tool to the metric that matters.

### Trimming

**Trimming** (also called linking) removes IL your app doesn't use from the published output, shrinking deployment size. The trimmer performs static analysis to find reachable code and discards the rest. The danger is **reflection**: if you look up a type or method by name at runtime, the trimmer can't see that dependency statically and may remove it, causing a `MissingMethodException` in production. This is why trimming, Native AOT, and reflection-heavy libraries are in tension — and why the ecosystem has moved toward **source generators**, which produce trimming-safe code at build time. Libraries annotate their trim-safety with attributes like `[RequiresUnreferencedCode]` and `[DynamicallyAccessedMembers]` so the trimmer can warn you.

## Assemblies, Loading, and Strong Naming

An **assembly** is the unit of deployment and versioning in .NET — a `.dll` or `.exe` containing IL, metadata (a manifest describing the types, version, and dependencies), and optionally resources. Assemblies are the boundary at which type identity is established: a type's full identity is its namespace-qualified name *plus* the assembly it lives in.

### AssemblyLoadContext

In modern .NET, assemblies are loaded into an **`AssemblyLoadContext` (ALC)**. Think of an ALC as an isolated container for a set of loaded assemblies. The **default ALC** loads your application and its dependencies. But you can create *additional* load contexts, which is the foundation for **plugin systems** and **hot-reload / hot-swap** scenarios: you can load a plugin (and its private dependency versions) into its own ALC, use it, and then **unload** the entire context to reclaim it — something impossible with the old fixed AppDomain-based model in .NET Core (AppDomains don't exist in .NET 5+).

```csharp
public sealed class PluginLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver _resolver;

    public PluginLoadContext(string pluginPath)
        : base(isCollectible: true)      // collectible → can be unloaded
        => _resolver = new AssemblyDependencyResolver(pluginPath);

    protected override Assembly? Load(AssemblyName name)
    {
        string? path = _resolver.ResolveAssemblyToPath(name);
        return path is null ? null : LoadFromAssemblyPath(path);
    }
}
```

Two assemblies with the *same name* loaded into *two different ALCs* are considered **different types** by the runtime — a subtle source of `InvalidCastException`s ("cannot cast Foo to Foo") in plugin systems. Type identity includes the load context.

### Strong naming

A **strong name** is a cryptographic identity for an assembly: the assembly is signed with a private key, and its identity becomes name + version + culture + **public key token**. Historically this was required for the Global Assembly Cache (GAC) and to prevent name collisions. In modern cross-platform .NET the GAC is gone and strong naming is far less important — it's mainly relevant for library authors who need a stable public identity or must be referenced by other strong-named assemblies. It is *not* a security feature (the public key is embedded and verifiable, but it doesn't prevent tampering the way Authenticode does). Don't rely on strong naming for security; use it for identity stability if you publish widely-consumed libraries.

## Common .NET Performance Anti-Patterns

A consolidated field guide to the recurring offenders, most of which this chapter has met:

- **Optimizing without measuring.** The meta-anti-pattern. You cannot fix what you have not measured.
- **String concatenation with `+=` in loops.** Quadratic allocations. Use `StringBuilder` or `string.Join`.
- **The N+1 query.** One query becomes hundreds via lazy navigation access. Use `Include` or projection ([Chapter 7](#chapter-7-data-access); EF Core performance in depth is in [Chapter 18](#chapter-18-data-in-depth)).
- **Sync-over-async (`.Result`/`.Wait()`).** Blocks threads, causes thread-pool starvation under load.
- **`List.Contains` inside a loop.** O(n²), fine on 100 dev rows and fatal on 100,000. Build a `HashSet` or `Dictionary` once, before the loop; [Chapter 2](#chapter-2-data-structures-and-algorithms-essentials) has the collections and their costs.
- **Fetching whole entities and unbounded result sets.** Project to DTOs; always paginate.
- **Catching exceptions for control flow.** Throwing is expensive (stack capture). Do not use `try/catch` where a `TryParse` or a null check works. Exceptions are for the exceptional.
- **Hidden boxing.** Value types silently heap-allocated by `object`/interface conversions and non-generic collections.
- **LINQ and closures in genuinely hot loops.** Fine everywhere else; a real cost in the flagged 10%.
- **Excessive logging in hot paths.** String formatting and I/O per request adds up; use structured logging with level checks and interpolated string handlers.
- **Not disposing / leaking IDisposables.** Undisposed `HttpClient` per request exhausts sockets; unclosed DB connections exhaust the pool. Use `IHttpClientFactory` and `using`.

## Putting It All Together

Performance engineering is not a bag of tricks to sprinkle everywhere — it is a discipline of *evidence*. The senior engineer's edge is not knowing more optimizations than the mid-level one; it is the restraint to not apply them until measurement demands it, and the tooling fluency to measure quickly and correctly when it does.

Internalize the loop: **define a goal, measure a baseline, profile to find the real bottleneck, change one thing, measure again, stop when you hit the budget.** Keep the mechanical knowledge (allocations create GC pressure, a large buffer per request is gen2 work per request, a blocked thread is a delay for everyone, the right collection changes the cost curve's shape, EF can turn one line into a thousand queries) in your back pocket so that when the profiler points at the hot 10%, you know exactly which lever to pull.

Above all, remember the golden rule from the start of this chapter, because it is the one you will be tempted to break every single time: **measure first. Don't guess.** The code you were *sure* was slow almost never is. Let the evidence, not your intuition, decide where you spend your effort.

## Practice

**1. Watch the pool inject threads (30 min).** Re-run [Chapter 4](#chapter-4-async-essentials)'s [`Starvation`](https://github.com/malyna2/dotnet-handbook/tree/main/verify/path/Starvation) experiment in blocking mode, with the counters open:

```bash
dotnet tool install -g dotnet-counters
cd verify/path
dotnet build -c Release Starvation       # build first: the run itself lasts about 12 s
dotnet run -c Release --project Starvation -- block
# in a second terminal, as soon as the run starts:
dotnet-counters monitor -n Starvation --counters 'EventCounters\System.Runtime'
```

Read `ThreadPool Thread Count` at every refresh and write down the increase. It jumps first, then climbs in ever smaller steps; compare the curve with the blocking-compensation schedule in [The Thread Pool Under Load](#the-thread-pool-under-load). `ThreadPool Queue Length` stays high until the threads catch up, and `CPU Usage (%)` barely moves. (Without the `EventCounters\` prefix, .NET 10 shows `dotnet.thread_pool.thread.count` as a change per second, not a count; [Chapter 9's worked methodology](#diagnosing-a-performance-problem-a-worked-methodology) explains why.) The [reference run](https://github.com/malyna2/dotnet-handbook/tree/main/verify/path/reference-runs) on 4 vCPU reached 68 pool threads and finished in 11.6 s; the `-- await` run peaked at 3 threads and finished in 1.1 s.

If the program ends before the tool attaches, start it under the tool instead. The documentation warns against `dotnet run` in this mode, because the first .NET process to connect is the one monitored:

```bash
dotnet-counters monitor --counters 'EventCounters\System.Runtime' -- dotnet exec Starvation/bin/Release/net10.0/Starvation.dll block
```

**2. Find the blocked threads in a dump (20 min).** During another blocking run:

```bash
dotnet tool install -g dotnet-dump
dotnet-dump collect -n Starvation
dotnet-dump analyze <the dump file it names>
# then, at the analyzer's prompt:
#   threadpool         the pool's threads and its state
#   threadpoolqueue    the work items waiting for a thread
#   parallelstacks     every thread's stack, merged
```

Most pool threads share one merged stack that ends in `Task.Wait`: that is sync-over-async as a dump shows it, and `threadpoolqueue` lists the work they are starving.

**3. Trace it (10 min).** `dotnet-trace collect -n Starvation` during a third run; stop it after a few seconds. Its default sampling records every thread's stack, waiting or running, so the blocked threads appear even though the CPU is idle. Open the `.nettrace` in PerfView or Visual Studio, or convert it with `dotnet-trace convert --format Speedscope`.

**4. Chapter 4's exercises (20 min).** In [Chapter 4](#chapter-4-async-essentials)'s *Exercises*, answer *Find the bug* by naming the pool mechanism behind each defect, then compare with the run verified in [`verify/exercises/Ch08`](https://github.com/malyna2/dotnet-handbook/tree/main/verify/exercises/Ch08): 20 requests took 6 s against 0.8 s for the fix, and the pool grew to 49 threads against 5. *What would you do* is the `ConfigureAwait` mechanism, argued in a review.

**Evidence to keep**, in your own public portfolio repo, not in this one: the counters as CSV (`dotnet-counters collect --counters 'EventCounters\System.Runtime' --format csv`), the `parallelstacks` excerpt, and one paragraph that explains your thread-count curve with the injection schedule.

Later, if you need it: [Chapter 35's Scenario 7](#scenario-7-the-slow-leak-memory-keeps-growing-until-the-pod-is-oom-killed) (finding a leak with a heap snapshot), [Benchmarking with BenchmarkDotNet](#benchmarking-with-benchmarkdotnet), and the runtime's own [memory model specification](https://github.com/dotnet/runtime/blob/main/docs/design/specs/Memory-model.md).

## Three questions

**1.** Two services block a pool thread per request: one with `.Result`, the other with `SemaphoreSlim.Wait()`. Under the same burst, why does the second recover more slowly, and far more slowly when the CPU is busy? And why does raising `ThreadPool.SetMinThreads` make both graphs look healthy?

<details>
<summary>Answer</summary>

- **`.Result` is reported; `SemaphoreSlim.Wait()` isn't.** `.Result` blocks in `Task.Wait`, which tells the pool. The pool compensates: one more thread per core at once, then one at a time, with pauses that grow from 25 ms to 250 ms. In the experiment, it took the pool to 68 threads in 11.6 s on 4 cores.
- **The unreported block gets only the starvation detector.** That is at most one thread per 500 ms while CPU use is below 80%. Above 80%, the wait becomes the thread-count goal × 1 s, so a busy service gets a new thread once a minute or less.
- **Below the minimum there is no schedule.** The pool creates threads as work arrives, so a minimum above the peak number of blocked calls hides both problems. Nothing stopped blocking: each call still holds a thread, hill climbing can't trim below the minimum, and the outage returns at a higher load.

Fix: stop blocking (`await`, `SemaphoreSlim.WaitAsync`). Keep a raised minimum only as a measured stopgap, with a comment that names what it covers.
</details>

**2.** A service builds a 100 KB buffer per request before uploading a file. Every few seconds p99 latency spikes, and the counters show gen2 collections climbing along with the LOH. Why does a buffer that lives for one request cause full collections, and what do you change, in what order?

<details>
<summary>Answer</summary>

- **85,000 bytes is the line.** An array that large is allocated on the Large Object Heap, which is collected only together with gen2. Every buffer dies young, but freeing it takes a full collection, so frequent large allocations mean frequent full collections. Background GC runs most of gen2 alongside the application, but its pauses and the CPU it takes land on the requests in flight.
- **Change the code first.** Stream the upload so there is no buffer, or rent one from `ArrayPool<byte>.Shared` and return it in a `finally` (it may hand you a larger array, so track the length yourself). Confirm with `dotnet.gc.heap.total_allocated`, gen2 collections per minute and `dotnet.gc.pause.time`, before and after.
- **Then the configuration, if the counters still say so.** ASP.NET Core already defaults to Server GC; on .NET 9 and later, DATAS adjusts its heaps. Never `GC.Collect()`: it resets the GC's tuning and pauses on purpose.
</details>

**3.** A worker loop checks a `bool _stop` field that another thread sets. It works in Debug and in every test; in Release under load, one instance never stops. What does the memory model allow here, and what is the smallest correct fix?

<details>
<summary>Answer</summary>

- **The read can be merged away.** The .NET memory model lets the JIT coalesce adjacent non-volatile reads of the same location. When the loop body gives it no reason to read again (no lock, no `Interlocked`, no call it can't see through), optimized code may read `_stop` once and loop on its own copy. Debug code isn't optimized that way, which is why it "works".
- **Smallest fix:** `Volatile.Read(ref _stop)` in the loop, or a `volatile` field: an acquire read that can't be coalesced. Pair it with `Volatile.Write` on the writer's side.
- **Better fix:** a `CancellationToken`. It already gives the visibility guarantee, and it composes with every async API the loop calls.
</details>

## Decide

An ASP.NET Core service on 8 cores calls a vendor SDK that has only a synchronous API: each call blocks for 200 ms to 2 s on a socket. At peak about 150 calls are in flight, and every endpoint slows down, including those that never touch the vendor. Three options are on the table:

1. Raise `ThreadPool.SetMinThreads` to cover the peak.
2. Put the SDK behind a bulkhead: a bounded `Channel<T>` drained by a fixed set of dedicated threads (`TaskCreationOptions.LongRunning`). Requests await their result, and get a fast `503` when the channel is full.
3. Replace the SDK with the vendor's HTTP API, called through `HttpClient` with `await`.

<details>
<summary>How a senior engineer weighs it</summary>

**What each costs.**

- *Raise the minimum:* one line and a deploy. Up to the new minimum, the pool stops injecting slowly. Every call still blocks a pool thread, hill climbing can't trim below the minimum, and when the peak passes the number, the old outage returns with nothing in the code to explain it.
- *Bulkhead:* about a day. The blocking is confined to threads that never run anyone else's continuations, so the rest of the service no longer feels the vendor's latency, and overload becomes a fast, visible rejection instead of pool-wide starvation. It costs queueing delay for vendor calls at peak, and a capacity number (threads × calls per second) that someone must own.
- *Rewrite:* weeks, plus re-implementing what the SDK did for you (authentication, retries, the wire format). It removes the blocking entirely.

**What decides it here:** the other endpoints share the pool. The damage is collateral, not the vendor's latency itself, and isolation removes collateral damage whatever the vendor does.

**The choice.** The bulkhead now, with a raised minimum as a same-day stopgap, sized from the measured peak and removed when the bulkhead ships. The rewrite goes on the roadmap if the vendor's HTTP API is documented and stable.

**What would change it.** If one low-traffic endpoint is the only caller, the raised minimum alone is a fair trade. If the vendor ships an async SDK, adopt it and skip the bulkhead. If a vendor call can't wait in a queue (a payment authorisation with a customer watching), size the dedicated threads for the peak and reject early.
</details>

## Check at work

**Inspect.** Search the service for `Thread.Sleep`, `.Wait()`, `.Result`, `.GetAwaiter().GetResult()`, `SemaphoreSlim.Wait(` and `lock` blocks around I/O; for `new byte[` and `MemoryStream` on request paths; for `GC.Collect` and `SetMinThreads`. A good result: every block sits in startup code, large buffers are pooled or streamed, and a raised minimum carries a comment naming the blocking call it covers. A bad one: a block on a request or message path, or a minimum nobody can explain.

**Measure.** At your service's peak, run `dotnet-counters monitor -n <process> --counters 'EventCounters\System.Runtime'` for five minutes, or read the same metrics in your APM: thread-pool queue length (near zero), thread count (flat, not climbing), time paused by GC per minute, gen 2 collections per minute, and the LOH size. In an APM on .NET 9+ these are `dotnet.thread_pool.queue.length`, `dotnet.thread_pool.thread.count`, `dotnet.gc.pause.time`, `dotnet.gc.collections` for `gen2` and `dotnet.gc.last_collection.heap.size` for `loh`; check that the first two are plotted as values, not rates.

## Interview Questions

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
