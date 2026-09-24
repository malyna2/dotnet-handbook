using Microsoft.EntityFrameworkCore;
using QueryLab.Core;

namespace QueryLab.Rungs;

public sealed class ProductSalesFigures : ProductSalesRung
{
    protected override async Task<ProductSales> GetSalesAsync(ShopContext db, long productId, CancellationToken ct)
    {
        var sales = await db.OrderLines.AsNoTracking()
            .Where(l => l.ProductId == productId)
            .GroupBy(l => l.ProductId)
            .Select(g => new ProductSales(
                g.Key, g.Count(), g.Sum(l => (long)l.Quantity), g.Sum(l => l.Quantity * l.UnitPrice)))
            .SingleOrDefaultAsync(ct);
        return sales ?? new ProductSales(productId, 0, 0, 0m);
    }
}
