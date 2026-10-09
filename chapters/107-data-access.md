# Chapter 7: Data Access

Almost every non-trivial application is, underneath its features, a machine for moving data in and out of a database safely and quickly. Flawless business logic doesn't survive a data layer that fires a thousand queries where one would do, scans an index it was meant to seek, or loses one user's update under another's. This chapter makes you able to write and review data access that sends the SQL you expect: you will read a query's actual plan and say why each index operator seeks or scans, write EF Core queries whose round trips you can count before running them, and protect a row against lost updates.

The order runs from the database up. The schema and SQL come first (normalization, joins, indexes, execution plans, transactions), because EF Core generates SQL and you can only judge its output once you can read it. Then EF Core itself: change tracking, loading related data and the N+1 problem, projections, bulk writes and cascades, and the lifetime of a `DbContext`. Concurrency builds on both: a concurrency token is a column in the `UPDATE`'s `WHERE`. Raw SQL and Dapper cover the queries an ORM shouldn't write, caching the queries you shouldn't run at all, and migrations how the schema changes in a pipeline. Part 2 goes further in [Chapter 18: Data in Depth](#chapter-18-data-in-depth): compiled queries, bulk inserts, PostgreSQL's planner, Redis, NoSQL and scaling.

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

## SQL Fundamentals

Every ORM call ends as SQL, so the SQL comes first: what a join returns, how an index finds rows, how the plan shows which way it found them, and what a transaction guarantees. A senior developer reads the generated SQL and the execution plan, not just the C#.

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

