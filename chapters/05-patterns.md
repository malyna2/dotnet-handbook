# Chapter 5: Design Patterns & Principles

_⏱️ Estimated read time: ~50 min ·     6360 words (study pace)_

A senior developer is not someone who has memorized twenty-three patterns from a book. A senior developer is someone who can look at a tangle of code and *feel* where the seams should be, who reaches for a pattern the way a carpenter reaches for the right chisel, and who — crucially — knows when to leave the chisel in the box and just drive the nail.

This chapter is about developing that instinct. We will walk through the classic patterns and the enterprise patterns you actually meet in modern .NET, but the goal is not encyclopedic coverage. The goal is *judgment*: understanding the force that pushes you toward a pattern, the cost that pattern extracts, and the point at which the cure becomes worse than the disease.

## What a Design Pattern Actually Is

A design pattern is a named, reusable solution to a recurring design problem. That is the textbook definition, and it is nearly useless on its own. Here is the useful version.

A pattern is a *record of a trade-off that someone made enough times to give it a name*. When you say "Strategy pattern," you are not describing a class hierarchy — you are describing a decision to trade a little indirection for the ability to swap an algorithm at runtime. The class hierarchy is just the shape that decision leaves in the code.

This reframing matters because it tells you how to learn patterns. Do not memorize the UML. Memorize the *problem* each pattern solves and the *price* it charges. Then, when you meet that problem in the wild, the pattern will suggest itself.

Patterns also give teams a shared vocabulary. When a colleague says "let's put a decorator around the repository to add caching," a whole design communicates in eight words. That compression is real value — it is the reason patterns are worth learning even for developers who could invent the solutions themselves.

### The Danger of Overusing Patterns

Here is the uncomfortable truth that separates mid-level from senior: **most code does not need a pattern, and reaching for one prematurely is a form of harm.**

The failure mode has a name in the community — "pattern-itis" or "architecture astronautics." It looks like this: a developer learns the patterns, gets excited, and starts seeing them everywhere. A simple `if/else` becomes a Strategy with three classes and a factory. A two-method service grows an interface, an abstract base, and a decorator "for flexibility." Six months later, tracing a single request means opening eleven files, and nobody can find where the actual work happens.

