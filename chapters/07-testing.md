# Chapter 7: Testing

_⏱️ Estimated read time: ~37 min ·     5292 words (study pace)_

Most developers arrive at their first senior interview able to write a test. Far fewer can explain *why* one test is worth writing and another is worth deleting, why a green test suite can still be worthless, or why the team that mocks everything ends up trusting nothing. This chapter is about that second, harder layer of understanding. We will write plenty of code, but the code is in service of judgment. By the end you should be able to look at a pull request and say, with reasons, "this test earns its keep" or "this test is a liability."

## Why We Test At All

Testing is not about proving your code is correct. You cannot prove correctness with tests; you can only demonstrate the presence of behaviour under specific conditions. What testing actually buys you is **confidence to change code**. A codebase without tests is a codebase where every change is a gamble, and where fear slowly ossifies the design because nobody dares refactor. The real product of a good test suite is not "quality" in the abstract — it is *velocity that doesn't decay*.

There is a well-worn observation that the cost of fixing a defect rises the later you catch it. A bug caught by a unit test on your machine costs a few minutes. The same bug caught in code review costs a round-trip of two people's attention. Caught in QA, it costs a bug report, a triage meeting, and a context switch back into code you've forgotten. Caught in production, it costs an incident, possibly customer trust, possibly money, and always the most expensive thing of all: debugging a live system under pressure with incomplete information. The exact multipliers are debated and context-dependent, but the *shape* of the curve is real and it is steep. Tests are a mechanism for pushing detection as far left — as early — as possible.

> **The core value proposition:** tests convert "I hope this still works" into "I know this still works, and here's the evidence." Everything else in this chapter is mechanics in support of that sentence.

### The Testing Pyramid

The testing pyramid is a heuristic for *how many* tests of *what kind* you should own. Picture a triangle. At the wide base sit **unit tests**: fast, numerous, isolated, testing a single unit of behaviour. In the middle sit **integration tests**: fewer, slower, verifying that components collaborate correctly — your code plus a real database, plus the HTTP stack, plus serialization. At the narrow top sit **end-to-end (E2E)** tests: few, slow, brittle, driving the whole system the way a user would.

The pyramid's shape encodes an economic argument. Unit tests are cheap to write, cheap to run, and pinpoint failures precisely — so have thousands. E2E tests exercise the most realistic scenarios but are expensive, flaky, and when they fail they tell you *something* is broken across a huge surface — so have few, reserved for critical user journeys.

The classic anti-pattern is the **ice cream cone**: lots of manual and E2E testing, few unit tests. Teams fall into it because E2E tests feel more "real." They are more real — and they will also bankrupt your CI time and your patience. Another failure mode is the **hourglass**: many units, many E2E, a starved integration middle — which leaves the seams between components untested precisely where the interesting bugs live.

> **Best practice:** treat the pyramid as a distribution, not a law. A data-heavy service might legitimately be integration-heavy because its logic *is* the database interaction. The point is intentionality: know which layer each test belongs to and why.

## Unit Testing with xUnit

.NET has three mainstream test frameworks: **xUnit**, **NUnit**, and **MSTest**. They are more alike than different, but xUnit has become the de facto default for new projects, partly because it was written by people reacting against perceived design mistakes in the others. We'll use xUnit as our primary vehicle and note the differences as we go.

### Facts and Theories

The atom of an xUnit test is a method decorated with `[Fact]`. A fact asserts something that is always true.

```csharp
public class MoneyTests
{
    [Fact]
    public void Add_TwoAmounts_ReturnsSum()
    {
        var a = new Money(10m, "USD");
        var b = new Money(5m, "USD");

        var result = a.Add(b);

        Assert.Equal(new Money(15m, "USD"), result);
    }
}
```

When the same logic should hold across many inputs, duplicating the method is wasteful. A `[Theory]` is a parameterized test — one method, many data rows. Each row runs as an independent test case with its own pass/fail.

