using System.Collections.Concurrent;
using System.Diagnostics;
using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using Xunit;

namespace ServiceBusLab;

// Needs the emulator (verify.sh starts it). Every test creates its own queue.
// Lock durations and delivery counts are scaled down so the experiments run in seconds.
public sealed class ServiceBusTests
{
    private const string Emulator = "Endpoint=sb://localhost;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;";
    private const string EmulatorAdmin = "Endpoint=sb://localhost:5300;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;";
    private static readonly TimeSpan Lock = TimeSpan.FromSeconds(5);
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    // Observations go to the file named by LAB_OBSERVATIONS (verify.sh sets it) as well as to the test output.
    private static void Log(string line)
    {
        TestContext.Current.TestOutputHelper?.WriteLine(line);
        if (Environment.GetEnvironmentVariable("LAB_OBSERVATIONS") is { Length: > 0 } path)
            lock (typeof(ServiceBusTests)) File.AppendAllText(path, $"{TestContext.Current.Test?.TestDisplayName}: {line}{Environment.NewLine}");
    }

    [Fact] // Experiment 1: the "crash" between the work and Complete.
    public async Task A_message_not_completed_comes_back_after_the_lock_expires()
    {
        string queue = await CreateQueueAsync(o => o.MaxDeliveryCount = 10);
        await using var client = new ServiceBusClient(Emulator);
        await client.CreateSender(queue).SendMessageAsync(new ServiceBusMessage("pay-1") { MessageId = "pay-1" }, Ct);

        await using var first = client.CreateReceiver(queue);
        ServiceBusReceivedMessage m1 = (await first.ReceiveMessageAsync(TimeSpan.FromSeconds(10), Ct))!;
        var sw = Stopwatch.StartNew();
        // ...the side effect happens here, then the process dies: no Complete, no Abandon.

        await using var second = client.CreateReceiver(queue);
        ServiceBusReceivedMessage m2 = (await second.ReceiveMessageAsync(TimeSpan.FromSeconds(30), Ct))!;
        Log($"first delivery count {m1.DeliveryCount}, redelivered after {sw.Elapsed.TotalSeconds:F1} s with delivery count {m2.DeliveryCount}");

        Assert.Equal(m1.MessageId, m2.MessageId);
        Assert.Equal(m1.DeliveryCount + 1, m2.DeliveryCount);
        Assert.True(sw.Elapsed >= Lock - TimeSpan.FromSeconds(1), $"came back after {sw.Elapsed}, before the lock expired");
        await second.CompleteMessageAsync(m2, Ct);
    }

    [Fact] // Experiment 2: retries are counted, and the count has a limit.
    public async Task A_message_abandoned_MaxDeliveryCount_times_is_dead_lettered()
    {
        string queue = await CreateQueueAsync(o => o.MaxDeliveryCount = 3);
        await using var client = new ServiceBusClient(Emulator);
        await client.CreateSender(queue).SendMessageAsync(new ServiceBusMessage("pay-2"), Ct);
        await using var receiver = client.CreateReceiver(queue);
        for (int i = 0; i < 3; i++)
            await receiver.AbandonMessageAsync((await receiver.ReceiveMessageAsync(TimeSpan.FromSeconds(10), Ct))!, cancellationToken: Ct);

        await using var dlq = client.CreateReceiver(queue, new ServiceBusReceiverOptions { SubQueue = SubQueue.DeadLetter });
        ServiceBusReceivedMessage dead = (await dlq.ReceiveMessageAsync(TimeSpan.FromSeconds(10), Ct))!;
        Log($"dead-letter reason: {dead.DeadLetterReason}; delivery count {dead.DeliveryCount}");

        Assert.Null(await receiver.PeekMessageAsync(cancellationToken: Ct));
        Assert.Equal("MaxDeliveryCountExceeded", dead.DeadLetterReason);
    }

    [Fact] // Experiment 3: order is per session, and sessions run in parallel.
    public async Task Sessions_deliver_each_sessions_messages_in_order()
    {
        string queue = await CreateQueueAsync(o => o.RequiresSession = true);
        await using var client = new ServiceBusClient(Emulator);
        ServiceBusSender sender = client.CreateSender(queue);
        foreach (int step in Enumerable.Range(1, 5))
            foreach (string order in new[] { "order-A", "order-B", "order-C" })
                await sender.SendMessageAsync(new ServiceBusMessage($"{order}:{step}") { SessionId = order }, Ct);

        var seen = new ConcurrentDictionary<string, ConcurrentQueue<int>>();
        await using ServiceBusSessionProcessor processor = client.CreateSessionProcessor(queue,
            new ServiceBusSessionProcessorOptions { MaxConcurrentSessions = 3, MaxConcurrentCallsPerSession = 1 });
        processor.ProcessMessageAsync += async args =>
        {
            await Task.Delay(Random.Shared.Next(5, 50));   // uneven work must not reorder a session
            seen.GetOrAdd(args.SessionId, _ => new()).Enqueue(int.Parse(args.Message.Body.ToString().Split(':')[1]));
        };
        processor.ProcessErrorAsync += _ => Task.CompletedTask;
        await processor.StartProcessingAsync(Ct);
        await WaitUntilAsync(() => seen.Values.Sum(q => q.Count) == 15);
        await processor.StopProcessingAsync(Ct);

        foreach (var (session, steps) in seen) Log($"{session}: {string.Join(",", steps)}");
        Assert.All(seen.Values, steps => Assert.Equal(new[] { 1, 2, 3, 4, 5 }, steps.ToArray()));
    }

