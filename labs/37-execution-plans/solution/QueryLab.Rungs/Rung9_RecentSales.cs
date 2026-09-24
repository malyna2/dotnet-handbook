using Microsoft.EntityFrameworkCore;
using QueryLab.Core;

namespace QueryLab.Rungs;

// After warm-up the prepared statement runs a generic plan, costed for an average product
// (a few hundred lines) — then the bestseller arrives with 1.4 million. The durable fix removes
// the choice: with (product_id, id) one plan is right for every product, reading only the 20
// newest lines of any of them. (Alternatives, with their costs, are in the chapter.)
public sealed class RecentSales : RecentSalesRung
{
    public override IReadOnlyList<string> Fixes =>
    [
        "CREATE INDEX lab_order_lines_product_id_id ON order_lines (product_id, id)",
    ];

    protected override async Task<IReadOnlyList<RecentSaleRow>> GetRecentSalesAsync(
        ShopContext db, long productId, int take, CancellationToken ct) =>
        await db.OrderLines.AsNoTracking()
            .Where(l => l.ProductId == productId)
            .OrderByDescending(l => l.Id)
            .Take(take)
            .Select(l => new RecentSaleRow(l.Id, l.OrderId, l.Quantity, l.UnitPrice))
            .ToListAsync(ct);
}