```csharp
public class DiscountTests
{
    [Theory]
    [InlineData(100, 0, 100)]
    [InlineData(100, 10, 90)]
    [InlineData(100, 100, 0)]
    public void ApplyDiscount_ReducesPriceByPercent(
        decimal price, decimal percent, decimal expected)
    {
        var result = Pricing.ApplyDiscount(price, percent);

        Assert.Equal(expected, result);
    }
}
```

`[InlineData]` is perfect for primitive literals. When you need richer objects or computed data, reach for `[MemberData]` (backed by a static property returning `IEnumerable<object[]>`) or `[ClassData]` (backed by a class implementing `IEnumerable<object[]>`). Modern xUnit also supports `TheoryData<T...>`, a strongly-typed container that gives you compile-time checking of your data shapes — prefer it over raw `object[]` because it catches type mistakes at build time.

```csharp
public static TheoryData<decimal, decimal, decimal> DiscountCases => new()
{
    { 100m, 0m, 100m },
    { 100m, 10m, 90m },
    { 250m, 20m, 200m },
};

[Theory]
[MemberData(nameof(DiscountCases))]
public void ApplyDiscount_Cases(decimal price, decimal percent, decimal expected)
    => Assert.Equal(expected, Pricing.ApplyDiscount(price, percent));
```

> **Pitfall:** a theory with data that is expensive or non-deterministic to generate (reading files, calling `DateTime.Now`) will bite you. Data generation runs at *discovery* time in some scenarios and *execution* time in others. Keep theory data pure and cheap.

### Lifecycle and Fixtures

Here is xUnit's most important and most surprising design decision: **xUnit creates a new instance of your test class for every single test method.** There is no shared mutable state between tests unless you deliberately introduce it. This is a feature — it makes test isolation the default and kills a whole category of order-dependent bugs.

Because of this, xUnit has no `[SetUp]`/`[TearDown]` attributes like NUnit. Instead:

- **Per-test setup** goes in the constructor.
- **Per-test teardown** goes in `Dispose()` (implement `IDisposable`), or `DisposeAsync()` via `IAsyncLifetime` for async cleanup.

```csharp
public class OrderServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly OrderService _sut; // "system under test"

    public OrderServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _sut = new OrderService(_connection);
    }

    [Fact]
    public void PlaceOrder_PersistsRow() { /* ... */ }

    public void Dispose() => _connection.Dispose();
}
```

When setup is genuinely expensive and *can* safely be shared — spinning up a database, building an `IHost` — recreating it per test is wasteful. xUnit's answer is **fixtures**:

- **`IClassFixture<T>`** — one shared instance for all tests in a single class.
- **`ICollectionFixture<T>`** — one shared instance across multiple classes grouped by `[Collection]`.

```csharp
public class DatabaseFixture : IAsyncLifetime
{
    public string ConnectionString { get; private set; } = "";

    public async Task InitializeAsync() { /* start container, migrate */ }
    public async Task DisposeAsync() { /* dispose container */ }
}

public class ProductRepositoryTests : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _fixture;
    public ProductRepositoryTests(DatabaseFixture fixture) => _fixture = fixture;

    // tests share one database, each in its own class instance
}
```

> **Best practice:** share the *plumbing* (a running database, a host) via a fixture, but never share *mutable domain state* across tests. Each test should set up and tear down its own rows/records. Shared state is the number-one cause of flaky, order-dependent suites.

### Framework Differences in Brief

- **NUnit** uses `[Test]` and `[TestCase]`, and by default creates **one** instance of the test class for the whole fixture, relying on `[SetUp]`/`[TearDown]` for isolation. Its parameterized-test and assertion model (`Assert.That(x, Is.EqualTo(y))`) is very expressive.
- **MSTest** uses `[TestMethod]`, `[TestInitialize]`, `[TestCleanup]`, and `[DataRow]`. It's Microsoft's own, ships with Visual Studio, and has closed much of the historical gap with recent versions.

They're all competent. If you have no constraint, xUnit's isolation-by-default and minimal-magic philosophy make it the safe modern pick. If you're joining an existing codebase, use what's there — consistency beats preference.