> **Overuse warning:** Every pattern adds indirection, and indirection is a cost paid by every future reader of the code. A pattern is justified only when the flexibility it buys is flexibility you will actually use. Speculative flexibility — "we might need to swap the database someday" — is usually a bad trade. This is YAGNI (You Aren't Gonna Need It), and it is the single most important principle in this chapter.

The right mental model: patterns are a response to *pain you already feel*, not insurance against pain you imagine. Write the simple version first. When it starts to hurt — when you find yourself editing the same `switch` in five places, when a class has grown three unrelated reasons to change — *then* refactor toward the pattern that relieves that specific pain. This is why patterns are best learned alongside refactoring: they are destinations, and refactoring is the road.

With that warning firmly in place, let's build the toolkit.

## Creational Patterns

Creational patterns are about *how objects come into existence*. The common thread: they decouple the code that uses an object from the code that decides which concrete object to create and how to wire it up.

### Factory Method

The Factory Method pattern defines a method whose job is to create an object, deferring the decision of *which* concrete type to a subclass or to configuration. The calling code depends only on the abstraction.

The problem it solves: you have code that needs an object, but the exact type depends on context, and you do not want `new SomeConcreteClass()` scattered through your business logic. A raw `new` welds your code to a specific implementation; a factory method inserts a seam.

```csharp
public interface INotification
{
    Task SendAsync(string recipient, string message);
}

public sealed class EmailNotification : INotification
{
    public Task SendAsync(string recipient, string message) =>
        Console.Out.WriteLineAsync($"Email to {recipient}: {message}");
}

public sealed class SmsNotification : INotification
{
    public Task SendAsync(string recipient, string message) =>
        Console.Out.WriteLineAsync($"SMS to {recipient}: {message}");
}

// The factory method encapsulates the choice.
public static class NotificationFactory
{
    public static INotification Create(NotificationChannel channel) => channel switch
    {
        NotificationChannel.Email => new EmailNotification(),
        NotificationChannel.Sms   => new SmsNotification(),
        _ => throw new ArgumentOutOfRangeException(nameof(channel))
    };
}

public enum NotificationChannel { Email, Sms }
```

The caller writes `NotificationFactory.Create(channel)` and receives an `INotification`. It has no idea `SmsNotification` exists. If you later add a `PushNotification`, exactly one place changes.

> **Best practice for .NET:** In an application wired with dependency injection, you rarely write hand-rolled static factories like this. The DI container *is* your factory. For runtime selection among registered services, inject `IEnumerable<INotification>` or use the keyed services introduced in .NET 8 (`services.AddKeyedScoped<INotification, EmailNotification>("email")`). Hand-written factories still earn their place when creation logic is genuinely complex or lives in a library that shouldn't depend on a container.

### Abstract Factory (brief)

Where Factory Method creates one product, Abstract Factory creates *families* of related products that must be used together. The classic example is a cross-platform UI toolkit: a `WindowsWidgetFactory` produces a `WindowsButton` and a `WindowsCheckbox`, while a `MacWidgetFactory` produces the Mac equivalents, and you never accidentally mix a Windows button with a Mac checkbox because a single factory hands you the whole consistent set. In practice this pattern is heavy; you meet it in framework code (ADO.NET's `DbProviderFactory` is a real example) far more than you write it yourself.

### Builder

The Builder pattern separates the *construction* of a complex object from its *representation*, letting you assemble an object step by step. It shines when an object has many optional parameters, when construction order or validation matters, or when you want an immutable result built through a fluent, readable sequence.

The pain it addresses is the "telescoping constructor" — a constructor with eight parameters, half of them optional, where callers write `new Report(null, null, true, null, false, ...)` and nobody can tell what the arguments mean.

```csharp
public sealed class EmailMessage
{
    public string From { get; }
    public IReadOnlyList<string> To { get; }
    public string Subject { get; }
    public string Body { get; }
    public bool IsHtml { get; }
    public IReadOnlyList<string> Attachments { get; }

    private EmailMessage(string from, List<string> to, string subject,
                         string body, bool isHtml, List<string> attachments)
    {
        From = from; To = to; Subject = subject;
        Body = body; IsHtml = isHtml; Attachments = attachments;
    }

    public sealed class Builder
    {
        private string _from = "";
        private readonly List<string> _to = new();
        private string _subject = "";
        private string _body = "";
        private bool _isHtml;
        private readonly List<string> _attachments = new();

        public Builder From(string address) { _from = address; return this; }
        public Builder AddRecipient(string address) { _to.Add(address); return this; }
        public Builder WithSubject(string subject) { _subject = subject; return this; }
        public Builder WithHtmlBody(string html) { _body = html; _isHtml = true; return this; }
        public Builder WithTextBody(string text) { _body = text; _isHtml = false; return this; }
        public Builder Attach(string path) { _attachments.Add(path); return this; }

        public EmailMessage Build()
        {
            if (_from.Length == 0) throw new InvalidOperationException("Sender is required.");
            if (_to.Count == 0) throw new InvalidOperationException("At least one recipient is required.");
            return new EmailMessage(_from, _to, _subject, _body, _isHtml, _attachments);
        }
    }
}

// Usage reads like a sentence:
var email = new EmailMessage.Builder()
    .From("noreply@shop.com")
    .AddRecipient("customer@example.com")
    .WithSubject("Your order shipped")
    .WithHtmlBody("<h1>On its way!</h1>")
    .Attach("invoice.pdf")
    .Build();
```

Each method returns `this`, enabling the fluent chain. `Build()` is the single choke point where invariants are validated, so an `EmailMessage` cannot exist in an invalid state.

> **Modern C# note:** For simple cases, C# often gives you the builder's benefits for free. Object initializers with `required` and `init` properties (`new EmailMessage { From = "...", To = [...] }`) handle optional-and-readable construction without a builder class. Records with `with` expressions cover immutable copies. Reserve a full builder for when construction involves real logic — conditional steps, accumulation, staged validation — not merely for setting properties. You already use builders constantly, by the way: `WebApplication.CreateBuilder(args)` and `StringBuilder` are exactly this pattern.

### Singleton

The Singleton pattern ensures a class has exactly one instance and provides a global point of access to it. It is the most famous pattern and, in modern .NET, the one you should almost never implement by hand.

Here is the classic thread-safe form using `Lazy<T>`, so you recognize it:

```csharp
public sealed class ConfigurationCache
{
    private static readonly Lazy<ConfigurationCache> _instance =
        new(() => new ConfigurationCache());

    public static ConfigurationCache Instance => _instance.Value;

    private ConfigurationCache() { /* expensive one-time load */ }

    public string? Get(string key) => /* ... */ null;
}
```

`Lazy<T>` gives you thread-safe, lazy initialization for free — no double-checked locking to get subtly wrong. The private constructor prevents anyone from calling `new`.

Now the reasons this pattern has a bad reputation:

> **Pitfalls of Singleton:**
> - **It is global mutable state in disguise.** Any code, anywhere, can reach `ConfigurationCache.Instance` and mutate it. That is invisible coupling — the dependency does not appear in any constructor or method signature.
> - **It destroys testability.** You cannot substitute a fake in a unit test, because the dependency is hard-wired via a static property rather than injected. Tests also leak state into one another through the shared instance.
> - **Lifetime is tied to the process, not to a scope.** In a web app you frequently want "one per request," which a static singleton cannot express.

> **Best practice: prefer DI-managed lifetime over the Singleton pattern.** Register the type with singleton *lifetime* and let the container hand it out: `services.AddSingleton<IConfigurationCache, ConfigurationCache>()`. You get the single-instance guarantee, but the dependency is now explicit in constructors, fully mockable, and free of global static access. The *lifetime* is what you wanted; the *global access point* was never a feature — it was a liability. Reserve the hand-rolled Singleton for the rare cases where no container is available.

### Prototype (brief)

The Prototype pattern creates new objects by *cloning* an existing instance rather than constructing from scratch, useful when construction is expensive or when you want a copy of a configured object. In C# this maps to copy constructors, `ICloneable` (best avoided — its shallow/deep contract is ambiguous), or, most idiomatically, records with `with` expressions: `var modified = original with { Status = "Revised" };` produces a shallow clone with one property changed. That single language feature has made the explicit Prototype pattern nearly invisible in modern C#.

## Structural Patterns

Structural patterns are about *composition* — how you assemble objects and classes into larger structures while keeping those structures flexible.

### Adapter

The Adapter pattern wraps an incompatible interface in one your code expects, letting classes that couldn't otherwise cooperate work together. It is the electrical plug adapter of software: the appliance and the socket are both fine, they just don't fit, so you insert a thin thing between them.

You reach for Adapter constantly when integrating third-party libraries or legacy code. Your application defines the interface it *wants*; the adapter translates that to the interface the external code *offers*.

```csharp
// What our application wants to depend on.
public interface IPaymentGateway
{
    Task<bool> ChargeAsync(decimal amount, string currency, string cardToken);
}

// A third-party SDK we don't control — awkward, differently named API.
public sealed class StripeSdkClient
{
    public Task<StripeChargeResult> CreateChargeAsync(long amountInCents, string curr, string source)
        => Task.FromResult(new StripeChargeResult { Succeeded = true });
}
public sealed class StripeChargeResult { public bool Succeeded { get; set; } }

// The adapter: speaks our language on the outside, Stripe's on the inside.
public sealed class StripePaymentAdapter : IPaymentGateway
{
    private readonly StripeSdkClient _stripe;
    public StripePaymentAdapter(StripeSdkClient stripe) => _stripe = stripe;

    public async Task<bool> ChargeAsync(decimal amount, string currency, string cardToken)
    {
        long cents = (long)(amount * 100);                    // translate units
        var result = await _stripe.CreateChargeAsync(cents, currency, cardToken);
        return result.Succeeded;                              // translate the result shape
    }
}
```

Your domain code depends on `IPaymentGateway` and never sees Stripe. Swap to PayPal by writing a `PayPalPaymentAdapter` — no business logic changes. The adapter also becomes the one place unit conversions and quirks live, keeping that mess quarantined.

### Decorator

The Decorator pattern attaches new behavior to an object by wrapping it in another object that shares the same interface. Because the wrapper implements the same interface, callers can't tell the difference — and you can stack decorators to layer behavior, each one adding a slice of responsibility.

This is one of the most valuable patterns in the .NET world, and it maps directly onto things you already use. It is the answer to "I want to add caching / logging / retries to this service without touching the service itself" — a direct application of the Open/Closed Principle.

```csharp
public interface IProductRepository
{
    Task<Product?> GetByIdAsync(int id);
}

public sealed class SqlProductRepository : IProductRepository
{
    public async Task<Product?> GetByIdAsync(int id)
    {
        // hit the database
        await Task.Delay(50);
        return new Product(id, "Widget");
    }
}

// A decorator: same interface, wraps another instance, adds caching.
public sealed class CachingProductRepository : IProductRepository
{
    private readonly IProductRepository _inner;
    private readonly IMemoryCache _cache;

    public CachingProductRepository(IProductRepository inner, IMemoryCache cache)
    {
        _inner = inner;
        _cache = cache;
    }

    public async Task<Product?> GetByIdAsync(int id)
    {
        if (_cache.TryGetValue(id, out Product? cached))
            return cached;

        var product = await _inner.GetByIdAsync(id);
        if (product is not null)
            _cache.Set(id, product, TimeSpan.FromMinutes(5));
        return product;
    }
}

public record Product(int Id, string Name);
```

The `CachingProductRepository` *has an* `IProductRepository` and *is an* `IProductRepository`. It adds caching and delegates the real work inward. You could wrap that in turn with a `LoggingProductRepository`, then a `RetryingProductRepository`, composing behavior like layers of an onion. Each class has exactly one reason to change.

> **Relate to ASP.NET Core:** The middleware pipeline is the Decorator pattern (combined with Chain of Responsibility) operating on the HTTP request. Each middleware wraps the next, optionally doing work before and after calling `await _next(context)`. When you write authentication, logging, or exception-handling middleware, you are decorating the request pipeline.

> **DI decoration:** The built-in container has no first-class decorator registration, which is why the **Scrutor** library is near-ubiquitous: `services.Decorate<IProductRepository, CachingProductRepository>()`. It registers the decorator so the container injects the inner implementation automatically. This is the idiomatic way to add cross-cutting concerns to a service in .NET without editing the service.

### The Rest, Briefly

- **Facade** provides a single simplified interface over a complicated subsystem. When you write an `OrderService` with a `PlaceOrder` method that internally coordinates inventory, payment, and shipping, that service is a facade. It reduces the surface area callers must understand.
- **Proxy** provides a stand-in that controls access to another object — for lazy loading, access control, or remoting. EF Core's lazy-loading proxies and the `HttpClient`-based typed clients that talk to remote services are proxies. It looks structurally like Decorator, but the *intent* differs: Proxy controls access to the same conceptual object; Decorator adds behavior to it.
- **Composite** lets you treat individual objects and groups of objects uniformly through a shared interface — a file-system tree where a folder and a file both expose `GetSize()`. Reach for it whenever you model recursive part-whole hierarchies.
- **Bridge** separates an abstraction from its implementation so the two can vary independently, avoiding a combinatorial explosion of subclasses. It is rare in application code; think of it when you notice you'd otherwise need `RedButton`, `BlueButton`, `RedCheckbox`, `BlueCheckbox`... and want to split "shape" from "color."
- **Flyweight** shares immutable state between many objects to save memory when you have a huge number of similar instances. .NET's string interning is a flyweight. You'll rarely implement it outside of performance-critical scenarios like rendering or game engines.

## Behavioral Patterns

Behavioral patterns are about *how objects communicate and how responsibility is assigned* — the algorithms and flows of control between collaborating objects.

### Strategy

The Strategy pattern defines a family of interchangeable algorithms behind a common interface and makes them swappable at runtime. It is the antidote to the sprawling `switch` statement that keeps growing new cases.

The problem: you have several ways to do one thing (calculate shipping, compress a file, rank search results), the choice varies, and you don't want that choice tangled into one giant conditional that everyone has to edit.

```csharp
public interface IShippingStrategy
{
    decimal CalculateCost(Order order);
}

public sealed class StandardShipping : IShippingStrategy
{
    public decimal CalculateCost(Order order) => 5.00m + order.Weight * 0.50m;
}

public sealed class ExpressShipping : IShippingStrategy
{
    public decimal CalculateCost(Order order) => 15.00m + order.Weight * 1.20m;
}

public sealed class FreeShipping : IShippingStrategy
{
    public decimal CalculateCost(Order order) => 0m;
}

public sealed class ShippingCalculator
{
    private readonly IShippingStrategy _strategy;
    public ShippingCalculator(IShippingStrategy strategy) => _strategy = strategy;
    public decimal Calculate(Order order) => _strategy.CalculateCost(order);
}

public record Order(decimal Weight);
```

Adding "overnight shipping" means writing one new class — the existing strategies and the calculator are untouched (Open/Closed again). Note that a strategy with a single method is really just a function; in C# you can often pass a `Func<Order, decimal>` instead of defining an interface. Use the interface when the strategy is stateful, has multiple methods, or needs DI registration; use the delegate when it's genuinely one function.

### Observer

The Observer pattern lets an object (the subject) notify a list of dependents (observers) automatically when its state changes, without the subject knowing who they are. It is publish-subscribe at the object level.

You almost never implement raw Observer in C#, because the language and framework give you three built-in expressions of it:

1. **Events and delegates** — the classic `event EventHandler` mechanism is Observer baked into the language. `button.Click += OnClick` subscribes an observer.
2. **`IObservable<T>` / `IObserver<T>`** — the reactive interfaces in the BCL, the foundation of Reactive Extensions (Rx.NET) for composing asynchronous event streams with LINQ-style operators.
3. **Message/event buses** — `INotificationHandler` in MediatR, or a domain-event dispatcher, is Observer at the application level.

```csharp
public sealed class StockTicker
{
    // The 'event' keyword IS the Observer pattern in C#.
    public event Action<string, decimal>? PriceChanged;

    public void UpdatePrice(string symbol, decimal price)
        => PriceChanged?.Invoke(symbol, price);   // notify all observers
}

// Observers subscribe without the ticker knowing anything about them.
var ticker = new StockTicker();
ticker.PriceChanged += (sym, price) => Console.WriteLine($"Logger: {sym} = {price}");
ticker.PriceChanged += (sym, price) => { /* update a dashboard */ };
ticker.UpdatePrice("MSFT", 425.30m);
```

> **Pitfall:** Events are the classic .NET memory leak. If an observer subscribes (`+=`) but never unsubscribes (`-=`), the subject holds a reference to it, keeping it alive for the subject's lifetime. Long-lived subjects with short-lived subscribers leak. Always unsubscribe, or use weak-event patterns / `IDisposable` subscriptions (which is exactly what `IObservable<T>` gives you — subscribing returns an `IDisposable` you dispose to unsubscribe).

### Mediator

The Mediator pattern introduces an object that encapsulates how a set of objects interact, so those objects no longer refer to each other directly — they talk *through* the mediator. It turns a tangled many-to-many web of dependencies into a tidy hub-and-spoke.

In .NET this pattern is synonymous with the **MediatR** library, which most teams use to implement CQRS-style request handling. Instead of a controller depending on five services, it depends on one `IMediator` and sends a request; MediatR routes it to the single handler that knows how to process it.

```csharp
// A request (the message) — carries data, knows nothing about its handler.
public record GetCustomerByIdQuery(int CustomerId) : IRequest<CustomerDto>;

// The handler — the only thing that knows how to satisfy this request.
public sealed class GetCustomerByIdHandler : IRequestHandler<GetCustomerByIdQuery, CustomerDto>
{
    private readonly ICustomerRepository _repo;
    public GetCustomerByIdHandler(ICustomerRepository repo) => _repo = repo;

    public async Task<CustomerDto> Handle(GetCustomerByIdQuery request, CancellationToken ct)
    {
        var customer = await _repo.GetByIdAsync(request.CustomerId);
        return new CustomerDto(customer.Id, customer.Name);
    }
}

// The controller depends on ONE thing, the mediator.
[ApiController, Route("customers")]
public sealed class CustomersController : ControllerBase
{
    private readonly IMediator _mediator;
    public CustomersController(IMediator mediator) => _mediator = mediator;

    [HttpGet("{id}")]
    public async Task<CustomerDto> Get(int id) =>
        await _mediator.Send(new GetCustomerByIdQuery(id));
}

public record CustomerDto(int Id, string Name);
```

The controller and handler are fully decoupled — neither references the other's type. MediatR's pipeline behaviors also let you insert cross-cutting concerns (validation, logging, transactions) around every request, which is Chain of Responsibility layered on top of Mediator.

> **Overuse warning:** MediatR is popular to the point of cargo-culting. For a CRUD app with thin controllers, routing everything through an in-memory mediator can add ceremony — an extra request class and handler class per operation — without buying decoupling you actually need. Use it when the indirection pays for itself: many handlers, cross-cutting pipeline behaviors, or a genuine desire to keep the transport (controllers) ignorant of the application layer. A three-endpoint service does not need it.

### The Rest, Briefly

- **Command** encapsulates a request as an object, letting you parameterize, queue, log, and undo operations. A MediatR request is a command; so is any `ICommand` you push onto a queue. Undo/redo stacks are the canonical use.
- **Template Method** defines the skeleton of an algorithm in a base class and lets subclasses override specific steps. ASP.NET's `ControllerBase` and many framework base classes use it. It's the inheritance-based cousin of Strategy (which favors composition).
- **Chain of Responsibility** passes a request along a chain of handlers until one handles it. This *is* the ASP.NET Core middleware pipeline: each component decides whether to handle the request, short-circuit, or pass it to the next. It's also how MediatR pipeline behaviors and message-processing pipelines work.
- **State** lets an object alter its behavior when its internal state changes, by delegating to a state object — cleaner than a giant `switch (_state)` scattered across methods. Order lifecycles (Pending → Paid → Shipped) are the classic fit.
- **Visitor** separates an algorithm from the object structure it operates on, letting you add new operations without modifying the objects. It's powerful but notoriously verbose; you meet it in compilers and expression-tree processing. C# pattern matching (`switch` on type) often replaces it more readably.
- **Iterator** provides sequential access to elements without exposing the underlying structure. This is `IEnumerable<T>` / `IEnumerator<T>`, and `yield return` is the language giving you iterators for free. You use this pattern every time you write `foreach`.
- **Memento** captures an object's internal state so it can be restored later, without violating encapsulation. Undo systems and snapshots use it.

## Enterprise & Application Patterns

These aren't in the original GoF catalog, but they dominate day-to-day .NET architecture. This is where senior-level judgment shows most.

### Repository & Unit of Work

The Repository pattern abstracts data access behind a collection-like interface (`GetById`, `Add`, `Remove`), so business logic doesn't know whether data lives in SQL, a document store, or memory. Unit of Work tracks a set of changes and commits them as a single atomic transaction.

```csharp
public interface IOrderRepository
{
    Task<Order?> GetByIdAsync(int id);
    Task AddAsync(Order order);
    void Remove(Order order);
}

public interface IUnitOfWork
{
    IOrderRepository Orders { get; }
    Task<int> SaveChangesAsync();   // commit everything atomically
}
```

Here is the senior-level nuance you must understand:

> **The great Repository-over-EF-Core debate.** EF Core's `DbContext` is *already* a Unit of Work (it tracks changes and commits them via `SaveChanges`), and `DbSet<T>` is *already* a repository (a queryable collection abstraction). So wrapping EF Core in your own repository and unit-of-work layer often means building an abstraction over an abstraction.

Arguments **against** adding your own repository over EF Core:
- It frequently leaks. To keep things efficient you end up exposing `IQueryable`, which drags EF's semantics right back through your "abstraction."
- Generic `Repository<T>` implementations tend toward a lowest-common-denominator API that either hides EF's powerful features (projections, `Include`, split queries) or re-exposes them awkwardly.
- The stated benefit — "we can swap the database" — almost never happens, and if it did, the query differences would break your abstraction anyway.

Arguments **for**:
- **Testability and boundary clarity** in a clean/hexagonal architecture: the domain layer depends on `IOrderRepository`, not on EF Core, keeping infrastructure out of the core.
- It gives you a home for **named, intention-revealing queries** (`GetOverdueOrdersAsync`) instead of duplicating LINQ across the app.

> **Best practice:** Don't reflexively build a generic repository over EF Core. If you want the boundary, prefer *specific* repositories with meaningful, use-case-driven methods that return materialized results (not `IQueryable`). For many applications, using `DbContext` directly — or the query side going straight through EF while commands go through repositories — is the pragmatic, honest choice. Decide based on your architecture's need for a domain boundary, not out of habit.

### Specification

The Specification pattern encapsulates a query or a business rule as a reusable, composable object. It solves the problem of query logic (`IsActive && SignedUpBefore(x)`) being duplicated and scattered.

```csharp
public interface ISpecification<T>
{
    Expression<Func<T, bool>> ToExpression();
}

public sealed class ActiveCustomerSpec : ISpecification<Customer>
{
    public Expression<Func<Customer, bool>> ToExpression() => c => c.IsActive;
}

public sealed class PremiumCustomerSpec : ISpecification<Customer>
{
    public Expression<Func<Customer, bool>> ToExpression() => c => c.TotalSpent > 10_000m;
}

// Because they return expressions, EF Core can translate them to SQL.
var spec = new ActiveCustomerSpec();
var activeCustomers = await dbContext.Customers
    .Where(spec.ToExpression())
    .ToListAsync();

public record Customer(bool IsActive, decimal TotalSpent);
```

Because specifications return `Expression<Func<T, bool>>`, EF Core translates them to SQL — the rule runs in the database, not in memory. Real-world implementations (the popular **Ardalis.Specification** library) let you compose specs with `And`/`Or` and also encapsulate `Include`, ordering, and paging. Specification pairs naturally with Repository: `GetAsync(ISpecification<T> spec)` gives you one flexible query method instead of dozens of named ones.

### CQRS

Command Query Responsibility Segregation splits your model in two: **commands** that change state and **queries** that read it travel through separate paths, often with separate models optimized for each.

The insight: reads and writes have different shapes. Writes need validation, business rules, and a rich domain model. Reads just need fast, flat data shaped for a screen. Forcing both through one model compromises both.

```csharp
// Write side: a command with rules, routed to a handler.
public record CreateOrderCommand(int CustomerId, List<OrderLine> Lines) : IRequest<int>;

// Read side: a query returning a flat DTO shaped for the UI.
public record GetOrderSummaryQuery(int OrderId) : IRequest<OrderSummaryDto>;
public record OrderSummaryDto(int Id, string CustomerName, decimal Total, string Status);
```

At its simplest, CQRS is just this separation of command and query objects (naturally expressed with MediatR). It does *not* require separate databases, event sourcing, or eventual consistency — those are advanced variants for high-scale systems.

> **Overuse warning:** Full CQRS with separate read/write databases and event sourcing is a heavyweight architecture that many teams adopt because it sounds sophisticated, then drown in the complexity of eventual consistency. Start with the lightweight version: separate command and query handlers against the *same* database. Add the heavy machinery only when a proven scaling need demands it. Lightweight CQRS is cheap and clarifying; full CQRS is expensive and situational.

### Options Pattern

The Options pattern is the idiomatic .NET way to bind configuration to strongly typed classes and inject them where needed, instead of reading magic strings from `IConfiguration` everywhere.

```csharp
public sealed class SmtpOptions
{
    public const string SectionName = "Smtp";
    public string Host { get; set; } = "";
    public int Port { get; set; } = 587;
    public bool UseSsl { get; set; } = true;
}

// Registration (Program.cs):
builder.Services.Configure<SmtpOptions>(
    builder.Configuration.GetSection(SmtpOptions.SectionName));

// Consumption — inject the typed options, not raw configuration.
public sealed class EmailSender
{
    private readonly SmtpOptions _options;
    public EmailSender(IOptions<SmtpOptions> options) => _options = options.Value;
}
```

Inject `IOptions<T>` for values fixed at startup, `IOptionsSnapshot<T>` for per-request reload of changed config, and `IOptionsMonitor<T>` for change notifications in singletons. You also get validation: `.ValidateDataAnnotations().ValidateOnStart()` fails fast at boot if configuration is invalid — far better than a `NullReferenceException` at 3 a.m. This is one pattern you should use by default; it's simply how configuration is done in modern .NET.

### Result Pattern & Railway-Oriented Programming

The Result pattern makes success and failure *explicit return values* rather than exceptions. A method returns a `Result<T>` that is either a success carrying a value or a failure carrying an error. This is for *expected* failures — validation errors, "not found," business-rule violations — that are part of normal flow, not exceptional.

```csharp
public readonly struct Result<T>
{
    public bool IsSuccess { get; }
    public T? Value { get; }
    public string? Error { get; }

    private Result(bool ok, T? value, string? error)
    {
        IsSuccess = ok; Value = value; Error = error;
    }

    public static Result<T> Success(T value) => new(true, value, null);
    public static Result<T> Failure(string error) => new(false, default, error);

    // The 'bind' that enables railway-oriented programming:
    // chain the next step only if we're still on the success track.
    public Result<TNext> Then<TNext>(Func<T, Result<TNext>> next)
        => IsSuccess ? next(Value!) : Result<TNext>.Failure(Error!);
}
```

**Railway-oriented programming** is the metaphor: picture two parallel train tracks, success and failure. Each operation is a switch. As long as steps succeed, you stay on the success track; the moment one fails, you shunt onto the failure track and every subsequent step is skipped, carrying the original error to the end. `Then` (also called `Bind`) implements the switch.

```csharp
Result<Order> result = ValidateOrder(request)
    .Then(ReserveInventory)
    .Then(ChargePayment)
    .Then(CreateShipment);

return result.IsSuccess
    ? Results.Ok(result.Value)
    : Results.BadRequest(result.Error);
```

> **Why prefer Result over exceptions for expected failures?** Exceptions are for the *exceptional* — the truly unexpected. Using them for ordinary control flow (a user typed a bad email) is expensive (stack-trace capture), hides the failure from the method's signature (you can't tell `Order Process()` might fail without reading its body), and encourages catch-all handlers that swallow bugs. A `Result<T>` return type makes failure part of the contract, visible and impossible to ignore. Reserve exceptions for genuinely exceptional conditions like a dropped database connection. Libraries such as **FluentResults** and **CSharpFunctionalExtensions** provide production-ready implementations.

### Null Object & Guard Clauses (brief)

- **Null Object:** Instead of returning `null` and forcing callers to null-check, return a benign object that implements the interface and does nothing. A `NullLogger` that silently discards messages lets callers log unconditionally without `if (logger is not null)`. It replaces scattered null checks with polymorphism — but use it only where "do nothing" is genuinely correct behavior, not to paper over a missing value that callers *should* handle.
- **Guard Clauses:** Validate preconditions at the top of a method and exit early, keeping the happy path unindented. `if (order is null) throw new ArgumentNullException(nameof(order));` up front beats wrapping the whole method body in an `if`. Modern C# and libraries like **Ardalis.GuardClauses** streamline this: `Guard.Against.Null(order);` or `ArgumentNullException.ThrowIfNull(order);`. Guard clauses are a small habit with an outsized effect on readability.

## Principles: The Foundation Under the Patterns

Patterns are specific moves; principles are the strategy that tells you which move to make. If you internalize the principles, most patterns become obvious — and, just as importantly, you learn when *not* to reach for one.

### SOLID

Five principles that together push you toward code that is easy to change.

**S — Single Responsibility Principle.** A class should have one reason to change. Put differently: it should answer to one stakeholder or concern.

```csharp
// BEFORE: this class has three reasons to change — report format,
// business rules, and delivery mechanism all live together.
public class InvoiceService
{
    public decimal CalculateTotal(Invoice inv) { /* business rules */ return 0; }
    public string RenderPdf(Invoice inv) { /* formatting */ return ""; }
    public void SendEmail(Invoice inv) { /* delivery */ }
}

// AFTER: each concern is separable and independently testable.
public class InvoiceCalculator { public decimal CalculateTotal(Invoice inv) => 0; }
public class InvoicePdfRenderer { public string Render(Invoice inv) => ""; }
public class InvoiceEmailSender { public void Send(Invoice inv) { } }
public record Invoice;
```

When the PDF library changes, only the renderer changes. When tax rules change, only the calculator changes. That isolation is the whole point.

**O — Open/Closed Principle.** Software should be open for extension but closed for modification — you should add behavior by adding code, not editing existing, tested code.

```csharp
// BEFORE: every new shipping method edits this switch. Closed for extension.
public decimal Cost(string method, Order o) => method switch
{
    "standard" => 5m,
    "express"  => 15m,
    _ => throw new ArgumentException()
};

// AFTER: the Strategy pattern from earlier. A new method = a new class.
// The calculator is never touched again.
```

Open/Closed is *why* Strategy, Decorator, and Factory exist. When you add a case to a `switch` for the third time, that's the principle telling you to refactor.

**L — Liskov Substitution Principle.** Subtypes must be usable anywhere their base type is expected, without surprising the caller. A derived class must honor the base class's contract.

```csharp
// VIOLATION: Square "is a" Rectangle mathematically, but overriding the
// setters to keep sides equal breaks code that sets width and height
// independently and expects them to stay independent.
public class Rectangle { public virtual int Width { get; set; } public virtual int Height { get; set; } }
public class Square : Rectangle
{
    public override int Width { set { base.Width = base.Height = value; } }
    public override int Height { set { base.Width = base.Height = value; } }
}
// A test expecting (w=5, set h=4 => area 20) suddenly gets 16. The subtype lied.
```

The fix is usually to rethink the hierarchy (composition, or a shared `IShape` interface) rather than force an "is-a" that isn't behaviorally true. LSP is a warning about inheritance abused for code reuse.

**I — Interface Segregation Principle.** Don't force clients to depend on methods they don't use. Prefer many small, focused interfaces over one fat one.

```csharp
// BEFORE: a printer that only prints is forced to implement scanning and faxing.
public interface IMachine { void Print(); void Scan(); void Fax(); }

// AFTER: split by capability; a class implements only what it truly does.
public interface IPrinter { void Print(); }
public interface IScanner { void Scan(); }
// A simple printer implements IPrinter alone; a multifunction device implements both.
```

Fat interfaces spread coupling: a change to `Fax()` recompiles and re-tests every implementer, even ones that stubbed it with `throw new NotImplementedException()` — itself an LSP violation waiting to happen.

**D — Dependency Inversion Principle.** High-level modules should depend on abstractions, not on low-level concrete details; both depend on abstractions.

```csharp
// BEFORE: the high-level order logic is welded to a concrete SMTP class.
public class OrderProcessor
{
    private readonly SmtpEmailSender _sender = new();  // hard dependency
}

// AFTER: depend on an abstraction, injected in. The concrete type is chosen
// at composition time, and tests substitute a fake freely.
public class OrderProcessor
{
    private readonly IEmailSender _sender;
    public OrderProcessor(IEmailSender sender) => _sender = sender;
}
public interface IEmailSender { void Send(string to, string body); }
```

Dependency Inversion is the principle behind the entire .NET dependency injection system. Every constructor that takes an interface instead of a `new`-ed concrete class is applying it. This is the principle that makes all the others practical.

### The Rest of the Toolkit

- **DRY (Don't Repeat Yourself):** Every piece of *knowledge* should have one authoritative representation. Note "knowledge," not "text" — two methods that look identical but change for different reasons are *not* a violation, and merging them creates false coupling. Over-eager DRY is a real senior-level mistake; sometimes a little duplication is cheaper than the wrong abstraction.
- **KISS (Keep It Simple, Stupid):** Prefer the simplest solution that works. The clever one-liner you're proud of is a liability the next reader must decode.
- **YAGNI (You Aren't Gonna Need It):** Don't build for imagined future requirements. The abstraction you add "just in case" is usually the wrong one when the case finally arrives — and pure cost until then. This is the principle that reins in pattern overuse.
- **Separation of Concerns:** Different aspects of a system — UI, business logic, data access — belong in different modules. Layered and clean architectures are this principle scaled up.
- **Law of Demeter (principle of least knowledge):** A method should talk only to its immediate collaborators, not reach through them. `order.Customer.Address.Country.Code` is a "train wreck" that couples you to the whole object graph; ask the nearest object for what you need instead. (LINQ chains are a deliberate exception — they operate on one pipeline, not a web of distinct objects.)
- **Composition over Inheritance:** Prefer assembling behavior from small, injected parts over deep inheritance trees. Inheritance is rigid — it's decided at compile time, exposes you to fragile-base-class problems, and forces the whole contract of the parent onto the child. Composition (the engine behind Strategy, Decorator, and DI) is flexible and testable. When you're about to write `class X : Y` for code reuse rather than a true "is-a" relationship, stop and ask whether X should instead *have* a Y.

## Closing Thought

Notice how many of these patterns dissolved into ordinary C# — Iterator became `yield`, Prototype became `with`, Observer became `event`, Strategy became `Func<>`, Singleton became a DI lifetime. That is not a coincidence. As a language and its ecosystem mature, yesterday's patterns become today's built-in features. The patterns worth carrying in your head are the ones the language *hasn't* absorbed and the *principles* underneath all of them.

So hold the patterns lightly and the principles tightly. When you feel real pain — a growing `switch`, a class with three jobs, a test you can't write because a dependency is hard-wired — let a pattern relieve exactly that pain and no more. Resist the urge to build cathedrals of indirection for problems you don't yet have. The mark of a senior developer is not how many patterns they can deploy, but how much needless complexity they can keep out of the codebase.
