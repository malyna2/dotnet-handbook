# Chapter 5: Design Patterns, Principles & Clean Code

_⏱️ Estimated read time: ~77 min · 10031 words (study pace)_

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

## Clean Code: Writing Code Humans Can Read

You will spend far more of your career reading code than writing it — reading to fix a bug, reading to add a feature, reading to remember what you yourself did three months ago. Studies and everyday experience agree that the ratio of reading to writing is lopsided, easily ten to one. That single observation reorganizes your priorities. If code is read ten times for every time it is written, then the reader — not the compiler, not your ego — is the customer you are writing for.

> **Clarity beats cleverness.** The compiler does not reward you for a dense one-liner, and the next developer will silently curse you for it. Optimize for the person who has to understand this code under pressure at 2 a.m.

Clean code is not a style preference or a matter of taste you can wave away. It is an economic decision. Messy code slows every future change, multiplies the chance of introducing bugs, and quietly taxes every estimate your team gives. The patterns and principles covered earlier in this chapter — SOLID, DRY, KISS, YAGNI, separation of concerns — are the *structural* side of good code. Clean code is the *local* side: what a single method, name, or file looks like up close. Both matter, and the local side is where most developers actually spend their day.

### Naming: the cheapest documentation you will ever write

A good name does the work of a comment for free and never goes stale. A bad name actively lies to you. Naming is genuinely hard, but the payoff is enormous because names are the interface through which you read everything else.

Aim for **intention-revealing names**. The name should answer why the thing exists, what it does, and how it is used — without forcing the reader to hunt for the answer elsewhere.

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

- **Avoid abbreviations and encodings.** `custMgr`, `strName`, `bIsActive` save a few keystrokes and cost every reader a translation step. Modern IDEs autocomplete; there is no excuse for `usr` over `user`. The old Hungarian-notation habit of baking the type into the name (`strName`, `iCount`) is obsolete in a statically typed language — the type is right there.
- **Use searchable names.** A bare `7` scattered through a file is impossible to grep for and easy to confuse with other sevens. `MaxRetryAttempts = 7` can be found, understood, and changed in one place.
- **Avoid disinformation.** Do not call something `accountList` if it is actually a `Dictionary`. Do not name a variable `hp` if it does not mean what "hp" means to the reader.
- **Classes are nouns, methods are verbs.** `Customer`, `InvoiceGenerator`, `PaymentProcessor` for classes; `CalculateTotal()`, `SendEmail()`, `IsValid()` for methods. A method named `Customer()` or a class named `Process` breaks the reader's model.
- **Keep a consistent vocabulary.** Pick one word per concept and stick with it. If you `Fetch` in one place, `Retrieve` in another, and `Get` in a third, the reader wonders whether the difference is meaningful. It usually is not — so pick one.
- **Avoid mental mapping.** Do not make the reader remember that in this loop `i` is really the customer index and `j` is really the order index. Name them `customerIndex` and `orderIndex`. The only place a single-letter name earns its keep is a tiny, conventional scope like a short LINQ lambda.

### Functions: small, focused, honest

The single most reliable structural rule for readable code is that **functions should be small and do one thing**. A function that fits on your screen without scrolling, whose statements all operate at the same conceptual level, is a function you can understand in one pass.

"One thing" is easier to feel than to define, but a useful test is the **single level of abstraction**: within a function, do not mix high-level policy with low-level mechanics. If one line calls `CalculatePricing(order)` and the next line is fiddling with string indices, those belong at different levels and probably in different methods.

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

- **Few parameters.** Zero, one, or two are easy. Three is a warning sign. **More than three is a smell**: introduce a *parameter object* that groups related arguments into a named type. It reads better and resists the classic bug of passing arguments in the wrong order.
- **Avoid flag/boolean parameters.** A call like `GenerateReport(true)` is unreadable at the call site — true *what*? A boolean parameter almost always means the function does two things; split it into `GenerateDetailedReport()` and `GenerateSummaryReport()`.
- **Avoid output parameters.** In C#, `out` and `ref` parameters that mutate the caller's variables surprise readers. Prefer returning a value or a small record. (The idiomatic `TryParse` pattern is the sanctioned exception.)
- **Command-Query Separation.** A method should either *do* something (a command that changes state, returns void) or *answer* something (a query that returns a value and changes nothing) — not both. `if (SetAttribute("x"))` is confusing because the reader cannot tell whether it is asking a question or performing an action.
- **No hidden side effects.** A method named `IsValid` that quietly initializes a session, or `GetUser` that also updates a last-seen timestamp, betrays its name. Side effects that contradict the name are a rich source of bugs.
- **Prefer exceptions to error codes.** Returning `-1` or `false` to signal failure forces the caller to check and pollutes the happy path with error handling. Throwing lets the happy path stay clean and makes ignoring the error a deliberate act.

