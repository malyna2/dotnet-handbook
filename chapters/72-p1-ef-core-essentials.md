# Part 1 · Module 2: EF Core Essentials

> **What this module makes you able to do.** Write and review EF Core data access that sends the SQL you expect: tracked reads only where you save, related data in one round trip, one `DbContext` per request, and a concurrency token that turns a lost update into an exception.

**Time:** reading ≈ 15 min; hands-on ≈ 1 h 10 min — the experiment 15 min, the Chapter 4 exercise 10, the questions 15, the check at work 30.

## Covers

- what `SaveChanges` compares, and why every tracked read costs memory;
- when `AsNoTracking` pays, and what it changes besides speed;
- `Include` against lazy and explicit loading, and the N+1 problem: how to see it and how to kill it;
- projections, and why an `Include` above a `Select` does nothing;
- `DbContext` lifetime: scoped, not thread-safe, and cheap because connections are pooled;
- optimistic concurrency: a concurrency token, `DbUpdateConcurrencyException`, and the window the token covers.

## The mechanism to explain without notes

**A `DbContext` is a unit of work that remembers every entity it hands you, and it talks to the database at three moments only: when a query is enumerated, when a navigation is loaded, and at `SaveChanges`.**

A tracking query stores a snapshot of each entity's original values and returns one instance per key (identity resolution). `SaveChanges` compares every tracked entity with its snapshot and sends `UPDATE`s for the changed columns only, all in one transaction. The traps follow from that:

