# Part 2 · Module 5: Observability and Testing at Scale

> **What this module makes you able to do.** Decide what a service must emit so that a page at 3 a.m. leads to a cause in minutes, set an SLO and alert on how fast its budget burns instead of on CPU, and choose which expensive tests (contract, load, chaos, mutation, property-based) earn their cost for the system in front of you.

**Time:** reading ≈ 55 min; hands-on ≈ 6 h 35 min — instrumenting a service 2 h, the load test 2 h, the chaos experiment 1 h, the mutation run 30, the questions 20, the decision 15, the check at work 30.

## Covers

- RED for symptoms and USE for causes, and why an unbounded tag on a metric (a user ID, a raw URL) multiplies the bill instead of the insight;
- how one trace ID crosses HTTP, a queue and a sampler (W3C `traceparent`, `Activity`, OpenTelemetry), and the three places it breaks;
- SLIs, SLOs and error budgets, and paging on how fast the budget burns rather than on a cause such as CPU;
- the 3 a.m. walk from alert to cause: metric, then trace, then logs, then a profiler when the time is spent inside one process;
- testing what unit tests can't see: contract tests between teams, load tests against the SLO, chaos experiments on failure paths, mutation and property-based tests on the tests themselves — what each catches and what it costs.

## The mechanism to explain without notes

**One user-visible SLI is the yardstick for everything in this module, and one propagated trace ID is the thread from a breached SLI to its cause.**

Telemetry trades cost for context. A metric is an aggregate: it keeps a number per time series and throws the requests away, so it is cheap enough to keep always on and to alert on. Through bounded tags (route, status code) it can say *that* something is wrong and *where*. Every distinct combination of tag values is a separate series, which is why a user ID on a metric explodes the backend's cost. A span or a log line keeps one request's detail, so it is expensive, sampled and capped, and it is the only signal that can say *which hop* and *why*. RED (rate, errors, duration) on each request path gives you the symptom; USE (utilization, saturation, errors) on each pool, queue and CPU gives you the cause to look for once a symptom has fired. Part 1's module on exceptions, structured logging and first diagnosis covers the log line itself; this module starts where the request leaves the process.

The trace ID is the join between the signals. `Activity` is the .NET span, and W3C `traceparent` carries the trace ID and the parent span over HTTP with no code from you: `HttpClient` injects it and ASP.NET Core reads it. It breaks in three places, and each one ends the 3 a.m. walk early:

- **a broker hop** where nobody injected the context into the message's properties, so the worker starts a new trace (Chapter 13, *Correlation Across Services*);
- **a sampler** that ignores the caller's decision (the callout below);
- **an ingestion cap** that stops all telemetry once an incident multiplies the volume — retries log every failure, so the cap trips exactly when you need the data (Chapter 51, Case 15).

The SLI is what users feel: the share of requests that succeed fast enough. The SLO is its target over a window, so the error budget (1 − SLO) is a quantity the team can spend. The **burn rate** is how fast it is being spent: the observed error ratio divided by the budget ratio. At a burn rate of 1 the budget lasts exactly the window; at 14.4, one hour spends 2% of a 30-day budget (14.4 hours out of 720). Page on that, because it measures user pain against the promise, whatever the cause. CPU measures one cause, pages when nobody is hurt and stays silent for every failure that doesn't use CPU. Testing at scale uses the same yardstick: a load test fails the build on the SLI's thresholds, a chaos experiment's steady state *is* the SLI, and the remaining budget decides how much risk the team may ship. Contract, mutation and property-based tests aim at what neither production signals nor unit tests reveal: an interface that drifted between services deployed independently, a test that runs code but asserts nothing, an input nobody thought to write.

