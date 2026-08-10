# Chapter 5: Design Patterns, Principles & Clean Code

_⏱️ Estimated read time: ~1 h 35 min · 12694 words (study pace)_

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

> **A note on licensing:** In April 2025, MediatR's maintainer (Jimmy Bogard) announced that MediatR and AutoMapper are moving to commercial licensing to fund their maintenance — existing versions remain under their open-source licenses, but new major versions are commercial. The practical consequence: the default of "just add MediatR" now deserves a license check, which only strengthens the advice above to ask whether you need the library at all. Hand-rolled handler interfaces plus DI cover most MediatR use, and a still-free library like **Wolverine** (MIT-licensed as of v4) is a fuller alternative; for mapping, manual code or the MIT-licensed, source-generated **Mapperly** are solid alternatives.

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
- **Guard Clauses:** Validate preconditions at the top of a method and exit early, keeping the happy path unindented. `if (order is null) throw new ArgumentNullException(nameof(order));` up front beats wrapping the whole method body in an `if`. Modern C# and libraries like **Ardalis.GuardClauses** streamline this: `Guard.Against.Null(order);` or `ArgumentNullException.ThrowIfNull(order);`. .NET 8 rounds out the built-in helpers with the range-checking family — `ArgumentOutOfRangeException.ThrowIfNegative(count)`, `ThrowIfZero(...)`, and `ThrowIfGreaterThan(...)` — so most preconditions need no hand-written `if`/`throw` at all. Guard clauses are a small habit with an outsized effect on readability.

## Exception Handling Strategy

Almost every codebase has a *style* of exception handling, and almost none have a *strategy*. The style is visible: `try`/`catch` blocks sprinkled wherever someone was once burned, a `catch (Exception ex) { _logger.LogError(ex.Message); throw; }` copied from file to file, a global handler that returns `"An error occurred"` and nothing else. The strategy is the thing that answers three questions, and this section answers them in order: **where do I catch, what do I log, and what do I surface?** Every one of those answers depends on a prior question that most code never asks.

### Classify the Failure First

You cannot decide how to handle a failure until you know what *kind* of failure it is. There are three, and they want three completely different mechanisms.

**A bug.** The code is wrong. A `NullReferenceException`, an `InvalidCastException`, an index off the end of an array, an `InvalidOperationException` because an object was in a state its own invariants say is impossible. There is no correct handling for a bug at runtime, because the process no longer knows what is true. The right response is to *not catch it*: let it tear down the current request, let the boundary log it with full fidelity, let the alert fire, and go fix the code. A `catch` around a bug converts a loud, diagnosable failure into a quiet, undiagnosable one.

**An environmental or transient failure.** A socket reset, a connection timeout, a SQL deadlock victim, a 503 from a dependency that is mid-deploy. Nothing is wrong with your code and nothing is wrong with the request — the world was briefly unavailable. These are the only failures where *retry* is a coherent response, because the same call with the same inputs may well succeed a moment later. This is Polly's territory: see the resilience pipelines in [Chapter 3: ASP.NET Core & Web APIs](#chapter-3-aspnet-core-web-apis) for the `HttpClient` wiring, and [Chapter 9: Messaging & Distributed Systems](#chapter-9-messaging-distributed-systems) for the wider retry/circuit-breaker/idempotency picture.

**A domain rule violation.** The request is well-formed, the system is healthy, and the business says no: insufficient balance, coupon expired, order already shipped. Nothing here is exceptional — this is one of the outcomes the feature was designed to produce. This is exactly where the `Result<T>` from earlier in this chapter belongs, and it is the category most often mishandled, because throwing an `InsufficientFundsException` *works*, so nobody notices that it has made an ordinary business outcome invisible in the method signature.

| Failure kind | Examples | Mechanism | What the caller sees |
|---|---|---|---|
| **Bug** | Null deref, bad cast, broken invariant | Do not catch. Let it reach the outermost boundary. | 500 + a trace id; an alert pages someone |
| **Environmental / transient** | Socket reset, timeout, SQL deadlock (1205), 503 | Retry with backoff, circuit-break, fall back | Success after retry — or 503/504 + `Retry-After` when exhausted |
| **Domain rule violation** | Coupon expired, insufficient balance, already shipped | `Result<T>` / validation errors — *not* an exception | 409 / 422 with a stable machine-readable code |
| **Malformed input** | Bad JSON, missing required field | Model validation at the edge, before your code runs | 400 `ValidationProblemDetails` |

