using Microsoft.EntityFrameworkCore;
using QueryLab.Core;

namespace QueryLab.Rungs;

public sealed class SignIn : SignInRung
{
    protected override async Task<CustomerRow?> FindByEmailAsync(ShopContext db, string email, CancellationToken ct) =>
        await db.Customers.AsNoTracking()
            .Where(c => c.Email.ToLower() == email.ToLower())
            .Select(c => new CustomerRow(c.Id, c.Email, c.Name))
            .SingleOrDefaultAsync(ct);
}
