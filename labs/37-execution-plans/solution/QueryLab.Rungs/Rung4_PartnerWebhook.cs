using Microsoft.EntityFrameworkCore;
using QueryLab.Core;

namespace QueryLab.Rungs;

// `o.Id == payload.OrderNumber` compiles because C# widens long to decimal, and EF Core translates
// that widening faithfully: o.id::numeric = @p. A cast on the column defeats the primary key.
// Convert the value (and validate it) on the .NET side, so the SQL compares bigint with bigint.
public sealed class PartnerWebhookHandler : PartnerWebhookRung
{
    protected override async Task<OrderRow?> FindOrderAsync(ShopContext db, PartnerWebhook payload, CancellationToken ct)
    {
        if (payload.OrderNumber != decimal.Truncate(payload.OrderNumber)
            || payload.OrderNumber < 1 || payload.OrderNumber > long.MaxValue)
            return null;
        var orderId = (long)payload.OrderNumber;

        return await db.Orders.AsNoTracking()
            .Where(o => o.Id == orderId)
            .Select(o => new OrderRow(o.Id, o.CustomerId, o.Status, o.CreatedAt, o.Total))
            .SingleOrDefaultAsync(ct);
    }
}