> **The classification is the design decision.** Everything downstream — whether to retry, whether to log at `Error` or `Warning`, whether the user sees a fixable message or an apology — is determined by which row you are in. Teams that argue about `try`/`catch` placement are usually arguing because they never agreed on the rows.

### Exceptions vs Result, Settled Properly

The earlier Result-pattern section made the case for `Result<T>`; here is the other half of the trade, stated honestly, because "exceptions are slow" is repeated far more often than it is understood.

**Exceptions are unignorable** — their single greatest property. If a method throws and you write no handler, the failure propagates and something eventually notices. Compare a method returning `Result<T>`: a caller can write `_ = DoTheThing();` and discard the failure entirely, and the compiler will not blink. Unignorability is why exceptions are right for the *exceptional*, where continuing is worse than stopping. **Results, in exchange, are visible in the signature and force a decision.** `Result<Order> Place(...)` tells you failure is expected without reading the body; `Order Place(...)` does not. The cost is signature pollution: `Result<T>` is viral, spreading up through every caller, and code that mixes both conventions gets the worst of each.

Now the performance, with the mechanism rather than folklore. A throw/catch pair costs on the order of **microseconds** — roughly 5–20 µs for a shallow stack, growing with depth, and worse under a debugger. Two things dominate. First, **the stack walk**: throwing does not simply jump, it walks frames outward looking for a handler whose filter matches, unwinding as it goes, so the same `throw` is cheap in a leaf method and expensive from twenty frames down a request pipeline. Second, **stack trace capture**: building the trace means resolving frames back to method metadata, which scales with depth again.

Put that in context, because context is the whole point. Ten microseconds once per failed HTTP request, against a budget of tens of milliseconds, is *noise* — nobody has ever had an outage because a 404 threw. Ten microseconds per row across 200,000 rows is **two seconds of pure overhead**, and that is a genuine, career-defining performance bug.

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

These details separate handling that helps diagnosis from handling that destroys it.

**`throw;` versus `throw ex;`.** `throw ex;` restarts the exception's journey from the current frame: the `StackTrace` is reset, and every frame *below* your catch — the frames that contain the actual bug — is erased. Your log then says the failure originated in the catch block, which is the one place it certainly did not. `throw;` rethrows the original, preserving the trace. There is no situation in which `throw ex;` is the right rethrow.

```csharp
catch (Exception ex)
{
    throw ex;   // ❌ stack trace now starts HERE — the real origin is erased
    throw;      // ✅ preserves the original trace
}
```

**Exception filters — and why they beat catch-inspect-rethrow.** `catch (X e) when (predicate)` looks like sugar for an `if` inside the catch, but the runtime treats it very differently. The filter expression runs during the **first pass**, *before the stack unwinds*. If the filter returns `false`, the exception continues outward with the stack still intact — no frames destroyed, and if it ultimately goes unhandled, a debugger or crash dump captures the state at the original throw site rather than at your catch. Catch-inspect-rethrow, by contrast, unwinds first and asks questions later.

```csharp
// ✅ Filter: decides before unwinding. Frames below stay intact.
catch (SqlException ex) when (IsTransient(ex)) { await RetryAsync(); }

// ❌ Catch, inspect, rethrow: the stack has already unwound by the time we look.
catch (SqlException ex) { if (!IsTransient(ex)) throw; await RetryAsync(); }
```

Filters are also the idiomatic way to branch on an error code (`when (ex.Number == 1205)`) or to add a side effect without handling — `catch (Exception ex) when (Log(ex))`, where `Log` returns `false`, logs at the throw site and lets the exception sail past untouched.

**`ExceptionDispatchInfo`.** When you must capture an exception now and rethrow it later — from a different thread, out of a stored task, after some bookkeeping — `throw capturedEx;` would reset the trace. `ExceptionDispatchInfo` exists precisely for this: it preserves the original stack and *appends* the new throw site instead of replacing it.

