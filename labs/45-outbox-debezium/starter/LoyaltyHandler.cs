using Npgsql;

namespace Outbox.Core;

// STARTER — this is the file you fix. It awards points correctly the first time a message arrives,
// and again every time the same message is delivered again. Make it idempotent.
public static class LoyaltyHandler
{
    public static async Task<HandleResult> HandleAsync(NpgsqlDataSource db, OrderPlaced order, Guid eventId, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        await using (var cmd = new NpgsqlCommand(
            "INSERT INTO loyalty_points (order_id, customer_id, points) VALUES (@order, @customer, @points)", conn, tx))
        {
            cmd.Parameters.AddWithValue("order", order.OrderId);
            cmd.Parameters.AddWithValue("customer", order.CustomerId);
            cmd.Parameters.AddWithValue("points", (int)Math.Floor(order.Total));
            await cmd.ExecuteNonQueryAsync(ct);
        }

        await tx.CommitAsync(ct);
        return HandleResult.Processed;
    }
}
