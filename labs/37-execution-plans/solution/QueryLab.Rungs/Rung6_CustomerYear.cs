using Microsoft.EntityFrameworkCore;
using QueryLab.Core;

namespace QueryLab.Rungs;

// (created_at, customer_id) is sorted by date first: the scan walks every order of the last year
// and checks customer_id on each. Equality column first, range column second, and the index
// goes straight to this customer's few rows — already in date order.
public sealed class CustomerYear : CustomerYearRung
{
    public override IReadOnlyList<string> Fixes =>
    [
        "CREATE INDEX lab_orders_customer_id_created_at ON orders (customer_id, created_at)",
    ];

    protected override async Task<IReadOnlyList<OrderRow>> GetOrdersAsync(
        ShopContext db, long customerId, DateTimeOffset since, CancellationToken ct) =>
        await db.Orders.AsNoTracking()
            .Where(o => o.CustomerId == customerId && o.CreatedAt >= since)
            .OrderByDescending(o => o.CreatedAt).ThenByDescending(o => o.Id)
            .Select(o => new OrderRow(o.Id, o.CustomerId, o.Status, o.CreatedAt, o.Total))
            .ToListAsync(ct);
}
