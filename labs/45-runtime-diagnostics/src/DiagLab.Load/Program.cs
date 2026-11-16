using System.Diagnostics;

// dotnet run --project src/DiagLab.Load -- <url> <requests> <concurrency> [GET|POST]
string url = args.ElementAtOrDefault(0) ?? "http://localhost:5080/quote?sku=abc";
int total = int.Parse(args.ElementAtOrDefault(1) ?? "400");
int concurrency = int.Parse(args.ElementAtOrDefault(2) ?? "100");
var method = new HttpMethod(args.ElementAtOrDefault(3) ?? "GET");

using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
var latencies = new List<double>();
var wall = Stopwatch.StartNew();
await Parallel.ForEachAsync(Enumerable.Range(0, total), new ParallelOptions { MaxDegreeOfParallelism = concurrency }, async (_, ct) =>
{
    var sw = Stopwatch.StartNew();
    using var response = await http.SendAsync(new HttpRequestMessage(method, url), ct);
    response.EnsureSuccessStatusCode();
    lock (latencies) latencies.Add(sw.Elapsed.TotalMilliseconds);
});
latencies.Sort();
double P(double q) => latencies[(int)Math.Min(latencies.Count - 1, q * latencies.Count)];
Console.WriteLine($"{total} requests, {concurrency} concurrent, {wall.Elapsed.TotalSeconds:F1} s: " +
                  $"p50 {P(0.50):F0} ms, p95 {P(0.95):F0} ms, max {latencies[^1]:F0} ms, {total / wall.Elapsed.TotalSeconds:F0} req/s");
