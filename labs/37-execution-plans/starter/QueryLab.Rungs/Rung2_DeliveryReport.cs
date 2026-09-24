using Microsoft.EntityFrameworkCore;
using QueryLab.Core;

namespace QueryLab.Rungs;

public sealed class DeliveryReport : DeliveryReportRung
{
    protected override async Task<IReadOnlyList<OrderExportRow>> ExportAsync(
        ShopContext db, long customerId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var orders = await db.Orders.AsNoTracking()
            .Include(o => o.Lines)
            .Include(o => o.Shipments)
            .Where(o => o.CustomerId == customerId && o.Status == "shipped"
                        && o.CreatedAt >= from && o.CreatedAt < to)
            .OrderBy(o => o.Id)
            .ToListAsync(ct);

        return orders.Select(o => new OrderExportRow(
                o.Id, o.CreatedAt, o.Total,
                o.Lines.OrderBy(l => l.Id).Select(l => new OrderLineRow(l.ProductId, l.Quantity, l.UnitPrice)).ToList(),
                o.Shipments.OrderBy(s => s.Id).Select(s => new ShipmentRow(s.Carrier, s.ShippedAt, s.TrackingCode)).ToList()))
            .ToList();
    }
}
