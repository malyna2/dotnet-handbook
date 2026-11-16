using DiagLab;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.SetMinimumLevel(LogLevel.Warning);
var app = builder.Build();

app.MapGet("/quote", Endpoints.QuoteAsync);              // the starvation lab
app.MapPost("/watchers", Endpoints.WatchPrices);          // the leak lab
app.MapGet("/stats", () => new
{
    PriceFeed.SubscriberCount,
    ThreadPool.ThreadCount,
    ThreadPool.PendingWorkItemCount,
    HeapMb = GC.GetTotalMemory(forceFullCollection: true) / 1_048_576,
});
app.Run();

public partial class Program;
