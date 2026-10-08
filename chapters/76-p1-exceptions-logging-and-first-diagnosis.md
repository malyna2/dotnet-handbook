# Part 1 · Module 6: Exceptions, Logging and First Diagnosis

> **What this module makes you able to do.** Decide where a failure is caught and what the caller sees, write log events that a log store can query and join across services, and take the first measurement of a slow endpoint before anyone guesses at a fix.

**Time:** reading ≈ 40 min; hands-on ≈ 1 h 5 min — the two experiments 20 min, the questions 15, the check at work 30.

## Covers

- where to catch an exception, and why most layers should not catch at all;
- `throw;` versus `throw ex;`, and what an exception filter (`when`) decides before the stack unwinds;
- what an API returns when it fails: ProblemDetails with a trace id, never the exception's text;
- structured logging: why a message template keeps properties and string interpolation flattens them; which level; what never goes into a log;
- following one request across services by its trace id, and reading the stack trace you find;
- the first look at a slow endpoint: CPU-bound or waiting, and the thread-pool starvation fingerprint.

## The mechanism to explain without notes

**An exception carries its own diagnosis — type, message, the frames it unwound through, the inner exception — so the code between the throw and the boundary should stay out of its way, and the boundary should turn it into exactly one log event and one safe response, joined by a trace id.**

A `catch` earns its place only if it *translates* (wraps the exception in one your abstraction owns, the original kept as `InnerException`), *handles* (retries, falls back, compensates) or *reports* (the outermost boundary: middleware, a message dispatcher, a worker loop). Every other `catch` can only destroy part of the diagnosis:

- **`throw ex;` restarts the trace at the rethrow.** The frame that threw disappears and the log blames the catch block; `throw;` keeps it. A filter (`catch … when`) runs before the stack unwinds, so an exception it declines travels on with every frame intact.
- **Log-and-rethrow multiplies, swallowing erases.** One failure becomes one `Error` per layer, or none at all.
- **The boundary answers in two directions.** Outward, a status code that says who can fix it and a ProblemDetails body with a stable code and the `traceId`. Inward, one log entry with the whole exception. The trace id is how support gets from the first to the second.

A log event follows the same rule. With a message template the logger receives the template and the values separately: each placeholder becomes a named, typed property, and the template itself names the kind of event. An interpolated string arrives finished, so the properties are gone and every value makes a new kind of event. The level says who has to act. Secrets and personal data stay out, because logs are copied to more places, kept longer and read by more people than the database.

Across services the trace id is the join key: `HttpClient` sends it in the W3C `traceparent` header, ASP.NET Core continues it, and the generic host adds it to the scope of every log entry. A queue carries it only if the message does.