## Test Doubles: The Full Taxonomy

"Mock" is used colloquially to mean "any fake object in a test," but the precise vocabulary (largely due to Gerard Meszaros and popularized by Martin Fowler) matters because it clarifies *what kind of verification you're doing*. All of these are **test doubles** — stand-ins for a real collaborator. There are five species.

- **Dummy** — passed around to satisfy a parameter but never actually used. A placeholder. `null` is sometimes a dummy; so is an empty object handed to a constructor just to make it compile.
- **Stub** — returns canned answers to calls. It feeds the system under test with predetermined data. A stub for `IExchangeRateProvider` might always return `1.1` regardless of input.
- **Fake** — a real, working implementation, just not suitable for production. An in-memory repository backed by a `Dictionary`, or SQLite in place of Postgres. It has real behaviour.
- **Spy** — a stub that *also records* how it was called, so you can inspect afterwards ("was `Send` called, and with what?").
- **Mock** — pre-programmed with *expectations* about the calls it should receive, and it *fails the test itself* if those expectations aren't met. The verification is built into the mock.

The distinction that trips people up is **stub vs mock**, and it maps onto a deeper split in testing philosophy:

- With a **stub**, you assert on the *state* of the system afterwards (**state verification**). "After placing the order, the balance is 90."
- With a **mock**, you assert on the *interactions* (**behaviour verification**). "Placing the order *called* `payment.Charge(90)` exactly once."

Here are hand-written examples so the concepts aren't hidden behind a library:

```csharp
public interface INotificationSender
{
    void Send(string to, string message);
}

// Dummy: exists only to fill a parameter, never exercised.
public sealed class DummySender : INotificationSender
{
    public void Send(string to, string message) => throw new NotSupportedException();
}

// Stub: canned behaviour, no recording.
public sealed class AlwaysSucceedsStub : INotificationSender
{
    public void Send(string to, string message) { /* do nothing, pretend success */ }
}

// Spy: records calls for later inspection.
public sealed class SenderSpy : INotificationSender
{
    public List<(string To, string Message)> Sent { get; } = new();
    public void Send(string to, string message) => Sent.Add((to, message));
}
```

```csharp
[Fact]
public void Register_SendsWelcomeEmail()
{
    var spy = new SenderSpy();
    var service = new RegistrationService(spy);

    service.Register("ada@example.com");

    // behaviour verification against a hand-rolled spy
    Assert.Single(spy.Sent);
    Assert.Equal("ada@example.com", spy.Sent[0].To);
}
```

> **Why learn the taxonomy if libraries blur it?** Because mocking libraries make *all five* trivially easy to produce, and the easy thing is not always the right thing. Knowing that you actually want a *fake* (a real in-memory implementation) rather than a *mock* (interaction assertions) is the difference between a test that survives refactoring and one that shatters the moment you change an internal call.

## Mocking Libraries: Moq and NSubstitute

Hand-writing doubles gets tedious. The two dominant .NET libraries are **Moq** and **NSubstitute**. They do the same job with different ergonomics.

### Moq

Moq uses a fluent, lambda-based API. You create a `Mock<T>`, configure it with `Setup`, read the real object off `.Object`, and assert with `Verify`.

```csharp
public interface IExchangeRates { decimal Rate(string from, string to); }

[Fact]
public void Convert_UsesProvidedRate()
{
    var rates = new Mock<IExchangeRates>();
    rates.Setup(r => r.Rate("USD", "EUR")).Returns(0.9m);

    var converter = new CurrencyConverter(rates.Object);
    var result = converter.Convert(100m, "USD", "EUR");

    Assert.Equal(90m, result);
    rates.Verify(r => r.Rate("USD", "EUR"), Times.Once);
}
```

Moq can match arguments loosely with `It.IsAny<T>()`, `It.Is<T>(predicate)`, throw exceptions with `.Throws<T>()`, return sequences across successive calls with `.SetupSequence(...)`, and verify call counts with `Times.Never`, `Times.Once`, `Times.Exactly(n)`.

