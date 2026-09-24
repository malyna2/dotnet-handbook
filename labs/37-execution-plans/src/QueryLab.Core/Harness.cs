using System.Diagnostics;
using System.Reflection;
using Npgsql;

namespace QueryLab.Core;

/// <summary>Per-call averages from pg_stat_statements: what the server itself counted.</summary>
public sealed record ServerCounts(double Statements, double Rows, double SharedHit, double SharedRead, double ExecMs)
{
    public double SharedBlocks => SharedHit + SharedRead;
}

public sealed record RungMeasurement(
    Rung Rung,
    IReadOnlyList<string> AppliedFixes,
    TimeSpan FixDuration,
    IReadOnlyList<string> FixIndexes,
    IReadOnlyList<double> CallMs,
    ServerCounts PerCall,
    IReadOnlyList<ExplainedStatement> Plans,
    int DistinctStatements,
    object Result,
    IReadOnlyList<string> EfWarnings)
{
    public double MedianMs => CallMs.Order().ElementAt(CallMs.Count / 2);
}

public static class Harness
{
    /// <summary>Every concrete rung in the given assembly, ordered by number.</summary>
    public static IReadOnlyList<Rung> DiscoverRungs(Assembly assembly) =>
        assembly.GetTypes()
            .Where(t => t is { IsAbstract: false, IsClass: true } && typeof(Rung).IsAssignableFrom(t))
            .Select(t => (Rung)Activator.CreateInstance(t)!)
            .OrderBy(r => r.Number)
            .ToList();

    /// <summary>Drops every lab_* index and statistics object, leaving the seeded baseline.</summary>
    public static async Task ResetSchemaAsync(LabDb lab, CancellationToken ct)
    {
        var drops = new List<string>();
        await using (var cmd = lab.DataSource.CreateCommand("""
            SELECT format('DROP INDEX IF EXISTS %I.%I', schemaname, indexname)
              FROM pg_indexes WHERE indexname LIKE 'lab\_%'
            UNION ALL
            SELECT format('DROP STATISTICS IF EXISTS %I.%I', stxnamespace::regnamespace::text, stxname)
              FROM pg_statistic_ext WHERE stxname LIKE 'lab\_%'
            """))
        await using (var reader = await cmd.ExecuteReaderAsync(ct))
            while (await reader.ReadAsync(ct)) drops.Add(reader.GetString(0));
        foreach (var drop in drops) await lab.ExecuteAsync(drop, ct);
    }

    public static async Task<RungMeasurement> MeasureAsync(
        LabDb lab, Rung rung, int runs = 5, bool explain = true, CancellationToken ct = default)
    {
        await lab.ResetRungStateAsync();
        await ResetSchemaAsync(lab, ct);

        var fixWatch = Stopwatch.StartNew();
        foreach (var fix in rung.Fixes) await lab.ExecuteAsync(fix, ct);
        fixWatch.Stop();
        var fixIndexes = rung.Fixes.Count == 0 ? [] : await IndexSizesAsync(lab, ct);

        await rung.SetUpAsync(lab, ct);
        await rung.WarmUpAsync(lab, ct);

        await lab.ExecuteAsync("SELECT pg_stat_statements_reset()", ct);
        var callMs = new List<double>();
        object result = null!;
        IReadOnlyList<CapturedCommand> lastRun = [];
        for (var i = 0; i < runs; i++)
        {
            lab.Log.Clear();
            lab.Log.Enabled = true;
            var sw = Stopwatch.StartNew();
            result = await rung.RunAsync(lab, ct);
            sw.Stop();
            lab.Log.Enabled = false;
            lastRun = lab.Log.Commands;
            callMs.Add(sw.Elapsed.TotalMilliseconds);
        }
        var perCall = await ReadServerCountsAsync(lab, runs, ct);

        var plans = new List<ExplainedStatement>();
        var distinct = lastRun.GroupBy(c => c.Sql).Select(g => g.First()).ToList();
        if (explain)
        {
            var overridden = await rung.ExplainOverrideAsync(lab, lastRun, ct);
            if (overridden is not null) plans.AddRange(overridden);
            else foreach (var command in distinct.Take(3)) plans.Add(await Explain.CommandAsync(lab.DataSource, command, ct));
        }

        List<string> warnings;
        lock (lab.EfWarnings) warnings = [.. lab.EfWarnings.Distinct()];
        await lab.ResetRungStateAsync();
        return new RungMeasurement(rung, rung.Fixes, fixWatch.Elapsed, fixIndexes, callMs, perCall, plans, distinct.Count, result, warnings);
    }

