using Npgsql;

namespace Outbox.Core;

// SOLUTION — the claim is the first write, under a unique key, in the same transaction as the effect.
public static class LoyaltyHandler
{
    public static async Task<HandleResult> HandleAsync(NpgsqlDataSource db, OrderPlaced order, Guid eventId, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        // A concurrent copy waits here on the first copy's uncommitted row, then sees the conflict.
        await using (var claim = new NpgsqlCommand(
            "INSERT INTO processed_messages (event_id) VALUES (@id) ON CONFLICT (event_id) DO NOTHING", conn, tx))
        {
            claim.Parameters.AddWithValue("id", eventId);
            if (await claim.ExecuteNonQueryAsync(ct) == 0)
                return HandleResult.Duplicate;   // already processed: do nothing, and let the offset move on
        }

        await using (var cmd = new NpgsqlCommand(
            "INSERT INTO loyalty_points (order_id, customer_id, points) VALUES (@order, @customer, @points)", conn, tx))
        {
            cmd.Parameters.AddWithValue("order", order.OrderId);
            cmd.Parameters.AddWithValue("customer", order.CustomerId);
            cmd.Parameters.AddWithValue("points", (int)Math.Floor(order.Total));
            await cmd.ExecuteNonQueryAsync(ct);
        }

        await tx.CommitAsync(ct);   // claim and effect together, or neither
        return HandleResult.Processed;
    }
}
