using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Npgsql;

namespace QueryLab.Core;

/// <summary>One node of an <c>EXPLAIN (ANALYZE, BUFFERS, FORMAT JSON)</c> plan.</summary>
public sealed class PlanNode
{
    public string NodeType { get; init; } = "";
    public string? RelationName { get; init; }
    public string? IndexName { get; init; }
    public double PlanRows { get; init; }
    public double ActualRows { get; init; }      // per loop, as PostgreSQL reports it
    public double ActualLoops { get; init; }
    public long SharedHitBlocks { get; init; }   // cumulative: this node and everything below it
    public long SharedReadBlocks { get; init; }
    public long? HeapFetches { get; init; }
    public List<PlanNode> Children { get; } = [];

    public double TotalActualRows => ActualRows * ActualLoops;
    public long SharedBlocks => SharedHitBlocks + SharedReadBlocks;

    public IEnumerable<PlanNode> SelfAndDescendants()
    {
        yield return this;
        foreach (var child in Children)
            foreach (var node in child.SelfAndDescendants())
                yield return node;
    }

    internal static PlanNode Parse(JsonElement e)
    {
        var node = new PlanNode
        {
            NodeType = e.GetProperty("Node Type").GetString() ?? "",
            RelationName = e.TryGetProperty("Relation Name", out var r) ? r.GetString() : null,
            IndexName = e.TryGetProperty("Index Name", out var i) ? i.GetString() : null,
            PlanRows = e.TryGetProperty("Plan Rows", out var pr) ? pr.GetDouble() : 0,
            ActualRows = e.TryGetProperty("Actual Rows", out var ar) ? ar.GetDouble() : 0,
            ActualLoops = e.TryGetProperty("Actual Loops", out var al) ? al.GetDouble() : 0,
            SharedHitBlocks = e.TryGetProperty("Shared Hit Blocks", out var h) ? h.GetInt64() : 0,
            SharedReadBlocks = e.TryGetProperty("Shared Read Blocks", out var rd) ? rd.GetInt64() : 0,
            HeapFetches = e.TryGetProperty("Heap Fetches", out var hf) ? hf.GetInt64() : null,
        };
        if (e.TryGetProperty("Plans", out var children))
            foreach (var child in children.EnumerateArray())
                node.Children.Add(Parse(child));
        return node;
    }
}

/// <summary>A statement, the plan PostgreSQL executed it with, and what that cost.</summary>
public sealed record ExplainedStatement(
    string Sql, string ParameterSummary, string TextPlan, PlanNode Root, double PlanningMs, double ExecutionMs)
{
    public IEnumerable<PlanNode> Nodes => Root.SelfAndDescendants();
}

public static class Explain
{
    /// <summary>Runs the captured command under EXPLAIN (ANALYZE, BUFFERS), text and JSON.</summary>
    public static async Task<ExplainedStatement> CommandAsync(
        NpgsqlDataSource dataSource, CapturedCommand command, CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        var json = await RunAsync(conn, "EXPLAIN (ANALYZE, BUFFERS, FORMAT JSON) " + command.Sql, command.Parameters, ct);
        var text = await RunAsync(conn, "EXPLAIN (ANALYZE, BUFFERS) " + command.Sql, command.Parameters, ct);
        return Build(command.Sql, Summarize(command.Parameters), text, json);
    }

