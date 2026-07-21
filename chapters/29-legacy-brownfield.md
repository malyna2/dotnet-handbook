# Chapter 29: Working with Legacy & Brownfield Code

_⏱️ Estimated read time: ~30 min ·     4293 words (study pace)_

## The Myth of the Greenfield

Somewhere early in your career you probably imagined that senior engineers spend their days architecting elegant new systems from a blank editor. The reality is almost the exact opposite. The overwhelming majority of professional software work happens on systems that already exist, already have users, already generate revenue, and already carry years of decisions — good and bad — baked into their code. This is *brownfield* development: building on land that has already been developed, where you must account for existing structures rather than pouring a fresh foundation.

The move from mid-level to senior is, more than anything, the ability to be effective in this environment. Junior developers freeze in front of a million-line codebase they don't understand. Senior developers have a systematic approach for changing code safely even when they only understand a small slice of it. That approach — the mindset, the techniques, and the tooling — is what this chapter is about.

### What "Legacy" Actually Means

The word "legacy" gets thrown around as an insult, usually meaning "code I didn't write and don't like." That's not a useful definition. Michael Feathers, in *Working Effectively with Legacy Code*, offers a far sharper one:

> **Legacy code is simply code without tests.**

Why tests specifically? Because without tests, you have no fast, repeatable way to know whether a change broke something. Code without tests is code you're afraid to touch, and *fear* is the defining emotion of working with legacy systems. You need a change, you find the spot, but you don't dare modify it because you can't predict the blast radius. So you paste in a special case, add a flag, or wrap it in another `if`. Multiply that by thousands of changes over a decade and you get the sprawling, tangled systems everyone complains about.

Understood this way, legacy is not about age. A service written last month with zero tests and tight coupling is legacy the moment it ships. A well-tested COBOL system might be easier to change safely than that. Legacy is a property of *changeability*, not calendar date.

### The Psychology and the Value

Two attitudes will sink you before you write a line of code.

The first is contempt. It is tempting to look at legacy code and assume the people who wrote it were incompetent. Almost always they were not. They were working under deadlines you don't know about, with framework versions that no longer exist, satisfying requirements that have since changed. That "insane" workaround was probably a rational response to a constraint that has been forgotten. Approach the code as an archaeologist, not a critic.

The second is the urge to rewrite. We'll return to this, but hold the thought: the ugly code that's still running is doing something valuable. It encodes thousands of bug fixes and edge cases — the accumulated learning of the business — that exist *nowhere else*, certainly not in any specification. That value is real, and throwing it away is expensive.

> **Best practice:** Before changing legacy code, assume it is the way it is for a reason you don't yet see. Your job is to understand that reason, then make it better, not to prove you're smarter than your predecessors.

## Changing Code You Don't Understand

The core problem of legacy work is a dilemma. To change code safely, you'd like tests. To write tests, you usually need to change the code so it can be tested (to break dependencies). But changing code without tests is exactly what's dangerous. How do you get out of this loop?

Feathers' answer is a discipline built around three ideas: **characterization tests**, **seams**, and **dependency-breaking techniques**.

### Characterization Tests: Pinning Down Behavior

A normal unit test asserts *correct* behavior — what the code *should* do. A characterization test asserts *actual* behavior — what the code *currently does*, correct or not. You are not trying to specify the truth; you are trying to build a net that catches any change in behavior.

The technique is almost mechanical. Write a test that calls the code, assert something you know is wrong (like expecting `null`), run it, and let the failure tell you the real value. Then encode that real value.

```csharp
[Fact]
public void CharacterizeInvoiceTotal()
{
    var calculator = new InvoiceCalculator();
    var invoice = BuildTypicalInvoice();

    var total = calculator.CalculateTotal(invoice);

    // I don't yet know the "right" answer. I write a deliberately
    // wrong assertion, run it, read the actual value from the failure,
    // then pin that value here:
    Assert.Equal(142.87m, total);
}
```

This feels strange the first time. You are asserting a number you didn't reason about. But that's the point: you're documenting the current reality so that when you refactor, any *accidental* change in the result lights up red. If the pinned value turns out to be a bug, you now have a test that will tell you the moment you fix it — deliberately.

