using System.Text;
using System.Text.Json;
using Confluent.Kafka;
using Npgsql;

namespace Outbox.Core;

/// <summary>
/// Reads OrderPlaced events and awards loyalty points. At-least-once: the offset is stored only after the
/// message is handled or dead-lettered, so a crash in between redelivers it. Transient failures are retried
/// with backoff and jitter; permanent ones, and transient ones that exhaust their attempts, go to the DLQ topic.
/// </summary>
public sealed class LoyaltyConsumer(
    string bootstrapServers, string groupId, NpgsqlDataSource db,
    Func<OrderPlaced, Task>? dependency = null, int maxAttempts = 5)
{
    public const string Topic = "outbox.event.Order";
    public const string DeadLetterTopic = "outbox.event.Order.dlq";
    public int Attempts { get; private set; }

    public async Task RunAsync(Func<long, bool> stopWhenSeen, TimeSpan timeout, bool fromBeginning, CancellationToken ct)
    {
        using var consumer = new ConsumerBuilder<string, string>(new ConsumerConfig
        {
            BootstrapServers = bootstrapServers,
            GroupId = groupId,
            AutoOffsetReset = fromBeginning ? AutoOffsetReset.Earliest : AutoOffsetReset.Latest,
            EnableAutoCommit = true,
            EnableAutoOffsetStore = false,          // store the offset ourselves, after the work
        }).Build();
        using var dlq = new ProducerBuilder<string, string>(new ProducerConfig { BootstrapServers = bootstrapServers }).Build();
        consumer.Subscribe(Topic);

        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
        {
            ConsumeResult<string, string>? result = consumer.Consume(TimeSpan.FromMilliseconds(500));
            if (result is null) continue;

            var eventId = Guid.Parse(Encoding.UTF8.GetString(result.Message.Headers.GetLastBytes("id")));
            var order = JsonSerializer.Deserialize<OrderPlaced>(result.Message.Value)!;
            try
            {
                await HandleWithRetriesAsync(order, eventId, ct);
            }
            catch (Exception e) when (e is PermanentException or TransientException)
            {
                await dlq.ProduceAsync(DeadLetterTopic, new Message<string, string>
                {
                    Key = result.Message.Key,
                    Value = result.Message.Value,
                    Headers = new Headers
                    {
                        { "id", Encoding.UTF8.GetBytes(eventId.ToString()) },
                        { "dlq-reason", Encoding.UTF8.GetBytes(e.GetType().Name + ": " + e.Message) },
                        { "dlq-source", Encoding.UTF8.GetBytes($"{result.Topic}/{result.Partition.Value}/{result.Offset.Value}") },
                    },
                }, ct);
            }
            consumer.StoreOffset(result);   // handled or dead-lettered: now it may be committed
            if (stopWhenSeen(order.OrderId)) break;
        }
        consumer.Close();
    }

    private async Task HandleWithRetriesAsync(OrderPlaced order, Guid eventId, CancellationToken ct)
    {
        for (int attempt = 0; ; attempt++)
        {
            Attempts++;
            try
            {
                if (order.Total < 0) throw new PermanentException($"Order {order.OrderId} has a negative total.");
                if (dependency is not null) await dependency(order);
                await LoyaltyHandler.HandleAsync(db, order, eventId, ct);
                return;
            }
            catch (TransientException) when (attempt + 1 < maxAttempts)
            {
                await Task.Delay(Backoff.Delay(attempt, TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(5)), ct);
            }
        }
    }
}
