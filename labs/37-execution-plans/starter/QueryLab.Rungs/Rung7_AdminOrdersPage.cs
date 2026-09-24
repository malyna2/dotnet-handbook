using Microsoft.EntityFrameworkCore;
using QueryLab.Core;

namespace QueryLab.Rungs;

public sealed class AdminOrdersPage : AdminOrdersPageRung
{
    protected override async Task<IReadOnlyList<OrderRow>> GetPageAsync(ShopContext db, PageRequest page, CancellationToken ct) =>
        await db.Orders.AsNoTracking()
            .OrderByDescending(o => o.CreatedAt).ThenByDescending(o => o.Id)
            .Skip((page.PageNumber - 1) * page.PageSize)
            .Take(page.PageSize)
            .Select(o => new OrderRow(o.Id, o.CustomerId, o.Status, o.CreatedAt, o.Total))
            .ToListAsync(ct);
}
