# Chapter 4: Data Access & Databases

_⏱️ Estimated read time: ~35 min · 4913 words (study pace)_

Almost every non-trivial application is, underneath all its features, a machine for moving data in and out of a database safely and quickly. You can write flawless business logic and beautiful APIs, but if your data access layer holds locks too long, fires a thousand queries where one would do, or corrupts a balance under concurrent writes, the whole system fails in ways that are hard to reproduce and harder to fix. This chapter takes you from the mechanics of Entity Framework Core down to the SQL and storage engine underneath it, then back up through caching, NoSQL, and deployment. The goal is that you stop treating the database as a black box and start reasoning about what it actually does.

## Entity Framework Core: The Object-Relational Mapper

An ORM's job is to bridge two worlds that think differently. Your C# code thinks in objects, references, and collections. A relational database thinks in tables, rows, and foreign keys. EF Core translates between them. The danger is that the translation is *so* smooth you forget it is happening — and every performance problem in EF comes from forgetting that a property access or a `foreach` might quietly become a database round trip.

### DbContext and Change Tracking

The `DbContext` is the heart of EF Core. Think of it as a **unit of work** combined with a **session**: it represents a single logical conversation with the database. It holds a `DbSet<T>` for each entity type, translates your LINQ into SQL, and — crucially — it tracks changes.

```csharp
public class ShopContext : DbContext
{
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Order> Orders => Set<Order>();

    protected override void OnConfiguring(DbContextOptionsBuilder options)
        => options.UseSqlServer("Server=.;Database=Shop;Trusted_Connection=True;");

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Order>()
            .HasOne(o => o.Customer)
            .WithMany(c => c.Orders)
            .HasForeignKey(o => o.CustomerId);
    }
}
```

When you load an entity, the context keeps a **snapshot** of its original values in an internal structure called the change tracker. When you call `SaveChanges()`, EF compares the current state of each tracked entity against that snapshot, works out which properties changed, and generates the minimal `INSERT`, `UPDATE`, or `DELETE` statements.

```csharp
using var ctx = new ShopContext();
var customer = ctx.Customers.Single(c => c.Id == 42);
customer.Email = "new@example.com";   // no SQL yet — just a change in memory
ctx.SaveChanges();                     // now EF emits: UPDATE Customers SET Email=... WHERE Id=42
```

Notice you never told EF to save the customer. It knew, because it had been tracking. This is powerful but has a cost: keeping snapshots of every loaded entity uses memory and CPU. Every entity flows through five states — `Added`, `Unchanged`, `Modified`, `Deleted`, `Detached` — which you can inspect via `ctx.Entry(customer).State`.

> **Best practice:** Keep a `DbContext` short-lived. It is a unit of work for *one* operation (typically one web request), not a long-lived cache. A context that lives for hours accumulates tracked entities and becomes a memory leak and a correctness hazard.

### AsNoTracking: Read-Only Speed

If you are only reading data to send it out — the common case in a web API — you do not need change tracking at all. `AsNoTracking()` tells EF to skip building snapshots. For read-heavy endpoints this is a meaningful win in both allocation and speed.

```csharp
// Read-only query: no snapshots, faster, less memory.
var products = ctx.Products
    .AsNoTracking()
    .Where(p => p.IsActive)
    .ToList();
```

> **Rule of thumb:** If you are not going to modify and save the entities in this same context, add `AsNoTracking()`. You can even make it the default with `ctx.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;`.

### Loading Related Data: Eager, Lazy, Explicit

Your `Order` has a `Customer` and a collection of `OrderLines`. How does EF fill those in? There are three strategies, and choosing wrong is the single most common EF performance mistake.

**Eager loading** pulls related data in the same query using `Include`:

```csharp
var orders = ctx.Orders
    .Include(o => o.Customer)
    .Include(o => o.Lines)
        .ThenInclude(l => l.Product)
    .Where(o => o.Status == OrderStatus.Open)
    .ToList();
```

**Explicit loading** loads a relationship on demand, deliberately, with a visible method call:

```csharp
var order = ctx.Orders.Single(o => o.Id == id);
ctx.Entry(order).Collection(o => o.Lines).Load();   // explicit, one extra query on purpose
```

