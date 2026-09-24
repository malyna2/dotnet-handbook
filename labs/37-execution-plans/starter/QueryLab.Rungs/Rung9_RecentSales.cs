using Microsoft.EntityFrameworkCore;
using QueryLab.Core;

namespace QueryLab.Rungs;

public sealed class RecentSales : RecentSalesRung
{
    protected override async Task<IReadOnlyList<RecentSaleRow>> GetRecentSalesAsync(
        ShopContext db, long productId, int take, CancellationToken ct) =>
        await db.OrderLines.AsNoTracking()
            .Where(l => l.ProductId == productId)
            .OrderByDescending(l => l.Id)
            .Take(take)
            .Select(l => new RecentSaleRow(l.Id, l.OrderId, l.Quantity, l.UnitPrice))
            .ToListAsync(ct);
}
