using System.Text.Json;
using Npgsql;

namespace Outbox.Core;

/// <summary>Writes the order and its event in ONE local transaction: no dual write.</summary>
public sealed class OrderStore(NpgsqlDataSource db)
{
    public async Task<Guid> PlaceAsync(OrderPlaced order, CancellationToken ct)
    {
        var eventId = Guid.NewGuid();
        await using var conn = await db.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        await using (var cmd = new NpgsqlCommand(
            "INSERT INTO orders (id, customer_id, total) VALUES (@id, @customer, @total)", conn, tx))
        {
            cmd.Parameters.AddWithValue("id", order.OrderId);
            cmd.Parameters.AddWithValue("customer", order.CustomerId);
            cmd.Parameters.AddWithValue("total", order.Total);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        await using (var cmd = new NpgsqlCommand(
            """
            INSERT INTO outbox (id, aggregatetype, aggregateid, type, payload)
            VALUES (@id, 'Order', @aggregateId, 'OrderPlaced', @payload::jsonb)
            """, conn, tx))
        {
            cmd.Parameters.AddWithValue("id", eventId);
            cmd.Parameters.AddWithValue("aggregateId", order.OrderId.ToString());
            cmd.Parameters.AddWithValue("payload", JsonSerializer.Serialize(order));
            await cmd.ExecuteNonQueryAsync(ct);
        }

        await tx.CommitAsync(ct);   // both rows or neither; Debezium sees the outbox row only after this
        return eventId;
    }
}
