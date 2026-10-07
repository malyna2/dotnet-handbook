using System.Diagnostics;

// Prove it: blocking on async work starves the thread pool; awaiting it does not.
// Run twice: `dotnet run -- await` and `dotnet run -- block`.
bool block = args.FirstOrDefault() == "block";
int requests = 50 * Environment.ProcessorCount;
var clock = Stopwatch.StartNew();

Task[] work = Enumerable.Range(0, requests).Select(_ => Task.Run(async () =>
{
    if (block) Task.Delay(1000).Wait();        // sync-over-async: the thread waits for the "I/O"
    else await Task.Delay(1000);               // async: the thread goes back to the pool
})).ToArray();

var probe = Stopwatch.StartNew();
await Task.Run(() => { });                     // one tiny unrelated request, queued behind them
Console.WriteLine($"a tiny unrelated request waited {probe.ElapsedMilliseconds} ms for a thread");

int peakThreads = 0;
while (!work.All(t => t.IsCompleted))
{
    peakThreads = Math.Max(peakThreads, ThreadPool.ThreadCount);
    await Task.Delay(50);
}
Console.WriteLine($"{requests} requests of 1 s each with {(block ? ".Wait()" : "await")}: " +
    $"done in {clock.Elapsed.TotalSeconds:F1} s, peak pool threads {peakThreads}");
