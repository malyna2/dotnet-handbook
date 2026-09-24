// Domain types the chapter samples assume. Only their shapes matter: nothing here runs.
using Microsoft.EntityFrameworkCore;

namespace Snippets;

public sealed record CreateOrder(string CustomerId);
public sealed record OrderPlaced(string OrderId, string CustomerId);
public sealed record Order(string id, string customerId, string status);
public sealed record RefundRequest(string OrderId, decimal Amount);
public sealed record ApprovalRequest(Guid RefundId, RefundRequest Request);
public sealed record ChargeCard(string PaymentId, decimal Amount);
public sealed class StripeOptions { public string ApiKey { get; set; } = ""; }

public sealed class PartnerApiClient(HttpClient http) { public HttpClient Http { get; } = http; }

public interface IOrderRepository
{
    Task<bool> ExistsAsync(string orderId, CancellationToken ct);
    Task InsertAsync(OrderPlaced order, CancellationToken ct);
}

public sealed class SqlOrderRepository : IOrderRepository
{
    public Task<bool> ExistsAsync(string orderId, CancellationToken ct) => Task.FromResult(false);
    public Task InsertAsync(OrderPlaced order, CancellationToken ct) => Task.CompletedTask;
}

public interface IPaymentService { Task ChargeOnceAsync(ChargeCard command, CancellationToken ct); }
public sealed class CardDeclinedException(string message) : Exception(message);

public sealed class ShopDbContext(DbContextOptions<ShopDbContext> options) : DbContext(options);

public partial class Refunds
{
    // The activities the orchestrator sample calls (the Durable analyzer checks that they exist).
    [Function(nameof(NotifyApprover))]
    public static Task NotifyApprover([ActivityTrigger] ApprovalRequest request) => Task.CompletedTask;

    [Function(nameof(IssueRefund))]
    public static Task<string> IssueRefund([ActivityTrigger] Guid refundId) => Task.FromResult("refunded");
}

public sealed record Charge(string PaymentId, decimal Amount);
public sealed record PaymentResult(string Status);
public sealed class PaymentOptions { public string BaseUrl { get; set; } = "https://payments.example.com"; }
public sealed class PaymentClient(HttpClient http) { public HttpClient Http { get; } = http; }

public partial class PaymentGateway
{
    private readonly PaymentOptions _options = new();
}

public partial class DeadLetterTools;

public sealed class StockItem
{
    public int WarehouseId { get; set; }
    public int ProductId { get; set; }
    public int Quantity { get; set; }
}

public sealed class StockDbContext(DbContextOptions<StockDbContext> options) : DbContext(options)
{
    public DbSet<StockItem> Stock => Set<StockItem>();
}

public partial class StockService(StockDbContext db)
{
    private readonly StockDbContext _db = db;
}
