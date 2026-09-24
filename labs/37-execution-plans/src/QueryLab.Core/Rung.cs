namespace QueryLab.Core;

/// <summary>
/// One slow query and the code around it. The contract classes in Contracts.cs fix each rung's
/// inputs and result; the implementations you edit live in starter/QueryLab.Rungs.
/// </summary>
public abstract class Rung
{
    public abstract int Number { get; }
    public abstract string Title { get; }

    /// <summary>What the code is for, in one sentence.</summary>
    public abstract string Scenario { get; }

    /// <summary>
    /// DDL your fix needs — indexes, statistics, ANALYZE. Name every index and statistics object
    /// <c>lab_*</c>: before each rung the harness drops all <c>lab_*</c> objects, so rungs stay
    /// independent of each other. Empty in the starter.
    /// </summary>
    public virtual IReadOnlyList<string> Fixes => [];

    /// <summary>Resolves inputs that come from the data (runs once, after the fixes are applied).</summary>
    public virtual Task SetUpAsync(LabDb lab, CancellationToken ct) => Task.CompletedTask;

    /// <summary>Untimed traffic before measuring. By default one call, so caches are warm.</summary>
    public virtual async Task WarmUpAsync(LabDb lab, CancellationToken ct) => await RunAsync(lab, ct);

    /// <summary>The call being measured.</summary>
    public abstract Task<object> RunAsync(LabDb lab, CancellationToken ct);

    /// <summary>
    /// Re-running EXPLAIN on the captured SQL shows the plan a fresh statement would get. When the
    /// server actually used a different one (a cached generic plan), a rung reproduces it here.
    /// </summary>
    public virtual Task<IReadOnlyList<ExplainedStatement>?> ExplainOverrideAsync(
        LabDb lab, IReadOnlyList<CapturedCommand> lastRun, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<ExplainedStatement>?>(null);

    public override string ToString() => $"Rung {Number}: {Title}";
}