> **Pay attention.** **The sampler that splits your traces.** Chapter 13's example sets `TraceIdRatioBasedSampler(0.1)` directly. The OpenTelemetry specification says that sampler *must ignore* the parent's sampled flag, so every service makes its own decision. The decisions line up only while every service runs the same ratio and the same algorithm. Change the ratio in one service and you get traces with holes: spans whose parent was dropped, and kept traces missing a hop. The fix is `new ParentBasedSampler(new TraceIdRatioBasedSampler(0.1))` in every service: the root decides and everyone downstream follows the `traceparent` flag (the SDK's default is `ParentBased` around always-on). Keep the errors and the slow traces with tail sampling in the Collector.

## Read (≈ 55 min)

1. [Chapter 13: Metrics](#metrics): the three instrument types, the cardinality pitfall, and RED against USE.
2. [Chapter 13: Distributed Tracing](#distributed-tracing): `traceparent`, `Activity` as the span, the end-to-end setup and sampling.
3. [Chapter 13: Centralized Logging](#centralized-logging) and [Correlation Across Services](#correlation-across-services): the trace ID on every log line, and why a broker hop needs explicit inject and extract.
4. [Chapter 13: Alerting, SLIs, SLOs, SLAs, and Error Budgets](#alerting-slis-slos-slas-and-error-budgets): symptoms against causes.
5. [Chapter 13: The 3 a.m. Walk: One Incident, Three Signals](#the-3-am-walk-one-incident-three-signals): metric, trace, logs, and the trace ID joining them.
6. [Chapter 51: Case 15 — Blind in the middle of the incident](#case-15-blind-in-the-middle-of-the-incident): a telemetry cost control that fails when the volume spikes.
7. [Chapter 15: Profiling: Finding the Bottleneck in a Running System](#profiling-finding-the-bottleneck-in-a-running-system): `dotnet-counters` first, then the tool its reading points to.
8. [Chapter 15: Load Testing: Proving It Under Pressure](#load-testing-proving-it-under-pressure) and [Chapter 25: Load & Performance Testing](#load-performance-testing): thresholds that fail the build, and where each kind of run belongs in CI.
9. [Chapter 25: Contract Testing: Killing the Integration Test Explosion](#contract-testing-killing-the-integration-test-explosion): consumer-driven contracts, provider states, and `can-i-deploy` as the gate.
10. [Chapter 21: Verifying Resilience: Chaos Engineering in Practice](#verifying-resilience-chaos-engineering-in-practice): steady state, hypothesis, blast radius and abort condition, then game days.
11. [Chapter 25: Mutation Testing: Testing Your Tests](#mutation-testing-testing-your-tests) and [Property-Based Testing: Asserting the Rules, Not the Examples](#property-based-testing-asserting-the-rules-not-the-examples): what coverage can't tell you, and shrinking.
12. [Chapter 25: Choosing Your Instruments](#choosing-your-instruments): the defect class each technique uniquely catches, and its price.

## Practice

**1. Instrument a service end to end (2 h).** Take a service of your own, or a sample with an API, a queue and a worker. Follow Chapter 13's [Instrumenting a .NET App End to End](#instrumenting-a-net-app-end-to-end): traces, RED metrics with bounded tags, and logs stamped with the trace ID, exported over OTLP to a local backend (the chapter's *Local dev tip* names the Aspire dashboard). Then make the queue hop carry the context: inject at publish, extract at consume. Done when one request shows up as **one** trace from the HTTP call through the worker, and one trace ID finds every log line of that request.

**2. A load test against an SLO (2 h).** Write an SLO for one endpoint: the SLI (requests that succeed under [threshold] ms, over all valid requests), the target and the window. Encode it as k6 `thresholds` (`http_req_duration` on `p(99)`, `http_req_failed` on `rate`), and generate the load with an arrival-rate executor, so a slow server can't slow the test down (question 3 explains why). Raise the rate step by step until a threshold fails: that rate is the knee. Run it again at the knee and read the process:

```bash
dotnet-counters monitor -p <pid> --counters System.Runtime,Microsoft.AspNetCore.Hosting
dotnet-trace collect -p <pid> --duration 00:00:20
```

Name the resource that saturated first (USE), and write the result down with the environment it ran on: CPU, RAM, runtime version, data scale, cache state.

**3. One chaos experiment (1 h).** In staging, run Chapter 21's six steps against one dependency with a Polly chaos strategy: the SLI as steady state, the hypothesis written down before you start, a 1–5% injection rate and an abort switch you have tested. If the team has never run one, run a game day instead; the chapter explains why it finds more.

**4. A mutation run (30 min).** Run Stryker.NET on one core domain project, only on what changed since `main`:

```bash
dotnet tool install -g dotnet-stryker
dotnet stryker --since:main
```

For three surviving mutants, write the test that kills each, or argue why the mutant is equivalent.

The script, the SLO, the knee, the experiment's hypothesis and result, and what you changed afterwards belong in **your own public portfolio repo**, not in this one; anything about a real employer's system stays private. The Practice Gym's planned incident gym (M4 in [`PRACTICE_ROADMAP.md`](https://github.com/malyna2/dotnet-handbook/blob/main/PRACTICE_ROADMAP.md)) will become this module's lab: eight injected faults, diagnosed from logs, metrics and traces only, timed and written up as post-mortems.

Later, if you need it: [Chapter 13: Health Checks: The Tie-In](#health-checks-the-tie-in), [Chapter 50: Observability: Application Insights and KQL](#observability-application-insights-and-kql), [Chapter 33: The Incident Cheat Card](#the-incident-cheat-card), and Chapter 25's [End-to-End, UI, and API Testing](#end-to-end-ui-and-api-testing) and [Deterministic Tests](#deterministic-tests-time-async-and-test-data).

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
- **The generator is the bottleneck.** Chapter 25's NBomber sample creates a `new HttpClient()` per iteration. That opens a connection per request (Part 1 covers why), so the test measures connection setup and can run out of local ports on the load generator. Share one client, and watch the generator's CPU and connection count during the run.
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
