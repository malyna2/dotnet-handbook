using Npgsql;
using Outbox.Core;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton(NpgsqlDataSource.Create(
    builder.Configuration.GetConnectionString("Shop") ?? "Host=localhost;Port=54345;Username=lab;Password=lab;Database=shop"));
builder.Services.AddSingleton<OrderStore>();

var app = builder.Build();

// One request = one transaction with two inserts: the order and its OrderPlaced outbox row.
app.MapPost("/orders", async (OrderPlaced order, OrderStore store, CancellationToken ct) =>
{
    Guid eventId = await store.PlaceAsync(order, ct);
    return Results.Created($"/orders/{order.OrderId}", new { order.OrderId, eventId });
});

app.Run();
