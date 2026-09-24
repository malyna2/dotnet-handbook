# Chapter 37: The Slow-Query Lab — Reading Execution Plans

_⏱️ Estimated read time: ~40 min · 7059 words (study pace)_

[Chapter 4](#chapter-4-data-access-databases) explains how to read a query plan. In this lab you read nine of them, on a dataset big enough for the difference to show: a million customers, five million orders and eighteen million order lines, with the skew real data has — one customer who places 5% of all orders, one product that appears in 8% of all lines. Every slow query comes from EF Core code that would pass a code review. For each one you find the mechanism in the plan, fix it at the right layer (the LINQ, the schema or the configuration), and prove the fix by running the same measurement again.

You finish with the most convincing performance artifact there is: a table of before and after numbers, where every row links to two plans and a paragraph explaining *why*.

```
 run the rung ──► read the plan ──► name the mechanism ──► smallest fix ──► run it again
      ▲                                                                          │
      └──────── compare the work (statements, rows, buffers), not just time ◄────┘
```

> **The portfolio rule.** Nothing you produce here goes into the handbook's repository. Your fixed rungs, your before and after plans and your results table go into **your own public portfolio repo**, where an interviewer can read them. If you retell the lab as a story about a real employer's system, that version stays **private**.

The kit is at [labs/37-execution-plans](https://github.com/malyna2/dotnet-handbook/tree/main/labs/37-execution-plans). It contains:

- a Docker Compose stack running PostgreSQL 18 with `pg_stat_statements` and `auto_explain`;
- a deterministic seed;
- a .NET 10 harness that runs each query and captures the plan the server really used;
- a starter project with nine slow implementations;
- acceptance tests;
- an optional SQL Server 2025 track.

The numbers in this chapter come from the kit's `reference-runs/`. Each file there starts with the environment it was measured on: 4 vCPU, 16 GB RAM, PostgreSQL 18.6, a warm cache, the medium scale.

## Goal and the senior signal it trains

**Goal:** given a slow query, find the mechanism in its plan, choose the smallest fix at the right layer, and prove the change with numbers someone else can reproduce.

**The senior signal: explaining *why*, and pricing the fix.** Most developers can make a query faster by adding an index and watching the time drop. The answers interviewers remember go one level deeper:

- which node in the plan did the damage;
- why the planner chose that node — it had nothing better, or it was misinformed;
- what the fix costs: build time, disk, write overhead, a change users will notice;
- how you know the improvement is real and not a warm cache.

That chain — *measure → mechanism → minimal fix → proof* — is what you practise nine times here.

**A second signal: knowing that the ORM decides the SQL.** Several rungs are not database problems at all. The plan is only where the evidence shows up; the fix is a line of C#.

## Time budget

| Activity | Time |
|---|---|
| Setup: compose up, seed at the medium scale, first run | 30 min (the seed takes about 5) |
| Level 1: rungs 1–4 | 2 h |
| Level 2: rungs 5–9 | 3 h |
| Level 3: the server's view, pricing a trade-off, SQL Server | 2–3 h |
| Break it | 1 h |
| Write-up for your portfolio | 1 h |

## Setup

You need:

- Docker with Compose v2;
- about 6 GB of free RAM (8 GB with SQL Server) and about 5 GB of disk;
- the .NET 10 SDK.

The kit's README covers pull limits and port clashes.

```bash
cd labs/37-execution-plans
docker compose up -d --wait
./seed.sh medium                                      # about 5 minutes
dotnet run --project src/QueryLab.Cli -- all          # every rung: numbers and plans
dotnet run --project src/QueryLab.Cli -- 3 --no-plan  # one rung, numbers only
dotnet test --project tests/QueryLab.Tests            # the acceptance tests
```

You work in `starter/QueryLab.Rungs/`. Each rung is one class:

- **Inputs and result type are fixed.** A base class in `src/QueryLab.Core/Contracts.cs` fixes them, so you change *how* a rung gets its answer, never *what* it returns.
- **DDL goes in `Fixes`.** A rung can list DDL there, and the harness runs it before the rung.
- **Name what you create `lab_*`.** The harness drops every `lab_*` index and statistics object before each rung and after the last one. Rungs never depend on each other's fixes, and the database is back to its seeded state when a run ends.

The acceptance tests start their own PostgreSQL in a container, seed it at the small scale, and hold each rung to two checks:

1. **Correctness.** The rung returns exactly the rows a hand-written SQL reference returns.
2. **Work.** It meets a limit on statements, rows, buffers or a plan node — never milliseconds, so a test passes or fails the same way on a laptop and in CI.

A starter rung fails with a message that starts with `ACCEPTANCE:`. Any other failure means the code is broken, not just slow.

### What the harness prints, and in what order to read it

For each rung the CLI:

1. applies the rung's fixes;
2. runs the rung once to warm the cache;
3. resets `pg_stat_statements`;
4. measures five calls;
5. prints what EF Core sent, the parameter values with their types, and the plan.

The summary for rung 4 looks like this (the full line is wider):

```
fixes       : none
wall time   : median 488.2 ms over 5 calls (min 458.7, max 547.6)
per call    : 1 statement(s), 1 rows returned,
              shared buffers 6,741 hit + 42,702 read, 501.6 ms server execution
```

Read it in this order:

1. **`per call` first.** Statements, rows returned and buffers measure the *work*, and they should match ours closely on any machine; the milliseconds will not.
   - A buffer is one 8 kB page.
   - `hit` means the page was already in PostgreSQL's shared buffers.
   - `read` means it was not. It may still have come from the operating system's page cache, so `read` does not always mean the disk.
2. **Then the plan, inside out**, as in [Reading EXPLAIN](#reading-explain): estimated against actual rows, `loops`, buffers per node.
3. **Wall time against server time.**
   - Wall time is measured in .NET around the whole call.
   - Server time is what `pg_stat_statements` recorded.
   - The gap is round trips, the network and EF Core turning rows into objects. On one rung that gap is the whole problem.

PostgreSQL 18 changed three things in the output that older articles won't show you:

- **Buffers by default.** `EXPLAIN ANALYZE` prints buffer counts without being asked for `BUFFERS`.
- **Two-decimal `rows=`.** `rows=` is now printed with two decimals, because it is an average per loop. `rows=72421.50 loops=2` means two parallel processes averaged 72,421.5 rows each.
- **`Index Searches:`** is a new line that counts how many times an index was descended, in total across loops. It is 1 for an ordinary scan, one per outer row on the inner side of a nested loop, and more than 1 when PostgreSQL 18's new skip scan jumps between values of a leading column.

## Tasks

The rung titles describe the feature, not the bug; finding the bug is the exercise. Every rung has an acceptance criterion and a *look first at* pointer. Check your reasoning against *Hints and answers* only once your fix passes.

### Level 1 — The code decides the SQL (rungs 1–4)

| Rung | Scenario | Passes when |
|---|---|---|
| 1 | The order history page: key account 2's latest 50 orders, each with its lines. | at most 2 statements per call |
| 2 | The quarterly delivery report: key account 2's shipped orders from the last 90 days, with their lines and shipments. | the server returns no more rows than there are orders, lines and shipments |
| 3 | Signing in: find the customer for the email typed at sign-in, whatever its case. | no `Seq Scan` on `customers` |
| 4 | The partner webhook: a delivery partner calls back with an order number, and the handler loads the order. | no `Seq Scan` on `orders` |

Look first at:

- **Rung 1:** the statement count, not the plan.
- **Rung 2:** the `EF warning` line and the rows returned.
- **Rung 3:** the `Filter` line.
- **Rung 4:** the SQL EF Core generated, character by character.

Done when:

- the acceptance tests pass for rungs 1–4;
- a `RESULTS.md` in your portfolio repo has one row per rung: the before and after wall time, statements, rows and buffers, and one sentence naming the mechanism;
- the before and after plans for each rung are saved as text next to it.

### Level 2 — The index decides the plan (rungs 5–9)

| Rung | Scenario | Passes when |
|---|---|---|
| 5 | Product sales figures: lines, units and revenue for product 50 across all orders. | an `Index Only Scan` on `order_lines` |
| 6 | The support screen: customer 33's orders from the last 365 days, newest first. | at most 50 buffers per call |
| 7 | The admin order list, page 4,001 at 50 per page. The request also carries the last row of page 4,000. | at most 100 buffers per call |
| 8 | Customer search: emails containing "tomasz.kowalski", ignoring case. | no `Seq Scan` on `customers` |
| 9 | The "latest 20 sales" panel, with Npgsql automatic preparation on. Traffic warms it up with twelve popular products, then someone opens the bestseller. | at most 100 buffers per call |

Look first at:

- **Rung 5:** `Heap Blocks` against rows.
- **Rung 6:** which column the index starts with.
- **Rung 7:** `rows=` on the node under `Limit`.
- **Rung 8:** `Rows Removed by Filter`, then the `ESCAPE` clause in the SQL.
- **Rung 9:** the `parameters` line (generic plans against custom plans), `$1` in the plan, and estimated against actual rows.

Done when:

- all nine acceptance tests pass;
- for every index you added, `RESULTS.md` records its build time and size (the CLI prints both) and one sentence on what it costs every insert;
- for rung 9, a paragraph explains why the problem appears only after warm-up.

### Level 3 — Beyond the harness

1. **See N+1 from the server's side.** Run the starter's rung 1 on its own (`-- 1 --no-plan`). Then query `pg_stat_statements` yourself for calls, rows, total time and buffers per statement. What does the ratio between the entries tell you? Why can no single plan show this problem?
2. **Catch rung 9 in the act.** The compose file loads `auto_explain` with a one-second threshold. Find the bestseller's executions in `docker compose logs postgres`, and prove from the log alone that the server ran a generic plan.
3. **Price the alternative.** Fix rung 9 *without* an index: run the starter with `plan_cache_mode` set through the connection string (the command is below). Then write down what this fix costs compared with the index.
4. **The SQL Server track.** Run `docker compose --profile sqlserver up -d` and `./sqlserver.sh seed`, then scripts 1 to 4. For each script, name the PostgreSQL rung it mirrors and where SQL Server behaves differently.

The command for task 3:

```bash
dotnet run --project src/QueryLab.Cli -- 9 --connection \
  "Host=localhost;Port=5433;Username=lab;Password=lab;Database=shop;Options=-c plan_cache_mode=force_custom_plan"
```

Done when `RESULTS.md` has:

- a comparison table: script, PostgreSQL counterpart, what differs, logical reads before and after;
- for tasks 1–3, the query result or log excerpt that proves each answer.

## Break it

The scripts in `postgres/` break something on purpose and put the database back afterwards. Run one with:

```bash
docker compose exec -T postgres psql -U lab -d shop -f /lab/break-it-stale-statistics.sql
```

### Stale statistics

In `break-it-stale-statistics.sql`, customer 900,001, a consumer account with three orders, becomes a marketplace seller and bulk-loads 300,000 orders. The load runs inside a transaction that is rolled back at the end.

| | Planner estimate for the new customer | Plan | Work |
|---|---|---|---|
| Before `ANALYZE` | 23 rows | Uses a plain `customer_id` index, fetches all 300,003 rows and sorts them to return 50 | 3,048 buffers, 80.5 ms |
| After `ANALYZE` | 306,526 rows | Walks the `created_at` index backwards until 50 rows match | 12 buffers, 0.1 ms |

Before you run it, predict the estimate. Afterwards, explain where 23 came from. The script prints what the planner knew.

### The visibility map

`break-it-visibility-map.sql` builds rung 5's covering index, then deletes every line of product 50 and rolls the delete back. No row has changed, yet:

| State | Heap Fetches | Buffers | Time |
|---|---|---|---|
| Visibility map up to date | 0 | 101 | 5.0 ms |
| After the rolled-back `DELETE` | 18,015 | 17,091 | 60.4 ms |
| After `VACUUM` | 0 | 98 | 7.6 ms |

Explain the middle row. What does this mean for index-only scans on a table that is written to all day?

> **Gotcha.** `ROLLBACK` is not undo. Rolled-back rows remain as dead tuples in the table and in every index until `VACUUM` removes them. The pages that the inserts split in an index stay split even after that. Both scripts clean up for this reason. Before they did, a rolled-back bulk load left rungs 6 and 7 doing measurably more work until autovacuum came round. On a shared staging database, the next person's measurements would pay for your experiment.

### Keyset pagination, written the other way

`sidebar-keyset-forms.sql` fetches rung 7's page four ways. All four return the same 50 rows.

| Form | Buffers | Time |
|---|---|---|
| `OFFSET 200000 LIMIT 50` | 2,791 | 68.2 ms |
| Keyset, OR form: `created_at < x OR (created_at = x AND id < y)` | 2,791 | 23.0 ms |
| Keyset, row value: `(created_at, id) < (x, y)` | 5 | 0.08 ms |
| Row value, with an index on `(created_at, id)` | 5 | 0.08 ms |

The OR form is correct keyset pagination, yet it does as much work as `OFFSET`. Find the line in its plan that explains why. Then say what the fourth plan gains over the third, since they read the same number of buffers.

### A misestimate that doesn't matter

`sidebar-correlated-columns.sql` counts last quarter's orders for customers in Berlin. The planner multiplies the selectivities of `country = 'DE'` and `city = 'Berlin'` as if they were independent, so it estimates **15,333** rows where there are **79,941** — about five times too few. `CREATE STATISTICS … (dependencies, mcv)` fixes the estimate (80,967).

The plan does not change: the same parallel hash join over the same scans, reading the same 15,369 buffers. Execution time goes from 127.6 ms to 95.9 ms, and it is tempting to credit the statistics. But the first run read 72 pages that the second found in cache, and the plan was identical. Better statistics cannot speed up a plan they did not change.

Two lessons:

- **A misestimate matters only when it changes a decision.** Look for a different plan, not a different number.
- **Compare warm runs with warm runs, and repeat them,** before you credit a change with a speed-up.

## Evidence to keep

| Artifact | Where | Visibility |
|---|---|---|
| `RESULTS.md`: rung, mechanism, fix, before and after wall time, statements, rows and buffers, and the fix's build time and size | Portfolio repo | Public |
| The environment header of your runs (CPU, RAM, versions, scale, cache state), at the top of `RESULTS.md` | Portfolio repo | Public |
| The before and after plan of every rung, as text | Portfolio repo, `plans/` | Public |
| Your rung implementations and `Fixes` | Portfolio repo | Public |
| Level 3: the `pg_stat_statements` and `auto_explain` excerpts, the rung 9 trade-off, the SQL Server comparison table | Portfolio repo | Public |
| A STAR worksheet built from the interview hook below | Story bank | Private |

## Interview hook

"A page got slow — what do you do?" comes up in almost every senior .NET loop, in one form or another. [Chapter 34](#chapter-34-interview-questions-how-to-answer-them) covers the general method; this lab gives you nine concrete cases and a story. Be open about where it comes from: "In a practice lab on a five-million-order dataset…" is a strong opening, and passing a lab off as production is not (the honesty rules are in [Chapter 36](#chapter-36-the-story-bank-evidence-portfolio)).

**"Tell me about a query you made faster"** — as STAR, built on rung 9, because it has the best follow-ups:

- **S:** A product page's "latest sales" panel over an 18-million-row table was fast in testing. In the lab it took about a second for the bestseller, but only after the service had been running for a while. [Or your real-work version: the endpoint, the table size, how it was noticed.]
- **T:** Find out why it happened only after warm-up, and fix it without slowing other products down.
- **A:**
  - Caught the slow execution in the `auto_explain` log. Its plan had `$1` in the index condition — a generic plan.
  - Traced the cause: Npgsql's automatic preparation had turned the query into a server-side prepared statement. After five custom plans, PostgreSQL switched to a generic plan costed for an average product (463 rows estimated); the bestseller has 1.44 million.
  - Priced two fixes:
    - forcing custom plans: 7 buffers, but a planning cost on every execution of every statement on those connections;
    - an index on `(product_id, id)` that makes one plan right for every product: 7 buffers, 11 seconds to build, 542 MB on disk.
  - Chose the index.
- **R:** In the lab, from 151,331 buffers and about a second to 7 buffers and 1.5 ms for the bestseller, with no change for other products. An acceptance test guards it. [Your real-work version: p95 before and after, from your APM.]

Three likely follow-ups:

1. **"Why only after warm-up?"** PostgreSQL's plan-cache rule. A prepared statement gets five custom plans; after that, the server uses a generic plan whenever its estimated cost is not much worse than the custom plans' average. Warm-up traffic with popular products made the custom plans look expensive, so the generic plan won.
2. **"Why not just set `force_custom_plan`?"** It trades a one-off cost (an index) for a recurring one: planning on every execution of every statement on those connections. It also hides the next skew problem instead of removing this one. It is a good stopgap, or a per-role setting for a workload you know is skewed.
3. **"What does the index cost on writes?"** Every insert into `order_lines` maintains one more index. Measure insert throughput before and after. The old `product_id` index is now redundant (it is the new index's leading column), so drop it once `pg_stat_user_indexes` confirms nothing else uses it. Then the number of indexes each insert updates is back where it started, though the index you kept is larger.

## Hints and answers

<details>
<summary>Rung 1 — the order history page</summary>

The plans are fine. The first statement reads 81 buffers, and each lines statement reads 4 and runs in 0.04 ms. The problem is how many statements there are: **51 per call**, one for the orders and then one per order for its lines. The starter calls a helper, `LinesForOrderAsync`, in a loop. The server did 2.0 ms of work per call, while the page took a median of 78.0 ms. Almost all of the time went on 51 round trips and 51 rounds of EF Core turning results into objects.

The fix is one projection that includes the nested collection:

```csharp
.Select(o => new OrderHistoryRow(o.Id, o.CreatedAt, o.Status, o.Total,
    o.Lines.OrderBy(l => l.Id)
           .Select(l => new OrderLineRow(l.ProductId, l.Quantity, l.UnitPrice))
           .ToList()))
```

EF Core turns it into a `LEFT JOIN` against a subquery that picks the 50 orders first. The result is 1 statement, the same 291 buffers, and 9.1 ms. `AsSplitQuery()` also passes: two statements. The database's work did not change at all; the round trips did. That is why N+1 is invisible in any single plan: the cost is in the round trips, as [Chapter 4](#the-n1-problem-seeing-it-and-killing-it) explains, and you find it by counting statements.
</details>

<details>
<summary>Rung 2 — the quarterly delivery report</summary>

The CLI prints EF Core's own warning, `MultipleCollectionIncludeWarning`. Two collection `Include`s in one query become two `LEFT JOIN`s, and every line of an order is repeated once for each of its shipments:

```
Nested Loop Left Join  (actual ... rows=72421.50 loops=2)
  ->  Nested Loop Left Join  (actual ... rows=2895.00 loops=2)
        ->  Parallel Index Scan ... on orders o  (actual ... rows=725.00 loops=2)
        ->  Index Scan using ix_shipments_order_id ...  (actual ... rows=3.99 loops=1450)
  ->  Index Scan using ix_order_lines_order_id ...  (actual ... rows=25.02 loops=5790)
```

Read the `loops`:

- 1,450 orders, with about 4 shipments each, gives 5,790 order-and-shipment rows;
- each of those rows then joins about 25 lines;
- the result is 144,843 rows, although the report needs only 43,529 (orders, lines and shipments together).

The sort above the join spilled to disk: `Sort Method: external merge  Disk: 16704kB`. The server spent 425.4 ms per call.

`AsSplitQuery()` sends one statement per collection:

- 3 statements, 43,529 rows, 86.3 ms on the server;
- 308.4 ms wall time. What remains is transferring 43,529 rows and building objects from them; a projection of only the columns the file needs would cut that too.

The price of a split query is consistency. The three statements are separate snapshots, so an order can gain a shipment between them. Wrap them in a snapshot or serializable transaction if the report must be internally consistent. For an account manager's download, it rarely has to be.
</details>

<details>
<summary>Rung 3 — signing in by email</summary>

```
Seq Scan on customers c  (actual ... rows=1.00 loops=1)
  Filter: (lower(email) = 'diego.larsen54321@corp.example'::text)
  Rows Removed by Filter: 999999
  Buffers: shared hit=13659
```

`c.Email.ToLower() == email.ToLower()` becomes `lower(c.email) = lower(@email)`. A B-tree on `email` stores `email`, not `lower(email)`, so it can't answer the question, and the server reads all 13,659 pages of the table to find one row. The whole table is in cache (`hit`), and it still costs 327.0 ms per call.

Index the expression:

```sql
CREATE UNIQUE INDEX lab_customers_email_lower ON customers (lower(email));
ANALYZE customers;
```

The result is 4 buffers and 1.9 ms. The index is `UNIQUE` on purpose: "one account per address, whatever the case" is a business rule, and now the database enforces it. `ANALYZE` gathers statistics on the new expression, which the planner otherwise has to guess. It took 3.3 s to build and takes 49 MB.

The alternatives — `citext`, or a case-insensitive collation — are in [When the Plan Is Wrong](#when-the-plan-is-wrong).
</details>

<details>
<summary>Rung 4 — the partner webhook</summary>

```sql
WHERE o.id::numeric = @payload_OrderNumber      -- @payload_OrderNumber = 424242 (numeric)
```

```
Seq Scan on orders o  (actual ... rows=1.00 loops=1)
  Filter: ((id)::numeric = '424242'::numeric)
  Rows Removed by Filter: 4999999
```

The partner's DTO maps the order number to `decimal`. `o.Id == payload.OrderNumber` compiles because C# silently widens `long` to `decimal`, and EF Core translates that widening faithfully, as a cast on the *column*. A primary-key index on `bigint` cannot answer a `numeric` comparison, so a key lookup becomes a read of all 49,443 pages of `orders`.

Most of those pages are `read`, not `hit`, on every call, even warm. PostgreSQL reads a table larger than a quarter of `shared_buffers` through a small ring of buffers, so a big sequential scan never pushes everything else out of the cache. `orders` is 386 MB against 512 MB of shared buffers; `customers` in rung 3 is 107 MB, and stayed cached.

Convert — and validate — on the .NET side:

```csharp
if (payload.OrderNumber != decimal.Truncate(payload.OrderNumber)
    || payload.OrderNumber < 1 || payload.OrderNumber > long.MaxValue)
    return null;
var orderId = (long)payload.OrderNumber;
```

The result is `Index Scan using orders_pkey`, 4 buffers, 1.6 ms, from a 488.2 ms median. No index was needed; the one that already existed can be used again. SQL Server's version of this bug is script 1 of the SQL Server track.
</details>

<details>
<summary>Rung 5 — product sales figures</summary>

```
Bitmap Heap Scan on order_lines o  (actual ... rows=18015.00 loops=1)
  Heap Blocks: exact=16993
  ->  Bitmap Index Scan on ix_order_lines_product_id  (actual ... rows=18015.00 loops=1)
        Buffers: shared hit=18
```

The index finds the product's 18,015 lines in 18 buffers. `quantity` and `unit_price`, though, live in the table, and the lines are spread across it — 16,993 different pages for 18,015 rows. The answer costs 17,011 buffers.

Put the columns in the index:

```sql
CREATE INDEX lab_order_lines_product_id_covering
    ON order_lines (product_id) INCLUDE (quantity, unit_price);
```

The result is `Index Only Scan`, `Heap Fetches: 0`, 98 buffers, 5.8 ms. `INCLUDE` columns are stored only in the leaf pages. They can't be searched or sorted by, but they don't make the upper levels of the tree bigger either.

Now price it. It took 12.9 s to build and takes **697 MB**; the `product_id` index it replaces is 120 MB. That index is small because B-tree deduplication, which PostgreSQL has had since version 13, stores a repeated key once with a list of row pointers: 18 million entries for 50,000 products collapse well. An index with `INCLUDE` columns cannot be deduplicated. Whether 577 MB more disk and cache, and a bigger index write on every insert, is worth about 27 ms on an admin page is a product decision. Make it with those numbers in front of you.

*Break it* shows the other cost: an index-only scan is only as good as the visibility map.
</details>

<details>
<summary>Rung 6 — a customer's orders this year</summary>

```
Index Scan Backward using ix_orders_created_at_customer_id on orders o  (actual ... rows=3.00 loops=1)
  Index Cond: ((created_at >= '2025-09-01 ...') AND (customer_id = '33'::bigint))
  Buffers: shared hit=6386
```

Both columns appear in `Index Cond`, so it looks as if the index is used well. It isn't. The index is sorted by `created_at` first. Only the leading column can narrow the range, so the scan walks every order of the last year and checks `customer_id` on each one: 6,386 of the index's 19,228 pages — a third of it — for 3 rows.

`Index Searches: 1` says PostgreSQL 18's skip scan did not step in either. Skip scan pays off when the leading column has few distinct values, and `created_at` has millions.

Put the equality column first and the range column second:

```sql
CREATE INDEX lab_orders_customer_id_created_at ON orders (customer_id, created_at);
```

The result is 6 buffers and 1.5 ms. The rule: **equality columns first, then the range or sort column.** The same index also returns the customer's orders already in date order. It cost 2.2 s and 150 MB; check whether the old index still serves a query of its own before you keep both.
</details>

<details>
<summary>Rung 7 — the admin order list, page 4,001</summary>

```
Limit  (actual ... rows=50.00 loops=1)
  ->  Incremental Sort  (actual ... rows=200050.00 loops=1)
        ->  Index Scan Backward using ix_orders_created_at_customer_id ...  (actual ... rows=200051.00 loops=1)
```

`OFFSET 200000` makes the server produce 200,050 rows and throw away all but the last 50. The cost grows linearly with the page number; page 4,001 costs 2,791 buffers.

The request already carries the last row of the previous page, so start after it. That is keyset pagination. On PostgreSQL, write it as a row-value comparison, which the Npgsql provider translates from a `ValueTuple`:

```csharp
.Where(o => EF.Functions.LessThan(
    ValueTuple.Create(o.CreatedAt, o.Id),
    ValueTuple.Create(after.CreatedAt, after.Id)))
.OrderByDescending(o => o.CreatedAt).ThenByDescending(o => o.Id)
.Take(page.PageSize)
```

```
Index Scan Backward using ix_orders_created_at_customer_id on orders o  (actual ... rows=51.00 loops=1)
  Index Cond: (created_at <= '2026-07-19 03:50:41.811925+00'::timestamp with time zone)
  Filter: (ROW(created_at, id) < ROW('2026-07-19 03:50:41.811925+00'::timestamp with time zone, '4800001'::bigint))
  Rows Removed by Filter: 1
```

The result is 5 buffers and 1.7 ms. The planner derived an index boundary, `created_at <=`, from the row comparison. The OR form in *Break it* gives the same rows, but PostgreSQL treats it only as a `Filter`, so the scan still walks, and throws away, the 200,000 rows before the page (`Rows Removed by Filter: 200000`).

With an index on `(created_at, id)`, the whole comparison becomes the `Index Cond`, and the `Incremental Sort` disappears, because the index already delivers the `ORDER BY`. It reads the same 5 buffers here; the gain is the sort that no longer runs.

The costs belong to the product, not to the database:

- there is no "jump to page 4,001" any more, only next and previous;
- the sort needs a unique tiebreaker (`id`);
- the cursor has to travel with the request.

[Chapter 15](#chapter-15-performance-optimization) makes the same point for unbounded lists.
</details>

<details>
<summary>Rung 8 — customer search</summary>

```sql
WHERE c.email ILIKE @p ESCAPE ''      -- @p = '%tomasz.kowalski%'
```

```
Parallel Seq Scan on customers c  (actual ... rows=83.00 loops=3)
  Filter: (email ~~* '%tomasz.kowalski%'::text)
  Rows Removed by Filter: 333250
```

A B-tree can serve `'prefix%'`, but not a pattern that starts with a wildcard. Three processes each read a third of the table (`loops=3`, 333,250 rows removed per process): 13,735 buffers.

A trigram index can serve it:

```sql
CREATE EXTENSION IF NOT EXISTS pg_trgm;
CREATE INDEX lab_customers_email_trgm ON customers USING gin (email gin_trgm_ops);
```

`pg_trgm` indexes every three-character piece of the text, and the pattern's own trigrams narrow the candidates before any row is read. The result is a `Bitmap Index Scan on lab_customers_email_trgm`, 439 buffers, 9.0 ms. It took 4.6 s to build and takes 52 MB. GIN indexes are slower to update than B-trees, and a search term shorter than three characters has no trigram to narrow with, so give the search box a minimum length.

> **Gotcha.** Look at `ESCAPE ''` in the starter's SQL. The Npgsql EF Core provider (10.0.3 here) emits it when you call `EF.Functions.ILike(match, pattern)` without an escape character: *no* escape character at all. A `_` typed into the search box then matches any character — and underscores are common in email addresses — and the user has no way to escape it. The solution passes a backslash as the escape character and escapes the input:
>
> ```csharp
> var pattern = "%" + EscapeLike(term) + "%";
> .Where(c => EF.Functions.ILike(c.Email, pattern, @"\"))
>
> static string EscapeLike(string s) =>
>     s.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_");
> ```
</details>

<details>
<summary>Rung 9 — recent sales of a product</summary>

The harness replays the statement the way the server really ran it: as a prepared statement, after the same warm-up. `pg_prepared_statements` reports **3 generic plans and 5 custom plans** so far:

```
Limit  (actual ... rows=20.00 loops=1)
  Buffers: shared read=151331
  ->  Sort  (actual ... rows=20.00 loops=1)
        Sort Key: id DESC
        Sort Method: top-N heapsort  Memory: 27kB
        ->  Index Scan using ix_order_lines_product_id on order_lines o  (cost=0.44..518.89 rows=463 width=26) (actual ... rows=1441651.00 loops=1)
              Index Cond: (product_id = $1)
```

`$1` in `Index Cond` means a generic plan: it was built without knowing the value. Here is how the server got there:

1. **Automatic preparation.** The service sets `Max Auto Prepare=20`; the default, 0, turns it off. After a statement has run 5 times (`Auto Prepare Min Usages`), Npgsql makes it a server-side prepared statement.
2. **The plan cache.** PostgreSQL gives a prepared statement five custom plans, then compares a generic plan's estimated cost with their average. The warm-up products are popular, so their custom plans looked expensive, and the generic plan won.
3. **The generic estimate.** A generic plan is costed for an *average* product: 463 lines, so fetch them all and sort. That is right for most products. The bestseller has 1,441,651 lines. The scan touches 151,331 pages — nearly the whole 150,272-page table — and they do not stay cached, so every call reads them again: 1,014.2 ms on the server.

The solution removes the choice:

```sql
CREATE INDEX lab_order_lines_product_id_id ON order_lines (product_id, id);
```

With this index one plan is right for every product: walk that product's entries backwards and stop after 20. `Index Scan Backward using lab_order_lines_product_id_id`, 7 buffers, 1.5 ms. The replay reports 0 generic and 8 custom plans; the custom plans are now so cheap that a generic one never wins. The cost is 11.0 s to build and 542 MB on disk, against 120 MB for the old index. `(product_id, id)` is unique per row, so deduplication has nothing to collapse.

The alternatives, with their prices, are in the Level 3 answers.
</details>

<details>
<summary>Level 3 — the server's view, auto_explain and the rung 9 trade-off</summary>

**1. N+1 in `pg_stat_statements`**, after five calls of the starter's rung 1:

```
 calls | rows | total_ms | buffers | query
-------+------+----------+---------+-------------------------------------------
   250 | 5910 |     9.11 |    1050 | SELECT o.product_id, o.quantity, o.unit_price FROM order_lines ...
     5 |  250 |     1.67 |     405 | SELECT o.id, o.created_at, o.currency, o.customer_id, ...
```

The signature is two statements whose call counts differ by exactly the page size (250 ÷ 5 = 50). Each statement is cheap, and no single plan looks wrong. The server's total for all five pages is under 11 ms; in the reference run a single page took a median of 78.0 ms of wall time. That gap is round trips. In production, sort `pg_stat_statements` by `calls` as well as by `total_exec_time`. [Finding the Queries Worth Looking At](#finding-the-queries-worth-looking-at) explains why the most expensive query in total is rarely the slowest one.

**2. `auto_explain`** logged every execution over one second. There are six in the reference run: the slow measured calls and the harness's replays. Each looks like this:

```
LOG:  duration: 1085.995 ms  plan:
  Query Text: SELECT o.id, o.order_id, o.quantity, o.unit_price FROM order_lines AS o
              WHERE o.product_id = $1 ORDER BY o.id DESC LIMIT $2
  Query Parameters: $1 = '1', $2 = '20'
  Limit  (cost=539.39..539.51 rows=46 width=26)
    ->  Sort  (cost=539.39..540.55 rows=463 width=26)
          ->  Index Scan using ix_order_lines_product_id on order_lines o  (... rows=463 ...)
                Index Cond: (product_id = $1)
```

Two clues prove it is a generic plan: `$1` in `Index Cond`, although the log also records the value `'1'`, and an estimate of 463 rows for a product with 1.44 million. The lab does not turn on `auto_explain.log_analyze`. The documentation warns that it instruments *every* statement, not only the slow ones, and that `auto_explain.log_timing = off` reduces the cost.

**3. `force_custom_plan` through the connection string.** The starter's rung 9 runs unchanged, with no new index:

| | Fix: index `(product_id, id)` | Fix: `plan_cache_mode = force_custom_plan` |
|---|---|---|
| Bestseller | 7 buffers, 1.5 ms | 7 buffers, 1.9 ms |
| Plans | 0 generic, 8 custom | 0 generic, 8 custom |
| The plan | walk the new index backwards | walk the *primary key* backwards and filter on `product_id` (225 rows removed) |
| What it costs | 11.0 s to build, 542 MB, one more index to maintain on every insert | planning on every execution (0.114 ms here) of every statement on those connections |
| What it depends on | nothing | the planner getting each value's custom plan right |

Custom planning works here because the bestseller is spread evenly through the table, so walking the primary key backwards finds 20 of its lines within 245 rows. The planner re-decides for every value, and so it is only as good as its statistics.

Other options:

- setting `plan_cache_mode` per role or per database, instead of in the connection string;
- turning automatic preparation off, which gives back its planning savings everywhere to fix one statement;
- EXPLAIN (GENERIC_PLAN), available since PostgreSQL 16, which shows you the generic plan without a warm-up, as a check to run in review.

For a skew you know about and a query you own, the index is the durable fix. `force_custom_plan` is the stopgap you can deploy in minutes, without a migration.
</details>

<details>
<summary>Level 3 — the SQL Server track</summary>

SQL Server 2025 (17.0, compatibility level 170), 200,000 customers and 2 million orders. Customer 1 has 100,353 orders. The counts are logical reads from `SET STATISTICS IO`.

| Script | Mirrors | What SQL Server shows |
|---|---|---|
| 1. `nvarchar` parameter against a `varchar` column | Rung 4 | `CONVERT_IMPLICIT(nvarchar(254),[email])` on the *column* turns a seek into an `Index Scan`: 1,174 reads against 6 with a `varchar(254)` parameter. The database uses `SQL_Latin1_General_CP1_CI_AS`, a SQL collation. |
| 2. Key lookups and the tipping point | Rung 5 | A typical customer: an index seek plus 11 key lookups, 36 reads. Customer 1: the optimizer ignores the index and scans the clustered index, 15,379 reads. Forcing the index costs 307,657 reads, so the optimizer was right. A covering index (`INCLUDE (created_at, status, total)`) costs 3 and 643 reads. |
| 3. Parameter sniffing in a stored procedure | Rung 9 | The first caller decides. If a typical customer compiles the plan, customer 1 then runs 100,353 key lookups (301,335 reads). If customer 1 compiles it, a typical customer scans the whole table for 11 rows (15,379 reads). The Parameter Sensitive Plan optimization did not engage for this procedure (`psp_dispatcher: no`). The covering index is right for both: 613 reads for customer 1, on a plan compiled for a typical customer. |
| 4. `OFFSET` against keyset | Rung 7 | `OFFSET 200000`: 613,192 reads, because every skipped row costs a key lookup. The OR-form keyset: 168 reads — SQL Server turns the OR into a seek over two ranges, which PostgreSQL does not. A redundant boundary (`created_at <= @c AND (…)`): 165 reads. |

The .NET lessons:

- **Script 1.** SqlClient and Dapper send a .NET `string` as `nvarchar` unless told otherwise. Use `DbString { IsAnsi = true }` in Dapper, or an explicit `SqlDbType.VarChar`. EF Core types the parameter from the column mapping, so map `varchar` columns as varchar (`IsUnicode(false)`, with a length).
- **Scripts 2 and 3.** Both are the same lesson as rung 9: when a plan choice depends on the value, remove the choice with an index that is right for every value, rather than fight over which value gets to compile it.
</details>

<details>
<summary>The reference numbers</summary>

Medium scale, warm cache, 5 measured calls per rung; per-call work from `pg_stat_statements`. Wall time varies with your machine; the work should match closely.

| Rung | Starter: wall · statements · buffers | Solution: wall · statements · buffers | Fix cost |
|---|---|---|---|
| 1 | 78.0 ms · 51 · 291 | 9.1 ms · 1 · 291 | none |
| 2 | 616.7 ms · 1 · 33,435 (144,843 rows) | 308.4 ms · 3 · 21,711 (43,529 rows) | none |
| 3 | 318.0 ms · 1 · 13,659 | 1.9 ms · 1 · 4 | 3.3 s, 49 MB |
| 4 | 488.2 ms · 1 · 49,443 | 1.6 ms · 1 · 4 | none |
| 5 | 32.5 ms · 1 · 17,011 | 5.8 ms · 1 · 98 | 12.9 s, 697 MB |
| 6 | 37.8 ms · 1 · 6,386 | 1.5 ms · 1 · 6 | 2.2 s, 150 MB |
| 7 | 44.4 ms · 1 · 2,791 | 1.7 ms · 1 · 5 | none |
| 8 | 202.7 ms · 1 · 13,735 | 9.0 ms · 1 · 439 | 4.6 s, 52 MB |
| 9 | 1,011.8 ms · 1 · 151,331 | 1.5 ms · 1 · 7 | 11.0 s, 542 MB |
</details>

<details>
<summary>Break it — the answers</summary>

**Stale statistics.** Customer 900,001 is not in the most-common-values list. For such a value the planner shares the rows the list doesn't cover evenly among the remaining distinct values: (1 − 0.0975) ÷ (210,578 − 11) × 5,000,396 ≈ 21.4 rows. It also scales row counts by the table's current size, which the load grew by 6%, so the estimate becomes 23.

With 23 rows expected, "fetch them all through `customer_id` and sort" looks cheap. With 306,526 it is obviously not, and walking the date index backwards finds 50 matches almost at once. Two lessons:

- **`ANALYZE` after a bulk load, explicitly.** Autovacuum's threshold is a fraction of the table (20% of rows for a vacuum, 10% for an analyze, by default), and 300,000 rows is 6% of this one.
- **Worry about `LIMIT` plans.** A plan that looks cheap only because of a `LIMIT` is the kind to worry about when the estimate is wrong — see [When the Plan Is Wrong](#when-the-plan-is-wrong).

**The visibility map.** An index-only scan still has to know that each row is visible to its transaction. The visibility map keeps one bit per table page meaning "every row here is visible to everyone". Any change to a page clears the bit, even a change that is later rolled back. Only `VACUUM` sets it again. With the bits cleared, all 18,015 index entries needed a trip to the table (`Heap Fetches: 18015`), and the covering index was no better than no covering index at all.

On a table written to all day, an index-only scan is only as good as autovacuum's pace. Check `Heap Fetches` in production plans, and tune `autovacuum_vacuum_scale_factor` per table where it matters.

**Keyset, the OR form.** Its plan has `Filter:` where the row-value form has `Index Cond:`, and `Rows Removed by Filter: 200000`. PostgreSQL does not turn this OR into an index boundary. The scan starts at the newest order and discards rows until the condition becomes true.

The fourth form moves the whole row comparison into `Index Cond` and loses the `Incremental Sort`. The buffers are the same (5), because the old index was already close to ideal for this page.

SQL Server handles the OR form (script 4 of the SQL Server track). It is a genuine difference between engines, and one reason why "it was fast on the other database" is not evidence.
</details>

## Further reading

- **PostgreSQL documentation:** *Using EXPLAIN*, *Index-Only Scans and Covering Indexes*, *Statistics Used by the Planner* (extended statistics), the `PREPARE` page (the custom- and generic-plan rule) and `plan_cache_mode`, and the pages for `pg_stat_statements`, `auto_explain` and `pg_trgm`.
- **Npgsql documentation:** *Prepared Statements*, including automatic preparation and its connection-string settings.
- **EF Core documentation:** *Single vs. Split Queries* and *Pagination* (keyset pagination).
- **Markus Winand, *Use The Index, Luke!*** — the clearest treatment of column order in multi-column indexes, and of the "seek method" for pagination.
- **Erland Sommarskog, "Slow in the Application, Fast in SSMS?"** — the standard text on SQL Server parameter sniffing.
