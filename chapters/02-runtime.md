# Chapter 2: .NET Runtime & Internals

_⏱️ Estimated read time: ~38 min ·     5842 words (study pace)_

A senior .NET developer is expected to reason about what happens *beneath* the C# they write. When a request slows down under load, when memory climbs and never comes back, when a `Scoped` service throws in a singleton, or when a container image is 200 MB larger than it should be — the answers all live in the runtime. This chapter is a deep tour of that machinery: how memory is managed, how your IL becomes machine code, how the modern hosting stack (configuration, dependency injection, logging, background work) is wired together, and how to serialize data efficiently. By the end you should be able to hold a mental model of the CLR precise enough to debug production problems and make informed architectural decisions.

We start at the foundation — memory — because almost every performance and correctness question in .NET eventually touches it.

## The Memory Model: Stack vs Managed Heap

The .NET runtime gives every managed program two fundamentally different regions of memory to work with: the **stack** and the **managed heap**. Understanding which values live where is the single most useful mental model for reasoning about allocation, garbage collection, and performance.

The **stack** is a thread-local, last-in-first-out region. Each thread gets its own stack (by default 1 MB on Windows). When you call a method, the runtime pushes a *stack frame* containing the method's parameters, its local variables, and bookkeeping like the return address. When the method returns, the frame is popped — instantly, with zero cleanup cost. There is no garbage collector involved in stack memory; deallocation is just moving a pointer back down.

The **managed heap** is a process-wide region shared by all threads, and it's where reference-type objects live. When you write `new Customer()`, the runtime carves space out of the heap and hands you back a *reference* (essentially a pointer) to it. That reference might sit on the stack (as a local variable) or inside another heap object (as a field), but the `Customer` instance itself is always on the heap.

The critical distinction is **value types vs reference types**:

- **Value types** (`struct`, `int`, `bool`, `DateTime`, `Guid`, enums, tuples) contain their data directly. Where the *value* lives depends on context: a local `int` lives on the stack; an `int` field inside a class lives on the heap *inside that object*; an `int` in an array lives in the array's heap buffer. So "value types live on the stack" is a common oversimplification — value types live wherever their container lives.
- **Reference types** (`class`, `interface`, arrays, delegates, `string`) always have their instance data on the heap; only the reference is passed around.

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

> **Why this matters:** every heap allocation has a cost — not just the allocation itself, but the eventual GC work to reclaim it. High-performance .NET code minimizes heap allocations by favoring `structs` for small, short-lived data, using `Span<T>` for slicing without copying, and avoiding hidden allocations (boxing, closures, LINQ in hot paths).

**Boxing** is the bridge between the two worlds and a classic performance trap. When you assign a value type to a variable of type `object` (or an interface it implements), the runtime *boxes* it: allocates a heap object, copies the value into it, and returns a reference. Unboxing copies it back out.

```csharp
int x = 5;
object boxed = x;        // boxing: heap allocation happens here
int y = (int)boxed;      // unboxing: value copied back to the stack
```

A single box is cheap; a million boxes in a loop is a GC storm. Generics were introduced in part to eliminate boxing — `List<int>` stores ints inline, whereas the old `ArrayList` boxed every element.

## Garbage Collection

The .NET garbage collector is a **tracing, generational, compacting** collector. Let's unpack each of those words, because each represents a deliberate design decision that shapes how your programs behave.

**Tracing** means the GC determines liveness by starting from a set of *roots* — static fields, local variables and CPU registers on every thread's stack, and GC handles — and following references transitively. Any object reachable from a root is *live*; everything else is *garbage*. The GC never needs the program to tell it what to free; it discovers it.

**Compacting** means that after identifying garbage, the GC slides the surviving objects together to close the gaps, eliminating fragmentation. This is why heap allocation in .NET is astonishingly fast: because the heap is kept compact, allocating is usually just bumping a pointer (the "allocation pointer") forward — no free-list search like `malloc`. The trade-off is that surviving objects get *moved*, which is why the runtime must pause threads at safe points and fix up references during a collection.

