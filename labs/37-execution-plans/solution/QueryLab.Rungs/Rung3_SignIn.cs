using Microsoft.EntityFrameworkCore;
using QueryLab.Core;

namespace QueryLab.Rungs;

// lower(email) can't use an index on email. Index the expression itself — and make it UNIQUE,
// because "one account per address, whatever the case" is a rule the database should enforce.
// ANALYZE gathers statistics on the new expression.
public sealed class SignIn : SignInRung
{
    public override IReadOnlyList<string> Fixes =>
    [
        "CREATE UNIQUE INDEX lab_customers_email_lower ON customers (lower(email))",
        "ANALYZE customers",
    ];

    protected override async Task<CustomerRow?> FindByEmailAsync(ShopContext db, string email, CancellationToken ct) =>
        await db.Customers.AsNoTracking()
            .Where(c => c.Email.ToLower() == email.ToLower())
            .Select(c => new CustomerRow(c.Id, c.Email, c.Name))
            .SingleOrDefaultAsync(ct);
}
