// dotnet run --project src/QueryLab.Cli -- [all | 1 2 3 ...] [--runs N] [--no-plan] [--keep] [--connection "<Npgsql connection string>"]
using System.Globalization;
using System.Reflection;
using QueryLab.Core;

var runs = 5;
var showPlans = true;
var keep = false;
var connection = LabDb.DefaultConnectionString;
var selected = new List<int>();
for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--runs": runs = int.Parse(args[++i], CultureInfo.InvariantCulture); break;
        case "--no-plan": showPlans = false; break;
        case "--keep": keep = true; break;
        case "--connection": connection = args[++i]; break;
        case "all": break;
        default: selected.Add(int.Parse(args[i], CultureInfo.InvariantCulture)); break;
    }
}

var rungsAssembly = Assembly.Load("QueryLab.Rungs");
var impl = rungsAssembly.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == "Implementation")?.Value ?? "?";
var rungs = Harness.DiscoverRungs(rungsAssembly).Where(r => selected.Count == 0 || selected.Contains(r.Number)).ToList();

await using var lab = new LabDb(connection);
Console.WriteLine("# QueryLab — Lab 37, reading execution plans");
Console.WriteLine();
Console.WriteLine(await Harness.EnvironmentAsync(lab, default));
Console.WriteLine($"rungs       : {impl} implementation, {runs} measured calls per rung");

var failures = 0;
foreach (var rung in rungs)
{
    RungMeasurement m;
    try
    {
        m = await Harness.MeasureAsync(lab, rung, runs, showPlans);
    }
    catch (Exception ex)
    {
        // Keep going, and keep the evidence: a failed rung is reported in the output itself.
        failures++;
        Console.WriteLine();
        Console.WriteLine($"## Rung {rung.Number} — {rung.Title} ({impl})");
        Console.WriteLine();
        Console.WriteLine($"ERROR       : {ex.GetType().Name}: {ex.Message}");
        Console.Error.WriteLine($"Rung {rung.Number} failed: {ex}");
        continue;
    }
    Console.WriteLine();
    Console.WriteLine($"## Rung {rung.Number} — {rung.Title} ({impl})");
    Console.WriteLine();
    Console.WriteLine(rung.Scenario);
    Console.WriteLine();
    Console.WriteLine(m.AppliedFixes.Count == 0
        ? "fixes       : none"
        : $"fixes       : {string.Join(" ; ", m.AppliedFixes)}  ({m.FixDuration.TotalSeconds:F1} s to apply)");
    if (m.FixIndexes.Count > 0) Console.WriteLine($"index size  : {string.Join(", ", m.FixIndexes)}");
    Console.WriteLine($"wall time   : median {m.MedianMs:F1} ms over {m.CallMs.Count} calls (min {m.CallMs.Min():F1}, max {m.CallMs.Max():F1})");
    Console.WriteLine($"per call    : {m.PerCall.Statements:F0} statement(s), {m.PerCall.Rows:N0} rows returned, " +
                      $"shared buffers {m.PerCall.SharedHit:N0} hit + {m.PerCall.SharedRead:N0} read, " +
                      $"{m.PerCall.ExecMs:F1} ms server execution (pg_stat_statements)");
    Console.WriteLine($"statements  : {m.DistinctStatements} distinct SQL text(s) per call");
    foreach (var warning in m.EfWarnings)
    {
        // LogTo lines look like "warn: <timestamp> <event id> (<category>) \n <message>": keep the event and the message.
        var parts = warning.Split('\n', 2, StringSplitOptions.TrimEntries);
        var eventId = parts[0].Split(' ').FirstOrDefault(p => p.Contains("EventId.", StringComparison.Ordinal)) ?? parts[0];
        Console.WriteLine($"EF warning  : {eventId} — {(parts.Length > 1 ? parts[1] : "")}");
    }
    foreach (var plan in m.Plans)
    {
        Console.WriteLine();
        Console.WriteLine("```sql");
        Console.WriteLine(plan.Sql.Trim());
        Console.WriteLine("```");
        Console.WriteLine($"parameters  : {plan.ParameterSummary}");
        Console.WriteLine("```");
        Console.WriteLine(plan.TextPlan);
        Console.WriteLine("```");
    }
}

// Leave the database as seeded, so the next run, psql session or script starts from the baseline.
// --keep leaves the last rung's lab_* objects in place, for exploring its plan by hand.
if (!keep) await Harness.ResetSchemaAsync(lab, default);

return failures == 0 ? 0 : 1;
