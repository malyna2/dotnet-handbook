// Chapter 9, Exercises → Find the bug: the payment call whose failure nobody can debug.
using Microsoft.Extensions.Logging;

namespace Ch09.Exercises;

public interface IPaymentGateway
{
    Task ChargeAsync(Guid orderId, decimal amount, CancellationToken ct);
}

// The code the chapter prints, unchanged.
public sealed class BuggyPaymentService(IPaymentGateway gateway, ILogger<BuggyPaymentService> logger)
{
    public async Task<bool> ChargeAsync(Guid orderId, decimal amount, CancellationToken ct)
    {
        try
        {
            await gateway.ChargeAsync(orderId, amount, ct);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError($"Payment failed: {ex.Message}");
            return false;
        }
    }
}

// The fix from the chapter's answer.
public sealed class FixedPaymentService(IPaymentGateway gateway, ILogger<FixedPaymentService> logger)
{
    public async Task<bool> ChargeAsync(Guid orderId, decimal amount, CancellationToken ct)
    {
        try
        {
            await gateway.ChargeAsync(orderId, amount, ct);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Payment for order {OrderId} of {Amount} failed", orderId, amount);
            return false;
        }
    }
}
