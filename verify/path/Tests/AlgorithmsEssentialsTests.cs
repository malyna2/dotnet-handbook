using Xunit;

namespace LearningPath.Tests;

// Chapter 2 (Data Structures and Algorithms Essentials): the CollectionCosts experiment, run as its
// own process. Both halves are checked: the scan against the hash lookup, and the key the set finds
// by hash against the mutated key it can no longer find although it still holds it.
public sealed class AlgorithmsEssentialsTests
{
    [Fact]
    public async Task List_grows_by_doubling_and_a_scan_compares_every_item_while_a_set_compares_one()
    {
        var run = await Experiment.RunAsync("CollectionCosts");

        Assert.Equal(0, run.ExitCode);
        Assert.Equal(4, run.Lines.Length);
        Assert.Equal("capacities while adding 10,000: 4 8 16 32 64 128 256 512 1024 2048 4096 8192 16384", run.Lines[0]);
        Assert.Equal("list.Contains(9999): 10000 Equals calls (a scan)", run.Lines[1]);
        Assert.Equal("set.Contains(9999):  1 Equals call (hash, bucket, one compare)", run.Lines[2]);
    }

    [Fact]
    public async Task A_key_mutated_inside_a_hash_set_is_lost_to_lookups_but_still_held()
    {
        var run = await Experiment.RunAsync("CollectionCosts");

        Assert.Equal(0, run.ExitCode);
        Assert.Equal("after mutating key 42: set.Contains(it) False, set.Count 10000, still there by reference: True", run.Lines[3]);
    }
}
