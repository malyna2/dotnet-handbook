using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Xunit.Sdk;
using Xunit.v3;

// Load tests measure the thread pool, which is per process: run them one at a time.
[assembly: Parallelization(Mode = ParallelMode.None)]

namespace Ch08.Exercises;

// Each defect is shown on the buggy code and shown absent on the fix, so the suite stays green
// and a regression in either direction turns it red. Requests are simulated by calling the
// action on a thread-pool thread, the way Kestrel would; the "I/O" is Task.Delay.

public sealed class StarvationTests
{
    [Fact]
    public async Task Buggy_controller_starves_the_thread_pool_under_concurrent_requests()
    {
        Load load = await Load.RunAsync(world => _ => world.Buggy().GetReport(1));
        TestContext.Current.TestOutputHelper?.WriteLine($"buggy: {load}");

        // 200 ms of report I/O and 4 × 200 ms of mail I/O per request should take well under a
        // second. Blocked threads make every request, and an unrelated one, wait for injection.
        Assert.True(load.Seconds > 3, $"expected starvation to stretch the run past 3 s: {load}");
        Assert.True(load.ProbeMs > 500, $"expected an unrelated request to wait for a thread: {load}");
    }

    [Fact]
    public async Task Fixed_controller_serves_the_same_load_without_starving()
    {
        Load load = await Load.RunAsync(world => ct => world.Fixed().GetReport(1, ct));
        TestContext.Current.TestOutputHelper?.WriteLine($"fixed: {load}");

        Assert.True(load.Seconds < 2, $"expected the fixed controller to finish in about half a second: {load}");
        Assert.True(load.ProbeMs < 250, $"expected an unrelated request to run at once: {load}");
    }
}

public sealed class CancellationTests
{
    private const int Recipients = 20;

    [Fact]
    public async Task Buggy_controller_keeps_mailing_after_the_client_has_gone()
    {
        var world = new World(recipients: Recipients, mailLatency: TimeSpan.FromMilliseconds(100));

        // There is no token to cancel: the client may disconnect at any point, and every email still goes.
        await Task.Run(() => world.Buggy().GetReport(1), TestContext.Current.CancellationToken);

        Assert.Equal(Recipients, world.Mailer.Sent);
    }

    [Fact]
    public async Task Fixed_controller_stops_mailing_when_the_request_is_aborted()
    {
        var world = new World(recipients: Recipients, mailLatency: TimeSpan.FromMilliseconds(100));
        using var aborted = new CancellationTokenSource(TimeSpan.FromMilliseconds(300)); // report done at 200 ms

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => world.Fixed().GetReport(1, aborted.Token));

        Assert.InRange(world.Mailer.Sent, 0, Recipients - 1);
    }
}

internal sealed record Load(double Seconds, long ProbeMs, int PeakThreads)
{
    // Requests arrive the way Kestrel queues them: from outside the pool, onto its global queue.
    // A dedicated thread does the queuing, so nothing lands in a pool thread's local queue.
    public static Task<Load> RunAsync(Func<World, Func<CancellationToken, object>> request) =>
        Task.Factory.StartNew(() => MeasureAsync(request), CancellationToken.None,
            TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap();

    private static async Task<Load> MeasureAsync(Func<World, Func<CancellationToken, object>> request)
    {
        var world = new World(recipients: 4, mailLatency: TimeSpan.FromMilliseconds(200));
        int requests = 5 * Environment.ProcessorCount;
        var clock = Stopwatch.StartNew();
        Task[] inFlight = Enumerable.Range(0, requests)
            .Select(_ => Task.Run(() => Complete(request(world)(CancellationToken.None))))
            .ToArray();

        var queued = Stopwatch.StartNew();
        long probeMs = await Task.Run(() => queued.ElapsedMilliseconds);   // an unrelated request: how long until it starts?

        int peak = 0;
        while (!inFlight.All(t => t.IsCompleted))
        {
            peak = Math.Max(peak, ThreadPool.ThreadCount);
            await Task.Delay(50);
        }
        await Task.WhenAll(inFlight);
        return new Load(Math.Round(clock.Elapsed.TotalSeconds, 1), probeMs, peak);
    }

    private static async Task Complete(object result)
    {
        if (result is Task task) await task;
    }
}

internal sealed class World
{
    private readonly DbContextOptions<ReportsDb> _options;

    public World(int recipients, TimeSpan mailLatency)
    {
        _options = new DbContextOptionsBuilder<ReportsDb>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        using var db = new ReportsDb(_options);
        db.Subscribers.AddRange(Enumerable.Range(1, recipients).Select(i => new Subscriber(i, 1, $"reader{i}@example.com")));
        db.SaveChanges();
        Mailer = new CountingMailer(mailLatency);
    }

    public CountingMailer Mailer { get; }
    public SlowReportService Reports { get; } = new(TimeSpan.FromMilliseconds(200));

    // A DbContext per request, as the scoped registration gives you.
    public BuggyReportsController Buggy() => new(Reports, new ReportsDb(_options), Mailer);
    public FixedReportsController Fixed() => new(Reports, new ReportsDb(_options), Mailer);
}

internal sealed class SlowReportService(TimeSpan latency) : IReportService
{
    public async Task<Report> BuildReportAsync(int id, CancellationToken ct = default)
    {
        await Task.Delay(latency, ct);
        return new Report(id, "Quarterly numbers");
    }
}

internal sealed class CountingMailer(TimeSpan latency) : IMailer
{
    private int _sent;
    public int Sent => Volatile.Read(ref _sent);

    public async Task SendAsync(string to, Report report, CancellationToken ct = default)
    {
        await Task.Delay(latency, ct);
        Interlocked.Increment(ref _sent);
    }
}