A slow endpoint gets the same discipline: measure before changing anything. A busy CPU means profile the CPU. An idle CPU with high latency means the request is *waiting*: on a dependency (its span dominates the trace), or for a thread (the pool's queue grows and every endpoint slows down, as [Chapter 8](#the-sync-over-async-deadlock) explains).

## Read (≈ 40 min)

1. [Chapter 5: Exception Handling Strategy](#exception-handling-strategy): classify the failure, then where to catch, *The Mechanics That Bite* (`throw;`, filters, `ExceptionDispatchInfo`), what to log (with the *Pay attention* callout on reading a stack trace) and what to surface.
2. [Chapter 3: Error Handling with ProblemDetails (RFC 7807)](#error-handling-with-problemdetails-rfc-7807): `UseExceptionHandler` and `IExceptionHandler`, and the *Pay attention* callout on what reaches the caller and what reaches the log.
3. [Chapter 2: Logging with Microsoft.Extensions.Logging](#logging-with-microsoftextensionslogging): the provider abstraction, placeholders matched by position, scopes.
4. [Chapter 13: Why Structured Beats String Logging](#why-structured-beats-string-logging), [Log Levels: A Shared Vocabulary](#log-levels-a-shared-vocabulary) and [What Not to Log: Secrets and PII](#what-not-to-log-secrets-and-pii).
5. [Chapter 13: Correlation Across Services](#correlation-across-services): the trace id over HTTP and through a queue, and why it beats a home-made correlation id.
6. [Chapter 34: Diagnosing a Performance Problem (a worked methodology)](#diagnosing-a-performance-problem-a-worked-methodology): CPU-bound versus waiting, the tools in order, and the starvation fingerprint in its *Pay attention* callout.

## Prove it

Predict each output before you run it.

**6a. `throw;` keeps the frame that threw; `throw ex;` erases it.**

`verify/path/ThrowVsThrowEx/Program.cs` · run it from `verify/path` with `dotnet run --project ThrowVsThrowEx`:

```csharp
// Prove it: after `throw;` the stack trace still starts in the method that threw; `throw ex;` restarts it at the rethrow.
using System.Runtime.CompilerServices;

foreach (bool resetTrace in new[] { false, true })
    try { OrderService.Get(42, resetTrace); }
    catch (Exception e) { Console.WriteLine($"{(resetTrace ? "throw ex;" : "throw;")}\n{e.StackTrace}"); }

static class OrderService
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Get(int id, bool resetTrace)
    {
        try { OrderStore.Load(id); }
        catch (InvalidOperationException) when (!resetTrace)
        {
            throw;                      // rethrows the same exception, its trace intact
        }
        catch (InvalidOperationException ex)
        {
#pragma warning disable CA2200         // the SDK flags the next line by default
            throw ex;                   // the trace restarts on this line
        }
    }
}

static class OrderStore
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Load(int id) => throw new InvalidOperationException($"order {id} is missing");
}
```

```text
throw;
   at OrderStore.Load(Int32 id) in …/ThrowVsThrowEx/Program.cs:line 29
   at OrderService.Get(Int32 id, Boolean resetTrace) in …/ThrowVsThrowEx/Program.cs:line 13
   at Program.<Main>$(String[] args) in …/ThrowVsThrowEx/Program.cs:line 5
throw ex;
   at OrderService.Get(Int32 id, Boolean resetTrace) in …/ThrowVsThrowEx/Program.cs:line 21
   at Program.<Main>$(String[] args) in …/ThrowVsThrowEx/Program.cs:line 5
```

What to notice:

- **Read a trace from the top.** The first frame is where the exception was thrown (line 29). Each frame below is the caller of the one above, at the line of the call. The last is the frame that caught it: a trace is the path the exception travelled, not the whole call stack.
- **`throw ex;` erased the frame with the bug.** The trace starts at line 21, the rethrow, and `OrderStore.Load` appears nowhere. In production that log entry points at the catch block.
- **The SDK already warns.** `throw ex;` is warning CA2200 by default, which is why the program needs a `#pragma` to build with warnings as errors.
- **A frame can be missing.** Release builds inline small methods into their callers, and an inlined method has no frame of its own. `NoInlining` keeps these two visible.

**6b. A template keeps a typed property; interpolation flattens it.**

`verify/path/LogTemplate/Program.cs` · run it from `verify/path` with `dotnet run --project LogTemplate`:

```csharp
// Prove it: a message template keeps {OrderId} as a named, typed property; an interpolated string arrives flat.
using Microsoft.Extensions.Logging;

using var factory = LoggerFactory.Create(logging => logging.AddProvider(new PrintingProvider()));
ILogger logger = factory.CreateLogger("Orders");
int orderId = 42;

logger.LogInformation("Order {OrderId} placed", orderId);
logger.LogInformation($"Order {orderId} placed");

// The smallest provider there is: it prints what every real provider receives.
sealed class PrintingProvider : ILoggerProvider, ILogger
{
    public ILogger CreateLogger(string categoryName) => this;
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;
    public void Dispose() { }

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        Console.WriteLine($"message:  {formatter(state, exception)}");
        if (state is IEnumerable<KeyValuePair<string, object?>> properties)
            foreach (var (name, value) in properties)
                Console.WriteLine($"  {name} = {value} ({value?.GetType().Name})");
    }
}
```

```text
message:  Order 42 placed
  OrderId = 42 (Int32)
  {OriginalFormat} = Order {OrderId} placed (String)
message:  Order 42 placed
  {OriginalFormat} = Order 42 placed (String)
```

What to notice:

- **The text is identical.** Reading the console, you can't tell which call was right; only what the provider receives shows it.
- **The template delivers `OrderId` as an `Int32`.** A log store indexes it, so `OrderId = 42` is a query, not a regular expression.
- **`{OriginalFormat}` names the kind of event.** Every call from the template line shares it, so a store can group and count those events. Interpolated, the "template" is the finished text: each order id is a new kind of event.
- **Nothing stops you.** The analyzer rule for this, CA2254, is a suggestion by default, so the build stays green. Raise it to a warning in `.editorconfig`.

## Three questions

**1.** A repository wraps every method in `try { … } catch (Exception ex) { _logger.LogError(ex, "Failed"); throw ex; }`. What does this cost, in the order an on-call engineer finds out?

<details>
<summary>Answer</summary>

- **The log points at the wrong line.** `throw ex;` restarts the stack trace at the rethrow, so the frame that actually failed — the query, the null dereference — is gone from every entry after this one.
- **One failure, several errors.** The boundary logs the same exception again, and so does every other layer with the same block: the error rate is inflated and each alert fires several times for one fault.
- **Cancellations become errors.** `catch (Exception)` also catches the `OperationCanceledException` of a client that disconnected, and logs it at `Error`.

Fix: delete the block. If the repository must translate, catch the specific exception (`catch (SqlException ex) when (…)`) and throw your own with the original as its inner exception; log once, at the boundary.
</details>

**2.** `logger.LogInformation($"Order {orderId} placed")` prints the same text as the template version. Why does it still break the log store, and what does it cost when `Information` is turned off?

<details>
<summary>Answer</summary>

- **The logger never sees the value.** The compiler builds the string before the call, so the provider receives one finished string: no `OrderId` property to query, and a different `{OriginalFormat}` for every order, so nothing groups.
- **It costs even when nobody listens.** The interpolation runs before `LogInformation` is called, so the string is built and thrown away when the level is off. A template defers the formatting until a provider is enabled, although its arguments are still boxed into an array; the `[LoggerMessage]` source generator checks the level first and avoids even that.

Fix: templates everywhere, CA2254 as a warning, and `[LoggerMessage]` on hot paths.
</details>

**3.** An endpoint's p99 jumped from 200 ms to 4 s, and CPU sits at 15%. What are the two likely causes, what tells them apart, and what do you open first?

<details>
<summary>Answer</summary>

- **Low CPU with high latency means waiting, not computing.** Either a dependency got slow (database, downstream HTTP, a lock), or the thread pool is starved by blocking calls.
- **What tells them apart.** A slow dependency slows only its own callers, its span dominates the trace, and the pool's queue stays near zero. Starvation slows *every* endpoint, including ones that never touch the dependency, while the thread-pool queue length grows and the thread count climbs (`ThreadPool Queue Length` and `ThreadPool Thread Count` in `dotnet-counters`).
- **First:** `dotnet-counters monitor --counters 'EventCounters\System.Runtime'` on the process (no setup), and the trace of one slow request. If it is starvation, `dotnet-stack report` shows the pool threads parked in `Task.InternalWait`.

Fix the cause: remove the blocking call (raising the pool's minimum only moves the cliff), or diagnose the dependency (its query plan, its timeout, its retry budget).
</details>

## Check at work

**Inspect.** Search your service for:

- `throw ex;` — CA2200 is a build warning by default, so a hit means warnings are being ignored or suppressed;
- `catch (Exception` outside the outermost boundaries, and `catch` blocks that only log and rethrow;
- `Log` calls with a `$"…"` message;
- `ex.Message` or `ex.ToString()` written into a response, and `UseDeveloperExceptionPage` or `ASPNETCORE_ENVIRONMENT=Development` anywhere near production.

A good result: each hit is a deliberate boundary. A bad one: a `catch` in every layer.

**Measure.** In a test environment, make one request fail. Count the `Error` entries it produced (one is right), then search your log store for the `traceId` from its ProblemDetails response and count the services whose entries you get back. During the next busy hour, run `dotnet-counters monitor` against one instance and write down CPU usage, thread-pool queue length and thread count: that is your baseline for the next slow day.
