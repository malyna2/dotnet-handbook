# Chapter 9: Exceptions, Logging and First Diagnosis

Every service fails, and what decides how fast a failure gets fixed is the trail it leaves: one log entry carrying the whole exception, a response that tells the caller what they can do about it, and one id that joins the two. This chapter teaches the habits that leave that trail, and the first measurements to take when the complaint is not an error but slowness.

It starts with the tool every later section uses: `ILogger` and structured logging, where a message template keeps typed properties a log store can query, a level says who has to act, and secrets stay out. The exception-handling strategy then settles where a failure is caught (almost nowhere), what gets logged (once, at the boundary) and what the caller sees (a ProblemDetails body with a stable code and a trace id). Next comes the trace id itself: the wiring that gives every request one, and how it follows the request across services and queues so the logs of five services read as one story. The chapter closes with a method for a slow endpoint — CPU-bound or waiting, and the fingerprint of a starved thread pool — and then two programs to run, three questions and checks for your own service.

## Logging with Microsoft.Extensions.Logging

`Microsoft.Extensions.Logging` provides a **provider-agnostic logging abstraction**. Your code depends on `ILogger<T>`; the actual output (console, files, Seq, Application Insights, Serilog) is a **provider** configured at startup. This decoupling means you can swap logging backends without touching application code.

### Why Structured Beats String Logging

Most developers start with logs like this:

```csharp
logger.LogInformation($"User {userId} placed order {orderId} for {amount:C}");
```

It produces `User 42 placed order 9981 for $59.99`, which reads fine until ten million such lines must answer "what did user 42 order today?" with regular expressions over text nobody designed to be parsed.

**Structured logging** keeps the values as named fields instead of baking them into text:

```csharp
logger.LogInformation("User {UserId} placed order {OrderId} for {Amount}", userId, orderId, amount);
```

Those are **message template** tokens, not interpolation holes. The framework captures `UserId`, `OrderId` and `Amount` as separate, typed properties of the event; the rendered text is the same, but `UserId = 42 AND Amount > 50` is now a query.

The interpolated version also costs more. The compiler builds the string before the call, so it is built *always*, even when the level is disabled and the line is thrown away. The template version defers formatting until an enabled provider needs it, but its arguments are still boxed into a `params object[]` on every call. On hot paths, the `[LoggerMessage]` source generator emits a method that checks `IsEnabled` first and passes the values without boxing.

> **Pay attention.** **The template is the kind of event.**
>
> `ILogger.Log` hands every provider a `state` object, not a string. For a template call it is a list of key-value pairs: one per placeholder, holding the original typed value, plus `{OriginalFormat}`, the template itself. Log stores use that last entry as the event's type, so every "order placed" event groups and counts together. An interpolated string is built by the compiler before the call: the provider receives no properties, and `{OriginalFormat}` is the finished text, a new "type" for every value. The *LogTemplate* program at the end of this chapter prints both states side by side.
>
> Use templates in every log call. The analyzer rule that flags interpolation, CA2254, is only a suggestion by default (.NET 10), so the build stays green: set `dotnet_diagnostic.CA2254.severity = warning` in `.editorconfig`.

> **Best practice.** Placeholders are matched to arguments **by position**, not by name, so order matters. Use **log scopes** (`logger.BeginScope`) to attach contextual properties, such as a correlation id, to every log line within a block; the Serilog section below shows the same idea as `LogContext`.

### Log Levels: A Shared Vocabulary

A level answers one question: who has to act on this entry, and how soon. It is also a filter: the configured minimum (`Logging:LogLevel:Default`, overridden per category such as `Microsoft.AspNetCore`) is checked before a message is formatted, so a disabled level costs almost nothing.

- **Trace / Verbose** — extremely detailed diagnostic flow, usually off in production.
- **Debug** — internal state useful during development or targeted troubleshooting.
- **Information** — normal, noteworthy business events: "order placed," "user registered." The heartbeat of your application.
- **Warning** — something unexpected happened but the system recovered or degraded gracefully: a retry succeeded, a cache missed, a deprecated path was hit.
- **Error** — an operation failed and a user or process was affected. A caught exception that broke a request.
- **Critical / Fatal** — the application or a major subsystem is unusable. Database unreachable, out of memory.

> **Pitfall:** Alerts and error-rate dashboards count `Error` entries. Every entry at `Error` that needs no action — a validation failure, a 404, a client that disconnected — teaches on-call to ignore the alert; every real failure logged at `Information` never reaches it. Reserve `Error` for failures someone must act on, and log each one once.

### What Not to Log: Secrets and PII

> **Critical pitfall:** Never log passwords, API keys, connection strings, bearer tokens, full credit card numbers, government IDs, or personal data like full names, emails, or addresses unless you have a lawful basis and proper redaction. Logs are frequently shipped to third-party systems, retained for months, and accessible to broad audiences. A logged secret is a leaked secret.

Concrete defenses:

- When destructuring objects with `{@Object}`, they may contain sensitive fields. Configure a Serilog destructuring policy or `[NotLogged]`-style attributes to strip them.
- Log a hashed or masked version instead: `****1234` for a card, or a stable pseudonymous user ID instead of an email.
- Under GDPR and similar regimes, personal data in logs is subject to retention and deletion rules. The safest log is one that contains no PII at all.

### Serilog in Depth

Serilog is the de facto structured logging library for .NET. Its mental model has four moving parts worth understanding deeply: **message templates**, **sinks**, **enrichers**, and the **LoggerConfiguration** pipeline.

A basic setup in a modern ASP.NET Core app looks like this:

```csharp
using Serilog;
using Serilog.Events;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, configuration) => configuration
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .Enrich.WithMachineName()
    .Enrich.WithProperty("Application", "OrderService")
    .WriteTo.Console(outputTemplate:
        "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}")
    .WriteTo.Seq("http://localhost:5341"));

var app = builder.Build();
```

Let's dissect each concept.

