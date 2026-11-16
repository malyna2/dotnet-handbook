using Azure.Messaging.ServiceBus;

namespace ServiceBusLab;

public sealed class CardDeclinedException(string message) : Exception(message);

// STARTER — this is the file you fix. Every exception escapes the handler, so the processor abandons the
// message and Service Bus redelivers it. That is right for a timeout and wrong for a declined card:
// the message is charged-and-declined over and over until MaxDeliveryCount dead-letters it with no reason a human can use.
public static class PaymentHandler
{
    public static async Task HandleAsync(ProcessMessageEventArgs args, Func<string, Task> charge)
    {
        string paymentId = args.Message.Body.ToString();
        await charge(paymentId);   // throws CardDeclinedException for a card that will never succeed
    }
}
