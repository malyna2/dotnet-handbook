using Npgsql;
using Outbox.Core;

// Runs the consumer until Ctrl+C. Pass --from-beginning with a NEW group id to replay the whole topic,
// or reuse a group id after `kafka-consumer-groups.sh --reset-offsets` to simulate redelivery.
string group = args.FirstOrDefault(a => !a.StartsWith("--")) ?? "loyalty";
bool fromBeginning = args.Contains("--from-beginning");
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

await using var db = NpgsqlDataSource.Create("Host=localhost;Port=54345;Username=lab;Password=lab;Database=shop");
var consumer = new LoyaltyConsumer("localhost:9092", group, db);
Console.WriteLine($"Consuming {LoyaltyConsumer.Topic} as group '{group}'. Ctrl+C to stop.");
await consumer.RunAsync(_ => false, TimeSpan.FromDays(1), fromBeginning, cts.Token);
