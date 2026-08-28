# Chapter 25: Advanced & Specialized Testing

_⏱️ Estimated read time: ~30 min · 5187 words (study pace)_

Chapter 7 gave you the foundations: unit tests with xUnit, mocking with Moq or NSubstitute, integration tests, and spinning up real dependencies with Testcontainers. Those techniques carry most teams a long way. But as a system grows from a single service into a fleet of services, and as a codebase matures from "does it work?" into "can we change it safely for the next five years?", a new set of problems appears that the foundational techniques do not address well.

This chapter is about those problems and the specialized tools built for them. Contract testing tames the combinatorial explosion of cross-service integration tests. Property-based testing finds the inputs you never thought to write an assertion for. End-to-end and UI testing verify the whole stack through a real browser. Load testing tells you whether the system survives Black Friday. Eval suites extend the portfolio to features whose output a model generates, where no fixed assertion applies. And a cluster of supporting disciplines — deterministic time, test data management, and mutation testing — keep the whole test suite honest.

The through-line is a single senior-level habit of mind: **treat your tests as a system to be engineered, with their own costs, failure modes, and return on investment**, not as a checkbox you tick after the "real" code is done.

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

This ties directly to **schema evolution** (Chapter 24). A pact is a living, executable record of exactly which fields and message shapes each consumer actually depends on. When you want to remove a field, the broker tells you whether any consumer's contract still references it. Contract testing and backward-compatible schema evolution are two views of the same discipline: **never break a consumer you can't see**. For asynchronous systems, Pact supports **message pacts** too — the consumer asserts on the shape of a Kafka or Service Bus message it can handle, and the provider verifies its published messages conform.

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

Not every end-to-end test needs a browser. For a service or API product, the most valuable E2E tests exercise the *deployed HTTP surface* directly — real network, real database, real auth — but with no UI. These are far faster and less flaky than browser tests while still proving the full stack integrates. Playwright itself ships an `APIRequestContext` for this; a plain `HttpClient` against a deployed environment works too. This is distinct from the in-process `WebApplicationFactory` integration tests of Chapter 7, which never leave the test host.

### The pyramid versus the trophy

The traditional **test pyramid** prescribes many fast unit tests, fewer integration tests, and very few slow E2E tests. The reasoning is economic: push confidence down to the cheapest, fastest layer that can provide it.

The **testing trophy** (popularised by Kent C. Dodds) argues that for many modern applications — especially those with rich frameworks and heavy I/O — *integration* tests hit the best cost/confidence ratio, because bugs cluster at the seams between components, not inside single units. The trophy is fatter in the middle.

The senior takeaway is not to pick a dogma but to **shape your suite by where your bugs actually live and how expensive each layer is to run**. A CRUD-over-HTTP service and a numerical library warrant very different distributions. The one universal law holds regardless of shape: **the slower and flakier a test is, the fewer of them you should have** — because a flaky test at the top of the suite poisons trust in the entire pipeline.

### Controlling flakiness

Flaky tests are worse than no tests: they train the team to ignore red builds. Attack flakiness structurally:

