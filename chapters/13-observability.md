# Chapter 13: Observability

_⏱️ Estimated read time: ~27 min ·     4348 words (study pace)_

Imagine you are the pilot of a modern aircraft. You cannot see the engines, you cannot feel the air pressure at 35,000 feet with your bare skin, and you certainly cannot inspect every one of the thousands of moving parts in real time. Yet you fly with confidence. Why? Because in front of you sits a cockpit full of instruments: altimeters, fuel gauges, temperature readouts, and warning lights that scream at you the moment something drifts out of tolerance. The aircraft is a black box, but the instruments make it *observable*.

A distributed .NET system is your aircraft. In production, you cannot attach a debugger, you cannot step through a request as it hops across five microservices, and you cannot ask a customer in Tokyo to reproduce the bug while you watch. Your only window into the running system is the telemetry it emits. This chapter is about building a cockpit for your software so that when something goes wrong at 3 a.m., you can diagnose it in minutes instead of guessing for hours.

## Why Observability, and How It Differs from Monitoring

The words *monitoring* and *observability* are often used interchangeably, but the distinction matters and it reveals a shift in how we operate systems.

**Monitoring** answers questions you already knew to ask. You decide in advance that CPU above 90% is bad, that a 500-error rate above 1% is bad, and you build dashboards and alerts for those known failure modes. Monitoring is a checklist of "known unknowns."

**Observability** is the property of a system that lets you ask *new* questions without shipping new code. It is about handling "unknown unknowns" — the failure you never anticipated. When a customer reports that checkout is slow, but only for users who paid with a specific gateway, only on mobile, only after 6 p.m., you did not build a dashboard for that. An observable system carries enough rich, high-cardinality data that you can slice and pivot your way to the answer after the fact.

> **Key distinction:** Monitoring tells you *that* something is wrong. Observability helps you understand *why* it is wrong. You need both, but as systems grow more distributed, observability becomes the dominant concern.

The foundation of observability rests on **three pillars**: **logs**, **metrics**, and **traces**. Each answers a different kind of question, and their real power emerges when you correlate them.

- **Logs** are discrete, timestamped records of events. "User 42 failed authentication at 14:03:11." They are the narrative detail.
- **Metrics** are numeric measurements aggregated over time. "Requests per second," "p99 latency," "queue depth." They are cheap to store and perfect for trends and alerting.
- **Traces** follow a single request as it travels through your system, showing where time was spent across service boundaries. They are the story of one journey.

Think of it this way: metrics are the vital signs on the patient monitor (heart rate, blood pressure), logs are the doctor's detailed notes on each symptom, and a trace is the timeline of the patient's entire visit from admission to discharge. A good diagnostician uses all three.

## Structured Logging

### Why Structured Beats String Logging

Most developers start with logs like this:

```csharp
logger.LogInformation($"User {userId} placed order {orderId} for {amount:C}");
```

This produces a human-readable string: `User 42 placed order 9981 for $59.99`. It looks fine until you have ten million of these lines and you need to answer "what is the total order value for user 42 today?" Now you are writing fragile regular expressions to parse text you never designed to be parsed.

**Structured logging** treats a log entry as a set of key-value properties, not a flat string. Instead of baking values into text, you keep them as named fields:

```csharp
logger.LogInformation("User {UserId} placed order {OrderId} for {Amount}", userId, orderId, amount);
```

Note the crucial difference: those are **not** string interpolation placeholders (`$"..."`). They are **message template** tokens. The logging framework captures `UserId`, `OrderId`, and `Amount` as separate, typed properties attached to the event. The rendered message is still `User 42 placed order 9981 for 59.99`, but the underlying event is now a queryable object. In a log store you can write `UserId = 42 AND Amount > 50` as a real query, no regex required.

> **Best practice:** Always use message templates with named placeholders, never string interpolation, in log calls. `LogInformation($"...")` throws away all structure and defeats the purpose. Enable the analyzer `CA2254` to catch this.

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

### Log Levels: A Shared Vocabulary

Log levels are not decoration; they are the primary control for signal-to-noise ratio. Use them deliberately:

- **Trace / Verbose** — extremely detailed diagnostic flow, usually off in production.
- **Debug** — internal state useful during development or targeted troubleshooting.
- **Information** — normal, noteworthy business events: "order placed," "user registered." The heartbeat of your application.
- **Warning** — something unexpected happened but the system recovered or degraded gracefully: a retry succeeded, a cache missed, a deprecated path was hit.
- **Error** — an operation failed and a user or process was affected. A caught exception that broke a request.
- **Critical / Fatal** — the application or a major subsystem is unusable. Database unreachable, out of memory.