**Lazy loading** loads a relationship automatically the moment you access the navigation property. It requires the `Microsoft.EntityFrameworkCore.Proxies` package, virtual navigation properties, and `UseLazyLoadingProxies()`. It looks convenient and is a trap, as the next section shows.

### The N+1 Problem — Seeing It and Killing It

This is the defining performance bug of ORMs. Consider printing every order with its customer's name:

```csharp
var orders = ctx.Orders.ToList();          // 1 query: SELECT * FROM Orders
foreach (var o in orders)
    Console.WriteLine(o.Customer.Name);    // each access = 1 more query (lazy loading)
```

If there are 500 orders, this runs **1 + 500 = 501 queries**. That is the N+1 problem: one query for the parents, then N queries for the children. On a database with any network latency, 501 round trips can turn a 5ms operation into a 5-second one. The insidious part is that it looks fine in development against a local database with three rows.

The fix is to tell EF what you need up front:

```csharp
var orders = ctx.Orders
    .Include(o => o.Customer)   // one JOIN, one round trip
    .ToList();
foreach (var o in orders)
    Console.WriteLine(o.Customer.Name);   // already in memory, no query
```

> **Pitfall — turn lazy loading off by default.** Lazy loading is the primary engine of accidental N+1. Many senior teams disable it entirely and force developers to declare their data needs with `Include` or projections. Silent convenience that costs 500 round trips is not convenience.

### Projections: Select Only What You Need

Often you do not want whole entities at all — you want a few columns shaped into a DTO. Projecting with `Select` produces narrower SQL, avoids loading unused columns, and never needs change tracking:

```csharp
var summaries = ctx.Orders
    .Where(o => o.Status == OrderStatus.Open)
    .Select(o => new OrderSummary(
        o.Id,
        o.Customer.Name,          // EF generates the JOIN automatically
        o.Lines.Sum(l => l.Price) // aggregated in SQL, not in C#
    ))
    .ToList();
```

EF translates `o.Customer.Name` into a join and `o.Lines.Sum(...)` into a SQL aggregate. You get exactly the three values you asked for, computed by the database. **Projection is often the best answer to N+1** because it sidesteps both eager loading and tracking at once.

### Include + Projection: The Include Is Silently Ignored

`Include` and `Select` answer the same question — "what data does this query need?" — but only one of them can win, and it is always the projection. `Include` is an instruction about *entity materialization*: "when you build these `Order` entities, also build their `Customer` entities and wire up the navigation." The moment a query ends in a `.Select(...)` to a non-entity type, EF is no longer materializing `Order` entities at all — it is materializing your DTO — so there is nothing for the `Include` to attach to. EF drops it without a warning, an exception, or any trace in the generated SQL.

That does not mean the related data goes missing. In a projection, the SQL JOINs come from the *navigation accesses inside the `Select`*, not from the Includes above it:

```csharp
var rows = ctx.Orders
    .Include(o => o.Customer)      // dead code: ignored, produces no SQL
    .Include(o => o.Approver)      // dead code: ignored, produces no SQL
    .Where(o => o.Status == OrderStatus.Open)
    .Select(o => new OrderRow(
        o.Id,
        o.Customer.Name,           // THIS generates the JOIN to Customers
        o.Approver.LastName))      // THIS generates the JOIN to Approvers
    .ToList();
```

Delete both `Include` lines and the SQL is byte-for-byte identical. The query works either way, which is exactly why the pattern survives code review: the Includes *look* load-bearing, readers assume they are doing the eager loading, and nobody notices they are inert.

Why this matters beyond tidiness:

- **It miscommunicates.** A shared base query like `AccessibleOrders()` that stacks Includes implies "callers get orders with customers attached." If every caller finishes with a projection, that promise is never kept — and the day someone materializes the entities directly (`.ToList()` without a `Select`), the Includes suddenly *do* fire and the query's shape and cost change underneath them.
- **It hides the real dependency.** The columns a projection needs are declared inside the `Select`. Includes floating above it are noise that must be mentally diffed against the projection to understand what the query actually fetches.

