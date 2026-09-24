using Microsoft.EntityFrameworkCore;

namespace QueryLab.Core;

// The fixed half of every rung: inputs, ordering rules and result shape. The tests hold your
// implementation to these, so a "fix" that returns different rows does not count.

public sealed record OrderLineRow(long ProductId, int Quantity, decimal UnitPrice);
public sealed record ShipmentRow(string Carrier, DateTimeOffset ShippedAt, string TrackingCode);
public sealed record OrderHistoryRow(long OrderId, DateTimeOffset CreatedAt, string Status, decimal Total, IReadOnlyList<OrderLineRow> Lines);
public sealed record OrderExportRow(long OrderId, DateTimeOffset CreatedAt, decimal Total, IReadOnlyList<OrderLineRow> Lines, IReadOnlyList<ShipmentRow> Shipments);
public sealed record CustomerRow(long Id, string Email, string Name);
public sealed record OrderRow(long Id, long CustomerId, string Status, DateTimeOffset CreatedAt, decimal Total);
public sealed record ProductSales(long ProductId, int Lines, long Units, decimal Revenue);
public sealed record OrderCursor(DateTimeOffset CreatedAt, long Id);
public sealed record PageRequest(int PageNumber, int PageSize, OrderCursor After);
public sealed record PartnerWebhook(string Event, decimal OrderNumber);
public sealed record RecentSaleRow(long LineId, long OrderId, int Quantity, decimal UnitPrice);

/// <summary>Rung 1.</summary>
public abstract class OrderHistoryRung : Rung
{
    public const long CustomerId = 2;
    public const int PageSize = 50;

    public sealed override int Number => 1;
    public sealed override string Title => "The order history page";
    public sealed override string Scenario =>
        $"Key account {CustomerId}'s latest {PageSize} orders, newest first (created_at, then id), each with its lines in line-id order.";

    public sealed override async Task<object> RunAsync(LabDb lab, CancellationToken ct)
    {
        await using var db = lab.CreateContext();
        return await GetOrderHistoryAsync(db, CustomerId, PageSize, ct);
    }

    protected abstract Task<IReadOnlyList<OrderHistoryRow>> GetOrderHistoryAsync(
        ShopContext db, long customerId, int take, CancellationToken ct);
}

/// <summary>Rung 2.</summary>
public abstract class DeliveryReportRung : Rung
{
    public const long CustomerId = 2;
    public static readonly DateTimeOffset From = LabDb.Now.AddDays(-90);

    public sealed override int Number => 2;
    public sealed override string Title => "The quarterly delivery report";
    public sealed override string Scenario =>
        $"Key account {CustomerId}'s shipped orders from the last 90 days in id order, each with its lines and its shipments (both in id order) — the file an account manager downloads.";

    public sealed override async Task<object> RunAsync(LabDb lab, CancellationToken ct)
    {
        await using var db = lab.CreateContext();
        return await ExportAsync(db, CustomerId, From, LabDb.Now, ct);
    }

    protected abstract Task<IReadOnlyList<OrderExportRow>> ExportAsync(
        ShopContext db, long customerId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
}

/// <summary>Rung 3.</summary>
public abstract class SignInRung : Rung
{
    public const long SampleCustomerId = 54321;

    /// <summary>The address as the user typed it: all lower case. Resolved from the data in SetUpAsync.</summary>
    public string TypedEmail { get; private set; } = "";

    public sealed override int Number => 3;
    public sealed override string Title => "Signing in by email";
    public sealed override string Scenario =>
        "Find the customer for the email address typed at sign-in. People type their address in any case; it is stored as they first entered it.";

    public sealed override async Task SetUpAsync(LabDb lab, CancellationToken ct) =>
        TypedEmail = await lab.ScalarAsync<string>($"SELECT lower(email) FROM customers WHERE id = {SampleCustomerId}", ct);

    public sealed override async Task<object> RunAsync(LabDb lab, CancellationToken ct)
    {
        await using var db = lab.CreateContext();
        return (await FindByEmailAsync(db, TypedEmail, ct))!;
    }

    protected abstract Task<CustomerRow?> FindByEmailAsync(ShopContext db, string email, CancellationToken ct);
}

/// <summary>Rung 4.</summary>
public abstract class PartnerWebhookRung : Rung
{
    /// <summary>The partner's API documents the order number as a JSON number; the DTO maps it to decimal.</summary>
    public static readonly PartnerWebhook Payload = new("order.delivered", 424242m);

    public sealed override int Number => 4;
    public sealed override string Title => "The partner webhook";
    public sealed override string Scenario =>
        "A delivery partner calls back with an order number, and the handler loads that order.";

    public sealed override async Task<object> RunAsync(LabDb lab, CancellationToken ct)
    {
        await using var db = lab.CreateContext();
        return (await FindOrderAsync(db, Payload, ct))!;
    }

    protected abstract Task<OrderRow?> FindOrderAsync(ShopContext db, PartnerWebhook payload, CancellationToken ct);
}

/// <summary>Rung 5.</summary>
public abstract class ProductSalesRung : Rung
{
    public const long ProductId = 50;

    public sealed override int Number => 5;
    public sealed override string Title => "Product sales figures";
    public sealed override string Scenario =>
        $"Order lines, units and revenue for product {ProductId} across all orders — the figures on the product's admin page.";

    public sealed override async Task<object> RunAsync(LabDb lab, CancellationToken ct)
    {
        await using var db = lab.CreateContext();
        return await GetSalesAsync(db, ProductId, ct);
    }

