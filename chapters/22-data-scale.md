# Chapter 22: Data at Scale & Multi-Tenancy

_⏱️ Estimated read time: ~24 min ·     4325 words (study pace)_

For most of a system's life, a single well-tuned database is enough. You add indexes, you cache the hot paths, you buy a bigger machine, and the graphs stay green. Then one day they don't. The write-ahead log can't flush fast enough, a nightly report locks a table that customers need, connections pile up faster than the pool can hand them out, and your one biggest customer's traffic starts starving everyone else. Scaling data is the art of pushing that day as far into the future as possible, and knowing what to do when it finally arrives.

This chapter is about two related pressures. The first is raw **scale**: more data and more traffic than one node can comfortably serve. The second is **multi-tenancy**: serving many independent customers from shared infrastructure without letting them see, slow down, or corrupt each other. Both problems ultimately come down to the same question — *where does the data live, and who is allowed to touch it?*

## Vertical vs. Horizontal Scaling

There are only two directions you can scale, and the choice colors every decision that follows.

**Vertical scaling** (scaling *up*) means giving one machine more resources: more CPU cores, more RAM, faster NVMe disks. It is gloriously simple. Your application doesn't change, your transactions stay ACID, your joins still work. The ceiling is real, though — the biggest cloud database instance is finite, and the price curve is exponential. Doubling from a medium box to a large one is cheap; doubling from the largest box to *twice that* is impossible at any price.

**Horizontal scaling** (scaling *out*) means adding more machines and spreading the load across them. The ceiling is effectively unbounded, but you pay for it in complexity: data now lives in more than one place, and the comforting guarantees of a single node — a global transaction, a cheap join, a unique index — start to fray.

> **Best practice:** Scale *up* until it genuinely hurts before you scale *out*. A single Postgres instance on modern hardware can serve enormous workloads. Horizontal scaling is a one-way door that permanently raises your operational complexity; walk through it deliberately, not reflexively.

The rest of this chapter is essentially a tour of horizontal-scaling techniques, ordered roughly from least to most invasive.

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

EF Core has no native primary/replica awareness, but the pattern is straightforward: give your `DbContext` two connection strings and choose one based on intent.

```csharp
public sealed class CatalogContext : DbContext
{
    private readonly string _writeConn;
    private readonly string _readConn;
    private bool _readOnly;

    public CatalogContext(IConfiguration config)
    {
        _writeConn = config.GetConnectionString("Primary")!;
        _readConn  = config.GetConnectionString("Replica")!;
    }

    // Call before a read-only query to opt into the replica.
    public CatalogContext AsReadOnly()
    {
        _readOnly = true;
        return this;
    }

    protected override void OnConfiguring(DbContextOptionsBuilder options)
    {
        var conn = _readOnly ? _readConn : _writeConn;
        options.UseNpgsql(conn);

        // A replica connection should never accidentally issue writes.
        if (_readOnly)
            options.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
    }
}
```

In practice you'd wrap this more cleanly — for example, a factory that returns a read-optimized context, or an interceptor that inspects whether the query tree contains writes. The key discipline is that **any `SaveChanges` must go to the primary**, and read-only queries that can tolerate lag go to a replica. Marking replica contexts `NoTracking` is doubly useful: it's faster, and it structurally prevents a replica context from ever trying to save.

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

**Distributed transactions.** A single ACID transaction spanning two shards requires a protocol like two-phase commit (2PC), which is slow, locks resources across nodes, and stalls entirely if the coordinator dies mid-commit. In practice, most large systems **refuse** distributed transactions and instead embrace eventual consistency: each shard commits locally, and cross-shard consistency is reconciled asynchronously via patterns like the **Saga** (a sequence of local transactions with compensating actions on failure — see the chapter on distributed systems).

> **Pitfall:** Teams often shard to solve a performance problem and discover they've traded a *throughput* problem for a *correctness and complexity* problem. The uniform, transactional, joinable world of a single database is a luxury you don't appreciate until it's gone.

### Resharding

Your first shard layout will be wrong, because your data will grow unevenly. **Resharding** — moving data between shards to rebalance — is one of the hardest operations in data engineering, because it must happen *while the system is live.*

The technique that makes this bearable is **consistent hashing** combined with many small **virtual shards.** Instead of mapping keys directly to 4 physical servers, map them to (say) 256 virtual shards, then map those virtual shards to physical servers. To add capacity, you move some virtual shards to a new server — only a fraction of the data moves, and the mapping change is a metadata update, not a full reshuffle. Provisioning far more logical shards than you currently need ("over-sharding") is cheap insurance: you can spread them across more hardware later without ever re-hashing keys.

## Change Data Capture (CDC)