The rule of thumb: a query either *materializes entities* (then `Include` is how you load relationships) or it *projects* (then the `Select` body is the single source of truth and Includes have no effect). Pick one per query, and delete Includes from any query that ends in a projection.

> **Gotcha.** The one nuance: `Include` is only ignored when the projection leaves entity types behind. If your `Select` returns an entity *inside* a wrapper — `.Select(o => new { Order = o, LineCount = o.Lines.Count })` — the `Order` entity is still being materialized, so Includes on it still apply. The dividing line is not "is there a Select" but "does the result still contain the entity the Include was for."

### Split Queries

Eager loading multiple collections with `Include` creates a problem called **cartesian explosion**. If an order has 10 lines and 5 shipments, a single JOIN returns 10 × 5 = 50 rows, duplicating the order data across every combination. `AsSplitQuery()` tells EF to run one query per collection instead:

```csharp
var orders = ctx.Orders
    .Include(o => o.Lines)
    .Include(o => o.Shipments)
    .AsSplitQuery()   // 3 queries total, no row multiplication
    .ToList();
```

The trade-off: multiple round trips versus one bloated result set. Use split queries when including several collections; keep single queries when including references (many-to-one) or one small collection.

### Compiled Queries

Every time EF runs a LINQ query it must translate the expression tree into SQL — parsing, analysing, caching by shape. For a hot query executed millions of times, that translation overhead adds up. `EF.CompileQuery` (or `CompileAsyncQuery`) does the translation once and hands you a reusable delegate:

```csharp
private static readonly Func<ShopContext, int, Customer?> _byId =
    EF.CompileQuery((ShopContext ctx, int id) =>
        ctx.Customers.FirstOrDefault(c => c.Id == id));

// Later, in a hot path:
var customer = _byId(ctx, 42);
```

This is a micro-optimization — reach for it only when profiling shows query compilation is a bottleneck, not by default.

### Set-Based Updates and Deletes: ExecuteUpdate and ExecuteDelete

Change tracking is the wrong tool for bulk writes. Updating 100,000 rows via load-modify-`SaveChanges` means materializing 100,000 entities, snapshotting each one, and issuing 100,000 individual `UPDATE`s. Since EF Core 7, `ExecuteUpdate` and `ExecuteDelete` translate your LINQ predicate directly into a single set-based SQL `UPDATE`/`DELETE` — nothing is loaded, nothing is tracked:

```csharp
await ctx.Orders
    .Where(o => o.Status == OrderStatus.Abandoned && o.Created < cutoff)
    .ExecuteDeleteAsync();

await ctx.Products
    .Where(p => p.CategoryId == categoryId)
    .ExecuteUpdateAsync(s => s.SetProperty(p => p.Price, p => p.Price * 0.9m));
```

> **Gotcha:** These bypass the change tracker entirely. Entities the context already tracks are *not* updated and silently go stale, and `SaveChanges` interceptors — and anything hooked to them, like auditing or domain-event dispatch — never fire. Use them for bulk maintenance and cleanup, not for writes your interceptors must see.

Modern EF mapping also reduces how often you drop to raw SQL for shape: EF Core 7 added **JSON columns** (map an owned aggregate to a single JSON column, still queryable through LINQ), and EF Core 8 added **complex types** (keyless value objects) and **primitive collections** (a `List<int>`/`string[]` stored inline as JSON instead of a side table).

## SQL Fundamentals

EF is a convenience over SQL, and to use it well you must understand the SQL it hides. A senior developer reads the generated SQL and the execution plan, not just the C#.

### Joins

A join combines rows from two tables based on a related column.

- **INNER JOIN** returns only rows with a match in both tables.
- **LEFT (OUTER) JOIN** returns all rows from the left table, with NULLs where the right has no match.
- **RIGHT JOIN** is the mirror; **FULL OUTER JOIN** returns unmatched rows from both sides.

```sql
SELECT o.Id, c.Name
FROM Orders o
INNER JOIN Customers c ON c.Id = o.CustomerId
WHERE o.Status = 'Open';
```

`o.Customer.Name` in an EF projection becomes exactly this INNER JOIN (or a LEFT JOIN if the relationship is optional).

### Indexes: Clustered, Non-Clustered, Covering

