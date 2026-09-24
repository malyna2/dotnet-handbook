using Microsoft.EntityFrameworkCore;
using QueryLab.Core;

namespace QueryLab.Rungs;

public sealed class PartnerWebhookHandler : PartnerWebhookRung
{
    protected override async Task<OrderRow?> FindOrderAsync(ShopContext db, PartnerWebhook payload, CancellationToken ct) =>
        await db.Orders.AsNoTracking()
            .Where(o => o.Id == payload.OrderNumber)
            .Select(o => new OrderRow(o.Id, o.CustomerId, o.Status, o.CreatedAt, o.Total))
            .SingleOrDefaultAsync(ct);
}