> **Pitfall:** Logging everything at `Information` (or worse, logging exceptions at `Information`) makes levels meaningless. When every line looks equally important, alert fatigue sets in and real errors drown. Reserve `Error` for genuine failures a human might need to act on.

### What Not to Log: Secrets and PII

This is a discipline that separates senior engineers from juniors.

> **Critical pitfall:** Never log passwords, API keys, connection strings, bearer tokens, full credit card numbers, government IDs, or personal data like full names, emails, or addresses unless you have a lawful basis and proper redaction. Logs are frequently shipped to third-party systems, retained for months, and accessible to broad audiences. A logged secret is a leaked secret.

Concrete defenses:

- When destructuring objects with `{@Object}`, they may contain sensitive fields. Configure a Serilog destructuring policy or `[NotLogged]`-style attributes to strip them.
- Log a hashed or masked version instead: `****1234` for a card, or a stable pseudonymous user ID instead of an email.
- Under GDPR and similar regimes, personal data in logs is subject to retention and deletion rules. The safest log is one that contains no PII at all.

### NLog, Briefly

NLog is the other mature structured logging library for .NET. It is configuration-file-driven (XML `nlog.config`) by tradition, with "targets" (equivalent to Serilog sinks) and "rules" that route loggers to targets by name and level. It also supports structured properties via the same `{Name}` template syntax through the `Microsoft.Extensions.Logging` bridge. Functionally the two are close; Serilog's fluent C# configuration and richer ecosystem of sinks have made it the more common choice in greenfield .NET projects, but NLog remains excellent and slightly faster in some file-logging benchmarks. Pick one and standardize.

> **Modern note:** The **OpenTelemetry Logs signal is now stable in .NET**. An `ILogger` → OpenTelemetry bridge — wired via `AddOpenTelemetry().WithLogging(...)`, sitting right alongside `.WithTracing()` and `.WithMetrics()` — lets your existing `ILogger` calls flow straight out over OTLP, completing the "one SDK for logs, metrics, and traces" story you meet later in this chapter.

## Metrics

If logs are the narrative, metrics are the numbers you graph. They are aggregated, low-cost, and ideal for answering "how much" and "how fast" over time.

### The Three Instrument Types

- **Counter** — a monotonically increasing value. Total requests served, total orders placed, total bytes sent. You never decrease it; you ask about its *rate of change*. "Requests per second" is the derivative of a request counter.
- **Gauge** — a value that goes up and down and represents a current state. Active connections, queue depth, memory in use, temperature. You sample its current value.
- **Histogram** — records the distribution of a set of values, bucketed. Request duration is the classic case. A histogram lets you compute percentiles: p50, p95, p99. Averages lie; percentiles tell the truth. An average latency of 100 ms can hide a p99 of 4 seconds affecting your most valuable customers.

> **Best practice:** Alert on percentiles, not averages. The p99 latency is what your unhappiest 1% of users actually experience, and that 1% is often the difference between a renewal and a churn.

### System.Diagnostics.Metrics

Modern .NET ships a first-class, vendor-neutral metrics API in `System.Diagnostics.Metrics`. It is the API that OpenTelemetry consumes directly, so instrumenting with it is future-proof.

```csharp
using System.Diagnostics.Metrics;

public class OrderMetrics
{
    private readonly Counter<long> _ordersPlaced;
    private readonly Histogram<double> _orderProcessingDuration;
    private readonly UpDownCounter<long> _ordersInFlight;

    public OrderMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create("MyShop.Orders");

        _ordersPlaced = meter.CreateCounter<long>(
            "orders.placed", unit: "{orders}", description: "Total orders placed");

        _orderProcessingDuration = meter.CreateHistogram<double>(
            "orders.processing.duration", unit: "ms", description: "Order processing time");

        _ordersInFlight = meter.CreateUpDownCounter<long>(
            "orders.in_flight", description: "Orders currently being processed");
    }

    public void OrderPlaced(string paymentMethod)
        => _ordersPlaced.Add(1, new KeyValuePair<string, object?>("payment.method", paymentMethod));

    public void RecordProcessing(double ms) => _orderProcessingDuration.Record(ms);
    public void Begin() => _ordersInFlight.Add(1);
    public void End() => _ordersInFlight.Add(-1);
}
```