An index is to a table what the index at the back of a book is to its pages: a sorted structure that lets the engine find rows without scanning everything. Without indexes, a `WHERE` on a million-row table means reading all million rows — a **table scan**.

A **clustered index** *is* the table, physically sorted by the index key. Because the data itself is ordered this way, a table can have only one clustered index — usually the primary key. Looking up by the clustered key is the fastest possible read.

A **non-clustered index** is a separate structure holding the indexed columns plus a pointer back to the full row. Finding a row by a non-clustered index takes two steps: search the index, then follow the pointer to fetch the rest of the row — an operation called a **key lookup**.

A **covering index** eliminates that second step by *including* the extra columns the query needs directly in the index, via `INCLUDE`:

```sql
-- Query: SELECT Email, Name FROM Customers WHERE City = 'Berlin'
CREATE NONCLUSTERED INDEX IX_Customers_City
    ON Customers (City)
    INCLUDE (Email, Name);   -- now the index alone answers the query
```

The query is *covered* — everything it needs lives in the index, so no key lookups occur.

> **Pitfall:** Indexes speed up reads but slow down writes, because every `INSERT`/`UPDATE`/`DELETE` must maintain them. Do not index every column. Index the columns you filter, join, and sort on, and measure.

### Execution Plans

The execution plan is the database's step-by-step strategy for a query: which indexes it uses, in what order it joins, whether it scans or seeks. An **index seek** (jumping straight to matching rows) is good; an **index scan** or **table scan** on a large table under a selective filter usually signals a missing index. In SQL Server you view it with `SET SHOWPLAN_ALL ON` or the graphical plan in SSMS; watch for scans, expensive key lookups, and warnings about missing indexes.

### Transactions and ACID

A transaction groups statements so they succeed or fail as a unit. ACID names its four guarantees:

- **Atomicity** — all statements commit or none do. A half-transferred payment cannot exist.
- **Consistency** — the database moves from one valid state to another, respecting constraints.
- **Isolation** — concurrent transactions do not corrupt each other (tunable, see below).
- **Durability** — once committed, the change survives a crash.

```sql
BEGIN TRANSACTION;
    UPDATE Accounts SET Balance = Balance - 100 WHERE Id = 1;
    UPDATE Accounts SET Balance = Balance + 100 WHERE Id = 2;
COMMIT;   -- both or neither; ROLLBACK undoes everything
```

### Isolation Levels

Isolation is a dial trading correctness against concurrency. Loosening it lets more transactions run at once but admits anomalies:

- **Dirty read** — you read another transaction's uncommitted change that may be rolled back.
- **Non-repeatable read** — you read a row twice and get different values because another transaction updated it in between.
- **Phantom read** — you run the same range query twice and new rows appear because another transaction inserted them.

The four standard levels, from loosest to strictest:

| Level | Dirty | Non-repeatable | Phantom |
|---|---|---|---|
| Read Uncommitted | possible | possible | possible |
| Read Committed (default) | prevented | possible | possible |
| Repeatable Read | prevented | prevented | possible |
| Serializable | prevented | prevented | prevented |

Serializable is safest but takes the most locks and most reduces concurrency. Most systems run Read Committed and tighten specific transactions when correctness demands it. (SQL Server also offers `SNAPSHOT` isolation, which uses row versioning to give consistent reads without blocking writers.)

### Deadlocks

A deadlock occurs when transaction A holds a lock B needs, while B holds a lock A needs — a circular wait, each waiting forever. The database detects the cycle and kills one transaction (the "victim"), which errors out. The classic cause is two code paths that lock the same rows **in different orders**.

> **Best practice:** Always access tables and rows in a **consistent order** across your application, keep transactions short, and be ready to catch a deadlock error (SQL Server error 1205) and retry the operation.

## Dapper: When the ORM Is Too Much

EF is productive but adds overhead: expression translation, change tracking, materialization. Sometimes you want raw SQL with a thin, fast mapping to objects. **Dapper** is a micro-ORM — really a set of extension methods on `IDbConnection` — that executes your SQL and maps the result to C# types, nothing more.