### NSubstitute

NSubstitute leans into a "no `.Object`, no `.Setup`" aesthetic — the substitute *is* the interface, and you configure it by calling it.

```csharp
[Fact]
public void Convert_UsesProvidedRate_NSub()
{
    var rates = Substitute.For<IExchangeRates>();
    rates.Rate("USD", "EUR").Returns(0.9m);

    var converter = new CurrencyConverter(rates);
    var result = converter.Convert(100m, "USD", "EUR");

    Assert.Equal(90m, result);
    rates.Received(1).Rate("USD", "EUR");
}
```

Many people find NSubstitute reads more cleanly because there's no `.Object` unwrapping and the setup looks like ordinary method calls. Moq is more explicit and, some argue, less prone to accidental "did I mean to stub or to verify?" ambiguity. Both are excellent; pick one *per codebase* and stay consistent — mixing them is needless cognitive tax.

> **Pitfall (Moq specifically):** setting up a method with specific argument values and then calling with different values returns `default` silently rather than throwing. A method returning `null` where you expected a configured value is almost always an argument-matcher mismatch. Consider `MockBehavior.Strict` when you want unconfigured calls to throw loudly — at the cost of more brittle tests.

> **A note on trust:** In August 2023, Moq v4.20 quietly bundled SponsorLink, a closed-source component that hashed developers' local git email addresses at build time. It was removed after community backlash, but the trust damage pushed many teams to NSubstitute — which partly explains its momentum, and interviewers still bring it up. The broader reminder: dependency trust is part of dependency choice.

### When NOT to Mock

This is the senior-level point of the whole section. Mocking is a sharp tool that is routinely overused.

- **Don't mock types you don't own.** Mocking `HttpClient`, `DbContext`, or a third-party SDK couples your test to *your assumptions* about how that library behaves, which may be wrong. Wrap it in your own thin interface and mock *that*, or use a real/fake implementation.
- **Don't mock value objects or pure logic.** If a class has no external dependencies, just use the real thing. Mocking a `Money` or a `DateRange` is absurd.
- **Don't mock what you could fake.** An in-memory repository is usually a better test collaborator than a mocked one, because it has real behaviour and doesn't need re-configuring in every test.
- **Beware over-specified interaction tests.** A test that asserts on every internal call `Verify`s the *implementation*, not the *behaviour*. Change the implementation without changing behaviour and the test breaks — that's a test working against you. This is the single most common cause of "our tests make refactoring painful."

> **Rule of thumb:** mock at the *boundaries* of your system (the network, the clock, the message bus), and use real objects everywhere inside. Interaction verification is appropriate when the interaction *is* the observable behaviour — e.g., "we must publish exactly one `OrderPlaced` event." It's inappropriate as a proxy for "did the code run the way I wrote it."

## Better Assertions: FluentAssertions and Shouldly

`Assert.Equal(expected, actual)` works but reads backwards and fails with terse messages. Two libraries make assertions read like English and fail with rich diagnostics.

**FluentAssertions** extends any object with a `.Should()` method:

```csharp
result.Should().Be(90m);
customer.Orders.Should().HaveCount(2)
        .And.ContainSingle(o => o.Status == OrderStatus.Shipped);
Action act = () => service.Withdraw(1000m);
act.Should().Throw<InsufficientFundsException>()
   .WithMessage("*balance*");
```

**Shouldly** takes a similar tack with extension methods and famously good failure messages that echo the *source expression*:

```csharp
result.ShouldBe(90m);
customer.Orders.ShouldContain(o => o.Status == OrderStatus.Shipped);
Should.Throw<InsufficientFundsException>(() => service.Withdraw(1000m));
```

The real payoff is failure output. A raw `Assert.True(list.Contains(x))` fails with "Expected true, got false" — useless. `list.Should().Contain(x)` tells you what was in the list and what wasn't. That diagnostic quality shortens the debugging loop every single time a test fails.

