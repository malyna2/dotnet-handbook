using Microsoft.EntityFrameworkCore;
using QueryLab.Core;

namespace QueryLab.Rungs;

// OFFSET 200,000 makes the server produce and discard 200,000 rows. Keyset pagination starts
// after the last row the client saw. Write it as a row-value comparison, (created_at, id) < (x, y):
// PostgreSQL turns that into an index boundary (created_at <= x). The equivalent OR form
// (created_at < x OR (created_at = x AND id < y)) gives the same rows but only a filter —
// the scan still walks and discards the 200,000 rows before it.
public sealed class AdminOrdersPage : AdminOrdersPageRung
{
    protected override async Task<IReadOnlyList<OrderRow>> GetPageAsync(ShopContext db, PageRequest page, CancellationToken ct)
    {
        var after = page.After;
        return await db.Orders.AsNoTracking()
            .Where(o => EF.Functions.LessThan(
                ValueTuple.Create(o.CreatedAt, o.Id),
                ValueTuple.Create(after.CreatedAt, after.Id)))
            .OrderByDescending(o => o.CreatedAt).ThenByDescending(o => o.Id)
            .Take(page.PageSize)
            .Select(o => new OrderRow(o.Id, o.CustomerId, o.Status, o.CreatedAt, o.Total))
            .ToListAsync(ct);
    }
}
