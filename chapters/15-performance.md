# Chapter 15: Performance & Optimization

_⏱️ Estimated read time: ~35 min ·     5380 words (study pace)_

Performance engineering is the discipline where good intentions go to die. Every experienced developer has, at some point, spent an afternoon lovingly hand-optimizing a loop that ran once at startup, only to discover the real bottleneck was a database query fired sixty times per request. This chapter is about not being that developer. It is about building the instincts, the tooling literacy, and the mechanical knowledge of the .NET runtime that separate a mid-level engineer who *thinks* their code is fast from a senior engineer who *knows*.

We will move from philosophy (how to think about performance) to measurement (how to prove what is slow) to mechanics (allocations, async, EF Core, collections) and finally to system-level levers (caching, AOT, load testing). By the end you should be able to walk into an unfamiliar slow service and methodically find and fix the real problem.

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

The rest of this chapter equips you for steps 2 through 5.

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
| **dotnet-trace** | Captures CPU sampling and runtime events over a window; produces a trace you analyze offline. | You need to know which *methods* consume CPU without installing a GUI on the server. |
| **dotnet-dump** | Captures and analyzes a process memory dump with SOS commands (`dumpheap`, `gcroot`). | You have a hang, a deadlock, or need to inspect the managed heap and object roots. |
| **dotnet-gcdump** | Captures a lightweight snapshot of the live GC heap for memory analysis. | You suspect a **memory leak** and want to see which types are accumulating. |
| **PerfView** | Powerful, free Windows ETW-based profiler for CPU, allocations, and GC. Steep learning curve, deep insight. | You need serious allocation and GC analysis on Windows. |
| **Visual Studio Profiler** | Integrated CPU usage, allocation, and DB tools with a friendly UI. | You are already in VS and want guided, visual analysis. |
| **JetBrains dotTrace / dotMemory** | Best-in-class commercial CPU (dotTrace) and memory (dotMemory) profilers with excellent visualizations. | You want the smoothest UX for timeline/call-tree analysis and memory snapshots with retention paths. |

A typical field workflow: start with **dotnet-counters** to confirm the symptom (Is CPU pegged? Is the allocation rate enormous? Are GC pauses long?). That reading tells you which deeper tool to reach for. High CPU → **dotnet-trace** or dotTrace to find the hot method. Growing memory → **dotnet-gcdump** or dotMemory to find the accumulating type. A hang → **dotnet-dump** to inspect thread stacks.

```bash
# Watch live vital signs of a running process (PID 12345)
dotnet-counters monitor -p 12345 --counters System.Runtime,Microsoft.AspNetCore.Hosting

# Collect a 20-second CPU trace, then open trace.nettrace in a viewer
dotnet-trace collect -p 12345 --duration 00:00:20

# Snapshot the heap to hunt a leak; open in dotMemory or PerfView
dotnet-gcdump collect -p 12345
```

> **Best practice:** Profile in an environment that resembles production as closely as you can — same runtime version, Release build, representative data volumes. Profiling a 10-row dev database will never reveal the query that dies at 10 million rows.

## Memory and Allocations: The Quiet Performance Killer

In managed .NET, you do not manually free memory — the garbage collector does. This is a wonderful productivity feature and, simultaneously, the source of the most common non-obvious performance problems. Understanding *why* allocations cost is essential senior knowledge.

### Why Allocations Cost

Allocating a small object on the managed heap is itself cheap — it is basically a pointer bump. The cost comes *later*, when the GC has to reclaim it. The .NET GC is generational: new objects live in **Gen0**, and survivors get promoted to **Gen1** then **Gen2**. Gen0 collections are frequent and fast; Gen2 collections are rare and expensive because they may scan the entire heap. Critically, a garbage collection can pause your application threads. (For the generational model in depth, plus Server-vs-Workstation GC and the newer DATAS mode, see Chapter 2 rather than re-deriving it here.)

So the real cost of allocations is **GC pressure**: the more garbage you produce, the more often the GC runs, the more CPU it burns, and the more latency spikes ("pauses") your users feel. A hot path that allocates a temporary object per iteration can trigger thousands of Gen0 collections per second. The allocation was cheap; the aggregate collection cost is not.

> **Mental model:** Every allocation is a small loan from the GC that must be repaid with interest at an unpredictable time. A little borrowing is fine. Borrowing millions of times per second means the collector is constantly working — and it does that work by stealing CPU cycles and occasionally freezing your threads.

