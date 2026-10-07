using System.Collections.Concurrent;

// Prove it: review the two handlers below BEFORE running this. Both "deduplicate" a message
// that the broker delivered twice; the two copies are being processed at the same time.
int charges = 0;

var seen = new ConcurrentDictionary<string, bool>();
async Task CheckThenAct(string messageId)
{
    if (seen.ContainsKey(messageId)) return;        // check …
    await ChargeCardAsync();                        // … the effect …
    seen[messageId] = true;                         // … then remember the id
}

var claimed = new ConcurrentDictionary<string, bool>();
async Task ClaimFirst(string messageId)
{
    if (!claimed.TryAdd(messageId, true)) return;  // one atomic step: only one copy can win
    await ChargeCardAsync();
}

await Task.WhenAll(CheckThenAct("order-42"), CheckThenAct("order-42"));
Console.WriteLine($"check, then act: the card was charged {charges} time(s)");

charges = 0;
await Task.WhenAll(ClaimFirst("order-42"), ClaimFirst("order-42"));
Console.WriteLine($"claim first:     the card was charged {charges} time(s)");

async Task ChargeCardAsync() { await Task.Delay(100); Interlocked.Increment(ref charges); }