Sharded or not, large systems rarely keep all their data in one place. The catalog lives in Postgres, search lives in Elasticsearch, the recommendation engine wants a stream of events, and analytics wants everything in a warehouse. The naive approach — have the application write to all of them — is a distributed-transaction nightmare: what happens when the Postgres write succeeds but the Elasticsearch write fails?

**Change Data Capture** solves this by treating the database's own change log as a source of truth. Every committed change (insert, update, delete) is captured and streamed to downstream consumers. Because it reads the write-ahead log *after commit*, a consumer sees exactly what was durably committed — no dual-write inconsistency.

**Debezium** is the de facto open-source CDC platform. It plugs into a database's replication stream (Postgres logical replication, MySQL binlog, etc.) and publishes each change as an event to **Kafka**. Downstream, one consumer updates the search index, another updates a cache, another feeds analytics — all decoupled from the application, all fed from the same ordered stream.

### Transactional Outbox vs. CDC

A common goal is: *"when I save an order, reliably publish an OrderCreated event."* Two patterns address this.

The **transactional outbox** writes the business change and an event row to an `outbox` table **in the same local transaction.** Because both are in one ACID transaction, they commit or fail together — no dual-write problem. A separate relay process then reads the outbox and publishes the events.

```sql
BEGIN;
INSERT INTO orders (id, customer_id, total) VALUES (...);
INSERT INTO outbox (id, aggregate_type, event_type, payload)
    VALUES (gen_random_uuid(), 'Order', 'OrderCreated', '{...}'::jsonb);
COMMIT;
```

The relay can poll the outbox table — or, elegantly, **CDC can read the outbox table** and stream its rows to Kafka, giving you low-latency publishing with no polling. This "outbox + Debezium" combination is a widely used, robust pattern.

So how do outbox and CDC relate? The outbox is *your application deliberately writing events you designed*; CDC is *infrastructure capturing raw row changes*. Use the **outbox** when you want clean, intentional domain events with a stable contract. Use **raw CDC** when you want to replicate or react to table changes without touching the application — for example, feeding a data warehouse. They compose beautifully: CDC is often the *transport* for outbox rows.

> **Best practice:** Never dual-write to a database and a message broker in application code hoping both succeed. Use the outbox pattern so the event and the state change share one transaction, then ship the events with CDC or a relay.

## Migrations at Scale (Zero-Downtime)

On a small system you run a migration, take thirty seconds of downtime, and move on. At scale that's unacceptable — and worse, some migrations lock large tables for minutes and hold up every request. The goal is **zero-downtime schema evolution**, and the governing technique is the **expand/contract** (also called parallel-change) pattern.

The insight: **you cannot deploy the schema change and the code that needs it at the same instant**, because during a rolling deploy, old and new code run simultaneously against the same database. So you split every breaking change into backward-compatible steps:

