# Chapter 18: Data in Depth

[Chapter 7](#chapter-7-data-access) made you able to use EF Core and SQL correctly inside one service: tracking, loading, projections, indexes, isolation levels, caching and migrations. This chapter is what comes after: making one database fast when the obvious levers are already pulled, and then deciding what to do when one database is no longer enough. It makes you able to read a PostgreSQL plan and fix the mechanism behind it, design a Redis cache that fails as a slowdown rather than an outage, and choose between replicas, sharding and tenant isolation with the cost of each spelled out.

The sections run from one query to the whole fleet. First the depth of the single node: EF Core's remaining performance tools (pagination, compiled queries, bulk loads), PostgreSQL's storage model, indexes and plans, and Redis in practice. Then scale, ordered from least to most invasive: scaling up, read replicas, connection pooling under load, partitioning and sharding, zero-downtime migrations, change data capture, polyglot persistence, and finally multi-tenancy, where both pressures meet. Both halves come down to the same question: *where does the data live, and who is allowed to touch it?* [Chapter 19](#chapter-19-the-slow-query-lab-reading-execution-plans) is the hands-on lab for the plan-reading half.

## Entity Framework Core: The Object-Relational Mapper — in depth

Entity Framework Core is where business services most often bleed performance, because one innocent line of C# can generate a catastrophic SQL pattern. [Chapter 7](#chapter-7-data-access) covers the levers that fix most of it: `AsNoTracking`, projection to DTOs, killing the N+1 with `Include` or a projection, split queries, and `ExecuteUpdate`/`ExecuteDelete`. Three more matter once those are in place.

> **Best practice.** Log and inspect the actual SQL EF generates (`LogTo`, or a profiler) before changing anything. Most EF performance problems are invisible in C# and obvious the moment you see the SQL.

### Pagination: Never Fetch an Unbounded Result Set

`ToListAsync()` on a table that grows to millions of rows will eventually take down your service. Always bound queries:

```csharp
var page = await db.Products
    .OrderBy(p => p.Id)
    .Skip((pageNumber - 1) * pageSize)
    .Take(pageSize)
    .ToListAsync();
```

`Skip` becomes `OFFSET`, and the database still reads and discards every skipped row, so page 10,000 costs as much as reading 10,000 pages. For large offsets use **keyset pagination**: remember the last key the client saw and ask for `WHERE Id > @lastId ORDER BY Id LIMIT @pageSize`, which an index answers directly whatever the page number.

### Compiled Queries

Every time EF runs a LINQ query it must translate the expression tree into SQL (parsing, analysing, caching by shape). For a hot query executed millions of times, that translation overhead adds up. `EF.CompileQuery` (or `CompileAsyncQuery`) does the translation once and hands you a reusable delegate:

```csharp
private static readonly Func<AppDb, int, Task<User?>> _getUser =
    EF.CompileAsyncQuery((AppDb db, int id) =>
        db.Users.FirstOrDefault(u => u.Id == id));

public Task<User?> GetUserAsync(int id) => _getUser(_db, id);
```

This is a micro-optimization: reach for it only when profiling shows query compilation is a bottleneck, not by default.

### Bulk Inserts and the Limits of SaveChanges

`ExecuteUpdate` and `ExecuteDelete` cover the write side of "change many rows I do not need to load". Inserts have no such escape hatch, and this is where teams most often discover that `SaveChanges` has a ceiling.

`SaveChanges` is not as naive as it looks. It sorts pending changes into dependency order, then **batches** statements into as few round trips as it can — the SQL Server provider packs up to 42 statements per batch by default (`MaxBatchSize` is configurable), and Npgsql batches similarly. So 1,000 inserts are not 1,000 round trips. But they are still 1,000 parameterized `INSERT` statements with 1,000 sets of parameters, preceded by change-tracker work for every entity.

That change-tracker work is the part that surprises people:

```csharp
// Quadratic: DetectChanges runs on every Add, scanning everything added so far.
foreach (var p in products)
    ctx.Products.Add(p);           // 50k entities -> DetectChanges 50k times

// Linear: one DetectChanges pass at the end.
ctx.Products.AddRange(products);
```

`Add` triggers `DetectChanges`, which walks every tracked entity looking for modifications. Adding *n* entities one at a time is therefore O(n²) in tracker work — the classic "why does importing 50,000 rows take four minutes when the database is idle?". `AddRange` collapses that to a single pass, and for import paths you can go further and switch the detection off entirely (`ctx.ChangeTracker.AutoDetectChangesEnabled = false`) as long as you turn it back on afterwards.

Even fixed, `SaveChanges` tops out well below what the database can ingest, because the protocol is the bottleneck: every row travels as a parameterized statement. Both major engines offer a bulk-load path that bypasses the statement protocol entirely — SQL Server has `SqlBulkCopy`, PostgreSQL has the binary `COPY` protocol, exposed by Npgsql:

```csharp
await using var conn = new NpgsqlConnection(connectionString);
await conn.OpenAsync(ct);

await using var writer = await conn.BeginBinaryImportAsync(
    "COPY products (sku, name, price) FROM STDIN (FORMAT BINARY)", ct);

foreach (var p in products)
{
    await writer.StartRowAsync(ct);
    await writer.WriteAsync(p.Sku, NpgsqlDbType.Text, ct);
    await writer.WriteAsync(p.Name, NpgsqlDbType.Text, ct);
    await writer.WriteAsync(p.Price, NpgsqlDbType.Numeric, ct);
}

await writer.CompleteAsync(ct);   // nothing is written unless you call this
```

The difference is not marginal. Loading a few hundred thousand rows takes minutes through `SaveChanges` and seconds through `COPY`, because the rows stream to the server in one continuous operation with no per-row statement parsing, no parameter binding, and no tracker.

> **Gotcha:** `CompleteAsync()` is what commits a binary import. Disposing the writer without calling it rolls the whole import back — silently, as far as your code can tell, because no exception is thrown. Forgetting it produces the memorable bug where the import "succeeds" every night and the table stays empty.

Choosing between them is mostly a question of scale and of what else has to happen:

| Rows per operation | Reach for | Why |
|---|---|---|
| Up to ~100 | `SaveChanges` | Batched already; tracker cost is noise. Domain events and interceptors work normally. |
| ~100 – 10,000 | `AddRange` + `SaveChanges`, detection off | Still one transaction, still your entity model, no extra dependency. |
| 10,000+ | `SqlBulkCopy` / `COPY`, or a bulk-extension library | The statement protocol is the bottleneck; only a bulk load path removes it. |
| "Change rows I do not need to read" | `ExecuteUpdate` / `ExecuteDelete` | One set-based statement, nothing materialized. |

Libraries such as **EFCore.BulkExtensions** and **linq2db.EntityFrameworkCore** wrap the provider-specific bulk APIs behind an EF-shaped surface (`BulkInsertAsync`, `BulkMergeAsync`) and add upsert support, which the raw APIs lack. They are worth the dependency when you need `MERGE`-style behaviour; a straight append does not need them.

> **Pitfall:** Bulk paths bypass everything EF layers on top of the database — no change tracker, no interceptors, no `SaveChanges` events, no domain-event dispatch, and no validation. That is precisely why they are fast, and precisely why they belong in import and maintenance jobs rather than in the middle of your domain logic.

## PostgreSQL in Practice: Indexes and Query Plans

[Chapter 7](#chapter-7-data-access)'s SQL is engine-agnostic. PostgreSQL is now the default relational database for new .NET services, and it differs from SQL Server in ways that change how you index and how you diagnose a slow query. The good news is that Postgres will tell you exactly what it did, in plain text, if you know how to ask.

### The Storage Model That Explains Everything Else

Three facts about how Postgres stores rows explain most of its behaviour.

**There is no clustered index.** Table rows live in an unordered **heap**, and every index — including the primary key — is a separate structure whose entries point at a physical row location (the `ctid`). Looking up by primary key is therefore always two steps: search the index, then fetch the row from the heap. SQL Server's "the clustered index *is* the table" intuition does not transfer, and neither does the habit of choosing a clustered key.

**Every update writes a new row version.** Postgres implements MVCC by never modifying a row in place: an `UPDATE` writes a whole new version and marks the old one dead; a `DELETE` only marks it dead. Dead versions are reclaimed later by **autovacuum**. This is why an update-heavy table grows even when its row count is constant (**bloat**), and why a long-running transaction is expensive for the *whole* database — nothing newer than the oldest open transaction can be cleaned up while it sits there.

There is an important optimisation hiding in that: if an update touches **no indexed column** and the page has free space, Postgres performs a **HOT update** — the new version goes on the same page and no index has to be touched at all. A table with eight indexes rarely gets HOT updates; the same table with three does. Indexes cost you on every write, not just in the obvious way.

**An index alone cannot prove a row is visible to you.** Visibility lives in the heap, so a lookup that finds its answer entirely in the index still has to check whether the row is visible to your snapshot. Postgres keeps a **visibility map** marking pages where every row is visible to everyone, and skips the heap for those. That is what makes an **index-only scan** possible — and why the plan reports `Heap Fetches: N`, and why a table that was just bulk-updated loses its index-only scans until autovacuum refreshes the map.

### Choosing an Index Type

B-tree is the default and the right answer most of the time, but Postgres has a genuinely useful index toolbox:

| Type | Good for | Notes |
|---|---|---|
| **B-tree** | Equality, ranges, sorting, `LIKE 'prefix%'` | The default. Handles `ORDER BY` and `MIN`/`MAX` too. |
| **GIN** | `jsonb` containment, arrays, full-text search | Many keys per row. Slow to update; consider `fastupdate`. |
| **GiST** | Geometry, ranges, nearest-neighbour, exclusion constraints | The extensible one; PostGIS is built on it. |
| **BRIN** | Enormous tables whose physical order tracks a column (time-series) | Tiny — kilobytes for a table of gigabytes — but only works when data is naturally ordered. |
| **Hash** | Equality on large values | Rarely worth it; B-tree does equality nearly as well and more besides. |

The B-tree rules that matter in practice:

- **Multi-column indexes obey the leftmost-prefix rule.** An index on `(customer_id, created_at)` serves `WHERE customer_id = ?`, and `WHERE customer_id = ? ORDER BY created_at`, but not `WHERE created_at > ?` alone. Put equality columns first, then the range or sort column; [Indexes: Clustered, Non-Clustered, Covering](#indexes-clustered-non-clustered-covering) explains why.
- **An index can supply the sort order.** If the index order matches the `ORDER BY`, the plan has no `Sort` node at all — the rows come out sorted. The direction and `NULLS FIRST/LAST` must match too.
- **`INCLUDE` makes an index covering**, so the query can be answered without touching the heap.
- **Partial indexes** index only the rows you actually query, which makes them dramatically smaller and cheaper to maintain.
- **Expression indexes** are how you index a computed value — necessary because *any* function applied to a column defeats a plain index on it.

```sql
-- Only open orders are ever listed, and always newest first.
CREATE INDEX idx_orders_open_recent
    ON orders (created_at DESC)
    WHERE status = 'open';

-- "Email must be unique among users who are not soft-deleted."
CREATE UNIQUE INDEX uq_users_email_active
    ON users (lower(email))
    WHERE deleted_at IS NULL;
```

That second one is worth remembering: a **partial unique index** is how you express a conditional uniqueness rule that a plain constraint cannot, and it is the database-level guarantee that makes an application-level uniqueness check safe (see the validation discussion in [Chapter 5](#chapter-5-http-and-web-apis): a check-then-insert without this index is a race, not a rule).

EF Core can express all of it, so these do not have to live in hand-written migration SQL:

```csharp
modelBuilder.Entity<Order>()
    .HasIndex(o => o.CreatedAt)
    .IsDescending()
    .HasFilter("status = 'open'")
    .IncludeProperties(o => o.Total);
```

### Reading EXPLAIN

`EXPLAIN` shows the plan the planner *chose*, with its estimates. `EXPLAIN (ANALYZE, BUFFERS)` actually runs the query and adds what really happened plus how much I/O it took. Always use the second form when diagnosing — estimates alone hide the interesting failure.

> **Gotcha:** `EXPLAIN ANALYZE` executes the statement, including `UPDATE` and `DELETE`. Wrap it: `BEGIN; EXPLAIN (ANALYZE) ...; ROLLBACK;`.

Read the plan tree **inside-out**: the most indented node runs first and feeds its parent. Three things carry nearly all the diagnostic value:

1. **`rows=` estimated versus `rows=` actual.** A large divergence means the planner is working from bad information, and every decision above that node is suspect.
2. **`actual time=` and `loops=`.** Times are *per loop*. A node showing `actual time=0.3..0.4 rows=1 loops=50000` did not take 0.4 ms; it took twenty seconds.
3. **`Buffers: shared hit=` versus `read=`.** Hits came from cache, reads went to the operating system. A query with a good plan but tens of thousands of reads is doing too much I/O.

Here is a real shape you will meet often — a listing endpoint that got slow as the table grew:

```sql
EXPLAIN (ANALYZE, BUFFERS)
SELECT id, created_at, total
FROM orders
WHERE status = 'open'
ORDER BY created_at DESC
LIMIT 20;
```

```
Limit  (cost=48231.19..48231.24 rows=20 width=24)
       (actual time=812.443..812.449 rows=20 loops=1)
  Buffers: shared hit=1204 read=23891
  ->  Sort  (cost=48231.19..48472.65 rows=96584 width=24)
            (actual time=812.441..812.444 rows=20 loops=1)
        Sort Key: orders.created_at DESC
        Sort Method: top-N heapsort  Memory: 27kB
        ->  Seq Scan on orders  (cost=0.00..45662.00 rows=96584 width=24)
                                (actual time=0.019..798.221 rows=96584 loops=1)
              Filter: (status = 'open')
              Rows Removed by Filter: 1903416
Planning Time: 0.184 ms
Execution Time: 812.503 ms
```

Everything you need is in there. `Rows Removed by Filter: 1903416` says the database read two million rows to keep ninety-six thousand. The `Sort` says it then ordered all of them to return twenty. `read=23891` says most of that came off disk. The estimate matches the actual, so the planner was not wrong — it simply had nothing better available. The partial index above changes the answer:

```
Limit  (cost=0.42..2.31 rows=20 width=24)
       (actual time=0.028..0.061 rows=20 loops=1)
  Buffers: shared hit=24
  ->  Index Scan using idx_orders_open_recent on orders
        (cost=0.42..9124.55 rows=96584 width=24)
        (actual time=0.026..0.057 rows=20 loops=1)
Execution Time: 0.089 ms
```

No `Filter`, because the index only contains open orders. No `Sort`, because the index is already in `created_at DESC` order. Twenty-four buffer hits instead of twenty-five thousand reads, and 0.09 ms instead of 812 ms. The `LIMIT` can stop as soon as it has twenty rows, which is why the actual cost is a fraction of the node's estimated total.

The vocabulary you need to read any plan:

| Node | What it means |
|---|---|
| `Seq Scan` | Read the whole table. Correct for small tables or unselective filters; a problem under a selective one. |
| `Index Scan` | Walk the index, fetch each matching row from the heap. |
| `Index Only Scan` | Answered from the index alone. Check `Heap Fetches` — a high number means the visibility map is stale. |
| `Bitmap Index Scan` + `Bitmap Heap Scan` | Collect matching row locations, sort them, then read the heap in physical order. Chosen when there are too many matches for random access to pay. `Recheck Cond` with `lossy=true` means it fell back to page granularity. |
| `Nested Loop` | For each outer row, look up the inner side. Great when the outer side is tiny, quadratic when it is not. |
| `Hash Join` | Build a hash of one side, probe with the other. The usual choice for large unsorted joins. |
| `Merge Join` | Both sides sorted, walked in step. Cheap when indexes already provide the order. |
| `Sort` | Watch `Sort Method`: `quicksort`/`top-N heapsort` with `Memory:` is fine; `external merge` with `Disk:` means it spilled and `work_mem` is too low. |
| `Memoize` | Caches inner-side lookups in a nested loop (PostgreSQL 14+). Often what rescues a repeated-lookup plan. |
| `Gather` / `Workers Launched` | Parallel execution. Useful for big scans, pure overhead for small ones. |

### When the Plan Is Wrong

If estimated and actual rows diverge badly, the planner was misinformed, and the fix is upstream of the query:

- **Stale statistics.** `ANALYZE orders;` refreshes them. Autovacuum does this on a threshold, so a table that just received a bulk load may be planned from statistics describing an empty table — a good reason to `ANALYZE` explicitly at the end of an import.
- **Correlated columns.** The planner assumes predicates are independent, so `WHERE country = 'DE' AND city = 'Berlin'` multiplies two selectivities and estimates far too few rows. `CREATE STATISTICS ... (dependencies)` teaches it otherwise.
- **A function around the column.** `WHERE lower(email) = ...` cannot use an index on `email`. Add an expression index, or make the column case-insensitive with `citext` or a non-deterministic ICU collation.
- **`random_page_cost` left at its default of 4.0**, which models a spinning disk. On SSD-backed storage a value near 1.1 is realistic, and the wrong setting systematically biases the planner toward sequential scans.
- **A `LIMIT` with a bad estimate.** The planner assumes it can stop early and picks a plan that walks an index until it finds enough matches. When the predicate is rarer than estimated, that walk covers the whole table. This is the classic "instant for most customers, thirty seconds for one".

> **Pitfall:** `.Where(u => u.Email.ToLower() == email)` in LINQ becomes `lower(email) = $1` in SQL, and quietly stops using your index on `email`. It is the single most common accidental index-defeat in EF Core on PostgreSQL. Either index the expression or fix the column's collation.

### Finding the Queries Worth Looking At

You cannot `EXPLAIN` a query you have not identified. Two extensions do the finding for you:

- **`pg_stat_statements`** aggregates every normalized statement with call count and total execution time. Sort by `total_exec_time`, not `mean_exec_time` — the query that costs you the most is usually a fast one running constantly, not the slow one running hourly.
- **`auto_explain`** logs the plan of any statement exceeding a duration threshold, which is how you capture the plan for a query that is only slow in production with production data.

`pg_stat_user_indexes` is worth a look too: an index with `idx_scan = 0` after a representative period is pure cost — write amplification and one more reason a HOT update cannot happen. Drop it.

### The .NET Side

Getting the SQL out of EF Core is the first step; `LogTo` will print it, and in development `EnableSensitiveDataLogging` includes parameter values so you can replay the statement faithfully. Do run `EXPLAIN` with the **same parameter values**, since selectivity is exactly what the planner reasons about.

Two Npgsql behaviours are worth knowing:

- **Automatic preparation.** Npgsql promotes a statement to a server-side prepared statement after it has been executed a few times (`Max Auto Prepare`). This saves parse and plan time, but after five executions Postgres may switch to a **generic plan** built without knowing your parameter values — which is a poor trade for a column with skewed data. `plan_cache_mode = force_custom_plan` is the escape hatch.
- **Connection poolers change the rules.** PgBouncer in transaction-pooling mode multiplexes connections across transactions, which breaks session-level state. Prepared statements were the classic casualty: a statement prepared on one server connection didn't exist on the next. Since PgBouncer 1.21 it tracks protocol-level prepared statements (the kind Npgsql uses) and re-prepares them on whichever server connection a transaction lands on; 1.24 turned this on by default (`max_prepared_statements = 200`). SQL-level `PREPARE`/`EXECUTE` still break, and on an older PgBouncer, or with the setting at 0, so does driver-level preparation. [Connection Management Under Load](#connection-management-under-load) below covers pooling; the point here is that a plan-caching win at the driver level can disappear entirely depending on what sits between you and the server.

Finally, three mapping choices that prevent whole categories of problem: store timestamps as `timestamptz` and never `timestamp` (see [Chapter 15](#chapter-15-dates-money-and-strings) on why "local time" is not a thing you can store); use `jsonb` rather than `json` for anything you will query, and index it with GIN; and reach for `citext` or a case-insensitive collation instead of scattering `ToLower()` through your LINQ.

> **Best practice:** Index the queries you actually run, not the columns that look important. Capture `EXPLAIN (ANALYZE, BUFFERS)` before and after every index you add, keep the two outputs in the pull request, and delete indexes that no scan counter has ever touched.

## Caching — in depth

[Chapter 7](#chapter-7-data-access) teaches the basics: `IMemoryCache` against `IDistributedCache`, cache-aside, invalidation, stampedes and HybridCache. The senior judgment on top is *when* to cache at all: data that is **read far more than written** and **tolerates some staleness** (reference data, computed aggregates, rendered fragments). Rapidly changing, per-user-critical or must-be-consistent data is not a cache candidate, and every cache needs an expiry, a size bound and a story for how stale data gets refreshed. The rest of this section is what it takes to run the shared tier, Redis, well.

### Redis in Practice: Key Design, Data Types, and Eviction

`IDistributedCache` presents Redis as a dictionary of byte arrays, which is enough to get started and hides most of what makes Redis good. A few decisions repay knowing the real thing.

**Key design is schema design.** Redis has no tables, so the key is the only structure you get. The conventional shape is colon-separated and hierarchical, from most general to most specific, with a version segment:

```
shop:v3:product:1234
shop:v3:product:1234:reviews
shop:v3:tenant:acme:cart:9f2c
```

The version segment is the part people leave out and regret. When the shape of a cached object changes in a deploy, old entries deserialize into the new type as garbage or throw. Bumping `v3` to `v4` makes the entire previous generation unreachable and lets it expire naturally — a rename instead of a mass deletion, and no cold-start thundering herd against the database at the moment of deploy.

**Always set a TTL, and jitter it.** A cache without expiry is a memory leak with good manners. And if a deploy or a batch job populates ten thousand keys at once with the same TTL, they expire at the same instant and all miss together — a stampede you created on a schedule. Adding a random spread of a few percent to each TTL breaks the synchronisation.

**Use the data types.** Storing a serialized object in a plain string means every field update rewrites the whole value, and every read transfers all of it:

| Type | Use it for |
|---|---|
| String | A serialized object, a counter (`INCR` is atomic), a flag |
| Hash | An object whose fields are read or updated independently — `HSET user:1 last_seen ...` touches one field |
| Sorted set | Leaderboards, rate limiters, and time-ordered indexes; range queries by score |
| Set | Membership and tag indexes — "which keys belong to product 1234" |
| List | Simple queues; `BLPOP` gives you a blocking pop |
| Stream | An append-only log with consumer groups — a real message primitive (see [Chapter 11](#chapter-11-messaging-and-background-work)) |

**Invalidation by tag** is where sets earn their place. You cannot glob for keys to delete — `KEYS pattern` walks the entire keyspace and, because Redis executes commands on a single thread, it blocks *every other client* while it does. (`SCAN` is the cursor-based alternative that does not, and is what any maintenance script should use.) Instead, maintain the index yourself: when caching a derived value that depends on product 1234, also add its key to the set `tag:product:1234`. On a write, read the set, delete those keys, delete the set. HybridCache exposes this idea directly with tags on `GetOrCreateAsync` and `RemoveByTagAsync`.

**Round trips dominate.** A Redis operation takes microseconds on the server and a network round trip to reach it, so ten sequential `GET`s cost ten round trips and one batch costs one. StackExchange.Redis pipelines automatically when you fire off several tasks before awaiting them:

```csharp
var db = _mux.GetDatabase();
var batch = ids.Select(id => db.StringGetAsync($"shop:v3:product:{id}")).ToArray();
var values = await Task.WhenAll(batch);   // one round trip, not ids.Length
```

**Eviction is a policy you must choose.** When Redis reaches `maxmemory`, the default `noeviction` policy starts *rejecting writes* — your cache stops accepting new entries and the application starts throwing on set. For a pure cache you want `allkeys-lru` (or `allkeys-lfu`), which discards the least useful key instead. Getting this wrong turns a full cache into an outage rather than a slowdown, and it is the most common Redis misconfiguration in production.

> **Gotcha:** Redis executes commands on a single thread. That is what makes its operations atomic and its latency predictable, and it means one expensive command — a `KEYS` sweep, a large `LRANGE`, an unbounded Lua script, or deleting a multi-megabyte value — stalls every other client for its whole duration. Slow queries in Redis are not slow for one caller; they are slow for everyone.

Two more practical notes. `ConnectionMultiplexer` is expensive, thread-safe, and designed to be shared: register exactly one as a **singleton** and never wrap it in a `using` (the classic mistake, and the reason for mysterious connection storms; [Chapter 3](#chapter-3-how-net-runs-your-code) covers lifetimes). And in a Redis **Cluster**, keys are distributed by hash slot, so a multi-key operation only works if the keys land on the same node; wrapping the common part in braces — `cart:{acme}:9f2c` — makes Redis hash only that part, keeping a tenant's keys together.

> **Pitfall:** Do not use a Redis lock as a correctness mechanism. The single-instance `SET key value NX PX` lock is fine for suppressing duplicate work — one instance rebuilds the cache, the others wait — but it cannot guarantee mutual exclusion across failover, and the distributed variant (Redlock) rests on timing assumptions that are actively disputed. If two workers doing the same thing would corrupt data, enforce it in the database with a unique constraint or a row lock, not in the cache.

Finally, measure the thing that tells you whether any of this is working: the **hit rate**. A cache below roughly 80% hits is usually caching the wrong things, or expiring them faster than they are reused — and every miss now costs a network round trip *plus* the original query, which makes a badly-tuned cache slower than no cache at all.

### Session State and the Stateless App Tier

The moment you run more than one instance of your app behind a load balancer, **in-memory state becomes a liability**. If instance A stores a user's session in its own RAM and the next request hits instance B, the session is gone. So shared state moves out of the process, most commonly into Redis:

```
        ┌── App Instance 1 ──┐
Client ─┤   App Instance 2   ├──▶ [ Redis ] ← single source of shared truth
        └── App Instance 3 ──┘       (cache + session store)
```

Configure ASP.NET Core sessions to use the distributed cache and any instance can serve any user. That keeps the app tier **stateless**, the property that makes horizontal scaling trivial: stateless app servers, externalized state and asynchronous messaging are, in a sentence, the architecture of nearly every scalable modern system. When the cached value depends on data another service owns, invalidation becomes an event: a `ProductUpdated` message whose consumer evicts the key ([Chapter 11](#chapter-11-messaging-and-background-work)).

> **Capstone tie-in.** ShopCore Step 5 (Caching, Auth, and Observability) in [Chapter 44](#chapter-44-capstone-one-project-growing-up) adds Redis as a distributed cache for the hot product-catalog read path, with invalidation on writes.

## Vertical vs. Horizontal Scaling

For most of a system's life, a single well-tuned database is enough. You add indexes, you cache the hot paths, you buy a bigger machine, and the graphs stay green. Then one day they don't. The write-ahead log can't flush fast enough, a nightly report locks a table that customers need, connections pile up faster than the pool can hand them out, and your biggest customer's traffic starts starving everyone else. Scaling data is the art of pushing that day as far into the future as possible, and knowing what to do when it finally arrives.

There are only two directions you can scale, and the choice colors every decision that follows.

**Vertical scaling** (scaling *up*) means giving one machine more resources: more CPU cores, more RAM, faster NVMe disks. It is gloriously simple. Your application doesn't change, your transactions stay ACID, your joins still work. The ceiling is real, though — the biggest cloud database instance is finite, and the price curve is exponential. Doubling from a medium box to a large one is cheap; doubling from the largest box to *twice that* is impossible at any price.

**Horizontal scaling** (scaling *out*) means adding more machines and spreading the load across them. The ceiling is effectively unbounded, but you pay for it in complexity: data now lives in more than one place, and the comforting guarantees of a single node — a global transaction, a cheap join, a unique index — start to fray.

> **Best practice:** Scale *up* until it genuinely hurts before you scale *out*. A single Postgres instance on modern hardware can serve enormous workloads. Horizontal scaling is a one-way door that permanently raises your operational complexity; walk through it deliberately, not reflexively.

The sections that follow are a tour of horizontal-scaling techniques, ordered roughly from least to most invasive.

## Scaling Reads with Replication

The cheapest form of horizontal scaling exploits a fact true of almost every business system: **reads vastly outnumber writes.** A product catalog is written once and read a million times. If you can serve those reads from copies of the database, your primary node only has to handle writes.

That is **replication.** A *primary* (or *leader*) accepts all writes and streams its changes — in Postgres, the write-ahead log — to one or more *replicas* (or *followers*). Each replica applies the same changes and can serve read queries. Add more replicas, serve more reads.

### Replication Lag Is the Whole Story

Replicas don't update instantly. There's a delay — usually milliseconds, occasionally seconds under load — between a write committing on the primary and appearing on a replica. This **replication lag** is the single most important thing to understand about read replicas, because it breaks an assumption your code has silently relied on forever: *read-your-own-writes.*

Picture a user updating their profile name. The write goes to the primary. The page reloads, issues a read, and that read is routed to a replica that hasn't caught up yet. The user sees their *old* name and concludes the save failed. They save again. Now you have a support ticket.

> **Pitfall:** Read replicas give you **eventual consistency**, not the strong consistency of a single node. Any flow where a user reads immediately after writing — form submissions, wizards, "did it save?" checks — must either route that read to the primary or tolerate stale data.

Common mitigations:

- **Route reads-after-writes to the primary.** After a write in a request, pin subsequent reads in that same logical operation to the primary.
- **Sticky primary window.** For a few seconds after a user writes, send all their reads to the primary.
- **Accept staleness where it's harmless.** Dashboards, search results, and reports rarely care about a 200ms lag.

### Routing Reads and Writes in EF Core

EF Core has no native primary/replica awareness, but the pattern is straightforward: decide *at creation time* whether a context talks to the primary or a replica, and hand out the right one from a factory.

```csharp
public sealed class CatalogContext(DbContextOptions<CatalogContext> options)
    : DbContext(options)
{
    public DbSet<Product> Products => Set<Product>();
}

public sealed class CatalogContextFactory(IConfiguration config)
{
    public CatalogContext CreateWrite() => new(Build("Primary", readOnly: false));
    public CatalogContext CreateRead()  => new(Build("Replica", readOnly: true));

    private DbContextOptions<CatalogContext> Build(string name, bool readOnly)
    {
        var builder = new DbContextOptionsBuilder<CatalogContext>()
            .UseNpgsql(config.GetConnectionString(name));
        // A replica context should never accidentally issue writes.
        if (readOnly)
            builder.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
        return builder.Options;
    }
}
```

> **Pitfall:** The tempting alternative — one context with a `_readOnly` flag you flip before querying, checked in `OnConfiguring` — is subtly broken. A context's configuration is built once and then cached (per instance on first use, and across instances when the context is pooled or resolved from DI), so a flag flipped afterwards is silently ignored and your "replica" query runs against the primary. Choose the connection when the context is *constructed*, not later.

The key discipline is that **any `SaveChanges` must go to the primary**, and read-only queries that can tolerate lag go to a replica. Marking replica contexts `NoTracking` is doubly useful: it's faster, and it structurally prevents a replica context from ever trying to save.

## Connection Management Under Load

A subtle scaling wall has nothing to do with data size: **connections.** Each Postgres connection is a backend process consuming several megabytes of RAM, and the server performs best with a *small* number of active connections — often just a few dozen. But a fleet of application servers, each with its own connection pool, can easily demand thousands.

Two layers of pooling save you:

- **Application-level pooling.** ADO.NET / Npgsql pool connections per process, reusing them across requests instead of opening a new one each time (opening a Postgres connection is expensive — a TCP handshake plus a process fork). This is on by default; the trap is *misconfiguring the max pool size* so that a slow query storm exhausts it and requests queue.
- **An external pooler like PgBouncer.** This sits between your app fleet and Postgres and multiplexes thousands of client connections onto a small pool of real database connections. In **transaction pooling** mode, a real connection is only held for the duration of a transaction, so hundreds of mostly-idle clients share a handful of backends. Serverless and autoscaling architectures — where instance count balloons unpredictably — essentially *require* a pooler to avoid overwhelming the database.

> **Pitfall:** PgBouncer's transaction-pooling mode breaks anything that relies on session state spanning multiple statements — session-level `SET`, SQL-level `PREPARE`, `LISTEN/NOTIFY`, advisory locks held across statements. Protocol-level prepared statements, which Npgsql uses, survive only from PgBouncer 1.21 on with `max_prepared_statements` above 0 (the default since 1.24). Know your pooling mode and its constraints before you deploy it.

## Partitioning and Sharding

Replication scales *reads* but does nothing for *writes* — every write still hits the single primary, and the entire dataset still has to fit on one machine. When the write volume or the data size exceeds one node, you must split the data itself. This is **partitioning**, and when partitions live on separate database servers, it's **sharding.**

The idea: instead of one table with a billion rows, keep many tables (shards) each holding a slice of the rows, spread across many servers. You choose a **shard key** — a column whose value decides which shard a row belongs to — and a strategy for mapping keys to shards.

### Range, Hash, and Key-Based Sharding

- **Range sharding** splits by ranges of the key: customers A–F on shard 1, G–M on shard 2, and so on. Range queries ("all orders in January") stay on one shard, which is efficient. The danger is **hot spots** — if half your customers' names start with S, that shard is overloaded. Time-based ranges are notorious for this: today's shard gets *all* the writes while yesterday's sits idle.
- **Hash sharding** runs the key through a hash function and uses the result to pick a shard. This spreads load evenly and kills hot spots, but destroys locality — a range query must now hit *every* shard.
- **Directory/key-based sharding** keeps an explicit lookup table mapping keys (or key ranges) to shards. Maximally flexible and easy to rebalance, but the directory becomes a critical dependency you must keep available and consistent.

### Choosing a Shard Key Is the Decision That Matters Most

The shard key is the most consequential choice in the whole design, because it determines which queries are cheap and which are agony. A good shard key has three properties:

1. **High cardinality** — enough distinct values to spread data across all shards.
2. **Even distribution** — no single value dominates (don't shard on `Country` if 80% of users are in one country).
3. **Alignment with your access patterns** — the queries you run most often should be answerable from a *single* shard.

That third point is the crux. In a multi-tenant SaaS, `TenantId` is often the ideal shard key: virtually every query is already scoped to one tenant, so it naturally lands on one shard. Shard by the wrong key and your most common query becomes a **scatter-gather** across every shard.

> **Best practice:** Pick the shard key by looking at your *read* patterns, not your data model. The question is not "how is this data structured?" but "what does 95% of my traffic filter by?"

### The Pain: Cross-Shard Queries, Joins, and Transactions

Once data is split, three things that used to be free become expensive or impossible:

**Cross-shard queries (scatter-gather).** A query that isn't scoped to the shard key must be sent to *every* shard, and the results merged in your application. `ORDER BY ... LIMIT 10` across 20 shards means pulling the top 10 from each of the 20, then re-sorting 200 rows to find the real top 10. Aggregations, pagination, and `COUNT(*)` all get painful.

**Distributed joins.** A join between two tables only works cheaply if both tables' relevant rows live on the *same* shard. This is why sharded systems try to **co-locate** related data — put a customer and all their orders on the same shard, keyed by `CustomerId` — so joins stay local. Join across the shard boundary and you're either shipping data over the network or denormalizing to avoid the join entirely.

**Distributed transactions.** A single ACID transaction spanning two shards requires a protocol like two-phase commit (2PC), which is slow, locks resources across nodes, and stalls entirely if the coordinator dies mid-commit. In practice, most large systems **refuse** distributed transactions and instead embrace eventual consistency: each shard commits locally, and cross-shard consistency is reconciled asynchronously via patterns like the **Saga** (a sequence of local transactions with compensating actions on failure; see [Chapter 20](#chapter-20-distributed-systems)).

> **Pitfall:** Teams often shard to solve a performance problem and discover they've traded a *throughput* problem for a *correctness and complexity* problem. The uniform, transactional, joinable world of a single database is a luxury you don't appreciate until it's gone.

### Resharding

Your first shard layout will be wrong, because your data will grow unevenly. **Resharding** — moving data between shards to rebalance — is one of the hardest operations in data engineering, because it must happen *while the system is live.*

The technique that makes this bearable is **consistent hashing** combined with many small **virtual shards.** Instead of mapping keys directly to 4 physical servers, map them to (say) 256 virtual shards, then map those virtual shards to physical servers. To add capacity, you move some virtual shards to a new server — only a fraction of the data moves, and the mapping change is a metadata update, not a full reshuffle. Provisioning far more logical shards than you currently need ("over-sharding") is cheap insurance: you can spread them across more hardware later without ever re-hashing keys.

## Migrations at Scale (Zero-Downtime)

On a small system you run a migration, take thirty seconds of downtime, and move on. At scale that's unacceptable — and worse, some migrations lock large tables for minutes and hold up every request. The goal is **zero-downtime schema evolution**, and the governing technique is the **expand/contract** (also called parallel-change) pattern.

The insight: **you cannot deploy the schema change and the code that needs it at the same instant**, because during a rolling deploy, old and new code run simultaneously against the same database. So you split every breaking change into backward-compatible steps:

1. **Expand.** Add the new structure without removing the old. Add a nullable column, a new table, a new index (built `CONCURRENTLY` in Postgres so it doesn't lock writes). Old code ignores it; new code can start using it. This step is safe because it breaks nothing.
2. **Migrate & dual-write.** Deploy code that writes to *both* old and new structures and backfills existing rows in batches. Now the data is consistent under both shapes.
3. **Contract.** Once all code reads and writes the new shape and no old code remains, drop the old column/table in a later release.

Consider renaming a `Name` column to `FullName` — a one-liner that, done naively, breaks every running instance of the old code the moment it deploys. Expand/contract turns it into: add `FullName` (expand) → deploy code writing both, backfill (migrate) → deploy code using only `FullName` → drop `Name` (contract). Each step is independently deployable and reversible.

> **Pitfall:** `ALTER TABLE` operations that rewrite a table or take an `ACCESS EXCLUSIVE` lock will block all reads and writes for the duration. On a large table under load this is an outage. Always check whether an operation is lock-free; add indexes `CONCURRENTLY`; add columns *without* a volatile default (modern Postgres makes adding a column with a constant default cheap, but backfilling is not).

[Chapter 7](#chapter-7-data-access) decides *where* migrations run (startup or a pipeline step). **Feature flags** ([Chapter 26](#feature-flags)) pair naturally with expand/contract: they decouple *deploying* the code that uses the new shape from *releasing* it, which is exactly what you want while the schema underneath is mid-transition.

## Change Data Capture (CDC)

Sharded or not, large systems rarely keep all their data in one place. The catalog lives in Postgres, search lives in Elasticsearch, the recommendation engine wants a stream of events, and analytics wants everything in a warehouse. The naive approach — have the application write to all of them — is a distributed-transaction nightmare: what happens when the Postgres write succeeds but the Elasticsearch write fails?

**Change Data Capture** solves this by treating the database's own change log as a source of truth. Every committed change (insert, update, delete) is captured and streamed to downstream consumers. Because it reads the write-ahead log *after commit*, a consumer sees exactly what was durably committed — no dual-write inconsistency.

**Debezium** is the de facto open-source CDC platform. It plugs into a database's replication stream (Postgres logical replication, MySQL binlog, etc.) and publishes each change as an event to **Kafka**. Downstream, one consumer updates the search index, another updates a cache, another feeds analytics — all decoupled from the application, all fed from the same ordered stream.

### Transactional Outbox vs. CDC

A common goal is: *"when I save an order, reliably publish an OrderCreated event."* Two patterns address this.

The **transactional outbox** ([Chapter 11](#chapter-11-messaging-and-background-work) covers its mechanics) writes the business change and an event row to an `outbox` table **in the same local transaction.** Because both are in one ACID transaction, they commit or fail together — no dual-write problem. A separate relay process then reads the outbox and publishes the events.

The relay can poll the outbox table — or, elegantly, **CDC can read the outbox table** and stream its rows to Kafka, giving you low-latency publishing with no polling. This "outbox + Debezium" combination is a widely used, robust pattern.

So how do outbox and CDC relate? The outbox is *your application deliberately writing events you designed*; CDC is *infrastructure capturing raw row changes*. Use the **outbox** when you want clean, intentional domain events with a stable contract. Use **raw CDC** when you want to replicate or react to table changes without touching the application — for example, feeding a data warehouse. They compose beautifully: CDC is often the *transport* for outbox rows.

> **Best practice:** Never dual-write to a database and a message broker in application code hoping both succeed. Use the outbox pattern so the event and the state change share one transaction, then ship the events with CDC or a relay.

## NoSQL and Polyglot Persistence

No single database is good at everything, and some data shapes fit other models better than tables:

- **MongoDB (document store):** stores JSON-like documents. Great when your data is hierarchical and read as a unit (a product with nested variants, specs and reviews). Flexible schema suits evolving or heterogeneous data. Weaker at cross-document transactions and complex joins.
- **Redis (key-value / in-memory):** fast because it lives in RAM. Ideal for caching, session state, rate-limiting counters, leaderboards and pub/sub. Not your system of record; treat it as ephemeral.
- **Elasticsearch (search engine):** built for full-text search, relevance ranking and analytics over large volumes. Use it alongside, not instead of, your primary database, which stays the source of truth.

**Polyglot persistence** means using the right store for each job: Postgres for transactional integrity, Elasticsearch for full-text search, Redis for ephemeral session data, a columnar warehouse for analytics, a graph database for relationship queries. CDC is the glue that keeps these stores in sync from a single source of truth.

The same idea, applied to one service's own data, is a **CQRS read store** ([Chapter 21](#chapter-21-architecture) teaches the pattern): writes go to a normalized transactional model, reads are served from stores shaped exactly for how they are queried (pre-joined, denormalized, indexed for one screen), kept up to date asynchronously, often from the same event or CDC stream. You accept eventual consistency in exchange for read models that are fast and don't compete with writes. And under all of it sit the caching layers (in-process, Redis, HTTP and CDN at the edge); CDC can drive precise cache invalidation too, by streaming the exact rows that changed.

> **Rule of thumb.** Choose storage by access pattern, not by hype. Most systems are polyglot: a relational database as the source of truth, Redis for caching, Elasticsearch for search. NoSQL is a specialization, not a replacement.

## Multi-Tenancy

A **multi-tenant** application serves many independent customers (tenants) from shared infrastructure. The central engineering tension is **isolation vs. efficiency**: strong isolation (separate everything) is safe but expensive; strong sharing (everything in one place) is cheap but risky. There are three canonical models along that spectrum.

### The Three Isolation Models

**1. Shared schema (discriminator column).** All tenants share the same tables; every tenant-owned row carries a `TenantId` column, and every query filters on it. This is the **cheapest and most scalable** model — one database, one schema, one connection pool. It's how most large SaaS products run. The price is that isolation is *entirely enforced by your code*: forget a single `WHERE TenantId = ...` and you leak one customer's data to another. Noisy neighbors also share resources directly.

**2. Schema-per-tenant.** One database, but each tenant gets its own schema (namespace) with its own copy of the tables. Better isolation — a query is scoped to a schema — and you can back up or migrate tenants somewhat independently. But schema count becomes an operational burden: migrations must run across hundreds of schemas, and thousands of schemas strain the database's catalog.

**3. Database-per-tenant.** Each tenant gets a physically separate database (or even server). **Maximum isolation** — a bug literally cannot cross a database boundary, noisy neighbors are contained, and you can put a premium tenant on dedicated hardware or in their required data-residency region. The cost is operational: hundreds of databases to provision, migrate, back up, and monitor, and far more idle capacity (each database has its own overhead even when the tenant is tiny).

| Model | Isolation | Cost/density | Ops burden | Typical fit |
|---|---|---|---|---|
| Shared schema | Weakest (code-enforced) | Cheapest, densest | Lowest | High-volume SaaS, many small tenants |
| Schema-per-tenant | Medium | Medium | Medium | Mid-market, moderate tenant count |
| Database-per-tenant | Strongest | Most expensive | Highest | Enterprise, regulated, data-residency |

> **Best practice:** Many mature SaaS platforms are *hybrid*: shared schema for the long tail of small customers, dedicated databases for large enterprise accounts that pay for isolation and demand data residency. The model is a per-tenant attribute, not a global decision.

### Tenant Resolution

Before any query runs, the system must answer: *which tenant is this request for?* This **tenant resolution** happens early in the pipeline (middleware) and typically reads one of:

- **Host/subdomain** — `acme.myapp.com` → tenant `acme`. Clean and cache-friendly.
- **HTTP header** — a custom `X-Tenant-Id` header, common for APIs.
- **A claim in the auth token** — the JWT carries a `tenant_id` claim. This is the most secure, because the tenant identity is cryptographically bound to the authenticated user and can't be spoofed by changing a URL.

The resolved tenant is stashed in a request-scoped service (`ITenantContext`) that everything downstream — including the `DbContext` — reads.

> **Pitfall:** Deriving the tenant from a header or route parameter that the *client* controls, without cross-checking it against the authenticated user's allowed tenants, is a classic privilege-escalation hole. A user authenticated for tenant A must not be able to set `X-Tenant-Id: B` and read tenant B's data. Always validate the requested tenant against the token's claims.

### Enforcing Isolation with EF Core Global Query Filters

In the shared-schema model, the nightmare scenario is a forgotten filter. Writing `WHERE TenantId = @t` on every one of hundreds of queries is a matter of time before someone forgets. EF Core's **global query filters** make isolation the *default* instead of something you remember: define the filter once on the entity, and EF appends it to **every** query for that entity automatically.

```csharp
public class AppDbContext : DbContext
{
    private readonly ITenantContext _tenant;

    public AppDbContext(DbContextOptions options, ITenantContext tenant)
        : base(options)
    {
        _tenant = tenant;
    }

    public DbSet<Invoice> Invoices => Set<Invoice>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Every query against Invoice is silently scoped to the current tenant.
        modelBuilder.Entity<Invoice>()
            .HasQueryFilter(i => i.TenantId == _tenant.TenantId);
    }

    // Also stamp TenantId on insert so nobody has to remember.
    public override int SaveChanges()
    {
        foreach (var entry in ChangeTracker.Entries<Invoice>())
            if (entry.State == EntityState.Added)
                entry.Entity.TenantId = _tenant.TenantId;
        return base.SaveChanges();
    }
}
```

Now `context.Invoices.ToList()` returns only the current tenant's invoices — the filter is compiled into the SQL. This is a **defense in depth** default, not a complete guarantee. Be aware of its escape hatches:

- **`IgnoreQueryFilters()`** removes the filter for a query. It exists for legitimate admin/reporting needs, but it's a loaded gun — one careless call and isolation is gone.
- **Raw SQL** (`FromSqlRaw`) and **stored procedures** bypass the filter entirely; you must scope them by hand.
- **Navigation loads and `Find()`** honor filters, but be careful with cross-tenant foreign keys.
- The filter reads `_tenant.TenantId` **when the query is built**, so the tenant context must be correctly resolved before any query runs.

> **Pitfall:** A global query filter protects *reads*. It does **not** stop you from *inserting* a row with the wrong `TenantId`, or *updating* a row you loaded via `IgnoreQueryFilters`. Stamp `TenantId` automatically on insert (as above), and treat every `IgnoreQueryFilters` call as a security-sensitive event worth a code-review flag. For the strongest guarantee, add a database-level safety net — Postgres **Row-Level Security** policies enforce tenant scoping even if the application forgets.

### Per-Tenant Migrations, Noisy Neighbors, and Cost

**Migrations** get harder as isolation increases. Shared schema: one migration, done. Schema- or database-per-tenant: the *same* migration must run against every tenant's schema/database, which means orchestration (a loop over tenants, with retry and progress tracking), the risk of partial rollout (some tenants migrated, some not — so your code must tolerate both shapes, exactly the expand/contract discipline from [Migrations at Scale](#migrations-at-scale-zero-downtime)), and a real time cost when you have thousands of tenants.

**Noisy neighbors** are the flip side of density. In shared-schema, one tenant running a giant report or a runaway query consumes CPU, I/O, and connections that everyone else needs, and everyone's latency suffers. Mitigations range from soft (per-tenant rate limits, query timeouts, separate connection pools for heavy operations) to hard (move the offender to a dedicated database — the isolation model itself is the ultimate noisy-neighbor fix). This is precisely why big-spending tenants often get their own database: they're paying to *not* share.

**Per-tenant scaling and cost** tie scale and tenancy together. Sharding and multi-tenancy converge: when you shard a multi-tenant system by `TenantId`, each shard is essentially a group of tenants, and moving a hot tenant to its own shard *is* resharding. The economics are the core of the SaaS business model — density (many tenants per resource) drives your gross margin, while isolation drives your ability to serve enterprise and regulated customers. The senior engineer's job is to place each tenant at the right point on that spectrum: pack the small ones tightly, isolate the large and sensitive ones, and build the tooling to move a tenant from one model to the other as they grow.

## Practice

**1. The Chapter 19 lab in full ([`labs/37-execution-plans`](https://github.com/malyna2/dotnet-handbook/tree/main/labs/37-execution-plans)).** If you did [Chapter 7](#chapter-7-data-access)'s subset (setup, Level 1, rung 6 and SQL Server script 1), the rest takes about 6 h: rungs 5 and 7–9, Level 3 (the server's view, `auto_explain`, pricing `force_custom_plan` against an index, the SQL Server track), *Break it* and the write-up. From scratch, the chapter's time budget adds up to 9 h 30 min – 10 h 30 min. You need Docker, about 6 GB of free RAM (8 GB with SQL Server) and the .NET 10 SDK.

```bash
cd labs/37-execution-plans
docker compose up -d --wait
./seed.sh medium                                      # about 4 minutes
dotnet run --project src/QueryLab.Cli -- 9            # one rung, with its plan
docker compose exec -T postgres psql -U lab -d shop -f /lab/break-it-stale-statistics.sql
docker compose --profile sqlserver up -d && ./sqlserver.sh seed
```

For every rung: predict the plan, run it, name the mechanism, apply the smallest fix, and price it (build time, size, the cost on every insert). Only then compare with the *Hints and answers* at the end of [Chapter 19](#chapter-19-the-slow-query-lab-reading-execution-plans).

**2. The lost update, live (20 min).** In the lab's PostgreSQL, open two sessions (`docker compose exec postgres psql -U lab -d shop`) and create a scratch table with one row holding a balance of 100. In both sessions: begin, read the balance, then write back the value you read plus 10, and commit. Under the default Read Committed, the final balance is 110: one write was lost. Repeat with `BEGIN ISOLATION LEVEL REPEATABLE READ`: the second session's update fails with `could not serialize access due to concurrent update`. Then do it once more with `UPDATE … SET balance = balance + 10` and no read at all, which gives 120 under either level. Drop the table when you are done.

**Evidence to keep**, in your own public portfolio repo, not in this one: `RESULTS.md` with its environment header, the before and after plans, the price of every index, the rung 9 trade-off paragraph and the SQL Server comparison table, plus the three isolation transcripts.

Later, if you need it: [Chapter 7: Dapper](#dapper-when-the-orm-is-too-much), [Connection Management Under Load](#connection-management-under-load), and [Chapter 29: Choosing the partition key](#choosing-the-partition-key-the-decision-you-cannot-easily-undo) for the same decision in Cosmos DB.

## Three questions

**1.** Two requests decrement the same product's stock with EF Core: load the entity, `Stock -= 1`, `SaveChanges`. Under Read Committed both succeed, and one decrement is lost. Why doesn't Read Committed prevent it, what does Repeatable Read do instead on PostgreSQL and on SQL Server, and what would you ship?

<details>
<summary>Answer</summary>

- **Read Committed promises committed data, not current data.** Both reads saw stock 10, legitimately. The write is computed from a value that changed in between: a read-modify-write race, which no read guarantee covers.
- **Repeatable Read turns it into an error.** On PostgreSQL the snapshot makes the second update fail with SQLSTATE 40001. On SQL Server both transactions keep their shared locks, both ask for exclusive ones, and the deadlock monitor kills one (1205). Either way the caller must retry the whole transaction, starting with the read.
- **Ship one of three, by contention:**
  - an atomic conditional update (`ExecuteUpdate` with `WHERE Stock >= 1`, checking the rows affected): no read, no race, no retry, but it bypasses the change tracker and interceptors;
  - an optimistic concurrency token (`[Timestamp]` on SQL Server, `xmin` on PostgreSQL) with a bounded retry on `DbUpdateConcurrencyException`, when the domain logic needs the entity;
  - `SELECT … FOR UPDATE` in a short transaction, when contention is high and retries would thrash.
</details>

**2.** A query that took 5 ms for months now takes a second for one customer, and nothing was deployed. Name three mechanisms that change a plan without a code change, how the plan tells them apart, and the durable fix for each.

<details>
<summary>Answer</summary>

- **Stale statistics.** Estimated and actual rows diverge by orders of magnitude at the scan; running `ANALYZE` (or updating statistics) fixes the estimate. Durable fix: analyze explicitly at the end of bulk loads, and tune the per-table autovacuum thresholds for tables that grow in bursts.
- **A plan built for another value.** In PostgreSQL, `$1` in the index condition and an estimate fitted to an average value: a generic plan. In SQL Server, the plan's compiled parameter values differ from the runtime ones: parameter sniffing. Durable fix: an index that makes one plan right for every value. Stopgaps: `plan_cache_mode = force_custom_plan`, `OPTION (RECOMPILE)`, a Query Store forced plan.
- **A cleared visibility map or bloat.** An `Index Only Scan` whose `Heap Fetches` jumped after heavy writes or a rolled-back bulk operation. Durable fix: vacuum keeps up on that table (`autovacuum_vacuum_scale_factor` per table), and write-heavy tables don't rely on index-only scans.

The habit behind all three: compare the plan with its previous self, node by node, before touching an index.
</details>

**3.** Product pages are cached in Redis with a 10-minute TTL, and a deploy warms all 10,000 keys at once. The database spikes every 10 minutes after each deploy, and a price change takes up to 10 minutes to show. Explain both, and fix both without dropping the cache.

<details>
<summary>Answer</summary>

- **The spike is a stampede on a schedule.** Keys written together with one TTL expire together, and every miss rebuilds from the database at the same moment. Jitter each TTL by a few percent, and collapse concurrent misses per key: HybridCache does it within one process, and a short `SET NX PX` lock suppresses duplicate rebuilds across instances (an efficiency lock, not a correctness one).
- **The stale price is invalidation by TTL alone.** Evict the key in the code path that changes the price, *after* the commit, so a reader can't re-cache the old row while the transaction is open. Keep the TTL as the safety net for writers you missed, or drive eviction from CDC.
- **What remains.** A reader that loaded the old price just before the commit can still write it back after your eviction. That window is bounded by the TTL; where it isn't acceptable (the price at checkout), read from the database, not the cache.
</details>

## Decide

A multi-tenant SaaS runs on one PostgreSQL primary with a shared schema (`TenantId` on every table). At peak the primary sits at 80% CPU. About 70% of the load is dashboard reads, and one tenant produces 30% of all writes. Three proposals:

1. Add read replicas and route dashboard reads to them.
2. Shard by `TenantId`.
3. Move the large tenant to a database of its own and keep everyone else shared.

<details>
<summary>How a senior engineer weighs it</summary>

**First, the precondition.** 80% CPU can be a handful of statements. Sort `pg_stat_statements` by total time and read the top plans before buying infrastructure: the advice in [Vertical vs. Horizontal Scaling](#vertical-vs-horizontal-scaling) is to scale up until it genuinely hurts.

**What each costs.**

- *Replicas:* days, and no change to the data model. They absorb the dashboard reads that dominate the load. The cost is lag: any read right after a write must go to the primary, so routing becomes a rule in code. Writes still have one node.
- *Sharding:* months, and a one-way door. It scales writes and data size, and in exchange cross-shard queries become scatter-gather, cross-shard transactions become sagas, and resharding becomes a project.
- *A dedicated database for the large tenant:* weeks. It removes the noisy neighbour and makes the tenant's own growth its own problem. The cost is a second shape to operate: migrations run against two targets, and the code must route by tenant.

**What decides it here:** where the load comes from. Reads dominate, so replicas address most of it. The large tenant is a noisy-neighbour problem, not yet a scale problem.

**The choice.** Fix the top queries, add replicas for the dashboards with primary pinning after writes, and plan the large tenant's move if its write share keeps growing. Sharding waits until writes or data size outgrow one primary after all of that.

**What would change it.** Write-dominated load, a dataset that no longer fits one node, or a data-residency requirement for the large tenant, which would put it in its own database whatever the load.
</details>

## Check at work

**Inspect.** Pick one write path that changes a balance, a stock level or a status. What prevents a lost update: an atomic statement, a concurrency token, a lock, or nothing? Does the code retry deadlocks (1205 in SQL Server, 40P01 in PostgreSQL) and serialization failures (40001), and does the retry restart the whole transaction? For the cache: are TTLs jittered, are keys versioned, what is `maxmemory-policy`, and what is the hit rate? For migrations: do they run at startup or in a pipeline step, and does the history show the last breaking change as expand, then contract?

**Measure.** The five statements with the highest total time, from `pg_stat_statements` or Query Store, and for each the estimated against actual rows on its costliest node. If you use replicas, the replication lag at peak.

## Sources & Further Reading

- **Martin Kleppmann, *Designing Data-Intensive Applications*** — the definitive treatment of replication, partitioning, transactions, consistency models, and stream processing. Chapters 5, 6, and 7 map directly onto much of this chapter.
- **Microsoft Learn — EF Core documentation**, especially "Global Query Filters," "Connection Resiliency," and the "Multi-tenancy" guidance (learn.microsoft.com/ef/core).
- **Microsoft Learn / Azure Architecture Center — Multitenant SaaS patterns**, covering the shared-schema, schema-per-tenant, and database-per-tenant models and tenancy trade-offs (learn.microsoft.com/azure/architecture/guide/multitenant).
- **PostgreSQL documentation** — "High Availability, Load Balancing, and Replication," "Logical Replication," "Row Security Policies," and "Building Indexes Concurrently" (postgresql.org/docs).
- **Debezium documentation** — CDC connectors, the Postgres logical decoding connector, and the "Outbox Event Router" (debezium.io/documentation).
- **PgBouncer documentation** — connection pooling modes and their constraints (pgbouncer.org).
- **Npgsql documentation** — connection pooling and configuration for .NET (npgsql.org/doc).
- **Chris Richardson, *Microservices Patterns*** and microservices.io — the Transactional Outbox, Saga, and CQRS patterns in depth.
