using QueryLab.Core;
using QueryLab.Tests;
using Testcontainers.PostgreSql;
using Xunit;

// One database for the whole run. All rung tests live in one class, and xUnit runs the tests of a
// class one at a time — they must not overlap, because each rung changes the schema.
[assembly: AssemblyFixture(typeof(LabDatabase))]

namespace QueryLab.Tests;

/// <summary>A throwaway PostgreSQL 18.6, configured like the compose file and seeded at the small scale.</summary>
public sealed class LabDatabase : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:18.6")
        .WithUsername("lab")
        .WithPassword("lab")
        .WithDatabase("shop")
        .WithCommand(
            "-c", "shared_preload_libraries=pg_stat_statements",
            "-c", "shared_buffers=256MB",
            "-c", "random_page_cost=1.1")
        .WithResourceMapping(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "seed.sql")), "/lab/seed.sql")
        .Build();

    public LabDb Lab { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();
        var seed = await _container.ExecAsync(["psql", "-U", "lab", "-d", "shop", "-v", "scale=small", "-f", "/lab/seed.sql"]);
        if (seed.ExitCode != 0)
            throw new InvalidOperationException("Seeding failed:\n" + seed.Stderr);
        Lab = new LabDb(_container.GetConnectionString());
    }

    public async ValueTask DisposeAsync()
    {
        if (Lab is not null) await Lab.DisposeAsync();
        await _container.DisposeAsync();
    }
}