### Generations 0, 1, and 2

The **generational hypothesis** is the observation that most objects die young. A JSON response deserialized to handle a web request, the intermediate strings in a formatting operation, the temporary list inside a method — these live for microseconds. A few objects (caches, singletons, long-lived buffers) live for the whole process. Very few live "medium" lengths.

The GC exploits this by dividing the heap into three **generations**:

- **Gen 0** — the nursery. All new small objects are allocated here. Gen 0 is small (tuned to fit in CPU cache), so collecting it is fast.
- **Gen 1** — a buffer between short-lived and long-lived. Objects that survive a Gen 0 collection are *promoted* to Gen 1.
- **Gen 2** — long-lived objects. Survivors of Gen 1 are promoted here. Gen 2 also holds the Large Object Heap.

The magic is that **collecting a lower generation doesn't require scanning higher ones for most purposes**. A Gen 0 collection only examines Gen 0 objects (plus a clever mechanism, described below, to find references *from* older objects *into* Gen 0). Because Gen 0 is small and most of it is dead, Gen 0 collections are extremely cheap and frequent. Gen 2 collections (also called *full* collections) are expensive because they scan the entire heap — these are the ones you want to keep rare.

> **The write barrier and card tables.** How can a Gen 0 collection be correct without scanning Gen 2? An old object might hold a reference to a young one (e.g., you add a freshly allocated item to a long-lived cache). The runtime handles this with a **write barrier**: every time you store a reference into an object's field, a tiny piece of JIT-emitted code marks a "card" (a small region of memory) as dirty in a **card table**. During a Gen 0 collection, the GC scans only the dirty cards of older generations to find cross-generational references. This is why reference assignments are marginally more expensive than value assignments — there's an invisible barrier running.

### How the GC decides to collect

A collection is triggered when one of these happens:

1. **Gen 0 fills up.** The allocation budget for Gen 0 is exhausted — the most common trigger. The runtime dynamically tunes this budget based on allocation and survival rates.
2. **Memory pressure from the OS.** The system signals it's low on physical memory.
3. **Explicit `GC.Collect()`.** You asked for it (usually a mistake — see below).

The GC also *dynamically self-tunes*. If it notices that objects promoted to Gen 2 keep surviving, it adjusts budgets to collect Gen 2 less often. It's an adaptive system, not a fixed schedule.

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

### Finalization and the relationship to IDisposable

The GC reclaims *managed* memory automatically. But some objects wrap *unmanaged* resources — file handles, socket descriptors, database connections, native memory — that the GC knows nothing about. Two mechanisms address this: **finalizers** and **`IDisposable`**.

A **finalizer** (`~ClassName()`) is a method the GC calls before reclaiming an object, giving it a chance to release unmanaged resources. But finalization is a costly, non-deterministic safety net:

1. When the GC finds an unreachable object *with a finalizer*, it can't reclaim it immediately. Instead it places the object on the **finalization queue**, which keeps it alive.
2. A dedicated **finalizer thread** later runs the finalizer.
3. Only on the *next* GC can the object actually be collected.

So a finalizable object survives at least one extra generation and requires two collection cycles to die. Overusing finalizers is a real performance problem.

**`IDisposable`** provides *deterministic* cleanup: you (or a `using` block) call `Dispose()` at a precise, known point rather than waiting for the GC. This is the preferred mechanism.

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
- **Limited reflection.** Because AOT trims aggressively and can't see code paths reached only via reflection, features like reflection-based serialization need special handling (source generators — see the JSON section).
- **Whole-program compilation** with **trimming** is mandatory, which can break code that reflects over types the trimmer removed.

```xml
<PropertyGroup>
  <PublishAot>true</PublishAot>
</PropertyGroup>
<!-- dotnet publish -r linux-x64 -c Release -->
```

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

## The Configuration System

