// The SQL EF Core 10 sends for the LINQ forms in Chapter 7's "SQL Queries in EF Core LINQ" table,
// and the rows it returns, on SQL Server and PostgreSQL (the schema and seed in ../schema.sql).
using Microsoft.EntityFrameworkCore;

foreach (var pg in new[] { false, true })
{
    using var ctx = new Shop(pg);
    Console.WriteLine($"===== {(pg ? "PostgreSQL" : "SQL Server")}");

    void Show<T>(string title, IQueryable<T> q)
    {
        Console.WriteLine($"--- {title}\n{q.ToQueryString()}");
        foreach (var r in q.ToList()) Console.WriteLine($"  {r}");
    }

    Show("!= on a nullable column", ctx.Customers.Where(c => c.City != "Kyiv").Select(c => c.Name));
    Show("!Any()", ctx.Customers.Where(c => !c.Orders.Any()).Select(c => c.Name));
    Show("!Contains over a subquery", ctx.Customers
        .Where(c => !ctx.Orders.Select(o => o.CustomerId).Contains(c.Id)).Select(c => c.Name));
    Show("Count and Sum per parent", ctx.Customers
        .Select(c => new { c.Name, Orders = c.Orders.Count(), Revenue = c.Orders.Sum(o => o.Total) }));
    Show("GroupBy, then Where", ctx.Orders.Where(o => o.Status == "Paid").GroupBy(o => o.CustomerId)
        .Where(g => g.Count() > 1).Select(g => new { g.Key, Count = g.Count(), Paid = g.Sum(o => o.Total) }));
    Show("LeftJoin", ctx.Customers.LeftJoin(ctx.Orders, c => c.Id, o => o.CustomerId,
        (c, o) => new { c.Name, OrderId = (int?)o!.Id }));
    Show("filtered navigation as the outer side", ctx.Customers.SelectMany(
        c => c.Orders.Where(o => o.Status == "Paid").DefaultIfEmpty(),
        (c, o) => new { c.Name, OrderId = (int?)o!.Id }));

    var top = ctx.Customers.Select(c => new
    {
        c.Name,
        Top = c.Orders.OrderByDescending(o => o.Total).ThenBy(o => o.Id).Take(2).ToList()
    });
    Console.WriteLine($"--- top 2 per customer\n{top.ToQueryString()}");
    foreach (var r in top.ToList()) Console.WriteLine($"  {r.Name}: {string.Join(", ", r.Top.Select(o => o.Id))}");
}

class Customer
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? City { get; set; }
    public List<Order> Orders { get; set; } = [];
}

class Order
{
    public int Id { get; set; }
    public int? CustomerId { get; set; }
    public string Status { get; set; } = "";
    public decimal Total { get; set; }
    public DateOnly CreatedAt { get; set; }
}

class Shop(bool pg) : DbContext
{
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Order> Orders => Set<Order>();

    protected override void OnConfiguring(DbContextOptionsBuilder b)
    {
        if (pg) b.UseNpgsql("Host=localhost;Port=54317;Username=postgres;Password=local-only;Database=postgres");
        else b.UseSqlServer("Server=localhost,14317;Database=handbook;User Id=sa;Password=Local-Only-Passw0rd!;TrustServerCertificate=true");
    }

    protected override void OnModelCreating(ModelBuilder m)
    {
        m.Entity<Customer>().Property(c => c.Name).HasMaxLength(50).IsUnicode(false);
        m.Entity<Customer>().Property(c => c.City).HasMaxLength(50).IsUnicode(false);
        m.Entity<Order>().Property(o => o.Status).HasMaxLength(20).IsUnicode(false);
        m.Entity<Order>().Property(o => o.Total).HasPrecision(10, 2);
        if (!pg) return;
        // schema.sql leaves identifiers unquoted, and PostgreSQL folds those to lower case.
        foreach (var e in m.Model.GetEntityTypes())
        {
            e.SetTableName(e.GetTableName()!.ToLowerInvariant());
            foreach (var p in e.GetProperties()) p.SetColumnName(p.Name.ToLowerInvariant());
        }
    }
}
