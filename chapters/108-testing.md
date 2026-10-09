# Chapter 8: Testing

Most developers can write a test. Far fewer can explain *why* one test is worth writing and another is worth deleting, why a green suite can still be worthless, or why the team that mocks everything ends up trusting nothing. This chapter makes you able to write and fix the tests of one service without help: put each test at the level that can prove its claim, choose the right test double, test the HTTP surface and the database for real, and remove a flaky test's cause instead of retrying it.

One idea ties the sections together: **a test is only as true as the things it runs for real, and only as repeatable as the inputs it controls.** The pyramid and xUnit come first: what each level proves, and how xUnit isolates one test from the next. Test doubles, mocking libraries, assertions and test data follow, which is where a test decides what it replaces and therefore what it can no longer catch. Integration testing puts the real pipeline and the real database engine back. TDD and BDD are ways of working with all of that; the craft section (names, structure, smells, flakiness) and deterministic time and data make a failure repeatable and readable. Contract, property-based, end-to-end, load and mutation testing are in [Chapter 25: Observability and Testing at Scale](#chapter-25-observability-and-testing-at-scale).

## Why We Test At All

Tests can't prove code correct; they show behaviour under specific conditions. What they buy is **confidence to change code**: without them every change is a gamble, and the design ossifies because nobody dares refactor.

The later a defect is caught, the more it costs: minutes on your machine, two people's attention in review, a bug report and a context switch in QA, an incident and live debugging in production. The multipliers are debated; the steep shape of the curve is not. Tests push detection as early as possible.

### The Testing Pyramid

The testing pyramid is a heuristic for *how many* tests of *what kind* you should own. Picture a triangle. At the wide base sit **unit tests**: fast, numerous, isolated, testing a single unit of behaviour. In the middle sit **integration tests**: fewer, slower, verifying that components collaborate correctly — your code plus a real database, plus the HTTP stack, plus serialization. At the narrow top sit **end-to-end (E2E)** tests: few, slow, brittle, driving the whole system the way a user would.

The pyramid's shape encodes an economic argument. Unit tests are cheap to write, cheap to run, and pinpoint failures precisely — so have thousands. E2E tests exercise the most realistic scenarios but are expensive, flaky, and when they fail they tell you *something* is broken across a huge surface — so have few, reserved for critical user journeys.

The classic anti-pattern is the **ice cream cone**: lots of manual and E2E testing, few unit tests. Teams fall into it because E2E tests feel more "real." They are more real — and they will also bankrupt your CI time and your patience. Another failure mode is the **hourglass**: many units, many E2E, a starved integration middle — which leaves the seams between components untested precisely where the interesting bugs live.

> **Best practice:** treat the pyramid as a distribution, not a law. A data-heavy service might legitimately be integration-heavy because its logic *is* the database interaction. The point is intentionality: know which layer each test belongs to and why.

[Chapter 25](#the-pyramid-versus-the-trophy) weighs the pyramid against the *testing trophy*, which puts the weight on integration tests.

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

> **xUnit v3 note:** xUnit v3 went GA in December 2024 as a ground-up repackaging (new package IDs, each test project now builds as an executable on Microsoft.Testing.Platform), and `IAsyncLifetime.InitializeAsync`/`DisposeAsync` now return `ValueTask` rather than `Task`. The examples here use idioms that read the same on v2 and v3; on v3 just expect `ValueTask` signatures.

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

> **Pay attention.** **A double tests your assumption, not the dependency.** The usual wrong answer to "why not mock the `DbContext`?" is "it's slow". The real reason is that the mock returns what you told it to, so the query translation, constraints and transactions that fail in production never run. Fix: mock only your own boundary interfaces, and run code that touches SQL against the same engine as production ([Integration Testing](#integration-testing), below).

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

Constructing objects by hand clutters tests with irrelevant detail: a `new Customer { ... }` with twenty properties hides the one field the test is about, and breaks every test when a required property is added.

The hand-written answer is a **builder** (or *Object Mother*): it constructs a valid default object and lets each test override only the field it cares about, so the test's *intent* is legible. Two libraries take the same idea further, with different goals.

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

> **Pitfall:** random data in tests can produce **non-reproducible failures**: a test that fails one run in fifty because the generator happened to produce an edge case. Seed the generator; [Seeded test data](#seeded-test-data), below, explains why and how.

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

1. **EF Core In-Memory provider.** Fast, zero setup — and *dangerous*. It is not a relational database: it doesn't enforce unique indexes, doesn't support transactions or raw SQL, and evaluates queries differently. A test that passes against it can fail against real Postgres. The EF Core documentation calls using it as a database fake "highly discouraged".
2. **SQLite in-memory.** A real relational engine, genuinely fast, supports transactions. A big step up in fidelity — but its SQL dialect and type handling differ from Postgres/SQL Server, so provider-specific features and migrations may not translate.
3. **The real database engine.** Highest fidelity, catches the bugs that actually happen. Historically this meant a fragile shared test database or a heavyweight local install. **Testcontainers** solved that.

> **Pay attention.** **The in-memory provider never generates SQL, so nothing a database enforces can fail.** It runs your LINQ over .NET collections. `HasIndex(...).IsUnique()` is metadata only a relational provider turns into `CREATE UNIQUE INDEX`, so two rows with the same email both save (this chapter's *Prove it* program shows it next to SQLite, which rejects the second). String comparison is C#'s, case-sensitive, where SQL Server's default collation is not. A query the real provider can't translate never reaches a translator. Beginning a transaction throws by default; suites that silence that warning get a transaction that does nothing, so a rollback test passes for the wrong reason. Fix: run anything that touches SQL against the production engine.

> **Best practice:** test business logic against fast fakes, but test anything that touches SQL — queries, migrations, constraints, concurrency — against the *same engine you run in production*.

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

- **Time and dates.** `DateTime.Now` makes behaviour depend on when the test runs. Inject `TimeProvider` and control time explicitly ([Deterministic Tests](#deterministic-tests-time-async-and-test-data), below).
- **Ordering and shared state.** Tests that pass alone but fail together share mutable state. xUnit's new instance per test protects instance fields only; statics, singletons, fixtures and database rows survive from one test to the next.
- **Async and timing.** `Task.Delay` and "wait a bit then assert" race the scheduler. Await deterministic signals, not wall-clock guesses.
- **Test parallelism.** By default each test class is its own collection, and collections run in parallel: two classes touching the same row or static race each other, and the outcome depends on scheduling. Give each test its own data, or put the classes in one `[Collection]` to serialise them.
- **Non-deterministic data.** Unseeded random generators ([Seeded test data](#seeded-test-data), below).
- **External dependencies.** A test calling a real network service fails when the network hiccups. Fake the boundary.

> **Best practice:** treat a flaky test as a **P1 defect in the suite**, not an annoyance to retry past. Quarantine it (mark it, get it out of the blocking path) *and* file a ticket to fix or delete it — but never leave it silently retrying, because a suite you don't trust is a suite you don't have.

## Deterministic Tests: Time, Async, and Test Data

Every fix in the flaky-test list comes down to one rule: a test must control its inputs, so the same code gives the same result on every run. Two of those inputs hide in plain sight, and you can remove both by design: **wall-clock time** and **random test data**.

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

### Seeded test data

A random generator is an input too. One seeded from the clock produces a suite that passes 99 runs and fails the 100th, because the generator happened to produce an edge case. That isn't a flaky test, it's an *undiscovered bug*, but without the seed nobody can reproduce it. Seed generators with a fixed value in tests (Bogus: a fixed `Randomizer.Seed`), and if you vary the seed on purpose to explore more inputs, log it on failure so any failure *is* reproducible. Then treat each such failure as a real finding.

## Bringing It Together

Testing maturity is not measured in a coverage percentage or a count of tests. It's measured in a single capability: **can your team change the code with confidence and speed?** Everything in this chapter serves that. The pyramid tells you where to invest. Unit tests with clean AAA structure and honest names give fast, precise feedback. Test doubles, used with the judgment to know when *not* to mock, isolate units without ossifying them. Integration tests with Testcontainers verify the seams against real infrastructure. TDD applies design pressure; BDD aligns with stakeholders when there's an audience for it. Controlled time and seeded data keep the suite repeatable, and relentless hygiene around flakiness protects the trust that makes the whole edifice worthwhile. Whether the tests actually verify anything is the question mutation testing answers, in [Chapter 25](#chapter-25-observability-and-testing-at-scale).

Write tests that would fail if the behaviour broke, that read clearly when they do, and that survive a refactor of the code they cover. Do that, and your test suite stops being a chore you maintain and becomes the thing that lets you move fast without breaking things.

> **Capstone tie-in:** This chapter is exercised by ShopCore Step 2 (Prove It Works: Tests) — you'd unit-test the domain rules with xUnit and place an order through the HTTP surface against a Testcontainers PostgreSQL via `WebApplicationFactory`. See [Chapter 44](#chapter-44-capstone-one-project-growing-up).

## Prove it

The same model, with a unique index on `Email`, saves two users with the same email on the in-memory provider and on in-process SQLite, a real relational engine. No Docker needed. Predict both lines first.

`verify/path/InMemoryProvider/Program.cs` · run it from `verify/path` with `dotnet run --project InMemoryProvider`:

```csharp
// Prove it: a test on the EF Core in-memory provider passes where a relational engine fails it: a unique index.
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using var sqlite = new SqliteConnection("DataSource=:memory:");    // a real relational engine, still in-process
sqlite.Open();

foreach (var (provider, options) in new[]
{
    ("InMemory", new DbContextOptionsBuilder<Shop>().UseInMemoryDatabase("shop").Options),
    ("SQLite  ", new DbContextOptionsBuilder<Shop>().UseSqlite(sqlite).Options),
})
{
    await using var db = new Shop(options);
    await db.Database.EnsureCreatedAsync();
    db.Users.AddRange(new User { Email = "ada@example.com" }, new User { Email = "ada@example.com" });
    try
    {
        await db.SaveChangesAsync();
        Console.WriteLine($"{provider}: saved {await db.Users.CountAsync()} users with the same email");
    }
    catch (DbUpdateException e) { Console.WriteLine($"{provider}: {e.InnerException?.Message}"); }
}

class Shop(DbContextOptions<Shop> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    protected override void OnModelCreating(ModelBuilder model) => model.Entity<User>().HasIndex(u => u.Email).IsUnique();
}
class User { public int Id { get; set; } public string Email { get; set; } = ""; }
```

```text
InMemory: saved 2 users with the same email
SQLite  : SQLite Error 19: 'UNIQUE constraint failed: Users.Email'.
```

What to notice:

- **The model declares the index; only one engine enforces it.** `HasIndex(...).IsUnique()` is metadata. The relational provider turns it into `CREATE UNIQUE INDEX`, and the engine rejects the second row. The in-memory provider stores objects in .NET collections and has nothing to reject it with, so a "duplicate email is refused" test can only pass for real against an engine.
- **SQLite is a step, not the destination.** It enforces constraints and transactions, but its SQL dialect and string comparison differ from SQL Server's or PostgreSQL's (SQLite compares case-sensitively, SQL Server's default collation doesn't). For queries, migrations and concurrency, run the production engine in a container.

## Three questions

**1.** A repository test runs on `UseInMemoryDatabase`, saves two users with the same email, and passes. Production has a unique index on `Email`. What did the test prove, and what would you change?

<details>
<summary>Answer</summary>

Only that the C# runs. The EF Core in-memory provider executes your LINQ over .NET collections: no SQL is generated, so there is no unique index to violate, no transaction (beginning one throws by default, and silencing that warning makes it a no-op), no raw SQL, and string comparison follows C# rather than the database's collation. Test anything that touches SQL against the production engine in a container (Testcontainers), and keep fakes for business logic. [In-Memory vs Real Database](#in-memory-vs-real-database).
</details>

**2.** A test passes on its own and fails when the whole suite runs. What is the usual mechanism in xUnit, and how do you fix it?

<details>
<summary>Answer</summary>

State that outlives a test. xUnit creates a new instance of the test class for every test, so instance fields are safe; statics, singletons, class and collection fixtures, and database rows are not. By default each test class is its own collection and collections run in parallel, so two classes touching the same rows or static race each other, and the result depends on scheduling. Fix: each test arranges and owns its data (unique keys, its own rows), state is reset between tests, and classes that must share a resource go into one `[Collection]`. [Flaky Tests](#flaky-tests).
</details>

**3.** When is `Verify(x => x.Send(...), Times.Once)` the right assertion, and when is it why a refactoring broke forty tests?

<details>
<summary>Answer</summary>

It is right when the interaction is the observable behaviour at a boundary you own: "exactly one confirmation email is sent", "one `OrderPlaced` event is published". It is wrong as a check of internal calls: it pins the implementation, so a change that keeps the behaviour fails the test. Mock at the boundaries (network, clock, message bus), use real objects or fakes inside, and don't mock types you don't own. [When NOT to Mock](#when-not-to-mock).
</details>

## Check at work

**Inspect.** In your test projects, search for `UseInMemoryDatabase`, `Thread.Sleep`, `Task.Delay`, `DateTime.Now` and `DateTime.UtcNow`, and for `static` mutable fields in test classes. Sort each hit: a test that touches SQL on the in-memory provider, an uncontrolled clock, a fixed wait, or shared state. Then pick five tests at random and read only their names: can you tell which requirement broke if each one fails?

**Measure.** From your CI history, count the tests that failed and then passed on a rerun of the same commit over the last month, and the suite's run time per level (unit, integration). Each rerun-to-green is a flaky test to fix or quarantine; in a slow integration tier, check how many containers start per run: one per collection, shared through a fixture, is usually enough.

## Interview Questions

**Unit vs integration test?**
A **unit test** exercises one small piece (a class/method) in isolation with dependencies mocked — fast, focused, pinpoints failures. An **integration test** exercises several components together, often with a real database or HTTP host, to catch wiring and contract issues unit tests miss. You need both; the classic pyramid has many unit, fewer integration, fewest end-to-end.

**Mock vs stub?**
A **stub** provides canned answers to make the test run (returns a fixed value). A **mock** additionally *verifies interactions* — that a method was called, with what arguments, how many times. Use a stub when you only need to supply data, a mock when the behavior under test *is* the interaction (e.g. "does it publish the event?"). Over-mocking couples tests to implementation.

**What is TDD, in one breath?**
Red-green-refactor: write a failing test for the next small behavior, write the minimum code to pass it, then refactor with the test as a safety net — repeat. It drives design toward testable, small units and gives you a regression suite for free. The discipline is writing the test *first*.

**How do you test async code?**
Make the test method `async Task` and `await` the operation — never block with `.Result` in tests (it hides exceptions and can deadlock). Assert on the awaited result or the thrown exception (`await Assert.ThrowsAsync`). For time-dependent code, inject a clock/`TimeProvider` rather than sleeping.

**What makes a good test?**
Fast, isolated/independent (no order dependence, no shared state), deterministic (no flakiness from time, randomness, or network), readable (Arrange-Act-Assert, one logical assertion of behavior), and testing *behavior not implementation* so refactors don't break it. A test you don't trust is worse than no test.

**Red flag:** "Good tests means 100% code coverage" — coverage proves code ran, not that behavior was asserted; a suite can hit every line yet catch nothing.
