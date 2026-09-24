// Chapter 51, Exercises → Find the bug #2: a big receive batch processed one by one outlives its locks.
using Azure.Messaging.ServiceBus;

namespace Ch51.Exercises;

public interface IOrderHandler
{
    Task HandleAsync(ServiceBusReceivedMessage message, CancellationToken ct);
}

/// <summary>The sample as printed in the chapter.</summary>
public sealed class BuggyOrderDrainer(ServiceBusReceiver receiver, IOrderHandler handler)
{
    public async Task DrainOnceAsync(CancellationToken ct)
    {
        IReadOnlyList<ServiceBusReceivedMessage> batch =
            await receiver.ReceiveMessagesAsync(maxMessages: 50, maxWaitTime: TimeSpan.FromSeconds(5), ct);

        foreach (ServiceBusReceivedMessage message in batch)
        {
            await handler.HandleAsync(message, ct);          // calls a partner API, ~2 s
            await receiver.CompleteMessageAsync(message, ct);
        }
    }
}

/// <summary>
/// The fix from the answer: a processor, which renews each message's lock while the handler runs
/// (MaxAutoLockRenewalDuration, 5 minutes by default) and receives only as many messages as it can work on.
/// </summary>
public static class FixedOrderProcessor
{
    public static ServiceBusProcessor Create(ServiceBusClient client, string queue, IOrderHandler handler)
    {
        ServiceBusProcessor processor = client.CreateProcessor(queue, new ServiceBusProcessorOptions
        {
            MaxConcurrentCalls = 4,
            PrefetchCount = 0,
            AutoCompleteMessages = true,
            MaxAutoLockRenewalDuration = TimeSpan.FromMinutes(5),
        });

        processor.ProcessMessageAsync += args => handler.HandleAsync(args.Message, args.CancellationToken);
        processor.ProcessErrorAsync += _ => Task.CompletedTask;
        return processor;
    }
}