    protected abstract Task<ProductSales> GetSalesAsync(ShopContext db, long productId, CancellationToken ct);
}

/// <summary>Rung 6.</summary>
public abstract class CustomerYearRung : Rung
{
    public const long CustomerId = 33;
    public static readonly DateTimeOffset Since = LabDb.Now.AddDays(-365);

    public sealed override int Number => 6;
    public sealed override string Title => "A customer's orders this year";
    public sealed override string Scenario =>
        $"The support screen: customer {CustomerId}'s orders from the last 365 days, newest first (created_at, then id).";

    public sealed override async Task<object> RunAsync(LabDb lab, CancellationToken ct)
    {
        await using var db = lab.CreateContext();
        return await GetOrdersAsync(db, CustomerId, Since, ct);
    }

    protected abstract Task<IReadOnlyList<OrderRow>> GetOrdersAsync(
        ShopContext db, long customerId, DateTimeOffset since, CancellationToken ct);
}

/// <summary>Rung 7.</summary>
public abstract class AdminOrdersPageRung : Rung
{
    public const int PageNumber = 4001;
    public const int PageSize = 50;

    /// <summary>The last row of page 4,000, resolved from the data in SetUpAsync.</summary>
    public OrderCursor After { get; private set; } = null!;

    public sealed override int Number => 7;
    public sealed override string Title => "The admin order list, page 4,001";
    public sealed override string Scenario =>
        $"All orders, newest first (created_at, then id), {PageSize} per page. The request carries the page number and the last row of the previous page.";

    public sealed override async Task SetUpAsync(LabDb lab, CancellationToken ct)
    {
        await using var cmd = lab.DataSource.CreateCommand(
            $"SELECT created_at, id FROM orders ORDER BY created_at DESC, id DESC OFFSET {(PageNumber - 1) * PageSize - 1} LIMIT 1");
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        After = new OrderCursor(reader.GetFieldValue<DateTimeOffset>(0), reader.GetInt64(1));
    }

    public sealed override async Task<object> RunAsync(LabDb lab, CancellationToken ct)
    {
        await using var db = lab.CreateContext();
        return await GetPageAsync(db, new PageRequest(PageNumber, PageSize, After), ct);
    }

    protected abstract Task<IReadOnlyList<OrderRow>> GetPageAsync(ShopContext db, PageRequest page, CancellationToken ct);
}

/// <summary>Rung 8.</summary>
public abstract class CustomerSearchRung : Rung
{
    public const string Term = "tomasz.kowalski";

    public sealed override int Number => 8;
    public sealed override string Title => "Customer search";
    public sealed override string Scenario =>
        $"The support team's search box: customers whose email contains \"{Term}\", ignoring case, in id order.";

    public sealed override async Task<object> RunAsync(LabDb lab, CancellationToken ct)
    {
        await using var db = lab.CreateContext();
        return await SearchAsync(db, Term, ct);
    }

    protected abstract Task<IReadOnlyList<CustomerRow>> SearchAsync(ShopContext db, string term, CancellationToken ct);
}

/// <summary>Rung 9.</summary>
public abstract class RecentSalesRung : Rung
{
    public const long Bestseller = 1;
    public const int Take = 20;

    /// <summary>Popular products (each in about 0.1% of order lines) that warm the statement up.</summary>
    public static readonly long[] WarmUpProducts = [50, 51, 52, 53, 54, 55, 56, 57, 58, 59, 60, 61];

    public sealed override int Number => 9;
    public sealed override string Title => "Recent sales of a product";
    public sealed override string Scenario =>
        $"The product page's \"latest {Take} sales\" panel, on the service's real configuration: Npgsql automatic " +
        $"preparation is on (Max Auto Prepare=20). Traffic warms it up with {WarmUpProducts.Length} popular products; " +
        $"then someone opens the bestseller, product {Bestseller}.";

    public sealed override async Task WarmUpAsync(LabDb lab, CancellationToken ct)
    {
        foreach (var product in WarmUpProducts)
        {
            await using var db = lab.CreateContext(lab.AutoPrepareDataSource);
            await GetRecentSalesAsync(db, product, Take, ct);
        }
    }

    public sealed override async Task<object> RunAsync(LabDb lab, CancellationToken ct)
    {
        await using var db = lab.CreateContext(lab.AutoPrepareDataSource);
        return await GetRecentSalesAsync(db, Bestseller, Take, ct);
    }

    /// <summary>
    /// The server executed a cached plan for a prepared statement; EXPLAIN on a fresh statement would
    /// not show it. Replay the same SQL as a prepared statement on the same connection instead.
    /// </summary>
    public sealed override async Task<IReadOnlyList<ExplainedStatement>?> ExplainOverrideAsync(
        LabDb lab, IReadOnlyList<CapturedCommand> lastRun, CancellationToken ct)
    {
        if (lastRun.Count == 0) return null;
        var command = lastRun[0];
        object[] ValuesFor(long product) =>
            command.Parameters.Select(p => p.Value is long v && v == Bestseller ? product : p.Value!).ToArray();
        var replay = await Explain.PreparedAsync(
            lab.AutoPrepareDataSource, command,
            WarmUpProducts.Take(6).Select(ValuesFor), ValuesFor(Bestseller), ct);
        return [replay];
    }

    protected abstract Task<IReadOnlyList<RecentSaleRow>> GetRecentSalesAsync(
        ShopContext db, long productId, int take, CancellationToken ct);
}

/// <summary>Helpers the rungs may use.</summary>
public static class QueryExtensions
{
    public static async Task<IReadOnlyList<T>> ToReadOnlyListAsync<T>(this IQueryable<T> query, CancellationToken ct) =>
        await query.ToListAsync(ct);
}