    [Fact] // Experiment 4: duplicate detection drops a second send of the same MessageId inside the window.
    public async Task Duplicate_detection_drops_a_resend_with_the_same_MessageId()
    {
        string queue = await CreateQueueAsync(o =>
        {
            o.RequiresDuplicateDetection = true;
            o.DuplicateDetectionHistoryTimeWindow = TimeSpan.FromMinutes(1);
        });
        await using var client = new ServiceBusClient(Emulator);
        ServiceBusSender sender = client.CreateSender(queue);
        await sender.SendMessageAsync(new ServiceBusMessage("charge") { MessageId = "payment-42" }, Ct);
        await sender.SendMessageAsync(new ServiceBusMessage("charge") { MessageId = "payment-42" }, Ct);   // a retried send
        await sender.SendMessageAsync(new ServiceBusMessage("charge") { MessageId = "payment-43" }, Ct);

        await using var receiver = client.CreateReceiver(queue, new ServiceBusReceiverOptions { ReceiveMode = ServiceBusReceiveMode.ReceiveAndDelete });
        var ids = new List<string>();
        while (await receiver.ReceiveMessageAsync(TimeSpan.FromSeconds(3), Ct) is { } m) ids.Add(m.MessageId);
        Log($"received: {string.Join(",", ids)}");
        Assert.Equal(new[] { "payment-42", "payment-43" }, ids);
    }

    [Fact] // Experiment 5: scheduled and deferred messages.
    public async Task A_scheduled_message_is_invisible_until_its_time_and_a_deferred_one_needs_its_sequence_number()
    {
        string queue = await CreateQueueAsync(_ => { });
        await using var client = new ServiceBusClient(Emulator);
        ServiceBusSender sender = client.CreateSender(queue);
        await sender.ScheduleMessageAsync(new ServiceBusMessage("reminder"), DateTimeOffset.UtcNow.AddSeconds(5), Ct);
        await using var receiver = client.CreateReceiver(queue);
        Assert.Null(await receiver.ReceiveMessageAsync(TimeSpan.FromSeconds(1), Ct));          // not yet
        ServiceBusReceivedMessage due = (await receiver.ReceiveMessageAsync(TimeSpan.FromSeconds(15), Ct))!;
        Assert.Equal("reminder", due.Body.ToString());

        await receiver.DeferMessageAsync(due, cancellationToken: Ct);                            // set aside
        Assert.Null(await receiver.ReceiveMessageAsync(TimeSpan.FromSeconds(1), Ct));          // a normal receive won't see it
        ServiceBusReceivedMessage back = (await receiver.ReceiveDeferredMessageAsync(due.SequenceNumber, Ct))!;
        await receiver.CompleteMessageAsync(back, Ct);
    }

    [Fact] // The exercise: the starter fails this, the solution passes.
    public async Task A_declined_card_is_charged_once_and_dead_lettered_with_a_reason()
    {
        string queue = await CreateQueueAsync(o => o.MaxDeliveryCount = 5);
        await using var client = new ServiceBusClient(Emulator);
        await client.CreateSender(queue).SendMessageAsync(new ServiceBusMessage("payment-7"), Ct);

        int charges = 0;
        await using ServiceBusProcessor processor = client.CreateProcessor(queue, new ServiceBusProcessorOptions { MaxConcurrentCalls = 1 });
        processor.ProcessMessageAsync += args => PaymentHandler.HandleAsync(args, _ =>
        {
            Interlocked.Increment(ref charges);
            throw new CardDeclinedException("Insufficient funds");
        });
        processor.ProcessErrorAsync += _ => Task.CompletedTask;
        await processor.StartProcessingAsync(Ct);

        await using var dlq = client.CreateReceiver(queue, new ServiceBusReceiverOptions { SubQueue = SubQueue.DeadLetter });
        ServiceBusReceivedMessage? dead = await dlq.ReceiveMessageAsync(TimeSpan.FromSeconds(60), Ct);
        await processor.StopProcessingAsync(Ct);
        Log($"charges: {charges}; dead-letter reason: {dead?.DeadLetterReason}");

        Assert.NotNull(dead);
        Assert.Equal("CardDeclined", dead.DeadLetterReason);
        Assert.Equal(1, charges);
    }

    private static async Task<string> CreateQueueAsync(Action<CreateQueueOptions> configure)
    {
        var options = new CreateQueueOptions("lab-" + Guid.NewGuid().ToString("N")[..8]) { LockDuration = Lock };
        configure(options);
        await new ServiceBusAdministrationClient(EmulatorAdmin).CreateQueueAsync(options, Ct);
        return options.Name;
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        while (!condition()) await Task.Delay(100, deadline.Token);
    }
}
