# Lab kit — Chapter 37: Reading Execution Plans

Nine slow queries in a .NET 10 / EF Core service over PostgreSQL 18, seeded with 5 million orders and 18 million order lines. You read each plan, fix the mechanism, and prove the fix with numbers. An optional SQL Server 2025 track covers the four places where SQL Server behaves differently.

> **The portfolio rule.** Don't push your fixes here. Your solutions, your before/after plans and your results table go in **your own public portfolio repo**, where an interviewer can read them.

## What you need

- Docker with Compose v2, about **6 GB RAM** free for PostgreSQL (8 GB with the SQL Server track) and **~5 GB disk** at the medium scale.
- .NET 10 SDK.
- Port 5433 free (PostgreSQL) and, for the optional track, port 14333 (SQL Server).

## Quick start

```bash
docker compose up -d --wait        # PostgreSQL 18.6 with pg_stat_statements
./seed.sh medium                   # ~4 minutes: 1 M customers, 5 M orders, 18 M order lines

# Run every rung with your (starter) code: timings, server counts and the real plans.
dotnet run --project src/QueryLab.Cli -- all

# Run one rung, faster, without plans:
dotnet run --project src/QueryLab.Cli -- 3 --runs 3 --no-plan

# Keep the last rung's lab_* objects afterwards, to explore its plan in psql:
dotnet run --project src/QueryLab.Cli -- 5 --keep
docker compose exec postgres psql -U lab -d shop
```

Edit `starter/QueryLab.Rungs/RungN_*.cs` — change the query, add DDL to `Fixes`, or both. Name every index or statistics object you create **`lab_*`**: the harness drops all `lab_*` objects before each rung and again after the last one, so rungs never depend on each other and a run leaves the database as seeded.

Check your work with the acceptance tests. They start their own PostgreSQL in Docker (Testcontainers), seed it at the small scale, and hold each rung to a criterion about work done — statements, rows, buffers, plan nodes — not milliseconds, so they pass or fail the same way on any machine:

```bash
dotnet test --project tests/QueryLab.Tests
```

Every test also checks that your rung returns exactly the rows a plain-SQL reference query returns. A faster query that returns different data is not a fix.

## The rungs

| # | File | Scenario |
|---|---|---|
| 1 | `Rung1_OrderHistory.cs` | A key account's latest 50 orders, each with its lines |
| 2 | `Rung2_DeliveryReport.cs` | A quarter of shipped orders with their lines and shipments |
| 3 | `Rung3_SignIn.cs` | Finding a customer by the email typed at sign-in |
| 4 | `Rung4_PartnerWebhook.cs` | Loading the order a delivery partner's webhook names |
| 5 | `Rung5_ProductSales.cs` | Lines, units and revenue for one product |
| 6 | `Rung6_CustomerYear.cs` | One customer's orders from the last 365 days |
| 7 | `Rung7_AdminOrdersPage.cs` | Page 4,001 of the admin order list |
| 8 | `Rung8_CustomerSearch.cs` | The support team's "email contains…" search |
| 9 | `Rung9_RecentSales.cs` | A product's latest 20 sales, with Npgsql automatic preparation on |

Each rung's inputs and result type are fixed in `src/QueryLab.Core/Contracts.cs`; you only change the implementation.

## What the harness measures

For each rung the CLI drops all `lab_*` objects, applies the rung's `Fixes`, runs the code once to warm the cache, resets `pg_stat_statements`, and then measures five calls:

- **fix cost** — how long the `Fixes` took to apply, and the size of every `lab_*` index they created;
- **wall time** — the median of the five calls, measured in .NET;
- **per call** — statements, rows returned and shared buffers (hit + read) as counted by `pg_stat_statements`, so they are the server's numbers, whatever shape your code takes. Npgsql's connection-reset statements (`DISCARD ALL` and its variants) are left out;
- **plans** — every distinct SQL text EF Core sent, re-run under `EXPLAIN (ANALYZE, BUFFERS)` with the same parameter values *and types*. Rung 9 instead replays the statement as a prepared statement, because that is what the server really executed.

## Break it

Scripts that break something on purpose, show the plan before and after, and put the database back as seeded (Chapter 37 has the questions):

```bash
docker compose exec -T postgres psql -U lab -d shop -f /lab/break-it-stale-statistics.sql   # a bulk load the planner hasn't heard about
docker compose exec -T postgres psql -U lab -d shop -f /lab/break-it-visibility-map.sql     # what a rolled-back DELETE does to an index-only scan
docker compose exec -T postgres psql -U lab -d shop -f /lab/sidebar-keyset-forms.sql        # rung 7's page written four ways
docker compose exec -T postgres psql -U lab -d shop -f /lab/sidebar-correlated-columns.sql  # a 5x misestimate that changes nothing
```

## The SQL Server track (optional)

```bash
docker compose --profile sqlserver up -d
./sqlserver.sh seed                # a few minutes: 200 000 customers, 2 M orders
./sqlserver.sh 1                   # then 2, 3, 4 — each script runs the slow and the fixed version
```

`sqlserver.sh` condenses `SET STATISTICS PROFILE` and `SET STATISTICS IO` output with `sqlserver/format.py` (plain sqlcmd output without python3).

## Troubleshooting

- **`toomanyrequests` / 429 when pulling `postgres:18.6`.** Docker Hub rate-limits anonymous pulls. Log in (`docker login`), or pull the same image from Google's mirror and tag it: `docker pull mirror.gcr.io/library/postgres:18.6 && docker tag mirror.gcr.io/library/postgres:18.6 postgres:18.6`. (The digests are identical.)
- **Port 5433 is in use.** Change the left side of `"5433:5432"` in `docker-compose.yml` and pass `--connection "Host=localhost;Port=<yours>;Username=lab;Password=lab;Database=shop"` to the CLI.
- **Timings differ from Chapter 37.** They will — your CPU, disk and cache differ. Plan shapes and buffer counts should match closely; ratios should hold.

## For maintainers

- `verify.sh` proves the lab is honest: every starter rung fails its acceptance test with an `ACCEPTANCE:` message (right rows, too much work) and every solution rung passes. CI runs it (`.github/workflows/labs.yml`).
- `reference-runs/capture.sh` regenerates the raw output behind every number in Chapter 37. Run it on a freshly seeded database: buffer counts depend on the physical layout, and the break-it scripts restore that layout (`VACUUM`, `REINDEX`) only because an earlier version of them didn't, and the numbers drifted.
- `solution/` is the reference solution. The CLI and the tests use it with `--property:Rungs=solution`.

## Last verified

2026-09-24 — PostgreSQL 18.6, SQL Server 2025 RTM-CU9 (17.0.5005.3), .NET SDK 10.0.112, EF Core 10.0.12, Npgsql 10.0.3, on 4 vCPU / 16 GB (Ubuntu 24.04, Docker 29.3.1). `verify.sh`: starter 9/9 fail as intended, solution 9/9 pass. Not verified on macOS, Windows or ARM64.