The goal in hot paths is therefore not "never allocate" but "**do not allocate needlessly and repeatedly**." Here are the tools .NET gives you.

### Span<T> and Memory<T>: Zero-Copy Slicing

`Span<T>` is a stack-allocated view over a contiguous region of memory — a managed array, a piece of native memory, or a `stackalloc` buffer. Its superpower is **slicing without copying and without allocating**. When you `Slice` a span, you get a new window over the *same* memory; no bytes move.

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

`firstField` is just a pointer and a length into the original string's characters. Nothing is copied. Many BCL APIs — `int.Parse`, `Utf8Formatter`, `Encoding`, `string.Create` — now accept spans precisely so you can operate on slices without allocating substrings.

`Memory<T>` is the heap-storable cousin of `Span<T>`. Because a `Span<T>` lives on the stack (it is a `ref struct`), it cannot be a field of a class, stored in an array, or used across an `await`. When you need those capabilities — for example, holding a buffer across an async call — you use `Memory<T>` and get a `Span<T>` from it (`memory.Span`) at the moment you actually touch the data.

> **Pitfall:** A `Span<T>` cannot cross an `await` or a `yield`, and cannot be captured in a lambda or stored on the heap. The compiler enforces this. If you fight the compiler here, reach for `Memory<T>` instead — do not try to defeat the rule; it exists to keep stack-referencing spans safe.

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

### struct vs class: Where Your Data Lives

A `class` is a reference type: instances live on the heap and are tracked by the GC. A `struct` is a value type: it lives inline — on the stack if it is a local, or embedded directly inside its containing array or object. This is a fundamental performance lever, because a `struct` used well **produces no separate heap allocation and no GC pressure**.

Use a `struct` when the type is small, immutable, and represents a single value (a coordinate, a money amount, a small result). A key win: an array of a million small structs is *one* contiguous allocation with excellent cache locality, whereas an array of a million class instances is one array of references plus a million separate heap objects scattered across memory — murder for the CPU cache.

> **Pitfall — boxing:** The moment you assign a struct to an `object`, an `interface`, or store it somewhere expecting a reference, the runtime **boxes** it — allocating a heap copy and defeating the entire purpose. `object o = myStruct;` allocates. Passing a struct to a method taking `IComparable` boxes it. Watch for hidden boxing in LINQ and non-generic collections.

> **Pitfall:** Large structs are copied by value on every assignment and every method call. A 200-byte struct passed to ten methods copies 200 bytes ten times. Keep structs small (roughly 16 bytes or under as a rule of thumb), or pass them by `in`/`ref` to avoid copies. Beyond that size, a class is usually the better choice.

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

## Async Performance

Chapter 8 covered async correctness. Here we cover its cost. Every `async` method that actually suspends builds a state machine and, if it awaits an incomplete operation, allocates a `Task`. Usually this is fine. In extremely hot async paths — think a method called millions of times that *often completes synchronously* (a cache hit, a buffered read) — that per-call `Task` allocation adds up.

**`ValueTask<T>`** exists for exactly this case. It can wrap a synchronously-available result *without allocating a Task*, only falling back to a real Task when the operation truly runs asynchronously.

```csharp
public ValueTask<User> GetUserAsync(int id)
{
    // Cache hit: returns synchronously, zero Task allocation.
    if (_cache.TryGetValue(id, out var user))
        return new ValueTask<User>(user);

    // Cache miss: falls back to a real async operation.
    return new ValueTask<User>(LoadUserAsync(id));
}
```

> **Pitfall:** `ValueTask` has strict usage rules — you may await it **only once**, and you must not access its result before it completes or block on it repeatedly. If you need to await a result multiple times or store it, call `.AsTask()` first. Because of these constraints, use `ValueTask` as a targeted optimization for hot, often-synchronous paths — not as a blanket replacement for `Task`.

The most damaging async performance anti-pattern is **sync-over-async**: calling `.Result` or `.Wait()` on a Task to get a synchronous answer. This blocks a thread-pool thread while the async work runs, and under load it can cause **thread-pool starvation** — a death spiral where all pool threads are blocked waiting, so no thread is free to complete the very work they wait on, and throughput collapses.

