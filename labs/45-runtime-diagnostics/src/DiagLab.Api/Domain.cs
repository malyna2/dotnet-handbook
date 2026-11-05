namespace DiagLab;

/// <summary>A slow downstream call: 100 ms of I/O, no CPU.</summary>
public static class PricingService
{
    public static async Task<decimal> GetPriceAsync(string sku)
    {
        await Task.Delay(100);
        return sku.Length * 10m;
    }
}

/// <summary>A process-wide price feed. Static, so it lives as long as the process — and so does every subscriber.</summary>
public static class PriceFeed
{
    public static event EventHandler<decimal>? PriceChanged;
    public static int SubscriberCount => PriceChanged?.GetInvocationList().Length ?? 0;
    public static void Publish(decimal price) => PriceChanged?.Invoke(null, price);
}

/// <summary>Watches prices for one request. Holds a 64 KB buffer to make each one visible on the heap.</summary>
public sealed class PriceWatcher : IDisposable
{
    private readonly byte[] _history = new byte[64 * 1024];
    public PriceWatcher() => PriceFeed.PriceChanged += OnPrice;
    public decimal Last { get; private set; }
    private void OnPrice(object? sender, decimal price) { Last = price; _history[0]++; }
    public void Dispose() => PriceFeed.PriceChanged -= OnPrice;
}
