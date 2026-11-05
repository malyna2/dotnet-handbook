using System.Diagnostics;
using DiagLab;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace DiagLab.Tests;

// The app runs in-process (WebApplicationFactory), so it shares this process's thread pool:
// blocking in the endpoint starves the same pool the test's requests need.
[Collection("serial")] // the two tests read process-wide state
public sealed class DiagTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Two_hundred_concurrent_quotes_finish_in_about_two_round_trips()
    {
        HttpClient client = factory.CreateClient();
        await client.GetStringAsync("/quote?sku=warmup", Ct);

        var sw = Stopwatch.StartNew();
        await Task.WhenAll(Enumerable.Range(0, 200).Select(_ => client.GetStringAsync("/quote?sku=abc", Ct)));
        sw.Stop();

        // 200 requests × 100 ms of I/O. Async: the waits overlap, and it takes a few hundred ms.
        // Blocking: each request holds a pool thread for 100 ms, and the pool adds threads slowly.
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(3), $"200 quotes took {sw.Elapsed.TotalSeconds:F1} s; threads now {ThreadPool.ThreadCount}");
    }

    [Fact]
    public async Task A_watcher_does_not_outlive_its_request()
    {
        HttpClient client = factory.CreateClient();
        int before = PriceFeed.SubscriberCount;
        for (int i = 0; i < 100; i++)
            (await client.PostAsync("/watchers", null, Ct)).EnsureSuccessStatusCode();

        int leaked = PriceFeed.SubscriberCount - before;
        Assert.True(leaked == 0, $"{leaked} watchers are still subscribed to the static PriceFeed, and rooted by it");
    }
}