> **A note on licensing:** FluentAssertions changed its license in 2025 (version 8 became commercial for some uses). This caused many teams to evaluate alternatives such as Shouldly or the newer community fork **AwesomeAssertions**. When you pick an assertion library today, check its current license — a detail that matters more than it used to.

## Generating Test Data: AutoFixture and Bogus

Constructing objects by hand clutters tests with irrelevant detail. Two tools help, with different goals.

**AutoFixture** creates objects filled with arbitrary-but-valid data, so you only specify the fields your test actually cares about:

```csharp
var fixture = new Fixture();
var customer = fixture.Create<Customer>();          // everything auto-populated
var order = fixture.Build<Order>()
                   .With(o => o.Total, 100m)         // pin what matters
                   .Without(o => o.CancelledAt)
                   .Create();
```

AutoFixture also integrates with xUnit via `[AutoData]` and `[InlineAutoData]`, injecting generated arguments straight into theory parameters — powerful, though it can make tests harder to read if overused.

**Bogus** is about *realistic, fake-but-plausible* data — names, emails, addresses, phone numbers — ideal for seeding demos and for tests where realism matters:

```csharp
var faker = new Faker<Customer>()
    .RuleFor(c => c.Name, f => f.Name.FullName())
    .RuleFor(c => c.Email, f => f.Internet.Email())
    .RuleFor(c => c.Country, f => f.Address.CountryCode());

var batch = faker.Generate(1000);
```

> **Pitfall:** random data in tests can produce **non-reproducible failures** — a test that fails one run in fifty because the generator happened to produce an edge case. That's not a flaky test, it's an *undiscovered bug*, but it's infuriating to reproduce. Seed your generators (`new Faker(...) { ... }` with a fixed `Randomizer.Seed`) in CI so failures are deterministic, then treat any failure as a real finding.

## Integration Testing

Unit tests verify units in isolation; integration tests verify that the wiring holds. In ASP.NET Core, the workhorse is **`WebApplicationFactory<TEntryPoint>`**, which boots your entire application in-memory — real routing, real middleware, real dependency injection, real model binding — and hands you an `HttpClient` that talks to it *without opening a network socket*.

```csharp
public class OrdersApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public OrdersApiTests(WebApplicationFactory<Program> factory)
        => _client = factory.CreateClient();

    [Fact]
    public async Task GetOrders_ReturnsOk()
    {
        var response = await _client.GetAsync("/api/orders");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
```

`WebApplicationFactory` sits on top of **`TestServer`**, an in-memory host built around a fake transport. The factory's real power is `WithWebHostBuilder` + `ConfigureTestServices`, which lets you swap production services for test ones — replacing the real payment gateway with a stub, or the real database with something you control:

```csharp
var client = factory.WithWebHostBuilder(builder =>
{
    builder.ConfigureTestServices(services =>
    {
        services.RemoveAll<IPaymentGateway>();
        services.AddSingleton<IPaymentGateway, FakePaymentGateway>();
    });
}).CreateClient();
```

### In-Memory vs Real Database

The most consequential integration-test decision is what to do about the database. Three options:

1. **EF Core In-Memory provider.** Fast, zero setup — and *dangerous*. It is not a relational database. It ignores relational constraints, doesn't enforce uniqueness the way SQL does, doesn't support transactions or raw SQL, and has different query-translation behaviour. A test that passes against it can fail against real Postgres. Microsoft themselves recommend against it for anything but the simplest cases.
2. **SQLite in-memory.** A real relational engine, genuinely fast, supports transactions. A big step up in fidelity — but its SQL dialect and type handling differ from Postgres/SQL Server, so provider-specific features and migrations may not translate.
3. **The real database engine.** Highest fidelity, catches the bugs that actually happen. Historically this meant a fragile shared test database or a heavyweight local install. **Testcontainers** solved that.

> **Best practice:** test business logic against fast fakes, but test anything that touches SQL — queries, migrations, constraints, concurrency — against the *same engine you run in production*. The in-memory provider's convenience is a trap that lets real database bugs sail through a green suite.

