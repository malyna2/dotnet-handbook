namespace DiagLab;

// SOLUTION
public static class Endpoints
{
    // Async all the way: the await frees the pool thread for the 100 ms of I/O.
    public static async Task<decimal> QuoteAsync(string sku) => await PricingService.GetPriceAsync(sku);

    // Dispose unsubscribes: the static event no longer roots the watcher and its buffer.
    public static IResult WatchPrices()
    {
        using var watcher = new PriceWatcher();
        PriceFeed.Publish(42m);
        return Results.Ok(watcher.Last);
    }
}