```csharp
using var conn = new SqlConnection(connectionString);
var orders = conn.Query<OrderSummary>(
    @"SELECT o.Id, c.Name AS CustomerName, SUM(l.Price) AS Total
      FROM Orders o
      JOIN Customers c ON c.Id = o.CustomerId
      JOIN OrderLines l ON l.OrderId = o.Id
      WHERE o.Status = @status
      GROUP BY o.Id, c.Name",
    new { status = "Open" });   // parameterized — safe from SQL injection
```

Dapper shines for read-heavy reporting queries, complex hand-tuned SQL, and hot paths where EF's overhead matters. Many mature systems use **both**: EF for the write-side domain model where change tracking pays off, Dapper for high-volume reads. You lose change tracking, migrations, and LINQ, and you own the SQL — which is exactly the point when you want that control.

## Database Design and Normalization

Good schema design prevents whole classes of bugs. **Normalization** is the discipline of structuring tables so each fact is stored exactly once.

- **First Normal Form (1NF):** every column holds a single atomic value — no comma-separated lists, no repeating groups. One phone number per column, or a related table for many.
- **Second Normal Form (2NF):** 1NF plus every non-key column depends on the *whole* primary key (relevant to composite keys). No column should depend on just part of the key.
- **Third Normal Form (3NF):** 2NF plus no non-key column depends on another non-key column (no *transitive* dependencies). If you store `CustomerId` and also `CustomerCity`, the city depends on the customer, not the order — move it out.

The intuition: **one fact, one place.** When a customer changes their city, you want to update one row, not hunt down every order that duplicated it. Duplication is how databases drift into inconsistency.

**Foreign keys and constraints** are the database enforcing your rules so bad data cannot exist regardless of application bugs:

```sql
CREATE TABLE Orders (
    Id INT PRIMARY KEY,
    CustomerId INT NOT NULL,
    Total DECIMAL(10,2) NOT NULL CHECK (Total >= 0),
    Status VARCHAR(20) NOT NULL DEFAULT 'Open',
    CONSTRAINT FK_Orders_Customers
        FOREIGN KEY (CustomerId) REFERENCES Customers(Id)
);
```

The foreign key guarantees no order references a non-existent customer. The `CHECK` guarantees no negative totals. These are your last line of defence and they never have bugs the way application code does.

### When to Denormalize

Normalization optimizes for correct writes; it can make reads slower by requiring many joins. **Denormalization** deliberately duplicates data to speed reads — for example, storing a precomputed `OrderCount` on `Customer` instead of counting orders every time.

> **Best practice:** Normalize first, denormalize only when a measured read problem demands it — and then own the cost of keeping the duplicated data in sync (via triggers, application code, or scheduled jobs). Denormalization is a performance loan you repay with complexity.

## NoSQL: The Right Tool for the Shape of Your Data

Relational databases are the default for good reason, but some data shapes fit other models better.

- **MongoDB (document store):** stores JSON-like documents. Great when your data is hierarchical and read as a unit — a product with nested variants, specs, and reviews. Flexible schema suits evolving or heterogeneous data. Weaker at cross-document transactions and complex joins.
- **Redis (key-value / in-memory):** blazingly fast because it lives in RAM. Ideal for caching, session state, rate-limiting counters, leaderboards, and pub/sub. Not your system of record — treat it as ephemeral.
- **Elasticsearch (search engine):** built for full-text search, relevance ranking, and analytics over large volumes. Use it for "search the product catalog by fuzzy text" or log analytics — alongside, not instead of, your primary database, which stays the source of truth.

> **Rule of thumb:** Choose storage by access pattern, not by hype. Most systems are **polyglot**: a relational database as the source of truth, Redis for caching, and Elasticsearch for search. NoSQL is a specialization, not a replacement.

## Caching

The fastest query is the one you never run. Caching stores expensive results so repeat requests are served from fast storage.

### IMemoryCache vs IDistributedCache

`IMemoryCache` stores objects in the local process's RAM — extremely fast, but each server instance has its own copy, and it vanishes on restart. `IDistributedCache` stores serialized bytes in an external store (usually Redis) shared across all instances — a little slower, but consistent across a scaled-out cluster.

```csharp
// In-memory, single instance
public async Task<Product> GetProductAsync(int id)
{
    return await _cache.GetOrCreateAsync($"product:{id}", async entry =>
    {
        entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10);
        return await _db.Products.AsNoTracking().SingleAsync(p => p.Id == id);
    });
}
```