Modern .NET replaced the old `app.config`/`web.config` XML world with a flexible, layered **configuration system** built around `IConfiguration`. The core idea: configuration is a set of **key-value pairs** assembled from multiple **providers**, layered so that later providers override earlier ones.

Common providers, typically layered in this order:

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

Reading individual string keys everywhere is fragile. The **Options pattern** binds a configuration section to a strongly-typed C# class, giving you type safety, IntelliSense, validation, and testability.

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

You then inject one of three options interfaces, and the difference between them is a common senior-level interview question:

- **`IOptions<T>`** — a singleton, computed once. Fine for values that don't change during the process lifetime. Can be injected into singletons.
- **`IOptionsSnapshot<T>`** — recomputed **per request** (it's a scoped service). Reflects config changes (e.g., an edited JSON file) and supports named options. Cannot be injected into a singleton (it's scoped — see captive dependencies below).
- **`IOptionsMonitor<T>`** — a singleton that supports **change notifications** via `OnChange` callbacks and always returns the current value. Use it when a singleton needs live-reloading config.

```csharp
public class Mailer(IOptions<EmailOptions> options)
{
    private readonly EmailOptions _cfg = options.Value;
}
```

## Dependency Injection

.NET has a built-in **DI container** (`Microsoft.Extensions.DependencyInjection`) at the heart of the modern hosting model. DI inverts control: instead of a class constructing its own dependencies, it *declares* them (usually as constructor parameters) and the container supplies them. This decouples classes from concrete implementations, makes them testable, and centralizes wiring.

You register services against a `IServiceCollection`, then the container builds an `IServiceProvider` that *resolves* them. Resolution is recursive: to build `OrderService`, the container sees it needs an `IRepository`, builds that, sees the repository needs a `DbContext`, builds that, and so on down the dependency graph.

### Service lifetimes

The lifetime you choose controls how long an instance lives and how often it's created:

- **Transient** — a **new instance every time** it's requested. Use for lightweight, stateless services. If `A` and `B` both depend on a transient `C`, they each get their own `C`.
- **Scoped** — **one instance per scope**. In ASP.NET Core, a scope is created per HTTP request, so a scoped service is shared within a request but distinct across requests. `DbContext` is the archetypal scoped service — you want one unit-of-work per request.
- **Singleton** — **one instance for the entire application** lifetime, created once and shared by everyone. Use for stateless services, caches, and expensive-to-create objects. Must be thread-safe, since concurrent requests share it.

```csharp
builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddScoped<IOrderRepository, SqlOrderRepository>();
builder.Services.AddTransient<IEmailValidator, EmailValidator>();
```

### Captive dependencies — the classic DI bug

Because the container resolves dependencies recursively, a longer-lived service that captures a shorter-lived one **freezes** the shorter-lived one for its own lifetime. This is a **captive dependency**, and it's a frequent production bug.

Consider a **singleton that depends on a scoped `DbContext`**. The singleton is created once, so it resolves the `DbContext` once, and then *holds that same `DbContext` forever* — across all requests, all threads. `DbContext` is not thread-safe and is meant to be short-lived, so you get corrupted state, `ObjectDisposedException`s, and concurrency errors that are maddening to reproduce.

> **The rule:** a service may only depend on services with an **equal or longer** lifetime. Singleton → Singleton is fine. Scoped → Singleton is fine. Singleton → Scoped is a bug. Transient captured by a Singleton effectively becomes a Singleton.

The built-in container helps catch this: in the Development environment, ASP.NET Core enables **scope validation**, which throws if you try to resolve a scoped service from the root (singleton) scope.

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

The container also manages **disposal**: if a resolved service implements `IDisposable`, the container disposes it when its scope ends (per-request for scoped, at app shutdown for singletons). This is why you should let the container own service lifetimes rather than `new`-ing services yourself — you'd lose automatic disposal.

## The Generic Host and Background Services

The **Generic Host** (`IHost`) is the composition root of a modern .NET application. It bundles together DI, configuration, logging, and lifetime management into one object that you build, run, and gracefully shut down. `WebApplication` (ASP.NET Core) and the console `Host` both build on it.