**Message templates** are the heart of Serilog. When you write `logger.LogInformation("Order {OrderId} shipped", orderId)`, Serilog stores the raw template *and* the property. This means events with the same template but different IDs are recognized as the same *kind* of event, which is enormously valuable for grouping and analysis.

A subtle but powerful feature is the `@` destructuring operator:

```csharp
var order = new Order { Id = 9981, Total = 59.99m, Items = 3 };
logger.LogInformation("Processing {@Order}", order);
```

The `@` tells Serilog to serialize the object's properties into structured data rather than calling `ToString()`. Without it (`{Order}`), you would get the type name. With `$` (`{$Order}`) you force stringification. Use `@` when you want the object's shape preserved in your log store.

**Sinks** are output destinations. Serilog's architecture is a pipeline where one log event fans out to many sinks. Console, File, Seq, Elasticsearch, Application Insights, Datadog — each is a separate NuGet package. You can attach as many as you like:

```csharp
.WriteTo.Console()
.WriteTo.File("logs/app-.log", rollingInterval: RollingInterval.Day)
.WriteTo.Seq("http://localhost:5341")
```

For high-throughput services, wrap slow sinks in the **async** sink so logging never blocks a request thread:

```csharp
.WriteTo.Async(a => a.File("logs/app-.log", rollingInterval: RollingInterval.Day))
```

**Enrichers** automatically attach context to every event. Rather than manually adding the machine name to each log call, an enricher does it once for all events. `Enrich.FromLogContext()` is the most important one: it lets you push properties onto an ambient scope that all logs within that scope inherit.

```csharp
using (LogContext.PushProperty("CorrelationId", correlationId))
{
    logger.LogInformation("Started processing");   // has CorrelationId
    await DoWorkAsync();                             // any log inside also has it
    logger.LogInformation("Finished processing");  // has CorrelationId
}
```

This is how you implement **correlation IDs** cleanly. Every log emitted while that scope is active is stamped with the same ID, so you can later filter your entire log store to a single request's journey. In practice you set this in middleware:

```csharp
app.Use(async (context, next) =>
{
    var correlationId = context.Request.Headers["X-Correlation-ID"].FirstOrDefault()
                        ?? Guid.NewGuid().ToString();
    context.Response.Headers["X-Correlation-ID"] = correlationId;

    using (Serilog.Context.LogContext.PushProperty("CorrelationId", correlationId))
    {
        await next();
    }
});
```

> **Best practice:** Configure logging as early as possible in `Program.cs`, and wrap the whole application in a `try/catch` that logs fatal startup exceptions to a bootstrap logger. A crash during startup that produces no log is the worst kind of silent failure.

### NLog, Briefly

NLog is the other mature structured logging library for .NET. It is configuration-file-driven (XML `nlog.config`) by tradition, with "targets" (equivalent to Serilog sinks) and "rules" that route loggers to targets by name and level. It also supports structured properties via the same `{Name}` template syntax through the `Microsoft.Extensions.Logging` bridge. Functionally the two are close; Serilog's fluent C# configuration and richer ecosystem of sinks have made it the more common choice in greenfield .NET projects, but NLog remains excellent and slightly faster in some file-logging benchmarks. Pick one and standardize.

