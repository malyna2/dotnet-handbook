# Chapter 25: Observability and Testing at Scale

[Chapter 9: Exceptions, Logging and First Diagnosis](#chapter-9-exceptions-logging-and-first-diagnosis) gave you the log line, and [Chapter 8: Testing](#chapter-8-testing) gave you unit and integration tests inside one service. This chapter starts where a request leaves the process and where a codebase outgrows one team. It makes you able to decide what a service must emit so that a page at 3 a.m. leads to a cause in minutes, to set an SLO and alert on how fast its budget burns instead of on CPU, and to choose which expensive tests — contract, property-based, end-to-end, load, mutation, evals — earn their cost for the system in front of you.

The first half is observability beyond logs. **Metrics** are aggregates: they keep a number per time series and throw the requests away, so they are cheap enough to keep always on and to alert on, and through bounded tags they say *that* something is wrong and *where*. **Traces** keep one request's detail across every hop, so they are sampled, and they say *which hop*. Together with the logs of Chapter 9 they meet in the SLO, the alert that fires on it, and a walk through one incident from metric to trace to log line — joined by a single propagated trace ID.

The second half is testing what unit tests can't see: interface drift between independently deployed services, inputs nobody thought to write, whole-stack breakage through a real browser, latency under concurrency, tests that execute code but assert nothing, and model output that no fixed assertion can pin. The through-line is one habit of mind: **treat your telemetry and your tests as systems to be engineered, with their own costs, failure modes, and return on investment** — and measure both against the same yardstick, the user-visible SLI.

## Metrics

If logs ([Chapter 9](#chapter-9-exceptions-logging-and-first-diagnosis)) are the narrative, metrics are the numbers you graph. They are aggregated, low-cost, and ideal for answering "how much" and "how fast" over time.

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

[Chapter 9](#observability-wiring) showed the minimal wiring that makes ASP.NET Core and `HttpClient` emit spans. Here is a complete, production-shaped tracing setup for an ASP.NET Core service, with custom sources, database spans and sampling:

```csharp
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource
        .AddService(serviceName: "OrderService", serviceVersion: "1.4.2"))
    .WithTracing(tracing => tracing
        .AddSource("MyShop.Payments")            // your custom ActivitySource
        .AddAspNetCoreInstrumentation()          // incoming HTTP spans
        .AddHttpClientInstrumentation()          // outgoing HTTP spans
        .AddEntityFrameworkCoreInstrumentation() // database spans
        .SetSampler(new ParentBasedSampler(new TraceIdRatioBasedSampler(0.1))) // follow the caller; roots: 10%
        .AddOtlpExporter(o => o.Endpoint = new Uri("http://collector:4317")));
```

The instrumentation packages are what make this genuinely powerful. `AddAspNetCoreInstrumentation` creates a root span for every incoming request. `AddHttpClientInstrumentation` automatically creates child spans for outbound calls *and injects the `traceparent` header* so downstream services join the trace. `AddEntityFrameworkCoreInstrumentation` captures each SQL query as a span, so you can see that the slow request spent 1.8 seconds in a single N+1 query. You wrote none of this glue; you get a full cross-service, cross-database waterfall for free.

**Sampling** deserves attention. Tracing every request in a high-traffic system is expensive to store and process. `TraceIdRatioBasedSampler(0.1)` keeps a representative 10% of the traces that *start* in this service. On its own it ignores the caller's decision (the OpenTelemetry specification says a ratio sampler must ignore the parent's sampled flag), so services sampling independently produce traces with holes. Wrapping it in `ParentBasedSampler` makes every service follow the decision that arrived in `traceparent`: the root decides once, and either the whole trace is kept or none of it is. For more advanced needs, *tail sampling* (done in the OpenTelemetry Collector) can keep 100% of *errors* and slow traces while sampling the boring successful ones, giving you the best of both worlds.

### Exporters: OTLP, Jaeger, Zipkin

An **exporter** ships your telemetry out of the process. **OTLP** (OpenTelemetry Protocol) is the native, preferred choice — a gRPC/HTTP protocol understood by the OpenTelemetry Collector and virtually every backend. The recommended architecture is: your apps export OTLP to a **Collector**, and the Collector fans the data out to your chosen backends. This decouples your applications entirely from backend choice and lets you add processing (batching, filtering, tail sampling) in one central place.

**Jaeger** and **Zipkin** are popular open-source trace visualization backends that render the span waterfall. Both now ingest OTLP natively, so in modern setups you typically export OTLP everywhere and let the Collector route it.

> **Local dev tip:** **.NET Aspire** ([Chapter 26](#net-aspire)) ships a built-in dashboard that is itself an OTLP receiver, giving you zero-config local traces, metrics, and logs — point your app's OTLP exporter at it and read a full cross-service waterfall without standing up Jaeger, Prometheus, or a Collector on your laptop.

> **Pay attention.** **The three places the trace ID breaks.** Over HTTP propagation needs no code from you, so teams assume it always works. It breaks in three places, and each one ends an investigation early:
>
> - **A broker hop** where nobody injected the context into the message's properties, so the worker starts a new trace. Whether your client library does it for you, and how to inject and extract by hand, is in [Chapter 9: Correlation Across Services](#correlation-across-services).
> - **A sampler that ignores the caller's decision** — a bare `TraceIdRatioBasedSampler`, as explained under *Sampling* above. Wrap it in `ParentBasedSampler` in every service.
> - **An ingestion cap** that stops all telemetry once an incident multiplies the volume. Retries log every failure, so the cap trips exactly when you need the data ([Chapter 30, Case 15](#case-15-blind-in-the-middle-of-the-incident)).
>
> The fix for all three is the same habit: check, on a quiet afternoon, that one request through every hop — HTTP, queue, worker — shows up as one trace.

## APM Tools

Application Performance Monitoring (APM) products bundle the three pillars into a polished, hosted experience with automatic instrumentation, correlation, and analytics.

- **Application Insights** is Microsoft's APM, part of Azure Monitor. It integrates deeply with .NET, and its Azure-native distributed tracing, live metrics stream, and Kusto (KQL) query language make it a natural fit for Azure shops. Its data model maps cleanly onto OpenTelemetry, and the modern integration is via the Azure Monitor OpenTelemetry distro ([Chapter 29](#observability-application-insights-and-kql) covers it with KQL).
- **Datadog** is a comprehensive, vendor-neutral SaaS platform spanning APM, infrastructure metrics, logs, and more, with strong .NET auto-instrumentation via a profiler agent.
- **New Relic** is another mature all-in-one APM with excellent .NET support and full OpenTelemetry ingestion.

> **Best practice:** Instrument with the vendor-neutral OpenTelemetry API and SDK, then point the OTLP exporter at whichever APM you use. This keeps your instrumentation portable. If your CFO switches vendors to cut costs, you change a connection string, not a thousand lines of instrumentation code. Vendor lock-in at the instrumentation layer is a trap seniors avoid.

## Alerting, SLIs, SLOs, SLAs, and Error Budgets

Telemetry you never look at is worthless. **Alerting** turns telemetry into action — but bad alerting trains people to ignore alarms.

These four acronyms form a hierarchy of reliability thinking:

- An **SLI (Service Level Indicator)** is a *measurement* of how well the service is doing. "Percentage of requests served in under 300 ms." "Percentage of requests without a 5xx error." SLIs come straight from your RED metrics.
- An **SLO (Service Level Objective)** is your internal *target* for an SLI. "99.9% of requests succeed over a rolling 30 days." It is a promise you make to yourself.
- An **SLA (Service Level Agreement)** is a *contract* with customers, usually with financial penalties, and is deliberately looser than your SLO. If your SLA is 99.5%, your internal SLO might be 99.9% so you have margin before you breach the contract.
- An **error budget** is the inverse of an SLO: `100% - SLO`. A 99.9% SLO permits 0.1% failures — about 43 minutes of downtime per month. That budget is a currency. As long as you have budget left, you can ship risky features fast. When you burn through it, you freeze feature work and focus on reliability. This reframes the eternal dev-versus-ops tension into a shared, quantitative decision.

The error budget becomes something you can alert on through the **burn rate**: how fast the budget is being spent, measured as the observed error ratio divided by the budget ratio. At a burn rate of 1 the budget lasts exactly the window; at 14.4, one hour spends 2% of a 30-day budget (14.4 hours out of 720). Paging on burn rate measures user pain against the promise, whatever the cause — the first of the *Three questions* at the end of the chapter works through the arithmetic, including why a plain error-rate threshold both wakes you for nothing and sleeps through a real outage.

> **Best practice:** Alert on **symptoms** (SLO burn rate, user-facing error rate, latency) rather than **causes** (a single machine's high CPU). A hot CPU that harms no user is not worth waking anyone. Fast SLO-burn-rate alerts catch real customer pain while staying quiet during harmless blips. Every alert should be actionable and point to a runbook; an alert nobody can act on is noise that erodes trust in the whole system.

## The 3 a.m. Walk: One Incident, Three Signals

Here is how the pillars actually combine when the page arrives. It is 3:07 a.m. and the SLO burn-rate alert fires: p99 latency on the order API has been over 2 seconds for ten minutes. Note what woke you: a **metric**. Metrics are the cheap, always-on signal, so they are the tripwire.

You open the Grafana dashboard backed by Prometheus. The RED panels tell the first part of the story: request rate is normal, error rate is near zero, but the duration histogram's p99 line stepped up sharply at 2:52. You slice by endpoint tag — every route is flat except `POST /orders`. In two minutes, a vague "the API is slow" has become "one endpoint's tail latency jumped at 2:52." That is as far as metrics can take you; aggregates cannot tell you *where inside a request* the time went.

So you pick one victim. In Jaeger you query for slow traces on that route (tail sampling has kept the slow ones) and open a 4-second specimen. The waterfall is unambiguous: the ASP.NET Core root span is thin, the EF Core spans are milliseconds, and almost the entire duration sits in one child span — the `HttpClient` call to the payment gateway, created automatically by `AddHttpClientInstrumentation`. The trace has answered the second question: *which hop*.

But a span only shows *that* the call took 3.8 seconds, not *why*. So you copy the trace ID from Jaeger, paste it into Seq, and — because every service stamps its logs with the trace ID ([Chapter 9](#correlation-across-services)) — you get every structured log line from every service for that exact request. There they are: three warnings, `Payment gateway returned 429, retrying in 800ms (attempt 3)`. The gateway was not slow; it was rejecting you, and your own retries were stacking inside the span. A quick pivot on the same query shows the 429s started at 2:52 — right when the nightly reconciliation job began hammering the gateway with the same API key. Kill the job, latency recovers, go back to bed.

Walk the chain again: the metric said *something is wrong and where*, the trace said *which hop*, the logs said *why*. Three tools, one investigation — and the only thing that connected them was the trace ID, propagated in every hop's `traceparent` header, recorded on every span, and stamped onto every log line. That correlation is not luck. It exists because the propagation, the enricher, and the sampler were wired up on a quiet afternoon, exactly as this chapter prescribes. At 3 a.m. you can only harvest what you instrumented at 3 p.m.

One more step exists when the trace points inside a single process — a span that is slow but makes no outbound calls. Then the next tool is a profiler on that process: `dotnet-counters` first, then whatever its reading points to ([Chapter 17: Profiling](#profiling-finding-the-bottleneck-in-a-running-system)).

> **Capstone tie-in:** This half of the chapter is exercised by ShopCore [Step 5](#step-5-caching-auth-and-observability) and [Step 8](#step-8-deploy-with-infrastructure-as-code) of [Chapter 44](#chapter-44-capstone-one-project-growing-up) — you'd add Serilog structured logging and OpenTelemetry so a single checkout produces one connected trace, then watch it cross service boundaries in a hosted backend.

Observability tells you what production is doing. The rest of the chapter is about finding out before production does — the tests that catch the defect classes unit and in-process integration tests structurally miss.

> **A shifting runner underneath it all:** the *engine* that runs your tests is changing. **Microsoft.Testing.Platform (MTP)** is the new, lightweight test runner that replaces the older VSTest host — each test project builds into a self-contained executable, and xUnit, NUnit, and MSTest now support running on it. Built exclusively on MTP is **TUnit**, a newer framework whose tests are *source-generated* at compile time (rather than reflected at runtime), run in parallel by default, and support Native AOT; it is still young (pre-1.0) but gaining real attention in 2025–2026 for its speed. None of the techniques below depend on your choice of runner, but it is worth knowing the ground is moving.

## Contract Testing: Killing the Integration Test Explosion

### The problem

Imagine a modest microservice architecture: an `Orders` service calls a `Payments` service, which calls a `Ledger` service, and a `Notifications` service subscribes to events from `Orders`. To gain confidence that these fit together, the naive approach is to deploy all of them together in a staging-like environment and run integration or end-to-end tests across the whole graph.

This works — until it doesn't. The pain compounds:

- **Slowness.** Every test run boots multiple services and their databases. Feedback that should take seconds takes tens of minutes.
- **Brittleness.** A failure in `Ledger` breaks the `Orders` test suite, even though `Orders` did nothing wrong. Diagnosing "whose fault is the red build?" becomes a daily tax.
- **Coordination cost.** To test `Orders` against a new `Payments` API, both teams must deploy compatible versions into the same environment at the same time. Independent deployment — the entire point of microservices — quietly dies.
- **Combinatorial coverage.** With *n* services and multiple versions each, the number of end-to-end combinations you would need to test to be truly safe grows far faster than you can afford.

The core insight of contract testing is that most cross-service bugs are not deep behavioural bugs — they are **interface mismatches**. The consumer expected a field named `total`; the provider renamed it to `amount`. The consumer sends `POST /orders`; the provider now requires an `Idempotency-Key` header. You don't need both services running to catch these. You need an agreed, machine-checkable description of the interface — a *contract* — and a way to verify each side against it independently.

### Consumer-driven contracts and Pact

**Consumer-driven contract (CDC) testing** flips the usual direction of API design. Instead of the provider publishing a spec and hoping consumers conform, each *consumer* declares exactly what it needs — the requests it will send and the responses it depends on — and that expectation *becomes* the contract. The provider then proves it can satisfy every consumer's stated needs.

[Pact](https://pact.io) is the de facto standard here, and [Pact.Net](https://github.com/pact-foundation/pact-net) is the .NET implementation. The workflow has two halves.

**1. The consumer side.** In a unit-style test, you use Pact's mock HTTP server. You describe an interaction ("given a product exists, when I GET /products/1, I expect this response shape"), point your real client code at the mock, and assert your client parses the response correctly. When the test passes, Pact writes a **pact file** — a JSON document recording every interaction.

```csharp
public class ProductsClientPactTests : IClassFixture<ProductsApiFixture>
{
    private readonly IPactBuilderV4 _pact;

    public ProductsClientPactTests()
    {
        var config = new PactConfig { PactDir = "../../../pacts" };
        _pact = Pact.V4("OrdersService", "ProductsService", config).WithHttpInteractions();
    }

    [Fact]
    public async Task GetProduct_WhenProductExists_ReturnsProduct()
    {
        _pact
            .UponReceiving("a request for product 1")
                .Given("product 1 exists")
                .WithRequest(HttpMethod.Get, "/products/1")
                .WithHeader("Accept", "application/json")
            .WillRespond()
                .WithStatus(HttpStatusCode.OK)
                .WithHeader("Content-Type", "application/json; charset=utf-8")
                .WithJsonBody(new
                {
                    // Matchers, not literals: we assert the *shape and type*,
                    // not the exact value. This is the crux of a good contract.
                    id = Match.Integer(1),
                    name = Match.Type("Widget"),
                    price = Match.Decimal(9.99m)
                });

        await _pact.VerifyAsync(async ctx =>
        {
            var client = new ProductsClient(new HttpClient { BaseAddress = ctx.MockServerUri });
            var product = await client.GetProductAsync(1);

            Assert.Equal(1, product.Id);
            Assert.Equal("Widget", product.Name);
        });
    }
}
```

> **Best practice: match on type, not value.** The single most common contract-testing mistake is asserting exact literal values (`name = "Widget"`). That couples your contract to test data and produces false failures the moment the provider's seed data changes. Use `Match.Type`, `Match.Integer`, `Match.Regex`, and friends so the contract captures *structure and types* — which is what interface compatibility actually means.

**2. The provider side.** The provider takes the pact file and *replays every recorded request against the real running provider*, asserting the real responses satisfy the consumer's expectations. Crucially, the provider must be able to set up the state each interaction assumes — the `Given("product 1 exists")` from above. Pact.Net calls back into a **provider state** endpoint you implement to seed exactly that precondition.

```csharp
public class ProductsProviderTests : IClassFixture<ProductStateFixture>
{
    private readonly ITestOutputHelper _output;
    private const string ProviderUri = "http://localhost:9223";

    [Fact]
    public void EnsureProviderHonoursPactWithOrders()
    {
        var verifier = new PactVerifier("ProductsService",
            new PactVerifierConfig { Outputters = new List<IOutput> { new XunitOutput(_output) } });

        verifier
            .WithHttpEndpoint(new Uri(ProviderUri))
            // Pull the contract straight from the broker, verified against real code.
            .WithPactBrokerSource(new Uri("https://broker.mycompany.com"), opts =>
            {
                opts.ConsumerVersionSelectors(new ConsumerVersionSelector { MainBranch = true })
                    .PublishResults("1.2.3", results => results.OnTestResults());
            })
            // The state endpoint seeds "product 1 exists" before that interaction replays.
            .WithProviderStateUrl(new Uri($"{ProviderUri}/provider-states"))
            .Verify();
    }
}
```

### The broker and the safety net

The **Pact Broker** (or its hosted cousin, PactFlow) is the piece that turns contract testing from a clever trick into a deployment safety net. Consumers publish their pacts to the broker, tagged with a version and branch. Providers fetch pacts from the broker and publish their verification results back. The broker then answers the question that actually matters at deploy time — via the `can-i-deploy` tool:

> "Can I deploy `Orders` version 1.2.3 to production right now, given the versions of `Products` and `Payments` currently there?"

The broker checks the recorded verification matrix and answers yes or no. This is what makes **independent deployment** safe: each service's pipeline calls `can-i-deploy` as a gate, and no service ships a change that breaks a consumer already in production.

### When contract testing replaces E2E tests

Contract testing does **not** verify business logic — it verifies that two services agree on their interface. But a huge fraction of cross-service E2E tests exist *only* to catch interface drift. Those you can and should delete, replacing them with fast, independent contract tests. Keep a thin layer of true end-to-end tests for a handful of critical user journeys where the *behaviour* of the assembled system, not just its wiring, is what you need to prove.

This ties directly to **schema evolution** ([Chapter 22](#chapter-22-api-evolution-real-time-and-serialization)). A pact is a living, executable record of exactly which fields and message shapes each consumer actually depends on. When you want to remove a field, the broker tells you whether any consumer's contract still references it. Contract testing and backward-compatible schema evolution are two views of the same discipline: **never break a consumer you can't see**. For asynchronous systems, Pact supports **message pacts** too — the consumer asserts on the shape of a Kafka or Service Bus message it can handle, and the provider verifies its published messages conform.

## Snapshot Testing with Verify

Contract tests pin the interface between two services. Inside one service, the equivalent risk is an output that changes shape without anyone deciding it should.

Some outputs are large and tedious to assert field-by-field — a serialized API response, generated code, a complex object graph. **Snapshot testing** (via the **Verify** library) records the output to a `.verified.txt` file on first run; subsequent runs diff the fresh output against the stored snapshot and fail on any difference, showing a diff.

```csharp
[Fact]
public Task Serialize_Invoice_MatchesSnapshot()
{
    var invoice = InvoiceFactory.SampleWithThreeLines();
    return Verify(invoice);   // writes .received.txt, compares to .verified.txt
}
```

The first run produces a `.received` file you review and rename to `.verified` (or accept via tooling). Commit the `.verified` file — it *is* the assertion. This is superb for locking down serialization and preventing accidental contract changes.

> **Pitfall:** snapshot tests are only as good as the discipline reviewing the diffs. A team that reflexively "accepts all" whenever a snapshot changes has converted a test into a rubber stamp. Snapshots also drift with non-deterministic content (timestamps, GUIDs) — use Verify's scrubbers to normalise those, or your snapshots will fail constantly.

## Property-Based Testing: Asserting the Rules, Not the Examples

### From examples to properties

An example-based test states one input and one expected output: `Add(2, 3)` returns `5`. You write a handful of these by hand, guided by your intuition about edge cases. The weakness is obvious in hindsight — **you only test the inputs you thought of**, and the bugs live in the inputs you didn't.

Property-based testing (PBT) inverts this. Instead of examples, you state a *property* that must hold for **all** inputs — a universally quantified invariant — and the framework generates hundreds of random inputs trying to break it. You stop asserting "for this input, that output" and start asserting "for any valid input, this rule is never violated."

Classic properties worth memorising:

- **Round-trip / inverse:** `decode(encode(x)) == x`. Serialization, compression, parsing, and encoding are all naturally testable this way.
- **Invariant:** the output always satisfies some rule regardless of input — a sorted list is always ordered and always a permutation of its input.
- **Oracle:** a fast implementation always agrees with a simple, obviously-correct (but slow) reference implementation.
- **Idempotence:** `f(f(x)) == f(x)` — normalising, saving, deduplicating.
- **Commutativity / metamorphic:** `f(a, b) == f(b, a)`, or "adding an item then removing it leaves the collection unchanged."

### Shrinking: why PBT is actually usable

The feature that makes PBT practical rather than merely noisy is **shrinking**. When the framework finds a failing input — say, a 400-element list of huge random integers — it doesn't just dump that mess in your face. It automatically searches for the *smallest, simplest* input that still fails: perhaps the two-element list `[0, -1]`. That minimal counterexample is often a near-complete bug report. Shrinking is the difference between "your test found a failure somewhere in this haystack" and "your test found *this exact needle*."

### FsCheck and CsCheck in C#

[FsCheck](https://fscheck.github.io/FsCheck/) is the .NET port of Haskell's QuickCheck. It's F#-native but has a first-class C# API and integrates with xUnit via `FsCheck.Xunit`. Here is the round-trip property for a serializer, expressed so the framework generates the objects for you:

```csharp
public record Money(decimal Amount, string Currency);

public class SerializationProperties
{
    [Property]
    public Property RoundTrip_PreservesValue(NonNull<string> currency, decimal amount)
    {
        var original = new Money(amount, currency.Get);

        var json = JsonSerializer.Serialize(original);
        var restored = JsonSerializer.Deserialize<Money>(json)!;

        // The property: serialize-then-deserialize is the identity function.
        return (restored == original).ToProperty();
    }

    [Property]
    public void Sort_IsIdempotentAndPermutes(int[] input)
    {
        var once = input.OrderBy(x => x).ToArray();
        var twice = once.OrderBy(x => x).ToArray();

        Assert.Equal(once, twice);                       // idempotent
        Assert.Equal(input.OrderBy(x => x), once);       // stays a permutation
        Assert.True(once.Zip(once.Skip(1)).All(p => p.First <= p.Second)); // ordered
    }
}
```

When you need to generate domain objects that random data can't validly produce — an email that must contain `@`, an order whose total equals the sum of its lines — you write a custom **generator** (`Gen<T>`) and register it via an `Arbitrary`. Controlling generation is where PBT graduates from toy examples to testing real domain models.

```csharp
public static class Generators
{
    public static Arbitrary<Money> Money() =>
        (from amount in Gen.Choose(0, 1_000_000)
         from currency in Gen.Elements("USD", "EUR", "GBP")
         select new Money(amount / 100m, currency))
        .ToArbitrary();
}

// Usage: [Property(Arbitrary = new[] { typeof(Generators) })]
```

[CsCheck](https://github.com/AnthonyLloyd/CsCheck) is a C#-first alternative worth knowing. It's designed around C# idioms (no F# dependency), has excellent shrinking, and adds genuinely useful extras: model-based and metamorphic testing helpers, and first-class support for **concurrency testing**, where it runs operations in random interleavings to flush out race conditions — something example-based tests essentially cannot do.

> **When PBT beats example-based tests:** reach for it whenever the code has a clear mathematical property (parsers, serializers, encoders, financial calculations, data structures, state machines) or where the input space is large and adversarial. It complements rather than replaces example tests — keep a few named examples as living documentation of specific, business-meaningful cases, and let properties patrol the vast space between them.

## End-to-End, UI, and API Testing

### Playwright for .NET

For browser-level E2E, [Microsoft Playwright for .NET](https://playwright.dev/dotnet/) has become the strong default, largely displacing Selenium for new work. It drives Chromium, Firefox, and WebKit through a single API, and its headline feature is **auto-waiting**: before acting on an element, Playwright automatically waits for it to be attached, visible, stable, and enabled. This design decision eliminates the single largest source of Selenium flakiness — the hand-rolled `Thread.Sleep` and explicit-wait soup.

```csharp
public class CheckoutTests : PageTest   // from Microsoft.Playwright.NUnit / MSTest
{
    [Test]
    public async Task Customer_CanCompleteCheckout()
    {
        await Page.GotoAsync("https://shop.example.com");

        // Locators are lazy and re-queried on each action — resilient to re-renders.
        await Page.GetByRole(AriaRole.Link, new() { Name = "Widget" }).ClickAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Add to cart" }).ClickAsync();
        await Page.GetByRole(AriaRole.Link, new() { Name = "Checkout" }).ClickAsync();

        await Page.GetByLabel("Email").FillAsync("buyer@example.com");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Place order" }).ClickAsync();

        // Web-first assertion: retries until true or times out. No manual wait.
        await Expect(Page.GetByText("Order confirmed")).ToBeVisibleAsync();
    }
}
```

> **Best practice: select by role and accessible name, not CSS/XPath.** `GetByRole`, `GetByLabel`, and `GetByText` bind your tests to what the *user perceives*, not to brittle DOM structure. A CSS refactor won't break them, and they double as an accessibility check. Playwright also records a **trace** — a DOM snapshot, screenshots, and network log for every step — that you can open in a viewer after a CI failure. This turns "it's red again and I can't reproduce it" into a post-mortem you can actually inspect.

### API-level E2E

Not every end-to-end test needs a browser. For a service or API product, the most valuable E2E tests exercise the *deployed HTTP surface* directly — real network, real database, real auth — but with no UI. These are far faster and less flaky than browser tests while still proving the full stack integrates. Playwright itself ships an `APIRequestContext` for this; a plain `HttpClient` against a deployed environment works too. This is distinct from the in-process `WebApplicationFactory` integration tests of [Chapter 8](#integration-testing), which never leave the test host.

### The pyramid versus the trophy

[Chapter 8](#the-testing-pyramid) introduced the **test pyramid**: many fast unit tests, fewer integration tests, very few slow E2E tests, pushing confidence down to the cheapest layer that can provide it.

The **testing trophy** (popularised by Kent C. Dodds) argues that for many modern applications — especially those with rich frameworks and heavy I/O — *integration* tests hit the best cost/confidence ratio, because bugs cluster at the seams between components, not inside single units. The trophy is fatter in the middle.

The senior takeaway is not to pick a dogma but to **shape your suite by where your bugs actually live and how expensive each layer is to run**. A CRUD-over-HTTP service and a numerical library warrant very different distributions. The one universal law holds regardless of shape: **the slower and flakier a test is, the fewer of them you should have** — because a flaky test at the top of the suite poisons trust in the entire pipeline.

### Controlling flakiness

Flaky tests are worse than no tests: they train the team to ignore red builds. The causes and the quarantine discipline are in [Chapter 8: Flaky Tests](#flaky-tests); the E2E layer, being the slowest and most timing-sensitive, adds two rules:

- **Never sleep for a fixed duration.** Wait for a *condition* — Playwright's web-first assertions do this for you.
- **Track flake rate as a metric.** If you can't measure it, you won't fix it.

### Accessibility checks in the same run

Since you already have a browser driving your app, you are one dependency away from catching a whole category of defects that unit tests structurally cannot see — and that, in the EU since June 2025, are compliance defects rather than cosmetic ones ([Chapter 6](#accessibility-the-part-that-is-now-law) and [Chapter 34](#accessibility-the-part-that-is-now-law-in-depth) cover the standards and the markup).

**axe-core** is the rules engine everyone uses; `Deque.AxeCore.Playwright` wires it into Playwright for .NET:

```csharp
using Deque.AxeCore.Playwright;
using Deque.AxeCore.Commons;

[Test]
public async Task Checkout_page_has_no_accessibility_violations()
{
    await Page.GotoAsync("/checkout");
    await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Checkout" }))
        .ToBeVisibleAsync();                       // don't scan a half-rendered page

    var results = await Page.RunAxe(new AxeRunOptions
    {
        RunOnly = new RunOnlyOptions
        {
            Type = "tag",
            Values = ["wcag2a", "wcag2aa", "wcag21a", "wcag21aa", "wcag22aa"]
        }
    });

    Assert.That(results.Violations, Is.Empty,
        FormatViolations(results.Violations));      // print rule, impact, and selector
}
```

Three things make the difference between this being useful and being a nuisance:

**Scan the page in the state you care about.** A scan that runs before hydration, or with a modal closed, tests markup no user sees. Drive the UI to the interesting state first — modal open, validation errors shown, table sorted — and scan there. Most real violations live in the states, not the initial render.

**Fail on new violations, not on all violations.** Retrofitting into an existing app produces hundreds of findings on day one, and a suite that is red on day one gets disabled by day three. Snapshot the current violations as a baseline, fail the build only on additions, and burn the baseline down deliberately. This is the same tactic as introducing any analyzer into legacy code ([Chapter 24](#chapter-24-working-with-legacy-brownfield-code)).

**Assert on roles and names throughout your normal E2E tests.** This is the underrated half. Playwright's `GetByRole`, `GetByLabel`, and `GetByText` locators resolve through the accessibility tree — the same tree a screen reader consumes. A test written as `Page.GetByRole(AriaRole.Button, new() { Name = "Place order" })` fails if that button loses its accessible name, becomes a `<div>`, or stops being labelled. You get accessibility regression coverage as a side effect of writing your E2E tests the way Playwright already recommends, at no extra cost.

> **Gotcha — know the ceiling.** Automated rules catch roughly a third of WCAG issues: the mechanical ones (missing labels, contrast, invalid ARIA, duplicate IDs). They cannot tell you whether alt text is *meaningful*, whether focus order is *logical*, or whether a custom widget is *usable*. A green axe run is evidence of no obvious errors, not evidence of an accessible product. Budget a manual keyboard-and-screen-reader pass per release for anything user-facing, and treat the automated suite as the regression net that keeps the manual findings fixed.

## Load Testing: Proving It Under Pressure

Functional tests answer "is it correct?"; load tests answer "does it stay correct and fast under concurrency and volume?" Benchmarks and profilers ([Chapter 17](#chapter-17-runtime-internals-and-performance)) examine one operation or one process. **Load testing** answers the system-level question: how does the whole service behave under many concurrent users? It reveals behaviors invisible in single-request testing — thread-pool starvation, connection-pool exhaustion, lock contention, and the difference between average and tail (p99) latency.

Three tools dominate:

- [**k6**](https://k6.io) (from Grafana) — a CLI load tester where you script scenarios in JavaScript. Language-agnostic, excellent for HTTP/gRPC/WebSocket load, and integrates cleanly into CI and Grafana dashboards. Great default for HTTP APIs.
- [**NBomber**](https://nbomber.com) — a .NET-native load testing framework where you write scenarios in **C#**, sharing models, auth helpers, and DTOs with your application code, and testing not just HTTP but any protocol you can call from C#.
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

In NBomber you express load as a *scenario* with an injection rate:

```csharp
// One client for the whole run: a new HttpClient per iteration would measure connection
// setup and can exhaust the load generator's ports (Chapter 5).
using var client = new HttpClient();

var scenario = Scenario.Create("checkout_load", async context =>
{
    var response = await client.PostAsJsonAsync(
        "https://api.example.com/orders",
        new { productId = 1, quantity = 2 });

    return response.IsSuccessStatusCode ? Response.Ok() : Response.Fail();
})
.WithLoadSimulations(
    // Ramp to 100 requests/sec over 30s, then hold for 1 minute.
    Simulation.RampingInject(rate: 100, interval: TimeSpan.FromSeconds(1), during: TimeSpan.FromSeconds(30)),
    Simulation.Inject(rate: 100, interval: TimeSpan.FromSeconds(1), during: TimeSpan.FromMinutes(1)));

NBomberRunner.RegisterScenarios(scenario).Run();
```

**Where it fits in CI.** Do not run a full soak test on every pull request — it's slow and its results are noisy on shared runners. Instead:

- Run a **short smoke load test** (a minute or two at moderate rate) on every PR to catch gross regressions early.
- Run **full load/soak tests on a schedule** (nightly) against a production-like environment, with **assertions on thresholds** — p95 latency under 200 ms, error rate under 0.1% — so the run *fails the build* on regression rather than merely producing a chart nobody reads. Both k6 and NBomber support pass/fail thresholds for exactly this.

> **Pitfall: measuring the wrong environment.** Load-test numbers from an under-provisioned CI runner or a "dev" tier with a shared database are actively misleading. Performance results are only meaningful against an environment whose topology mirrors production.

## Mutation Testing: Testing Your Tests

Code coverage lies. A line can be "covered" — executed during a test — while no assertion actually checks its behaviour. 100% coverage with zero assertions is entirely possible and entirely worthless. Coverage measures what your tests *touch*, not what they *verify*.

**Coverlet** is the standard .NET coverage collector, integrated via the `coverlet.collector` package and run with `dotnet test --collect:"XPlat Code Coverage"`. It reports line, branch, and method coverage, typically exported as Cobertura XML for CI dashboards and tools like ReportGenerator. Treat what it reports as a **signal, not a goal**: targeting a coverage *number* is actively harmful, because it incentivises tests that touch lines without asserting anything, gaming the metric while adding maintenance burden. The pathological end state is 90% coverage and zero confidence.

**Mutation testing** measures what coverage can't. [Stryker.NET](https://stryker-mutator.io/docs/stryker-net/introduction/) deliberately introduces small bugs — **mutants** — into your code: flipping `>` to `>=`, replacing `+` with `-`, negating a boolean, swapping a `return` value for a default. For each mutant, it reruns your test suite. If a test fails, the mutant is **killed** — your tests caught the injected bug, good. If every test still passes, the mutant **survived** — meaning your tests would not have noticed that bug in real code.

Your **mutation score** (killed ÷ total) is a far more honest measure of test *effectiveness* than line coverage. A method with 100% coverage but no meaningful assertions will have a dismal mutation score, and a surviving mutant is a concrete, actionable finding: "if this operator were wrong, no test would tell you." You run Stryker with a simple CLI invocation:

```
dotnet tool install -g dotnet-stryker
dotnet stryker --threshold-high 80 --threshold-low 60 --break-at 50
```

> **Practical note:** mutation testing is computationally expensive — it reruns the suite once per mutant, potentially thousands of times. Don't run it on every commit over the whole solution. Run it **on the diff** in CI (Stryker supports `--since` to mutate only changed code), or on a nightly schedule for critical modules. Point it at your core domain logic, where a missed bug is most costly — not at DTOs and configuration glue. Use it to *audit* a suite you suspect is hollow.

> **How to use coverage well:** read it as a map of *what's untested*, not a scoreboard. A sudden drop on a pull request is a useful prompt ("you added a branch with no test"). A blanket "we must hit 80%" mandate produces box-ticking. Combine coverage (did it run?) with mutation testing (would we notice a bug?) for the full picture.

## Testing Nondeterministic Systems: Evals for AI Features

Every technique so far assumes a fixed input produces a fixed output. Ship a feature backed by an LLM and that assumption is gone: the same prompt can return different text on every call, and *both* answers may be correct. `Assert.Equal(expected, actual)` has nothing to say about it. [Chapter 33](#chapter-33-building-ai-powered-systems) covers building these systems; this section is about the testing portfolio they need, because teams reliably reach one of two wrong conclusions — "you can't test this" or "we'll just mock the model" — and both leave the actual risk uncovered.

The way out is to split the system into two parts that are tested completely differently.

### Most of it is ordinary code — test it ordinarily

An AI feature is mostly not the model. Prompt construction, retrieval, chunking, tool implementations, schema validation, retries, budget enforcement, and the workflow's control flow are all deterministic code, and they are where most bugs actually live. Test them with everything in [Chapter 8](#chapter-8-testing) and this chapter as normal — and to do that, you need the model out of the way.

Program against `IChatClient` ([Chapter 33](#chapter-33-building-ai-powered-systems)) and a fake becomes trivial: a stub returning a canned `ChatResponse` lets you assert that your code built the right prompt, parsed the response correctly, enforced the token budget, and took the right branch. This is where property-based testing earns a second look — a chunker is exactly the kind of component whose invariants ("no chunk exceeds the token limit", "concatenating chunks reproduces the source", "overlaps are within bounds") FsCheck will break far faster than your examples will.

> **Best practice — test the tools as tools.** In an agentic feature, the functions the model can invoke are the code with the real blast radius: they read databases and send emails. They're plain methods. Test them directly, with the model nowhere in sight, including the argument validation that runs when the model passes something malformed — which it will, and which a test suite that only exercises well-formed calls will never catch.

### The model's output needs an eval suite, not a test

For the part that's genuinely nondeterministic, you build an **eval set**: representative inputs paired with a grading method, run as a suite, tracked as a **pass rate over time**. The difference from a unit test is the assertion, and there are four kinds worth knowing, in descending order of how much you should want them:

- **Deterministic checks on non-deterministic output.** Often overlooked, and always the first choice. Does the JSON validate against the schema? Is the cited document id one that was actually retrieved? Is the total the sum of the line items? These are ordinary assertions that happen to run against generated text, and they are fast, free, and unambiguous.
- **Reference-based.** Compare against a known-good answer — exact match for classification and extraction, similarity for freeform.
- **LLM-as-judge.** A model grades the output against a rubric. Scalable and surprisingly decent, but it is *itself* a nondeterministic component: validate the judge against human labels periodically, or you are measuring with an instrument you never calibrated.
- **Human review.** The gold standard, reserved for high-stakes features and periodic sampling of production traffic.

`Microsoft.Extensions.AI.Evaluation` gives this a home in a normal .NET test project, so evals live beside your unit tests and run on the same runner.

### Making it survive CI

Three practical problems separate an eval suite that runs in CI from one that gets disabled in month two:

**They cost money and time.** A 200-case eval set against a frontier model on every push is a bill and a slow pipeline. Split the suite: a small, cheap smoke set (20-30 cases, deterministic checks only) on every PR, and the full set nightly or on release branches. A provider's batch API halves the cost of the nightly run at the price of latency nobody is waiting on.

**They're flaky by construction.** Never gate on a single case passing — gate on the *aggregate*, with a threshold: "≥ 92% of the eval set passes." A single case flipping is signal only in a trend. Do pin what you can: fix the temperature at 0, seed anything random, freeze the retrieval corpus for the eval run, and pin the model version — an unpinned model is a dependency your provider updates without telling you, and it will move your pass rate on a day you changed nothing.

**Regression matters more than the absolute number.** "87% pass" means little on its own. "87%, down from 94% before this prompt change" is the finding. Store the run history and compare against the previous baseline, exactly as you'd treat a performance benchmark — and for the same reason: it is the delta that tells you whether the change you just made was an improvement.

> **Pitfall — the eval set that only contains cases that pass.** Eval sets are usually seeded from examples someone tried while building the feature, which are the examples the feature already handles. The valuable cases are the opposite: real production inputs that produced bad answers, added the day you find them. An eval set that isn't growing from production failures is measuring how well the feature works on the demo.

## Choosing Your Instruments

Every testing technique in this chapter earns its keep by catching a defect class nothing else catches — at a price. Weigh both columns before adding one to your portfolio.

| Technique | Defect class it uniquely catches | What it costs you | Reach for it when |
|---|---|---|---|
| Contract testing (Pact + broker) | Interface drift between services: renamed fields, changed shapes, broken consumers | Broker infrastructure; provider-state endpoints; buy-in from both teams | Multiple teams deploy services independently |
| Property-based testing (FsCheck/CsCheck) | Edge-case inputs you never thought to write; violated invariants; races (CsCheck) | Writing generators for valid domain objects; a different way of thinking about assertions | Parsers, serializers, financial calcs, data structures, state machines |
| Browser E2E (Playwright) | Whole-stack breakage only visible through the user's eyes | The slowest, flakiest layer; browser infrastructure in CI | A handful of critical user journeys — no more |
| API-level E2E | Full-stack wiring against a real deployed environment (network, DB, auth) | A deployed environment to point at; slower than in-process tests | The HTTP surface *is* the product |
| Load testing (k6/NBomber) | Latency and error regressions under concurrency that functional tests can't see | A production-like environment; noisy results on shared runners | Before traffic events; nightly with pass/fail thresholds |
| Mutation testing (Stryker.NET) | Assertion-free "covered" code — tests that execute but verify nothing | Reruns the suite once per mutant; very CPU-expensive | Core domain logic; run on the diff or nightly |
| Eval suites (Microsoft.Extensions.AI.Evaluation) | Quality regressions in nondeterministic output that no assertion can pin | Token spend per run; a curated, maintained case set; threshold tuning | Any shipped feature whose output comes from a model |
| Fake time + fixed seeds (`TimeProvider`, Chapter 8) | Expiry/scheduling bugs; irreproducible time- and randomness-based flakes | Retrofitting injection into legacy code | Anything touching clocks, delays, timers, or random data |

## Bringing It Together

Observability is not a library you install; it is a design property you cultivate. The senior mindset treats telemetry as a first-class feature, budgeted for and reviewed like any other. Record **metrics** chosen by RED and USE, guarding against cardinality explosions. Trace requests end to end with **OpenTelemetry**, propagating context across HTTP and messaging so a single trace ID unlocks the whole story — including the structured logs of Chapter 9. Feed **SLIs** into **SLOs** with **error budgets** that turn reliability into a shared, quantitative decision, and alert on how fast the budget burns, not on noise. Build the cockpit before you need it: when the 3 a.m. page arrives, the difference between a five-minute fix and a five-hour outage is the instrumentation you had the discipline to add while the skies were still clear.

Each testing technique in this chapter targets a specific weakness of the foundational testing you already know:

- **Contract testing** replaces slow, brittle cross-service integration tests with fast, independent verification of interfaces — and, wired to a broker, becomes a safe-deployment gate that makes schema evolution auditable.
- **Snapshot testing** locks down large outputs such as serialized responses, provided someone actually reviews the diffs.
- **Property-based testing** finds the inputs your example tests never imagined, and shrinking hands you a minimal reproduction.
- **Playwright and API-level E2E** prove the assembled system works through the user's eyes, while the pyramid-versus-trophy debate reminds you to shape the suite around where bugs actually live.
- **k6 and NBomber** answer the questions functional tests can't, provided you assert on thresholds and run against realistic environments.
- **Mutation testing** audits the auditors, exposing the tests that execute code without actually checking it.
- **Eval suites** extend the portfolio to output no assertion can pin, trading exact expectations for a tracked pass rate — the only way to change a prompt or a model with confidence.
- Underneath all of them, **`TimeProvider`, deterministic seeding, and disciplined test data** ([Chapter 8](#deterministic-tests-time-async-and-test-data)) are the unglamorous infrastructure that makes every other test trustworthy.

The senior mindset that unifies both halves: **every test and every signal is an investment with a cost and a return.** Fast, deterministic, and targeted at where failure is likely and expensive — that is the portfolio you are building.

## Sources & Further Reading

- **Pact documentation** — pact.io — consumer-driven contracts, the broker, provider states, `can-i-deploy`, and message pacts.
- **Pact.Net** — github.com/pact-foundation/pact-net — the .NET consumer and provider verification API.
- **FsCheck documentation** — fscheck.github.io/FsCheck — property-based testing, generators, arbitraries, and shrinking in .NET.
- **CsCheck** — github.com/AnthonyLloyd/CsCheck — C#-first property-based, model-based, metamorphic, and concurrency testing.
- **Microsoft Playwright for .NET documentation** — playwright.dev/dotnet — browser automation, locators, web-first assertions, tracing, and `APIRequestContext`.
- **k6 documentation** — k6.io / grafana.com/docs/k6 — scripting, load simulations, and thresholds.
- **NBomber documentation** — nbomber.com — C# load testing scenarios and load simulations.
- **Stryker.NET documentation** — stryker-mutator.io — mutation testing, mutation score, thresholds, and diff-based runs.
- **Microsoft Learn: `TimeProvider` and `FakeTimeProvider`** — learn.microsoft.com — testing time-dependent code in .NET 8+.

- **Microsoft.Extensions.AI.Evaluation** — learn.microsoft.com — building and running LLM eval suites inside a .NET test project.
- Kent C. Dodds, *"Write Tests. Not Too Many. Mostly Integration."* — the testing trophy argument.

## Practice

**1. Instrument a service end to end (2 h).** Take a service of your own, or a sample with an API, a queue and a worker. Follow [Instrumenting a .NET App End to End](#instrumenting-a-net-app-end-to-end) above: traces, RED metrics with bounded tags, and logs stamped with the trace ID, exported over OTLP to a local backend (the *Local dev tip* names the Aspire dashboard). Then make the queue hop carry the context: inject at publish, extract at consume ([Chapter 9](#correlation-across-services)). Done when one request shows up as **one** trace from the HTTP call through the worker, and one trace ID finds every log line of that request.

**2. A load test against an SLO (2 h).** Write an SLO for one endpoint: the SLI (requests that succeed under [threshold] ms, over all valid requests), the target and the window. Encode it as k6 `thresholds` (`http_req_duration` on `p(99)`, `http_req_failed` on `rate`), and generate the load with an arrival-rate executor, so a slow server can't slow the test down (question 3 explains why). Raise the rate step by step until a threshold fails: that rate is the knee. Run it again at the knee and read the process:

```bash
dotnet-counters monitor -p <pid> --counters System.Runtime,Microsoft.AspNetCore.Hosting
dotnet-trace collect -p <pid> --duration 00:00:20
```

Name the resource that saturated first (USE), and write the result down with the environment it ran on: CPU, RAM, runtime version, data scale, cache state.

**3. One chaos experiment (1 h).** In staging, run the six steps of [Chapter 20: Verifying Resilience](#verifying-resilience-chaos-engineering-in-practice) against one dependency with a Polly chaos strategy: the SLI as steady state, the hypothesis written down before you start, a 1–5% injection rate and an abort switch you have tested. If the team has never run one, run a game day instead; Chapter 20 explains why it finds more.

**4. A mutation run (30 min).** Run Stryker.NET on one core domain project, only on what changed since `main`:

```bash
dotnet tool install -g dotnet-stryker
dotnet stryker --since:main
```

For three surviving mutants, write the test that kills each, or argue why the mutant is equivalent.

The script, the SLO, the knee, the experiment's hypothesis and result, and what you changed afterwards belong in **your own public portfolio repo**, not in this one; anything about a real employer's system stays private. The Practice Gym's planned incident gym (M4 in [`PRACTICE_ROADMAP.md`](https://github.com/malyna2/dotnet-handbook/blob/main/PRACTICE_ROADMAP.md)) will become this chapter's lab: eight injected faults, diagnosed from logs, metrics and traces only, timed and written up as post-mortems.

## Three questions

**1.** The SLO is 99.9% of requests succeed over 30 days. Why does an alert on "error rate above 1% for 5 minutes" both wake you for nothing and sleep through a real outage, and what do you alert on instead?

<details>
<summary>Answer</summary>

- **The budget.** 99.9% leaves 0.1% of requests to fail in 30 days. The burn rate is the observed error ratio divided by 0.001, and the share of the budget an episode spends is its burn rate times its duration over the window.
- **It wakes you for nothing.** 1% for 5 minutes is a burn rate of 10 for 5 of the window's 43,200 minutes: about 0.12% of the month's budget.
- **It sleeps through the outage.** A steady 0.5% never crosses 1%, yet it burns at 5× and spends the whole budget in 6 days.
- **Alert on the burn rate over two windows.** For example, page when the last hour spent at least 2% of the budget (burn rate 14.4) *and* the last 5 minutes are still burning that fast, so the alert clears soon after the fix. Open a ticket for slow burns, such as 10% of the budget over 3 days (burn rate 1). The thresholds are a policy choice; the arithmetic makes them comparable.
- **Low traffic breaks the ratio.** At 20 requests an hour, one failure is a 5% error ratio, a burn rate of 50. Add a minimum request count or synthetic probes.

CPU, memory and pool saturation belong on the dashboard you diagnose with (USE), not on the pager.
</details>

**2.** A request crosses an API, a queue and a worker. In the trace backend you find the API's trace and, separately, the worker's; and some API traces lose a hop in the middle. What breaks each, and what does it cost you during an incident?

<details>
<summary>Answer</summary>

- **The split at the queue.** HTTP propagation is automatic: `HttpClient` injects `traceparent` and ASP.NET Core extracts it. A broker carries only what is in the message. Unless your client library does it for you, the producer must inject the context into the message properties, and the consumer must extract it and start its span with that parent. Without it, the slow trace ends at "publish", and the worker's logs carry a different trace ID.
- **The holes.** A `TraceIdRatioBasedSampler` set directly decides per service and ignores the caller's flag. A service with a different ratio drops spans of kept traces and keeps spans of dropped ones. Wrap it in `ParentBasedSampler` everywhere, and keep errors and slow traces with tail sampling in the Collector.
- **The cost.** The trace ID is the only join between metric, trace and logs. Wherever it breaks, the two-minute pivot of the 3 a.m. walk becomes a search by timestamp across services, during the incident.
</details>

**3.** A load test with 200 virtual users reports p99 of 180 ms, inside the SLO. In production, at the same request rate, p99 is several seconds. Name the mechanisms that make the test lie, and how you would make its number trustworthy.

<details>
<summary>Answer</summary>

- **A closed model slows down with the server.** A virtual user sends its next request only after the previous response arrives. When the server slows, the test sends less: the requests that would have queued are never sent, so their latency is never measured (often called *coordinated omission*). Real users arrive whether or not you are slow. Use an open model — k6's arrival-rate executors, NBomber's `Inject` — and check the achieved rate against the target.
- **The wrong environment.** A shared CI runner, a small database, warm caches and a single instance move the knee. Run against production-like topology and data volume, and publish the environment with the number.
- **The generator is the bottleneck.** A scenario that creates a `new HttpClient()` per iteration opens a connection per request ([Chapter 5](#keep-alive-connection-pooling-and-socket-exhaustion) explains why), so the test measures connection setup and can run out of local ports on the load generator. Share one client, as the NBomber sample above does, and watch the generator's CPU and connection count during the run.
</details>

## Decide

Checkout calls Payments and Inventory, each owned by another team and deployed on its own schedule. Last quarter brought three incidents: twice a renamed field in a Payments response broke checkout, and once p99 latency collapsed during a promotion. You have one engineer for six weeks. Which goes first?

- **A.** Consumer-driven contract tests: Pact for checkout's calls to Payments and Inventory, a broker, and `can-i-deploy` in all three pipelines.
- **B.** A nightly load test against checkout's SLO in a production-like environment, with profiling at the knee.
- **C.** A shared staging environment with end-to-end tests of the checkout journey.

<details>
<summary>Answer</summary>

**The cost of each.**
- **A** costs a broker, provider-state endpoints, and — the expensive part — the other teams' time, because the provider has to run verification in its own pipeline.
- **B** costs a production-like environment (money) and noisy results to triage. It is fully under your control.
- **C** costs the most for the least: slow, brittle, and every team must deploy compatible versions into one place at once. It would catch the renamed field only if someone deployed it to staging first, and it catches latency only at staging's scale.

**What decides it here: the incident classes and their recurrence.** Two of three incidents were interface drift, and drift recurs with every independent deploy of Payments. The latency collapse needs a traffic event to recur, and you can schedule a load test before the next promotion.

**The choice: A, starting with Payments.** It is the provider with the record. Then a short B before the next promotion: one scenario, an open-model rate at the promotion's expected peak, the SLO as thresholds. Skip C. Keep one or two end-to-end journeys only where behaviour, not wiring, needs proving.

**What would change it.**
- If the Payments team won't run provider verification, contract tests protect nothing: fall back to a tolerant reader in checkout plus schema checks on the published API, and spend the rest on B.
- If the next promotion is in three weeks, B goes first.
</details>

## Check at work

**Inspect.** For your most important endpoint, find three things. The SLI and the SLO: are they written down? The alert that fires when it breaks: on a symptom (burn rate, error ratio, latency) or on a cause (CPU, memory, one pod)? And the trail of one real request through a queue: does its trace ID reach the worker's logs? Good: one SLO, a burn-rate alert linked to a runbook, an unbroken trace. Bad: CPU alerts, and a trail that ends at the queue.

**Measure.** Read the last 30 days: the SLI, the share of the error budget spent, and the number of pages that led to no action. Every page that led to no action is a candidate for deletion or for demotion to a ticket.
