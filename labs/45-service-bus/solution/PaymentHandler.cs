using Azure.Messaging.ServiceBus;

namespace ServiceBusLab;

public sealed class CardDeclinedException(string message) : Exception(message);

// SOLUTION — classify the failure. Permanent: dead-letter now, with a reason someone can act on.
// Transient: let it escape, so the processor abandons the message and Service Bus retries it.
public static class PaymentHandler
{
    public static async Task HandleAsync(ProcessMessageEventArgs args, Func<string, Task> charge)
    {
        string paymentId = args.Message.Body.ToString();
        try
        {
            await charge(paymentId);
        }
        catch (CardDeclinedException e)
        {
            await args.DeadLetterMessageAsync(args.Message, "CardDeclined", e.Message, args.CancellationToken);
        }
    }
}
