# Chapter 3: How .NET Runs Your Code

This chapter explains the machinery around your code that every .NET service shares: where the runtime puts what you allocate and how the garbage collector gets it back, how settings reach your classes, how the dependency-injection container builds and disposes your objects, and how the Generic Host starts and stops the application. With it you can explain why memory climbs and comes back, why a misspelt setting fails silently, and why a `Scoped` service inside a singleton serves stale data to every request.

The sections go from the bottom up. **Memory and garbage collection** come first, because allocation cost, disposal and object lifetimes all rest on them. **Configuration** and **dependency injection** are the two systems that wire an application together; the options pattern joins them, and service lifetimes decide which objects live how long. The **Generic Host** owns both, plus the lifecycle of background work. The chapter ends with the **release cadence**, which decides which runtime version you should be on.

[Chapter 1](#chapter-1-c-essentials) covers value and reference types and boxing at the language level, and this chapter builds on it. The depth (the Large Object Heap, Server and Workstation GC, background GC, the JIT and Native AOT) is in [Chapter 17: Runtime Internals and Performance](#chapter-17-runtime-internals-and-performance); logging is in [Chapter 9](#chapter-9-exceptions-logging-and-first-diagnosis), and JSON serialization in [Chapter 5](#serialization-systemtextjson-vs-newtonsoftjson).

## The Memory Model: Stack vs Managed Heap

The .NET runtime gives every managed program two fundamentally different regions of memory to work with: the **stack** and the **managed heap**. Understanding which values live where is the single most useful mental model for reasoning about allocation, garbage collection, and performance.

The **stack** is a thread-local, last-in-first-out region. Each thread gets its own stack (by default 1 MB on Windows). When you call a method, the runtime pushes a *stack frame* containing the method's parameters, its local variables, and bookkeeping like the return address. When the method returns, the frame is popped — instantly, with zero cleanup cost. There is no garbage collector involved in stack memory; deallocation is just moving a pointer back down.

The **managed heap** is a process-wide region shared by all threads, and it's where reference-type objects live. When you write `new Customer()`, the runtime carves space out of the heap and hands you back a *reference* (essentially a pointer) to it. That reference might sit on the stack (as a local variable) or inside another heap object (as a field), but the `Customer` instance itself is always on the heap.

Which of the two holds a given piece of data depends on where the variable is declared, not only on its type: a local `int` is in the stack frame, an `int` field of a class is inside the object on the heap, and a reference variable on the stack points to an object on the heap. [Chapter 1](#stack-vs-heap-where-the-bytes-actually-live) gives the rules.

```csharp
public void Example()
{
    int localValue = 42;              // the int 42 lives on the stack
    Point p = new Point(1, 2);        // Point is a struct: its two ints live on the stack
    Customer c = new Customer();      // 'c' (a reference) is on the stack;
                                      // the Customer object is on the heap
    int[] numbers = new int[100];     // 'numbers' reference on stack;
                                      // the 100 ints live in a heap array
}

public struct Point { public int X, Y; public Point(int x, int y) => (X, Y) = (x, y); }
public class Customer { public int Id; public string Name = ""; }
```

When `Example` returns, the stack frame — `localValue`, `p`, the references `c` and `numbers` — vanishes for free. But the `Customer` and the `int[]` remain on the heap until the garbage collector proves nobody can reach them anymore. That "proving unreachability and reclaiming" is the job of the GC.

> **Why this matters:** every heap allocation has a cost — not just the allocation itself, but the eventual GC work to reclaim it. High-performance .NET code minimizes heap allocations by favoring `structs` for small, short-lived data, using `Span<T>` for slicing without copying, and avoiding hidden allocations (boxing, closures, LINQ in hot paths); [Chapter 17](#chapter-17-runtime-internals-and-performance) shows how to measure them.

**Boxing** is where the two meet: a value type assigned to `object` or an interface is copied into a new heap object, an allocation the code doesn't show ([Chapter 1](#boxing-and-unboxing-the-hidden-tax)).

## Garbage Collection

The .NET garbage collector is a **tracing, generational, compacting** collector. Let's unpack each of those words, because each represents a deliberate design decision that shapes how your programs behave.

**Tracing** means the GC determines liveness by starting from a set of *roots* — static fields, local variables and CPU registers on every thread's stack, and GC handles — and following references transitively. Any object reachable from a root is *live*; everything else is *garbage*. The GC never needs the program to tell it what to free; it discovers it.

**Compacting** means that after identifying garbage, the GC slides the surviving objects together to close the gaps, eliminating fragmentation. This is why heap allocation in .NET is astonishingly fast: because the heap is kept compact, allocating is usually just bumping a pointer (the "allocation pointer") forward — no free-list search like `malloc`. The trade-off is that surviving objects get *moved*, which is why the runtime must pause threads at safe points and fix up references during a collection.

### Generations 0, 1, and 2

The **generational hypothesis** is the observation that most objects die young. A JSON response deserialized to handle a web request, the intermediate strings in a formatting operation, the temporary list inside a method — these live for microseconds. A few objects (caches, singletons, long-lived buffers) live for the whole process. Very few live "medium" lengths.

The GC exploits this by dividing the heap into three **generations**:

- **Gen 0** — the nursery. All new small objects are allocated here. Gen 0 is small (tuned to fit in CPU cache), so collecting it is fast.
- **Gen 1** — a buffer between short-lived and long-lived. Objects that survive a Gen 0 collection are *promoted* to Gen 1.
- **Gen 2** — long-lived objects. Survivors of Gen 1 are promoted here. Objects of 85,000 bytes or more skip the nursery and go to the Large Object Heap, which is collected with Gen 2 ([Chapter 17](#chapter-17-runtime-internals-and-performance) explains why that matters).

```
   new small objects
        |
        v
   +--------+               +--------+               +-----------------+
   | Gen 0  | --survivors-> | Gen 1  | --survivors-> |      Gen 2      |
   +--------+   promoted    +--------+   promoted    +-----------------+
    most objects                                      collected only by
    die here (cheap,                                  full GCs (expensive)
    frequent GCs)                                    +-----------------+
   new objects >= 85,000 bytes --------------------> |       LOH       |
                                                     +-----------------+
                                                      collected with Gen 2
```

The magic is that **collecting a lower generation doesn't require scanning higher ones for most purposes**. A Gen 0 collection only examines Gen 0 objects (plus a clever mechanism, described below, to find references *from* older objects *into* Gen 0). Because Gen 0 is small and most of it is dead, Gen 0 collections are extremely cheap and frequent. Gen 2 collections (also called *full* collections) are expensive because they scan the entire heap — these are the ones you want to keep rare.

> **The write barrier and card tables.** How can a Gen 0 collection be correct without scanning Gen 2? An old object might hold a reference to a young one (e.g., you add a freshly allocated item to a long-lived cache). The runtime handles this with a **write barrier**: every time you store a reference into an object's field, a tiny piece of JIT-emitted code marks a "card" (a small region of memory) as dirty in a **card table**. During a Gen 0 collection, the GC scans only the dirty cards of older generations to find cross-generational references. This is why reference assignments are marginally more expensive than value assignments — there's an invisible barrier running.

### How the GC decides to collect

A collection is triggered when one of these happens:

1. **Gen 0 fills up.** The allocation budget for Gen 0 is exhausted — the most common trigger. The runtime dynamically tunes this budget based on allocation and survival rates.
2. **Memory pressure from the OS.** The system signals it's low on physical memory.
3. **Explicit `GC.Collect()`.** You asked for it (usually a mistake — see below).

The GC also *dynamically self-tunes*. If it notices that objects promoted to Gen 2 keep surviving, it adjusts budgets to collect Gen 2 less often. It's an adaptive system, not a fixed schedule.

### Finalization and the relationship to IDisposable

The GC reclaims *managed* memory automatically. But some objects wrap *unmanaged* resources — file handles, socket descriptors, database connections, native memory — that the GC knows nothing about. Two mechanisms address this: **finalizers** and **`IDisposable`**.

A **finalizer** (`~ClassName()`) is a method the GC calls before reclaiming an object, giving it a chance to release unmanaged resources. But finalization is a costly, non-deterministic safety net:

1. When the GC finds an unreachable object *with a finalizer*, it can't reclaim it immediately. Instead it places the object on the **finalization queue**, which keeps it alive.
2. A dedicated **finalizer thread** later runs the finalizer.
3. Only on the *next* GC can the object actually be collected.

So a finalizable object survives at least one extra generation and requires two collection cycles to die. Overusing finalizers is a real performance problem.

**`IDisposable`** provides *deterministic* cleanup: you (or a `using` block) call `Dispose()` at a precise, known point rather than waiting for the GC. This is the preferred mechanism, and [Chapter 1](#idisposable-iasyncdisposable-and-the-dispose-pattern) covers using it.

The canonical pattern combines both — `Dispose()` for deterministic cleanup, a finalizer as a backstop if the caller forgets, and `GC.SuppressFinalize` to skip the expensive finalizer when `Dispose` already did the work:

```csharp
public sealed class NativeBufferOwner : IDisposable
{
    private IntPtr _handle;              // unmanaged resource
    private bool _disposed;

    public NativeBufferOwner(int size) => _handle = Marshal.AllocHGlobal(size);

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);       // no need to run the finalizer now
    }

    private void Dispose(bool disposing)
    {
        if (_disposed) return;
        if (disposing)
        {
            // release *managed* IDisposable fields here
        }
        if (_handle != IntPtr.Zero)      // release *unmanaged* resources always
        {
            Marshal.FreeHGlobal(_handle);
            _handle = IntPtr.Zero;
        }
        _disposed = true;
    }

    ~NativeBufferOwner() => Dispose(false); // backstop if Dispose was never called
}
```

> **Best practice:** If your type only holds *managed* `IDisposable` fields (the common case — an `HttpClient`, a `DbConnection`), implement `IDisposable` but **do not** write a finalizer. Let `SafeHandle`-based types handle the unmanaged layer. Only write a finalizer when you directly own a raw unmanaged resource like an `IntPtr`. Prefer `SafeHandle` even then, as it's more robust against edge cases.

### GC.Collect and why not to call it

`GC.Collect()` forces a collection. It is almost always the wrong thing to do in production code because:

- The GC's self-tuning heuristics are better than your intuition. A manual full collection resets its carefully-learned budgets and often makes things *worse*.
- It forces promotion: objects that would have died in Gen 0 get scanned and possibly promoted to Gen 1/2 if they happen to be alive at that instant, making them longer-lived.
- It introduces a synchronous pause exactly when you didn't need one.

Legitimate uses are rare: benchmarking (to establish a clean baseline), a one-time cleanup after loading a huge dataset that you know created long-lived garbage, or immediately before taking a memory snapshot for diagnostics. If you find yourself reaching for `GC.Collect()` to "fix" a memory problem, the real fix is almost always to allocate less or to dispose properly.

That is the GC a middle developer needs: generations, what triggers a collection, and why finalizers are a backstop. Which GC flavour a server runs, how the Large Object Heap fragments and how to read GC counters under load are in [Chapter 17](#chapter-17-runtime-internals-and-performance).

## The Configuration System

Every service needs settings that change between environments without a rebuild: connection strings, endpoints, feature switches. `IConfiguration` is a set of **key-value pairs** assembled from several **providers**; a later provider overrides an earlier one. `WebApplication.CreateBuilder` layers them in this order:

1. `appsettings.json` (base settings)
2. `appsettings.{Environment}.json` (e.g., `appsettings.Production.json`)
3. **User secrets** (development only — keeps secrets out of source control)
4. **Environment variables**
5. **Command-line arguments** (highest priority)

Because each layer overrides the previous, an environment variable can override a JSON setting in production without changing any file, and a command-line flag can override everything for a one-off run. Keys are hierarchical, with `:` as the separator (`Logging:LogLevel:Default`); environment variables use `__` (double underscore) since `:` isn't portable across shells.

```json
// appsettings.json
{
  "ConnectionStrings": { "Default": "Server=.;Database=App;" },
  "Email": { "SmtpHost": "smtp.local", "Port": 25, "UseSsl": false }
}
```

```csharp
var builder = WebApplication.CreateBuilder(args);
// CreateBuilder already wires up JSON, env vars, user secrets, and CLI args.

string? conn = builder.Configuration.GetConnectionString("Default");
int port = builder.Configuration.GetValue<int>("Email:Port");
```

### The Options pattern and binding

The **Options pattern** binds a configuration section to a typed class, so the rest of the code never reads string keys, and the values can be validated.

```csharp
public sealed class EmailOptions
{
    public const string SectionName = "Email";
    public string SmtpHost { get; set; } = "";
    public int Port { get; set; }
    public bool UseSsl { get; set; }
}

// Registration:
builder.Services
    .AddOptions<EmailOptions>()
    .Bind(builder.Configuration.GetSection(EmailOptions.SectionName))
    .ValidateDataAnnotations()          // enforce [Required], [Range], etc.
    .ValidateOnStart();                 // fail fast at startup, not first use
```

You then inject one of three options interfaces; their lifetimes decide which one sees a changed value (`CreateBuilder` reloads `appsettings*.json` on change by default):

- **`IOptions<T>`** — a singleton, computed on first use and never again: an edited file never reaches it. Safe to inject anywhere.
- **`IOptionsSnapshot<T>`** — a **scoped** service, computed once per request, so it sees reloaded values; supports named options. Cannot be injected into a singleton (see captive dependencies below).
- **`IOptionsMonitor<T>`** — a singleton that recomputes when the configuration reloads, exposes `CurrentValue`, and raises `OnChange`. Use it when a singleton needs live config.

> **Pay attention.** **Binding never fails on a missing or misspelt key; it leaves the default.** The binder copies the keys it finds onto matching properties and ignores the rest, so `"SmtpHots"` in JSON, or a forgotten environment variable, yields `SmtpHost = ""` and no error. Validation is what turns that into a failure, and it runs when the options are first computed: on the first request that needs them, possibly hours after a deployment that looked healthy. `ValidateOnStart()` moves it into host start-up (`Host.StartAsync` runs it before any hosted service starts), so a bad setting fails the deployment instead of a customer's request. `BinderOptions.ErrorOnUnknownConfiguration = true` additionally rejects keys that match no property.

```csharp
public class Mailer(IOptions<EmailOptions> options)
{
    private readonly EmailOptions _cfg = options.Value;
}
```

## Dependency Injection

The options pattern above already relied on it: `AddOptions<T>()` registers a service, and `Mailer` received `IOptions<EmailOptions>` without creating it. A class *declares* its dependencies (usually as constructor parameters) and the built-in container (`Microsoft.Extensions.DependencyInjection`) supplies them. You register services on an `IServiceCollection`; the container builds an `IServiceProvider` that *resolves* them recursively: to build `OrderService` it builds the `IRepository` it needs, then the `DbContext` the repository needs, and so on down the graph.

### Service lifetimes

- **Transient** — a **new instance every time** it's requested. If `A` and `B` both depend on a transient `C`, they each get their own `C`.
- **Scoped** — **one instance per scope**. ASP.NET Core creates a scope per HTTP request, so a scoped service is shared within a request and distinct across requests. `DbContext` is the archetype: one unit of work per request.
- **Singleton** — **one instance for the application's lifetime**, shared by every request at once, so it must be thread-safe. For stateless services, caches and expensive-to-create objects.

```csharp
builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddScoped<IOrderRepository, SqlOrderRepository>();
builder.Services.AddTransient<IEmailValidator, EmailValidator>();
```

### Captive dependencies — the classic DI bug

Because the container resolves dependencies recursively, a longer-lived service that captures a shorter-lived one **freezes** the shorter-lived one for its own lifetime. This is a **captive dependency**, and it's a frequent production bug.

Consider a **singleton that depends on a scoped `DbContext`**. The singleton is created once, so it resolves the `DbContext` once, and then *holds that same `DbContext` forever* — across all requests, all threads. `DbContext` is not thread-safe, so two concurrent requests get EF Core's *"A second operation was started on this context instance"*, and tracking queries keep handing back entities loaded long ago, with their old values.

> **The rule:** a service may only depend on services with an **equal or longer** lifetime. Singleton → Singleton is fine. Scoped → Singleton is fine. Singleton → Scoped is a bug. Transient captured by a Singleton effectively becomes a Singleton.

> **Pay attention.** **The captured instance belongs to no request, so nothing ever disposes it.** The container builds a singleton in the *root* scope and resolves the singleton's dependencies there too; a scoped service resolved in the root scope is, in the runtime's own words, "promoted to singleton". It is a separate instance from every request's own, lives until the app stops, and is shared by all of them, which is why the symptom is concurrency and stale data, not `ObjectDisposedException`. That exception comes from the opposite capture: work that outlives its request, such as a `Task.Run` closure still using the request's `DbContext` after the response has gone and the scope has disposed it. One fix covers both: whoever outlives the request creates and owns its own scope.

The built-in container can catch the first capture. **`ValidateScopes`** makes it throw when a singleton consumes a scoped service (*"Cannot consume scoped service 'X' from singleton 'Y'"*) or when a scoped service is resolved from the root provider. **`ValidateOnBuild`** checks every registration when the provider is built, so `builder.Build()` fails at start-up instead of on the first resolve. `WebApplicationBuilder` turns both on **only in the Development environment**; in Production nothing checks, and the bug ships.

```csharp
// If a singleton genuinely needs a scoped service, inject the FACTORY,
// not the service, and create a scope explicitly for each unit of work:
public class BackgroundProcessor(IServiceScopeFactory scopeFactory)
{
    public async Task DoWorkAsync()
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        // ... use db within this scope, then it's disposed
    }
}
```

The container also **disposes** what it creates: an `IDisposable` service is disposed when its scope ends (per request for scoped, at shutdown for singletons). A service you `new` up yourself gets none of that.

## The Generic Host and Background Services

The **Generic Host** (`IHost`) is the composition root of a modern .NET application. It bundles together DI, configuration, logging, and lifetime management into one object that you build, run, and gracefully shut down. `WebApplication` (ASP.NET Core) and the console `Host` both build on it.

The host manages a set of **`IHostedService`** instances — components with a `StartAsync`/`StopAsync` lifecycle tied to the application's. When the host starts, it starts all hosted services; when it receives a shutdown signal (Ctrl+C, SIGTERM from Kubernetes), it stops them gracefully, giving in-flight work a chance to finish.

For long-running background work, you inherit from **`BackgroundService`**, a base class that implements `IHostedService` and exposes a single `ExecuteAsync` method. The example shows the part that follows from this chapter, the scope per unit of work; [Chapter 11](#chapter-11-messaging-and-background-work) covers writing workers in full:

```csharp
public sealed class QueueProcessor(
    IServiceScopeFactory scopeFactory,
    ILogger<QueueProcessor> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Queue processor started");
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();     // scope per iteration
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                await ProcessBatchAsync(db, stoppingToken);
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
            catch (OperationCanceledException) { break; }         // graceful shutdown
            catch (Exception ex)
            {
                logger.LogError(ex, "Batch failed; retrying");
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
        }
    }
}

// builder.Services.AddHostedService<QueueProcessor>();
```

> **Pitfall:** the `stoppingToken` is your shutdown signal — honor it, or your app won't shut down cleanly and orchestrators will `SIGKILL` it after a grace period. Also note the **scope-per-iteration** pattern: a `BackgroundService` is effectively a singleton, so it must *not* capture scoped services directly — it creates a scope for each unit of work, exactly as the captive-dependency rule demands.

## .NET Release Cadence: LTS vs STS

Microsoft ships a **new major .NET version every November**, on a predictable cadence, alternating between two support tracks:

- **LTS (Long-Term Support)** releases are supported for **3 years**. These are the **even-numbered** versions: .NET 6, **.NET 8**, **.NET 10**.
- **STS (Standard-Term Support)** releases are supported for **2 years** — 18 months up to .NET 7, extended to two years from .NET 9. These are the **odd-numbered** versions: .NET 7, **.NET 9**, **.NET 11**.

Both tracks follow the same engineering and release process; the difference is purely the **support window**. The last six months of each window are *maintenance*: security fixes only. Patches ship monthly, on Patch Tuesday.

Versions and their dates, as of October 2026 (`releases-index.json` in the `dotnet/core` repository; [the appendix](#appendix-net-version-comparison-cheat-sheet) has what each version added):

- **.NET 8** (LTS, Nov 2023) and **.NET 9** (STS, Nov 2024) — both in maintenance; support for both ends on **November 10, 2026**. The longer STS window is why they end together.
- **.NET 10** (LTS, Nov 2025) — the active long-term-support release, supported until **November 14, 2028**; the target for anything new.
- **.NET 11** (STS) — at release candidate (RC1, September 2026, supported in production as "go-live"); GA is due in November 2026.

> **Best practice for teams:** standardize on **LTS releases** for products with long maintenance horizons — you get three years before a forced upgrade and a smaller upgrade treadmill. Choose **STS** only when you specifically need a feature that shipped there. Whatever you pick, plan upgrades *before* the support window closes: running on an out-of-support runtime means no security patches, which is an audit and compliance problem.

## Summary

- **Memory** splits into the per-thread **stack**, reclaimed for free when a method returns, and the shared **managed heap**, reclaimed by the GC; every heap allocation is future GC work.
- The **GC** is tracing, generational and compacting: Gen 0 collections are cheap and frequent, Gen 2 collections are expensive and should stay rare. Finalizers keep an object alive for an extra collection, so prefer `IDisposable`, and leave `GC.Collect()` alone.
- **Configuration** layers providers, a later one overriding an earlier one; the **options pattern** binds a section to a class, and only validation (with `ValidateOnStart`) turns a missing key into an error.
- The **DI container** builds the object graph with three lifetimes; a service may depend only on equal or longer lifetimes, and `ValidateScopes` catches the captive dependency only in Development.
- The **Generic Host** composes configuration, DI and logging, starts and stops hosted services, and hands `BackgroundService` a stopping token to honor.
- The **November release cadence** with **LTS (even) / STS (odd)** tracks lets you plan upgrades before support ends.
