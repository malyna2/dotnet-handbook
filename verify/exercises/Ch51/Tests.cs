using System.Collections.Concurrent;
using Azure;
using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Xunit;

namespace Ch51.Exercises;

// Each defect is shown on the buggy code and shown absent on the fix, so the suite stays green
// and a regression in either direction turns it red. Needs the emulators from docker-compose.yml.

public sealed class BlobLostUpdateTests
{
    private const string Azurite = "UseDevelopmentStorage=true";

    [Fact]
    public async Task Buggy_store_silently_loses_a_concurrent_reservation()
    {
        var (racing, reader) = await ArrangeRaceAsync(competitor => BuggyInventoryStore.ReserveAsync(competitor, "sku-1", 3, default));

        await BuggyInventoryStore.ReserveAsync(racing, "sku-1", 2, TestContext.Current.CancellationToken);

        // 10 - 3 - 2 should be 5. The competitor's reservation of 3 was overwritten: no error, wrong stock.
        Assert.Equal(8, await ReadStockAsync(reader));
    }

    [Fact]
    public async Task Fixed_store_detects_the_race_with_the_etag_and_keeps_both_reservations()
    {
        var (racing, reader) = await ArrangeRaceAsync(competitor => FixedInventoryStore.ReserveAsync(competitor, "sku-1", 3, default));

        await FixedInventoryStore.ReserveAsync(racing, "sku-1", 2, TestContext.Current.CancellationToken);

        Assert.Equal(5, await ReadStockAsync(reader));
        Assert.Equal(1, racing.PreconditionFailures);
    }

    private static async Task<(InterleavingBlobClient Racing, BlobClient Reader)> ArrangeRaceAsync(Func<BlobClient, Task> competitorWrite)
    {
        var container = new BlobContainerClient(Azurite, "inv-" + Guid.NewGuid().ToString("N"));
        await container.CreateAsync();
        var seed = new Inventory { Stock = { ["sku-1"] = 10 } };
        await container.GetBlobClient("inventory.json").UploadAsync(BinaryData.FromObjectAsJson(seed));

        var competitor = new BlobClient(Azurite, container.Name, "inventory.json");
        // The competitor (another instance of the same service) completes its whole
        // read-modify-write between our read and our write: the classic interleaving.
        var racing = new InterleavingBlobClient(Azurite, container.Name, "inventory.json", () => competitorWrite(competitor));
        return (racing, new BlobClient(Azurite, container.Name, "inventory.json"));
    }

    private static async Task<int> ReadStockAsync(BlobClient reader)
    {
        BlobDownloadResult result = await reader.DownloadContentAsync();
        return result.Content.ToObjectFromJson<Inventory>()!.Stock["sku-1"];
    }
}

/// <summary>A BlobClient that lets another writer run right after its first read.</summary>
public sealed class InterleavingBlobClient(string connectionString, string container, string blob, Func<Task> afterFirstRead)
    : BlobClient(connectionString, container, blob)
{
    private bool _raced;
    public int PreconditionFailures { get; private set; }

    public override async Task<Response<BlobDownloadResult>> DownloadContentAsync(CancellationToken cancellationToken = default)
    {
        Response<BlobDownloadResult> result = await base.DownloadContentAsync(cancellationToken);
        if (!_raced)
        {
            _raced = true;
            await afterFirstRead();
        }
        return result;
    }

    public override async Task<Response<BlobContentInfo>> UploadAsync(BinaryData content, BlobUploadOptions options, CancellationToken cancellationToken = default)
    {
        try
        {
            return await base.UploadAsync(content, options, cancellationToken);
        }
        catch (RequestFailedException e) when (e.Status == 412)
        {
            PreconditionFailures++;
            throw;
        }
    }
}

public sealed class LockExpiryTests
{
    private const string Emulator = "Endpoint=sb://localhost;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;";
    private const string EmulatorAdmin = "Endpoint=sb://localhost:5300;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;";
    private const int MessageCount = 12;