### Comments: explain the WHY, not the WHAT

The best comment is the one you did not need to write because the code said it for you. A comment is a small failure — an admission that the code could not express intent on its own. Sometimes that failure is unavoidable and the comment is exactly right. Often it is a missed opportunity to rename a variable or extract a well-named method.

The distinction that matters is **why versus what**. Code already states *what* it does; a comment restating that is noise that will drift out of date the moment someone edits the code without touching the comment.

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

### Formatting and structure

Formatting is not about beauty; it is about reducing the reader's cognitive load. The single most important rule is **consistency** — and you should not be enforcing it by hand. Push it down into an `.editorconfig` and Roslyn analyzers so the whole team formats identically and the CI build fails on drift (see the Tooling chapter for setting this up). Arguing about brace placement in code review is a waste of expensive human attention that a tool settles for free.

Beyond consistency, aim for **vertical density and locality**: keep related things close together. Declare a variable near where it is first used, not at the top of a 200-line method. Keep a private helper method just below the public method that calls it, so the reader can follow the call chain by scrolling down. Blank lines are punctuation — use them to separate distinct thoughts within a method, and do not scatter them randomly.

### Error handling is part of clean code

How you handle failure shapes how readable the *success* path is. As noted above, prefer **exceptions over return codes**. Beyond that:

- **Never swallow exceptions.** An empty `catch { }` hides the very information you will desperately want later. If you truly must ignore something, log it and leave a comment explaining why it is safe.
- **Fail fast.** Validate inputs at the boundary and throw immediately (`ArgumentNullException`, `ArgumentException`) rather than letting a bad value travel deep into the system where the eventual failure is unrelatable to its cause.
- **Use guard clauses and early returns** to flatten nested conditionals. As covered earlier in this chapter, deep nesting is hard to follow; inverting conditions and returning early keeps the main logic at the leftmost indentation.

```csharp
// BEFORE — the arrow of doom; the real work is buried four levels deep
public decimal Discount(Customer customer)
{
    if (customer != null)
    {
        if (customer.IsActive)
        {
            if (customer.Orders.Any())
            {
                return customer.IsPremium ? 0.2m : 0.1m;
            }
        }
    }
    return 0m;
}
```

```csharp
// AFTER — guard clauses handle the exceptions first, logic stays flat
public decimal Discount(Customer customer)
{
    ArgumentNullException.ThrowIfNull(customer);

    if (!customer.IsActive) return 0m;
    if (!customer.Orders.Any()) return 0m;

    return customer.IsPremium ? 0.2m : 0.1m;
}
```

Finally, **do not return `null`** as a routine result. Null is the invitation to a `NullReferenceException` and forces every caller to remember a defensive check. Prefer an empty collection for "no results", a `Result<T>` or `Option<T>` type for operations that can fail meaningfully (the Result pattern covered earlier), or nullable reference types with the compiler's null-flow analysis turned on so the risk is at least visible. Returning an empty `IEnumerable<T>` instead of null means the caller can just `foreach` without ceremony.

### The Boy-Scout Rule and the cost of cleverness

Two closing habits separate developers who keep a codebase healthy from those who let it rot.

The first is the **Boy-Scout Rule**: leave the code a little cleaner than you found it. You do not need a grand refactoring project. Rename one confusing variable, extract one overgrown method, delete one block of commented-out code each time you pass through. Small, continuous improvement compounds and quietly reverses the entropy that otherwise creeps into every long-lived project.

The second is a healthy suspicion of **clever code**. The bit-twiddling trick, the deeply nested ternary, the LINQ query that spans fifteen lines and three levels of `SelectMany` — these feel satisfying to write and are miserable to read. Cleverness that saves a line but costs the reader a minute is a bad trade. Write the boring, obvious version. The senior move is usually not the cleverest solution but the one your teammates understand instantly.

## Code Smells & Their Refactorings

A **code smell** is a surface-level symptom in the code that hints at a deeper design problem. The term, popularized by Martin Fowler and Kent Beck, is deliberately soft. A smell is not a bug — the code may work perfectly — and it is not automatically wrong. It is a *heuristic*: something that, in your experience, is worth a second look. A long method is not illegal; it is just a place where problems tend to hide.