A few senior-level notes. Register the class as a singleton and inject `IMeterFactory` (the DI-friendly way introduced in .NET 8) rather than newing up a `Meter` yourself, so the framework manages disposal and testing. The extra `KeyValuePair` arguments are **tags** (also called dimensions or labels): they let you break the counter down by `payment.method`, `region`, or `status`. 

> **Pitfall — cardinality explosion:** Tags multiply the number of stored time series. Never use unbounded values like user ID, order ID, or raw URLs as tag values. A metric tagged with a million user IDs becomes a million time series and will bankrupt your metrics backend. High-cardinality data belongs in logs and traces, not metrics.

For gauges observed on demand (like queue depth), use an **observable** instrument that the runtime polls:

```csharp
meter.CreateObservableGauge("orders.queue.depth", () => _queue.Count);
```

### Prometheus and Grafana

**Prometheus** is the dominant open-source metrics database. Its model is *pull-based*: your application exposes a `/metrics` HTTP endpoint in a simple text format, and Prometheus scrapes it every few seconds. **Grafana** sits on top as the visualization layer, querying Prometheus with its query language, PromQL, to draw dashboards.

Wiring .NET metrics to Prometheus is trivial with OpenTelemetry:

```csharp
builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics => metrics
        .AddMeter("MyShop.Orders")
        .AddAspNetCoreInstrumentation()
        .AddRuntimeInstrumentation()
        .AddPrometheusExporter());

// exposes GET /metrics for Prometheus to scrape
app.MapPrometheusScrapingEndpoint();
```

`AddRuntimeInstrumentation()` gives you GC pauses, thread pool queue length, and heap sizes for free — invaluable signals that most teams forget to collect.

### RED and USE: Two Methods for Choosing What to Measure

Faced with infinite things you *could* measure, two frameworks tell you what you *should*.

The **RED method** is for request-driven services (your APIs):
- **Rate** — requests per second.
- **Errors** — failed requests per second.
- **Duration** — the distribution of request latencies.

Track RED for every endpoint and you can spot almost any user-facing problem.

The **USE method** is for resources (CPU, memory, disks, connection pools):
- **Utilization** — the percent of time the resource was busy.
- **Saturation** — the amount of queued work the resource cannot yet handle.
- **Errors** — error events for that resource.

RED tells you the *symptom* (requests are slow); USE helps you find the *cause* (the database connection pool is saturated). Use them together.

## Distributed Tracing

In a monolith, a stack trace tells you the whole story. In a microservice architecture, a single user click might touch an API gateway, an orders service, a payments service, an inventory service, and three databases. When it is slow, *which hop* was slow? Distributed tracing answers exactly this.

### Spans, Traces, and Context Propagation

A **trace** represents one end-to-end request through the system. It is composed of **spans**, where each span is a single unit of work — one service handling the request, one database call, one outbound HTTP call. Spans form a tree: a parent span (the incoming request) has child spans (the outbound calls it makes). Each span records a start time, duration, a name, and attributes.

The magic that stitches spans across process boundaries is **context propagation**. When service A calls service B over HTTP, it injects the current trace ID and its own span ID into the request headers. Service B reads those headers, sees "I am a child of that span in trace X," and continues the same trace. Without propagation you would get disconnected fragments instead of one coherent story.

### W3C Trace Context

For years, every vendor propagated context with its own proprietary headers, so a Zipkin service and a Datadog service could not understand each other. The **W3C Trace Context** standard fixed this with a universal HTTP header, `traceparent`:

```
traceparent: 00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01
             │  │                                │                │
          version    trace-id (32 hex)      parent span-id     flags
```

Every service that speaks W3C Trace Context can participate in the same trace regardless of vendor. **This is enabled by default in modern .NET** — `HttpClient` injects `traceparent` automatically and ASP.NET Core reads it. Interoperability that used to require careful configuration now happens for free.

### OpenTelemetry: The Standard

**OpenTelemetry (OTel)** is the vendor-neutral standard for generating and exporting telemetry — traces, metrics, and logs. It emerged from the merger of the OpenTracing and OpenCensus projects and is now the industry consensus, backed by every major APM vendor. Its promise is decoupling: you instrument your code *once* against the OpenTelemetry API, and you can send that data to Jaeger today, Datadog tomorrow, and Grafana Tempo next year by changing only exporter configuration, never your code.