### Testcontainers for .NET

Testcontainers spins up **real** services in Docker containers, programmatically, from your test code — a genuine Postgres, Redis, RabbitMQ, whatever — and tears them down afterwards. You get production fidelity with unit-test-like ergonomics and no shared-environment contention.

```csharp
public class ProductRepositoryTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    private AppDbContext _db = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();               // pulls image, starts container

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;

        _db = new AppDbContext(options);
        await _db.Database.MigrateAsync();          // run real migrations
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    [Fact]
    public async Task Add_ThenQuery_RoundTripsThroughPostgres()
    {
        _db.Products.Add(new Product { Name = "Widget", Price = 9.99m });
        await _db.SaveChangesAsync();

        var found = await _db.Products.SingleAsync(p => p.Name == "Widget");

        found.Price.Should().Be(9.99m);
    }
}
```

The container starts in a second or two on a warm machine, runs your *real* migrations against *real* Postgres, and disposes cleanly. For a suite, share one container across a class or collection via a fixture and reset state between tests (truncate tables, or wrap each test in a transaction you roll back).

> **Pitfall:** Testcontainers needs a working Docker (or compatible) runtime on every machine that runs the suite, including CI agents. Budget for that and for image-pull time on cold caches. Cache images in CI and pin explicit tags (`postgres:16-alpine`, never `latest`) so your tests are reproducible.

## Test-Driven Development

TDD is a *discipline*, not a framework: you write the test *before* the production code, in a tight loop called **red-green-refactor**.

1. **Red** — write a failing test for the next tiny piece of behaviour. It must fail, and fail for the right reason (if it passes immediately, either the behaviour exists or your test is wrong).
2. **Green** — write the *simplest* code that makes it pass. Not the elegant code. The simplest. Even a hard-coded return is allowed here.
3. **Refactor** — now, with a green safety net, improve the design. Remove duplication, rename, extract. Run the tests after each change.

Let's build a `Fizzbuzz`-flavoured `RomanNumeral` converter, TDD-style, to feel the rhythm.

**Red** — the smallest possible behaviour:

```csharp
[Fact]
public void One_IsI()
    => RomanNumeral.From(1).Should().Be("I");
```

**Green** — the shameless simplest thing:

```csharp
public static class RomanNumeral
{
    public static string From(int n) => "I";
}
```

Yes, it's a lie. But it's a *green* lie, and TDD says: don't write code the tests don't demand. Now force generality with a new red test:

```csharp
[Theory]
[InlineData(1, "I")]
[InlineData(2, "II")]
[InlineData(3, "III")]
public void SmallNumbers(int n, string expected)
    => RomanNumeral.From(n).Should().Be(expected);
```

`2` fails. **Green** by generalising just enough:

```csharp
public static string From(int n) => new string('I', n);
```

Add `[InlineData(5, "V")]` — red again. Now the naive approach breaks, and the pressure of the failing test *drives* us to the real algorithm:

```csharp
public static string From(int n)
{
    var map = new (int Value, string Symbol)[]
    {
        (1000, "M"), (900, "CM"), (500, "D"), (400, "CD"),
        (100, "C"), (90, "XC"), (50, "L"), (40, "XL"),
        (10, "X"), (9, "IX"), (5, "V"), (4, "IV"), (1, "I"),
    };

    var sb = new StringBuilder();
    foreach (var (value, symbol) in map)
        while (n >= value) { sb.Append(symbol); n -= value; }
    return sb.ToString();
}
```

**Refactor** — the code is already clean; the tests stay green; we're done. Notice what TDD gave us: we never wrote a line of production code that wasn't justified by a test, our design emerged from the examples, and we have a full regression suite for free.

> **What TDD is really for:** it's a design tool disguised as a testing tool. Writing the test first forces you to use your own API before it exists, which surfaces awkward interfaces immediately. The tests are a valuable by-product; the *design pressure* is the main event. TDD is not mandatory, and it shines most on logic-heavy code with clear inputs and outputs, less so on exploratory or UI-glue work.