> **Pitfall:** Don't try to write "correct" tests for code you don't understand. You'll waste days and produce assertions that are themselves wrong. Characterize first, understand later, correct last.

### Seams: Places to Change Behavior Without Editing

A **seam** is a place where you can alter the behavior of your program without editing in that place. Seams are how you get a tendril of test harness into otherwise rigid code. The most useful seam in C# is the *object seam*: because a method call can be dispatched to a subclass or an interface implementation, you can substitute a test double at that point.

Consider this untestable method:

```csharp
public class OrderProcessor
{
    public void Process(Order order)
    {
        // Hard dependency on the real world: a static clock,
        // a direct DB call, and an SMTP send. None can be faked.
        if (DateTime.Now.Hour < 9)
            throw new InvalidOperationException("Too early");

        SqlGateway.Save(order);
        new SmtpClient("mail.corp.local").Send(BuildEmail(order));
    }
}
```

There is no seam here. `DateTime.Now`, the static `SqlGateway`, and the `new SmtpClient` are all *hard-coded dependencies* — you cannot get between this code and the outside world. Every dependency-breaking technique that follows is, at heart, a way to *introduce a seam* so that a test can take control.

### Dependency-Breaking Technique 1: Extract Interface

The cleanest seam is an interface. Pull the collaborators behind interfaces and inject them.

```csharp
public interface IClock { DateTime Now { get; } }
public interface IOrderStore { void Save(Order order); }
public interface INotifier { void Send(Order order); }

public class OrderProcessor
{
    private readonly IClock _clock;
    private readonly IOrderStore _store;
    private readonly INotifier _notifier;

    public OrderProcessor(IClock clock, IOrderStore store, INotifier notifier)
        => (_clock, _store, _notifier) = (clock, store, notifier);

    public void Process(Order order)
    {
        if (_clock.Now.Hour < 9)
            throw new InvalidOperationException("Too early");

        _store.Save(order);
        _notifier.Send(order);
    }
}
```

Now every collaborator is a seam. A test injects a fake clock fixed at 03:00 to verify the guard, or an in-memory `IOrderStore` to verify persistence, all without touching a database or a mail server.

### Dependency-Breaking Technique 2: Wrap the Dependency

Sometimes you can't inject through the constructor because too many callers already exist. A gentler move is *parameterize* the dependency with an overload, or wrap a static call behind an instance you can override. The **Extract and Override Call** technique makes the awkward call a `protected virtual` method:

```csharp
public class OrderProcessor
{
    public void Process(Order order)
    {
        if (CurrentTime().Hour < 9)
            throw new InvalidOperationException("Too early");
        Save(order);
    }

    // Seams created purely so a test subclass can override them.
    protected virtual DateTime CurrentTime() => DateTime.Now;
    protected virtual void Save(Order order) => SqlGateway.Save(order);
}
```

In the test you subclass `OrderProcessor` (a *testing subclass*) and override `CurrentTime()` and `Save()` to observe or control them. It's not the final design you want, but it gets the code under test today with a minimal, low-risk edit.

### Sprout Method and Sprout Class

Suppose you need to add logic to a giant, untested method. You can't safely refactor the whole thing first. The **Sprout Method** technique says: write your *new* logic in a brand-new method — which you write test-first — and call it from the old code with a single line.

```csharp
public void PostTransactions(List<Transaction> transactions)
{
    // ...200 lines of legacy code you dare not touch...

    // New requirement: dedupe transactions before posting.
    // Instead of weaving it into the mess, sprout a tested method:
    transactions = RemoveDuplicates(transactions);   // <-- one new line

    // ...200 more lines...
}

// New, fully tested, isolated:
public List<Transaction> RemoveDuplicates(List<Transaction> input) =>
    input.GroupBy(t => t.Id).Select(g => g.First()).ToList();
```

The new behavior is clean and tested; the legacy method is barely disturbed. **Sprout Class** is the same idea scaled up: when the new responsibility is large, or the host class is so entangled you can't even instantiate it in a test, put the new logic in a new class and call into it. You get an island of quality inside the swamp, and over time the islands grow.

### Wrap Method

**Wrap Method** is for when you need to run additional behavior *whenever* an existing method runs. Rename the original, then create a new method with the old name that calls both the original and your addition:

