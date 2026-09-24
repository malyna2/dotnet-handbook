// Chapter 51, Exercises → Find the bug #1: read-modify-write on a blob without a concurrency check.
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace Ch51.Exercises;

public sealed class Inventory
{
    public Dictionary<string, int> Stock { get; set; } = new();
}

/// <summary>The sample as printed in the chapter.</summary>
public static class BuggyInventoryStore
{
    public static async Task ReserveAsync(BlobClient blob, string sku, int quantity, CancellationToken ct)
    {
        BlobDownloadResult current = await blob.DownloadContentAsync(ct);
        Inventory inventory = current.Content.ToObjectFromJson<Inventory>()!;

        if (inventory.Stock[sku] < quantity)
            throw new InvalidOperationException($"Not enough {sku} in stock.");

        inventory.Stock[sku] -= quantity;

        await blob.UploadAsync(BinaryData.FromObjectAsJson(inventory), overwrite: true, ct);
    }
}

/// <summary>The fix from the answer: optimistic concurrency on the blob's ETag, and retry on 412.</summary>
public static class FixedInventoryStore
{
    public static async Task ReserveAsync(BlobClient blob, string sku, int quantity, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            BlobDownloadResult current = await blob.DownloadContentAsync(ct);
            Inventory inventory = current.Content.ToObjectFromJson<Inventory>()!;

            if (inventory.Stock[sku] < quantity)
                throw new InvalidOperationException($"Not enough {sku} in stock.");

            inventory.Stock[sku] -= quantity;

            try
            {
                await blob.UploadAsync(
                    BinaryData.FromObjectAsJson(inventory),
                    new BlobUploadOptions
                    {
                        // Write only if nobody changed the blob since we read it.
                        Conditions = new BlobRequestConditions { IfMatch = current.Details.ETag }
                    },
                    ct);
                return;
            }
            catch (RequestFailedException e) when (e.Status == 412 && attempt < 5)
            {
                // Someone else won the race: re-read and re-apply against the new state.
            }
        }
    }
}
