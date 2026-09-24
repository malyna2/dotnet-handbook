using Microsoft.EntityFrameworkCore;
using QueryLab.Core;

namespace QueryLab.Rungs;

// The index finds the product's lines, but quantity and unit_price live in the heap, so every
// line costs a visit to a random table page. INCLUDE puts them in the index: an index-only scan.
// (In a real system you would then drop ix_order_lines_product_id — this index replaces it.)
public sealed class ProductSalesFigures : ProductSalesRung
{
    public override IReadOnlyList<string> Fixes =>
    [
        "CREATE INDEX lab_order_lines_product_id_covering ON order_lines (product_id) INCLUDE (quantity, unit_price)",
    ];

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