### The Cache-Aside Pattern

The most common caching strategy. The application, not the cache, owns the logic:

1. Look in the cache. If present (a **hit**), return it.
2. On a **miss**, load from the database, store it in the cache, then return it.

That `GetOrCreateAsync` above is cache-aside in one call. It is simple and robust because the cache is never the source of truth — a cold cache just means a slower first request, not a broken one.

### Invalidation and Stampede

> "There are only two hard things in computer science: cache invalidation and naming things." — Phil Karlton

**Invalidation** is deciding when cached data is stale. Two broad strategies: **expiration** (time-based — simple, but serves stale data for up to the TTL) and **explicit eviction** (remove the key when the underlying data changes — accurate, but you must remember every place that writes). Most systems combine both: evict on write, and set a TTL as a safety net for the writes you missed.

A **cache stampede** (or "dog-pile") happens when a popular key expires and hundreds of concurrent requests all miss simultaneously, all hammering the database at once to rebuild it — potentially overwhelming it exactly when traffic is highest. Defences include a **lock** so only one request rebuilds while others wait, **early/probabilistic refresh** (rebuild slightly before expiry), and serving slightly stale data during a refresh.

.NET 9's **HybridCache** (`Microsoft.Extensions.Caching.Hybrid`) packages these ideas for you: it unifies an in-process L1 with a distributed L2 behind a single `GetOrCreateAsync` API, and it ships cache-stampede protection out of the box — concurrent misses for the same key collapse into one rebuild. If you find yourself hand-rolling a two-level cache plus a rebuild lock, reach for it instead.

## Concurrency: Optimistic vs Pessimistic

When two users edit the same record at once, one can silently overwrite the other's changes — the **lost update** problem. Two philosophies address it.

**Pessimistic locking** assumes conflicts are likely: lock the row when you read it so nobody else can touch it until you are done (`SELECT ... FOR UPDATE`). Safe, but locks hurt concurrency and risk deadlocks. Suitable for short, high-contention operations like decrementing inventory.

**Optimistic concurrency** assumes conflicts are rare: let everyone read freely, but detect a conflict at save time. EF Core supports this with a **row version** (concurrency token). Each `UPDATE` includes the version in its `WHERE` clause; if another transaction already changed the row, the version no longer matches, zero rows are affected, and EF throws `DbUpdateConcurrencyException`.

```csharp
public class Product
{
    public int Id { get; set; }
    public int Stock { get; set; }

    [Timestamp]                       // maps to SQL Server rowversion
    public byte[] RowVersion { get; set; } = default!;
}

try
{
    var product = ctx.Products.Single(p => p.Id == id);
    product.Stock -= 1;
    ctx.SaveChanges();
    // EF emits: UPDATE Products SET Stock=@s WHERE Id=@id AND RowVersion=@original
}
catch (DbUpdateConcurrencyException)
{
    // Someone else updated it first. Reload, re-apply, and retry — or tell the user.
}
```

> **Best practice:** Prefer optimistic concurrency for typical web apps — it scales because it holds no locks. Reserve pessimistic locking for genuinely high-contention hotspots.

## Stored Procedures, Views, and Raw SQL

A **view** is a saved query you can select from like a table — useful for encapsulating a complex join or presenting a simplified shape. A **stored procedure** is precompiled SQL logic living in the database, callable by name.

Raw SQL — via stored procs, `FromSqlRaw`, or Dapper — is justified when: the query is too complex or too performance-critical for LINQ to express well; you need database-specific features EF does not surface; or you are doing bulk set-based operations (updating a million rows in one statement rather than loading and tracking them).

> **Note:** For the *bulk write* case specifically, you usually no longer need raw SQL — EF Core 7+ `ExecuteUpdate`/`ExecuteDelete` (covered above) issue a single set-based `UPDATE`/`DELETE` without loading or tracking, e.g. `ctx.Orders.Where(o => o.Status == OrderStatus.Abandoned).ExecuteDeleteAsync();`. Reserve raw SQL for genuinely complex queries or provider-specific features.

