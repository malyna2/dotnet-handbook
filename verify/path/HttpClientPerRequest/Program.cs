using System.Net.NetworkInformation;

// Prove it: a new HttpClient per request opens (and closes) one TCP connection per request, and
// every closed connection then sits in TIME_WAIT. A shared client reuses its pooled connections.
int port = Random.Shared.Next(20_000, 30_000);     // a fresh port, so earlier runs don't count
var builder = WebApplication.CreateSlimBuilder();
builder.Logging.ClearProviders();
builder.WebHost.UseUrls($"http://127.0.0.1:{port}");
var server = builder.Build();
server.MapGet("/", () => "ok");
await server.StartAsync();

var shared = new HttpClient();
await Measure("one shared HttpClient", () => shared.GetStringAsync($"http://127.0.0.1:{port}/"));
await Measure("new HttpClient per request", async () =>
{
    using var perRequest = new HttpClient();
    return await perRequest.GetStringAsync($"http://127.0.0.1:{port}/");
});

async Task Measure(string label, Func<Task<string>> call)
{
    int before = SocketsInTimeWait();
    for (int i = 0; i < 500; i++) await call();
    Console.WriteLine($"{label,-27} 500 requests, new sockets in TIME_WAIT: {SocketsInTimeWait() - before}");
}

int SocketsInTimeWait() => IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpConnections()
    .Count(c => c.State == TcpState.TimeWait && c.RemoteEndPoint.Port == port);
