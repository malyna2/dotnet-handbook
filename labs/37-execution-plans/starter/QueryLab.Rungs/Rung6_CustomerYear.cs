using Microsoft.EntityFrameworkCore;
using QueryLab.Core;

namespace QueryLab.Rungs;

public sealed class CustomerYear : CustomerYearRung
{
    protected override async Task<IReadOnlyList<OrderRow>> GetOrdersAsync(
        ShopContext db, long customerId, DateTimeOffset since, CancellationToken ct) =>
        await db.Orders.AsNoTracking()
            .Where(o => o.CustomerId == customerId && o.CreatedAt >= since)
            .OrderByDescending(o => o.CreatedAt).ThenByDescending(o => o.Id)
            .Select(o => new OrderRow(o.Id, o.CustomerId, o.Status, o.CreatedAt, o.Total))
            .ToListAsync(ct);
}
