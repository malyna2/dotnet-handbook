using Microsoft.EntityFrameworkCore;
using QueryLab.Core;

namespace QueryLab.Rungs;

// One statement: project the orders and their lines together. EF Core turns the nested
// collection into a LEFT JOIN against the 50 orders it selects first.
public sealed class OrderHistory : OrderHistoryRung
{
    protected override async Task<IReadOnlyList<OrderHistoryRow>> GetOrderHistoryAsync(
        ShopContext db, long customerId, int take, CancellationToken ct) =>
        await db.Orders.AsNoTracking()
            .Where(o => o.CustomerId == customerId)
            .OrderByDescending(o => o.CreatedAt).ThenByDescending(o => o.Id)
            .Take(take)
            .Select(o => new OrderHistoryRow(
                o.Id, o.CreatedAt, o.Status, o.Total,
                o.Lines.OrderBy(l => l.Id)
                       .Select(l => new OrderLineRow(l.ProductId, l.Quantity, l.UnitPrice))
                       .ToList()))
            .ToListAsync(ct);
}
