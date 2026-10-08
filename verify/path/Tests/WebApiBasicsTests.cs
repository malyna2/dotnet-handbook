using Xunit;

namespace LearningPath.Tests;

// Part 1, Module 8 (Web API Basics): the experiment runs as its own process, as a reader runs it.
// The test checks both halves: the captive instance shared across scopes, and the refusal under
// ValidateScopes, so a change in either direction turns it red.

public sealed class WebApiBasicsTests
{
    [Fact]
    public async Task A_singleton_keeps_one_scoped_instance_for_every_scope_and_ValidateScopes_refuses_it()
    {
        var run = await Experiment.RunAsync("CaptiveDependency");

        Assert.Equal(0, run.ExitCode);
        Assert.Equal(
            [
                "request 1: its own AppDb #1, the singleton's AppDb #2",
                "request 2: its own AppDb #3, the singleton's AppDb #2",
                "ValidateScopes = true: InvalidOperationException: Cannot consume scoped service 'AppDb' from singleton 'PriceCache'.",
            ],
            run.Lines);
    }
}