In .NET, OpenTelemetry does not reinvent tracing. It builds on the **`Activity`** and **`ActivitySource`** types that already lived in `System.Diagnostics`. This is a beautiful piece of design: `Activity` *is* a span. When you learn `ActivitySource`, you are learning both the .NET-native API and OpenTelemetry at once.

### Activity and ActivitySource

You create an `ActivitySource` once (a singleton), then start `Activity` instances to represent spans:

```csharp
using System.Diagnostics;

public class PaymentService
{
    private static readonly ActivitySource ActivitySource = new("MyShop.Payments");

    public async Task<PaymentResult> ChargeAsync(Order order)
    {
        using var activity = ActivitySource.StartActivity("ChargeCard");
        activity?.SetTag("order.id", order.Id);
        activity?.SetTag("payment.amount", order.Total);
        activity?.SetTag("payment.gateway", "stripe");

        try
        {
            var result = await _gateway.ChargeAsync(order);
            activity?.SetTag("payment.transaction_id", result.TransactionId);
            activity?.SetStatus(ActivityStatusCode.Ok);
            return result;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.AddException(ex);
            throw;
        }
    }
}
```

The `activity?.` null-conditional is deliberate: if no listener is subscribed (for example in a unit test with tracing off), `StartActivity` returns `null` and your code costs nothing. This is a zero-overhead-when-disabled design. The `using` ensures the span is stopped and its duration recorded when the method exits, even on exception.

### Instrumenting a .NET App End to End

Here is a complete, production-shaped tracing setup for an ASP.NET Core service:

```csharp
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource
        .AddService(serviceName: "OrderService", serviceVersion: "1.4.2"))
    .WithTracing(tracing => tracing
        .AddSource("MyShop.Payments")            // your custom ActivitySource
        .AddAspNetCoreInstrumentation()          // incoming HTTP spans
        .AddHttpClientInstrumentation()          // outgoing HTTP spans
        .AddEntityFrameworkCoreInstrumentation() // database spans
        .SetSampler(new TraceIdRatioBasedSampler(0.1)) // sample 10%
        .AddOtlpExporter(o => o.Endpoint = new Uri("http://collector:4317")));
```

The instrumentation packages are what make this genuinely powerful. `AddAspNetCoreInstrumentation` creates a root span for every incoming request. `AddHttpClientInstrumentation` automatically creates child spans for outbound calls *and injects the `traceparent` header* so downstream services join the trace. `AddEntityFrameworkCoreInstrumentation` captures each SQL query as a span, so you can see that the slow request spent 1.8 seconds in a single N+1 query. You wrote none of this glue; you get a full cross-service, cross-database waterfall for free.

**Sampling** deserves attention. Tracing every request in a high-traffic system is expensive to store and process. `TraceIdRatioBasedSampler(0.1)` keeps a representative 10%. Because the sampling decision is based on the trace ID and propagated, either the whole trace is kept or none of it is — you never get half a trace. For more advanced needs, *tail sampling* (done in the OpenTelemetry Collector) can keep 100% of *errors* and slow traces while sampling the boring successful ones, giving you the best of both worlds.

### Exporters: OTLP, Jaeger, Zipkin

An **exporter** ships your telemetry out of the process. **OTLP** (OpenTelemetry Protocol) is the native, preferred choice — a gRPC/HTTP protocol understood by the OpenTelemetry Collector and virtually every backend. The recommended architecture is: your apps export OTLP to a **Collector**, and the Collector fans the data out to your chosen backends. This decouples your applications entirely from backend choice and lets you add processing (batching, filtering, tail sampling) in one central place.

**Jaeger** and **Zipkin** are popular open-source trace visualization backends that render the span waterfall. Both now ingest OTLP natively, so in modern setups you typically export OTLP everywhere and let the Collector route it.

> **Local dev tip:** **.NET Aspire** ships a built-in dashboard that is itself an OTLP receiver, giving you zero-config local traces, metrics, and logs — point your app's OTLP exporter at it and read a full cross-service waterfall without standing up Jaeger, Prometheus, or a Collector on your laptop.

## APM Tools

Application Performance Monitoring (APM) products bundle the three pillars into a polished, hosted experience with automatic instrumentation, correlation, and analytics.

