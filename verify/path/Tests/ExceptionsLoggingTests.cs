using System.Text.RegularExpressions;
using Xunit;

namespace LearningPath.Tests;

// Part 1, Module 6 (Exceptions, Logging and First Diagnosis): both experiments run as their own
// process, as a reader runs them. Each test checks both halves: the trap and the fix.
public sealed class ExceptionsLoggingTests
{
    [Fact]
    public async Task Throw_keeps_the_origin_frame_and_throw_ex_restarts_the_trace_at_the_rethrow()
    {
        var run = await Experiment.RunAsync("ThrowVsThrowEx");
        Assert.Equal(0, run.ExitCode);
        Assert.Equal("throw;", run.Lines[0]);
        int split = Array.IndexOf(run.Lines, "throw ex;");
        Assert.True(split > 1, run.Stdout);

        // Every frame must point at the source line the page says it does.
        string[] source = File.ReadAllLines(Path.Combine(PathRoot(), "ThrowVsThrowEx", "Program.cs"));
        int LineOf(string code) => Array.FindIndex(source, l => l.TrimStart().StartsWith(code, StringComparison.Ordinal)) + 1;
        int main = LineOf("try { OrderService.Get(42");

        // throw; — the trace still starts in the method that threw, at the throw itself.
        Assert.Equal(
            [("OrderStore.Load", LineOf("public static void Load")), ("OrderService.Get", LineOf("try { OrderStore.Load(id); }")), ("Program.<Main>$", main)],
            Frames(run.Lines[1..split]));

        // throw ex; — the frames below the catch are gone: the trace starts at the rethrow.
        Assert.Equal(
            [("OrderService.Get", LineOf("throw ex;")), ("Program.<Main>$", main)],
            Frames(run.Lines[(split + 1)..]));
    }

    [Fact]
    public async Task A_template_keeps_a_named_typed_property_and_an_interpolated_string_arrives_flat()
    {
        var run = await Experiment.RunAsync("LogTemplate");

        Assert.Equal(0, run.ExitCode);
        Assert.Equal(
            [
                "message:  Order 42 placed",
                "  OrderId = 42 (Int32)",
                "  {OriginalFormat} = Order {OrderId} placed (String)",
                "message:  Order 42 placed",
                "  {OriginalFormat} = Order 42 placed (String)",
            ],
            run.Lines);
    }

    private static List<(string Method, int Line)> Frames(string[] lines) =>
        lines.Select(line =>
        {
            var m = Regex.Match(line, @"^   at ([\w.<>$]+)\(.*\) in .+:line (\d+)$");
            Assert.True(m.Success, $"not a stack frame with a line number: '{line}'");
            return (m.Groups[1].Value, int.Parse(m.Groups[2].Value));
        }).ToList();

    // verify/path, found the way Experiment finds it: the folder that holds check_path.py.
    private static string PathRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "check_path.py"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return dir.FullName;
    }
}