```csharp
// EF calling raw SQL while staying in the entity model
var open = ctx.Orders
    .FromSqlInterpolated($"SELECT * FROM Orders WHERE Status = {status}")
    .ToList();   // interpolated form is parameterized — not string concatenation
```

> **Pitfall:** Never build SQL by concatenating user input — that is the door to SQL injection. Always use parameters. Note the trade-off with stored procedures: logic in the database is invisible to your application's source control and CI unless you deliberately manage it as versioned migration scripts.

## DbContext Lifetime and Connection Pooling

Opening a physical database connection is expensive — a TCP handshake and authentication. **Connection pooling**, on by default in ADO.NET, keeps a pool of open connections and hands them out on demand, so "opening" a connection usually just borrows an idle one. This is why you should open connections late and close them early: you are borrowing from a shared, finite pool.

`DbContext` is **not thread-safe** and must be **scoped** — one instance per request. `AddDbContext` registers it with scoped lifetime, which is exactly right:

```csharp
builder.Services.AddDbContext<ShopContext>(o =>
    o.UseSqlServer(connectionString));   // scoped: one per HTTP request
```

> **Critical pitfall:** Never inject a `DbContext` into a **singleton** service. A singleton outlives the request scope, so it would share one context across all concurrent requests — a thread-safety disaster and a source of bizarre, intermittent bugs. If a singleton needs data, inject `IDbContextFactory<T>` and create a context per operation.

For very high throughput, `AddDbContextPool` reuses context *instances* (not just connections), resetting their state between requests to avoid re-running the setup cost:

```csharp
builder.Services.AddDbContextPool<ShopContext>(o =>
    o.UseSqlServer(connectionString), poolSize: 128);
```

The catch: pooled contexts are reused, so never stash per-request state in a field on your `DbContext` — it will leak into the next request that borrows that instance.

## Migrations in CI/CD

Your schema evolves alongside your code, and those changes must be applied to every environment reliably and repeatably. EF **migrations** capture each schema change as a versioned C# file generated from your model diff:

```bash
dotnet ef migrations add AddCustomerCity
dotnet ef database update            # apply to the target database
```

Each migration records what changed and how to reverse it, and EF tracks which have been applied in a `__EFMigrationsHistory` table so it never runs one twice.

The strategic question is *when* migrations run in your pipeline. Options:

- **`context.Database.Migrate()` on app startup** — simplest, but risky at scale: if several instances start simultaneously they can race, and a failed migration can crash your whole deployment.
- **A dedicated deployment step** — generate an idempotent SQL script (`dotnet ef migrations script --idempotent`) and run it as an explicit, gated CI/CD stage before the new app version goes live. This is the safest, most auditable approach for production.
- **Standalone migration tools — DbUp or Flyway** — apply plain, hand-written, ordered SQL scripts. Teams that want full control over the exact SQL (and want DBAs to review it) often prefer these over EF's generated migrations. **DbUp** is a .NET library; **Flyway** is a language-agnostic tool. Both track applied scripts in a metadata table, just like EF.

> **Best practice:** Make schema changes **backward compatible** so the old and new app versions can run against the new schema at once during a rolling deploy. Add a nullable column now; make it required in a *later* migration after all code writes to it. This "expand then contract" approach lets you deploy schema and code independently with zero downtime.

> **Capstone tie-in:** This chapter is exercised by ShopCore Steps 1 (The Honest Monolith) and 7 (Split Into Microservices) — you'd model products, carts, and orders with EF Core and PostgreSQL, creating the schema from migrations, and later add a transactional outbox table. See Chapter 32.

## Summary

The through-line of this chapter is that the database is not a black box. EF Core is a productivity multiplier, but only if you know what SQL it generates — when it tracks, when it round-trips, when `Include` explodes into a cartesian product. Underneath, indexes, execution plans, transactions, and isolation levels determine whether your system is fast and correct or slow and subtly broken. Around it, caching removes load, NoSQL stores handle shapes relational tables handle poorly, concurrency tokens protect you from lost updates, and disciplined migrations let your schema evolve safely. A senior developer moves fluidly between these layers, always asking the same question: *what is actually happening at the database, and is it the least work required to be correct?*