- **Application Insights** is Microsoft's APM, part of Azure Monitor. It integrates deeply with .NET, and its Azure-native distributed tracing, live metrics stream, and Kusto (KQL) query language make it a natural fit for Azure shops. Its data model maps cleanly onto OpenTelemetry, and the modern integration is via the Azure Monitor OpenTelemetry distro.
- **Datadog** is a comprehensive, vendor-neutral SaaS platform spanning APM, infrastructure metrics, logs, and more, with strong .NET auto-instrumentation via a profiler agent.
- **New Relic** is another mature all-in-one APM with excellent .NET support and full OpenTelemetry ingestion.

> **Best practice:** Instrument with the vendor-neutral OpenTelemetry API and SDK, then point the OTLP exporter at whichever APM you use. This keeps your instrumentation portable. If your CFO switches vendors to cut costs, you change a connection string, not a thousand lines of instrumentation code. Vendor lock-in at the instrumentation layer is a trap seniors avoid.

## Centralized Logging

In a fleet of containers, SSHing into a box to `tail` a log file is hopeless — the box may already be gone. Centralized logging ships every log to one searchable place.

- **ELK / Elastic Stack** — Elasticsearch (storage and search), Logstash (ingestion and transformation), and Kibana (visualization). It is powerful and battle-tested but operationally heavy; running Elasticsearch at scale is a job in itself.
- **Loki** — Grafana's log aggregation system, designed to be cheaper than ELK by indexing only labels (not the full log text) and storing the rest compressed in object storage. It pairs naturally with Prometheus and Grafana for a unified pane of glass.
- **Seq** — a logging server built specifically for structured logs, and a joy for .NET developers. It understands Serilog's structured events natively, so you can query with real filters (`Amount > 50 and PaymentMethod = 'stripe'`), build dashboards, and set alerts, all with almost zero setup. For a .NET team, Seq is often the fastest path to genuinely useful structured logging, especially in development and small-to-mid production systems.

## Correlation Across Services

Everything in this chapter converges on one goal: given a single symptom, reconstruct the whole story. That requires **correlation** — the ability to jump from a metric spike to the exact traces behind it, and from a trace to the exact logs of each span.

The unifying key is the **trace ID**. Because W3C Trace Context propagates it automatically over HTTP, the trick is simply to stamp it onto your logs. In .NET, `Activity.Current` always holds the ambient trace context, so a tiny enricher connects logs to traces:

```csharp
.Enrich.WithSpan()   // via Serilog.Enrichers.Span, adds TraceId and SpanId
```

Or manually:

```csharp
using (LogContext.PushProperty("TraceId", Activity.Current?.TraceId.ToString()))
{
    logger.LogError(ex, "Payment failed for order {OrderId}", order.Id);
}
```

Now a single trace ID lets you pivot: see the slow trace in Jaeger, copy its ID, paste it into Seq, and read every log line from every service for that exact request. This is the payoff of observability.

**Messaging** needs the same discipline, and here the framework will not save you — message brokers do not automatically carry `traceparent`. When you publish to a queue (RabbitMQ, Azure Service Bus, Kafka), you must **inject** the trace context into the message headers, and the consumer must **extract** it to continue the trace:

```csharp
// Producer
var propagator = Propagators.DefaultTextMapPropagator;
propagator.Inject(
    new PropagationContext(Activity.Current!.Context, Baggage.Current),
    message.ApplicationProperties,
    (props, key, value) => props[key] = value);
```

The consumer extracts the same context and starts its span as a child of the producer's. Get this right and an asynchronous, event-driven system traces as cleanly as a synchronous one. Skip it and your traces shatter at every queue boundary.

## Alerting, SLIs, SLOs, SLAs, and Error Budgets

Telemetry you never look at is worthless. **Alerting** turns telemetry into action — but bad alerting trains people to ignore alarms.

These four acronyms form a hierarchy of reliability thinking:

- An **SLI (Service Level Indicator)** is a *measurement* of how well the service is doing. "Percentage of requests served in under 300 ms." "Percentage of requests without a 5xx error." SLIs come straight from your RED metrics.
- An **SLO (Service Level Objective)** is your internal *target* for an SLI. "99.9% of requests succeed over a rolling 30 days." It is a promise you make to yourself.
- An **SLA (Service Level Agreement)** is a *contract* with customers, usually with financial penalties, and is deliberately looser than your SLO. If your SLA is 99.5%, your internal SLO might be 99.9% so you have margin before you breach the contract.
- An **error budget** is the inverse of an SLO: `100% - SLO`. A 99.9% SLO permits 0.1% failures — about 43 minutes of downtime per month. That budget is a currency. As long as you have budget left, you can ship risky features fast. When you burn through it, you freeze feature work and focus on reliability. This reframes the eternal dev-versus-ops tension into a shared, quantitative decision.

