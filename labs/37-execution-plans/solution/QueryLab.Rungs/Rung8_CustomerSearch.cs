using Microsoft.EntityFrameworkCore;
using QueryLab.Core;

namespace QueryLab.Rungs;

// A B-tree can serve 'prefix%' but not '%fragment%'. A trigram GIN index can: it indexes every
// three-character piece of the text, and the pattern's trigrams narrow the candidates before any
// row is read. Escape the user's input, too: '%' and '_' typed into a search box are wildcards —
// and EF Core's Npgsql provider emits ESCAPE '' (no escape character at all) unless you pass one.
public sealed class CustomerSearch : CustomerSearchRung
{
    public override IReadOnlyList<string> Fixes =>
    [
        "CREATE EXTENSION IF NOT EXISTS pg_trgm",
        "CREATE INDEX lab_customers_email_trgm ON customers USING gin (email gin_trgm_ops)",
    ];

    protected override async Task<IReadOnlyList<CustomerRow>> SearchAsync(ShopContext db, string term, CancellationToken ct)
    {
        var pattern = "%" + EscapeLike(term) + "%";
        return await db.Customers.AsNoTracking()
            .Where(c => EF.Functions.ILike(c.Email, pattern, @"\"))
            .OrderBy(c => c.Id)
            .Select(c => new CustomerRow(c.Id, c.Email, c.Name))
            .ToListAsync(ct);
    }

    // Backslash is the escape character passed to ILike above.
    private static string EscapeLike(string s) =>
        s.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_");
}