- **Never sleep for a fixed duration.** Wait for a *condition* (Playwright's web-first assertions do this for you).
- **Isolate state.** Each test creates its own data and cleans up (or runs in a transaction that rolls back). Shared mutable state across tests is the leading cause of order-dependent failures.
- **Quarantine, don't ignore.** When a test flakes, move it to a quarantined lane that runs but doesn't block the pipeline, file a bug, and fix or delete it on a deadline. A permanently-ignored `[Fact(Skip = "flaky")]` is dead weight that rots.
- **Track flake rate as a metric.** If you can't measure it, you won't fix it.

## Load & Performance Testing

Functional tests answer "is it correct?"; load tests answer "does it stay correct and fast under concurrency and volume?" Two tools dominate for .NET teams.

[k6](https://k6.io) (from Grafana) is a CLI load tester where scenarios are written in JavaScript. It's language-agnostic, excellent for HTTP/gRPC/WebSocket load, and integrates cleanly into CI and Grafana dashboards.

[NBomber](https://nbomber.com) is the natural choice when you want load tests **in C#**, sharing models, auth helpers, and DTOs with your application code. You express load as a *scenario* with an injection rate:

```csharp
var scenario = Scenario.Create("checkout_load", async context =>
{
    using var client = new HttpClient();
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

## Deterministic Tests: Time, Async, and Test Data

Everything above assumes tests are *deterministic* — same input, same result, every run. Two forces most often break that assumption: **wall-clock time** and **unmanaged test data**.

### Fake time with TimeProvider (.NET 8+)

For years, testing time-dependent code meant hand-rolling an `IClock` abstraction. .NET 8 standardised this with the abstract **`TimeProvider`** class. Inject it wherever you'd otherwise call `DateTime.UtcNow`, `Stopwatch`, `Task.Delay`, or create a timer. Production code uses `TimeProvider.System`; tests use the **`FakeTimeProvider`** (from the `Microsoft.Extensions.TimeProvider.Testing` package), whose clock only moves when you advance it.

```csharp
public class TokenService
{
    private readonly TimeProvider _time;
    public TokenService(TimeProvider time) => _time = time;

    public Token Issue() => new(ExpiresAt: _time.GetUtcNow().AddMinutes(30));
}

[Fact]
public void Token_IsExpired_AfterThirtyMinutes()
{
    var fakeTime = new FakeTimeProvider(new DateTimeOffset(2026, 07, 21, 12, 0, 0, TimeSpan.Zero));
    var token = new TokenService(fakeTime).Issue();

    fakeTime.Advance(TimeSpan.FromMinutes(31));  // jump forward instantly

    Assert.True(token.ExpiresAt < fakeTime.GetUtcNow());
}
```

The real power: `FakeTimeProvider` also controls `Task.Delay` and timers created through it. A test for a component that retries "after 5 seconds" no longer waits 5 real seconds — you call `Advance` and the delayed continuation fires immediately, deterministically. **Retrofitting `TimeProvider` into a legacy codebase is one of the highest-leverage testability refactors you can make**: it converts an entire category of slow, flaky, time-based tests into fast, reliable ones.

### Test data management

Sprawling, hand-built object graphs (`new Customer { ... }` with twenty properties) make tests unreadable and fragile. Two patterns keep data under control:

- **Builders / Object Mothers.** A fluent builder constructs a valid default object and lets each test override only the one field it cares about — making the test's *intent* legible. Libraries like [AutoFixture](https://github.com/AutoFixture/AutoFixture) generate anonymous valid data automatically so tests declare only what's relevant, and [Bogus](https://github.com/bchavez/Bogus) produces realistic fake names, emails, and addresses.
- **Deterministic seeding.** Random data generators must be *seeded with a fixed value* in tests. A generator seeded from the clock produces a suite that passes 99 runs and mysteriously fails the 100th — the worst kind of flake, because it isn't reproducible. Fix the seed; log it on failure so any failure *is* reproducible.

## Mutation Testing: Testing Your Tests

Code coverage lies. A line can be "covered" — executed during a test — while no assertion actually checks its behaviour. 100% coverage with zero assertions is entirely possible and entirely worthless. Coverage measures what your tests *touch*, not what they *verify*.

**Mutation testing** measures the latter. [Stryker.NET](https://stryker-mutator.io/docs/stryker-net/introduction/) deliberately introduces small bugs — **mutants** — into your code: flipping `>` to `>=`, replacing `+` with `-`, negating a boolean, swapping a `return` value for a default. For each mutant, it reruns your test suite. If a test fails, the mutant is **killed** — your tests caught the injected bug, good. If every test still passes, the mutant **survived** — meaning your tests would not have noticed that bug in real code.

Your **mutation score** (killed ÷ total) is a far more honest measure of test *effectiveness* than line coverage. A surviving mutant is a concrete, actionable finding: "if this operator were wrong, no test would tell you." You run Stryker with a simple CLI invocation:

```
dotnet stryker --threshold-high 80 --threshold-low 60 --threshold-break 50
```

> **Practical note:** mutation testing is computationally expensive — it reruns the suite once per mutant, potentially thousands of times. Don't run it on every commit over the whole solution. Run it **on the diff** in CI (Stryker supports `--since` to mutate only changed code), or on a nightly schedule for critical modules. Point it at your core domain logic, where a missed bug is most costly — not at DTOs and configuration glue.

## Testing Nondeterministic Systems: Evals for AI Features

Every technique so far assumes a fixed input produces a fixed output. Ship a feature backed by an LLM and that assumption is gone: the same prompt can return different text on every call, and *both* answers may be correct. `Assert.Equal(expected, actual)` has nothing to say about it. Chapter 19 covers building these systems; this section is about the testing portfolio they need, because teams reliably reach one of two wrong conclusions — "you can't test this" or "we'll just mock the model" — and both leave the actual risk uncovered.

The way out is to split the system into two parts that are tested completely differently.

### Most of it is ordinary code — test it ordinarily

An AI feature is mostly not the model. Prompt construction, retrieval, chunking, tool implementations, schema validation, retries, budget enforcement, and the workflow's control flow are all deterministic code, and they are where most bugs actually live. Test them with everything in Chapters 7 and 25 as normal — and to do that, you need the model out of the way.

Program against `IChatClient` (Chapter 19) and a fake becomes trivial: a stub returning a canned `ChatResponse` lets you assert that your code built the right prompt, parsed the response correctly, enforced the token budget, and took the right branch. This is where property-based testing earns a second look — a chunker is exactly the kind of component whose invariants ("no chunk exceeds the token limit", "concatenating chunks reproduces the source", "overlaps are within bounds") FsCheck will break far faster than your examples will.

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

Every technique in this chapter earns its keep by catching a defect class nothing else catches — at a price. Weigh both columns before adding one to your portfolio.

| Technique | Defect class it uniquely catches | What it costs you | Reach for it when |
|---|---|---|---|
| Contract testing (Pact + broker) | Interface drift between services: renamed fields, changed shapes, broken consumers | Broker infrastructure; provider-state endpoints; buy-in from both teams | Multiple teams deploy services independently |
| Property-based testing (FsCheck/CsCheck) | Edge-case inputs you never thought to write; violated invariants; races (CsCheck) | Writing generators for valid domain objects; a different way of thinking about assertions | Parsers, serializers, financial calcs, data structures, state machines |
| Browser E2E (Playwright) | Whole-stack breakage only visible through the user's eyes | The slowest, flakiest layer; browser infrastructure in CI | A handful of critical user journeys — no more |
| API-level E2E | Full-stack wiring against a real deployed environment (network, DB, auth) | A deployed environment to point at; slower than in-process tests | The HTTP surface *is* the product |
| Load testing (k6/NBomber) | Latency and error regressions under concurrency that functional tests can't see | A production-like environment; noisy results on shared runners | Before traffic events; nightly with pass/fail thresholds |
| Mutation testing (Stryker.NET) | Assertion-free "covered" code — tests that execute but verify nothing | Reruns the suite once per mutant; very CPU-expensive | Core domain logic; run on the diff or nightly |
| Eval suites (Microsoft.Extensions.AI.Evaluation) | Quality regressions in nondeterministic output that no assertion can pin | Token spend per run; a curated, maintained case set; threshold tuning | Any shipped feature whose output comes from a model |
| Fake time + fixed seeds (`TimeProvider`) | Expiry/scheduling bugs; irreproducible time- and randomness-based flakes | Retrofitting injection into legacy code | Anything touching clocks, delays, timers, or random data |

## Bringing It Together

Each technique in this chapter targets a specific weakness of the foundational testing you already know:

- **Contract testing** replaces slow, brittle cross-service integration tests with fast, independent verification of interfaces — and, wired to a broker, becomes a safe-deployment gate that makes schema evolution auditable.
- **Property-based testing** finds the inputs your example tests never imagined, and shrinking hands you a minimal reproduction.
- **Playwright and API-level E2E** prove the assembled system works through the user's eyes, while the pyramid-versus-trophy debate reminds you to shape the suite around where bugs actually live.
- **k6 and NBomber** answer the questions functional tests can't, provided you assert on thresholds and run against realistic environments.
- **`TimeProvider`, deterministic seeding, and disciplined test data** are the unglamorous infrastructure that makes every other test trustworthy.
- **Mutation testing** audits the auditors, exposing the tests that execute code without actually checking it.
- **Eval suites** extend the portfolio to output no assertion can pin, trading exact expectations for a tracked pass rate — the only way to change a prompt or a model with confidence.

The senior mindset that unifies them: **every test is an investment with a cost and a return.** Fast, deterministic, and targeted at where failure is likely and expensive — that is the portfolio you are building, and these are the specialized instruments for building it well.

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
- **AutoFixture and Bogus** — github.com/AutoFixture/AutoFixture and github.com/bchavez/Bogus — automated and realistic test data generation.
- **Microsoft.Extensions.AI.Evaluation** — learn.microsoft.com — building and running LLM eval suites inside a .NET test project.
- Kent C. Dodds, *"Write Tests. Not Too Many. Mostly Integration."* — the testing trophy argument.
