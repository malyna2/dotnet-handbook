using Microsoft.EntityFrameworkCore;
using QueryLab.Core;

namespace QueryLab.Rungs;

public sealed class OrderHistory : OrderHistoryRung
{
    protected override async Task<IReadOnlyList<OrderHistoryRow>> GetOrderHistoryAsync(
        ShopContext db, long customerId, int take, CancellationToken ct)
    {
        var orders = await db.Orders.AsNoTracking()
            .Where(o => o.CustomerId == customerId)
            .OrderByDescending(o => o.CreatedAt).ThenByDescending(o => o.Id)
            .Take(take)
            .ToListAsync(ct);

        var rows = new List<OrderHistoryRow>(orders.Count);
        foreach (var order in orders)
        {
            var lines = await LinesForOrderAsync(db, order.Id, ct);
            rows.Add(new OrderHistoryRow(order.Id, order.CreatedAt, order.Status, order.Total, lines));
        }
        return rows;
    }

    // The same helper the order detail page uses.
    private static async Task<IReadOnlyList<OrderLineRow>> LinesForOrderAsync(
        ShopContext db, long orderId, CancellationToken ct) =>
        await db.OrderLines.AsNoTracking()
            .Where(l => l.OrderId == orderId)
            .OrderBy(l => l.Id)
            .Select(l => new OrderLineRow(l.ProductId, l.Quantity, l.UnitPrice))
            .ToListAsync(ct);
}
