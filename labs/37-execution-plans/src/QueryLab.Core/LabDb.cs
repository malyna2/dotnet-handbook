using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace QueryLab.Core;

/// <summary>Connections to the lab database, and EF Core contexts that record what they execute.</summary>
public sealed class LabDb : IAsyncDisposable
{
    /// <summary>The lab clock. The seed spreads orders over the three years before it.</summary>
    public static readonly DateTimeOffset Now = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);

    public const string DefaultConnectionString =
        "Host=localhost;Port=5433;Username=lab;Password=lab;Database=shop";

    private readonly Dictionary<NpgsqlDataSource, DbContextOptions<ShopContext>> _options = [];
    private NpgsqlDataSource? _autoPrepare;

    public LabDb(string connectionString)
    {
        ConnectionString = connectionString;
        DataSource = NpgsqlDataSource.Create(connectionString);
    }

    public string ConnectionString { get; }

    /// <summary>The plain data source: no automatic preparation, default pool.</summary>
    public NpgsqlDataSource DataSource { get; }

    public CommandLog Log { get; } = new();

    /// <summary>Warnings EF Core logged while running (it does warn about some of the rungs).</summary>
    public List<string> EfWarnings { get; } = [];

    /// <summary>
    /// The configuration a team turns on "for performance": Npgsql prepares any statement it has
    /// executed <c>Auto Prepare Min Usages</c> (default 5) times. One connection, so the warm-up and
    /// the measured calls share the same prepared statements, as a busy pooled connection would.
    /// </summary>
    public NpgsqlDataSource AutoPrepareDataSource =>
        _autoPrepare ??= NpgsqlDataSource.Create(ConnectionString + ";Max Auto Prepare=20;Maximum Pool Size=1");

    public ShopContext CreateContext(NpgsqlDataSource? dataSource = null)
    {
        dataSource ??= DataSource;
        if (!_options.TryGetValue(dataSource, out var options))
        {
            options = new DbContextOptionsBuilder<ShopContext>()
                .UseNpgsql(dataSource)
                .AddInterceptors(Log)
                .LogTo(message => { lock (EfWarnings) EfWarnings.Add(message); }, LogLevel.Warning)
                .Options;
            _options[dataSource] = options;
        }
        return new ShopContext(options);
    }

    /// <summary>Drops per-rung connections (and the statements prepared on them).</summary>
    public async Task ResetRungStateAsync()
    {
        if (_autoPrepare is not null)
        {
            _options.Remove(_autoPrepare);
            await _autoPrepare.DisposeAsync();
            _autoPrepare = null;
        }
        lock (EfWarnings) EfWarnings.Clear();
        Log.Clear();
    }

    public async Task<T> ScalarAsync<T>(string sql, CancellationToken ct = default)
    {
        await using var cmd = DataSource.CreateCommand(sql);
        return (T)(await cmd.ExecuteScalarAsync(ct))!;
    }

    public async Task ExecuteAsync(string sql, CancellationToken ct = default)
    {
        await using var cmd = DataSource.CreateCommand(sql);
        cmd.CommandTimeout = 0;
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async ValueTask DisposeAsync()
    {
        await ResetRungStateAsync();
        await DataSource.DisposeAsync();
    }
}
