using Xunit;

namespace LearningPath.Tests;

// Part 1, Module 7 (C# Essentials): the experiment printed on chapters/77-p1-csharp-essentials.md,
// run as its own process. Both halves are checked: the query that runs on every enumeration and
// the materialized list that runs once; the late-bound captured variable and the list's snapshot.
public sealed class CSharpEssentialsTests
{
    [Fact]
    public async Task A_deferred_query_runs_on_every_enumeration_and_ToList_runs_it_once()
    {
        var run = await Experiment.RunAsync("DeferredExecution");

        Assert.Equal(0, run.ExitCode);
        Assert.Equal(5, run.Lines.Length);
        Assert.Equal("query defined:        the predicate ran 0 times", run.Lines[0]);
        Assert.Equal("Count() and Sum():    the predicate ran 8 times (count 3, sum 9)", run.Lines[1]);
        Assert.Equal("ToList(), then both:  the predicate ran 4 times (count 3, sum 9)", run.Lines[2]);
    }

    [Fact]
    public async Task A_closure_reads_the_captured_variable_when_the_query_runs()
    {
        var run = await Experiment.RunAsync("DeferredExecution");

        Assert.Equal(0, run.ExitCode);
        Assert.Equal("after minimum = 4:    query [4], list [2, 3, 4]", run.Lines[3]);

        // The expression tree refers to the captured variable as a field of a compiler-generated
        // class, not to the value 2 or 4. The class name is the compiler's, so only its shape is checked.
        Assert.StartsWith("an IQueryable holds an expression tree: System.Int32[].Where(n => (n >= value(", run.Lines[4]);
        Assert.EndsWith(").minimum))", run.Lines[4]);
    }
}