> **Modern note:** The **OpenTelemetry Logs signal is now stable in .NET**. An `ILogger` → OpenTelemetry bridge — wired via `AddOpenTelemetry().WithLogging(...)`, sitting right alongside `.WithTracing()` and `.WithMetrics()` — lets your existing `ILogger` calls flow straight out over OTLP, completing the "one SDK for logs, metrics, and traces" story of [Observability Wiring](#observability-wiring) later in this chapter.

## Exception Handling Strategy

Most codebases have a *style* of exception handling — `try`/`catch` wherever someone was once burned, a `catch (Exception ex) { _logger.LogError(ex.Message); throw; }` copied from file to file, a global handler that returns `"An error occurred"` — and no *strategy*. A strategy answers three questions: **where do I catch, what do I log, and what do I surface?** Each answer depends on a prior question: what kind of failure is this? The logging rules above are the tool; this section decides where it is used.

### Classify the Failure First

You cannot decide how to handle a failure until you know what *kind* of failure it is. There are three, and they want three completely different mechanisms.

**A bug.** The code is wrong. A `NullReferenceException`, an `InvalidCastException`, an index off the end of an array, an `InvalidOperationException` because an object was in a state its own invariants say is impossible. There is no correct handling for a bug at runtime, because the process no longer knows what is true. The right response is to *not catch it*: let it tear down the current request, let the boundary log it with full fidelity, let the alert fire, and go fix the code. A `catch` around a bug converts a loud, diagnosable failure into a quiet, undiagnosable one.

**An environmental or transient failure.** A socket reset, a connection timeout, a SQL deadlock victim, a 503 from a dependency that is mid-deploy. Nothing is wrong with your code and nothing is wrong with the request — the world was briefly unavailable. These are the only failures where *retry* is a coherent response, because the same call with the same inputs may well succeed a moment later. This is Polly's territory: see [Resilience with Polly](#resilience-with-polly) in Chapter 5 for the `HttpClient` wiring, and [Chapter 20: Distributed Systems](#chapter-20-distributed-systems) for the wider retry, circuit-breaker and idempotency picture.

**A domain rule violation.** The request is well-formed, the system is healthy, and the business says no: insufficient balance, coupon expired, order already shipped. Nothing here is exceptional — this is one of the outcomes the feature was designed to produce. This is exactly where a `Result<T>` return value belongs ([Chapter 10](#result-pattern-railway-oriented-programming) builds one), and it is the category most often mishandled, because throwing an `InsufficientFundsException` *works*, so nobody notices that it has made an ordinary business outcome invisible in the method signature.

| Failure kind | Examples | Mechanism | What the caller sees |
|---|---|---|---|
| **Bug** | Null deref, bad cast, broken invariant | Do not catch. Let it reach the outermost boundary. | 500 + a trace id; an alert pages someone |
| **Environmental / transient** | Socket reset, timeout, SQL deadlock (1205), 503 | Retry with backoff, circuit-break, fall back | Success after retry — or 503/504 + `Retry-After` when exhausted |
| **Domain rule violation** | Coupon expired, insufficient balance, already shipped | `Result<T>` / validation errors — *not* an exception | 409 / 422 with a stable machine-readable code |
| **Malformed input** | Bad JSON, missing required field | Model validation at the edge, before your code runs | 400 `ValidationProblemDetails` |

> **The classification is the design decision.** Everything downstream — whether to retry, whether to log at `Error` or `Warning`, whether the user sees a fixable message or an apology — is determined by which row you are in. Teams that argue about `try`/`catch` placement are usually arguing because they never agreed on the rows.

### Exceptions vs Result, Settled Properly

A `Result<T>` is a return value that holds either the success value or an error, so an expected failure becomes part of the method's contract; the [Result pattern](#result-pattern-railway-oriented-programming) in Chapter 10 builds one and chains steps on it. The choice between the two deserves an honest statement of the trade, because "exceptions are slow" is repeated far more often than it is understood.

**Exceptions are unignorable** — their single greatest property. If a method throws and you write no handler, the failure propagates and something eventually notices. Compare a method returning `Result<T>`: a caller can write `_ = DoTheThing();` and discard the failure entirely, and the compiler will not blink. Unignorability is why exceptions are right for the *exceptional*, where continuing is worse than stopping. **Results, in exchange, are visible in the signature and force a decision.** `Result<Order> Place(...)` tells you failure is expected without reading the body; `Order Place(...)` does not. The cost is signature pollution: `Result<T>` is viral, spreading up through every caller, and code that mixes both conventions gets the worst of each.

Now the performance, with the mechanism rather than folklore. A throw/catch pair costs a few **microseconds** on .NET 10: about 4 µs when the catch is one frame above the throw, 8–10 µs ten frames up, 12–19 µs twenty frames up and about 30 µs fifty frames up. Returning a failure value through the same frames costs well under a microsecond. (Release build, no debugger, 4 vCPU Xeon, .NET 10.0.12; the program and its raw runs are in [`verify/measurements/ExceptionCost`](https://github.com/malyna2/dotnet-handbook/tree/main/verify/measurements/ExceptionCost).) Two things dominate. First, **the stack walk**: throwing does not simply jump. The runtime walks frames outward looking for a handler whose filter matches, unwinding as it goes, so the same `throw` is cheap in a leaf method and expensive from twenty frames down a request pipeline. Second, **the stack trace string**: while unwinding, the runtime records only the raw frames. It resolves them to method names and formats the text when something reads `StackTrace` or calls `ToString()`, and logging the exception does exactly that. In the same runs, catching *and* reading the trace cost 13 µs at one frame, 38–48 µs at ten and 150–165 µs at fifty, three to five times the bare throw.

Put that in context, because context is the whole point. Ten microseconds once per failed HTTP request, against a budget of tens of milliseconds, is *noise* — nobody has ever had an outage because a 404 threw. Ten microseconds per row across 200,000 rows is **two seconds of pure overhead**, and logging each one multiplies it again: that is a genuine, career-defining performance bug.

> **Best practice.** Use exceptions for the exceptional and for anything a caller must not be able to ignore. Use `Result<T>` for expected alternate outcomes — especially inside loops and hot paths, where the per-item cost of throwing is the thing that kills you. The tell for a misuse is a `try`/`catch` *inside* a `foreach`: that is control flow wearing an exception costume.

### Where to Catch

Here is the rule that replaces a thousand scattered `try` blocks: **catch only where you can add value — and there are exactly three ways to add value.**

- **Translate it.** Wrap a low-level exception into one that means something in your abstraction, so callers do not end up depending on `SqlException` or `HttpRequestException` leaking out of a repository. The interface promised an `IOrderRepository`; it should not fail in ADO.NET vocabulary. Always pass the original as the inner exception — translation preserves, it does not discard.
- **Handle it.** Actually do something: retry, fall back to a cache, compensate a half-finished workflow, degrade gracefully. If your `catch` block does not change the outcome, it is not handling anything.
- **Report it.** At the outermost boundary — the global exception handler, a message consumer's dispatch loop, a `BackgroundService`'s work loop — someone must turn the exception into a log entry and a response. This is the *only* place a blanket `catch (Exception)` is legitimate, because it is the last frame before the exception escapes into the void.

Everywhere else, do nothing — let it go up. Layers with nothing useful to add should be transparent to failure.

```
   request travels down  ▼            ▲  exception travels up

   Exception middleware ─────────────────►  REPORT: log once, map to ProblemDetails
        ▼                             ▲
   Controller / endpoint  ────────────┤    (nothing to add — transparent)
        ▼                             ▲
   Application service    ────────────┤    (nothing to add — transparent)
        ▼                             ▲
   Domain                 ────────────┤    (nothing to add — transparent)
        ▼                             ▲
   OrderRepository        ────────────┤    TRANSLATE: SqlException →
        ▼                             ▲               OrderStoreUnavailableException
   Polly pipeline         ────────────┤    HANDLE: retry if transient,
        ▼                             ▲            rethrow when exhausted
   SqlConnection ── throws SqlException(40613) ─┘
```

Three catch sites in a five-layer stack, each earning its place. The middle three layers contain no `try` at all — and that absence is the design, not an oversight.

```csharp
// TRANSLATE — at the infrastructure boundary.
public async Task<Order?> GetByIdAsync(int id, CancellationToken ct)
{
    try { return await _db.Orders.FirstOrDefaultAsync(o => o.Id == id, ct); }
    catch (SqlException ex) when (ex.Number is 40613 or 4060)   // database unavailable
    {
        // Callers depend on our abstraction, not on ADO.NET. Inner exception preserved.
        throw new OrderStoreUnavailableException(id, ex);
    }
}
```

> **Pitfall.** A `catch` whose body is `throw;` and nothing else is pure cost: it buys nothing and, before exception filters existed, it also cost you the unwound stack (see below). Delete it. Likewise, a `try`/`finally` with no `catch` is often exactly right — you want the cleanup, you do not want to intercept the failure. Better still, `using` expresses that intent in one line.

### The Mechanics That Bite

**`throw;` versus `throw ex;`.** `throw ex;` restarts the exception's journey from the current frame: the `StackTrace` is reset, and every frame *below* your catch — the ones containing the actual bug — is erased, so the log blames the catch block. `throw;` rethrows the original with its trace. `throw ex;` is never the right rethrow.

```csharp
catch (Exception ex)
{
    throw ex;   // ❌ stack trace now starts HERE — the real origin is erased
    throw;      // ✅ preserves the original trace
}
```

**Exception filters — and why they beat catch-inspect-rethrow.** `catch (X e) when (predicate)` looks like an `if` inside the catch, but the filter runs during the **first pass**, *before the stack unwinds*. If it returns `false`, the exception continues outward with the stack intact, so if it ends up unhandled, a debugger or crash dump sees the original throw site rather than your catch. Catch-inspect-rethrow unwinds first and asks questions later.

```csharp
// ✅ Filter: decides before unwinding. Frames below stay intact.
catch (SqlException ex) when (IsTransient(ex)) { await RetryAsync(); }

// ❌ Catch, inspect, rethrow: the stack has already unwound by the time we look.
catch (SqlException ex) { if (!IsTransient(ex)) throw; await RetryAsync(); }
```

Filters are also the idiomatic way to branch on an error code (`when (ex.Number == 1205)`), or to log without handling: `catch (Exception ex) when (Log(ex))`, where `Log` returns `false`.

**`ExceptionDispatchInfo`.** To capture an exception now and rethrow it later — on another thread, out of a stored task, after some bookkeeping — use `ExceptionDispatchInfo`: it keeps the original stack and *appends* the new throw site, where `throw capturedEx;` would reset it. It is also how `await` rethrows a faulted task's exception with its original trace.

```csharp
ExceptionDispatchInfo? captured = null;
try { await DoWorkAsync(); }
catch (Exception ex) { captured = ExceptionDispatchInfo.Capture(ex); }
await CleanupAsync();
captured?.Throw();      // original stack trace intact, rethrow site appended
```

**`AggregateException` and `Task.WhenAll`.** When three tasks in a `Task.WhenAll` fault, `await` rethrows only one of the exceptions, and the other two stay invisible unless you read the task's `Exception` property. It is a common source of "we fixed the error and it still fails": you were only ever shown one of three. [Exception Handling with WhenAll](#exception-handling-with-whenall) in Chapter 4 has the mechanism, which exception you get, and the logging pattern.

**`OperationCanceledException` is not a failure.** It is a *successful* response to a request to stop: the client closed the connection, a shutdown began. Logging it as an error trains your team to ignore errors, and in a busy API client disconnects alone can drown a real incident. Filter it at the boundary and log it at `Information` or `Debug`.

```csharp
catch (OperationCanceledException) when (ct.IsCancellationRequested)
{
    _logger.LogInformation("Request cancelled by client for order {OrderId}", id);
    return;   // no response — the caller is already gone
}
```

The filter matters: without `when (ct.IsCancellationRequested)` you also swallow *timeouts*, which are real problems worth surfacing.

### Designing Your Own Exceptions

**Have few of them.** A healthy bounded context has a handful of exception types, not one per error message. "One type per message" is a smell with a simple tell: every type is thrown from exactly one line and caught nowhere. Types exist so that a *caller can catch them differently*; if no caller ever will, the distinction belongs in the data, not in the class hierarchy.

**Name them for the situation, not the throw site.** `OrderStoreUnavailableException` describes a condition a caller can reason about; `OrderRepositoryGetByIdFailedException` describes a line of your code, which is what the stack trace is for. And **carry structured data as properties, not baked into a string** — a message is for humans, while the properties are what your handler, your logs, and your API response actually consume. Formatting the order id into the message and then regex-ing it back out at the boundary is a real pattern in real codebases, and it is always a mistake.

```csharp
// A small, per-context base so the boundary can catch one type.
public abstract class OrderingException : Exception
{
    protected OrderingException(string message, Exception? inner = null) : base(message, inner) { }
    /// Stable, machine-readable code surfaced to API clients.
    public abstract string ErrorCode { get; }
}

public sealed class OrderStoreUnavailableException : OrderingException
{
    public int OrderId { get; }                       // structured, queryable
    public override string ErrorCode => "order_store_unavailable";
    public OrderStoreUnavailableException(int orderId, Exception inner)
        : base($"The order store was unavailable while loading order {orderId}.", inner)
        => OrderId = orderId;
}
```

The common base per bounded context earns its place when the boundary wants one `catch (OrderingException ex)` that maps `ErrorCode` and a status onto a response, instead of a growing list of `catch` clauses. Keep the base *thin* — a marker plus the shared contract — and resist the urge to make every exception in the system inherit from one god-base, which just recreates `catch (Exception)` with extra ceremony.

### What to Log

**Log the exception exactly once, at the boundary that handles it, with the context the stack trace does not already carry.** The trace already knows the type, the message, and every frame. What it does not know is *which order*, *which tenant*, *which correlation id* — and that is the only information worth adding.

```csharp
// ✅ The exception is the first argument — that is what preserves it as a
// structured field with full type/message/stack/inner-exception detail.
_logger.LogError(ex, "Failed to place order for customer {CustomerId} (cart {CartId})",
                 customerId, cartId);

// ❌ The exception becomes a flat string; inner exceptions and stack are lost.
_logger.LogError($"Failed to place order: {ex.Message}");
```

That first argument is not a stylistic preference. Logging providers treat the exception parameter specially, serializing type, message, stack, and the full inner-exception chain as structured data; interpolating `ex.Message` into the template throws all of that away and, for good measure, destroys the message template that makes logs aggregatable. It is the [structured-logging](#why-structured-beats-string-logging) rule from the start of this chapter, applied to the exception.

**The log-and-rethrow anti-pattern.** This is the most common exception mistake in enterprise .NET:

```csharp
// In the repository, the service, the handler, AND the controller — all four:
catch (Exception ex) { _logger.LogError(ex, "Something went wrong"); throw; }
```

One failure now produces four `Error` entries. They are not four problems and they are not even four *views* of the problem — they are the same exception, at four different stack depths, with four different timestamps, interleaved with other requests' logs. On-call now has to reconstruct that these four are one event before they can start. Your error rate metric is inflated fourfold. Your alert thresholds are meaningless. And the extra entries added no information the boundary's single entry would not have had, because the exception was already carrying its whole stack.

> **Best practice.** Log where you *handle*, not where you *pass through*. If a layer genuinely knows something the boundary cannot — a retry attempt count, the exact query that failed — log that as a `Warning` with the specific fact, and still let the boundary own the single `Error` for the failure itself.

> **Pay attention.** **Reading the exception you logged.**
>
> `LogError(ex, …)` records `ex.ToString()`: the outer type and message, then each inner exception after ` ---> `, each followed by its own frames and `--- End of inner exception stack trace ---`, then the outer exception's frames. The root cause is the innermost exception, so read the deepest `--->` first.
>
> - **A trace is the path the exception travelled.** The top frame is where it was thrown, each frame below is the caller of the one above, and the last frame is the one that caught it: the trace grows as the stack unwinds and stops at the catch, so it is not the whole call stack.
> - **`--- End of stack trace from previous location ---`** marks a capture and rethrow through `ExceptionDispatchInfo`, which is what every `await` of a faulted task does: above the line is where it failed, below is where it was awaited.
> - **Frames can be missing or late.** `throw ex;` drops everything below the rethrow. Release builds inline small methods into their callers, so an inlined method has no frame, and line numbers appear only when the `.pdb` is deployed with the assembly.
>
> Read down to the first frame in your own code: frames above it are the library that threw, frames below it are how you got there.

### What to Surface: ProblemDetails

The response to the outside world is a **product decision**, not a debugging artifact. Never surface a stack trace, a SQL statement, a connection string, an internal type name, or raw inner-exception text: at best it confuses the caller, at worst it is a reconnaissance gift to an attacker (see [A05: Security Misconfiguration](#a05-security-misconfiguration) in Chapter 12).

What a caller does need is: **a stable machine-readable code** they can branch on, **a human-readable summary** they can act on, and **a trace id** they can quote to your support team. Every API needs *one* error shape to carry them, and **ProblemDetails** is the standard: RFC 7807, since replaced by RFC 9457, which the current ASP.NET Core docs cite. It is a JSON object with `type`, `title`, `status`, `detail` and `instance`, plus any extension members, so clients and tools parse every error the same way. ASP.NET Core produces it natively:

```csharp
builder.Services.AddProblemDetails();

app.UseExceptionHandler(); // With AddProblemDetails, emits RFC 7807 on unhandled errors.
```

To map your own exceptions, implement `IExceptionHandler` (a clean, testable seam) rather than stuffing logic into middleware. This one maps the `OrderingException` family from above and writes through `IProblemDetailsService`, so the response gets the same defaults, and the same `traceId`, as every other error the app returns:

```csharp
public sealed class OrderingExceptionHandler(
    IProblemDetailsService problemDetails, ILogger<OrderingExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext ctx, Exception ex, CancellationToken ct)
    {
        if (ex is not OrderingException ordering) return false;  // let the generic 500 handler take it
        logger.LogError(ex, "Ordering failure {ErrorCode} on {Path}",
                        ordering.ErrorCode, ctx.Request.Path);

        ctx.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = ctx,
            Exception = ex,
            ProblemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status503ServiceUnavailable,
                Title  = "The order could not be processed.",
                Type   = $"https://errors.example.com/{ordering.ErrorCode}",
                // No stack, no SQL, no inner message: a code, plus the traceId the writer adds.
                Extensions = { ["code"] = ordering.ErrorCode }
            }
        });
    }
}
// builder.Services.AddExceptionHandler<OrderingExceptionHandler>();
```

A FluentValidation `ValidationException` maps the same way, to a 400 `ValidationProblemDetails`; validation at the edge, before your code runs, is in [Model Binding & Validation](#model-binding-validation) in Chapter 5.

> **Tip — `AddExceptionHandler` vs writing your own exception middleware.** `UseExceptionHandler()` *is* the middleware; `AddExceptionHandler<T>()` registers handlers it calls in registration order until one returns `true`, and anything unhandled falls through to the default ProblemDetails response. A hand-rolled `try/catch` middleware makes you own what the built-in one already does: status 500 and cleared cache headers, a response that has already started, content negotiation through `IProblemDetailsService`, and the logs and metrics tooling expects. `IExceptionHandler` classes are plain DI services, testable without `RequestDelegate` plumbing, one class per exception family. Keep custom middleware for work that isn't "map this exception to a response", or for targets before .NET 8, where the `UseExceptionHandler(errorApp => ...)` overload fills the role.

The status code carries the most important piece of information, so choose it deliberately. The dividing line is **who can fix this**: 4xx means the caller can change something and succeed; 5xx means only you can. Getting this wrong is expensive in both directions — 500s for user mistakes wake up your on-call for nothing, and 200s or 400s for server faults hide real outages from your dashboards and stop clients from retrying.

| Situation | Status | Why |
|---|---|---|
| Malformed or missing input | **400** | The caller can fix the request |
| Well-formed but business-unacceptable | **422** | Syntax is fine; semantics are not |
| Optimistic-concurrency conflict | **409** | The caller can re-read and retry — this is a real outcome, not a bug |
| Domain rule violation on a valid request | **409 / 422** | Pick one per API and be consistent; carry the code in the body |
| Bug in your code | **500** | Nothing the caller can do; page someone |
| Dependency down / retries exhausted | **503** (+ `Retry-After`) | Transient; tells clients it is worth trying again |
| Client cancelled / disconnected | **no response** | The caller is gone. Do not manufacture a 500 for a socket nobody is reading |

> **Pay attention.** **What reaches the caller, and what reaches the log.**
>
> - **The trace id goes out by default.** The default ProblemDetails writer adds `traceId` (`Activity.Current?.Id`, else `HttpContext.TraceIdentifier`) to every body it writes, so write your responses through `IProblemDetailsService` rather than `WriteAsJsonAsync` and the caller always has the key that finds your log entry.
> - **The stack trace goes out only through the environment.** In Development, `WebApplication` adds the developer exception page itself. A client that doesn't ask for HTML gets a ProblemDetails whose `exception` field holds `ex.ToString()` and *every request header*, `Authorization` included. A production container started with `ASPNETCORE_ENVIRONMENT=Development` serves that to anyone. Pin the environment in deployment, and never call `UseDeveloperExceptionPage` unconditionally.
> - **The middleware logs too.** It writes its own `Error` entry for each exception it catches. On .NET 10 it skips that entry when an `IExceptionHandler` returned `true` (`ExceptionHandlerOptions.SuppressDiagnosticsCallback` changes the rule); before .NET 10 it always writes it, so a handler that also logs records every failure twice.
>
> Put `detail` text that is safe to show a client; the details belong in the one log entry the `traceId` points to.

### Process-Level Safety Nets

**`BackgroundService`.** Since .NET 6, an unhandled exception in `ExecuteAsync` stops the **entire host** by default (`BackgroundServiceExceptionBehavior.StopHost`) — a deliberate change, because the previous behavior silently killed the service and left the process running as a hollow shell that looked healthy to every probe. Keep that default and put your `try`/`catch` *inside* the loop, so one bad message does not take down the worker while a genuinely broken worker still takes down the host and lets the orchestrator restart it. The `QueueProcessor` in [The Generic Host and Background Services](#the-generic-host-and-background-services) (Chapter 3) has exactly that shape: the loop body is the per-item boundary. Filter its cancellation catch with `when (stoppingToken.IsCancellationRequested)`, for the reason given under *The Mechanics That Bite*. [Part A — Background Processing in .NET](#part-a-background-processing-in-net) in Chapter 11 covers the worker itself.

**`AppDomain.CurrentDomain.UnhandledException`** fires for exceptions escaping any thread. It is a *last-chance logger*, not a handler: you cannot prevent the process from terminating, and you have limited time before it dies — use it to flush a final log entry, nothing more. **`TaskScheduler.UnobservedTaskException`** fires when a faulted `Task` is garbage-collected without anyone having observed its exception. Since .NET 4.5 that no longer crashes the process, which means these failures are entirely silent by default; subscribing to the event is one of the highest-value ten-line additions you can make to a service, because it is how you discover the fire-and-forget `_ = DoWorkAsync();` calls that have been failing in production for months.

> **Pitfall.** A top-level `catch (Exception) { }` that swallows and continues is worse than a crash. A crashed process is unambiguous: the orchestrator restarts it, the health check fails, the alert fires, and someone looks. A process that keeps running while lying about its state corrupts data quietly, reports itself healthy, and produces the kind of outage that takes three days to diagnose because the logs are clean. **Fail loudly or handle genuinely — never in between.**

### A Reviewer's Checklist

Run down this list on any pull request that touches error handling:

- [ ] Every `catch` **translates**, **handles**, or **reports**. If it does none of the three, delete it.
- [ ] No `throw ex;` anywhere — only `throw;` or a new exception with the original as `InnerException`.
- [ ] No empty `catch { }`, and no `catch (Exception)` outside an outermost boundary.
- [ ] Expected business outcomes return `Result<T>` or a validation error, not an exception — and nothing throws inside a hot loop.
- [ ] `OperationCanceledException` is filtered out of error logging and never becomes a 500.
- [ ] The exception is passed as the **first argument** to `LogError`, never interpolated into the message.
- [ ] The failure is logged **once**, at the boundary — no log-and-rethrow chains.
- [ ] The response is a `ProblemDetails` with a stable code and a trace id; no stack trace, SQL, or inner-exception text escapes.
- [ ] The status code answers "who can fix this?" — 4xx caller, 5xx you.
- [ ] Custom exceptions carry structured properties, are named for the situation, and there are few of them.
- [ ] `TaskScheduler.UnobservedTaskException` is subscribed somewhere in the host.

## Why Observability, and How It Differs from Monitoring

Everything so far ends a failure in one log entry and a response carrying a trace id. The rest of the chapter is about that id and the telemetry around it, because in production you cannot attach a debugger, step through a request as it hops across five services, or ask a customer to reproduce the bug while you watch: what the running system emits is your only window into it.

The words *monitoring* and *observability* are often used interchangeably, but the distinction matters and it reveals a shift in how we operate systems.

**Monitoring** answers questions you already knew to ask. You decide in advance that CPU above 90% is bad, that a 500-error rate above 1% is bad, and you build dashboards and alerts for those known failure modes. Monitoring is a checklist of "known unknowns."

**Observability** is the property of a system that lets you ask *new* questions without shipping new code. It is about handling "unknown unknowns" — the failure you never anticipated. When a customer reports that checkout is slow, but only for users who paid with a specific gateway, only on mobile, only after 6 p.m., you did not build a dashboard for that. An observable system carries enough rich, high-cardinality data that you can slice and pivot your way to the answer after the fact.

> **Key distinction:** Monitoring tells you *that* something is wrong. Observability helps you understand *why* it is wrong. You need both, but as systems grow more distributed, observability becomes the dominant concern.

The foundation of observability rests on **three pillars**: **logs**, **metrics**, and **traces**. Each answers a different kind of question, and their real power emerges when you correlate them.

- **Logs** are discrete, timestamped records of events. "User 42 failed authentication at 14:03:11." They are the narrative detail.
- **Metrics** are numeric measurements aggregated over time. "Requests per second," "p99 latency," "queue depth." They are cheap to store and perfect for trends and alerting.
- **Traces** follow a single request as it travels through your system, showing where time was spent across service boundaries. They are the story of one journey.

Think of it this way: metrics are the vital signs on the patient monitor (heart rate, blood pressure), logs are the doctor's detailed notes on each symptom, and a trace is the timeline of the patient's entire visit from admission to discharge. A good diagnostician uses all three. This chapter wires them up and joins them by the trace id; [Chapter 25: Observability and Testing at Scale](#chapter-25-observability-and-testing-at-scale) designs metrics, custom spans and alerts.

## Observability Wiring

ASP.NET Core is instrumented *out of the box*. Kestrel and the hosting layer emit metrics (request rate, duration, active connections) through `System.Diagnostics.Metrics` and the older EventCounters, and every request runs inside an `Activity` — .NET's native distributed-tracing span. The instrumentation is always there; what's missing by default is something *listening*. That's what OpenTelemetry provides:

```csharp
builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("shop-api"))
    .WithTracing(t => t
        .AddAspNetCoreInstrumentation()      // Span per incoming request.
        .AddHttpClientInstrumentation()      // Span per outbound call.
        .AddOtlpExporter())                  // Ship to your collector.
    .WithMetrics(m => m
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddOtlpExporter());
```

With those few lines, every request produces a trace: a root span from the ASP.NET Core instrumentation, child spans for each `HttpClient` call, all exported over OTLP to whatever backend you run (Jaeger, Tempo, an APM vendor — the wire format is standard). Better still, propagation is automatic: `HttpClient` injects the **W3C trace-context** `traceparent` header on outbound calls and ASP.NET Core reads it on inbound ones, so when service A calls service B, both ends land in the *same* trace without either team writing propagation code. Combine that with the [`IHttpClientFactory`](#ihttpclientfactory-resilience-with-polly) discipline from Chapter 5 and a slow endpoint stops being a mystery — the trace shows you exactly which downstream call ate the time budget.

The point is that the web framework *participates natively*: a few lines in `Program.cs` and every request carries a trace. Custom `ActivitySource` spans and metrics design are Part 2 material, in Chapter 25; the next sections put the trace id to work in your logs.

## Centralized Logging

In a fleet of containers, SSHing into a box to `tail` a log file is hopeless — the box may already be gone. Centralized logging ships every log to one searchable place.

- **ELK / Elastic Stack** — Elasticsearch (storage and search), Logstash (ingestion and transformation), and Kibana (visualization). It is powerful and battle-tested but operationally heavy; running Elasticsearch at scale is a job in itself.
- **Loki** — Grafana's log aggregation system, designed to be cheaper than ELK by indexing only labels (not the full log text) and storing the rest compressed in object storage. It pairs naturally with Prometheus and Grafana for a unified pane of glass.
- **Seq** — a logging server built specifically for structured logs, and a joy for .NET developers. It understands Serilog's structured events natively, so you can query with real filters (`Amount > 50 and PaymentMethod = 'stripe'`), build dashboards, and set alerts, all with almost zero setup. For a .NET team, Seq is often the fastest path to genuinely useful structured logging, especially in development and small-to-mid production systems.

## Correlation Across Services

The logging, the exception boundary and the tracing above all serve one goal: given a single symptom, reconstruct the whole story. That requires **correlation** — the ability to jump from a metric spike to the exact traces behind it, and from a trace to the exact logs of each span.

The unifying key is the **trace ID**. W3C Trace Context propagates it over HTTP, and `Activity.Current` holds it for the code handling the request, so logs only need to record it. With `Microsoft.Extensions.Logging` under the generic host this is on by default: the host sets `ActivityTrackingOptions` to `TraceId | SpanId | ParentId`, which adds them to every entry's scope (a provider prints scopes only if configured to, such as the console's `IncludeScopes`). Current Serilog versions read `Activity.Current` themselves and store `TraceId` and `SpanId` on every event; older code used the `Serilog.Enrichers.Span` package for this. To add it by hand:

```csharp
using (LogContext.PushProperty("TraceId", Activity.Current?.TraceId.ToString()))
{
    logger.LogError(ex, "Payment failed for order {OrderId}", order.Id);
}
```

Now a single trace ID lets you pivot: see the slow trace in Jaeger, copy its ID, paste it into Seq, and read every log line from every service for that exact request. This is the payoff of observability.

**Messaging** needs the same discipline, and whether the framework helps depends on the client library. A broker stores bytes and properties; the trace context crosses a queue only if the producer's client writes it into the message and the consumer's client reads it back. The Azure Service Bus SDK does both. On send it writes the current activity's W3C id into the message's `Diagnostic-Id` application property (and into `traceparent` as well once activity-source tracing is on), and `ServiceBusProcessor` makes that context the parent of its processing span. Its tracing is still experimental, so OpenTelemetry sees those spans only after `AppContext.SetSwitch("Azure.Experimental.EnableActivitySource", true)` and `AddSource("Azure.Messaging.ServiceBus.*")`. With a client that doesn't propagate (check yours, for RabbitMQ and Kafka alike) or a hand-rolled transport, you **inject** the trace context into the message headers, and the consumer **extracts** it to continue the trace:

```csharp
// Producer
var propagator = Propagators.DefaultTextMapPropagator;
propagator.Inject(
    new PropagationContext(Activity.Current!.Context, Baggage.Current),
    message.ApplicationProperties,
    (props, key, value) => props[key] = value);
```

The consumer extracts the same context and starts its span as a child of the producer's. Skip it and traces break at every queue.

> **Pay attention.** **Why the trace id beats a home-made correlation id.**
>
> A custom `X-Correlation-ID` header travels only as far as code copies it: the middleware earlier in this chapter reads it and pushes it into the log context, but an outgoing `HttpClient` call doesn't send it unless a `DelegatingHandler` adds it, so the chain breaks at the first service that forgot. The trace id needs no such code over HTTP: it lives in `Activity.Current`, which flows with the async call chain, `HttpClient` writes it into `traceparent`, ASP.NET Core starts the next request's activity as its child, and logs and spans record the same id. Use the trace id as the correlation id — return it to clients (ProblemDetails does, as `traceId`) and search logs by it. Keep a business key, such as an order id, as an ordinary log property; it identifies the order, not the request.

## Health Checks: The Tie-In

During diagnosis, a readiness check's verdict is telemetry too: a rising count of failing readiness checks names the dependency that is degrading, often before the error rate moves, so publish the results where your dashboards and alerts can see them. How ASP.NET Core runs the checks and keeps liveness apart from readiness is in [Health Checks](#health-checks) in Chapter 5.

## Diagnosing a Performance Problem (a worked methodology)

Not every incident throws. When the complaint is "it's slow", the discipline is the same as for an exception: let the system tell you what happened before you change anything. The method below is phrased as the interview question it usually arrives as.

**Walk me through how you diagnose a slow endpoint.**
1. **Reproduce and quantify.** Get a percentile (p95/p99), a throughput figure and the conditions (endpoint, payload, load): "slow" is not a number.
2. **Measure, don't guess.** The cost usually hides where nobody looked (a serializer, a logging call, a chatty ORM), and a baseline is what proves a fix helped.
3. **Classify the bottleneck.** CPU, memory/GC, disk I/O, network, database, or lock contention: each has its own tools and fixes.
4. **Go from cheap metrics to expensive profilers.** Always-on signals first (APM dashboards, `dotnet-counters`), then `dotnet-trace` (sampled thread stacks), `dotnet-dump` (heap, thread stacks) and query plans once you've narrowed the suspect.
5. **Fix one thing, verify, repeat.** Change a single variable and re-measure against the baseline.

**Red flag:** "I'd add caching and make everything async": naming fixes before measuring anything is optimizing on a guess.

**How do you tell if it's CPU-bound vs waiting?**
Check CPU while the endpoint is slow. High CPU with low throughput → CPU-bound (hot loop, serialization, regex, crypto). Low CPU but high latency → *waiting* (DB, downstream HTTP, a lock, or a starved thread pool).

> **Pay attention.** **Starvation looks like a slow dependency: how to tell them apart.**
>
> Sync-over-async parks pool threads while the continuations that would free them queue behind new requests, and the pool adds threads slowly ([The Sync-Over-Async Deadlock](#the-sync-over-async-deadlock) in Chapter 4 has the mechanism). Three signals give it away:
>
> - **Counters.** Queue length grows and thread count climbs steadily while CPU stays low. In `dotnet-counters`, read them as `ThreadPool Queue Length` and `ThreadPool Thread Count` (`threadpool-queue-length`, `threadpool-thread-count`): on .NET 8 they are the default `System.Runtime` view, on .NET 9 and 10 pass `--counters 'EventCounters\System.Runtime'`. The newer `dotnet.thread_pool.queue.length` and `dotnet.thread_pool.thread.count` are published as *monotonic* counters on .NET 9 and 10, so `dotnet-counters` shows their change per second, not the value: a pool that has grown to dozens of threads reads a small number each second, and a draining queue reads negative. The runtime's main branch already declares both as up-down counters. Before you trust a dashboard built on them, check whether it plots the value or a rate.
> - **Every endpoint slows down**, including ones that never call the slow dependency. With async code, a slow dependency delays only its own callers.
> - **Stacks.** `dotnet-stack report` (or `parallelstacks` in `dotnet-dump analyze`) shows most pool threads parked in `Task.InternalWait`.
>
> Fix the blocking call. Raising `ThreadPool.SetMinThreads` only moves the cliff.

**Which tools, concretely, in a .NET app?**
- `dotnet-counters monitor`: CPU, GC counts, allocation rate, thread-pool queue length and thread count. Add `Microsoft.AspNetCore.Hosting` to `--counters` for request metrics. First stop, zero setup.
- `dotnet-trace`: sampled stacks of every thread, to find hot or blocked methods without a full profiler.
- `dotnet-dump` / `dotnet-gcdump`: heap snapshots for leaks and retention; `dotnet-dump` also has every thread's stack.
- APM (Application Insights, OpenTelemetry, Datadog): distributed traces show *which hop* in a request eats the time.
- DB: `EXPLAIN`/`EXPLAIN ANALYZE` (Postgres), the actual execution plan (SQL Server), the slow-query log.

[Profiling](#profiling-finding-the-bottleneck-in-a-running-system) in Chapter 17 runs each of these tools against a live process.

**What are the usual culprits you look for?**
- **N+1 queries**: a loop issuing one query per row. Fix with a join, `Include` or a batched load.
- **Missing or unusable index**: a scan where a seek should be; the plan shows it.
- **Sync-over-async** (`.Result`, `.Wait()`): starves the thread pool, as above.
- **Excess allocations / GC pressure**: a high gen-0 rate and frequent gen-2 collections. Fix with pooling, `Span<T>`, and fewer LINQ allocations in hot paths.
- **Chatty network calls**: many small serial round-trips. Batch or parallelize them.
- **No caching**: recomputing or re-fetching identical results on every request.

> **Follow-up:** *The p99 is bad but p50 is fine — what does that tell you?* Something intermittent: GC pauses, lock contention, a cold cache, a slow downstream that only some requests hit, or connection-pool exhaustion under bursts. A fine median with a bad tail points at contention or resource limits, not raw algorithmic cost.

**The DB is the bottleneck — now what?**
Pull the execution plan for the slow query ([Execution Plans](#execution-plans) in Chapter 7; [Chapter 19](#chapter-19-the-slow-query-lab-reading-execution-plans) is a lab on reading them). Look for scans that should be seeks (a missing index, or one the predicate can't use because it wraps the column in a function or forces an implicit conversion), bad join order from stale statistics, or far more rows than the caller needs. Then consider, cheapest first: indexing, a query rewrite, pagination, caching, read replicas.

## Prove it

Predict each output before you run it.

**`throw;` keeps the frame that threw; `throw ex;` erases it.**

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

**A template keeps a typed property; interpolation flattens it.**

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
