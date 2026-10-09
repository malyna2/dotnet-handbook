# Chapter 21: Architecture

You can write correct code and still build a system that becomes miserable to change. Correctness is about whether a single function returns the right answer; architecture is about whether, six months from now, a new feature takes an afternoon or a fortnight. Architecture is the cost of change, decided in advance: every boundary makes change cheap on one side of it and expensive across it. This chapter makes you able to put boundaries where the business changes independently, to choose between a modular monolith and microservices with the cost of each spelled out, and to defend the choice in a design review and write it down so the next team knows why.

[Chapter 10](#chapter-10-design-basics) covered the code-level foundations: SOLID, the common patterns, Repository and Unit of Work, layered and Clean architecture. Here those become system-level choices. The chapter starts with the two forces every choice trades (coupling and cohesion), then the boundaries inside a domain (DDD's aggregates and bounded contexts, the Specification pattern for query rules), then the split between the write model and the read model (CQRS and event sourcing), then the styles that draw the boundary somewhere else (vertical slices, the modular monolith, microservices), and finally the edge where clients meet the services (gateways and BFFs). Once a boundary becomes a network boundary, [Chapter 20](#chapter-20-distributed-systems) takes over; how the contracts that cross a boundary evolve is [Chapter 22](#chapter-22-api-evolution-real-time-and-serialization).

There is no "correct" architecture, only architectures that fit a particular set of forces: team size, deployment cadence, domain complexity, and how much uncertainty you're carrying. Through every section, keep asking the only question that matters: *what problem does this solve, and do I actually have that problem?*

## Why Architecture Matters: Coupling and Cohesion

Every discussion of architecture eventually reduces to two words: **coupling** and **cohesion**. Master these and everything else is commentary.

**Cohesion** measures how much the things inside a module belong together. A class called `OrderProcessor` that validates orders, charges payment, and sends emails has *low* cohesion — three unrelated responsibilities crammed together. Split it so each unit does one thing well and cohesion goes up.

**Coupling** measures how much one module depends on the internals of another. If changing the shape of your database table forces you to edit your HTTP controllers, those two are tightly coupled. Tight coupling is the silent killer of software: it turns a small change into a cascade of edits and makes reasoning about any single part impossible without holding the whole system in your head.

> **The golden rule: aim for high cohesion and low coupling.** Related things live together; unrelated things can change independently. Almost every architectural pattern in this chapter is a specific technique for achieving that one goal.

An analogy: think of a well-organized kitchen. The knives are together (cohesion), and rearranging the spice rack doesn't require moving the refrigerator (low coupling). A badly organized kitchen has forks in three drawers and requires you to empty the pantry to reach the salt. Software rots the same way — one careless dependency at a time.

Coupling isn't binary; it comes in flavors, roughly from worst to least harmful:

- **Content coupling** — one module reaches into another's private data. Avoid entirely.
- **Common coupling** — modules share global mutable state. Fragile.
- **Control coupling** — one module passes a flag that dictates another's control flow (`DoThing(isPreview: true)`).
- **Data coupling** — modules communicate only through simple parameters. This is the goal.

The cost of getting this wrong compounds. Cheap-to-change software wins in the long run not because it's elegant but because businesses change their minds, and the system that bends survives.

## Domain-Driven Design

Domain-Driven Design (DDD) is less an architecture than a philosophy: put the **domain** — the actual business problem — at the heart of your design, and let the code speak the language of the business. DDD splits into *strategic* design (the big-picture boundaries) and *tactical* design (the building blocks inside a boundary).

### Ubiquitous Language

Everything starts with the **Ubiquitous Language**: a shared, precise vocabulary used identically by domain experts and developers, in conversation *and* in code. If the business says "a Policy is *lapsed* when a premium is 30 days overdue," then there is a concept named `Lapsed` in the code, not a magic `status == 3`. The language removes the costly translation layer between what the business means and what the software does.

### Tactical Building Blocks

**Entities** have identity that persists over time. A `Customer` is the same customer even after they change their name and address. Equality is by ID, not by attributes.

```csharp
public sealed class Customer   // Entity: identity matters
{
    public CustomerId Id { get; }
    public string Name { get; private set; }
    public void Rename(string newName) { /* invariants enforced here */ }
}
```

**Value Objects** have no identity; they're defined entirely by their values and are immutable. Money, a date range, an address. Two `Money(10, "USD")` instances are interchangeable. C# `record` types are a natural fit.

```csharp
public sealed record Money(decimal Amount, string Currency)
{
    public Money Add(Money other)
    {
        if (other.Currency != Currency) throw new InvalidOperationException("Currency mismatch");
        return this with { Amount = Amount + other.Amount };
    }
}
```

> **Best practice:** reach for Value Objects aggressively. Replacing bare `decimal` and `string` with `Money` and `EmailAddress` moves validation into the type system — an invalid value literally cannot be constructed — and makes the domain self-documenting.

**Aggregates and Aggregate Roots.** An aggregate is a cluster of objects treated as a single unit for data changes. The **aggregate root** is the one entity through which all outside access must go; it enforces the invariants of the whole cluster. An `Order` (root) contains `OrderLine` objects. You never modify an `OrderLine` directly from outside — you call `order.AddLine(...)`, and the `Order` guarantees rules like "total cannot exceed the credit limit."

```csharp
public sealed class Order   // Aggregate Root
{
    private readonly List<OrderLine> _lines = new();
    public IReadOnlyList<OrderLine> Lines => _lines.AsReadOnly();
    public Money Total => _lines.Aggregate(Money.Zero, (sum, l) => sum.Add(l.Subtotal));

    public void AddLine(ProductId product, int qty, Money unitPrice)
    {
        if (qty <= 0) throw new DomainException("Quantity must be positive.");
        _lines.Add(new OrderLine(product, qty, unitPrice));   // invariant guarded by the root
    }
}
```

> **The aggregate boundary is also the transactional and consistency boundary.** One transaction should modify one aggregate. Keep aggregates small — large aggregates cause lock contention and load whole object graphs into memory. When two aggregates must coordinate, do it through *eventual consistency* and domain events, not one giant transaction; across services, that coordination becomes a saga ([Chapter 20](#chapter-20-distributed-systems)).

**Domain Events** capture something meaningful that happened in the domain: `OrderPlaced`, `PaymentFailed`. The aggregate raises them; handlers elsewhere react. They decouple the "what happened" from the "what should happen next."

```csharp
public sealed record OrderPlaced(OrderId OrderId, CustomerId CustomerId, DateTime OccurredAt);
```

**Repositories** provide a collection-like illusion over persistence for aggregate roots — one repository per aggregate, never per table. `IOrderRepository`, not `IOrderLineRepository`. The interface belongs to the domain; the implementation to infrastructure ([Chapter 10](#chapter-10-design-basics)'s Clean Architecture).

### Strategic Design: Bounded Contexts

Here's the insight that separates senior DDD from junior DDD. The word "Customer" means different things in different parts of a business. To Sales, a Customer is a lead with a pipeline stage. To Shipping, a Customer is an address and a delivery preference. To Billing, a Customer is a payment method and a credit limit. Forcing all of these into one universal `Customer` class creates a bloated, contradictory model that nobody can change safely.

A **Bounded Context** is an explicit boundary within which a particular model and its Ubiquitous Language apply consistently. Inside the Sales context, "Customer" means the sales meaning — full stop. Different contexts can have their own `Customer`, and they connect through well-defined contracts (a *Context Map* describes these relationships — shared kernel, customer/supplier, anti-corruption layer, etc.).

```
   Sales Context           Shipping Context          Billing Context
+----------------+       +------------------+      +------------------+
|  Customer      |       |  Customer        |      |  Customer        |
|  (lead, stage) | <---> |  (address, pref) |<---> |  (credit, cards) |
|  Opportunity   |       |  Shipment        |      |  Invoice         |
+----------------+       +------------------+      +------------------+
      each context owns its own model & language
```

> **Bounded Contexts are the most valuable idea in DDD and, not coincidentally, they are the natural seams along which you later split microservices.** Get the boundaries right and everything downstream is easier. An **anti-corruption layer** — a translation shim at a context boundary — prevents another team's model from leaking into and polluting yours.

## The Specification Pattern

Repositories ([Chapter 10](#chapter-10-design-basics) introduces Repository and Unit of Work) raise a question the moment a domain has rules about *which* objects qualify: where does that rule live? The Specification pattern encapsulates a query or a business rule as a reusable, composable object. It solves the problem of query logic (`IsActive && SignedUpBefore(x)`) being duplicated and scattered.

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

## CQRS and Event Sourcing

### CQRS

**Command Query Responsibility Segregation** rests on a simple observation: the way you *change* data and the way you *read* data have different needs. Writes care about invariants, validation, and consistency. Reads care about speed and shape — they often want denormalized, screen-ready data. CQRS says: use separate models for the two.

At its lightest, CQRS is just a code convention: commands (return void/an ID, cause side effects) and queries (return data, cause no side effects) go through different handlers. Libraries like MediatR make this ergonomic.

```csharp
public sealed record PlaceOrderCommand(CustomerId CustomerId, IReadOnlyList<CartLine> Lines)
    : IRequest<OrderId>;

public sealed record GetOrderSummaryQuery(OrderId Id) : IRequest<OrderSummaryDto>;
```

MediatR is an in-process dispatcher with a pipeline ([Chapter 10](#chapter-10-design-basics) teaches it as the Mediator pattern): it decouples a controller from its handler and gives cross-cutting behaviour (validation, logging, a transaction) one place to live. It does *not* give you CQRS by itself. Commands and queries routed through MediatR may still share one entity model and one `DbContext`; there is no read store, no scaling and no consistency guarantee until you build them.

> **Licensing note.** MediatR announced a move to commercial licensing for new major versions in April 2025 (existing versions remain open source); the Mediator section of [Chapter 10: Design Basics](#chapter-10-design-basics) has the full note and the alternatives. Check it before making MediatR a default dependency.

Note what the lightweight version does *not* require: separate databases, event sourcing or eventual consistency. Those are advanced variants for systems whose reads and writes have genuinely diverged.

At its heaviest, CQRS uses *physically separate stores*: writes go to a normalized transactional database through rich domain aggregates; a projection process updates a denormalized read store (say, a document DB or a set of flat read tables) optimized for queries. The read side is eventually consistent with the write side, and is often fed by the same outbox or CDC stream as everything else ([Chapter 18](#nosql-and-polyglot-persistence) places it among the other stores).

```
  Command --> Write Model (aggregates) --> Write DB
                     |
                 publishes events
                     v
             Projection Handler --> Read DB --> Query --> DTO
```

> **Trade-off:** Full CQRS with separate stores gives you independently scalable reads, tailor-made query models, and no more contorting one ORM model to serve both a complex edit form and a dashboard. The price is significant: two data models to keep in sync, eventual consistency the UI must account for, and real operational complexity. **Start with the lightweight command/query split. Reach for separate read stores only when read and write scaling or modeling needs genuinely diverge.** Do not adopt heavyweight CQRS by default — it is one of the most over-applied patterns in the field.

### Event Sourcing

Event Sourcing is a distinct idea often paired with CQRS. Instead of storing the *current state* of an entity and overwriting it on each change, you store the *full sequence of events* that led to that state. The current state is a left-fold (a replay) over the events.

Think of a bank account. A traditional system stores `Balance = 120`. An event-sourced system stores the history:

```
Event Stream for Account #A-1001
--------------------------------------------------
1. AccountOpened        { owner: "Ivy",  at: 09:00 }
2. MoneyDeposited       { amount: 100,   at: 09:05 }
3. MoneyDeposited       { amount: 50,    at: 10:12 }
4. MoneyWithdrawn       { amount: 30,    at: 11:40 }
--------------------------------------------------
Current balance = 0 + 100 + 50 - 30 = 120   (replayed)
```

```csharp
public Account Rehydrate(IEnumerable<object> events)
{
    var account = new Account();
    foreach (var e in events) account.Apply(e);   // fold events into state
    return account;
}
```

The events are the source of truth; current state is derived. This gives you a perfect audit log, time-travel ("what was the balance last Tuesday?"), and the ability to build new read projections retroactively by replaying history.

> **Event Sourcing is powerful and rarely needed.** The costs are steep: schema evolution of old events, snapshotting for performance, the mental shift for the whole team, and the fact that you can never "just fix a row." Use it where the audit trail *is* the product — finance, compliance, inventory ledgers — not because it sounds sophisticated. And note: **CQRS does not require Event Sourcing, and Event Sourcing does not require CQRS**, though they combine naturally.

## Vertical Slice Architecture

Layered and Clean architectures ([Chapter 10](#chapter-10-design-basics)) organize code *horizontally* by technical concern — all controllers here, all services there, all repositories over there. Adding one feature means touching a file in every layer, and unrelated features share the same fat service classes (low cohesion, remember?).

**Vertical Slice Architecture** flips the axis. Organize by *feature*. Each slice contains everything it needs — endpoint, request/response, handler, validation, data access — grouped together, often in a single folder or even file.

```
Features/
  Orders/
    PlaceOrder/
      PlaceOrderCommand.cs
      PlaceOrderHandler.cs
      PlaceOrderValidator.cs
      PlaceOrderEndpoint.cs
    GetOrderSummary/
      GetOrderSummaryQuery.cs
      GetOrderSummaryHandler.cs
```

The philosophy: **maximize cohesion within a feature and minimize coupling between features.** Each slice can make its own choices — a trivial query can hit the database directly; a complex command can use full domain aggregates. You stop forcing every feature through the same abstractions.

> **Trade-off:** Vertical slices make features easy to add, delete, and reason about in isolation — the change footprint of a feature is one folder. The risk is duplication and inconsistency across slices, and less enforced structure to lean on. It pairs beautifully with CQRS/MediatR. Many modern .NET teams blend it with Clean Architecture: slices for the application layer, a shared domain core underneath.

## Monolith vs Microservices vs Modular Monolith

### The Spectrum

A **monolith** is a single deployable unit. All modules run in one process, share one database, and ship together. A **microservices** architecture decomposes the system into small, independently deployable services, each owning its own data, communicating over the network. A **modular monolith** sits between: a single deployable unit, but internally partitioned into strict modules with enforced boundaries and, ideally, separate schemas per module.

```
 Monolith            Modular Monolith           Microservices
+----------+       +---------------------+     +------+ +------+ +------+
|          |       | [Mod A][Mod B][Mod C]|     | Svc A| | Svc B| | Svc C|
|  one big |       |  strict boundaries   |     |  +DB | |  +DB | |  +DB |
|  blob    |       |  one deployable      |     +------+ +------+ +------+
+----------+       +---------------------+       network calls between
  one DB               one DB (schemas)
```

### The Trade-offs

Microservices are frequently sold as *the* modern architecture. Adopt them for the wrong reasons and you'll trade in-process method calls (fast, transactional, easy to debug) for network calls (slow, unreliable, eventually consistent, hard to trace). You inherit distributed systems problems ([Chapter 20](#chapter-20-distributed-systems)): partial failure, network partitions, distributed transactions, versioning of contracts ([Chapter 22](#chapter-22-api-evolution-real-time-and-serialization)), and an operations burden that demands real DevOps maturity.

What microservices genuinely buy you:

- **Independent deployment** — teams ship without coordinating a giant release.
- **Independent scaling** — scale only the hot service.
- **Technology heterogeneity** — the right tool per service.
- **Fault isolation** — one service degrading needn't take down the whole system (if designed for it).
- **Team autonomy** — small teams own services end to end.

Notice most of these benefits are *organizational and scaling* benefits, not code-quality benefits. That points to the deciding factor.

### When to Split, and Conway's Law

> **Conway's Law:** "Organizations design systems that mirror their own communication structure." Your architecture will come to resemble your org chart whether you plan it or not. Microservices work when you have multiple autonomous teams that need to deploy independently. If you're one team of six, microservices mostly give you a distributed monolith — all the coupling, none of the independence, plus network latency.

> **Best practice — start with a Modular Monolith.** You get clean boundaries (Bounded Contexts as modules), a single simple deployment, in-process calls, and real transactions. If a module later proves it needs independent scaling or a dedicated team, its clean boundary makes extraction to a microservice tractable. Do not begin a greenfield project with microservices unless you already know the boundaries cold and have the team structure and operational muscle to match. **The modular monolith is the pragmatic senior default.**

The corollary: bad boundaries in a monolith are cheap to fix (move a class). Bad boundaries between microservices are agony to fix (coordinated multi-service migration). Get the boundaries right *before* you distribute.

## API Gateway and Backend for Frontend

Once you have multiple services, clients shouldn't call each one directly — that leaks internal topology and burdens the client with orchestration, auth, and retries. An **API Gateway** is a single entry point that routes requests to backend services and handles cross-cutting concerns: authentication, rate limiting, TLS termination, request aggregation, caching. In .NET, YARP (Yet Another Reverse Proxy) is the common building block; [Chapter 26](#chapter-26-delivery-and-platform) places gateways among the other edge infrastructure (load balancers, reverse proxies, CDNs). The architectural question is how many entry points you need and who owns them.

```
                 +------------------+       +-- Orders Service
   Clients  -->  |   API Gateway    | --->  +-- Catalog Service
                 | auth, routing,   |       +-- Pricing Service
                 | rate-limit, agg  |       +-- ...
                 +------------------+
```

A **Backend for Frontend (BFF)** takes this further: instead of one general-purpose gateway, you build a *dedicated* backend per client type. The mobile app has different needs than the desktop web app — fewer fields, different aggregation, different caching. A single one-size-fits-all API forces awkward compromises. So you give each frontend its own tailored BFF that composes exactly the data that frontend needs.

```
  Mobile App  --> Mobile BFF  -->
  Web SPA     --> Web BFF     -->  [ downstream services ]
  Partner API --> Partner BFF -->
```

> **Trade-off:** BFFs eliminate over- and under-fetching and let frontend teams move fast without waiting on a shared API. The cost is more services to maintain and potential logic duplication across BFFs. The BFF is also a natural home for the OAuth token-handling pattern in modern SPAs (keeping tokens server-side). Use BFFs when your clients genuinely diverge; a single gateway suffices when they don't.

## Bringing It Together

If you take one thing from this chapter, let it be this: **architecture is the art of deferring and containing change.** Every pattern here is a way to draw a boundary so that a change on one side doesn't force a change on the other. Coupling and cohesion are the physics; Clean Architecture, DDD bounded contexts, CQRS, vertical slices and modular monoliths are the engineering; and the distributed patterns of [Chapter 20](#chapter-20-distributed-systems) (sagas, the outbox, idempotency, eventual consistency) are what you need once a boundary becomes a network boundary.

The senior move is restraint. Reach for the simplest structure that fits the forces in play, and add ceremony only when a real force demands it. A modular monolith with clean domain boundaries will serve the vast majority of systems far longer than most engineers expect — and it leaves every more-complex option open when, and only when, you actually need it.

## Practice

**1. An ADR for one decision (1 h).** Take a decision your team made, or the one in *Decide* below, and write it in the shape of [Chapter 16's Architecture Decision Records](#architecture-decision-records-adrs): context, the options with their costs, the decision, the consequences, and what would make you revisit it. Keep it to one page; an ADR is read by someone in a hurry.

**2. A system design in 90 minutes (1 h 30 min).** Pick a prompt you haven't seen designed, for example "deliver webhooks to partners, with retries, at a few hundred thousand events a day". Follow [Chapter 23](#chapter-23-system-design)'s approach and write each step down: requirements (functional and not), estimates (requests per second, storage per year), the API, the data model, the components, the first two bottlenecks, and for each major choice the option you rejected and why. Then mark every place where [Chapter 20](#chapter-20-distributed-systems)'s guarantees (outbox, idempotency, ordering) apply.

**3. The capstone, as a longer project.** [Chapter 44](#chapter-44-capstone-one-project-growing-up)'s Steps 6 and 7 ([Refactor Toward Clean Architecture and DDD](#step-6-refactor-toward-clean-architecture-and-ddd) and [Split Into Microservices](#step-7-split-into-microservices)) are this chapter's material built for real; write an ADR for each split you make.

**Evidence to keep**, in your own public portfolio repo, not in this one: the ADRs and the design write-up. Decisions about a real employer's system stay private; publish a version about the capstone or an invented system.

Later, if you need it: [Chapter 26: The 12-Factor App](#the-12-factor-app) and [.NET Aspire](#net-aspire), and [Chapter 22: Versioning Strategies for REST APIs](#versioning-strategies-for-rest-apis).

## Three questions

**1.** A team split its monolith into five services, and now every release still needs all five deployed together, and an outage in one takes the others down. What went wrong, and how do you tell a real service boundary from a fake one?

<details>
<summary>Answer</summary>

- **The coupling moved instead of disappearing.** Usually the services share a database (a schema change breaks all of them), or call each other synchronously in chains (one slow service stalls the rest), or share a library of domain types that must be upgraded in lockstep. That is a distributed monolith.
- **A real boundary has three properties.** The service owns its data, and nobody else reads its tables. It can serve its requests from its own state, getting other services' data through events it has already received rather than through calls on the request path. And it changes on its own schedule, which usually means one team owns it.
- **The way back.** Merge services that always change together into one, or into a module of a modular monolith; move shared tables behind one owner; replace synchronous chains with events and local copies of the data (the outbox and idempotent consumers of [Chapter 11](#chapter-11-messaging-and-background-work) and [Chapter 20](#chapter-20-distributed-systems)).
</details>

**2.** A team says it does CQRS because every request goes through MediatR. What does MediatR give them, what doesn't it, and what would real CQRS change?

<details>
<summary>Answer</summary>

- **What MediatR gives.** An in-process dispatch from a request object to its single handler, so controllers don't depend on services directly, and pipeline behaviours that wrap every request with validation, logging or a transaction.
- **What it doesn't.** Separate models: the commands and queries may still share one entity model and one `DbContext`. It adds no read store, no scaling, no consistency guarantee, and a level of indirection on every call. New major versions are commercially licensed.
- **What CQRS changes.** Queries read a model shaped for the screen (a projection, a view, or a separate store), and commands go through the domain model that enforces invariants. With a separate store, reads become eventually consistent, and the UI and the business rules must tolerate that. It pays where reads and writes have very different shapes or loads; for CRUD, it is ceremony.
</details>

**3.** A public API returns `customerName`. The business now wants `givenName` and `familyName`. How do you ship it without a v2, which steps break whom, and how do you know when the old field can go?

<details>
<summary>Answer</summary>

- **Expand.** Add `givenName` and `familyName` to responses next to `customerName`, and accept either form on requests. Adding response fields is safe for tolerant readers; check that your known clients are.
- **Migrate.** Mark `customerName` deprecated in the OpenAPI document and the changelog, tell the consumers, and log which consumers still read or send it, on logs or traces rather than metric tags if there are many consumers.
- **Contract.** Remove `customerName` only when that telemetry has shown no use for an agreed period, after the announced date.
- **When a version is right instead.** If the meaning changes (a name is no longer one string anywhere in the domain), or the old field can't be derived from the new ones, a new version with a published retirement date for the old one is more honest than a field that lies.
</details>

## Decide

A team of twelve developers works on one ASP.NET Core monolith with one database. Deploys take an afternoon, merge conflicts are frequent, and a bug in invoicing blocked a release of the catalogue last month. Three proposals:

1. Split it into six microservices along the current folders.
2. Turn it into a modular monolith: modules with their own schemas and narrow APIs, boundaries checked by architecture tests, still one deployable.
3. Keep the structure and fix the delivery pipeline: faster tests, trunk-based development, feature flags.

<details>
<summary>How a senior engineer weighs it</summary>

**What each costs.**

- *Six services:* months of work and a permanent operations bill (pipelines, monitoring, on-call, contracts and versioning between services, [Chapter 20](#chapter-20-distributed-systems)'s consistency machinery). The folders are not known to be business boundaries, so the split risks a distributed monolith. It is also the only option that lets parts deploy and scale independently.
- *Modular monolith:* weeks to draw and enforce boundaries, with a lot of untangling where modules read each other's tables. It addresses merge conflicts and the blast radius of a bug, keeps one deployment and transactions across modules, and makes a later split cheap, because a module with its own schema and API is most of a service.
- *Fix the pipeline:* days to weeks. It addresses the slow deploys and blocked releases directly (a feature flag would have shipped the catalogue with invoicing switched off), and changes nothing about coupling.

**What decides it here:** the pains are delivery and coupling, not scale. Nothing says one part needs to scale or deploy independently of the others.

**The choice.** Fix the pipeline first, because it pays within weeks, and move to a modular monolith in parallel, one module at a time, starting with the most independent one. Record both decisions as ADRs.

**What would change it.** A part with a genuinely different scaling or availability profile, a separate team that must ship on its own schedule, or a compliance boundary would justify extracting that one module into a service — and the modular boundary is what makes that extraction cheap.
</details>

## Check at work

**Inspect.** Does your domain project reference EF Core, ASP.NET Core or an HTTP client? Which modules or services read another's tables? Which decisions of the last year exist only in someone's memory? Pick one and write its ADR. For your API: is there a written list of what counts as a breaking change, and does anything in the pipeline (an OpenAPI diff, contract tests) catch one before release?

**Measure.** For each API version or deprecated field you serve: how many consumers called it in the last 30 days. For a request path that crosses services: how many synchronous calls it makes, and the end-to-end latency against the sum of its parts.

## Interview Questions

**Explain SOLID with a one-liner each.**
- **S**RP — a class has one reason to change (split the class that both formats *and* saves a report).
- **O**CP — open to extension, closed to modification (add a new payment type via a new class, not by editing a `switch`).
- **L**SP — subtypes must be substitutable for their base without breaking callers (the `Square : Rectangle` trap).
- **I**SP — many small interfaces beat one fat one (don't force implementers to stub methods they don't use).
- **D**IP — depend on abstractions, not concretions (inject `IEmailSender`, not `SmtpClient`).

**DI vs IoC — are they the same?**
IoC (Inversion of Control) is the broad principle: the framework controls flow and creation, not your code. Dependency Injection is one specific way to apply it — supplying a class's dependencies from outside rather than having it `new` them. DI enables testability and swapping implementations.

**Is the repository pattern still worth it over EF Core?**
Contested. The argument *against*: `DbContext` is already a Unit of Work and `DbSet` is already a repository, so wrapping it adds a leaky abstraction. The argument *for*: a repository can centralize query logic, keep the domain persistence-ignorant, and simplify testing. Senior answer: don't add a generic repository reflexively; add task-specific repositories when they earn their keep, otherwise use EF directly.

**Red flag:** "Always wrap EF in a generic repository — it's best practice" — `DbContext` already is a unit of work and repository; the reflexive wrapper is a leaky layer.

**What is CQRS and when do you use it?**
Command Query Responsibility Segregation splits the write model (commands that change state) from the read model (queries), often with different shapes and even different stores. Use it when read and write workloads diverge sharply or you want optimized read projections. It adds complexity — don't apply it to simple CRUD.

**What is an aggregate in DDD?**
A cluster of domain objects treated as one consistency boundary, with a single **aggregate root** as the only entry point. Invariants hold within the aggregate, and you load/save it as a unit. Rule of thumb: keep aggregates small, reference other aggregates by ID, and enforce cross-aggregate consistency asynchronously.

**Microservices vs monolith — the trade-off?**
Monolith: simplest to build, deploy, and debug; one codebase, in-process calls, easy transactions — but scales and evolves as one unit. Microservices: independent deploy/scale/tech per service and team autonomy — but you pay with network latency, distributed transactions, operational complexity, and harder debugging. Most teams should start with a well-structured monolith.

**Red flag:** "Microservices are the modern way; monoliths are legacy" — splitting without a clear need yields a distributed monolith.

**Coupling and cohesion — define and relate.**
Cohesion is how focused a module is on a single responsibility (high is good). Coupling is how dependent modules are on each other (low is good). Aim for high cohesion, low coupling: modules that each do one thing well and interact through narrow, stable interfaces.

**When would you NOT use microservices?**
Small teams, early-stage products, unclear domain boundaries, or when the operational maturity (CI/CD, observability, on-call) isn't there. Premature microservices give you a distributed monolith: all the network pain, none of the independence. Split only when a clear boundary and a scaling or team-autonomy need justify it.

## Further Reading

- *Clean Architecture* and *Clean Code* — Robert C. Martin
- *Domain-Driven Design* — Eric Evans (the "Blue Book")
- *Implementing Domain-Driven Design* — Vaughn Vernon (the "Red Book")
- *Patterns of Enterprise Application Architecture* — Martin Fowler
- *Building Microservices* — Sam Newman
- *Monolith to Microservices* — Sam Newman
- *Designing Data-Intensive Applications* — Martin Kleppmann
- *Enterprise Integration Patterns* — Gregor Hohpe & Bobby Woolf
- *Learning Domain-Driven Design* — Vlad Khononov
- The Twelve-Factor App — https://12factor.net
