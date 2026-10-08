# Part 2 · Module 1: Runtime and Concurrency Internals

> **What this module makes you able to do.** Predict how a .NET service behaves when its threads block or its heap churns, prove it from counters and a dump, and choose between tuning the runtime and changing the code, with the cost of each spelled out for the team that will live with the choice.

**Time:** reading ≈ 45 min; hands-on ≈ 2 h 25 min — the counters, the dump and the trace 1 h, Chapter 8's exercises 20, the questions 20, the decision 15, the check at work 30.

## Covers

- what the async method builder does with a result, an exception and a captured context, and what `ConfigureAwait` changes;
- how fast the thread pool adds threads for `.Result`, for `Thread.Sleep` and for CPU work, and why `SetMinThreads` is a stopgap, not a fix;
- the rules behind `ValueTask`, back-pressure with a bounded `Channel<T>`, `lock` and `System.Threading.Lock`, `Interlocked`, and the memory model;
- why a 100 KB buffer per request turns into gen2 collections, and what Server GC and DATAS change;
- `Span<T>` and pooling as "no allocation, no collection";
- reading a live process: `dotnet-counters` first, then `dotnet-dump` or `dotnet-trace`.

## The mechanism to explain without notes

**Every request shares two runtime resources, a small pool of threads and a generational heap, and both are tuned for short work: a thread held while waiting, or an object kept a little too long, becomes a delay for everyone else.**

Part 1's async essentials module showed the state machine. This module is what the runtime does around it.

- **The builder owns the outcome.** The compiler moves the method body into `MoveNext`, inside one `try/catch`, and the builder turns the outcome into the returned `Task` with `SetResult` or `SetException`. `await` later calls `GetResult()`, which rethrows the first stored exception through `ExceptionDispatchInfo`. An `async void` builder has no task to fill, so it rethrows on the captured `SynchronizationContext` or, when there is none, on a pool thread, where nothing catches it. The context is captured where the continuation is registered; `ConfigureAwait(false)` tells that one `await` not to post back to it. ASP.NET Core installs no context, so there it changes nothing.
- **The pool grows on a schedule, not on demand.** Up to its minimum, one thread per core by default, it creates threads as work arrives. Above the minimum, three mechanisms add threads, each slower than the one before:
  - *Blocking compensation.* Since .NET 6, a pool thread that blocks in `Task.Wait` (which `.Result` and `.GetAwaiter().GetResult()` also use) tells the pool. On .NET 10 the pool adds up to one more thread per core at once, then one at a time: 25 ms before each, 25 ms longer after every further batch of one thread per core, never more than 250 ms (`PortableThreadPool.Blocking.cs`).
  - *The starvation detector.* Every 500 ms the gate thread adds one thread if queued work has not moved for 500 ms, but only while CPU use is below 80%. Above that, it waits the thread-count goal × 1 s. This is all that `Thread.Sleep`, `SemaphoreSlim.Wait` and a contended `lock` get.
  - *Hill climbing.* It moves the thread count up and down, keeps the direction that raises completed work per second, and never adds a thread while CPU use is above 95%. It is built for CPU work and rescues no one.
- **The heap is cheap to allocate from and expensive to keep.** An allocation is a pointer bump in gen0; the cost comes at collection. Survivors are promoted, and a gen2 collection works over the whole heap. Arrays of 85,000 bytes or more go to the Large Object Heap, which is collected only with gen2, so a large buffer per request is gen2 work per request. Server GC, ASP.NET Core's default, gives each core its own heap and GC thread; since .NET 9, DATAS sizes those heaps to the workload instead of committing one per core up front.

The traps in *Covers* all follow. A blocked thread can't run the continuation that would unblock it. An allocation that `Span<T>`, `stackalloc` or `ArrayPool<T>` avoids is collection work that never happens. A bounded `Channel<T>` turns "too much work" into a producer that waits, instead of a heap that grows.

**The memory model, in one paragraph.** Without synchronization, the JIT and the CPU may reorder memory accesses, and the JIT may merge two adjacent reads of the same field into one, so a `while (!_stop)` loop can spin forever. `Volatile.Read` has acquire semantics and `Volatile.Write` has release semantics. Taking a `lock` is an acquire and releasing it a release; every `Interlocked` method is a full fence. Assigning a reference to a fully built object is a release, so another thread that sees the reference also sees the object's fields (dotnet/runtime's `Memory-model.md`). On .NET 9 with C# 13, `lock` on a `System.Threading.Lock` uses that type's faster API. `ConcurrentDictionary` makes each call atomic, not your check-then-act sequence of calls.

