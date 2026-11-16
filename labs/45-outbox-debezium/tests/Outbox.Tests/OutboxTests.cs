using System.Text;
using Confluent.Kafka;
using Npgsql;
using Outbox.Core;
using Xunit;

namespace Outbox.Tests;

// Needs the compose stack and the registered connector (verify.sh does both).
public sealed class OutboxTests : IAsyncLifetime
{
    private const string Kafka = "localhost:9092";
    private readonly NpgsqlDataSource _db = NpgsqlDataSource.Create("Host=localhost;Port=54345;Username=lab;Password=lab;Database=shop");
    private static long _next = DateTime.UtcNow.Ticks / 10_000;   // unique order ids per run
    private static long NextId() => Interlocked.Increment(ref _next);
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_outbox_row_reaches_Kafka_keyed_by_the_order()
    {
        long orderId = NextId();
        Guid eventId = await new OrderStore(_db).PlaceAsync(new OrderPlaced(orderId, 7, 42.50m), Ct);

        using var consumer = new ConsumerBuilder<string, string>(new ConsumerConfig
        { BootstrapServers = Kafka, GroupId = $"probe-{Guid.NewGuid()}", AutoOffsetReset = AutoOffsetReset.Earliest }).Build();
        consumer.Subscribe(LoyaltyConsumer.Topic);
        ConsumeResult<string, string>? found = null;
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (found is null && DateTime.UtcNow < deadline)
        {
            var r = consumer.Consume(TimeSpan.FromSeconds(1));
            if (r is not null && r.Message.Key == orderId.ToString()) found = r;
        }

        Assert.NotNull(found);
        Assert.Equal(eventId.ToString(), Encoding.UTF8.GetString(found.Message.Headers.GetLastBytes("id")));
        Assert.Equal("OrderPlaced", Encoding.UTF8.GetString(found.Message.Headers.GetLastBytes("eventType")));
        if (Environment.GetEnvironmentVariable("LAB_OBSERVATIONS") is { Length: > 0 } path)
            File.AppendAllText(path, $"key={found.Message.Key} partition={found.Partition.Value} value={found.Message.Value}{Environment.NewLine}");
    }

    [Fact]
    public async Task A_redelivered_event_awards_points_once()
    {
        long orderId = NextId();
        await new OrderStore(_db).PlaceAsync(new OrderPlaced(orderId, 8, 120m), Ct);
        string group = $"loyalty-{Guid.NewGuid()}";

        // The same event handled twice by the same handler and database: what a crash between the work and
        // the offset commit, a rebalance, or an offset reset produces. (A second group stands in for the replay.)
        await new LoyaltyConsumer(Kafka, group, _db).RunAsync(id => id == orderId, TimeSpan.FromSeconds(60), fromBeginning: true, Ct);
        await new LoyaltyConsumer(Kafka, group + "-replay", _db).RunAsync(id => id == orderId, TimeSpan.FromSeconds(60), fromBeginning: true, Ct);

        Assert.Equal(1L, await CountAsync("SELECT count(*) FROM loyalty_points WHERE order_id = @id", orderId));
    }

    [Fact]
    public async Task A_poison_event_goes_to_the_dead_letter_topic_and_awards_nothing()
    {
        long orderId = NextId();
        await new OrderStore(_db).PlaceAsync(new OrderPlaced(orderId, 9, -5m), Ct);
        var consumer = new LoyaltyConsumer(Kafka, $"loyalty-{Guid.NewGuid()}", _db);
        await consumer.RunAsync(id => id == orderId, TimeSpan.FromSeconds(60), fromBeginning: true, Ct);

        Assert.Equal(0L, await CountAsync("SELECT count(*) FROM loyalty_points WHERE order_id = @id", orderId));
        Assert.True(await DeadLetteredAsync(orderId), "the poison event is not on the DLQ topic");
    }

    [Fact]
    public async Task A_transient_failure_is_retried_and_succeeds_once()
    {
        long orderId = NextId();
        await new OrderStore(_db).PlaceAsync(new OrderPlaced(orderId, 10, 30m), Ct);
        int calls = 0;
        var consumer = new LoyaltyConsumer(Kafka, $"loyalty-{Guid.NewGuid()}", _db, dependency: o =>
        {
            if (o.OrderId != orderId) return Task.CompletedTask;
            return ++calls <= 2 ? throw new TransientException("partner API timed out") : Task.CompletedTask;
        });
        await consumer.RunAsync(id => id == orderId, TimeSpan.FromSeconds(60), fromBeginning: true, Ct);

        Assert.Equal(1L, await CountAsync("SELECT count(*) FROM loyalty_points WHERE order_id = @id", orderId));
        Assert.Equal(3, calls);   // two failures, then success
    }

    [Fact]
    public void Backoff_grows_exponentially_and_stays_under_the_cap()
    {
        var random = new Random(42);
        for (int attempt = 0; attempt < 10; attempt++)
        {
            TimeSpan d = Backoff.Delay(attempt, TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(5), random);
            Assert.InRange(d.TotalMilliseconds, 0, Math.Min(5000, 100 * Math.Pow(2, attempt)));
        }
    }

    private async Task<long> CountAsync(string sql, long id)
    {
        await using var cmd = _db.CreateCommand(sql);
        cmd.Parameters.AddWithValue("id", id);
        return (long)(await cmd.ExecuteScalarAsync(Ct))!;
    }

    private static Task<bool> DeadLetteredAsync(long orderId)
    {
        using var consumer = new ConsumerBuilder<string, string>(new ConsumerConfig
        { BootstrapServers = Kafka, GroupId = $"dlq-probe-{Guid.NewGuid()}", AutoOffsetReset = AutoOffsetReset.Earliest }).Build();
        consumer.Subscribe(LoyaltyConsumer.DeadLetterTopic);
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            var r = consumer.Consume(TimeSpan.FromSeconds(1));
            if (r is not null && r.Message.Key == orderId.ToString()) return Task.FromResult(true);
        }
        return Task.FromResult(false);
    }

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;
    public async ValueTask DisposeAsync() => await _db.DisposeAsync();
}