> **Best practice:** Async all the way down. Never `.Result`/`.Wait()` on hot paths. If a library forces you to bridge sync and async, isolate it and understand you are paying a real, non-linear cost under load.

Also prefer `ConfigureAwait(false)` in library code to avoid capturing and marshaling back to a synchronization context, and remember that async work still uses pooled `Task` machinery — the runtime already pools much of it, so most of your job is simply not blocking.

## EF Core Performance

Entity Framework Core is where mid-level services most often bleed performance, because a single innocent line of C# can generate a catastrophic SQL pattern. These techniques matter more than almost anything else in a typical business app.

**AsNoTracking for read-only queries.** By default EF Core tracks every entity it returns so it can detect changes for `SaveChanges`. For queries where you only read and never update, that tracking is pure overhead — memory for the change tracker plus CPU to snapshot each entity. `AsNoTracking()` skips it.

```csharp
// Read-only: no change tracking, less memory, faster materialization.
var products = await db.Products
    .AsNoTracking()
    .Where(p => p.IsActive)
    .ToListAsync();
```

**Project to DTOs — select only what you need.** Fetching entire entities when you need three columns wastes bandwidth, memory, and materialization time. Projecting with `Select` into a DTO tells EF to `SELECT` only those columns.

```csharp
var summaries = await db.Orders
    .Where(o => o.CustomerId == id)
    .Select(o => new OrderSummary(o.Id, o.Total, o.CreatedAt)) // SELECT Id, Total, CreatedAt only
    .ToListAsync();
```

**Avoid the N+1 query.** This is the single most common EF performance disaster. You fetch a list, then lazily access a navigation property inside a loop, firing one query *per item*.

```csharp
// N+1: 1 query for orders, then 1 query PER order for its customer. Deadly at scale.
var orders = await db.Orders.ToListAsync();
foreach (var o in orders)
    Console.WriteLine(o.Customer.Name); // triggers a query each iteration

// Fixed: one query with a JOIN via eager loading.
var orders = await db.Orders.Include(o => o.Customer).ToListAsync();
```

**Paginate — never fetch unbounded result sets.** `ToListAsync()` on a table that grows to millions of rows will eventually take down your service. Always bound queries with `Skip`/`Take` (or keyset pagination for large offsets).

```csharp
var page = await db.Products
    .OrderBy(p => p.Id)
    .Skip((pageNumber - 1) * pageSize)
    .Take(pageSize)
    .ToListAsync();
```

**Batch your writes.** EF Core batches multiple inserts/updates into fewer round-trips on `SaveChanges`, so add many entities and save once rather than saving in a loop. For bulk operations, `ExecuteUpdateAsync`/`ExecuteDeleteAsync` (EF Core 7+) issue a single SQL `UPDATE`/`DELETE` without loading entities into memory at all.

**Compiled queries** eliminate the per-call cost of translating a LINQ expression tree into SQL. For a query executed on a very hot path, `EF.CompileAsyncQuery` caches the translation.

```csharp
private static readonly Func<AppDb, int, Task<User?>> _getUser =
    EF.CompileAsyncQuery((AppDb db, int id) =>
        db.Users.FirstOrDefault(u => u.Id == id));

public Task<User?> GetUserAsync(int id) => _getUser(_db, id);
```

> **Best practice:** Log and inspect the actual SQL EF generates (enable `LogTo` or use a profiler). Most EF performance problems are invisible in C# and obvious the moment you see the SQL. The N+1 that reads fine in code screams at you in the query log.

## Caching as a Performance Lever

The fastest work is the work you skip entirely. Caching stores the result of an expensive operation so subsequent requests return it cheaply. It is often the highest-leverage optimization available — turning a 200ms database aggregation into a sub-millisecond dictionary lookup.

In-process `IMemoryCache` is fastest but per-instance and lost on restart; distributed caches like Redis (`IDistributedCache`) are shared across instances and survive restarts at the cost of a network hop and serialization. The senior judgment is knowing *when* to cache: data that is **read far more than written** and **tolerates some staleness**. Reference data, computed aggregates, and rendered fragments are ideal. Rapidly changing, per-user-critical, or must-be-consistent data is not.

> **Pitfall:** Caching introduces the two hardest problems in computing — invalidation and staleness. Always set an expiration, size limits to bound memory, and a clear story for how stale data gets refreshed. An unbounded cache is a memory leak with good intentions.