```csharp
ExceptionDispatchInfo? captured = null;
try { await DoWorkAsync(); }
catch (Exception ex) { captured = ExceptionDispatchInfo.Capture(ex); }
await CleanupAsync();
captured?.Throw();      // original stack trace intact, rethrow site appended
```

**`AggregateException` and `Task.WhenAll`.** When you `await Task.WhenAll(...)` and three tasks faulted, `await` unwraps and rethrows only the **first** exception — the other two are silently invisible unless you go looking. Retrieve the whole set from the task's `Exception` property. This is a common source of "we fixed the error and it still fails": you were only ever shown one of three. See [Chapter 8: Asynchronous & Concurrent Programming](#chapter-8-asynchronous-concurrent-programming) for the full behavior of aggregated faults.

```csharp
var task = Task.WhenAll(jobs);
try { await task; }                 // rethrows only the FIRST fault
catch (Exception)
{
    // task.Exception is the AggregateException carrying ALL of them.
    foreach (var inner in task.Exception!.InnerExceptions)
        _logger.LogError(inner, "Job failed");
    throw;
}
```

**`OperationCanceledException` is not a failure.** A cancelled operation is a *successful* response to a request to stop — a client closed the connection, a shutdown began, a timeout token fired. Logging it as an error trains your team to ignore errors, and in a busy API the client-disconnect case alone can drown a real incident in noise. Filter it out at the boundary and log at `Information` or `Debug`.

```csharp
catch (OperationCanceledException) when (ct.IsCancellationRequested)
{
    _logger.LogInformation("Request cancelled by client for order {OrderId}", id);
    return;   // no response — the caller is already gone
}
```

The filter matters here too: without `when (ct.IsCancellationRequested)` you also swallow the *timeout* case, which usually is a real problem worth surfacing.

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

