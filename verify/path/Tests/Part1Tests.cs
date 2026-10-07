using System.Diagnostics;
using System.Text.RegularExpressions;
using Xunit;

namespace LearningPath.Tests;

// Part 1 experiments printed in the learning-path pages (chapters 70-84) run here exactly as a
// reader runs them: as their own process.
// Every test checks both halves of its experiment (the failure and the fix), so a change in
// either direction turns the suite red. Tests marked Requires=Docker need `docker compose up -d`.

public sealed class AsyncInternalsTests
{
    [Fact]
    public async Task Async_void_exception_bypasses_the_callers_catch_and_kills_the_process()
    {
        var run = await Experiment.RunAsync("AsyncVoid");

        Assert.Contains("async Task: the caller caught ArgumentOutOfRangeException", run.Stdout);
        Assert.Contains("async void: the call returned normally and the catch below never ran", run.Stdout);
        Assert.DoesNotContain("async void: the caller caught", run.Stdout);
        Assert.DoesNotContain("still alive", run.Stdout);
        Assert.NotEqual(0, run.ExitCode);
        Assert.Contains("Unhandled exception. System.ArgumentOutOfRangeException", run.Stderr);
        Assert.Contains("ThreadPoolWorkQueue.Dispatch", run.Stderr);   // rethrown on a thread-pool thread
    }

    [Fact]
    public async Task Await_on_WhenAll_throws_one_exception_while_the_task_holds_all()
    {
        var run = await Experiment.RunAsync("WhenAll");

        Assert.Equal(0, run.ExitCode);
        Assert.Equal(
            [
                "await threw:   InvalidOperationException: B (listed second, fails first)",
                "all.Exception: 2 inner exceptions",
                "  - B (listed second, fails first)",
                "  - A (listed first, fails last)",
                ".Wait() threw: AggregateException with 2 inner exceptions",
                "WhenAll over Task<int> threw: A (argument order this time)",
            ],
            run.Lines);
    }

    [Fact]
    public async Task Blocking_on_async_work_starves_the_pool_and_awaiting_does_not()
    {
        var awaited = Starvation.Parse(await Experiment.RunAsync("Starvation", "await"));
        var blocked = Starvation.Parse(await Experiment.RunAsync("Starvation", "block"));
        TestContext.Current.TestOutputHelper?.WriteLine($"await: {awaited}; block: {blocked}");

        Assert.True(awaited.Seconds < 3, $"awaiting should finish in about 1 s, took {awaited.Seconds} s");
        Assert.True(awaited.ProbeMs < 500, $"the unrelated request should not wait, waited {awaited.ProbeMs} ms");
        Assert.True(blocked.Seconds > 3 * awaited.Seconds, $"blocking should be several times slower: {blocked.Seconds} s vs {awaited.Seconds} s");
        Assert.True(blocked.ProbeMs > 2000, $"the unrelated request should wait seconds, waited {blocked.ProbeMs} ms");
        Assert.True(blocked.PeakThreads > 2 * awaited.PeakThreads, $"blocking should force thread injection: {blocked.PeakThreads} vs {awaited.PeakThreads}");
    }

    [Fact]
    public async Task New_HttpClient_per_request_leaves_one_TIME_WAIT_socket_per_request()
    {
        var run = await Experiment.RunAsync("HttpClientPerRequest");

        Assert.Equal(0, run.ExitCode);
        Assert.Equal(2, run.Lines.Length);
        Assert.InRange(TimeWait(run.Lines[0], "one shared HttpClient"), 0, 2);
        Assert.InRange(TimeWait(run.Lines[1], "new HttpClient per request"), 450, 500);

        static int TimeWait(string line, string label)
        {
            Assert.StartsWith(label, line);
            return int.Parse(Regex.Match(line, @"TIME_WAIT: (\d+)$").Groups[1].Value);
        }
    }
}

public sealed class MessagingTests
{
    [Fact]
    [Trait("Requires", "Docker")]
    public async Task A_handler_that_outlives_its_lock_gets_the_same_message_again()
    {
        var run = await Experiment.RunAsync("PeekLock");

        Assert.Equal(0, run.ExitCode);
        Assert.Equal(
            [
                "received order-42, DeliveryCount=1, locked for 5 s",
                "received order-42 again, DeliveryCount=2",
                "completing the first copy threw MessageLockLost; its charge already happened",
                "completed the second copy; message still in the queue: False",
            ],
            run.Lines);
    }
}

public sealed class IndexTests
{
    [Fact]
    [Trait("Requires", "Docker")]
    public async Task Only_sargable_predicates_on_a_leftmost_prefix_seek()
    {
        var run = await Experiment.RunAsync("SeekVsScan");
        Assert.Equal(0, run.ExitCode);
        Assert.Equal(10, run.Lines.Length);

        AssertPlan(run, 0, "WHERE Email = @p (@p NVarChar)", "Index Scan", "CONVERT_IMPLICIT(nvarchar(100),[Email],0)", scan: true);
        AssertPlan(run, 2, "WHERE Email = @p (@p VarChar)", "Index Seek", "SEEK:([Email]=[@p])", scan: false);
        AssertPlan(run, 4, "WHERE LOWER(Email) = @p (@p VarChar)", "Index Scan", "lower([Email])", scan: true);
        AssertPlan(run, 6, "WHERE City = @p AND CreatedAt >= '2025-12-31' (@p VarChar)", "Index Seek", "SEEK:([City]=[@p] AND [CreatedAt] >=", scan: false);
        AssertPlan(run, 8, "WHERE CreatedAt >= @p (@p DateTime2)", "Index Scan", "WHERE:([CreatedAt]>=[@p])", scan: true);

        static void AssertPlan(Experiment.Run run, int line, string query, string op, string detail, bool scan)
        {
            Assert.StartsWith(query + ": ", run.Lines[line]);
            int reads = int.Parse(Regex.Match(run.Lines[line], @": (\d+) logical reads$").Groups[1].Value);
            Assert.True(scan ? reads > 300 : reads < 10, $"{query}: {reads} logical reads");
            Assert.StartsWith("    |--" + op + "(", run.Lines[line + 1]);
            Assert.Contains(detail, run.Lines[line + 1]);
        }
    }
}

public sealed class CodeReviewTests
{
    [Fact]
    public async Task Check_then_act_charges_twice_and_claim_first_charges_once()
    {
        var run = await Experiment.RunAsync("CheckThenAct");

        Assert.Equal(0, run.ExitCode);
        Assert.Equal(
            [
                "check, then act: the card was charged 2 time(s)",
                "claim first:     the card was charged 1 time(s)",
            ],
            run.Lines);
    }
}

internal sealed record Starvation(int ProbeMs, double Seconds, int PeakThreads)
{
    public static Starvation Parse(Experiment.Run run)
    {
        Assert.Equal(0, run.ExitCode);
        var probe = Regex.Match(run.Stdout, @"a tiny unrelated request waited (\d+) ms for a thread");
        var done = Regex.Match(run.Stdout, @"done in ([\d.]+) s, peak pool threads (\d+)");
        Assert.True(probe.Success && done.Success, run.Stdout);
        return new(int.Parse(probe.Groups[1].Value),
            double.Parse(done.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture),
            int.Parse(done.Groups[2].Value));
    }
}
