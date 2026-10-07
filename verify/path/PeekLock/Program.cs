using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;

// Prove it: peek-lock is a lease, not a hand-over. A handler that outlives the lock gets the same
// message again. Needs the Service Bus emulator: `docker compose up -d` in this folder.
const string Emulator = "Endpoint=sb://localhost;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;";
string queue = "payments-" + Guid.NewGuid().ToString("N")[..8];
var admin = new ServiceBusAdministrationClient(Emulator.Replace("sb://localhost", "sb://localhost:5300"));
await admin.CreateQueueAsync(new CreateQueueOptions(queue) { LockDuration = TimeSpan.FromSeconds(5) });

await using var client = new ServiceBusClient(Emulator);
await client.CreateSender(queue).SendMessageAsync(new ServiceBusMessage("charge order 42") { MessageId = "order-42" });
ServiceBusReceiver receiver = client.CreateReceiver(queue);                // peek-lock is the default mode

ServiceBusReceivedMessage first = await receiver.ReceiveMessageAsync();
Console.WriteLine($"received {first.MessageId}, DeliveryCount={first.DeliveryCount}, locked for 5 s");
await Task.Delay(TimeSpan.FromSeconds(8));                                 // the "charge" takes 8 s
ServiceBusReceivedMessage second = await receiver.ReceiveMessageAsync(TimeSpan.FromSeconds(10));
Console.WriteLine($"received {second.MessageId} again, DeliveryCount={second.DeliveryCount}");

try { await receiver.CompleteMessageAsync(first); }
catch (ServiceBusException e) when (e.Reason == ServiceBusFailureReason.MessageLockLost)
{
    Console.WriteLine("completing the first copy threw MessageLockLost; its charge already happened");
}
await receiver.CompleteMessageAsync(second);
Console.WriteLine($"completed the second copy; message still in the queue: {await receiver.PeekMessageAsync() is not null}");