## Behaviour-Driven Development

BDD reframes tests as *executable specifications* written in near-natural language, so non-developers (product owners, QA) can read and even author them. In .NET the tool was **SpecFlow**; after SpecFlow was discontinued, the community fork **Reqnroll** carries the torch with a compatible API.

Scenarios are written in **Gherkin** — `Given/When/Then`:

```gherkin
Feature: Order discounts

  Scenario: Loyalty members get 10% off
    Given a customer with a loyalty membership
    And a cart totalling 100 USD
    When the order is placed
    Then the charged amount should be 90 USD
```

Each line binds to a C# "step definition" method via attributes; Reqnroll wires them together into a runnable test.

```csharp
[Binding]
public class OrderSteps
{
    private Cart _cart = null!;
    private decimal _charged;

    [Given("a customer with a loyalty membership")]
    public void GivenLoyaltyMember() => _cart = new Cart { IsLoyalty = true };

    [When("the order is placed")]
    public void WhenPlaced() => _charged = new Checkout().Place(_cart);

    [Then("the charged amount should be (.*) USD")]
    public void ThenCharged(decimal expected) => _charged.Should().Be(expected);
}
```

> **When BDD is worth it:** the overhead of Gherkin only pays off when non-technical stakeholders genuinely read or write the scenarios, or when a living, human-readable spec has real value. If it's just developers writing `Given/When/Then` for other developers, you've added indirection for no audience — a plain xUnit test with a good name is simpler. Use BDD for the collaboration, not for the syntax.

## Specialized Techniques

### Snapshot Testing with Verify

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

### Mutation Testing with Stryker.NET

Code coverage tells you which lines *ran*. It does not tell you whether your tests would *notice* if those lines were wrong. **Mutation testing** answers the harder question. **Stryker.NET** deliberately introduces small bugs — "mutants" — into your code (flips a `>` to `>=`, replaces a `+` with `-`, negates a boolean) and reruns your tests. If a test fails, the mutant is "killed" — good, your tests caught the change. If all tests still pass, the mutant "survived" — your tests are blind to that logic.

Your **mutation score** (killed / total mutants) is a far truer measure of test *effectiveness* than line coverage. A method with 100% coverage but no meaningful assertions will have a dismal mutation score — mutation testing exposes exactly the "tests that execute but don't verify" problem.

```
dotnet tool install -g dotnet-stryker
dotnet stryker
```

> **Best practice:** mutation testing is slow (it reruns the suite once per mutant), so run it periodically or on critical modules rather than every commit. Use it to *audit* the quality of a suite you suspect is hollow.

### Code Coverage with Coverlet

**Coverlet** is the standard .NET coverage collector, integrated via the `coverlet.collector` package and run with `dotnet test --collect:"XPlat Code Coverage"`. It reports line, branch, and method coverage, typically exported as Cobertura XML for CI dashboards and tools like ReportGenerator.

Coverage is a **signal, not a goal**. High coverage tells you code was executed; it says nothing about whether it was *verified*. And targeting a coverage *number* is actively harmful — it incentivises tests that touch lines without asserting anything, gaming the metric while adding maintenance burden. The pathological end state is 90% coverage and zero confidence.

> **How to use coverage well:** read it as a map of *what's untested*, not a scoreboard. A sudden drop on a pull request is a useful prompt ("you added a branch with no test"). A blanket "we must hit 80%" mandate produces box-ticking. Combine coverage (did it run?) with mutation testing (would we notice a bug?) for the full picture.

## Craft: Naming, Structure, and Smells

The techniques above are worthless if the tests themselves are unreadable or unreliable. Test code is production code — it is read far more often than it is written, and it is the first documentation a new developer meets.

### Test Naming

A test name should tell you what broke *without opening the body*. Popular conventions:

- `MethodName_Scenario_ExpectedBehaviour` — e.g. `Withdraw_AmountExceedsBalance_ThrowsInsufficientFunds`.
- `Given_When_Then` phrasing — e.g. `GivenEmptyCart_WhenCheckout_ThenThrows`.
- Plain-English sentences — e.g. `Withdrawing_more_than_the_balance_is_rejected`.

