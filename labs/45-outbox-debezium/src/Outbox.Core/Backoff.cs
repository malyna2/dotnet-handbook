namespace Outbox.Core;

/// <summary>Exponential backoff with full jitter: a random delay between 0 and min(cap, base·2^attempt).</summary>
public static class Backoff
{
    public static TimeSpan Delay(int attempt, TimeSpan baseDelay, TimeSpan cap, Random? random = null)
    {
        double ceiling = Math.Min(cap.TotalMilliseconds, baseDelay.TotalMilliseconds * Math.Pow(2, attempt));
        return TimeSpan.FromMilliseconds((random ?? Random.Shared).NextDouble() * ceiling);
    }
}