    /// <summary>
    /// Reproduces what a prepared statement does after warm-up: PREPARE the captured SQL, EXECUTE it
    /// once per warm-up value, then EXPLAIN EXECUTE the target value — on the given data source, so
    /// connection-level settings (plan_cache_mode, for instance) apply.
    /// </summary>
    public static async Task<ExplainedStatement> PreparedAsync(
        NpgsqlDataSource dataSource, CapturedCommand command,
        IEnumerable<object[]> warmUpValues, object[] targetValues, CancellationToken ct)
    {
        var (sql, names) = ToPositional(command);
        // Only touch our own statement name: Npgsql tracks the statements it auto-prepared on
        // this connection, and DEALLOCATE ALL would pull them out from under it.
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await ExecAsync(conn, $"PREPARE lab_replay AS {sql}", ct);
        string json, text, genericPlans;
        try
        {
            foreach (var values in warmUpValues)
                await ExecAsync(conn, $"EXECUTE lab_replay({Literals(values)})", ct);
            var execute = $"EXECUTE lab_replay({Literals(targetValues)})";
            json = await RunAsync(conn, "EXPLAIN (ANALYZE, BUFFERS, FORMAT JSON) " + execute, [], ct);
            text = await RunAsync(conn, "EXPLAIN (ANALYZE, BUFFERS) " + execute, [], ct);
            genericPlans = await ScalarAsync(conn,
                "SELECT generic_plans || ' generic, ' || custom_plans || ' custom' FROM pg_prepared_statements WHERE name = 'lab_replay'", ct);
        }
        finally
        {
            await ExecAsync(conn, "DEALLOCATE lab_replay", CancellationToken.None);
        }
        var summary = $"PREPARE lab_replay; {string.Join(", ", names)} = {Literals(targetValues)} after " +
                      $"{warmUpValues.Count()} warm-up executions — plans so far: {genericPlans}";
        return Build(sql, summary, text, json);
    }

    private static ExplainedStatement Build(string sql, string parameters, string text, string json)
    {
        using var doc = JsonDocument.Parse(json);
        var top = doc.RootElement[0];
        return new ExplainedStatement(
            sql, parameters, text, PlanNode.Parse(top.GetProperty("Plan")),
            top.GetProperty("Planning Time").GetDouble(), top.GetProperty("Execution Time").GetDouble());
    }

    private static async Task<string> RunAsync(
        NpgsqlConnection conn, string sql, IReadOnlyList<NpgsqlParameter> parameters, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(sql, conn);
        foreach (var p in parameters) cmd.Parameters.Add(p.Clone());
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var sb = new StringBuilder();
        while (await reader.ReadAsync(ct)) sb.AppendLine(reader.GetString(0));
        return sb.ToString().TrimEnd();
    }

    private static async Task ExecAsync(NpgsqlConnection conn, string sql, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task<string> ScalarAsync(NpgsqlConnection conn, string sql, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(sql, conn);
        return (await cmd.ExecuteScalarAsync(ct))?.ToString() ?? "";
    }

    // EF Core writes @name placeholders; PREPARE needs $1, $2, … in parameter order.
    private static (string Sql, IReadOnlyList<string> Names) ToPositional(CapturedCommand command)
    {
        var names = command.Parameters.Select(p => p.ParameterName.TrimStart('@')).ToList();
        var sql = command.Sql;
        foreach (var (name, index) in names.Select((n, i) => (n, i)).OrderByDescending(x => x.n.Length))
            sql = Regex.Replace(sql, "@" + Regex.Escape(name) + @"\b", "$" + (index + 1));
        return (sql, names);
    }

    private static string Literals(object[] values) => string.Join(", ", values.Select(Literal));

    private static string Literal(object value) => value switch
    {
        string s => "'" + s.Replace("'", "''") + "'",
        DateTimeOffset d => "'" + d.ToString("O", CultureInfo.InvariantCulture) + "'",
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => "'" + value.ToString()?.Replace("'", "''") + "'",
    };

    private static string Summarize(IReadOnlyList<NpgsqlParameter> parameters) =>
        parameters.Count == 0
            ? "(no parameters)"
            : string.Join(", ", parameters.Select(p =>
                $"{p.ParameterName} = {FormatValue(p.Value)} ({p.DataTypeName ?? p.NpgsqlDbType.ToString()})"));

    private static string FormatValue(object? v) => v switch
    {
        null or DBNull => "NULL",
        string s => "'" + s + "'",
        DateTimeOffset d => d.ToString("u", CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => v.ToString() ?? "",
    };
}
