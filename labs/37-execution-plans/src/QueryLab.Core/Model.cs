using System.Text;
using Microsoft.EntityFrameworkCore;

namespace QueryLab.Core;

public sealed class Customer
{
    public long Id { get; set; }
    public string Email { get; set; } = "";
    public string Name { get; set; } = "";
    public string Country { get; set; } = "";
    public string City { get; set; } = "";
    public string Tier { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public List<Order> Orders { get; set; } = [];
}

public sealed class Product
{
    public long Id { get; set; }
    public string Sku { get; set; } = "";
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    public decimal Price { get; set; }
}

public sealed class Order
{
    public long Id { get; set; }
    public long CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;
    public string Status { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public decimal Total { get; set; }
    public string Currency { get; set; } = "";
    public string? ShippingNote { get; set; }
    public List<OrderLine> Lines { get; set; } = [];
    public List<Shipment> Shipments { get; set; } = [];
}

public sealed class OrderLine
{
    public long Id { get; set; }
    public long OrderId { get; set; }
    public Order Order { get; set; } = null!;
    public long ProductId { get; set; }
    public Product Product { get; set; } = null!;
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
}

public sealed class Shipment
{
    public long Id { get; set; }
    public long OrderId { get; set; }
    public string Carrier { get; set; } = "";
    public DateTimeOffset ShippedAt { get; set; }
    public string TrackingCode { get; set; } = "";
}

/// <summary>The shop database, mapped onto the schema that postgres/seed.sql creates.</summary>
public sealed class ShopContext(DbContextOptions<ShopContext> options) : DbContext(options)
{
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderLine> OrderLines => Set<OrderLine>();
    public DbSet<Shipment> Shipments => Set<Shipment>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Customer>().ToTable("customers");
        b.Entity<Product>().ToTable("products");
        b.Entity<Order>().ToTable("orders");
        b.Entity<OrderLine>().ToTable("order_lines");
        b.Entity<Shipment>().ToTable("shipments");

        b.Entity<Order>().Property(o => o.Currency).HasColumnType("char(3)");
        b.Entity<Order>().Property(o => o.Total).HasPrecision(12, 2);
        b.Entity<OrderLine>().Property(l => l.UnitPrice).HasPrecision(10, 2);
        b.Entity<Product>().Property(p => p.Price).HasPrecision(10, 2);

        // The seed script owns the schema; map every column to its snake_case name.
        foreach (var entity in b.Model.GetEntityTypes())
            foreach (var property in entity.GetProperties())
                property.SetColumnName(SnakeCase(property.Name));
    }

    private static string SnakeCase(string name)
    {
        var sb = new StringBuilder(name.Length + 4);
        for (var i = 0; i < name.Length; i++)
        {
            if (char.IsUpper(name[i]) && i > 0) sb.Append('_');
            sb.Append(char.ToLowerInvariant(name[i]));
        }
        return sb.ToString();
    }
}