    /// <summary>What a fix costs on disk: every lab_* index with its size.</summary>
    private static async Task<List<string>> IndexSizesAsync(LabDb lab, CancellationToken ct)
    {
        var sizes = new List<string>();
        await using var cmd = lab.DataSource.CreateCommand("""
            SELECT format('%s %s', indexrelname, pg_size_pretty(pg_relation_size(indexrelid)))
              FROM pg_stat_user_indexes WHERE indexrelname LIKE 'lab\_%' ORDER BY indexrelname
            """);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) sizes.Add(reader.GetString(0));
        return sizes;
    }

    private static async Task<ServerCounts> ReadServerCountsAsync(LabDb lab, int runs, CancellationToken ct)
    {
        await using var cmd = lab.DataSource.CreateCommand("""
            SELECT coalesce(sum(calls), 0)::float8, coalesce(sum(rows), 0)::float8,
                   coalesce(sum(shared_blks_hit), 0)::float8, coalesce(sum(shared_blks_read), 0)::float8,
                   coalesce(sum(total_exec_time), 0)::float8
              FROM pg_stat_statements
             WHERE dbid = (SELECT oid FROM pg_database WHERE datname = current_database())
               AND toplevel
               AND query NOT ILIKE '%pg_stat_statements%'
               -- Npgsql resetting a pooled connection when it is returned: DISCARD ALL, or, when the
               -- connection holds prepared statements, the same reset minus DEALLOCATE ALL.
               AND query !~ '^(DISCARD (ALL|TEMP|SEQUENCES)|CLOSE ALL|SET SESSION AUTHORIZATION DEFAULT|RESET ALL|UNLISTEN \*|SELECT pg_advisory_unlock_all\(\))$'
            """);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        return new ServerCounts(
            reader.GetDouble(0) / runs, reader.GetDouble(1) / runs,
            reader.GetDouble(2) / runs, reader.GetDouble(3) / runs, reader.GetDouble(4) / runs);
    }

    /// <summary>A short description of where the numbers came from, printed above every run.</summary>
    public static async Task<string> EnvironmentAsync(LabDb lab, CancellationToken ct)
    {
        var version = await lab.ScalarAsync<string>("SELECT version()", ct);
        var orders = await lab.ScalarAsync<long>("SELECT count(*) FROM orders", ct);
        var lines = await lab.ScalarAsync<long>("SELECT reltuples::bigint FROM pg_class WHERE relname = 'order_lines'", ct);
        var cpu = File.Exists("/proc/cpuinfo")
            ? File.ReadLines("/proc/cpuinfo").FirstOrDefault(l => l.StartsWith("model name"))?.Split(':', 2)[1].Trim()
            : null;
        var memTotal = File.Exists("/proc/meminfo")
            ? File.ReadLines("/proc/meminfo").FirstOrDefault(l => l.StartsWith("MemTotal"))?.Split(':', 2)[1].Trim()
            : null;
        var ramGb = memTotal is not null
            ? long.Parse(memTotal.Split(' ')[0], System.Globalization.CultureInfo.InvariantCulture) / 1024.0 / 1024
            : GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 1024.0 / 1024 / 1024;
        return $"""
            date        : {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC
            server      : {version}
            client      : .NET {Environment.Version}, Npgsql {typeof(NpgsqlConnection).Assembly.GetName().Version}
            machine     : {Environment.ProcessorCount} vCPU{(cpu is null ? "" : $" ({cpu})")}, {ramGb:F0} GB RAM
            data        : {orders:N0} orders, ~{lines:N0} order lines
            cache state : warm — each rung runs once before it is measured
            """;
    }
}