The host manages a set of **`IHostedService`** instances — components with a `StartAsync`/`StopAsync` lifecycle tied to the application's. When the host starts, it starts all hosted services; when it receives a shutdown signal (Ctrl+C, SIGTERM from Kubernetes), it stops them gracefully, giving in-flight work a chance to finish.

For long-running background work, you inherit from **`BackgroundService`**, a base class that implements `IHostedService` and exposes a single `ExecuteAsync` method:

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

## Logging with Microsoft.Extensions.Logging

`Microsoft.Extensions.Logging` provides a **provider-agnostic logging abstraction**. Your code depends on `ILogger<T>`; the actual output (console, files, Seq, Application Insights, Serilog) is a **provider** configured at startup. This decoupling means you can swap logging backends without touching application code.

**Log levels**, from most to least verbose: `Trace`, `Debug`, `Information`, `Warning`, `Error`, `Critical`. You configure a minimum level (globally and per-namespace via configuration), and messages below it are cheaply skipped.

### Structured logging

The most important concept for senior developers is **structured logging**. Instead of building a formatted string, you pass a **message template** with named placeholders and the values separately. The logging framework captures these as **structured key-value pairs**, not just text.

```csharp
public class OrderService(ILogger<OrderService> logger)
{
    public void Place(int orderId, decimal amount)
    {
        // GOOD: structured — 'OrderId' and 'Amount' become queryable fields
        logger.LogInformation("Order {OrderId} placed for {Amount:C}", orderId, amount);

        // BAD: interpolated string — collapses to opaque text, loses structure
        logger.LogInformation($"Order {orderId} placed for {amount:C}");
    }
}
```

Why it matters: with structured logs feeding a system like Seq or Elasticsearch, you can query `OrderId = 4567` across millions of log lines, or aggregate by `Amount`. The interpolated version throws that away — and worse, it *always* builds the string even when the log level is disabled, wasting CPU. The template version defers formatting and skips it entirely if the level is off.

> **Best practice:** always use message templates with named placeholders, never string interpolation, inside logging calls. Note that placeholders are matched to arguments **by position**, not by name — order matters. Use **log scopes** (`logger.BeginScope`) to attach contextual properties (like a correlation ID) to every log line within a block.

## Serialization: System.Text.Json vs Newtonsoft.Json

For years, **Newtonsoft.Json** (Json.NET) was the de facto standard. Since .NET Core 3.0, Microsoft ships **`System.Text.Json` (STJ)** in the box, designed for **high performance and low allocation** — it works directly over `Span<byte>`/UTF-8, avoiding the intermediate string conversions Newtonsoft performs, and is significantly faster with a smaller memory footprint.

Key differences to know:

- **Performance:** STJ is substantially faster and allocates less. It's the default in ASP.NET Core.
- **Defaults:** STJ is **stricter** by default — case-sensitive property matching (configurable), no comments or trailing commas unless enabled, and it doesn't handle some things Newtonsoft did permissively (like quoted numbers) without opting in.
- **Feature breadth:** Newtonsoft still has richer support for some advanced scenarios (`TypeNameHandling` for polymorphic type embedding, `[JsonConstructor]` flexibility, `DefaultValueHandling` nuances, `JObject`/`JToken` LINQ-to-JSON ergonomics), though STJ has closed most gaps and added polymorphism support (`[JsonDerivedType]`) and `JsonNode`.

```csharp
var options = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = false
};

string json = JsonSerializer.Serialize(order, options);
Order? back = JsonSerializer.Deserialize<Order>(json, options);
```

### Custom converters

When the default mapping doesn't fit (a custom date format, an enum-as-string, a domain primitive), you write a **`JsonConverter<T>`**:

```csharp
public sealed class DateOnlyConverter : JsonConverter<DateOnly>
{
    private const string Format = "yyyy-MM-dd";
    public override DateOnly Read(ref Utf8JsonReader reader, Type t, JsonSerializerOptions o)
        => DateOnly.ParseExact(reader.GetString()!, Format);
    public override void Write(Utf8JsonWriter writer, DateOnly value, JsonSerializerOptions o)
        => writer.WriteStringValue(value.ToString(Format));
}
```

