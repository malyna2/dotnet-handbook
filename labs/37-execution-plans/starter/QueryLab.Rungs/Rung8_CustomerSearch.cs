using Microsoft.EntityFrameworkCore;
using QueryLab.Core;

namespace QueryLab.Rungs;

public sealed class CustomerSearch : CustomerSearchRung
{
    protected override async Task<IReadOnlyList<CustomerRow>> SearchAsync(ShopContext db, string term, CancellationToken ct) =>
        await db.Customers.AsNoTracking()
            .Where(c => EF.Functions.ILike(c.Email, "%" + term + "%"))
            .OrderBy(c => c.Id)
            .Select(c => new CustomerRow(c.Id, c.Email, c.Name))
            .ToListAsync(ct);
}