- **Tracking is paid per entity.** The snapshot and the identity map exist for every row a tracking query returns, and a read-only endpoint gets nothing for them. Use `AsNoTracking()`, or project: a result that holds no entities is never tracked.
- **The query decides the SQL, not the code that runs after it.** Related rows arrive only if the query asked for them, with `Include` or with navigations inside the `Select`. Otherwise every navigation you touch is one more statement (lazy or explicit loading: 1 + N), or nothing at all (lazy loading off: an empty collection). Under a projection the `Select` alone decides the SQL, so an `Include` above it is dead code.
- **One context is one unit of work, on one thread at a time.** Its change tracker isn't thread-safe, and EF Core throws when it sees a second operation start before the first has finished. `AddDbContext` registers it as scoped, one per request. That is cheap because the physical connection underneath comes from ADO.NET's pool; a singleton that captures a context shares one tracker across every request (Chapter 2's captive dependency).
- **The snapshot is also the concurrency check.** A concurrency token's *original* value, the one the context read, goes into the `UPDATE`'s `WHERE`. Zero rows affected means the row changed since that read, and `SaveChanges` throws `DbUpdateConcurrencyException`.

## Read (≈ 15 min)

1. [Chapter 4: DbContext and Change Tracking](#dbcontext-and-change-tracking): the snapshot and the five entity states.
2. [Chapter 4: AsNoTracking: Read-Only Speed](#asnotracking-read-only-speed).
3. [Chapter 4: Loading Related Data: Eager, Lazy, Explicit](#loading-related-data-eager-lazy-explicit).
4. [Chapter 4: The N+1 Problem — Seeing It and Killing It](#the-n1-problem-seeing-it-and-killing-it).
5. [Chapter 4: Projections: Select Only What You Need](#projections-select-only-what-you-need), then [Include + Projection: The Include Is Silently Ignored](#include-projection-the-include-is-silently-ignored).
6. [Chapter 4: Split Queries](#split-queries): what two collection `Include`s do to the row count.
7. [Chapter 4: DbContext Lifetime and Connection Pooling](#dbcontext-lifetime-and-connection-pooling).
8. [Chapter 4: Concurrency: Optimistic vs Pessimistic](#concurrency-optimistic-vs-pessimistic).

## Prove it

The same 50 orders and 100 order lines, loaded four ways, with every SQL command EF Core sends counted. Predict the four counts before you run it; the last one is the surprise.

`verify/path/NPlusOne/Program.cs` · run it from `verify/path` with `dotnet run --project NPlusOne` (the .NET 10 SDK is all it needs):

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

// Prove it: loading related rows one parent at a time sends 1 + N SQL statements; Include or a projection sends 1.
int statements = 0;
var options = new DbContextOptionsBuilder<Shop>().UseSqlite("Data Source=shop;Mode=Memory;Cache=Shared")
    .LogTo(_ => statements++, [RelationalEventId.CommandExecuted]).Options;       // counts every SQL command sent
using var seed = new Shop(options);
seed.Database.OpenConnection();                    // an in-memory database lives while a connection to it is open
seed.Database.EnsureCreated();
seed.Orders.AddRange(Enumerable.Range(1, 50).Select(i => new Order { Lines = [new() { Sku = $"A-{i}" }, new() { Sku = $"B-{i}" }] }));
seed.SaveChanges();
Count("Load() each order's lines in a loop", db =>
{
    var orders = db.Orders.ToList();
    foreach (var order in orders) db.Entry(order).Collection(o => o.Lines).Load();   // what lazy loading runs per order
    return orders.Sum(o => o.Lines.Count);
});
Count("Include(o => o.Lines)", db => db.Orders.Include(o => o.Lines).ToList().Sum(o => o.Lines.Count));
Count("Select(o => new { o.Id, Skus = ... })", db => db.Orders.Select(o => new { o.Id, Skus = o.Lines.Select(l => l.Sku).ToList() }).ToList().Sum(o => o.Skus.Count));
Count("neither, and no lazy loading", db => db.Orders.ToList().Sum(o => o.Lines.Count));
void Count(string label, Func<Shop, int> load)
{
    using var db = new Shop(options);              // a fresh context, as each request gets from DI
    statements = 0;
    Console.WriteLine($"{label,-38} {load(db),3} order lines, {statements,2} SQL statement(s)");
}
class Shop(DbContextOptions<Shop> options) : DbContext(options) { public DbSet<Order> Orders => Set<Order>(); }
class Order { public int Id { get; set; } public List<OrderLine> Lines { get; set; } = []; }
class OrderLine { public int Id { get; set; } public int OrderId { get; set; } public string Sku { get; set; } = ""; }
```

```text
Load() each order's lines in a loop    100 order lines, 51 SQL statement(s)
Include(o => o.Lines)                  100 order lines,  1 SQL statement(s)
Select(o => new { o.Id, Skus = ... })  100 order lines,  1 SQL statement(s)
neither, and no lazy loading             0 order lines,  1 SQL statement(s)
```

What to notice:

- **1 + N is a count, not a slow query.** 51 statements: one for the orders, then one per order. Lazy loading produces exactly this, because a lazy navigation's getter runs the same `Load` on first access; there is just no call to see in the loop. Against SQLite in memory each statement costs microseconds. Against a database server each one is a network round trip and a connection borrowed from the pool, and their number grows with the rows on the page.
- **`Include` and the projection send one statement each.** Both become one `LEFT JOIN`. The projection reads only the columns in its `Select` and tracks nothing; `Include` materialises and tracks all 150 entities.
- **Loading neither is not slow; it is wrong.** With lazy loading off, `o.Lines` stays the empty list the entity was created with: 0 lines, and no exception. Switching lazy loading off without adding `Include` or a projection turns an N+1 into missing data. A collection the entity doesn't initialise stays `null` instead, and the loop throws.
- **The fresh context per measurement matters.** The seed context still tracks every order and line, so a query through it would return the instances it already holds, lines attached, without loading anything. A test that seeds and queries through one context can pass against code that returns nothing in production.

Then do Chapter 4's *Find the bug*, in the Exercises at the end of [Chapter 4](#chapter-4-data-access-databases): count the queries the endpoint sends before you open the answer.

## Three questions

**1.** An endpoint sends 1 + N queries, and every one of their plans is an index seek under a millisecond. Why is the endpoint slow, why can't any single plan show the problem, and how do you find it?

<details>
<summary>Answer</summary>

- **The cost is the count.** Each statement pays a network round trip, a connection borrowed from the pool and a round of materialisation. N statements pay it N times, and N grows with the data, not with the code.
- **No plan contains it.** Each statement really is cheap, so each plan looks perfect. The problem is the number of statements one request sends, and no plan holds that number.
- **Count statements per request.** In development, EF Core's command log (`LogTo`, or the `Microsoft.EntityFrameworkCore.Database.Command` category at `Information`). In production, the database spans of one request's trace (Chapter 13), or call counts on the server: `pg_stat_statements` (Chapter 37, Level 3) or Query Store. The signature is a statement whose call count is a multiple of another's.
- **Fix:** `Include`, a projection or a split query, and lazy loading off so the pattern can't come back unnoticed.
</details>

**2.** A query starts with `.Include(o => o.Customer)`, has `.AsNoTracking()` in the middle and ends with `.Select(o => new OrderRow(o.Id, o.Customer.Name))`. Which of the three calls changes the SQL or the work, and why?

<details>
<summary>Answer</summary>

- **The `Select` decides everything.** The navigation `o.Customer.Name` inside it generates the join, and only the two selected columns are read.
- **`Include` does nothing.** It is an instruction for materialising `Order` entities, and this query materialises `OrderRow`s. EF Core drops it without a warning; the SQL is identical without it.
- **`AsNoTracking` does nothing either.** Tracking applies to entity instances, and a result without entities is never tracked.

Delete both. Both start to matter only if the result contains the entity again, for example `.Select(o => new { Order = o, o.Customer.Name })`.
</details>

**3.** `Product` has a `[Timestamp]` row version. A user opens the edit form, someone else saves the same product, and five minutes later the first user saves and silently overwrites that change. No `DbUpdateConcurrencyException`. Why, and what is the fix?

<details>
<summary>Answer</summary>

- **The token guards the window between one context's read and its write.** EF Core puts the token's *original* value, the one this context read, into the `UPDATE`'s `WHERE`.
- **The save handler read the current version.** The user's form came from version 1. The other save made it version 2. The handler loads the product (version 2), copies the form onto it and sends `UPDATE … WHERE RowVersion = <version 2>`: one row matches, nothing throws, and the other user's change is gone.
- **Fix: make the original value the one the user saw.** Send the row version with the form (or as an `ETag`), and before `SaveChanges` set `db.Entry(product).Property(p => p.RowVersion).OriginalValue = form.RowVersion`. Now zero rows match, `SaveChanges` throws, and the handler returns `409 Conflict` (`412 Precondition Failed` for an `If-Match` header) so the user reloads.
</details>

## Check at work

**Inspect.** Turn on EF Core's command log in development, call your busiest endpoint once with realistic data, and count the `Executed DbCommand` lines. Good: a small number that stays the same when the data grows. Bad: a number that grows with the rows on the page. Then search the code for `.Include(` in queries that end in `.Select(`, for `UseLazyLoadingProxies`, and for a `DbContext` held by anything registered as a singleton, hosted services included.

**Measure.** In your APM or OpenTelemetry traces, the number of database spans per request for that endpoint over a day. Look at the maximum, not the average: N+1 shows on the requests with the most rows.
