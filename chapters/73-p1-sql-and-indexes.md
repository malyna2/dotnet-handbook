# Part 1 · Module 3: SQL and Indexes

> **What this module makes you able to do.** Open the actual plan of a slow query in your own service, name each index operator a seek or a scan and say why, fix the predicate, the parameter type or the index so it seeks, and show the change in logical reads.

**Time:** reading ≈ 20 min; hands-on ≈ 4 h 10 min — the experiment 15 min, the Chapter 4 exercise 10, the lab subset 3 h, the questions 15, the check at work 30.

## Covers

- what an index is, and clustered against non-clustered against covering in SQL Server;
- seek against scan, and why "the query uses the index" is not "the query seeks it";
- the leftmost-prefix rule for composite indexes, and why it holds;
- sargability: a function on the column, and an `nvarchar` parameter against a `varchar` column, with the .NET fix;
- reading an actual plan at the basic level: operators, estimated against actual rows, logical reads;
- joins, and what a transaction guarantees.

## The mechanism to explain without notes

**An index is a copy of some columns, kept sorted by its key from left to right, with a pointer back to the row. A seek jumps to one contiguous range of that order; a scan reads all of it.**

The optimizer can seek only when the predicate pins a *leftmost prefix* of the key, using the values the index stores. Two things defeat it:

- **A predicate on a non-leading column.** Its rows have no contiguous range: the rows for one date are scattered across every city's block of a `(City, CreatedAt)` index. So the rule is equality columns first, then the range or sort column.
- **A function or a conversion applied to the column**, such as `LOWER(Email)` or `CONVERT_IMPLICIT(nvarchar, Email)`. It asks about values the index doesn't store. In .NET the conversion usually comes from a `string` parameter, which SqlClient sends as `nvarchar`.

The pointer explains covering. In SQL Server a non-clustered index row holds its key columns plus the clustered key, so a query that needs any other column pays a key lookup per row, unless `INCLUDE` puts that column in the index. Logical reads count the 8 KB pages a query touched: that is the work, and unlike milliseconds it is the same on every machine.

Joins and transactions sit on top of the same storage: a join is a lookup per row or a pass over two inputs, and both are cheap only when an index serves the join column. A transaction makes a group of statements all-or-nothing; EF Core's `SaveChanges` wraps its statements in one, and two `SaveChanges` calls are two transactions.

## Read (≈ 20 min)

1. [Chapter 4: Joins](#joins).
2. [Chapter 4: Indexes: Clustered, Non-Clustered, Covering](#indexes-clustered-non-clustered-covering): the row locator, and why the leftmost-prefix rule holds.
3. [Chapter 4: Execution Plans](#execution-plans): actual plans, logical reads, and why an implicit conversion scans.
4. [Chapter 4: Transactions and ACID](#transactions-and-acid).
5. [Chapter 4: Choosing an Index Type](#choosing-an-index-type): the B-tree rules, from the PostgreSQL side.
6. [Chapter 4: Reading EXPLAIN](#reading-explain): PostgreSQL's plan text, which the lab prints.
7. [Chapter 37: What the harness prints, and in what order to read it](#what-the-harness-prints-and-in-what-order-to-read-it).
8. [Chapter 37: Level 1 — The code decides the SQL (rungs 1–4)](#level-1-the-code-decides-the-sql-rungs-14), and rung 6 in [Level 2 — The index decides the plan (rungs 5–9)](#level-2-the-index-decides-the-plan-rungs-59).

## Prove it

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

**Then the lab** ([kit](https://github.com/malyna2/dotnet-handbook/tree/main/labs/37-execution-plans), PostgreSQL in Docker, about 3 hours): read the *Goal* and *Setup* sections of [Chapter 37](#chapter-37-the-slow-query-lab-reading-execution-plans), do Level 1 (rungs 1–4: N+1, a cartesian explosion from two `Include`s, a function on a column, a conversion on a column) and rung 6 (column order). Open its *Hints and answers* for a rung only after your fix passes. Your results belong in your own public portfolio repo, not in this one.

Also do Chapter 4's *What would you do* (the 40-second report), in the Exercises at the end of [Chapter 4](#chapter-4-data-access-databases).

## Three questions

**1.** An index on `(City, CreatedAt)`. Why does `WHERE CreatedAt >= @d` scan while `WHERE City = @c AND CreatedAt >= @d` seeks? And why would `(CreatedAt, City)` be worse for the second query?

<details>
<summary>Answer</summary>

- **Why one seeks and the other scans.** The index is sorted by `City`, then by `CreatedAt` within each city. One city is a contiguous block with its dates in order, so the engine seeks to `(City, start date)` and reads forward. A date range alone is spread across every city's block: there is no single range to seek, so it scans (712 reads against 3 in the experiment).
- **Why `(CreatedAt, City)` is worse.** The date range on the leading column *is* contiguous, but it holds every city's rows for that period, and inside that range the cities are not in order. `City` can only be checked row by row, so the engine reads the whole period to keep one city's rows. Chapter 37's rung 6 measures exactly this.
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

## Check at work

**Inspect.** Take your service's most expensive query from Query Store (or your APM's slowest dependency) and open its *actual* plan. Name every index operator a seek or a scan. For each scan, decide which of the experiment's three causes it is, or whether the scan is simply right because the query needs most of the table. For every `varchar` column your code filters on, check the type the parameter arrives with (`ToQueryString()` shows EF Core's `DECLARE`).

**Measure.** The query's logical reads before and after your fix (`SET STATISTICS IO ON`), and its executions per hour from Query Store. Reads saved times executions is the load you removed.