```csharp
// Before: public void Pay(Employee e) { ...existing... }

public void Pay(Employee e)
{
    LogPayrollAudit(e);   // new behavior
    DispatchPayment(e);   // the original, renamed
}

private void DispatchPayment(Employee e) { /* ...original body... */ }
private void LogPayrollAudit(Employee e) { /* new, tested */ }
```

Every existing caller of `Pay` now gets the audit log for free, and you never touched the original logic. These techniques share a philosophy: **add, don't modify.** Adding is low-risk; modifying untested code is high-risk. Push as much change as you can into new, tested code, and change existing code as little as physically possible.

## Modernizing at Scale: Strangler Fig vs. the Big Rewrite

The techniques above operate at the method and class level. When the goal is to modernize an entire system — say, move a .NET Framework monolith to modern .NET — you need a strategy at the architecture level. Here the field has essentially settled on one answer, and it has a memorable name.

### Why Big-Bang Rewrites Usually Fail

The seductive plan is: freeze the old system, build a shiny new one in parallel, and cut over when it's ready. This is the *big-bang rewrite*, and it fails with grim reliability. The reasons are structural, not down to bad luck:

- **You lose the encoded knowledge.** The old system's thousands of edge cases live only in its code. The rewrite re-discovers each one as a production incident.
- **The target keeps moving.** The business can't stop for two years. Every feature added to the old system must also be added to the new one, so you're maintaining two systems and never catching up.
- **Value arrives only at the end** — if it arrives. For years there's no return on the investment, which makes the project a prime target for cancellation the moment budgets tighten.
- **Cutover is all-or-nothing.** You flip the switch and either it works or the business stops. There is no gentle rollback.

> **Pitfall:** "We'll just rewrite it, it'll be faster the second time" is one of the most expensive sentences in software. The second time you'll rediscover why the first version was complicated — the hard way, in production.

### The Strangler Fig Pattern

Martin Fowler named the *Strangler Fig* pattern after the tropical vine that germinates in a host tree's canopy, sends roots down around the trunk, and gradually envelops the tree until it can stand on its own — the original eventually gone, its structure replaced in place.

Applied to software: you build the new system *around* the old one, incrementally routing functionality away from the legacy system piece by piece, until the legacy system is doing nothing and can be deleted. You never have two full systems running in parallel hoping to swap; instead the new grows and the old shrinks continuously.

The mechanics usually center on an **interception layer** — most often an HTTP proxy, gateway, or facade — sitting in front of the legacy system:

```
Step 0:   [ Clients ] ─────────────► [ Legacy Monolith ]

Step 1:   [ Clients ] ──► [ Facade / Router ] ──► [ Legacy Monolith ]
          (facade added; all traffic still flows to legacy)

Step 2:   [ Clients ] ──► [ Facade / Router ] ─┬─► [ New: Orders svc ]
                                                └─► [ Legacy Monolith ]
          (Orders feature reimplemented; router sends /orders to it)

Step 3:   [ Clients ] ──► [ Facade / Router ] ─┬─► [ New: Orders svc ]
                                                ├─► [ New: Billing svc ]
                                                └─► [ Legacy (shrinking) ]

Step N:   [ Clients ] ──► [ Facade / Router ] ──► [ New services ]
          (legacy has nothing left to do — delete it)
```

