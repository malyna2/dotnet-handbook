using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace QueryLab.Core;

/// <summary>A command EF Core sent, with copies of its parameters (types included).</summary>
public sealed record CapturedCommand(string Sql, IReadOnlyList<NpgsqlParameter> Parameters);

/// <summary>
/// Records every command EF Core executes while <see cref="Enabled"/> is on, so the harness can
/// count them and re-run each one under EXPLAIN with exactly the same parameter values and types.
/// </summary>
public sealed class CommandLog : DbCommandInterceptor
{
    private readonly List<CapturedCommand> _commands = [];
    private readonly Lock _gate = new();

    public bool Enabled { get; set; }

    public IReadOnlyList<CapturedCommand> Commands
    {
        get { lock (_gate) return [.. _commands]; }
    }

    public void Clear()
    {
        lock (_gate) _commands.Clear();
    }

    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        Capture(command);
        return result;
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        Capture(command);
        return ValueTask.FromResult(result);
    }

    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<object> result,
        CancellationToken cancellationToken = default)
    {
        Capture(command);
        return ValueTask.FromResult(result);
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Capture(command);
        return ValueTask.FromResult(result);
    }

    private void Capture(DbCommand command)
    {
        if (!Enabled) return;
        var parameters = command.Parameters.OfType<NpgsqlParameter>().Select(p => p.Clone()).ToList();
        lock (_gate) _commands.Add(new CapturedCommand(command.CommandText, parameters));
    }
}