That first argument is not a stylistic preference. Logging providers treat the exception parameter specially, serializing type, message, stack, and the full inner-exception chain as structured data; interpolating `ex.Message` into the template throws all of that away and, for good measure, destroys the message template that makes logs aggregatable. See [Chapter 13: Observability](#chapter-13-observability) for structured logging, message templates, and log scopes.

**The log-and-rethrow anti-pattern.** This is the most common exception mistake in enterprise .NET:

```csharp
// In the repository, the service, the handler, AND the controller — all four:
catch (Exception ex) { _logger.LogError(ex, "Something went wrong"); throw; }
```

One failure now produces four `Error` entries. They are not four problems and they are not even four *views* of the problem — they are the same exception, at four different stack depths, with four different timestamps, interleaved with other requests' logs. On-call now has to reconstruct that these four are one event before they can start. Your error rate metric is inflated fourfold. Your alert thresholds are meaningless. And the extra entries added no information the boundary's single entry would not have had, because the exception was already carrying its whole stack.

> **Best practice.** Log where you *handle*, not where you *pass through*. If a layer genuinely knows something the boundary cannot — a retry attempt count, the exact query that failed — log that as a `Warning` with the specific fact, and still let the boundary own the single `Error` for the failure itself.

### What to Surface

The response to the outside world is a **product decision**, not a debugging artifact. Never surface a stack trace, a SQL statement, a connection string, an internal type name, or raw inner-exception text: at best it confuses the caller, at worst it is a reconnaissance gift to an attacker (see the error-handling notes in [Chapter 14: Security](#chapter-14-security)).

What a caller does need is: **a stable machine-readable code** they can branch on, **a human-readable summary** they can act on, and **a correlation/trace id** they can quote to your support team. RFC 7807 `ProblemDetails` is the standard shape, and ASP.NET Core produces it natively — see [Chapter 3: ASP.NET Core & Web APIs](#chapter-3-aspnet-core-web-apis) for `AddProblemDetails` and `IExceptionHandler`, and [Chapter 13: Observability](#chapter-13-observability) for wiring the trace id that ties the response back to the log entry.

```csharp
public sealed class OrderingExceptionHandler(ILogger<OrderingExceptionHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext ctx, Exception ex, CancellationToken ct)
    {
        if (ex is not OrderingException ordering) return false;  // let the generic 500 handler take it
        logger.LogError(ex, "Ordering failure {ErrorCode} on {Path}",
                        ordering.ErrorCode, ctx.Request.Path);

        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status503ServiceUnavailable,
            Title  = "The order could not be processed.",
            Type   = $"https://errors.example.com/{ordering.ErrorCode}",
            // No stack, no SQL, no inner message. A code and an id.
            Extensions = { ["code"] = ordering.ErrorCode,
                           ["traceId"] = Activity.Current?.Id ?? ctx.TraceIdentifier }
        };
        ctx.Response.StatusCode = problem.Status!.Value;
        await ctx.Response.WriteAsJsonAsync(problem, ct);
        return true;
    }
}
```

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

### Process-Level Safety Nets

Below the request boundary sits the process, and it has its own failure modes.

**`BackgroundService`.** Since .NET 6, an unhandled exception in `ExecuteAsync` stops the **entire host** by default (`BackgroundServiceExceptionBehavior.StopHost`) — a deliberate change, because the previous behavior silently killed the service and left the process running as a hollow shell that looked healthy to every probe. Keep that default and put your `try`/`catch` *inside* the loop, so one bad message does not take down the worker while a genuinely broken worker still takes down the host and lets the orchestrator restart it. [Chapter 22: Background Processing, Scheduling & the Actor Model](#chapter-22-background-processing-scheduling-the-actor-model) covers the loop shape in detail.

```csharp
protected override async Task ExecuteAsync(CancellationToken stoppingToken)
{
    while (!stoppingToken.IsCancellationRequested)
    {
        try { await ProcessNextAsync(stoppingToken); }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        catch (Exception ex) { _logger.LogError(ex, "Work item failed; continuing."); }
        //  ↑ per-item boundary: one poisoned item must not kill the loop.
        //    An exception escaping ExecuteAsync itself stops the host — by design.
    }
}
```

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

## Clean Code & Code Smells

You will spend far more of your career reading code than writing it — easily ten times more. That single observation reorganizes your priorities: the reader, not the compiler, is the customer you are writing for. The principles above are the *structural* side of good code; clean code is the *local* side — what a single name, method, or file looks like up close, which is where most developers actually spend their day. And it is not a matter of taste: messy code slows every future change and quietly taxes every estimate your team gives.

> **Clarity beats cleverness.** The compiler does not reward you for a dense one-liner, and the next developer will silently curse you for it. Optimize for the person who has to understand this code under pressure at 2 a.m.

### Naming: the cheapest documentation you will ever write

A good name does the work of a comment for free and never goes stale; a bad name actively lies to you. Aim for **intention-revealing names** — the name should answer why the thing exists and what it does without forcing the reader to hunt elsewhere.

```csharp
// BEFORE — every name forces a mental lookup
public List<int[]> GetThem()
{
    var list1 = new List<int[]>();
    foreach (var x in theList)
        if (x[0] == 4)
            list1.Add(x);
    return list1;
}
```

```csharp
// AFTER — the names carry the meaning
public List<Cell> GetFlaggedCells()
{
    var flaggedCells = new List<Cell>();
    foreach (var cell in gameBoard)
        if (cell.IsFlagged)
            flaggedCells.Add(cell);
    return flaggedCells;
}
```

Nothing about the algorithm changed. What changed is that `cell.IsFlagged` tells you what `x[0] == 4` never could.

A handful of concrete rules cover most cases:

- **No abbreviations or encodings.** `custMgr`, `strName`, `bIsActive` save keystrokes and cost every reader a translation step; in a statically typed language the type is already in the declaration, and the IDE autocompletes.
- **Use searchable names.** A bare `7` cannot be grepped; `MaxRetryAttempts = 7` can be found, understood, and changed in one place.
- **Avoid disinformation.** Do not call something `accountList` if it is actually a `Dictionary`.
- **Classes are nouns, methods are verbs.** `InvoiceGenerator` and `CalculateTotal()`; a class named `Process` breaks the reader's model.
- **One word per concept.** If you `Fetch` in one place, `Retrieve` in another, and `Get` in a third, the reader wonders whether the difference is meaningful. It usually is not — pick one.
- **No mental mapping.** Name loop variables `customerIndex`, not `i`. Single letters earn their keep only in a tiny, conventional scope like a short LINQ lambda.

### Functions: small, focused, honest

The single most reliable structural rule for readable code is that **functions should be small and do one thing**. "One thing" is easier to feel than to define, but a useful test is the **single level of abstraction**: do not mix high-level policy with low-level mechanics. If one line calls `CalculatePricing(order)` and the next is fiddling with string indices, those belong at different levels and probably in different methods.

```csharp
// BEFORE — one function, three levels of abstraction, several responsibilities
public void ProcessOrder(Order order)
{
    if (order.Items.Count == 0) throw new InvalidOperationException("Empty order");

    decimal total = 0;
    foreach (var item in order.Items)
    {
        var line = item.UnitPrice * item.Quantity;
        if (item.Quantity >= 10) line *= 0.9m; // bulk discount
        total += line;
    }
    total += total * 0.2m; // VAT

    var conn = new SqlConnection(_connectionString);
    conn.Open();
    var cmd = new SqlCommand("INSERT INTO Orders ...", conn);
    cmd.ExecuteNonQuery();

    _smtp.Send(new MailMessage("shop@x.com", order.CustomerEmail, "Receipt", $"Total: {total}"));
}
```

```csharp
// AFTER — each function does one thing, all at one level of abstraction
public void ProcessOrder(Order order)
{
    ValidateOrder(order);
    var total = CalculateTotal(order);
    _orderRepository.Save(order, total);
    SendReceipt(order, total);
}

private static void ValidateOrder(Order order)
{
    if (order.Items.Count == 0)
        throw new InvalidOperationException("Cannot process an order with no items.");
}

private static decimal CalculateTotal(Order order)
{
    var subtotal = order.Items.Sum(LineTotal);
    return subtotal * (1 + VatRate);
}

private static decimal LineTotal(OrderItem item)
{
    var line = item.UnitPrice * item.Quantity;
    return item.Quantity >= BulkThreshold ? line * BulkDiscount : line;
}
```

`ProcessOrder` now reads like a table of contents. Notice too that the magic numbers became named constants (`VatRate`, `BulkThreshold`, `BulkDiscount`) — a smell we will return to shortly.

Some hard-won guidelines for function signatures:

- **Few parameters.** Zero to two are easy; more than three is a smell — introduce a *parameter object* that groups related arguments into a named type. It reads better and resists the classic bug of passing arguments in the wrong order.
- **No flag parameters.** `GenerateReport(true)` is unreadable at the call site — true *what*? A boolean parameter almost always means the function does two things; split it in two.
- **Avoid output parameters.** `out` and `ref` parameters that mutate the caller's variables surprise readers; return a value or a small record instead. (The idiomatic `TryParse` pattern is the sanctioned exception.)
- **Command-Query Separation.** A method should either *do* something (change state, return void) or *answer* something (return a value, change nothing) — not both. `if (SetAttribute("x"))` leaves the reader unsure whether it asks or acts.
- **No hidden side effects.** An `IsValid` that quietly initializes a session, or a `GetUser` that also updates a last-seen timestamp, betrays its name — a rich source of bugs.

### Comments: explain the WHY, not the WHAT

A comment is a small failure — an admission that the code could not express intent on its own. Sometimes that is unavoidable and the comment is exactly right; often it is a missed opportunity to rename a variable or extract a well-named method. The distinction that matters is **why versus what**: code already states *what* it does, and a comment restating that is noise that will drift out of date the moment someone edits the code without touching it.

```csharp
// BAD — restates the obvious, and will lie the day the code changes
// increment i by one
i++;

// GOOD — explains a non-obvious business reason the code cannot express
// The payment gateway rejects amounts over 10,000 in a single call,
// so we split large transfers into chunks. See INC-4821.
foreach (var chunk in transfers.Chunk(MaxTransferBatchSize))
    _gateway.Send(chunk);
```

Good comments earn their place: they explain *intent* behind a non-obvious choice, warn about consequences ("this is not thread-safe"), mark honest `// TODO:` and `// HACK:` debts, clarify a gnarly regex or algorithm, or carry required legal/license headers. Bad comments are redundant restatements, misleading or stale descriptions, and — worst of all — **commented-out code**. Delete dead code; version control remembers it, and a graveyard of commented blocks makes readers afraid to touch anything.

For **public APIs**, XML doc comments (`/// <summary>`) are the right kind of comment: they surface in IntelliSense, feed generated documentation, and describe a contract that consumers cannot see the implementation of. Document the public surface; keep private methods self-explanatory instead.

### Formatting, error handling, and everyday discipline

Formatting is not about beauty; it is about reducing the reader's cognitive load, and its single rule is **consistency** — which you should not enforce by hand. Push it into an `.editorconfig` and Roslyn analyzers so CI fails on drift (see the Tooling chapter); arguing about brace placement in code review wastes expensive human attention a tool settles for free. Beyond that, aim for **locality**: declare variables near first use, keep a private helper just below the method that calls it, and use blank lines as punctuation between distinct thoughts.

Error handling shapes how readable the *success* path is. Prefer exceptions to error codes — returning `-1` or `false` pollutes the happy path and makes ignoring failure the default — and reach for the Result pattern covered earlier when failure is *expected* rather than exceptional. Never swallow exceptions: an empty `catch { }` hides exactly the information you will want later. Fail fast — validate at the boundary and throw immediately rather than letting a bad value travel deep into the system — and flatten nesting with the guard clauses covered earlier. Finally, **do not return `null`** as a routine result: prefer an empty collection for "no results" (callers just `foreach`), a `Result<T>` for meaningful failure, and nullable reference types so any remaining risk is at least visible to the compiler.

Two closing habits round this out. The **Boy-Scout Rule**: leave the code a little cleaner than you found it — rename one confusing variable, delete one block of commented-out code each time you pass through; small improvement compounds and reverses entropy. And a healthy suspicion of **clever code**: the deeply nested ternary or fifteen-line LINQ trick is satisfying to write and miserable to read. Cleverness that saves a line but costs the reader a minute is a bad trade — this is KISS applied at the keyboard, and the senior move is the boring version your teammates understand instantly.

### Code smells: naming the pain

A **code smell** — the term popularized by Martin Fowler and Kent Beck — is a surface-level symptom that hints at a deeper design problem. A smell is not a bug (the code may work perfectly) and not automatically wrong; it is a *heuristic*, something worth a second look. Its real value is vocabulary: when you can name what bothers you ("this is Feature Envy"), you also know the standard refactorings that address it.

> **Smells guide, they do not dictate.** A smell is a prompt to *consider* a refactoring, not a rule that forces one. Sometimes the smelly version is genuinely the clearest option, and forcing a "clean" structure onto it makes things worse.

| Smell | The pain you feel | Refactor toward |
|---|---|---|
| **Long Method** | You scroll, lose the plot, can't test the middle | Extract Method; Decompose Conditional |
| **God Class / Large Class** | Every change lands here; constant merge conflicts | Extract Class; move behavior to collaborators |
| **Long Parameter List** | Unreadable call sites; arguments in the wrong order | Parameter Object |
| **Duplicated Code** | You fix a bug twice and miss the third copy | Extract Method/Class — one home per piece of knowledge |
| **Feature Envy** | A method keeps reaching into another class's data | Move Method to where the data lives |
| **Primitive Obsession** | Same validation scattered; invalid values circulate freely | Value Object |
| **Data Clumps** | The same field trio travels everywhere together | Extract Class (an `Address`, a `DateRange`) |
| **Shotgun Surgery** | One small change means edits across many files | Move Method/Field to consolidate the responsibility |
| **Divergent Change** | One class changes for unrelated reasons (SRP violated) | Extract Class along the axes of change |
| **Switch on Type** | The same `switch` duplicated; each new case is a hunt | Replace Conditional with Polymorphism; Strategy |
| **Message Chains** | `a.B().C().D()` breaks when anything in the chain moves | Hide Delegate; ask the nearest object (Demeter) |
| **Temporal Coupling** | Methods only work when called in a secret order | Redesign the API so misuse won't compile |
| **Speculative Generality** | Abstractions nobody uses that everyone pays for | Collapse Hierarchy; delete unused hooks (YAGNI) |
| **Comments as deodorant** | Prose apologizing for code that can't explain itself | Extract Method with a good name; Rename |
| **Magic Numbers / Strings** | Unexplained literals nobody dares to change | Named Constant; enum |

Several of these are the local, code-level face of the principles above: Divergent Change is SRP violated, Duplicated Code is DRY violated, Message Chains break the Law of Demeter, Speculative Generality is YAGNI ignored. The smell vocabulary and the principle vocabulary describe the same forces from different distances.

### A worked refactoring: Primitive Obsession → Value Object

Representing a domain concept as a bare primitive scatters its rules across the codebase and lets invalid values exist.

```csharp
// BEFORE — an email is "just a string", so validation lives everywhere and nowhere
public class Customer
{
    public string Email { get; set; } // could be "", "not-an-email", null...
}

// callers must remember to validate, and they won't, consistently
if (!string.IsNullOrEmpty(input) && input.Contains("@"))
    customer.Email = input;
```

```csharp
// AFTER — a value object makes an invalid email unrepresentable
public sealed record EmailAddress
{
    public string Value { get; }

    public EmailAddress(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || !value.Contains('@'))
            throw new ArgumentException($"'{value}' is not a valid email address.");
        Value = value.Trim().ToLowerInvariant();
    }

    public override string ToString() => Value;
}

public class Customer
{
    public EmailAddress Email { get; set; }
}
```

The validation now lives in exactly one place, the type system guarantees that any `EmailAddress` in the system is valid, and the domain reads in domain terms. This is the same move that dissolves Data Clumps — a `street`/`city`/`postcode`/`country` quartet that keeps appearing together wants to become an `Address` value object.

The other two headline refactorings you have already seen in this chapter. **Long Method → Extract Method** is exactly the `ProcessOrder` walkthrough in the Functions section above; Fowler's *Replace Temp with Query* is the same move applied to temporary variables — turn each recomputed temp into a named query method, accepting a little recomputation in exchange for readability. And **Switch on Type → Polymorphism** is the same move as the Strategy/Open-Closed example from earlier in this chapter: each case becomes a class, and a new case becomes a new class instead of another edit to a `switch` that is quietly being duplicated across the codebase.

### Refactor under tests, in tiny steps

Notice that the right-hand column of the smells table reduces to a small vocabulary of named moves — Extract Method/Class, Move Method, Parameter Object, Value Object, Replace Conditional with Polymorphism, Named Constant — that you will use constantly. The non-negotiable discipline around all of them: **refactor under test coverage, in tiny steps.** Refactoring by definition preserves behavior, and the only way you *know* behavior is preserved is a green test suite (see the Testing chapter). Make one small move, run the tests, commit; make the next. The catastrophic refactor is the one done in a single giant, untested edit — that is not refactoring, that is rewriting with extra confidence and no safety net.

You also do not have to sniff out every smell by hand: **Roslyn analyzers** flag many issues at build time, **SonarQube**/SonarLint track duplication, complexity, and a large smell ruleset across the codebase, and cyclomatic-complexity metrics put a number on "this method is too tangled." Wire them into the pipeline as described in the Tooling chapter so smells surface in pull requests rather than in production incidents.

A last pragmatic note: it is entirely possible to over-refactor — to shatter a perfectly readable 30-line method into eight one-line methods that force the reader to jump around the file to reconstruct a single thought, or to extract abstractions so eagerly that you commit Speculative Generality in the name of curing other smells. The goal is never "zero smells." The goals are **readability and changeability**: code a teammate can understand quickly and modify safely. If a refactoring serves those two ends, do it; if it only satisfies a checklist, leave it alone.

> **Further reading:** *Clean Code* (Robert C. Martin), *Refactoring* (Martin Fowler), *The Pragmatic Programmer*.

## Closing Thought

Notice how many of these patterns dissolved into ordinary C# — Iterator became `yield`, Prototype became `with`, Observer became `event`, Strategy became `Func<>`, Singleton became a DI lifetime. That is not a coincidence. As a language and its ecosystem mature, yesterday's patterns become today's built-in features. The patterns worth carrying in your head are the ones the language *hasn't* absorbed and the *principles* underneath all of them.

So hold the patterns lightly and the principles tightly. When you feel real pain — a growing `switch`, a class with three jobs, a test you can't write because a dependency is hard-wired — let a pattern relieve exactly that pain and no more. Resist the urge to build cathedrals of indirection for problems you don't yet have. The mark of a senior developer is not how many patterns they can deploy, but how much needless complexity they can keep out of the codebase.