The router can be as simple as URL-prefix rules in YARP (Microsoft's reverse proxy for .NET) or a piece of middleware:

```csharp
app.MapWhen(
    ctx => ctx.Request.Path.StartsWithSegments("/orders"),
    branch => branch.UseMiddleware<NewOrdersModule>());

// Everything not yet migrated falls through to the legacy proxy.
app.Run(async ctx => await _legacyProxy.ForwardAsync(ctx));
```

The strategic advantages mirror the rewrite's failures point for point: **value ships continuously** (each migrated slice is in production immediately), **risk is bounded** (if the new Orders service misbehaves, you re-route that one path back to legacy), and **you can stop anytime** with a coherent, working system. Strangler Fig is not merely "nicer"; it is a fundamentally lower-risk bet.

> **Best practice:** Migrate along seams that matter to the business — a whole capability like "checkout" or "reporting" — not arbitrary technical layers. A migrated slice should be independently valuable and independently deployable.

### Anti-Corruption Layers at the Boundary

While old and new coexist, they must talk to each other, and here lies a trap: the new system, if it calls the old one naively, will end up shaped by the old system's awkward data model and conventions. The clean new domain gets "corrupted" by legacy concepts leaking across the boundary.

The **Anti-Corruption Layer (ACL)** — a term from Eric Evans' Domain-Driven Design — is a translation layer that keeps the two models isolated. It speaks the legacy system's language on one side and your clean domain's language on the other, translating between them so neither leaks into the other.

```csharp
// The new domain model is clean and expressive.
public record Customer(CustomerId Id, string FullName, LoyaltyTier Tier);

// The legacy service returns a flat, cryptic DTO. Keep it OUT of the domain.
public interface ICustomerRepository
{
    Task<Customer> GetAsync(CustomerId id);
}

public class LegacyCustomerAcl : ICustomerRepository
{
    private readonly LegacyCrmClient _legacy;

    public async Task<Customer> GetAsync(CustomerId id)
    {
        LegacyCustRec rec = await _legacy.FetchCust(id.Value);   // CUST_NM, LYLTY_CD...

        // Translation lives here and ONLY here.
        return new Customer(
            id,
            $"{rec.CUST_FNM} {rec.CUST_LNM}".Trim(),
            MapTier(rec.LYLTY_CD));
    }

    private static LoyaltyTier MapTier(string code) => code switch
    {
        "G" => LoyaltyTier.Gold,
        "S" => LoyaltyTier.Silver,
        _   => LoyaltyTier.Standard
    };
}
```

Now the rest of your new code never sees `CUST_FNM` or `LYLTY_CD`. When the legacy CRM finally dies, you rewrite one class and the domain is untouched.

## .NET Framework to Modern .NET in Practice

For .NET developers, the most common brownfield project of the decade is moving from .NET Framework (4.x) to modern .NET (currently .NET 8/9). The two are related but genuinely different runtimes, and the migration is where the abstract strategy above meets concrete tooling.

### Assess Before You Touch

Start with analysis, not code. Microsoft ships tools for exactly this:

- **The .NET Upgrade Assistant** — a CLI and Visual Studio extension that analyzes a solution, produces an upgrade report, and can perform many mechanical changes (SDK-style project conversion, target-framework updates, known package swaps) in guided, incremental steps.
- **try-convert** — a lower-level tool that converts old-style `.csproj` files to the terse SDK-style format. The Upgrade Assistant uses it under the hood; you can also run it directly.
- **The .NET Portability Analyzer** / API analyzers — tools that flag uses of APIs unavailable on your target framework, so you know your exposure *before* committing.

The output you want from this phase is a map: which projects convert trivially, which depend on APIs or packages that don't exist on modern .NET, and which are effectively rewrites.

### The Common Blockers

Certain technologies simply don't cross the bridge unchanged. Know them going in:

| Legacy (.NET Framework) | Modern .NET path |
|---|---|
| System.Web / Web Forms | No direct port — reimplement on **ASP.NET Core** (MVC/Razor Pages/Minimal APIs) |
| WCF service host | **CoreWCF** (community port) for compatibility, or re-front with **gRPC** / REST for new clients |
| ASMX web services | ASP.NET Core Web API |
| `app.config` / `web.config` `<appSettings>` | **`IConfiguration`** (appsettings.json, env vars, secrets) |
| `HttpContext.Current` (ambient static) | Injected `IHttpContextAccessor` |
| App domains, remoting, `System.Drawing` on non-Windows | Redesign; use supported alternatives |

WCF deserves special note. If you host SOAP services and clients you don't control, **CoreWCF** lets you keep the contract while running on modern .NET — a compatibility bridge, not a destination. For internal service-to-service calls where you own both ends, migrating to **gRPC** (which .NET supports first-class) or plain HTTP APIs is usually the better long-term move.

### Configuration: app.config to IConfiguration

The configuration model is one of the most visible changes. Old code reaches into a global static:

```csharp
// .NET Framework
var timeout = int.Parse(ConfigurationManager.AppSettings["TimeoutSeconds"]);
var conn = ConfigurationManager.ConnectionStrings["Main"].ConnectionString;
```

Modern .NET uses a composed, injectable `IConfiguration`, ideally bound to strongly typed *options*:

```csharp
// appsettings.json → bind to a POCO
public class OrderOptions { public int TimeoutSeconds { get; set; } }

builder.Services.Configure<OrderOptions>(
    builder.Configuration.GetSection("Order"));

// Consume via DI, not a static lookup:
public class OrderService(IOptions<OrderOptions> options)
{
    private readonly OrderOptions _cfg = options.Value;
}
```

This is more than syntax. `ConfigurationManager` is an ambient static — itself a testability seam problem. Moving to injected options makes the dependency explicit and mockable, which is exactly the direction all the earlier techniques pushed you.

### Multi-Targeting During the Transition

You rarely flip a large solution in one commit. A powerful transitional trick is to make shared libraries **multi-target** — compile for both frameworks at once — so the same project can be referenced by legacy and modern hosts simultaneously:

```xml
<PropertyGroup>
  <TargetFrameworks>net48;net8.0</TargetFrameworks>
</PropertyGroup>
```

Where the two frameworks diverge, guard the differences with conditional compilation:

```csharp
public string ContentRoot()
{
#if NET48
    return AppDomain.CurrentDomain.BaseDirectory;
#else
    return AppContext.BaseDirectory;
#endif
}
```

Better still, target **.NET Standard 2.0** for pure library code where possible — it's the common denominator both runtimes understand, letting one binary serve both without `#if` at all. Multi-targeting is the Strangler Fig applied to your class libraries: the shared core is modernized first and quietly serves both worlds while the hosts migrate around it.

## Living With a Big Ball of Mud

Not every legacy system is a candidate for framework migration this quarter. Often you simply inherit a *Big Ball of Mud* — a system with no discernible architecture — and must keep improving it while keeping it running. The senior skill here is *prioritization*: you cannot refactor everything, so refactor what pays.

### Finding Hotspots: Churn × Complexity

The best signal for where to spend refactoring effort is the intersection of two measures:

- **Churn** — how often a file changes (from your version-control history).
- **Complexity** — how tangled the file is (cyclomatic complexity, or a cheap proxy like line count / indentation depth).

A file that is complex but never changes is not worth touching — it's stable, leave it. A file that changes constantly but is simple is fine. The danger zone is the top-right quadrant: **high churn *and* high complexity.** That's code the team fights with every week, and every fight risks a bug. Refactoring there yields the highest return.

You can approximate churn straight from git:

```bash
# Files ranked by number of commits touching them (last 12 months)
git log --since="12 months ago" --name-only --pretty=format: \
  | grep '\.cs$' | sort | uniq -c | sort -rn | head -30
```

Cross-reference that list with a complexity report (from an analyzer, or tools like *code-maat* / NDepend), and you have a data-driven refactoring backlog instead of a gut-feel one. This is Adam Tornhill's "behavioral code analysis," and it consistently beats intuition about where the pain really is.

> **Best practice:** Refactor along the path of your actual work. When a ticket takes you into a hotspot, leave that corner a little cleaner (the "Boy Scout Rule"). You get modernization for free as a side effect of feature work, and you only invest in code that's proven it matters by changing.

### Making Technical Debt Visible and Deliberate

The metaphor of *technical debt* is due to Ward Cunningham, and it's precise: shipping imperfect code is like borrowing money. It lets you move faster now, but you pay *interest* — every future change in that area is slower — until you pay down the *principal* by refactoring.

The failure mode isn't having debt; all real systems carry some. The failure is *invisible* debt that nobody chose. Manage it deliberately:

- **Make it visible.** Track debt as backlog items, tag it, or keep a lightweight debt register. What isn't tracked can't be prioritized or communicated to stakeholders.
- **Distinguish deliberate from reckless debt.** "We're shipping a known shortcut to hit the launch, and here's the follow-up ticket" is prudent. "We had no idea this was a shortcut" is not.
- **Mark it in code.** A `// TODO`/`// HACK` with a ticket number and a reason is a breadcrumb for the next person (often you). Fail-fast markers help too:

```csharp
// HACK(#4821): tax rounding hard-coded to EU rules until the
// pricing service exposes locale. Revisit before US launch.
[Obsolete("Temporary: see #4821. Do not build new callers on this.")]
public decimal RoundTax(decimal amount) => Math.Round(amount, 2);
```

- **Budget for it.** Reserve a standing slice of each iteration for debt paydown. If it competes ticket-by-ticket with features, it always loses, and the interest compounds until the team grinds to a halt.

## Data, Downtime, and Measuring Progress

Two concerns are easy to under-plan and expensive to get wrong.

**The database is usually the hardest part.** Code can be deployed and rolled back in minutes; data migrations are often one-way and touch information you cannot afford to lose. When a schema must change beneath a running system, use the **expand/contract (parallel change)** pattern: first *expand* — add the new column/table and write to both old and new shapes; then *migrate* — backfill existing rows and shift reads to the new shape; finally *contract* — once nothing reads the old shape, remove it. At every step the system stays deployable and reversible, which is the whole point. During a Strangler migration where old and new code share a database, treat that shared schema as a boundary and put an ACL-style translation in front of it, or the two systems will couple through the data and you'll have gained nothing.

**Keep the lights on.** Modernization that breaks production destroys the credibility of the whole effort. Techniques that let you change safely at runtime are essential: **feature flags** to turn new paths on and off without redeploying; **parallel run** (a.k.a. "dark launch"), where you execute both old and new implementations, serve the old result, and *compare* the new one in the background to build confidence before cutting over; and **canary releases** that expose the new path to a small percentage of traffic first.

```csharp
public decimal Price(Cart cart)
{
    var legacy = _legacyPricer.Price(cart);

    if (_flags.IsEnabled("new-pricing-shadow"))
    {
        var next = _newPricer.Price(cart);
        if (next != legacy)
            _log.Warning("Pricing mismatch {Cart}: {Old} vs {New}",
                cart.Id, legacy, next);   // observe, don't serve — yet
    }
    return legacy;   // still trusting the old path
}
```

**Measure progress with real signals, not vibes.** A modernization program that can't show it's working will be cancelled. Track things that stakeholders and the team both feel: percentage of traffic served by new services; number of legacy endpoints retired; test coverage on hotspot files trending up; deployment frequency and lead time (are changes getting easier?); and change-failure rate (are they getting safer?). These last two come from the DORA metrics and are excellent proxies for "is this codebase becoming pleasant to work in." The goal isn't a perfect architecture on a slide; it's a system your team can change quickly and safely — and that you can *prove* is trending that way.

## Sources & Further Reading

- **Michael Feathers, *Working Effectively with Legacy Code*** — the definitive treatment of characterization tests, seams, dependency-breaking techniques, and Sprout/Wrap Method/Class. The single most important book on this topic.
- **Martin Fowler, "StranglerFigApplication"** and **"Legacy Modernization"** — martinfowler.com. Origin and detailed discussion of the Strangler Fig pattern and incremental modernization strategy.
- **Eric Evans, *Domain-Driven Design*** — source of the Anti-Corruption Layer pattern.
- **Microsoft Learn: ".NET Framework to .NET migration"** and **"Overview of the .NET Upgrade Assistant"** — learn.microsoft.com/dotnet/core/porting — official guidance, the Upgrade Assistant, try-convert, portability analysis, and API-compatibility resources.
- **Microsoft Learn: "Anti-corruption Layer pattern"** and **"Strangler Fig pattern"** (Azure Architecture Center) — learn.microsoft.com/azure/architecture/patterns.
- **CoreWCF** (project documentation) — the community/Microsoft-supported path for hosting WCF services on modern .NET.
- **Ward Cunningham, "The WyCash Portfolio Management System" / the Technical Debt metaphor** — origin of technical debt as a deliberate, managed concept.
- **Adam Tornhill, *Your Code as a Crime Scene* and *Software Design X-Rays*** — churn × complexity hotspot analysis and behavioral code analysis.
- **Martin Fowler, "ParallelChange" (expand/contract)** — martinfowler.com — safe, reversible schema and interface evolution.
- **Brian Foote & Joseph Yoder, "Big Ball of Mud"** — the classic paper naming the un-architected system.
