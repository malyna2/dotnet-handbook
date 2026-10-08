using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

// Prove it: loading related rows one parent at a time sends 1 + N SQL statements; Include or a projection sends 1.
int statements = 0;
var options = new DbContextOptionsBuilder<Shop>().UseSqlite("Data Source=shop;Mode=Memory;Cache=Shared")
    .LogTo(_ => statements++, [RelationalEventId.CommandExecuted]).Options;       // counts every SQL command sent
using var seed = new Shop(options);
seed.Database.OpenConnection();                    // an in-memory database lives while a connection to it is open
seed.Database.EnsureCreated();
seed.Orders.AddRange(Enumerable.Range(1, 50).Select(i => new Order { Lines = [new() { Sku = $"A-{i}" }, new() { Sku = $"B-{i}" }] }));
seed.SaveChanges();
Count("Load() each order's lines in a loop", db =>
{
    var orders = db.Orders.ToList();
    foreach (var order in orders) db.Entry(order).Collection(o => o.Lines).Load();   // what lazy loading runs per order
    return orders.Sum(o => o.Lines.Count);
});
Count("Include(o => o.Lines)", db => db.Orders.Include(o => o.Lines).ToList().Sum(o => o.Lines.Count));
Count("Select(o => new { o.Id, Skus = ... })", db => db.Orders.Select(o => new { o.Id, Skus = o.Lines.Select(l => l.Sku).ToList() }).ToList().Sum(o => o.Skus.Count));
Count("neither, and no lazy loading", db => db.Orders.ToList().Sum(o => o.Lines.Count));
void Count(string label, Func<Shop, int> load)
{
    using var db = new Shop(options);              // a fresh context, as each request gets from DI
    statements = 0;
    Console.WriteLine($"{label,-38} {load(db),3} order lines, {statements,2} SQL statement(s)");
}
class Shop(DbContextOptions<Shop> options) : DbContext(options) { public DbSet<Order> Orders => Set<Order>(); }
class Order { public int Id { get; set; } public List<OrderLine> Lines { get; set; } = []; }
class OrderLine { public int Id { get; set; } public int OrderId { get; set; } public string Sku { get; set; } = ""; }