`o.Customer.Name` in an EF Core projection ([Projections](#projections-select-only-what-you-need), below) becomes exactly this INNER JOIN, or a LEFT JOIN if the relationship is optional.

### Indexes: Clustered, Non-Clustered, Covering

An index is a copy of some of a table's columns, kept sorted by its key in a B-tree, so the engine can find rows without reading every page. Without one, a `WHERE` on a million-row table reads all million rows: a **table scan**.

A **clustered index** *is* the table: its leaf pages hold the rows themselves, in key order. A table has only one, usually the primary key (SQL Server makes the primary key clustered unless a clustered index already exists), and a lookup by the clustered key is the cheapest read there is.

A **non-clustered index** is a separate B-tree whose leaf rows hold the indexed columns plus the row's locator: the clustered key, or a row ID on a table without a clustered index. Every column the query needs beyond those costs a **key lookup**: one more descent, through the clustered index, per matching row.

A **covering index** removes that step by *including* the extra columns in its leaf rows:

```sql
-- Query: SELECT Email, Name FROM Customers WHERE City = 'Berlin'
CREATE NONCLUSTERED INDEX IX_Customers_City
    ON Customers (City)
    INCLUDE (Email, Name);   -- now the index alone answers the query
```

The query is *covered*: everything it needs lives in the index, so no key lookups occur. Because the clustered key sits in every non-clustered index, `SELECT Id FROM Customers WHERE Email = @e` is covered by an index on `Email` alone, and a wide clustered key widens every other index.

> **Pay attention.** **Why a composite index serves only its leftmost prefix.** An index on `(City, CreatedAt)` is sorted by `City`, and by `CreatedAt` only within each city, like a phone book sorted by surname and then first name. A seek needs one contiguous range of that order:
>
> - `City = @c` is one contiguous block: a seek.
> - `City = @c AND CreatedAt >= @d` is one range inside that block: a seek on both columns, and the rows come out in date order, so `ORDER BY CreatedAt` needs no sort.
> - `CreatedAt >= @d` alone has no range: its rows sit in every city's block, so the engine scans the whole index and filters.
> - In `(CreatedAt, City)`, the range on the *first* column makes the second one useless for seeking: inside a date range the cities are in no order. SQL Server seeks to the start of the range and checks `City` row by row, as a residual `WHERE:` in the seek operator, reading every row of the period to keep one city's.
>
> So: equality columns first, then the one range or sort column, and check that every column you meant to seek on appears in the plan's seek predicate. On SQL Server 2022 CU27 (200,000 rows, 4 vCPU, warm cache), `CreatedAt >= @p` scanned `(City, CreatedAt)` for 712 logical reads, while `City = @p AND CreatedAt >= …` sought it for 3 ([`seek-vs-scan.txt`](https://github.com/malyna2/dotnet-handbook/blob/main/verify/path/reference-runs/seek-vs-scan.txt)). Rung 6 of [Chapter 19: The Slow-Query Lab](#chapter-19-the-slow-query-lab-reading-execution-plans) measures the wrong order on PostgreSQL.

> **Pitfall:** Indexes speed up reads but slow down writes, because every `INSERT`/`UPDATE`/`DELETE` must maintain them. Do not index every column. Index the columns you filter, join, and sort on, and measure.

### Execution Plans

The execution plan is the engine's strategy for a query: which indexes it reads, in what order it joins, whether it seeks or scans. An **index seek** jumps to one contiguous range of an index; an **index scan** reads all of it (a clustered index scan is a table scan). A scan of a large table under a selective filter usually means a missing index or a predicate the index can't use.

Read the *actual* plan. In SSMS, *Include Actual Execution Plan* (Ctrl+M); in a script, `SET STATISTICS XML ON` or `SET STATISTICS PROFILE ON`. `SET SHOWPLAN_ALL ON` and the estimated plan don't run the query, so they have no actual row counts. Add `SET STATISTICS IO ON` for each table's **logical reads**: the 8 KB pages the query touched in memory. They measure the work, so unlike milliseconds they come out the same on a laptop and on a server. Then look for scans under a selective filter, key lookups multiplied by many rows, estimates far from actual row counts, and warnings.

A predicate can seek only if it is **sargable**: it compares the stored column itself with a value. Wrap the column in a function (`LOWER(Email)`, `YEAR(CreatedAt) = 2026`), compute with it, or start a `LIKE` with `%`, and the engine must evaluate the expression for every row: a scan. Rewrite the predicate around the bare column (`CreatedAt >= '2026-01-01' AND CreatedAt < '2027-01-01'`), or index the expression (a computed column in SQL Server, an expression index in PostgreSQL).

> **Pay attention.** **A .NET `string` against a `varchar` column can turn a seek into a scan.** SqlClient and Dapper send a C# `string` as `nvarchar`, and so does EF Core for a property mapped as Unicode, the default. `nvarchar` has the higher data-type precedence, so SQL Server converts the *column*, `CONVERT_IMPLICIT(nvarchar(100),[Email],0)`, not the parameter. Whether that still seeks depends on the column's collation:
>
> - **Under a SQL collation**, `varchar` is compared with the collation's own sort rules and `nvarchar` with Unicode rules, and the two orders differ: `'a-c' < 'ab'`, but `N'a-c' > N'ab'`. The index's `varchar` order can't answer the converted comparison, so the plan scans the index, and the XML plan carries `<PlanAffectingConvert ConvertIssue="Seek Plan" …>`. `SQL_Latin1_General_CP1_CI_AS` is one: the setup default for US English installations and the default collation of a new Azure SQL database.
> - **Under a Windows collation** (`Latin1_General_CI_AS`) both types follow the same rules, so the optimizer computes a seek range from the parameter (`GetRangeThroughConvert` in the plan) and still seeks. That is why the same code is fast on one database and slow on another.
>
> On SQL Server 2022 CU27 (16.0.4295.3, 200,000 rows, 4 vCPU, warm cache), the `nvarchar` parameter cost an Index Scan and 888 logical reads, the `varchar` one an Index Seek and 3; under a Windows collation the `nvarchar` parameter also read 3 ([`seek-vs-scan.txt`](https://github.com/malyna2/dotnet-handbook/blob/main/verify/path/reference-runs/seek-vs-scan.txt), [`collation.txt`](https://github.com/malyna2/dotnet-handbook/blob/main/verify/path/reference-runs/collation.txt)). The fix is a parameter of the column's type: in EF Core, `.IsUnicode(false).HasMaxLength(100)` on the property (`ToQueryString()` then shows `DECLARE @email varchar(100)` instead of `nvarchar(4000)`); in Dapper, `new DbString { Value = email, IsAnsi = true, Length = 100 }`; in ADO.NET, `SqlDbType.VarChar`. Or make the column `nvarchar`, so both sides agree.

Reading plans is a skill worth acquiring properly rather than by pattern-matching, and it is easiest to learn on PostgreSQL, whose `EXPLAIN` output is plain text and tells you both what it *expected* and what actually *happened*. [Chapter 18: Data in Depth](#chapter-18-data-in-depth) reads it line by line, and [Chapter 19](#chapter-19-the-slow-query-lab-reading-execution-plans) is the hands-on lab.

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

EF Core wraps the statements of one `SaveChanges` call in one transaction, so a unit of work is all-or-nothing by default. Two `SaveChanges` calls are two transactions: if the second fails, the first stays committed.

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

## Entity Framework Core: The Object-Relational Mapper

With the SQL in view, the ORM on top of it. An ORM's job is to bridge two worlds that think differently. Your C# code thinks in objects, references, and collections. A relational database thinks in tables, rows, and foreign keys. EF Core translates between them. The danger is that the translation is *so* smooth you forget it is happening — and every performance problem in EF comes from forgetting that a property access or a `foreach` might quietly become a database round trip.

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

### DbContext Lifetime and Connection Pooling

Opening a physical database connection is expensive — a TCP handshake and authentication. **Connection pooling**, on by default in ADO.NET, keeps a pool of open connections and hands them out on demand, so "opening" a connection usually just borrows an idle one. This is why you should open connections late and close them early: you are borrowing from a shared, finite pool.

`DbContext` is **not thread-safe** and must be **scoped** — one instance per request. `AddDbContext` registers it with scoped lifetime, which is exactly right:

```csharp
builder.Services.AddDbContext<ShopContext>(o =>
    o.UseSqlServer(connectionString));   // scoped: one per HTTP request
```

> **Critical pitfall:** Never inject a `DbContext` into a **singleton** service. A singleton outlives the request scope, so it would share one context across all concurrent requests — a thread-safety disaster and a source of bizarre, intermittent bugs. It is the captive dependency of [Chapter 3](#captive-dependencies-the-classic-di-bug). If a singleton needs data, inject `IDbContextFactory<T>` and create a context per operation.

For very high throughput, `AddDbContextPool` reuses context *instances* (not just connections), resetting their state between requests to avoid re-running the setup cost:

```csharp
builder.Services.AddDbContextPool<ShopContext>(o =>
    o.UseSqlServer(connectionString), poolSize: 128);
```

The catch: pooled contexts are reused, so never stash per-request state in a field on your `DbContext` — it will leak into the next request that borrows that instance. What the connection pool does when requests outnumber its connections is in [Chapter 18](#connection-management-under-load).

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

### Cascade Behaviour: What Happens to the Children

Delete a customer who has orders, and something must happen to those orders. EF Core's answer is governed by `DeleteBehavior`, and the reason it confuses people is that a single enum value configures **two different mechanisms at two different layers**: the foreign key EF writes into your migration, and what EF itself does in memory to the children it happens to be tracking.

```
      ctx.Customers.Remove(customer)
                 │
                 ├─── EF, in memory ────►  what happens to LOADED children
                 │                          (the "Client..." half of the behaviour)
                 │
                 └─── SQL sent to DB ───►  what the FOREIGN KEY does
                                            (ON DELETE CASCADE / SET NULL / NO ACTION)
```

| `DeleteBehavior` | Foreign key in the database | What EF does to *tracked* children |
|---|---|---|
| `Cascade` | `ON DELETE CASCADE` | Deletes them |
| `ClientCascade` | `ON DELETE NO ACTION` | Deletes them |
| `SetNull` | `ON DELETE SET NULL` | Sets the FK to null |
| `ClientSetNull` | `ON DELETE NO ACTION` | Sets the FK to null |
| `Restrict` | `ON DELETE RESTRICT` | Nothing |
| `NoAction` | `ON DELETE NO ACTION` | Nothing |
| `ClientNoAction` | `ON DELETE NO ACTION` | Nothing, and EF skips its own consistency check |

The conventions EF applies when you say nothing follow from whether the relationship is required: a **required** relationship defaults to `Cascade` (an order line cannot exist without its order, so deleting the order deletes the lines), and an **optional** relationship defaults to `ClientSetNull` (the child can live on with a null FK — but only if EF has it loaded).

That last clause is the whole trap. `ClientSetNull` nulls the FK on children **in the change tracker**. Children still sitting in the database untouched are not fixed up, and the FK is `NO ACTION`, so the database rejects the delete:

```csharp
// Children not loaded -> nothing for EF to fix up -> the FK stops the DELETE.
var customer = await ctx.Customers.SingleAsync(c => c.Id == id);
ctx.Customers.Remove(customer);
await ctx.SaveChangesAsync();   // DbUpdateException: FK violation

// Children loaded -> EF nulls their FK in the same SaveChanges.
var customer = await ctx.Customers
    .Include(c => c.Orders)
    .SingleAsync(c => c.Id == id);
ctx.Customers.Remove(customer);
await ctx.SaveChangesAsync();   // works
```

The same delete succeeds or fails depending on whether an `Include` appears three lines earlier. That is a genuinely surprising coupling, and it is the single most common source of "it works in the test and fails in production" around deletes — tests often load the whole aggregate, production code often does not.

`Restrict` and `NoAction` look interchangeable and are not. `Restrict` emits an FK that refuses the delete *immediately*, as soon as the row is touched. `NoAction` defers the check to the end of the statement, which means a statement that deletes parent and children together can succeed under `NoAction` and fail under `RESTRICT`. PostgreSQL implements this distinction faithfully; it also matters for deferrable constraints, where `NO ACTION` can be postponed to commit time and `RESTRICT` cannot.

Two more behaviours worth knowing before they bite:

**Severing a required relationship deletes the child.** Removing an item from `order.Lines` does not just clear the FK — for a required relationship the child is now an orphan, and EF deletes it. That is usually what you want inside an aggregate and alarming when the relationship was required by accident.

**`ExecuteDelete` does not cascade in EF at all.** It emits one `DELETE` and lets the database decide, so it only works when a real `ON DELETE CASCADE` exists in the schema. If your model uses `ClientCascade` — which writes `NO ACTION` — the bulk delete you added for performance will fail with an FK violation on exactly the rows the tracked path used to handle:

```csharp
// Fine with DeleteBehavior.Cascade. FK violation with ClientCascade.
await ctx.Customers.Where(c => c.LastSeen < cutoff).ExecuteDeleteAsync(ct);
```

> **Gotcha:** SQL Server refuses to create **multiple cascade paths** to the same table (error 1785) — if two relationships can both cascade into `OrderLines`, the migration fails. The fix is to set one path to `NoAction` and delete those rows explicitly. PostgreSQL has no such restriction, which is one of the quieter differences that surfaces when a codebase is ported between the two.

Soft delete deserves a warning of its own: a global query filter that hides `IsDeleted` rows does **not** cascade. Soft-deleting a parent leaves its children visible and referencing a parent the rest of the application cannot see. If you soft-delete an aggregate root, soft-delete the aggregate — in one `ExecuteUpdate` per child table, or in a domain method that owns the whole operation.

> **Best practice:** Let cascade delete operate *inside* an aggregate and never across aggregate boundaries (aggregates are defined in [Chapter 21: Architecture](#chapter-21-architecture)). Deleting an order should delete its lines; it should never silently delete the customer's invoices. Configure the boundary explicitly with `Restrict` rather than relying on a convention, so that an accidental cascade becomes a loud error instead of missing data discovered a month later.

## Concurrency: Optimistic vs Pessimistic

Isolation levels decide what concurrent transactions see; they don't stop the most common web bug. When two users edit the same record at once, one can silently overwrite the other's changes — the **lost update** problem — because each user's read and write happen in different requests, so no single transaction spans them. Two philosophies address it.

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

> **Pay attention.** **The window a concurrency token covers.** EF Core puts the token's *original* value, the one this context read, into the `WHERE`. The sample above therefore guards only the milliseconds between its own `Single` and `SaveChanges`. A web edit has a far longer window. The user's form was built from version 1; someone else saved version 2; the save handler loads the product again, gets version 2, copies the form onto it, and its `UPDATE … WHERE RowVersion = <version 2>` matches one row. Nothing throws, and the other user's change is gone. The fix is to make the original value the one the user saw: send the row version with the form (or as an `ETag`) and set it before saving.

```csharp
var product = await ctx.Products.SingleAsync(p => p.Id == id, ct);
ctx.Entry(product).Property(p => p.RowVersion).OriginalValue = form.RowVersion; // the version the user edited
product.Stock = form.Stock;
try
{
    await ctx.SaveChangesAsync(ct);
}
catch (DbUpdateConcurrencyException)
{
    return Results.Conflict();   // or 412 Precondition Failed when the version came as If-Match
}
```

## Stored Procedures, Views, and Raw SQL

A **view** is a saved query you can select from like a table — useful for encapsulating a complex join or presenting a simplified shape. A **stored procedure** is precompiled SQL logic living in the database, callable by name.

Raw SQL — via stored procs, `FromSqlRaw`, or Dapper — is justified when: the query is too complex or too performance-critical for LINQ to express well; you need database-specific features EF does not surface; or you are doing bulk set-based operations (updating a million rows in one statement rather than loading and tracking them).

> **Note:** For the *bulk write* case you usually no longer need raw SQL: `ExecuteUpdate`/`ExecuteDelete` ([Set-Based Updates and Deletes](#set-based-updates-and-deletes-executeupdate-and-executedelete)) issue one set-based statement without loading or tracking. Reserve raw SQL for genuinely complex queries or provider-specific features.

```csharp
// EF calling raw SQL while staying in the entity model
var open = ctx.Orders
    .FromSqlInterpolated($"SELECT * FROM Orders WHERE Status = {status}")
    .ToList();   // interpolated form is parameterized — not string concatenation
```

> **Pitfall:** Never build SQL by concatenating user input — that is the door to SQL injection ([Chapter 12](#a03-injection)). Always use parameters. Note the trade-off with stored procedures: logic in the database is invisible to your application's source control and CI unless you deliberately manage it as versioned migration scripts.

## Dapper: When the ORM Is Too Much

When the SQL is yours anyway, the next question is whether EF Core needs to be in the path at all. EF is productive but adds overhead: expression translation, change tracking, materialization. Sometimes you want raw SQL with a thin, fast mapping to objects. **Dapper** is a micro-ORM — really a set of extension methods on `IDbConnection` — that executes your SQL and maps the result to C# types, nothing more.

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

### The Parts of Dapper Worth Knowing

Dapper's surface is small, but four features cover most of what people otherwise write by hand.

**Multi-mapping** splits one row into several objects, which is how you materialize a join without a flat DTO:

```csharp
var sql = @"SELECT o.id, o.total, c.id, c.name
            FROM orders o JOIN customers c ON c.id = o.customer_id
            WHERE o.status = @status";

var orders = await conn.QueryAsync<Order, Customer, Order>(
    sql,
    (order, customer) => { order.Customer = customer; return order; },
    new { status = "open" },
    splitOn: "id");   // where one object ends and the next begins
```

`splitOn` is the part that trips people up: it names the column at which Dapper starts filling the *next* type, and it defaults to `Id`. Get the column order wrong and you get nulls rather than an error.

**`QueryMultiple`** returns several result sets from one round trip — the cheap way to build a dashboard payload without N queries:

```csharp
using var multi = await conn.QueryMultipleAsync(
    "SELECT * FROM orders WHERE id = @id; SELECT * FROM order_lines WHERE order_id = @id;",
    new { id });

var order = await multi.ReadSingleAsync<Order>();
var lines = (await multi.ReadAsync<OrderLine>()).ToList();
```

**`DynamicParameters`** handles output parameters and stored procedures, and **list parameters just work** — Dapper expands `WHERE id = ANY(@ids)` (or `IN @ids` on SQL Server) from an array without you building placeholders.

**Unbuffered queries** stream instead of materializing. `QueryAsync` buffers the whole result into a list by default; passing `buffered: false` yields rows as they arrive, which is what you want for an export of a million rows and what you must *not* use if you plan to close the connection mid-iteration.

Cancellation needs `CommandDefinition` — the convenience overloads have no `CancellationToken` parameter, which is a common way for a token to get silently dropped on the way to the database:

```csharp
var orders = await conn.QueryAsync<Order>(
    new CommandDefinition(sql, new { status }, cancellationToken: ct));
```

### Mixing Dapper and EF Core in One Transaction

The two coexist better than people expect, because EF will hand you its connection and its ambient transaction. That means a Dapper query can participate in the same unit of work as your tracked changes:

```csharp
await using var tx = await ctx.Database.BeginTransactionAsync(ct);

// Dapper, on EF's connection and inside EF's transaction
var affected = await ctx.Database.GetDbConnection().ExecuteAsync(
    new CommandDefinition(
        "UPDATE inventory SET reserved = reserved + @qty WHERE sku = @sku",
        new { qty, sku },
        transaction: ctx.Database.GetDbTransaction(),
        cancellationToken: ct));

ctx.Orders.Add(order);
await ctx.SaveChangesAsync(ct);
await tx.CommitAsync(ct);
```

> **Gotcha:** Passing the transaction is not optional. A Dapper command issued on EF's connection *without* `transaction:` will fail on SQL Server (the connection has a pending local transaction) or run outside your transaction on PostgreSQL — the second failure mode being the dangerous one, because it commits independently and survives your rollback.

Also remember that Dapper writes are invisible to the change tracker, exactly like `ExecuteUpdate`. Entities already loaded keep their stale values.

| Situation | Reach for |
|---|---|
| Write-side domain logic, aggregates, invariants | EF Core — tracking and unit of work are the point |
| A read model with a hand-tuned join or window function | Dapper |
| Hot path where materialization cost shows up in a profile | Dapper, after measuring |
| Set-based maintenance over many rows | `ExecuteUpdate`/`ExecuteDelete`, or raw SQL |
| Schema evolution | EF migrations, whichever you query with |

> **Best practice:** Do not let "Dapper is faster" become an architecture. The performance gap only matters once materialization is a measurable share of your request time, and by then you will know which three queries need it. Introducing Dapper for a specific read path is a good decision; rewriting a domain model around it usually is not.

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

Redis itself (key design, data types, eviction) and distributed caching across instances are in [Chapter 18: Data in Depth](#chapter-18-data-in-depth).

## Migrations in CI/CD

Your schema evolves alongside your code, and those changes must be applied to every environment reliably and repeatably. EF **migrations** capture each schema change as a versioned C# file generated from your model diff:

```bash
dotnet ef migrations add AddCustomerCity
dotnet ef database update            # apply to the target database
```

Each migration records what changed and how to reverse it, and EF tracks which have been applied in a `__EFMigrationsHistory` table so it never runs one twice.

The strategic question is *when* migrations run in your pipeline. Options:

- **`context.Database.Migrate()` on app startup** — simplest, but risky at scale. Since EF Core 9, `Migrate()` and `MigrateAsync()` take a database-wide lock before applying anything (`sp_getapplock` on SQL Server; on SQLite a row in a `__EFMigrationsLock` table, which a killed process can leave behind), so instances that start together queue instead of corrupting the schema. The lock doesn't remove the other costs: every instance needs DDL rights at runtime, every instance waits while one migrates (long enough, on a big table, for start-up probes to restart them), a failed migration takes the whole deployment down, and since EF Core 9 `Migrate()` throws when the model has changes no migration covers. On EF Core 8 and earlier there is no lock, and simultaneous starts really do race.
- **A dedicated deployment step** — generate an idempotent SQL script (`dotnet ef migrations script --idempotent`) and run it as an explicit, gated CI/CD stage before the new app version goes live. This is the safest, most auditable approach for production.
- **Standalone migration tools — DbUp or Flyway** — apply plain, hand-written, ordered SQL scripts. Teams that want full control over the exact SQL (and want DBAs to review it) often prefer these over EF's generated migrations. **DbUp** is a .NET library; **Flyway** is a language-agnostic tool. Both track applied scripts in a metadata table, just like EF.

> **Best practice:** Make schema changes **backward compatible** so the old and new app versions can run against the new schema at once during a rolling deploy. Add a nullable column now; make it required in a *later* migration after all code writes to it. This "expand then contract" approach lets you deploy schema and code independently with zero downtime; [Chapter 18](#migrations-at-scale-zero-downtime) does it on tables too big to lock.

## Summary

The database is not a black box. Normalization keeps each fact in one place, and constraints keep bad data out whatever the application does. An index is a sorted copy of some columns, so a query seeks only when its predicate pins a leftmost prefix with values the index stores; the actual plan and its logical reads show whether it did. A transaction makes a group of statements all-or-nothing, and its isolation level decides what concurrent transactions see.

EF Core is a productivity multiplier only if you know the SQL it sends: tracking only where you save, related rows in one round trip through `Include` or a projection, one short-lived `DbContext` per request, set-based statements for bulk writes, and a concurrency token whose original value is the one the user saw. Raw SQL and Dapper take the queries LINQ expresses badly, caching removes load you understand, and expand-then-contract migrations let the schema change under a running system. The question to keep asking is *what is actually happening at the database, and is it the least work required to be correct?*

> **Capstone tie-in:** This chapter is exercised by ShopCore Steps 1 (The Honest Monolith) and 7 (Split Into Microservices) — you'd model products, carts, and orders with EF Core and PostgreSQL, creating the schema from migrations, and later add a transactional outbox table. See [Chapter 44](#chapter-44-capstone-one-project-growing-up).

## Prove it

Two programs: the first shows which predicates can seek an index, the second counts the SQL statements EF Core sends for four ways of loading the same rows.

### Seek or scan

Five queries against one 200,000-row table, each printed with its index operator and its logical reads. Before you run it, predict SEEK or SCAN for each of the five.

It needs SQL Server: start it from `verify/path` with `ACCEPT_EULA=Y docker compose up -d mssql` (the image has a EULA; in PowerShell set `$env:ACCEPT_EULA="Y"` first), and stop it with `docker compose down`.

`verify/path/SeekVsScan/Program.cs` · run it from `verify/path` with `dotnet run --project SeekVsScan`:

```csharp
using System.Data;
using Microsoft.Data.SqlClient;

// Prove it: which predicates can SEEK an index and which must SCAN it. Needs a SQL Server:
// `docker compose up -d` in this folder. Prints each query's index operator and logical reads.
using var db = new SqlConnection("Server=localhost,14330;Database=tempdb;User Id=sa;Password=Emulator-Only-Passw0rd!;TrustServerCertificate=true");
db.Open();
new SqlCommand("""
    DROP TABLE IF EXISTS dbo.Customers;
    CREATE TABLE dbo.Customers (Id int IDENTITY PRIMARY KEY, Email varchar(100) NOT NULL, City varchar(50) NOT NULL, CreatedAt datetime2 NOT NULL);
    INSERT dbo.Customers (Email, City, CreatedAt) SELECT CONCAT('user', value, '@example.com'), CONCAT('City', value % 200), DATEADD(minute, -value, '2026-01-01') FROM GENERATE_SERIES(1, 200000);
    CREATE INDEX IX_Email ON dbo.Customers (Email); CREATE INDEX IX_City_CreatedAt ON dbo.Customers (City, CreatedAt);
    SET STATISTICS PROFILE ON; SET STATISTICS IO ON;
    """, db).ExecuteNonQuery();
string reads = "?";
db.InfoMessage += (_, e) => { if (e.Message.Contains("logical reads")) reads = e.Message.Split("logical reads ")[1].Split(',')[0]; };
Plan("Email = @p", SqlDbType.NVarChar, "user42@example.com");     // a C# string is sent as nvarchar
Plan("Email = @p", SqlDbType.VarChar, "user42@example.com");
Plan("LOWER(Email) = @p", SqlDbType.VarChar, "user42@example.com");
Plan("City = @p AND CreatedAt >= '2025-12-31'", SqlDbType.VarChar, "City7");
Plan("CreatedAt >= @p", SqlDbType.DateTime2, new DateTime(2025, 12, 31));
void Plan(string where, SqlDbType type, object value)
{
    using var command = new SqlCommand($"SELECT Id FROM dbo.Customers WHERE {where}", db);
    command.Parameters.Add(new SqlParameter("@p", type) { Value = value });
    var ops = new List<string>();
    using (var reader = command.ExecuteReader())
        do while (reader.Read()) if (reader.FieldCount > 2 && reader.GetString(2).Contains("Index")) ops.Add(reader.GetString(2).Trim()); while (reader.NextResult());
    Console.WriteLine($"WHERE {where} (@p {type}): {reads} logical reads\n    {string.Join(" ", ops).Replace("[tempdb].[dbo].[Customers].", "")}");
}
```

```text
WHERE Email = @p (@p NVarChar): 888 logical reads
    |--Index Scan(OBJECT:([IX_Email]),  WHERE:(CONVERT_IMPLICIT(nvarchar(100),[Email],0)=[@p]))
WHERE Email = @p (@p VarChar): 3 logical reads
    |--Index Seek(OBJECT:([IX_Email]), SEEK:([Email]=[@p]) ORDERED FORWARD)
WHERE LOWER(Email) = @p (@p VarChar): 888 logical reads
    |--Index Scan(OBJECT:([IX_Email]),  WHERE:(lower([Email])=[@p]))
WHERE City = @p AND CreatedAt >= '2025-12-31' (@p VarChar): 3 logical reads
    |--Index Seek(OBJECT:([IX_City_CreatedAt]), SEEK:([City]=[@p] AND [CreatedAt] >= '2025-12-31 00:00:00.0000000') ORDERED FORWARD)
WHERE CreatedAt >= @p (@p DateTime2): 712 logical reads
    |--Index Scan(OBJECT:([IX_City_CreatedAt]),  WHERE:([CreatedAt]>=[@p]))
```

The run: SQL Server 2022 CU27 (16.0.4295.3) with the `SQL_Latin1_General_CP1_CI_AS` collation, in Docker on a 4 vCPU Xeon with 16 GB RAM. What to notice — three scans, each for a different reason:

- **The conversion lands on the column.** `nvarchar` outranks `varchar` in data-type precedence, so SQL Server converts the *column*: `CONVERT_IMPLICIT(…,[Email],0)`. Under a SQL collation `varchar` and `nvarchar` sort differently, so the index's order can't answer the question: 888 reads instead of 3. Under a Windows collation the same parameter still seeks, through a computed range: the companion run [`collation.txt`](https://github.com/malyna2/dotnet-handbook/blob/main/verify/path/reference-runs/collation.txt) shows 3 reads and `GetRangeThroughConvert`. That is why this bug appears on one database and not on another.
- **A function on the column.** `lower([Email])` is a value the index doesn't store: 888 reads.
- **The index is used, but scanned.** `CreatedAt` alone isn't a leftmost prefix of `(City, CreatedAt)`, so SQL Server reads the whole index: 712 reads. With `City` pinned first, both columns appear in `SEEK:`: 3 reads.
- **Every query is covered.** Each selects only `Id`, the clustered key, which every non-clustered index row carries. Select `City` from `IX_Email` and each matching row would add a key lookup.

**The fix in .NET:** send the parameter as `varchar`. In EF Core, map the property `.IsUnicode(false).HasMaxLength(100)`: `ToQueryString()` then shows `DECLARE @email varchar(100)`, where the default mapping shows `nvarchar(4000)`. In Dapper, `new DbString { Value = email, IsAnsi = true, Length = 100 }`; in raw ADO.NET, `SqlDbType.VarChar`.

**Then the lab** ([kit](https://github.com/malyna2/dotnet-handbook/tree/main/labs/37-execution-plans), PostgreSQL in Docker, about 3 hours): read the *Goal* and *Setup* sections of [Chapter 19](#chapter-19-the-slow-query-lab-reading-execution-plans), do Level 1 (rungs 1–4: N+1, a cartesian explosion from two `Include`s, a function on a column, a conversion on a column) and rung 6 (column order). Open its *Hints and answers* for a rung only after your fix passes. Your results belong in your own public portfolio repo, not in this one.

Also do *What would you do* (the 40-second report) in the Exercises below.

### Counting statements

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

Then do *Find the bug* in the Exercises below: count the queries the endpoint sends before you open the answer.

## Three questions

### Seeks, scans and covering

**1.** An index on `(City, CreatedAt)`. Why does `WHERE CreatedAt >= @d` scan while `WHERE City = @c AND CreatedAt >= @d` seeks? And why would `(CreatedAt, City)` be worse for the second query?

<details>
<summary>Answer</summary>

- **Why one seeks and the other scans.** The index is sorted by `City`, then by `CreatedAt` within each city. One city is a contiguous block with its dates in order, so the engine seeks to `(City, start date)` and reads forward. A date range alone is spread across every city's block: there is no single range to seek, so it scans (712 reads against 3 in the experiment).
- **Why `(CreatedAt, City)` is worse.** The date range on the leading column *is* contiguous, but it holds every city's rows for that period, and inside that range the cities are not in order. `City` can only be checked row by row, so the engine reads the whole period to keep one city's rows. Rung 6 of [Chapter 19](#chapter-19-the-slow-query-lab-reading-execution-plans) measures exactly this.
- **The rule:** equality columns first, then the range or sort column. PostgreSQL 18's skip scan softens it only when the leading column has few distinct values.
</details>

**2.** The same query seeks when you run it in SSMS with a literal, and scans when the application sends it. The column is `varchar`. Why, and what are the fixes in EF Core and Dapper?

<details>
<summary>Answer</summary>

- **The application sends `nvarchar`.** SqlClient and Dapper send a C# `string` as `nvarchar` unless told otherwise; so does EF Core for a property not mapped as non-Unicode.
- **The conversion lands on the column.** `nvarchar` has the higher data-type precedence, so SQL Server converts the `varchar` column, not the parameter.
- **The collation decides whether that scans.** Under a SQL collation, such as `SQL_Latin1_General_CP1_CI_AS`, `varchar` and `nvarchar` sort differently, so the index's order can't answer the converted comparison: a scan, 888 reads against 3. Under a Windows collation the optimizer can still compute a seek range, which is why it "works on the other database". The SSMS literal `'user42@example.com'` is `varchar`, so it seeks everywhere.
- **Fixes:** send `varchar`. EF Core: `.IsUnicode(false).HasMaxLength(100)` on the property. Dapper: `DbString { IsAnsi = true, Length = 100 }`. ADO.NET: `SqlDbType.VarChar`. Or make the column `nvarchar`.
</details>

**3.** An index on `Email`, and `Id` is the clustered primary key. `SELECT Id FROM Customers WHERE Email = @e` is one Index Seek. Add `City` to the `SELECT` and the plan grows a Key Lookup. Why, what happens when 10,000 rows match, and what is the fix?

<details>
<summary>Answer</summary>

- **`Id` comes free.** A non-clustered index row holds its key and the clustered key, the row's locator. `Id` is in the index, so the seek answers the query alone.
- **`City` needs the row.** For each matching index row, SQL Server follows the clustered key into the clustered index, a *key lookup*: one more descent of the clustered index, a few pages, per matching row. For 10,000 rows that is 10,000 descents, and past some number of rows the optimizer scans the whole table instead.
- **Fix:** `CREATE INDEX … ON Customers (Email) INCLUDE (City)`. The index now covers the query, and the lookups disappear. `INCLUDE` columns live only in the index's leaf rows: they cost space and write time, not key order.
</details>

### What EF Core sends

**4.** An endpoint sends 1 + N queries, and every one of their plans is an index seek under a millisecond. Why is the endpoint slow, why can't any single plan show the problem, and how do you find it?

<details>
<summary>Answer</summary>

- **The cost is the count.** Each statement pays a network round trip, a connection borrowed from the pool and a round of materialisation. N statements pay it N times, and N grows with the data, not with the code.
- **No plan contains it.** Each statement really is cheap, so each plan looks perfect. The problem is the number of statements one request sends, and no plan holds that number.
- **Count statements per request.** In development, EF Core's command log (`LogTo`, or the `Microsoft.EntityFrameworkCore.Database.Command` category at `Information`). In production, the database spans of one request's trace ([Chapter 9](#observability-wiring)), or call counts on the server: `pg_stat_statements` ([Chapter 19, Level 3](#level-3-beyond-the-harness)) or Query Store. The signature is a statement whose call count is a multiple of another's.
- **Fix:** `Include`, a projection or a split query, and lazy loading off so the pattern can't come back unnoticed.
</details>

**5.** A query starts with `.Include(o => o.Customer)`, has `.AsNoTracking()` in the middle and ends with `.Select(o => new OrderRow(o.Id, o.Customer.Name))`. Which of the three calls changes the SQL or the work, and why?

<details>
<summary>Answer</summary>

- **The `Select` decides everything.** The navigation `o.Customer.Name` inside it generates the join, and only the two selected columns are read.
- **`Include` does nothing.** It is an instruction for materialising `Order` entities, and this query materialises `OrderRow`s. EF Core drops it without a warning; the SQL is identical without it.
- **`AsNoTracking` does nothing either.** Tracking applies to entity instances, and a result without entities is never tracked.

Delete both. Both start to matter only if the result contains the entity again, for example `.Select(o => new { Order = o, o.Customer.Name })`.
</details>

**6.** `Product` has a `[Timestamp]` row version. A user opens the edit form, someone else saves the same product, and five minutes later the first user saves and silently overwrites that change. No `DbUpdateConcurrencyException`. Why, and what is the fix?

<details>
<summary>Answer</summary>

- **The token guards the window between one context's read and its write.** EF Core puts the token's *original* value, the one this context read, into the `UPDATE`'s `WHERE`.
- **The save handler read the current version.** The user's form came from version 1. The other save made it version 2. The handler loads the product (version 2), copies the form onto it and sends `UPDATE … WHERE RowVersion = <version 2>`: one row matches, nothing throws, and the other user's change is gone.
- **Fix: make the original value the one the user saw.** Send the row version with the form (or as an `ETag`), and before `SaveChanges` set `db.Entry(product).Property(p => p.RowVersion).OriginalValue = form.RowVersion`. Now zero rows match, `SaveChanges` throws, and the handler returns `409 Conflict` (`412 Precondition Failed` for an `If-Match` header) so the user reloads.
</details>

## Check at work

### Your slowest query

**Inspect.** Take your service's most expensive query from Query Store (or your APM's slowest dependency) and open its *actual* plan. Name every index operator a seek or a scan. For each scan, decide which of the experiment's three causes it is, or whether the scan is simply right because the query needs most of the table. For every `varchar` column your code filters on, check the type the parameter arrives with (`ToQueryString()` shows EF Core's `DECLARE`).

**Measure.** The query's logical reads before and after your fix (`SET STATISTICS IO ON`), and its executions per hour from Query Store. Reads saved times executions is the load you removed.

### Your busiest endpoint

**Inspect.** Turn on EF Core's command log in development, call your busiest endpoint once with realistic data, and count the `Executed DbCommand` lines. Good: a small number that stays the same when the data grows. Bad: a number that grows with the rows on the page. Then search the code for `.Include(` in queries that end in `.Select(`, for `UseLazyLoadingProxies`, and for a `DbContext` held by anything registered as a singleton, hosted services included.

**Measure.** In your APM or OpenTelemetry traces, the number of database spans per request for that endpoint over a day. Look at the maximum, not the average: N+1 shows on the requests with the most rows.

## Exercises

### Find the bug

This endpoint is correct, passes its integration test against a seeded database of 20 orders, and brings production to its knees.

```csharp
app.MapGet("/api/customers/{id:int}/summary", async (int id, AppDbContext db) =>
{
    var customer = await db.Customers.FindAsync(id);
    if (customer is null) return Results.NotFound();

    var orders = await db.Orders
        .Where(o => o.CustomerId == id)
        .ToListAsync();

    var lines = new List<OrderLineDto>();
    foreach (var order in orders)
    {
        foreach (var line in order.Lines)          // navigation property
            lines.Add(new OrderLineDto(line.Sku, line.Quantity, line.Product.Name));
    }

    return Results.Ok(new SummaryDto(customer.Name, orders.Count, lines));
});
```

How many queries does this execute for a customer with 200 orders averaging 5 lines each?

<details>
<summary>Answer</summary>

Roughly **1 + 1 + 200 + 1000 = 1,202 queries**, assuming lazy loading is enabled.

- 1 for the customer, 1 for the orders.
- `order.Lines` was never loaded, so each iteration triggers a separate query — 200 of them.
- `line.Product.Name` triggers another per line — about 1,000 more.

This is the **N+1 problem**, twice, nested. It passes the test because 20 orders means ~120 queries against a local database, which is fast enough that nobody notices.

Two things make it worse than it looks. First, if lazy loading is *not* enabled, `order.Lines` is an empty collection and the endpoint silently returns wrong data instead of being slow — a worse failure. Second, each of those queries takes a connection from the pool, so this endpoint under concurrency exhausts the pool and degrades endpoints that have nothing to do with it.

The fix is to project the lines in one query and count the orders in another:

```csharp
var lines = await db.Orders
    .Where(o => o.CustomerId == id)
    .SelectMany(o => o.Lines)
    .Select(l => new OrderLineDto(l.Sku, l.Quantity, l.Product.Name))
    .ToListAsync();
var orderCount = await db.Orders.CountAsync(o => o.CustomerId == id);
```

Three statements in all, whatever the customer's size. The projection reads only the columns it names, so EF never materialises an `Order`, `OrderLine` or `Product`, and nothing is tracked: a result without entities is never tracked, so `AsNoTracking()` would change nothing here. A product shared by several lines is lazy-loaded only once per context, so 1,000 is the upper bound for the product queries.
</details>

### What would you do

A report query takes 40 seconds. A colleague proposes adding a Redis cache in front of it with a 5-minute TTL. The execution plan shows a clustered index scan over 12 million rows and a hash match spilling to tempdb. What do you say?

<details>
<summary>How a senior engineer reasons about it</summary>

The cache is not wrong, but it is being proposed as a substitute for understanding, and that has costs the proposer has not priced:

- **It hides the problem without bounding it.** The scan still runs — once every five minutes, plus on every cold start, plus on every cache eviction. Under enough concurrency, several requests miss simultaneously and you get a stampede: five copies of a 40-second query running at once, which is worse than the uncached case.
- **It adds a correctness question** nobody asked yet: is five-minute-stale data acceptable for this report? Perhaps it is. That should be a stated decision, not a side effect of a performance fix.
- **The plan is telling you exactly what is wrong.** A scan plus a spill means either a missing index, or a predicate that isn't sargable (a function applied to the column, an implicit type conversion, a leading wildcard), or a query returning far more rows than it needs.

So the answer is "probably both, in this order": read the plan, fix the scan — usually a covering index or a rewritten predicate — and *then* decide whether caching is still needed. A 40-second query that becomes 200ms may not need a cache at all, and you have removed a component, a failure mode, and a staleness question rather than adding them.

The generalizable point: caching is a legitimate tool for load you understand and have chosen not to pay repeatedly. It is a poor tool for making an unexamined query disappear from a dashboard.
</details>

### Go check

In your own service:

- Turn on EF Core's sensitive-data query logging in development and load one busy page. Count the queries. Almost everyone is surprised at least once.
- Find a read-only query path that does not use `AsNoTracking()`. Measure the difference on a realistic result set.
- Take your slowest known query and look at its actual execution plan — not the estimated one. Is there a scan where you expected a seek? Which index did the optimizer pick, and why not the one you assumed?
- Check the isolation level your transactions actually run at. Most people say "read committed" and are right; some are running under snapshot or serializable without knowing, and it explains their deadlocks.

## Interview Questions

**What is change tracking?**
EF Core's `DbContext` snapshots loaded entities and tracks their state (Added/Modified/Deleted/Unchanged). On `SaveChanges` it generates the SQL for exactly the changes. It's convenient but costs memory and CPU proportional to tracked entities — a reason to disable it for read-only queries.

**When and why `AsNoTracking`?**
For read-only queries you won't update. It skips building the change-tracking snapshot, so it's faster and lighter. Use it for list/reporting/GET endpoints; keep tracking for the read-modify-save flow.

**What is the N+1 problem in EF?**
One query loads N parents, then accessing a navigation property fires one query per parent — N+1 round-trips. Caused by lazy loading in a loop or projecting navigations without including them. Fix with eager loading (`Include`), a projection (`Select`) that joins, or a split query — turning N+1 into 1 or 2 queries.

**Red flag:** "Make the loop parallel/async so the queries run faster" — parallel N+1 is still N+1 round-trips; the fix is fewer queries, not faster loops.

**Lazy vs eager vs explicit loading?**
**Eager** (`Include`) loads related data up front in the query. **Lazy** loads it on first access to the navigation (convenient, but the N+1 footgun). **Explicit** (`Load()`) loads related data on demand by an explicit call. Prefer eager or projection for predictable query counts; be wary of lazy loading in hot paths.

**Transactions and isolation levels — name them.**
From weakest to strongest: **Read Uncommitted** (dirty reads), **Read Committed** (default in many DBs; no dirty reads), **Repeatable Read** (no non-repeatable reads), **Serializable** (full isolation, no phantoms). Higher isolation means more locking/contention. Choose the weakest level that preserves correctness for the operation; snapshot isolation (MVCC) reduces reader-writer blocking.

**Clustered vs non-clustered index?**
A **clustered** index defines the physical row order of the table — one per table, usually the primary key. A **non-clustered** index is a separate structure with pointers back to the rows — many allowed. Clustered is great for range scans on the key; non-clustered covers other lookup columns. A "covering" index includes all columns a query needs so it never touches the table.

**When does an index hurt?**
Every index must be maintained on insert/update/delete and consumes storage, so over-indexing slows writes. Very low-cardinality columns (a boolean) rarely benefit. Index the columns you filter, join, and sort on — measure with query plans rather than indexing everything.

**Red flag:** "Indexes only help, so index every column" — every index is maintained on every write, taxing inserts and updates.

**What causes a database deadlock and how do you avoid it?**
Two transactions each hold a lock the other needs, in opposing order. Avoid by acquiring locks in a consistent order everywhere, keeping transactions short, using the lowest workable isolation level, and adding retry logic for the deadlock-victim error. Deadlocks are a design/ordering issue, not just bad luck.

**Optimistic vs pessimistic concurrency?**
**Optimistic**: assume conflicts are rare, don't lock; detect a conflict at save time via a version/rowversion column and retry if someone else changed the row. **Pessimistic**: lock the row on read so no one else can touch it until you're done. Optimistic scales better and is the default for web apps; pessimistic suits short, high-contention critical sections.

**When would you drop EF Core for Dapper?**
When you need tight control over SQL and maximum read performance — hot query paths, complex hand-tuned queries, bulk reads — and don't need change tracking or migrations. Many teams use both: EF for the write model and CRUD, Dapper for performance-critical reads. It's a per-query decision, not religion.

**What does ACID stand for?**
**Atomicity** (all-or-nothing), **Consistency** (valid state to valid state, constraints hold), **Isolation** (concurrent transactions don't corrupt each other), **Durability** (committed data survives crashes). Relational DBs give you these; distributed systems often trade some away.

**What is normalization and when do you denormalize?**
Normalization organizes data to eliminate redundancy (each fact stored once) — reduces update anomalies. You denormalize deliberately for read performance: duplicate or pre-join data to avoid expensive joins on hot read paths, accepting the cost of keeping copies in sync. Normalize by default, denormalize with evidence.
