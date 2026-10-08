using Xunit;

namespace LearningPath.Tests;

// Part 1, Module 9 (Testing Essentials): the experiment runs as its own process, as a reader runs it.
// The test checks both halves: the in-memory provider saving a duplicate, and the relational engine
// refusing the same save, so a change in either direction turns it red.

public sealed class TestingEssentialsTests
{
    [Fact]
    public async Task The_in_memory_provider_saves_a_duplicate_that_a_unique_index_rejects_in_SQLite()
    {
        var run = await Experiment.RunAsync("InMemoryProvider");

        Assert.Equal(0, run.ExitCode);
        Assert.Equal(
            [
                "InMemory: saved 2 users with the same email",
                "SQLite  : SQLite Error 19: 'UNIQUE constraint failed: Users.Email'.",
            ],
            run.Lines);
    }
}
