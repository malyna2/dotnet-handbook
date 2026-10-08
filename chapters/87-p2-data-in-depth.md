# Part 2 · Module 2: Data in Depth

> **What this module makes you able to do.** Choose the isolation, the index, the cache and the migration path for data that many requests, many instances and several releases share, and name the failure each choice prevents and the cost it adds.

**Time:** reading ≈ 1 h 5 min; hands-on ≈ 7 h 40 min — the rest of Lab 37 6 h, the isolation experiment 20 min, the questions 20, the decision 15, the check at work 45.

## Covers

- which anomalies each isolation level allows, including the lost update and write skew, what PostgreSQL and SQL Server really do at each level, and why deadlocks need a retry of the whole transaction;
- reading a plan in depth: statistics, estimated against actual rows, generic plans and parameter sniffing, and why a plan regresses without a deploy;
- set-based writes and bulk loads, and everything they bypass;
- caching: cache-aside, the stampede, invalidation, Redis key design and eviction;
- schema changes across releases: migrations in the pipeline, expand and contract, and the same rules for messages and documents;
- data at scale: replicas and their lag, partitioning and sharding, multi-tenancy.

## The mechanism to explain without notes

**Shared data is read by many actors at once and across time — concurrent transactions, a cached copy, a replica, the previous release — and every technique here decides which of them may see a stale or partial state, for how long, and at what cost.**

- **Inside one database, the isolation level picks the interleavings you are protected from.** Chapter 4's table lists dirty, non-repeatable and phantom reads. Two more decide real designs:
  - *the lost update*: two transactions read a value, both write a new one computed from it, and the first write vanishes. Read Committed allows it, because each read saw committed data;
  - *write skew*: two transactions read an overlapping set and each writes a different row ("at least one doctor stays on call"). Only Serializable prevents it.

  PostgreSQL implements Repeatable Read as a snapshot: no phantoms, but write skew is possible, and an update of a row another transaction changed fails with `could not serialize access` (SQLSTATE 40001). Its Serializable level detects the dangerous patterns and aborts one transaction with the same code. SQL Server's Read Committed takes shared locks unless the database has `READ_COMMITTED_SNAPSHOT` on, and its Repeatable Read holds them to the end of the transaction. A stricter level therefore doesn't remove failures; it turns silent anomalies into errors your code must retry, from the first read.
- **The planner decides from statistics, not from your data.** It picks a plan by estimated rows. The estimate comes from statistics sampled at the last analyze and from the parameter values the plan was built for. When either is wrong, the plan is wrong without anything in your code changing: Chapter 37's stale-statistics script plans for 23 rows that are really 306,526.
- **A cache is a replica you manage by hand.** Cache-aside fills it on a miss. Staleness is bounded by the TTL or by eviction on write, and a stampede is many misses rebuilding the same key at once.
- **During a rolling deploy, two releases share one schema and one message stream.** Every schema change must work with the code before it and the code after it, so a breaking change becomes expand, migrate, contract. Message and document contracts follow the same rule: add optional fields, ignore unknown ones (the tolerant reader), never reuse a field's identity.
- **Scaling out moves the stale reader into the infrastructure.** A replica serves reads that lag the primary, so read-your-writes breaks. A shard key decides which queries stay on one node. A tenant's isolation model decides who can slow down or see whom.

> **Pay attention.** **The lost update survives Read Committed, and a stricter level answers with errors, not correctness.**
>
> - **The mechanism.** Two requests load stock 10, both compute 9, both save. Each read saw committed data, so Read Committed has nothing to stop.
> - **What Repeatable Read does instead.** On PostgreSQL, the second update fails with SQLSTATE 40001. On SQL Server, both transactions hold shared locks and both ask to convert them to exclusive ones: a deadlock, and one of them is the victim (error 1205). Either way you need a retry loop around the whole transaction.
> - **The fix in the code.** Make the read and the write one step: `ExecuteUpdate` with `SET Stock = Stock - 1 WHERE Id = @id AND Stock >= 1`, checking the rows affected; or an optimistic concurrency token with a retry on `DbUpdateConcurrencyException`; or `SELECT … FOR UPDATE` for a short, contended path.

