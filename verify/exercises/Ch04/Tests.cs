using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Ch04.Exercises;

// Each defect is shown on the buggy endpoint and shown absent on the fix, so the suite stays green and
// a regression in either direction turns it red. The endpoints run over real HTTP, with a scoped
// AppDbContext per request as AddDbContext gives you, against SQLite in memory; every SQL command
// EF Core sends during the request is counted.
public sealed class NPlusOneTests
{
    // The chapter's customer: 200 orders of 5 lines each. Every line names its own product, as an
    // order history over a large catalogue mostly does; a shared product is loaded once per context.
    private const int Orders = 200, LinesPerOrder = 5;

    [Fact]
    public async Task Buggy_endpoint_with_lazy_loading_sends_one_query_per_order_and_one_per_line()
    {
        await using var shop = await Shop.StartAsync(BuggyEndpoint.Map, lazyLoading: true);
        var (summary, statements) = await shop.GetSummaryAsync(1);

        Assert.Equal(1 + 1 + Orders + Orders * LinesPerOrder, statements);      // 1,202
        Assert.Equal(Orders, summary.OrderCount);
        Assert.Equal(Orders * LinesPerOrder, summary.Lines.Count);

        var (_, smallStatements) = await shop.GetSummaryAsync(2);              // the test database: 20 orders
        Assert.Equal(1 + 1 + 20 + 20 * LinesPerOrder, smallStatements);        // 122: fast enough to pass
    }

    [Fact]
    public async Task Buggy_endpoint_without_lazy_loading_returns_no_lines_instead_of_being_slow()
    {
        await using var shop = await Shop.StartAsync(BuggyEndpoint.Map, lazyLoading: false);
        var (summary, statements) = await shop.GetSummaryAsync(1);

        Assert.Equal(2, statements);
        Assert.Equal(Orders, summary.OrderCount);
        Assert.Empty(summary.Lines);                                           // wrong data, no exception
    }

    [Fact]
    public async Task Fixed_endpoint_returns_the_same_summary_in_three_statements_whatever_the_size()
    {
        await using var lazy = await Shop.StartAsync(BuggyEndpoint.Map, lazyLoading: true);
        await using var shop = await Shop.StartAsync(FixedEndpoint.Map, lazyLoading: true);
        var (expected, _) = await lazy.GetSummaryAsync(1);
        var (summary, statements) = await shop.GetSummaryAsync(1);
        var (_, smallStatements) = await shop.GetSummaryAsync(2);

        Assert.Equal(3, statements);
        Assert.Equal(3, smallStatements);
        Assert.Equal(expected.CustomerName, summary.CustomerName);
        Assert.Equal(expected.OrderCount, summary.OrderCount);
        Assert.Equal(expected.Lines.OrderBy(l => l.Sku), summary.Lines.OrderBy(l => l.Sku));
    }

    [Fact]
    public async Task Both_endpoints_return_404_for_an_unknown_customer()
    {
        await using var buggy = await Shop.StartAsync(BuggyEndpoint.Map, lazyLoading: true);
        await using var fixedShop = await Shop.StartAsync(FixedEndpoint.Map, lazyLoading: true);

        Assert.Equal(HttpStatusCode.NotFound, (await buggy.Http.GetAsync("/api/customers/999/summary", TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await fixedShop.Http.GetAsync("/api/customers/999/summary", TestContext.Current.CancellationToken)).StatusCode);
    }

    private sealed class Shop : IAsyncDisposable
    {
        private readonly SqliteConnection _keepAlive;
        private readonly WebApplication _app;
        private readonly Counter _counter;

        private Shop(SqliteConnection keepAlive, WebApplication app, Counter counter, HttpClient http) =>
            (_keepAlive, _app, _counter, Http) = (keepAlive, app, counter, http);

        public HttpClient Http { get; }

        public static async Task<Shop> StartAsync(Action<IEndpointRouteBuilder> map, bool lazyLoading)
        {
            string database = $"Data Source=ch04-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
            var keepAlive = new SqliteConnection(database);      // the in-memory database lives while this is open
            keepAlive.Open();
            var counter = new Counter();
            var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(database)
                .LogTo(_ => counter.Increment(), [RelationalEventId.CommandExecuted]).Options;
            Seed(options);

            var builder = WebApplication.CreateSlimBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Services.AddScoped(_ =>
            {
                var db = new AppDbContext(options);
                db.ChangeTracker.LazyLoadingEnabled = lazyLoading;
                return db;
            });
            var app = builder.Build();
            map(app);
            await app.StartAsync(TestContext.Current.CancellationToken);
            var http = new HttpClient { BaseAddress = new Uri(app.Urls.First()) };
            return new Shop(keepAlive, app, counter, http);
        }

        public async Task<(SummaryDto Summary, int Statements)> GetSummaryAsync(int customerId)
        {
            _counter.Reset();
            var summary = await Http.GetFromJsonAsync<SummaryDto>($"/api/customers/{customerId}/summary", TestContext.Current.CancellationToken);
            return (summary!, _counter.Value);
        }

        private static void Seed(DbContextOptions<AppDbContext> options)
        {
            using var db = new AppDbContext(options);
            db.Database.EnsureCreated();
            int product = 0;
            foreach (var (customerId, orders) in new[] { (1, Orders), (2, 20) })
            {
                db.Customers.Add(new Customer { Id = customerId, Name = $"Customer {customerId}" });
                for (int o = 0; o < orders; o++)
                {
                    var order = new Order { CustomerId = customerId };
                    for (int l = 0; l < LinesPerOrder; l++)
                    {
                        var p = new Product { Name = $"Product {++product}" };
                        order.Lines.Add(new OrderLine { Sku = $"SKU-{product}", Quantity = l + 1, Product = p });
                    }
                    db.Orders.Add(order);
                }
            }
            db.SaveChanges();
        }

        public async ValueTask DisposeAsync()
        {
            Http.Dispose();
            await _app.DisposeAsync();
            await _keepAlive.DisposeAsync();
        }
    }

    private sealed class Counter
    {
        private int _value;
        public int Value => Volatile.Read(ref _value);
        public void Increment() => Interlocked.Increment(ref _value);
        public void Reset() => Volatile.Write(ref _value, 0);
    }
}

// A product shared by several lines is loaded once per context: identity resolution fixes up the
// other lines' navigations, so the per-line count in the chapter is an upper bound.
public sealed class SharedProductTests
{
    [Fact]
    public void Lazy_loading_a_product_already_tracked_sends_no_query()
    {
        string database = $"Data Source=ch04-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        using var keepAlive = new SqliteConnection(database);
        keepAlive.Open();
        int statements = 0;
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(database)
            .LogTo(_ => statements++, [RelationalEventId.CommandExecuted]).Options;
        using (var seed = new AppDbContext(options))
        {
            seed.Database.EnsureCreated();
            var shared = new Product { Name = "Shared" };
            var order = new Order { CustomerId = 1 };
            for (int i = 0; i < 5; i++) order.Lines.Add(new OrderLine { Sku = $"S-{i}", Quantity = 1, Product = shared });
            seed.Customers.Add(new Customer { Id = 1, Name = "C" });
            seed.Orders.Add(order);
            seed.SaveChanges();
        }

        using var db = new AppDbContext(options);
        var lines = db.Orders.Single().Lines;
        statements = 0;
        var names = lines.Select(l => l.Product.Name).ToList();

        Assert.Equal(5, names.Count);
        Assert.Equal(1, statements);
    }
}