> **Smells guide, they do not dictate.** A smell is a prompt to *consider* a refactoring, not a rule that forces one. Sometimes the smelly version is genuinely the clearest option, and forcing a "clean" structure onto it makes things worse.

The value of learning the catalogue is that it gives you a shared vocabulary and a fast pattern-matcher. When you can name what bothers you about a piece of code ("this is Feature Envy"), you also know the standard set of refactorings that address it.

### A catalogue of common smells

| Smell | What it is | Common refactoring(s) |
|---|---|---|
| **Long Method** | A method that does too much or is simply too long to grasp at a glance. | Extract Method; Replace Temp with Query; Decompose Conditional. |
| **Large Class / God Object** | A class with too many fields and responsibilities that knows and does everything. | Extract Class; Extract Interface; move behavior to collaborators. |
| **Long Parameter List** | More than ~3 parameters, or several that always travel together. | Introduce Parameter Object; Preserve Whole Object. |
| **Duplicated Code** | The same structure repeated in multiple places (violates DRY). | Extract Method/Class; Pull Up Method; Form Template Method. |
| **Feature Envy** | A method that is more interested in another class's data than its own. | Move Method; Extract Method then Move. |
| **Primitive Obsession** | Using primitives (`string`, `decimal`, `int`) for domain concepts. | Replace Primitive with Value Object; Encapsulate. |
| **Data Clumps** | The same group of fields/parameters appearing together repeatedly. | Extract Class; Introduce Parameter Object. |
| **Shotgun Surgery** | One change forces many small edits across many classes. | Move Method/Field to consolidate the responsibility. |
| **Divergent Change** | One class changes for many different reasons (violates SRP). | Extract Class to split along the axes of change. |
| **Switch Statements / Type Code** | A `switch` on a type field, often duplicated across the codebase. | Replace Conditional with Polymorphism; Strategy. |
| **Message Chains / Train Wreck** | `a.B().C().D().E()` — reaching through many objects (Law of Demeter). | Hide Delegate; Extract Method; add a purposeful method. |
| **Temporal Coupling** | Methods that must be called in a specific hidden order to work. | Redesign API so misuse is impossible; combine steps. |
| **Speculative Generality** | Abstraction built for a future that never arrives (violates YAGNI). | Collapse Hierarchy; Inline Class; delete the unused hooks. |
| **Comments (as deodorant)** | Comments used to explain bad code instead of fixing it. | Extract Method with a good name; Rename. |
| **Magic Numbers / Strings** | Unexplained literals scattered through the code. | Replace Magic Number with Named Constant; enum. |

Several of these are the local, code-level face of principles covered earlier in this chapter: Divergent Change is a Single Responsibility violation, Duplicated Code is a DRY violation, Message Chains break the Law of Demeter, and Speculative Generality is YAGNI ignored. The smell vocabulary and the principle vocabulary describe the same underlying forces from different distances.

### Refactoring 1: Primitive Obsession → Value Object

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

### Refactoring 2: Long Method → Extract Method + Replace Temp with Query

Explaining variables are fine, but a method dense with temporary variables and inline computation is hard to skim. Extracting well-named queries turns computation into vocabulary.

```csharp
// BEFORE — a wall of temps and inline logic
public string Summarize(Invoice invoice)
{
    decimal subtotal = 0;
    foreach (var line in invoice.Lines)
        subtotal += line.Price * line.Quantity;

    decimal tax = subtotal * 0.2m;
    decimal total = subtotal + tax;

    string status = total > 1000 ? "LARGE" : "STANDARD";
    return $"{invoice.Number}: {total:C} ({status})";
}
```

```csharp
// AFTER — each concept is a named query; the summary reads like a sentence
public string Summarize(Invoice invoice)
    => $"{invoice.Number}: {Total(invoice):C} ({Size(invoice)})";

private static decimal Subtotal(Invoice invoice)
    => invoice.Lines.Sum(l => l.Price * l.Quantity);

private static decimal Total(Invoice invoice)
    => Subtotal(invoice) * (1 + TaxRate);

private static string Size(Invoice invoice)
    => Total(invoice) > LargeInvoiceThreshold ? "LARGE" : "STANDARD";
```

Replacing temporaries with query methods costs a little recomputation but buys readability and reusability — and if a hot path makes the recomputation matter, that is a measured optimization decision, not a default.