Any of them is fine; consistency within a codebase matters more than the choice. The failing-test report in CI should read like a list of broken requirements.

### Arrange-Act-Assert

The **AAA** pattern structures every test into three visually distinct blocks:

```csharp
[Fact]
public void Withdraw_ReducesBalance()
{
    // Arrange
    var account = new Account(balance: 100m);

    // Act
    account.Withdraw(30m);

    // Assert
    account.Balance.Should().Be(70m);
}
```

**Arrange** sets up the world, **Act** performs the single operation under test, **Assert** checks the outcome. The discipline of a *single* Act line is underrated: if you find yourself with two Acts, you're probably testing two behaviours and should split into two tests. (The BDD `Given/When/Then` is the same idea in different clothes.)

### Test Smells

- **The mystery guest** — a test depends on external data (a file, a shared DB row) not visible in the test itself. The reader can't tell why it passes. Make the fixture explicit and local.
- **The overspecified mock** — verifies interactions that aren't the point, breaking on every refactor. Assert behaviour, not implementation.
- **Logic in tests** — `if`/`for`/`switch` inside a test means the test itself can be buggy and needs testing. Prefer straight-line tests and theories.
- **Assertion roulette** — many bare assertions with no messages, so a failure doesn't tell you *which* one blew up. Fluent assertion libraries fix this by producing descriptive messages automatically.
- **The slow test** — a "unit" test that hits disk, network, or `Thread.Sleep`. It'll get skipped, disabled, or ignored. Push it down to a fake or up to the integration tier where its cost is expected.
- **Excessive setup** — twenty lines of arrange for a two-line act signals the *design* is too coupled. The test is telling you something about the production code.

### Flaky Tests

A **flaky test** passes or fails without any code change — the most corrosive thing in a test suite, because it destroys the one property tests exist to provide: *trust*. Once a suite is flaky, people start re-running CI until it's green, and at that moment every test has become worthless, because a real failure is indistinguishable from noise.

Common causes and fixes:

- **Time and dates.** `DateTime.Now` makes behaviour depend on when the test runs. Inject an `IClock`/`TimeProvider` (built into modern .NET) and control time explicitly.
- **Ordering and shared state.** Tests that pass alone but fail together share mutable state. Isolate them — this is exactly why xUnit's per-test instance model exists.
- **Async and timing.** `Task.Delay` and "wait a bit then assert" race the scheduler. Await deterministic signals, not wall-clock guesses.
- **Test parallelism.** Two tests hitting the same database row concurrently. Give each its own data, or serialise them with a collection.
- **Non-deterministic data.** Unseeded random generators (see Bogus/AutoFixture above).
- **External dependencies.** A test calling a real network service fails when the network hiccups. Fake the boundary.

> **Best practice:** treat a flaky test as a **P1 defect in the suite**, not an annoyance to retry past. Quarantine it (mark it, get it out of the blocking path) *and* file a ticket to fix or delete it — but never leave it silently retrying, because a suite you don't trust is a suite you don't have.

## Bringing It Together

Testing maturity is not measured in a coverage percentage or a count of tests. It's measured in a single capability: **can your team change the code with confidence and speed?** Everything in this chapter serves that. The pyramid tells you where to invest. Unit tests with clean AAA structure and honest names give fast, precise feedback. Test doubles — used with the judgment to know when *not* to mock — isolate units without ossifying them. Integration tests with Testcontainers verify the seams against real infrastructure. TDD applies design pressure; BDD aligns with stakeholders when there's an audience for it. Mutation testing audits whether your tests actually verify, and coverage maps what's untouched. And relentless hygiene around flakiness protects the trust that makes the whole edifice worthwhile.

Write tests that would fail if the behaviour broke, that read clearly when they do, and that survive a refactor of the code they cover. Do that, and your test suite stops being a chore you maintain and becomes the thing that lets you move fast without breaking things.