1. **Expand.** Add the new structure without removing the old. Add a nullable column, a new table, a new index (built `CONCURRENTLY` in Postgres so it doesn't lock writes). Old code ignores it; new code can start using it. This step is safe because it breaks nothing.
2. **Migrate & dual-write.** Deploy code that writes to *both* old and new structures and backfills existing rows in batches. Now the data is consistent under both shapes.
3. **Contract.** Once all code reads and writes the new shape and no old code remains, drop the old column/table in a later release.

Consider renaming a `Name` column to `FullName` — a one-liner that, done naively, breaks every running instance of the old code the moment it deploys. Expand/contract turns it into: add `FullName` (expand) → deploy code writing both, backfill (migrate) → deploy code using only `FullName` → drop `Name` (contract). Each step is independently deployable and reversible.

> **Pitfall:** `ALTER TABLE` operations that rewrite a table or take an `ACCESS EXCLUSIVE` lock will block all reads and writes for the duration. On a large table under load this is an outage. Always check whether an operation is lock-free; add indexes `CONCURRENTLY`; add columns *without* a volatile default (modern Postgres makes adding a column with a constant default cheap, but backfilling is not).

**Feature flags** pair naturally with this. Deploy new code dark (flag off), flip the flag for 1% of traffic, watch the metrics, then ramp to 100%. If something breaks, you flip the flag off — no rollback, no redeploy. This decouples *deploying* code from *releasing* behavior, which is exactly what you want when the schema underneath is mid-transition.

## Connection Management Under Load

A subtle scaling wall has nothing to do with data size: **connections.** Each Postgres connection is a backend process consuming several megabytes of RAM, and the server performs best with a *small* number of active connections — often just a few dozen. But a fleet of application servers, each with its own connection pool, can easily demand thousands.

Two layers of pooling save you:

- **Application-level pooling.** ADO.NET / Npgsql pool connections per process, reusing them across requests instead of opening a new one each time (opening a Postgres connection is expensive — a TCP handshake plus a process fork). This is on by default; the trap is *misconfiguring the max pool size* so that a slow query storm exhausts it and requests queue.
- **An external pooler like PgBouncer.** This sits between your app fleet and Postgres and multiplexes thousands of client connections onto a small pool of real database connections. In **transaction pooling** mode, a real connection is only held for the duration of a transaction, so hundreds of mostly-idle clients share a handful of backends. Serverless and autoscaling architectures — where instance count balloons unpredictably — essentially *require* a pooler to avoid overwhelming the database.

> **Pitfall:** PgBouncer's transaction-pooling mode breaks anything that relies on session state spanning multiple statements — session-level `SET`, prepared statements, `LISTEN/NOTIFY`, advisory locks held across statements. Know your pooling mode and its constraints before you deploy it.

## Polyglot Persistence, CQRS Read Stores, and Caching

No single database is good at everything. **Polyglot persistence** means using the right store for each job: Postgres for transactional integrity, Elasticsearch for full-text search, Redis for ephemeral session data, a columnar warehouse for analytics, a graph database for relationship queries. CDC is the glue that keeps these stores in sync from a single source of truth.

This connects directly to **CQRS** (Command Query Responsibility Segregation). Instead of forcing one schema to serve both writes and reads, you split them: writes go to a normalized transactional model; reads are served from one or more **read stores** shaped exactly for how they're queried — pre-joined, denormalized, indexed for the specific screen. The read store is kept up to date asynchronously (often via the same event/CDC stream). You accept eventual consistency in exchange for read models that are fast and don't compete with writes for resources.

And underpinning all of it, recall the **caching layers** from earlier chapters: an in-process cache for the hottest tiny data, a distributed cache (Redis) shared across the fleet, and HTTP/CDN caching at the edge. Caching is the cheapest scaling technique of all — a cache hit is a query that never touches your database. The eternal caveat is invalidation: a cache is a bet that stale data is acceptable for its TTL, and CDC can also drive precise cache invalidation by streaming the exact rows that changed.

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

**Migrations** get harder as isolation increases. Shared schema: one migration, done. Schema- or database-per-tenant: the *same* migration must run against every tenant's schema/database, which means orchestration (a loop over tenants, with retry and progress tracking), the risk of partial rollout (some tenants migrated, some not — so your code must tolerate both shapes, exactly the expand/contract discipline from earlier), and a real time cost when you have thousands of tenants.

**Noisy neighbors** are the flip side of density. In shared-schema, one tenant running a giant report or a runaway query consumes CPU, I/O, and connections that everyone else needs, and everyone's latency suffers. Mitigations range from soft (per-tenant rate limits, query timeouts, separate connection pools for heavy operations) to hard (move the offender to a dedicated database — the isolation model itself is the ultimate noisy-neighbor fix). This is precisely why big-spending tenants often get their own database: they're paying to *not* share.

**Per-tenant scaling and cost** finally tie the two halves of this chapter together. Sharding and multi-tenancy converge: when you shard a multi-tenant system by `TenantId`, each shard is essentially a group of tenants, and moving a hot tenant to its own shard *is* resharding. The economics are the core of the SaaS business model — density (many tenants per resource) drives your gross margin, while isolation drives your ability to serve enterprise and regulated customers. The senior engineer's job is to place each tenant at the right point on that spectrum: pack the small ones tightly, isolate the large and sensitive ones, and build the tooling to move a tenant from one model to the other as they grow.

## Sources & Further Reading

- **Martin Kleppmann, *Designing Data-Intensive Applications*** — the definitive treatment of replication, partitioning, transactions, consistency models, and stream processing. Chapters 5, 6, and 7 map directly onto much of this chapter.
- **Microsoft Learn — EF Core documentation**, especially "Global Query Filters," "Connection Resiliency," and the "Multi-tenancy" guidance (learn.microsoft.com/ef/core).
- **Microsoft Learn / Azure Architecture Center — Multitenant SaaS patterns**, covering the shared-schema, schema-per-tenant, and database-per-tenant models and tenancy trade-offs (learn.microsoft.com/azure/architecture/guide/multitenant).
- **PostgreSQL documentation** — "High Availability, Load Balancing, and Replication," "Logical Replication," "Row Security Policies," and "Building Indexes Concurrently" (postgresql.org/docs).
- **Debezium documentation** — CDC connectors, the Postgres logical decoding connector, and the "Outbox Event Router" (debezium.io/documentation).
- **PgBouncer documentation** — connection pooling modes and their constraints (pgbouncer.org).
- **Npgsql documentation** — connection pooling and configuration for .NET (npgsql.org/doc).
- **Chris Richardson, *Microservices Patterns*** and microservices.io — the Transactional Outbox, Saga, and CQRS patterns in depth.
