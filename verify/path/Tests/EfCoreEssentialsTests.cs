using Xunit;

namespace LearningPath.Tests;

// Part 1, Module 2 (EF Core Essentials), run as a reader runs it: as its own process. The test checks
// both halves: the loop's 1 + N statements, and the single statement that Include and a projection
// send for the same 100 lines. The last line is the trap of loading neither: not slow, silently empty.
public sealed class EfCoreEssentialsTests
{
    [Fact]
    public async Task Loading_children_parent_by_parent_sends_one_statement_each_and_Include_or_a_projection_sends_one()
    {
        var run = await Experiment.RunAsync("NPlusOne");

        Assert.Equal(0, run.ExitCode);
        Assert.Equal(
            [
                "Load() each order's lines in a loop    100 order lines, 51 SQL statement(s)",
                "Include(o => o.Lines)                  100 order lines,  1 SQL statement(s)",
                "Select(o => new { o.Id, Skus = ... })  100 order lines,  1 SQL statement(s)",
                "neither, and no lazy loading             0 order lines,  1 SQL statement(s)",
            ],
            run.Lines);
    }
}