### Source-generated serialization

Reflection-based serialization has two costs: **startup reflection overhead**, and **incompatibility with trimming/Native AOT** (the trimmer can't see reflection-driven access). STJ's **source generator** solves both. You declare a partial `JsonSerializerContext` with `[JsonSerializable]` attributes, and at *compile time* the generator emits fast, reflection-free, trim-safe serialization code.

```csharp
[JsonSerializable(typeof(Order))]
[JsonSerializable(typeof(Customer))]
public partial class AppJsonContext : JsonSerializerContext { }

// Usage — no runtime reflection, AOT-safe, faster startup:
string json = JsonSerializer.Serialize(order, AppJsonContext.Default.Order);
Order? o = JsonSerializer.Deserialize(json, AppJsonContext.Default.Order);
```

> **Best practice:** for new projects, default to `System.Text.Json`. Reach for source generation in AOT/trimmed apps and hot serialization paths. Keep Newtonsoft only where you depend on a feature STJ lacks or a library that requires it — and be aware of the strictness differences when migrating existing code.

## .NET Release Cadence: LTS vs STS

Microsoft ships a **new major .NET version every November**, on a predictable cadence, alternating between two support tracks:

- **LTS (Long-Term Support)** releases are supported for **3 years**. These are the **even-numbered** versions: .NET 6, **.NET 8**, **.NET 10**.
- **STS (Standard-Term Support)** releases — formerly "Current" — are supported for **18 months**. These are the **odd-numbered** versions: .NET 7, **.NET 9**.

Both LTS and STS are equally *stable and production-ready*; the difference is purely the **support window**, not quality. STS releases often preview features that later land in the next LTS.

As of this writing (mid-2026), the relevant versions are:

- **.NET 8** (LTS, Nov 2023) — the workhorse for most production systems; supported into late 2026.
- **.NET 9** (STS, Nov 2024) — performance and feature refinements; support ends mid-2026.
- **.NET 10** (LTS, Nov 2025) — the current long-term-support release, the recommended target for new long-lived systems.

> **Best practice for teams:** standardize on **LTS releases** for products with long maintenance horizons — you get three years before a forced upgrade and a smaller upgrade treadmill. Choose **STS** only when you specifically need a feature that shipped there. Whatever you pick, plan upgrades *before* the support window closes: running on an out-of-support runtime means no security patches, which is an audit and compliance problem.

## Summary

The runtime is the substrate on which all your .NET code executes, and senior-level judgment comes from understanding it:

- **Memory** splits into the fast, automatic **stack** and the GC-managed **heap**; knowing where data lives explains allocation costs and boxing traps.
- The **GC** is generational, compacting, and self-tuning; Gen 0 is cheap, Gen 2 and the LOH are expensive, Server GC scales with cores, and Background GC keeps pauses low. Prefer `IDisposable` for deterministic cleanup and avoid finalizers and `GC.Collect()`.
- **JIT + Tiered Compilation + PGO** deliver fast startup and peak throughput; **ReadyToRun**, **Native AOT**, and **trimming** trade flexibility for startup speed and size, pushing the ecosystem toward **source generators**.
- The **hosting stack** — layered **configuration** with the options pattern, the **DI container** with its three lifetimes and the captive-dependency rule, the **Generic Host** with `BackgroundService`, and **structured logging** — is the shared skeleton of every modern .NET app.
- **System.Text.Json** is the fast, AOT-friendly default for serialization, with source generation for trim-safe, reflection-free performance.
- The **November release cadence** with **LTS (even) / STS (odd)** tracks lets you plan upgrades deliberately.

Internalize these and you can reason from symptoms (a latency spike, a memory leak, a cold-start regression) back to root causes in the runtime — which is exactly what separates a mid-level developer from a senior one.