> **Pay attention.** **Why a query gets slow with no deploy.** A plan is chosen at run time and often reused, so three things change it under you:
>
> - **Statistics went stale.** Autovacuum analyzes after 10% of a table changes by default, so a 6% bulk load leaves the planner working from the old distribution (Chapter 37's *Break it*).
> - **The cached plan was built for another value.** PostgreSQL gives a prepared statement five custom plans, then may switch to a generic one (`$1` in the plan). SQL Server compiles a procedure's plan for its first caller's values, and recompiles after a restart, a failover or a statistics update, so "the first caller" can change overnight.
> - **The data changed shape.** One customer becomes a marketplace seller; the old plan was right for the old distribution.
>
> Compare estimated with actual rows on the node that dominates, then remove the choice: an index that is right for every value (Chapter 37's rung 9 and SQL Server script 3). `force_custom_plan`, `OPTION (RECOMPILE)` and a Query Store forced plan are stopgaps.

## Read (≈ 1 h 5 min)

1. [Chapter 4: Isolation Levels](#isolation-levels), [Deadlocks](#deadlocks) and [Concurrency: Optimistic vs Pessimistic](#concurrency-optimistic-vs-pessimistic): the anomalies, the victim, and the two cures for a lost update.
2. [Chapter 4: PostgreSQL in Practice: Indexes and Query Plans](#postgresql-in-practice-indexes-and-query-plans): MVCC and the visibility map, estimated against actual rows, *When the Plan Is Wrong*, and Npgsql's automatic preparation.
3. [Chapter 4: Set-Based Updates and Deletes](#set-based-updates-and-deletes-executeupdate-and-executedelete) and [Bulk Inserts and the Limits of SaveChanges](#bulk-inserts-and-the-limits-of-savechanges): what each path bypasses, and the table of when to use which.
4. [Chapter 4: Caching](#caching): cache-aside, stampedes, HybridCache, Redis key versions, TTL jitter and eviction policy.
5. [Chapter 4: Migrations in CI/CD](#migrations-in-cicd) and [Chapter 23: Migrations at Scale (Zero-Downtime)](#migrations-at-scale-zero-downtime): where migrations run, and expand and contract.
6. [Chapter 24: The Core Topic: Schema and Contract Evolution](#the-core-topic-schema-and-contract-evolution), [Versioning Events and Messages](#versioning-events-and-messages) and [A Concrete Example: Evolving an Order Event Safely](#a-concrete-example-evolving-an-order-event-safely): compatibility directions, the change matrix, upcasting.
7. [Chapter 23: Scaling Reads with Replication](#scaling-reads-with-replication), [Partitioning and Sharding](#partitioning-and-sharding) and [Multi-Tenancy](#multi-tenancy): lag, the shard key, the three isolation models.
8. Chapter 37's later levels: [Level 2](#level-2-the-index-decides-the-plan-rungs-59), [Level 3](#level-3-beyond-the-harness), and the *Break it* scripts [Stale statistics](#stale-statistics), [The visibility map](#the-visibility-map), [Keyset pagination, written the other way](#keyset-pagination-written-the-other-way) and [A misestimate that doesn't matter](#a-misestimate-that-doesnt-matter).

## Practice

**1. Lab 37 in full ([`labs/37-execution-plans`](https://github.com/malyna2/dotnet-handbook/tree/main/labs/37-execution-plans)).** If you did Part 1's subset (setup, Level 1, rung 6 and SQL Server script 1), the rest takes about 6 h: rungs 5 and 7–9, Level 3 (the server's view, `auto_explain`, pricing `force_custom_plan` against an index, the SQL Server track), *Break it* and the write-up. From scratch, the chapter's time budget adds up to 9 h 30 min – 10 h 30 min. You need Docker, about 6 GB of free RAM (8 GB with SQL Server) and the .NET 10 SDK.

```bash
cd labs/37-execution-plans
docker compose up -d --wait
./seed.sh medium                                      # about 4 minutes
dotnet run --project src/QueryLab.Cli -- 9            # one rung, with its plan
docker compose exec -T postgres psql -U lab -d shop -f /lab/break-it-stale-statistics.sql
docker compose --profile sqlserver up -d && ./sqlserver.sh seed
```

For every rung: predict the plan, run it, name the mechanism, apply the smallest fix, and price it (build time, size, the cost on every insert). Only then compare with the *Hints and answers* at the end of [Chapter 37](#chapter-37-the-slow-query-lab-reading-execution-plans).

**2. The lost update, live (20 min).** In the lab's PostgreSQL, open two sessions (`docker compose exec postgres psql -U lab -d shop`) and create a scratch table with one row holding a balance of 100. In both sessions: begin, read the balance, then write back the value you read plus 10, and commit. Under the default Read Committed, the final balance is 110: one write was lost. Repeat with `BEGIN ISOLATION LEVEL REPEATABLE READ`: the second session's update fails with `could not serialize access due to concurrent update`. Then do it once more with `UPDATE … SET balance = balance + 10` and no read at all, which gives 120 under either level. Drop the table when you are done.

**Evidence to keep**, in your own public portfolio repo, not in this one: `RESULTS.md` with its environment header, the before and after plans, the price of every index, the rung 9 trade-off paragraph and the SQL Server comparison table, plus the three isolation transcripts.

Later, if you need it: [Dapper: When the ORM Is Too Much](#dapper-when-the-orm-is-too-much), [Chapter 23: Connection Management Under Load](#connection-management-under-load), and [Chapter 50: Choosing the partition key](#choosing-the-partition-key-the-decision-you-cannot-easily-undo) for the same decision in Cosmos DB.

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

**First, the precondition.** 80% CPU can be a handful of statements. Sort `pg_stat_statements` by total time and read the top plans before buying infrastructure: Chapter 23's advice is to scale up until it genuinely hurts.

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