> **Best practice:** Alert on **symptoms** (SLO burn rate, user-facing error rate, latency) rather than **causes** (a single machine's high CPU). A hot CPU that harms no user is not worth waking anyone. Fast SLO-burn-rate alerts catch real customer pain while staying quiet during harmless blips. Every alert should be actionable and point to a runbook; an alert nobody can act on is noise that erodes trust in the whole system.

## Health Checks: The Tie-In

Observability's closest operational cousin is the **health check** — a lightweight endpoint that reports whether an instance is fit to serve traffic. ASP.NET Core has first-class support:

```csharp
builder.Services.AddHealthChecks()
    .AddDbContextCheck<OrderDbContext>()
    .AddCheck("payment-gateway", () =>
        _gateway.IsReachable ? HealthCheckResult.Healthy() : HealthCheckResult.Degraded("Slow"))
    .AddCheck<RedisHealthCheck>("redis");

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false   // liveness: is the process running at all?
});
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")  // readiness: can it serve?
});
```

Distinguish **liveness** (is the process alive? if not, restart it) from **readiness** (are its dependencies ready? if not, stop sending traffic but do not restart). Kubernetes uses these two probes to make orchestration decisions, and they feed your metrics: a rising count of failing readiness checks is an early warning that a dependency is degrading. Health checks are the simplest, cheapest observability signal, and the first one you should implement.

## The 3 a.m. Walk: One Incident, Three Signals

Here is how the pillars actually combine when the page arrives. It is 3:07 a.m. and the SLO burn-rate alert fires: p99 latency on the order API has been over 2 seconds for ten minutes. Note what woke you: a **metric**. Metrics are the cheap, always-on signal, so they are the tripwire.

You open the Grafana dashboard backed by Prometheus. The RED panels tell the first part of the story: request rate is normal, error rate is near zero, but the duration histogram's p99 line stepped up sharply at 2:52. You slice by endpoint tag — every route is flat except `POST /orders`. In two minutes, a vague "the API is slow" has become "one endpoint's tail latency jumped at 2:52." That is as far as metrics can take you; aggregates cannot tell you *where inside a request* the time went.

So you pick one victim. In Jaeger you query for slow traces on that route (tail sampling has kept the slow ones) and open a 4-second specimen. The waterfall is unambiguous: the ASP.NET Core root span is thin, the EF Core spans are milliseconds, and almost the entire duration sits in one child span — the `HttpClient` call to the payment gateway, created automatically by `AddHttpClientInstrumentation`. The trace has answered the second question: *which hop*.

But a span only shows *that* the call took 3.8 seconds, not *why*. So you copy the trace ID from Jaeger, paste it into Seq, and — because every service stamps its logs via the Serilog span enricher — you get every structured log line from every service for that exact request. There they are: three warnings, `Payment gateway returned 429, retrying in 800ms (attempt 3)`. The gateway was not slow; it was rejecting you, and your own retries were stacking inside the span. A quick pivot on the same query shows the 429s started at 2:52 — right when the nightly reconciliation job began hammering the gateway with the same API key. Kill the job, latency recovers, go back to bed.

Walk the chain again: the metric said *something is wrong and where*, the trace said *which hop*, the logs said *why*. Three tools, one investigation — and the only thing that connected them was the trace ID, propagated in every hop's `traceparent` header, recorded on every span, and stamped onto every log line. That correlation is not luck. It exists because the propagation, the enricher, and the sampler were wired up on a quiet afternoon, exactly as this chapter prescribed. At 3 a.m. you can only harvest what you instrumented at 3 p.m.

## Bringing It Together

Observability is not a library you install; it is a design property you cultivate. The senior mindset treats telemetry as a first-class feature, budgeted for and reviewed like any other. Emit **structured logs** with correlation IDs and zero secrets. Record **metrics** chosen by RED and USE, guarding against cardinality explosions. Trace requests end to end with **OpenTelemetry**, propagating context across HTTP and messaging so a single trace ID unlocks the whole story. Feed **SLIs** into **SLOs** with **error budgets** that turn reliability into a shared, quantitative decision, and alert on symptoms, not noise.

Build the cockpit before you need it. When the 3 a.m. page arrives — and it will — the difference between a five-minute fix and a five-hour outage is the instrumentation you had the discipline to add while the skies were still clear.
