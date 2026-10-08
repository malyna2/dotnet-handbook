// Prove it: a test on the EF Core in-memory provider passes where a relational engine fails it: a unique index.
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using var sqlite = new SqliteConnection("DataSource=:memory:");    // a real relational engine, still in-process
sqlite.Open();

foreach (var (provider, options) in new[]
{
    ("InMemory", new DbContextOptionsBuilder<Shop>().UseInMemoryDatabase("shop").Options),
    ("SQLite  ", new DbContextOptionsBuilder<Shop>().UseSqlite(sqlite).Options),
})
{
    await using var db = new Shop(options);
    await db.Database.EnsureCreatedAsync();
    db.Users.AddRange(new User { Email = "ada@example.com" }, new User { Email = "ada@example.com" });
    try
    {
        await db.SaveChangesAsync();
        Console.WriteLine($"{provider}: saved {await db.Users.CountAsync()} users with the same email");
    }
    catch (DbUpdateException e) { Console.WriteLine($"{provider}: {e.InnerException?.Message}"); }
}

class Shop(DbContextOptions<Shop> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    protected override void OnModelCreating(ModelBuilder model) => model.Entity<User>().HasIndex(u => u.Email).IsUnique();
}
class User { public int Id { get; set; } public string Email { get; set; } = ""; }