    // Scaled down so the test runs in seconds: a 5-second lock (the emulator's minimum) and a
    // 1-second handler, instead of the chapter's 1-minute default lock and 2-second handler.
    private static readonly TimeSpan LockDuration = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan HandlerTime = TimeSpan.FromSeconds(1);

    [Fact]
    public async Task Buggy_drainer_loses_locks_and_handles_orders_twice()
    {
        string queue = await CreateQueueAndSendAsync();
        await using var client = new ServiceBusClient(Emulator);
        await using ServiceBusReceiver receiver = client.CreateReceiver(queue);
        var handler = new CountingHandler(HandlerTime);
        var drainer = new BuggyOrderDrainer(receiver, handler);
        int lockLost = 0;

        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        while (!await IsDrainedAsync(receiver, handler))
        {
            try
            {
                await drainer.DrainOnceAsync(deadline.Token);
            }
            catch (ServiceBusException e) when (e.Reason == ServiceBusFailureReason.MessageLockLost)
            {
                lockLost++;
            }
        }

        TestContext.Current.TestOutputHelper?.WriteLine(
            $"lock-lost completes: {lockLost}; handler calls: {handler.Calls.Values.Sum()} for {MessageCount} messages; " +
            $"handled more than once: {handler.Calls.Values.Count(c => c > 1)}");

        Assert.True(lockLost > 0, "expected CompleteMessageAsync to fail with MessageLockLost");
        Assert.Contains(handler.Calls.Values, calls => calls > 1);
        Assert.True(handler.Calls.Values.Sum() > MessageCount,
            $"expected duplicate handling, got {handler.Calls.Values.Sum()} calls for {MessageCount} messages");
    }

    [Fact]
    public async Task Fixed_processor_renews_locks_and_handles_each_order_once()
    {
        string queue = await CreateQueueAndSendAsync();
        await using var client = new ServiceBusClient(Emulator);
        var handler = new CountingHandler(HandlerTime);
        await using ServiceBusProcessor processor = FixedOrderProcessor.Create(client, queue, handler);

        await processor.StartProcessingAsync(TestContext.Current.CancellationToken);
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        while (handler.Calls.Count < MessageCount)
            await Task.Delay(250, deadline.Token);
        await Task.Delay(LockDuration * 2, TestContext.Current.CancellationToken);   // long enough for any expired lock to show up as a redelivery
        await processor.StopProcessingAsync(TestContext.Current.CancellationToken);

        Assert.Equal(MessageCount, handler.Calls.Count);
        Assert.All(handler.Calls.Values, calls => Assert.Equal(1, calls));
    }

    private static async Task<string> CreateQueueAndSendAsync()
    {
        string queue = "orders-" + Guid.NewGuid().ToString("N")[..8];
        var admin = new ServiceBusAdministrationClient(EmulatorAdmin);
        await admin.CreateQueueAsync(new CreateQueueOptions(queue) { LockDuration = LockDuration, MaxDeliveryCount = 10 }, TestContext.Current.CancellationToken);

        await using var client = new ServiceBusClient(Emulator);
        await using ServiceBusSender sender = client.CreateSender(queue);
        for (int i = 0; i < MessageCount; i++)
            await sender.SendMessageAsync(new ServiceBusMessage($"order {i}") { MessageId = $"order-{i}" }, TestContext.Current.CancellationToken);
        return queue;
    }

    private static async Task<bool> IsDrainedAsync(ServiceBusReceiver receiver, CountingHandler handler)
    {
        if (handler.Calls.Count < MessageCount) return false;
        // Messages whose lock expired come back after the lock duration; wait it out before declaring empty.
        await Task.Delay(LockDuration + TimeSpan.FromSeconds(1));
        return (await receiver.PeekMessageAsync()) is null;
    }

    private sealed class CountingHandler(TimeSpan work) : IOrderHandler
    {
        public ConcurrentDictionary<string, int> Calls { get; } = new();

        public async Task HandleAsync(ServiceBusReceivedMessage message, CancellationToken ct)
        {
            Calls.AddOrUpdate(message.MessageId, 1, (_, n) => n + 1);
            await Task.Delay(work, ct);
        }
    }
}