## Native AOT and Trimming: Startup and Size

Most of this chapter targets steady-state throughput. But two metrics — **cold-start latency** and **deployment size** — matter enormously for serverless functions, CLI tools, and containers that scale from zero. Here the levers are **trimming** and **Native AOT**.

**Trimming** (`PublishTrimmed`) analyzes your app and removes unused code from the assemblies it ships, shrinking the deployment. **Native AOT** (`PublishAot`) goes further: it compiles your app ahead of time to a single native executable with no JIT and a minimal runtime. The payoff is dramatic — near-instant startup (no JIT warmup), much smaller memory footprint, and no runtime dependency to install.

The cost is real constraints. AOT and aggressive trimming are hostile to **runtime reflection** and **runtime code generation**, because the compiler must statically determine every type and method that could be used. Libraries that lean on reflection-heavy serialization or dynamic proxies may break or require source-generator-based alternatives (for example, `System.Text.Json`'s source-generated serialization instead of its reflection mode).

> **Best practice:** Native AOT shines for small, self-contained services where startup and footprint dominate — serverless, CLI tools, microservices that scale to zero. It is usually *not* worth the constraints for a large monolith that starts once and runs for weeks. Match the tool to the metric that matters.

## Big-O Awareness and Choosing the Right Collection

No amount of micro-optimization saves an algorithm that scales badly. A senior engineer's most powerful performance skill is often just picking the right data structure, because that choice changes the *shape* of the cost curve, not merely its constant factor. An O(n²) algorithm that is beautifully hand-tuned still loses catastrophically to an O(n) one as data grows.

The most common real-world mistake is repeatedly searching a `List<T>`. `list.Contains(x)` is O(n) — it scans from the front. Do it inside a loop over another list and you have an O(n²) time bomb that runs fine on 100 dev records and melts on 100,000 production ones.

Here is the practical decision guide for the core collections:

| Collection | Lookup | Add | Ordered? | Reach for it when... |
|-----------|--------|-----|----------|---------------------|
| **`T[]` (array)** | O(n) scan / O(1) by index | fixed size | insertion order | Size is known and fixed; you want minimal overhead and cache locality. |
| **`List<T>`** | O(n) `Contains` / O(1) index | O(1) amortized append | insertion order | A growable, indexed sequence you mostly iterate or append to. |
| **`Dictionary<K,V>`** | O(1) average by key | O(1) average | no | You look things up **by key**. The default answer for "find X by its id." |
| **`HashSet<T>`** | O(1) average `Contains` | O(1) average | no | You need fast membership tests / de-duplication, no values. |
| **`SortedDictionary`/`SortedSet`** | O(log n) | O(log n) | sorted by key | You need lookups **and** sorted iteration or range queries. |

Understanding the *internals* explains the table. A `Dictionary<K,V>` is a hash table: it computes `GetHashCode()` on the key to jump near-directly to a bucket, giving average O(1) — but a pathologically bad hash (or a mutable key whose hash changes after insertion) degrades it toward O(n). A `List<T>` is a backing array that **doubles** when full; that doubling is why appends are *amortized* O(1) rather than truly O(1), and why setting an initial `capacity` when you know the size avoids repeated reallocation and copying.

> **Best practice:** When you find yourself calling `.Contains()`, `.Any()`, or `.FirstOrDefault(match)` on a `List<T>` inside a loop, stop. You almost certainly want a `Dictionary` or `HashSet`. Converting the inner list to a `HashSet` once, before the loop, can turn an O(n²) operation into O(n) — often the biggest single win available in real code.

> **Best practice:** Pre-size collections when you know roughly how many elements they will hold: `new List<T>(expectedCount)`, `new Dictionary<K,V>(expectedCount)`. This skips the sequence of internal reallocations as the collection grows.

> **Modern note (.NET 8):** For a lookup table built **once and then read many times** — reference data loaded at startup — `FrozenDictionary<TKey,TValue>` and `FrozenSet<T>` (in `System.Collections.Frozen`) trade slower construction for measurably faster reads than `Dictionary`/`HashSet`; a `FrozenDictionary` row would slot naturally into the table above. For scanning a string or buffer for any of a fixed set of values, `SearchValues<T>` gives a vectorized, hardware-accelerated `IndexOfAny` that far outpaces a naive multi-value search.

## Load Testing: Proving It Under Pressure

Benchmarks and profilers examine one operation or one process. **Load testing** answers the system-level question: how does the whole service behave under many concurrent users? It reveals behaviors invisible in single-request testing — thread-pool starvation, connection-pool exhaustion, lock contention, and the difference between average and tail (p99) latency.

Three tools dominate:

- **k6** — a modern, developer-friendly load tester where you script scenarios in JavaScript. Excellent for CI integration and clear metrics. Great default for HTTP APIs.
- **NBomber** — a .NET-native load testing framework where you write scenarios in **C#**. The natural choice when you want your load tests in the same language and solution as your service, testing not just HTTP but any protocol you can call from C#.
- **JMeter** — the venerable, feature-rich Java-based tool with a GUI. Powerful and battle-tested, if heavier and less code-friendly than the other two.

A minimal k6 script conveys the shape:

```javascript
import http from 'k6/http';
import { check } from 'k6';

export const options = {
  vus: 200,          // 200 virtual users concurrently
  duration: '2m',    // for two minutes
  thresholds: {
    http_req_duration: ['p(95)<200'], // 95% of requests must finish under 200ms
  },
};

export default function () {
  const res = http.get('https://localhost:5001/api/products');
  check(res, { 'status is 200': (r) => r.status === 200 });
}
```

The key discipline is reading **percentiles, not averages**. An average latency of 50ms can hide a p99 of 3 seconds — meaning one request in a hundred is agonizingly slow, which at scale is thousands of unhappy users. Averages lie; percentiles tell the truth about tail behavior, and tail behavior is what users actually feel.

> **Best practice:** Load test against production-like infrastructure and data volumes, and define pass/fail thresholds (like the k6 `thresholds` above) so the test objectively fails when performance regresses. Wire it into CI to catch regressions before they ship.

## Common .NET Performance Anti-Patterns

A consolidated field guide to the recurring offenders, most of which we have met above:

- **Optimizing without measuring.** The meta-anti-pattern. You cannot fix what you have not measured.
- **String concatenation with `+=` in loops.** Quadratic allocations. Use `StringBuilder` or `string.Join`.
- **The N+1 query.** One query becomes hundreds via lazy navigation access. Use `Include` or projection.
- **Sync-over-async (`.Result`/`.Wait()`).** Blocks threads, causes thread-pool starvation under load.
- **`List.Contains` inside a loop.** O(n²). Use a `HashSet` or `Dictionary`.
- **Fetching whole entities and unbounded result sets.** Project to DTOs; always paginate.
- **Catching exceptions for control flow.** Throwing is expensive (stack capture). Do not use `try/catch` where a `TryParse` or a null check works. Exceptions are for the exceptional.
- **Hidden boxing.** Value types silently heap-allocated by `object`/interface conversions and non-generic collections.
- **LINQ and closures in genuinely hot loops.** Fine everywhere else; a real cost in the flagged 10%.
- **Excessive logging in hot paths.** String formatting and I/O per request adds up; use structured logging with level checks and interpolated string handlers.
- **Not disposing / leaking IDisposables.** Undisposed `HttpClient` per request exhausts sockets; unclosed DB connections exhaust the pool. Use `IHttpClientFactory` and `using`.

> **Capstone tie-in:** This chapter is exercised by ShopCore Step 5 (Caching, Auth, and Observability) — you'd add Redis as a distributed cache for the hot product-catalog read path, with sensible invalidation on writes. See Chapter 32.

## Putting It All Together

Performance engineering is not a bag of tricks to sprinkle everywhere — it is a discipline of *evidence*. The senior engineer's edge is not knowing more optimizations than the mid-level one; it is the restraint to not apply them until measurement demands it, and the tooling fluency to measure quickly and correctly when it does.

Internalize the loop: **define a goal, measure a baseline, profile to find the real bottleneck, change one thing, measure again, stop when you hit the budget.** Keep the mechanical knowledge — allocations create GC pressure, the right collection changes the cost curve's shape, async that blocks starves the pool, EF can turn one line into a thousand queries — in your back pocket so that when the profiler points at the hot 10%, you know exactly which lever to pull.

Above all, remember the golden rule that opened this chapter, because it is the one you will be tempted to break every single time: **measure first. Don't guess.** The code you were *sure* was slow almost never is. Let the evidence, not your intuition, decide where you spend your effort.
