using Xunit;

namespace LearningPath.Tests;

// Chapter 3 (How .NET Runs Your Code): the GcGenerations experiment, run as its own process. Both
// halves are checked: the plain object reclaimed by the first collection, and the finalizable one
// that survives it until its finalizer has run and a second collection reclaims it.
public sealed class RuntimeEssentialsTests
{
    [Fact]
    public async Task A_survivor_is_promoted_one_generation_per_collection()
    {
        var run = await Experiment.RunAsync("GcGenerations");

        Assert.Equal(0, run.ExitCode);
        Assert.Equal("a live object:       gen 0 -> gen 1 -> gen 2  (one step per collection it survives)", run.Lines[0]);
    }

    [Fact]
    public async Task A_finalizable_object_needs_a_second_collection_and_a_plain_one_does_not()
    {
        var run = await Experiment.RunAsync("GcGenerations");

        Assert.Equal(0, run.ExitCode);
        Assert.Equal(
            [
                "after 1 collection:  plain alive False, with finalizer alive True",
                "finalizer has run:   True",
                "after 2 collections: plain alive False, with finalizer alive False",
            ],
            run.Lines[1..]);
    }
}