> **Pay attention.** **Why `ThreadPool.SetMinThreads` looks like a fix.**
>
> - **Below the minimum there is no schedule.** The pool creates a thread for queued work at once, without any of the delays above, so a minimum above the peak number of blocked calls makes starvation vanish from the graphs.
> - **Nothing stopped blocking.** Every blocked call still holds a thread and its stack. Hill climbing can no longer go below the new minimum, and the old outage returns the day concurrency passes the number you chose, with nothing in the code to say why.
>
> Treat a raised minimum as a stopgap sized from a measurement, with a comment that names the blocking call it covers. The fix is to stop blocking.

## Read (≈ 45 min)

1. [Chapter 8: SynchronizationContext and ConfigureAwait](#synchronizationcontext-and-configureawait): where the continuation runs, and why `ConfigureAwait(false)` fixes nothing in ASP.NET Core.
2. [Chapter 8: Task vs ValueTask](#task-vs-valuetask): three rules, all consequences of a backing object that may be reused.
3. [Chapter 8: The TPL: Parallelism for CPU-Bound Work](#the-tpl-parallelism-for-cpu-bound-work) and [System.Threading.Channels: Producer/Consumer Pipelines](#systemthreadingchannels-producerconsumer-pipelines): `Parallel.ForEachAsync`'s limit and a bounded channel are the same idea, a cap on work in flight.
4. [Chapter 8: Thread Safety: Sharing State Correctly](#thread-safety-sharing-state-correctly): `Lock`, `Interlocked`, `GetOrAdd`'s factory, barriers.
5. [Chapter 2: Garbage Collection](#garbage-collection): generations and the card table, the LOH, Server GC and DATAS, finalization.
6. [Chapter 2: From IL to Machine Code: the CLR and JIT](#from-il-to-machine-code-the-clr-and-jit): tiered compilation and Dynamic PGO, the reason a fresh instance is slower in its first minute.
7. [Chapter 1: Span<T>, Memory<T>, and stackalloc](#spant-memoryt-and-stackalloc), then Chapter 15's [Why Allocations Cost](#why-allocations-cost) and [Object Pooling](#object-pooling-reusing-instead-of-reallocating).
8. [Chapter 15: Profiling: Finding the Bottleneck in a Running System](#profiling-finding-the-bottleneck-in-a-running-system) and [Async Performance](#async-performance).
9. [Chapter 33: Scenario 3 — Stop-the-world](#scenario-3-stop-the-world-garbage-collector-pauses-are-causing-latency-spikes): the incident, end to end, with the GC counters to read on .NET 9 and later and their .NET 8 names.

## Practice

**1. Watch the pool inject threads (30 min).** Re-run Part 1's [`Starvation`](https://github.com/malyna2/dotnet-handbook/tree/main/verify/path/Starvation) experiment in blocking mode, with the counters open:

```bash
dotnet tool install -g dotnet-counters
cd verify/path
dotnet build -c Release Starvation       # build first: the run itself lasts about 12 s
dotnet run -c Release --project Starvation -- block
# in a second terminal, as soon as the run starts:
dotnet-counters monitor -n Starvation --counters 'EventCounters\System.Runtime'
```

Read `ThreadPool Thread Count` at every refresh and write down the increase. It jumps first, then climbs in ever smaller steps; compare the curve with the blocking-compensation schedule above. `ThreadPool Queue Length` stays high until the threads catch up, and `CPU Usage (%)` barely moves. (Without the `EventCounters\` prefix, .NET 10 shows `dotnet.thread_pool.thread.count` as a change per second, not a count; [Chapter 34](#diagnosing-a-performance-problem-a-worked-methodology) explains why.) The [reference run](https://github.com/malyna2/dotnet-handbook/tree/main/verify/path/reference-runs) on 4 vCPU reached 68 pool threads and finished in 11.6 s; the `-- await` run peaked at 3 threads and finished in 1.1 s.

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

**4. Chapter 8's exercises (20 min).** In [Chapter 8](#chapter-8-asynchronous-concurrent-programming)'s *Exercises*, answer *Find the bug* by naming the pool mechanism behind each defect, then compare with the run verified in [`verify/exercises/Ch08`](https://github.com/malyna2/dotnet-handbook/tree/main/verify/exercises/Ch08): 20 requests took 6 s against 0.8 s for the fix, and the pool grew to 49 threads against 5. *What would you do* is this module's `ConfigureAwait` mechanism, argued in a review.

**Evidence to keep**, in your own public portfolio repo, not in this one: the counters as CSV (`dotnet-counters collect --counters 'EventCounters\System.Runtime' --format csv`), the `parallelstacks` excerpt, and one paragraph that explains your thread-count curve with the schedule above.

Later, if you need it: [Chapter 33's Scenario 7](#scenario-7-the-slow-leak-memory-keeps-growing-until-the-pod-is-oom-killed) (finding a leak with a heap snapshot), [Benchmarking with BenchmarkDotNet](#benchmarking-with-benchmarkdotnet), and the runtime's own [memory model specification](https://github.com/dotnet/runtime/blob/main/docs/design/specs/Memory-model.md).

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