### Refactoring 3: Switch on Type → Polymorphism / Strategy

A `switch` on a type code is a smell because the *same* switch tends to appear in several places, and adding a new case means hunting them all down (Shotgun Surgery, waiting to happen).

```csharp
// BEFORE — a type code and a switch that will be duplicated elsewhere
public decimal Pay(Employee e) => e.Type switch
{
    EmployeeType.Salaried => e.MonthlySalary,
    EmployeeType.Hourly   => e.HourlyRate * e.HoursWorked,
    EmployeeType.Commission => e.BaseSalary + e.Sales * e.CommissionRate,
    _ => throw new ArgumentOutOfRangeException()
};
```

```csharp
// AFTER — each type owns its own behavior; adding a type touches one new class
public abstract class Employee
{
    public abstract decimal CalculatePay();
}

public class SalariedEmployee : Employee
{
    public decimal MonthlySalary { get; init; }
    public override decimal CalculatePay() => MonthlySalary;
}

public class HourlyEmployee : Employee
{
    public decimal HourlyRate { get; init; }
    public decimal HoursWorked { get; init; }
    public override decimal CalculatePay() => HourlyRate * HoursWorked;
}
```

Now adding a `CommissionEmployee` is a new class in isolation, and the compiler helps ensure it implements the contract — no existing switch to find and edit. (When the behavior varies but the type hierarchy should not, the Strategy pattern covered earlier in this chapter is the same idea expressed through composition.)

### The core refactoring moves — and how to apply them

Most refactoring reduces to a small vocabulary of named moves you will use constantly:

- **Extract Method / Extract Class** — pull a fragment into its own well-named unit.
- **Introduce Parameter Object** — bundle arguments that travel together.
- **Replace Conditional with Polymorphism** — turn a type switch into a hierarchy or strategy.
- **Replace Magic Number with Named Constant** — give literals a name and a home.
- **Encapsulate / Introduce Value Object** — wrap a primitive with its rules.
- **Move Method / Move Field** — relocate behavior to the class that owns the data it uses.

The non-negotiable discipline around all of them: **refactor under test coverage, in tiny steps.** Refactoring by definition preserves behavior, and the only way you *know* behavior is preserved is a green test suite (see the Testing chapter). Make one small move, run the tests, commit; make the next. The catastrophic refactor is the one done in a single giant, untested edit — that is not refactoring, that is rewriting with extra confidence and no safety net.

### Let tools do the smelling for you

You do not have to sniff out every smell by hand. **Roslyn analyzers** flag many issues at build time and can be tuned per project via `.editorconfig`. **SonarQube** (and the SonarLint IDE plugin) tracks duplication, complexity, and a large ruleset of smells across the codebase, and can gate your CI pipeline. **Cyclomatic complexity** and maintainability-index metrics — available in Visual Studio's Code Metrics and via analyzers — put a number on "this method is too tangled." Wire these into the pipeline as described in the Tooling chapter so smells surface in pull requests automatically rather than in production incidents.

### A pragmatic closing note

Smells are heuristics, not commandments. It is entirely possible to over-refactor — to shatter a perfectly readable 30-line method into eight one-line methods that force the reader to jump around the file to reconstruct a single thought, or to extract abstractions so eagerly that you commit the Speculative Generality smell in the name of cleaning up others. The goal is never "zero smells" for its own sake. The goals are **readability and changeability**: code a teammate can understand quickly and modify safely. If a refactoring serves those two ends, do it. If it only satisfies a checklist, leave it alone.

> **Further reading:** *Clean Code* (Robert C. Martin), *Refactoring* (Martin Fowler), *The Pragmatic Programmer*.

## Closing Thought

Notice how many of these patterns dissolved into ordinary C# — Iterator became `yield`, Prototype became `with`, Observer became `event`, Strategy became `Func<>`, Singleton became a DI lifetime. That is not a coincidence. As a language and its ecosystem mature, yesterday's patterns become today's built-in features. The patterns worth carrying in your head are the ones the language *hasn't* absorbed and the *principles* underneath all of them.

So hold the patterns lightly and the principles tightly. When you feel real pain — a growing `switch`, a class with three jobs, a test you can't write because a dependency is hard-wired — let a pattern relieve exactly that pain and no more. Resist the urge to build cathedrals of indirection for problems you don't yet have. The mark of a senior developer is not how many patterns they can deploy, but how much needless complexity they can keep out of the codebase.
