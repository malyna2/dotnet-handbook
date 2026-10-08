// Chapter 4, Exercises → Find the bug: the customer summary endpoint with a nested N+1.
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Ch04.Exercises;

public sealed record OrderLineDto(string Sku, int Quantity, string ProductName);
public sealed record SummaryDto(string CustomerName, int OrderCount, List<OrderLineDto> Lines);

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<Product> Products => Set<Product>();
}

// Lazy loading without proxies: EF Core injects ILazyLoader through the private constructor, and the
// navigation getters call LazyLoader.Load, the same call the Proxies package's interceptor makes.
// ChangeTracker.LazyLoadingEnabled = false turns it off, which is the chapter's second case.
public sealed class Customer
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

public sealed class Order
{
    private List<OrderLine>? _lines = [];
    public Order() { }
    private Order(ILazyLoader lazyLoader) => LazyLoader = lazyLoader;
    private ILazyLoader? LazyLoader { get; set; }

    public int Id { get; set; }
    public int CustomerId { get; set; }
    public List<OrderLine> Lines { get => LazyLoader.Load(this, ref _lines)!; set => _lines = value; }
}

public sealed class OrderLine
{
    private Product? _product;
    public OrderLine() { }
    private OrderLine(ILazyLoader lazyLoader) => LazyLoader = lazyLoader;
    private ILazyLoader? LazyLoader { get; set; }

    public int Id { get; set; }
    public int OrderId { get; set; }
    public string Sku { get; set; } = "";
    public int Quantity { get; set; }
    public int ProductId { get; set; }
    public Product Product { get => LazyLoader.Load(this, ref _product)!; set => _product = value; }
}

public sealed class Product
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

/// <summary>The sample as printed in the chapter.</summary>
public static class BuggyEndpoint
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/customers/{id:int}/summary", async (int id, AppDbContext db) =>
        {
            var customer = await db.Customers.FindAsync(id);
            if (customer is null) return Results.NotFound();

            var orders = await db.Orders
                .Where(o => o.CustomerId == id)
                .ToListAsync();

            var lines = new List<OrderLineDto>();
            foreach (var order in orders)
            {
                foreach (var line in order.Lines)          // navigation property
                    lines.Add(new OrderLineDto(line.Sku, line.Quantity, line.Product.Name));
            }

            return Results.Ok(new SummaryDto(customer.Name, orders.Count, lines));
        });
    }
}

/// <summary>The fix from the answer: the lines in one projection, the order count in one COUNT.</summary>
public static class FixedEndpoint
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/customers/{id:int}/summary", async (int id, AppDbContext db) =>
        {
            var customer = await db.Customers.FindAsync(id);
            if (customer is null) return Results.NotFound();

            var lines = await db.Orders
                .Where(o => o.CustomerId == id)
                .SelectMany(o => o.Lines)
                .Select(l => new OrderLineDto(l.Sku, l.Quantity, l.Product.Name))
                .ToListAsync();
            var orderCount = await db.Orders.CountAsync(o => o.CustomerId == id);

            return Results.Ok(new SummaryDto(customer.Name, orderCount, lines));
        });
    }
}
