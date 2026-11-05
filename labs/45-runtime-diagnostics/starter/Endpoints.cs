namespace DiagLab;

// STARTER — two production bugs. Find them with the tools before you read the code closely.
public static class Endpoints
{
    // Under load, latency climbs while CPU stays low.
    public static Task<decimal> QuoteAsync(string sku)
    {
        decimal price = PricingService.GetPriceAsync(sku).Result;
        return Task.FromResult(price);
    }

    // Memory grows with every request and never comes back.
    public static IResult WatchPrices()
    {
        var watcher = new PriceWatcher();
        PriceFeed.Publish(42m);
        return Results.Ok(watcher.Last);
    }
}
