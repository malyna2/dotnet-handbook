# Chapter 11: Messaging and Background Work

@@TODO: write this chapter's introduction (what it makes the reader able to do, how its sections connect), then remove every @@ line.@@

@@SRC: introduction of old Chapter 9: Messaging & Distributed Systems@@

Somewhere along the road from junior to senior, you stop asking "how do I call this API?" and start asking "what happens when this API is down, slow, or lying to me?" That shift in mindset is the heart of distributed systems. This chapter is about the tools and patterns we use to build systems out of many independent parts that keep working even when some of those parts fail.

Messaging is the connective tissue. Instead of components shouting directly at each other and waiting for an answer, they drop notes in mailboxes and get on with their lives. That single change — from a phone call to a postal system — has profound consequences for how resilient, scalable, and maintainable your system becomes. Let's build up the "why" before we touch a single broker.

@@SRC: introduction of old Chapter 22: Background Processing, Scheduling & the Actor Model@@

Almost every non-trivial system does work that no user is waiting on: sending emails, retrying failed payments, rebuilding search indexes, aggregating metrics, cleaning up expired data. The naive approach - do it inline on the request thread - couples user-facing latency to work that has no business being on the hot path, and it silently loses that work whenever a request is cancelled or a pod restarts.

This chapter is about doing that work *deliberately*. We move from the humble in-process background loop, through dedicated job frameworks like Hangfire and Quartz.NET, and finally into the actor model and Microsoft Orleans - a paradigm that reframes how you think about concurrency and stateful services entirely. The through-line is a single question that separates mid-level from senior engineering: **not "how do I run this in the background?" but "what happens when it fails, when it runs twice, and when I have ten copies of my service running at once?"**

---

@@SRC: old Chapter 9: Messaging & Distributed Systems@@
## Why Messaging at All?

A naive checkout does everything inside the "Buy" request: charge the card, reserve inventory, send the confirmation email, update loyalty points, notify the warehouse, refresh analytics.

```
Customer ──HTTP──▶ [CheckoutService]
                        ├── calls PaymentService   (200ms, sometimes down)
                        ├── calls InventoryService (slow under load)
                        ├── calls EmailService     (third party, flaky)
                        ├── calls LoyaltyService
                        └── calls WarehouseService
```

This is **synchronous, temporal coupling**. Every downstream service must be up, fast, and healthy at the exact moment the customer clicks. The checkout is only as reliable as the *weakest* dependency, and only as fast as the *sum* of all of them. If the email provider hiccups, the customer sees an error for a purchase that actually succeeded.

Now flip it. The checkout does the essential, transactional work (charge, reserve inventory), then publishes an `OrderPlaced` message; email, loyalty, warehouse and analytics each react on their own schedule.

```
Customer ──HTTP──▶ [CheckoutService] ──publish──▶ [ Message Broker ]
                                                    │  OrderPlaced
                        ┌───────────────────────────┼───────────────┐
                        ▼            ▼               ▼               ▼
                  [EmailSvc]   [LoyaltySvc]   [WarehouseSvc]   [AnalyticsSvc]
```

Three things improve:

- **Decoupling.** The checkout doesn't know who consumes `OrderPlaced`; a fraud-detection consumer can be added next quarter without touching it.
- **Temporal decoupling.** Producer and consumer need not be up at the same time. If the email service is down, messages wait in the queue and are processed when it recovers; the purchase is unaffected.
- **Load levelling.** A burst waits in the queue instead of overloading the consumer, which drains it at its own rate. Add consumer instances to drain it faster; each service scales on its own load.

> **The core trade-off:** messaging buys you decoupling and resilience at the cost of *eventual consistency* and *complexity*. The email goes out "soon", not instantly, which suits most business processes. Knowing when it's *not* fine (e.g., "is this seat still available?") is a senior-level judgment call.

### Synchronous vs Asynchronous, More Precisely

Don't conflate "synchronous" with "request/response" or "async" with "messaging". They're orthogonal axes:

- **Synchronous:** the caller waits (logically) for the result — a REST or gRPC call — so both parties must be alive at once.
- **Asynchronous:** the caller hands off the work and continues; messaging is the classic vehicle.

Call synchronously when you need the answer *now* to proceed ("is this coupon valid?"); message when announcing that something happened or delegating work that can finish later.

@@SRC: old Chapter 9: Messaging & Distributed Systems@@
## Message Brokers Compared

A broker is the post office in the middle. All the major ones move messages, but their internal models differ enough that picking the wrong one causes real pain. Let's understand each on its own terms.

### RabbitMQ — The Smart Router (AMQP)

RabbitMQ implements AMQP 0-9-1, and its mental model is built from three primitives:

- **Exchange** — where publishers send messages. An exchange never stores anything; it's a router.
- **Queue** — where messages wait to be consumed. This is the buffer.
- **Binding** — a rule connecting an exchange to a queue, often with a *routing key* pattern.

```
                        ┌──────────────┐   binding: "order.*"   ┌──────────┐
publisher ──"order.eu"─▶│   Exchange   │───────────────────────▶│ Queue A  │─▶ consumer
                        │  (topic)     │───────────────────────▶│ Queue B  │─▶ consumer
                        └──────────────┘   binding: "order.eu"  └──────────┘
```

Exchange types define the routing logic:

- **Direct** — routing key must match exactly.
- **Topic** — routing key matches a pattern with wildcards (`order.eu.*`).
- **Fanout** — ignore the key, send to every bound queue (true broadcast).
- **Headers** — route on message header attributes instead of a key.

The killer feature is this flexible routing. RabbitMQ excels when you have complex "who should get this?" logic and want relatively low-latency, per-message delivery with acknowledgements. Messages are typically *consumed and removed* — once acknowledged, they're gone.

> **Pick RabbitMQ when:** you need rich routing, per-message acknowledgements, priority queues, and traditional "task queue" or "work distribution" semantics. It's the Swiss Army knife of brokers.

### Apache Kafka — The Distributed Log

Kafka throws away the "queue that empties" model entirely. A Kafka **topic** is an append-only **log** split into **partitions**. Messages aren't deleted when read — they sit there for a configured retention period (hours, days, or forever). Consumers track their own position (**offset**) in the log.

```
Topic "orders", 3 partitions:

Partition 0: [m0][m3][m6][m9] ...   ← append only, ordered
Partition 1: [m1][m4][m7]     ...
Partition 2: [m2][m5][m8]     ...
                 ▲
      Consumer group "billing" reads offset 5 in P0
      Consumer group "analytics" reads offset 2 in P0  (independent!)
```

Key ideas:

- **Partitions** are the unit of parallelism and ordering. Messages within a single partition are strictly ordered; across partitions there's no global order. A message's partition is chosen by hashing its key — so all events for `customerId=42` land in the same partition and stay ordered relative to each other.
- **Consumer groups** enable competing consumers. Within a group, each partition is assigned to exactly one consumer, so N partitions means at most N parallel consumers per group. Different groups read the *same* data independently — that's how you get pub/sub over a log.
- **Retention + replay.** Because the log persists, a brand-new consumer can start from offset 0 and re-process all of history. This is the foundation of **event streaming** and event sourcing.

> **Pick Kafka when:** you have high-throughput event streams, need replayability, want multiple independent consumers of the same firehose, or are building stream-processing pipelines. It's less a "message queue" and more a "distributed commit log you can subscribe to".

> **Modern Kafka (4.0+):** ZooKeeper has been removed — clusters now run on **KRaft**, Kafka's built-in metadata quorum, so there's one system to operate instead of two. And **KIP-932 "Queues for Kafka"** (early access in 4.0) adds *share groups*, giving Kafka queue-like semantics — per-message acknowledgement, redelivery, and unordered consumption beyond the partition count — which softens the classic "Kafka is a log, not a queue" framing.

The mental shift: RabbitMQ *pushes* messages and forgets them; Kafka *stores* an ordered history that consumers *pull* from at their own pace.

### Azure Service Bus — The Enterprise Managed Broker

Azure Service Bus (ASB) is a fully-managed broker with a queue/topic model closer to RabbitMQ's semantics than Kafka's, but with enterprise features baked in:

- **Queues** for point-to-point, **Topics + Subscriptions** for pub/sub (each subscription is effectively a virtual queue with its own filter).
- Built-in **dead-letter queues**, **sessions** (for ordered, stateful message groups), **scheduled delivery**, **duplicate detection**, and **transactions**.
- Deep integration with the rest of Azure and Azure AD auth.

> **Pick Azure Service Bus when:** you're on Azure, want a managed service (no broker to operate), and need reliable enterprise messaging with features like sessions and duplicate detection out of the box.

### AWS SQS + SNS — The Cloud-Native Duo

AWS splits the responsibilities:

- **SQS (Simple Queue Service)** is a managed queue. **Standard** queues offer massive throughput with at-least-once delivery and *best-effort* ordering. **FIFO** queues guarantee ordering and exactly-once *processing* (within limits) at lower throughput.
- **SNS (Simple Notification Service)** is pub/sub — publish once, fan out to many subscribers (including multiple SQS queues, Lambda, HTTP endpoints).

The idiomatic pattern is **SNS → SQS fan-out**: publish an event to an SNS topic, and each interested service has its own SQS queue subscribed to it. Each service gets its own durable buffer.

```
              ┌──────────┐   ┌── SQS: email-queue    ──▶ EmailService
publisher ──▶ │   SNS    │──▶├── SQS: warehouse-queue ──▶ WarehouseService
              │  topic   │   └── SQS: analytics-queue ──▶ AnalyticsService
              └──────────┘
```

> **Pick SQS/SNS when:** you're on AWS and want dead-simple, serverless-friendly, pay-per-use messaging without running infrastructure.

### Quick Comparison

| Dimension | RabbitMQ | Kafka | Azure SB | SQS/SNS |
|---|---|---|---|---|
| Model | Queues + smart exchanges | Distributed log | Queues + topics | Queue (SQS) + pub/sub (SNS) |
| Message after read | Deleted on ack | Retained (replayable) | Deleted on complete | Deleted on delete |
| Ordering | Per-queue | Per-partition | Per-session | FIFO queues only |
| Best at | Flexible routing, task queues | High-throughput streaming, replay | Managed enterprise on Azure | Serverless cloud fan-out |
| You operate it | Yes (or managed) | Yes (or managed) | No (managed) | No (managed) |

That table compares products; the more important comparison is between the three *interaction models* they implement. Decide which model your problem is first — the broker choice usually falls out of it.

| | Work queue (RabbitMQ queue, ASB queue, SQS) | Event stream (Kafka) | Pub-sub event bus (SNS→SQS, ASB topics, fanout exchange) |
|---|---|---|---|
| What it models | A to-do list: "do this task" | A ledger: ordered, replayable history of facts | A broadcast: "this happened", to whoever cares |
| Delivery / replay | Each message to one worker; deleted on ack; no replay | Retained for the retention window; consumers track offsets; replay from any point | Each subscriber gets its own copy; gone once that subscriber acks; no replay for late joiners |
| Consumer model | Competing consumers; add workers to add throughput | Consumer groups; parallelism capped at partition count; groups read independently | 0..N independent subscribers, each with its own buffer; publisher unaware |
| Reach for it when | Delegating work, load-leveling, background jobs | High-throughput events, event sourcing, many independent readers of one firehose | Decoupling domains; adding consumers without touching the publisher |
| Watch out for | DLQ silently filling; out-of-order under competing consumers | No global order across partitions; retention and partition-count decisions are up-front commitments | Commands smuggled in as "events" (hidden coupling); new subscribers can't see the past |

@@SRC: old Chapter 9: Messaging & Distributed Systems@@
## Core Messaging Patterns

Regardless of broker, the same handful of patterns recur. Learn them once and you can map them onto any technology.

### Publish/Subscribe

One publisher, many independent subscribers. The publisher doesn't know who's listening. This is the backbone of event-driven systems. (Fanout exchange in RabbitMQ, consumer groups in Kafka, Topics in ASB, SNS in AWS.)

### Request/Response

Sometimes you *do* need an answer over a message channel. The requester sends a message with a `ReplyTo` address and a `CorrelationId`, then waits for a response message on that reply channel matching the ID. Frameworks like MassTransit make this look like an `await`, but under the hood it's two one-way messages stitched together.

> **Best practice:** avoid request/response over messaging when a plain HTTP/gRPC call would do. You lose the simplicity of synchronous calls and gain latency. Reserve it for cases where you specifically want the broker's load-balancing or resilience.

### Competing Consumers

Multiple instances of the same consumer read from one queue; the broker hands each message to exactly one of them. This is how you scale throughput horizontally — just add more consumers.

```
                    ┌──▶ [Worker 1]
[Queue] ──messages──┼──▶ [Worker 2]   ← broker load-balances, each msg to one worker
                    └──▶ [Worker 3]
```

### Dead-Letter Queues (DLQ)

A message that can't be processed — malformed, or still throwing after N retries — must neither block the queue nor be lost. It moves to a **dead-letter queue**, a holding pen for "poison messages" that a human or a tool inspects later. In Azure Service Bus that happens when the handler dead-letters it explicitly, or automatically once its delivery count exceeds `MaxDeliveryCount` (10 by default): every abandon or expired lock counts as a delivery. Nothing drains a DLQ; messages stay until someone reads them.

> **Pitfall:** a DLQ silently filling up is one of the most common production incidents. Always alert on DLQ depth. A message in the DLQ usually means a bug or a bad assumption — investigate, don't just retry blindly.

### Message Ordering

A FIFO queue hands messages out in order; nothing makes them *finish* in order. Competing consumers, or one consumer with concurrency above 1, process messages side by side, so worker 2 finishes message 5 before worker 1 finishes message 4. A message that is abandoned or whose lock expires is processed again after the later messages other receivers took meanwhile, and with prefetch it goes to the back of the local buffer. Solutions:

- **Kafka:** order is guaranteed *within a partition*. Route related messages to the same partition via a key.
- **Azure Service Bus:** **sessions**. The sender sets `SessionId` (say, the order ID); a receiver that accepts the session holds an exclusive lock on all its messages and receives them in order, one receiver per session, many sessions in parallel. Sessions are chosen when the queue or subscription is created and can't be switched on later. **RabbitMQ:** a consistent-hash exchange pins each key to one queue with one consumer.
- **Design around it:** the best answer is often to make consumers tolerant of out-of-order delivery (e.g., include version numbers and ignore stale updates).

@@SRC: old Chapter 9: Messaging & Distributed Systems@@
## MassTransit: Messaging for .NET

Writing raw broker client code (the RabbitMQ `IModel`, Kafka's consumer loop) is tedious and error-prone. **MassTransit** is the dominant .NET abstraction layer. It gives you a broker-agnostic API, built-in retry/redelivery, the outbox, sagas, and serialization — while letting you swap RabbitMQ for Azure Service Bus with a config change.

> **A note on licensing (2025):** In 2025 the MassTransit team announced that v9 will ship under a **commercial license** (official release expected around early 2026), with **v8 remaining the last broadly free OSS version** (Apache 2.0), maintained through the transition. That complicates the old "default free choice" framing, so give more weight to the alternatives when starting new projects: **NServiceBus** is also commercial, while **Rebus** and **Wolverine** are OSS — as are the raw broker client libraries. The concepts in this chapter transfer to all of them.

Let's define a message contract. In MassTransit, an interface or record shared between publisher and consumer *is* the contract.

```csharp
// Shared contract library, referenced by both producer and consumers.
namespace Shop.Contracts;

// An EVENT: past tense, states a fact. "This happened."
public record OrderPlaced
{
    public Guid OrderId { get; init; }
    public string CustomerEmail { get; init; } = default!;
    public decimal Total { get; init; }
    public DateTime PlacedAtUtc { get; init; }
}
```

A **consumer** implements `IConsumer<T>`:

```csharp
using MassTransit;
using Microsoft.Extensions.Logging;

public class SendConfirmationEmailConsumer : IConsumer<OrderPlaced>
{
    private readonly IEmailSender _email;
    private readonly ILogger<SendConfirmationEmailConsumer> _log;

    public SendConfirmationEmailConsumer(
        IEmailSender email,
        ILogger<SendConfirmationEmailConsumer> log)
    {
        _email = email;
        _log = log;
    }

    public async Task Consume(ConsumeContext<OrderPlaced> context)
    {
        var msg = context.Message;
        _log.LogInformation("Sending confirmation for order {OrderId}", msg.OrderId);

        // If this throws, MassTransit applies the configured retry policy,
        // and eventually dead-letters the message if it keeps failing.
        await _email.SendAsync(
            to: msg.CustomerEmail,
            subject: $"Order {msg.OrderId} confirmed",
            body: $"Thanks! Your total was {msg.Total:C}.");
    }
}
```

Wiring it up in a .NET host with RabbitMQ:

```csharp
builder.Services.AddMassTransit(x =>
{
    // Register all consumers in the assembly.
    x.AddConsumer<SendConfirmationEmailConsumer>();

    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host("localhost", "/", h =>
        {
            h.Username("guest");
            h.Password("guest");
        });

        // Retry with exponential backoff before dead-lettering.
        cfg.UseMessageRetry(r => r.Exponential(
            retryLimit: 5,
            minInterval: TimeSpan.FromSeconds(1),
            maxInterval: TimeSpan.FromMinutes(1),
            intervalDelta: TimeSpan.FromSeconds(2)));

        // Auto-create the queue and bind it to the OrderPlaced exchange.
        cfg.ConfigureEndpoints(context);
    });
});
```

Publishing the event from the checkout service:

```csharp
public class CheckoutService
{
    private readonly IPublishEndpoint _publish;

    public CheckoutService(IPublishEndpoint publish) => _publish = publish;

    public async Task PlaceOrderAsync(Order order)
    {
        // ... transactional work: charge card, reserve inventory ...

        // Publish is fire-and-forget pub/sub — every subscribed consumer gets a copy.
        await _publish.Publish(new OrderPlaced
        {
            OrderId = order.Id,
            CustomerEmail = order.CustomerEmail,
            Total = order.Total,
            PlacedAtUtc = DateTime.UtcNow
        });
    }
}
```

Notice how little broker-specific code there is. `Publish` vs `Send`: **Publish** is pub/sub (goes to all subscribers of that event type); **Send** targets one specific endpoint (a command to one handler). This maps directly onto the events-vs-commands distinction below.

### Brief Mentions: NServiceBus and Rebus

- **NServiceBus** (from Particular Software) is the commercial, batteries-included, enterprise-grade option. It has the deepest saga tooling, excellent monitoring (ServiceInsight/ServicePulse), and strong support contracts. If you're a large enterprise that wants a vendor to call, this is it.
- **Rebus** is the lightweight, free, "just enough" alternative. Smaller API surface, easy to learn, fewer bells and whistles. Great when MassTransit feels like too much.

All three share the same conceptual model, so skills transfer.

@@SRC: old Chapter 9: Messaging & Distributed Systems@@
## Event-Driven Architecture: Events, Commands, and Messages

These three words get used interchangeably and it causes real confusion. Let's be precise.

- A **message** is the generic envelope — any data moving through the broker.
- A **command** is a message that *instructs* a specific recipient to do something. Imperative, present tense: `ChargePayment`, `ShipOrder`. It has **one** logical handler. The sender expects it to be acted upon and often cares whether it succeeded.
- An **event** is a message that *announces* something already happened. Past tense: `PaymentCharged`, `OrderShipped`. It has **zero or many** subscribers. The publisher doesn't know or care who reacts.

```
Command:  Sender ──"ShipOrder"──▶ [exactly one handler]     (imperative, coupling to intent)
Event:    Publisher ──"OrderShipped"──▶ [0..N subscribers]  (declarative, decoupled)
```

> **Best practice:** commands are owned by the *sender's* vocabulary ("I want you to do X"); events are owned by the *publisher's* vocabulary ("X happened in my domain"). If you find a service publishing an "event" that's really telling another service what to do, you've smuggled a command into an event's clothing — and coupled your services more than you think.

**Event streaming vs queues** is the other axis. A queue is a to-do list: work gets pulled off and disappears. A stream (Kafka) is a ledger: an ordered, replayable history of facts. Choose a queue for "do this task"; choose a stream for "record this fact so anyone, now or later, can build state from it."

@@SRC: old Chapter 9: Messaging & Distributed Systems@@
## Distributed Patterns Every Senior Should Know

This is where distributed systems get genuinely hard — and where interviews and production incidents both live.

### Idempotent Consumers

You will receive duplicate messages (*Delivery Guarantees* below explains why). An **idempotent** consumer produces the same result whether it processes a message once or five times, and that is what makes at-least-once delivery safe to live with.

> **Pay attention.** **Check-then-act deduplication does the work twice.** "If this message ID was processed, return; do the work; record the ID" leaves a window between the check and the record. Two copies delivered at the same time — two instances, `MaxConcurrentCalls` above 1, or a redelivery overlapping a slow first attempt — both pass the check before either records the ID, and both do the work. A crash between the work and the record loses the record, so the redelivery does the work again. The fix is to make the record *be* the check: insert the ID under a unique key first, in the same transaction as the effect.

```csharp
public async Task Consume(ConsumeContext<OrderPlaced> context)
{
    CancellationToken ct = context.CancellationToken;
    await using var tx = await _db.Database.BeginTransactionAsync(ct);
    _db.ProcessedMessages.Add(new ProcessedMessage(context.MessageId!.Value));  // unique key on MessageId
    try { await _db.SaveChangesAsync(ct); }                                       // the claim: a duplicate stops here
    catch (DbUpdateException e) when (IsUniqueViolation(e)) { return; }           // 2601/2627 SQL Server, 23505 PostgreSQL

    _db.LoyaltyPoints.Add(LoyaltyPoints.For(context.Message));                    // the effect, in the same transaction
    await _db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
}
```

Walk the second copy through it. Its insert waits on the first copy's uncommitted key. If the first commits, the insert fails with a duplicate-key error and the consumer returns before the effect; if the first rolls back, the claim disappears with it, and the second copy does the work. [Chapter 3: Idempotency Keys](#idempotency-keys-making-post-retry-safe) walks the same mechanism for HTTP. An effect outside your database, such as a payment API, can't join the transaction: pass the message's key to it as the provider's idempotency key as well.

> **Best practice:** design every consumer to be idempotent *by default*; it's cheaper than chasing exactly-once delivery. Use natural keys where you can: a consumer that inserts the order under a unique order ID has its claim built in, with no separate processed-messages table.

### The Transactional Outbox

A handler writes to the database *and* publishes a message. What if it crashes between them?

```
1. Save Order to DB   ✓
2. Publish OrderPlaced ✗  ← crash here: DB updated but no one notified!
```

An order exists that no downstream service knows about. Publish first and you get the opposite bug: a message for an order that was never saved. No ordering of the two calls fixes this: they are two systems with no shared transaction (the *dual write*).

The **transactional outbox** writes the outgoing message into an `outbox` table in the *same transaction* as the business data. A separate relay reads the outbox, publishes to the broker, and marks rows as sent.

```
┌─────────── single DB transaction ───────────┐
│  INSERT INTO orders (...)                    │
│  INSERT INTO outbox (OrderPlaced payload)    │
└──────────────────────────────────────────────┘
                    │  (commit is atomic)
                    ▼
        [Outbox Relay polls table]
                    │
                    ▼  publishes, then marks sent
              [ Message Broker ]
```

Both inserts commit atomically, so "saved but not published" can't happen. A relay that crashes after publishing but before marking the row publishes it again: at-least-once, so consumers deduplicate. MassTransit has a built-in transactional outbox:

```csharp
x.AddEntityFrameworkOutbox<AppDbContext>(o =>
{
    o.UseSqlServer();
    o.UseBusOutbox(); // messages published in a handler go through the outbox
});
```

The mirror image is the **inbox**: the processed-message claims from *Idempotent Consumers*. The outbox makes sending reliable; the inbox makes receiving safe to repeat. Together they give effectively-once behavior on at-least-once transport.

@@SRC: old Chapter 9: Messaging & Distributed Systems@@
## Delivery Guarantees

Getting this wrong loses or duplicates data. There are three possible guarantees:

- **At-most-once.** Fire and forget. The message is delivered zero or one times — it may be lost, never duplicated. Fast, simplest, acceptable for high-volume telemetry where losing one reading doesn't matter.
- **At-least-once.** The message will be delivered, but possibly more than once. This is the default and most common guarantee in real brokers. It's achieved with acknowledgements: the consumer processes a message, then acks. If it crashes before acking, the broker redelivers. But if it processed *and then crashed before the ack*, you get a duplicate.
- **Exactly-once.** The holy grail — delivered and processed precisely once. And it is *extraordinarily* hard.

### Why Exactly-Once Is (Almost) a Myth

The acknowledgement is itself a network operation that can fail. A consumer processes a message and sends an ack; the ack is lost; the broker, not knowing the message was handled, redelivers it. Two parties on a lossy channel can never be *certain* they agree on "was this done?" — the **Two Generals Problem**.

Systems that advertise "exactly-once" (like Kafka's transactional producers or SQS FIFO) achieve it under specific constraints, and usually it's really *exactly-once processing*, not delivery — the transport is at-least-once, and duplicates are suppressed by deduplication.

> **The pragmatic senior answer:** you don't chase exactly-once *delivery*. You accept **at-least-once delivery** and make your consumers **idempotent**, giving you exactly-once *effects*. This combination is robust, achievable, and how virtually every serious system does it.

### Deduplication

Every message carries a unique ID, and the consumer claims it atomically with the effect (the inbox pattern, under *Idempotent Consumers*), so repeats are discarded. Brokers help only at the edges: Azure Service Bus duplicate detection and SQS FIFO deduplication (5 minutes) drop a second *send* of the same ID — a producer retrying — but never see a redelivery of a message already sent. Application-level dedup on a business key is the reliable layer: it covers redeliveries, longer windows and broker changes.

@@SRC: old Chapter 9: Messaging & Distributed Systems@@
## Wrapping Up

Zoom out and a coherent philosophy emerges. Distributed systems fail in parts, so we design for *partial failure*: decouple with messaging so a downstream outage doesn't cascade; accept *at-least-once* delivery and make consumers *idempotent* rather than chasing the mirage of exactly-once; use the *outbox* to bridge database and broker atomically; coordinate multi-step work with *sagas* and compensations instead of impossible distributed transactions; protect ourselves with *retries, circuit breakers, and bulkheads*; and embrace *eventual consistency* as the natural, affordable state of a decoupled system — reserving stronger guarantees for the rare places that truly need them.

None of these patterns is exotic once you've internalized the core insight: **the network is unreliable, and every design decision is a negotiation with that fact.** Master that negotiation, and you're thinking like a senior engineer.

@@SRC: old Chapter 22: Background Processing, Scheduling & the Actor Model@@
## Part A — Background Processing in .NET

### `IHostedService` and `BackgroundService`

The .NET Generic Host owns a collection of `IHostedService` instances. When the host starts, it calls `StartAsync` on each; when it stops, it calls `StopAsync`. This is the foundational hook for anything that needs to live for the lifetime of your application - a message consumer, a polling loop, a cache warmer.

```csharp
public interface IHostedService
{
    Task StartAsync(CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);
}
```

Implementing this raw is fiddly: `StartAsync` is expected to *return quickly* (the host awaits it before considering itself started), so you cannot simply `await` a long loop inside it. You would have to spin up a `Task`, stash it in a field, wire up a `CancellationTokenSource`, and join it in `StopAsync`. That boilerplate is exactly what `BackgroundService` exists to eliminate.

```csharp
public abstract class BackgroundService : IHostedService, IDisposable
{
    protected abstract Task ExecuteAsync(CancellationToken stoppingToken);
    // StartAsync stores the Task returned by ExecuteAsync; StopAsync
    // signals the token and awaits that Task (up to the shutdown timeout).
}
```

You override one method, `ExecuteAsync`, and treat the supplied `stoppingToken` as your signal to wind down.

> **Key mental model:** `ExecuteAsync` runs on a background flow, not a request. There is no ambient `HttpContext`, no scoped services unless you create a scope, and no per-request lifetime. A singleton `BackgroundService` that needs a scoped `DbContext` **must** open its own scope per unit of work.

### The Worker Service template

.NET ships a project template for exactly this: `dotnet new worker`. It produces a console app whose `Program.cs` builds a host and registers a single worker. It is the right starting point for a standalone processor - a queue consumer, a scheduled batch job runner, an ETL pipeline - that has no HTTP surface.

Here is a realistic worker that drains an in-memory channel of outbound emails. Note how it acquires a fresh DI scope for each item and never lets one poison message kill the loop.

```csharp
public sealed class EmailDispatchWorker : BackgroundService
{
    private readonly ChannelReader<EmailJob> _reader;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<EmailDispatchWorker> _logger;

    public EmailDispatchWorker(
        Channel<EmailJob> channel,
        IServiceScopeFactory scopeFactory,
        ILogger<EmailDispatchWorker> logger)
    {
        _reader = channel.Reader;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // ReadAllAsync honours the token and completes gracefully on shutdown.
        await foreach (var job in _reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var sender = scope.ServiceProvider.GetRequiredService<IEmailSender>();
                await sender.SendAsync(job, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break; // shutting down - stop cleanly
            }
            catch (Exception ex)
            {
                // One bad message must not tear down the whole worker.
                _logger.LogError(ex, "Failed to dispatch email {JobId}", job.Id);
            }
        }
    }
}
```

Registration is one line, and `System.Threading.Channels` gives you a bounded, back-pressured, thread-safe hand-off between producers (say, a controller) and this consumer:

```csharp
builder.Services.AddSingleton(_ =>
    Channel.CreateBounded<EmailJob>(new BoundedChannelOptions(1000)
    {
        FullMode = BoundedChannelFullMode.Wait
    }));
builder.Services.AddHostedService<EmailDispatchWorker>();
```

> **Best practice:** Prefer a bounded channel over an unbounded one. An unbounded queue turns a downstream slowdown into an out-of-memory crash. Bounded + `Wait` applies back-pressure to producers, which is almost always what you want.

### The outbox-driven worker

In-memory channels have a fatal flaw for anything that matters: **if the process dies, the queue dies with it.** When a user places an order, you write the order to the database *and* you want to publish an `OrderPlaced` event - and doing that as two separate operations means a crash between them loses one side. That is the dual-write problem, and the **transactional outbox** pattern solves it by making the "I need to publish X" fact part of the *same database transaction* as the business change. Chapter 9 covers the mechanics - the outbox table, the atomic commit, and MassTransit's built-in support. Here the point is the other half of the pattern: the background worker that actually drains the table.

The worker polls unprocessed rows and publishes them, marking each as done only after the broker acknowledges:

```csharp
protected override async Task ExecuteAsync(CancellationToken stoppingToken)
{
    using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
    while (await timer.WaitForNextTickAsync(stoppingToken))
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();

        var batch = await db.OutboxMessages
            .Where(m => m.ProcessedOnUtc == null)
            .OrderBy(m => m.OccurredOnUtc)
            .Take(50)
            .ToListAsync(stoppingToken);

        foreach (var msg in batch)
        {
            await bus.PublishAsync(msg.Type, msg.Payload, stoppingToken);
            msg.ProcessedOnUtc = DateTime.UtcNow;
        }
        await db.SaveChangesAsync(stoppingToken);
    }
}
```

`PeriodicTimer` (introduced in .NET 6) is the idiomatic modern loop timer: it is awaitable, allocation-light, cancellation-aware, and it does not stack up ticks if an iteration runs long.

### Graceful shutdown and the `CancellationToken`

When Kubernetes sends `SIGTERM`, or an operator hits Ctrl+C, the host begins a *graceful* shutdown: it signals the `stoppingToken`, then waits up to a timeout (default 30 seconds) for `ExecuteAsync` to complete. Work that ignores the token is work that gets **killed mid-flight** when the timeout expires.

Two obligations fall on you:

1. **Observe the token** in every loop and every `await` that supports it, so you stop *starting* new work promptly.
2. **Finish or safely abandon in-flight work** before the timeout, so you don't leave half-completed operations.

```csharp
builder.Services.Configure<HostOptions>(o =>
    o.ShutdownTimeout = TimeSpan.FromSeconds(60)); // give long units room to drain
```

> **Pitfall:** Passing `CancellationToken.None` to your database and HTTP calls "so they don't get interrupted" is the wrong instinct. It means a shutdown must wait the full timeout and then hard-kill anyway. Thread the real token through, and design each unit of work to be *resumable* rather than uninterruptible.

### Scaling workers, at-least-once delivery, and idempotency

The moment you run more than one instance of your service - and in any serious deployment you will, for availability alone - two copies of that outbox worker are polling the same table. Both may grab the same row. Your message gets published twice.

You cannot engineer this possibility away entirely. Distributed systems give you **at-least-once** delivery as the practical default; exactly-once is a comforting fiction that, when you look closely, is always at-least-once plus idempotent processing (Chapter 9 explains why). So the senior move is to stop fighting duplicates and instead make processing **idempotent** - safe to run more than once with the same net effect. The implementation - dedupe on a natural or supplied idempotency key, with a unique index as your backstop - is covered in Chapter 21; apply it to every handler a worker runs.

For the polling contention itself, options range from a `SELECT ... FOR UPDATE SKIP LOCKED` (PostgreSQL) to claiming rows with an atomic `UPDATE ... SET LockedBy = @me WHERE ...`, to simply electing a single leader (covered in Part B) so only one instance polls at all. The right answer depends on throughput, but the principle is constant: **assume duplicates and design so they don't hurt.**

@@SRC: old Chapter 22: Background Processing, Scheduling & the Actor Model@@
## Async Request-Reply: 202, a Status Resource, and Retry-After

Some work doesn't fit in a request: a four-minute report, a transcode, an import. Keep it in the request and three things break. Front ends cut long requests (App Service at 230 seconds: [Chapter 51, Case 11](#case-11-large-uploads-fail-at-almost-exactly-four-minutes)). A client that times out retries, and the server, which never noticed the first client leave, does the work twice. And the final `200` promises work that happens only if nothing crashes first. The Azure Architecture Center's *Asynchronous Request-Reply* pattern replaces the one long request with a short `POST` that creates a job and short `GET`s that read it:

```
client                                   API                                     worker
  │ POST /reports                          │ validate: a bad request gets its 400 now
  │ Idempotency-Key: 5f3b… ───────────────►│ one transaction: job row (Pending) + outbox row
  │◄── 202 Accepted ───────────────────────│        relay ──► queue ──► claim the job, run it,
  │    Location: /jobs/7                   │                            Status = Succeeded
  │    Retry-After: 5                      │
  │ GET /jobs/7 ──────────────────────────►│ 200 { "status": "Running", … }
  │ GET /jobs/7 ──────────────────────────►│ 303 See Other, Location: /reports/7
  │ GET /reports/7 ───────────────────────►│ 200 the report
```

**The `POST` does only what must be synchronous.** It validates, then records the job and an outbox message in one transaction ([Chapter 9: The Transactional Outbox](#the-transactional-outbox)); publishing to the broker after the commit would be the dual write again. It answers `202 Accepted` with `Location`, the status resource (not the result), and `Retry-After`, the seconds until a poll is worth making. A unique index on the idempotency key turns a retried `POST` into a lookup of the job it already created; [Chapter 3: Idempotency Keys](#idempotency-keys-making-post-retry-safe) has the claim-first mechanics.

```csharp
app.MapPost("/reports", async (ReportRequest request, [FromHeader(Name = "Idempotency-Key")] string key,
                               AppDbContext db, HttpResponse response, CancellationToken ct) =>
{
    if (!request.TryValidate(out var errors)) return Results.ValidationProblem(errors);

    var job = new Job { Id = Guid.NewGuid(), IdempotencyKey = key, Status = JobStatus.Pending, CreatedAt = DateTimeOffset.UtcNow };
    db.Jobs.Add(job);
    db.Outbox.Add(OutboxMessage.For(new GenerateReport(job.Id, request)));   // one SaveChanges, one transaction
    try { await db.SaveChangesAsync(ct); }
    catch (DbUpdateException e) when (IsUniqueViolation(e))                 // a retry: this key already has a job
    {
        db.ChangeTracker.Clear();
        job = await db.Jobs.SingleAsync(j => j.IdempotencyKey == key, ct);
    }
    response.Headers.RetryAfter = "5";
    return Results.Accepted($"/jobs/{job.Id}", new { job.Id, job.Status });
});

app.MapGet("/jobs/{id:guid}", async (Guid id, AppDbContext db, HttpResponse response, CancellationToken ct) =>
{
    Job? job = await db.Jobs.FindAsync([id], ct);
    if (job is null) return Results.NotFound();
    if (job.Status != JobStatus.Succeeded)
        return Results.Ok(new { job.Status, job.CreatedAt, job.LastUpdatedAt, job.Error });  // Error: RFC 9457 problem details
    response.Headers.Location = $"/reports/{job.Id}";       // the Redirect helpers send 301/302/307/308, never 303
    return Results.StatusCode(StatusCodes.Status303SeeOther);
});
```

**The status resource answers `200` until the job is done.** Its body carries a documented set of states (`Pending`, `Running`, `Succeeded`, `Failed`, `Canceled`), the timestamps that tell a slow job from a stuck one, and a problem-details `error` when it fails. On success it answers `303 See Other` to the result. A `303` makes the client follow with a `GET`; on a `302`, some clients replay the original method. Don't answer `404` for "not ready yet": the client can't tell it from a wrong ID.

**The worker is a `BackgroundService`** reading the queue (or a queue-triggered Function). Delivery is at-least-once, so it claims the job with a conditional update before running it:

```csharp
DateTimeOffset now = DateTimeOffset.UtcNow;
int claimed = await db.Jobs
    .Where(j => j.Id == message.JobId
             && (j.Status == JobStatus.Pending || (j.Status == JobStatus.Running && j.LeaseUntil < now)))
    .ExecuteUpdateAsync(s => s.SetProperty(j => j.Status, JobStatus.Running)
                              .SetProperty(j => j.LeaseUntil, now.AddMinutes(15)), ct);
if (claimed == 0) return;   // another delivery owns it or finished it: complete the message, do nothing
```

The lease lets a redelivery take over a job whose worker died mid-run, so a rerun must be safe: write the result keyed by the job ID, overwriting any partial one. A transient failure throws, and the broker redelivers the message; a permanent one, such as input that validation couldn't catch, is dead-lettered at once. Either way the job must end `Failed`, with a reason someone can act on: set by the worker on a permanent failure or on the last attempt, by whatever drains the dead-letter queue, or by a sweeper that fails jobs whose `LastUpdatedAt` has stopped moving. A status stuck at `Running` is the HTTP face of a dead-letter queue nobody watches.

**Clients poll, or get called back.** A polling client waits `Retry-After` between `GET`s and gives up at a deadline. A callback (a webhook, a SignalR message) saves the polling, but needs a reachable, authenticated endpoint on the client's side and is itself delivered at-least-once ([Chapter 26](#chapter-26-real-world-engineering-essentials) covers webhook signatures), so keep polling as the fallback. Expose `DELETE /jobs/{id}` if a job can be cancelled, and delete old jobs and results on a retention schedule.

---

@@SRC: practice from old module page Part 1 · Module 4: Messaging and Long-Running Work@@

## Prove it

Two programs: the first shows the broker redelivering a message to a handler that is still working, the second shows why the usual deduplication doesn't stop the duplicate effect. Both live in [`verify/path`](https://github.com/malyna2/dotnet-handbook/tree/main/verify/path), where a test runs each one and checks its output.

**Peek-lock is a lease.** It needs Docker: start the Service Bus emulator from `verify/path` with `ACCEPT_EULA=Y docker compose up -d` (the emulator and SQL Server have EULAs), and stop it with `docker compose down`. The run takes about 10 seconds. Without Docker, read the output below.

`verify/path/PeekLock/Program.cs` · run it from `verify/path` with `dotnet run --project PeekLock`:

```csharp
using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;

// Prove it: peek-lock is a lease, not a hand-over. A handler that outlives the lock gets the same
// message again. Needs the Service Bus emulator: `docker compose up -d` in this folder.
const string Emulator = "Endpoint=sb://localhost;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;";
string queue = "payments-" + Guid.NewGuid().ToString("N")[..8];
var admin = new ServiceBusAdministrationClient(Emulator.Replace("sb://localhost", "sb://localhost:5300"));
await admin.CreateQueueAsync(new CreateQueueOptions(queue) { LockDuration = TimeSpan.FromSeconds(5) });

await using var client = new ServiceBusClient(Emulator);
await client.CreateSender(queue).SendMessageAsync(new ServiceBusMessage("charge order 42") { MessageId = "order-42" });
ServiceBusReceiver receiver = client.CreateReceiver(queue);                // peek-lock is the default mode

ServiceBusReceivedMessage first = await receiver.ReceiveMessageAsync();
Console.WriteLine($"received {first.MessageId}, DeliveryCount={first.DeliveryCount}, locked for 5 s");
await Task.Delay(TimeSpan.FromSeconds(8));                                 // the "charge" takes 8 s
ServiceBusReceivedMessage second = await receiver.ReceiveMessageAsync(TimeSpan.FromSeconds(10));
Console.WriteLine($"received {second.MessageId} again, DeliveryCount={second.DeliveryCount}");

try { await receiver.CompleteMessageAsync(first); }
catch (ServiceBusException e) when (e.Reason == ServiceBusFailureReason.MessageLockLost)
{
    Console.WriteLine("completing the first copy threw MessageLockLost; its charge already happened");
}
await receiver.CompleteMessageAsync(second);
Console.WriteLine($"completed the second copy; message still in the queue: {await receiver.PeekMessageAsync() is not null}");
```

```text
received order-42, DeliveryCount=1, locked for 5 s
received order-42 again, DeliveryCount=2
completing the first copy threw MessageLockLost; its charge already happened
completed the second copy; message still in the queue: False
```

What to notice:

- **The same `MessageId` came back with `DeliveryCount=2`.** The lock expired at 5 seconds while the handler was still "charging", so Service Bus made the message visible again.
- **Completing the first copy fails, but its side effect has already happened.** In production, that is the duplicate charge.
- **Duplicate detection would not have helped.** It drops a second *send* with the same `MessageId`; this is one message delivered twice.
- **Lock renewal removes this trigger, not the others.** `ServiceBusProcessor` renews the lock while your handler runs (`MaxAutoLockRenewalDuration`, 5 minutes by default). Crashes and lost settlements still redeliver, so the handler must be idempotent anyway.
- **The numbers are scaled down** so the run takes seconds: a 5-second lock and an 8-second "charge". Azure's default lock is 1 minute, and 5 minutes is the maximum.

**Check, then act.** Before you run it, read the two handlers and predict how many times each one charges the card.

`verify/path/CheckThenAct/Program.cs` · run it from `verify/path` with `dotnet run --project CheckThenAct`:

```csharp
using System.Collections.Concurrent;

// Prove it: review the two handlers below BEFORE running this. Both "deduplicate" a message
// that the broker delivered twice; the two copies are being processed at the same time.
int charges = 0;

var seen = new ConcurrentDictionary<string, bool>();
async Task CheckThenAct(string messageId)
{
    if (seen.ContainsKey(messageId)) return;        // check …
    await ChargeCardAsync();                        // … the effect …
    seen[messageId] = true;                         // … then remember the id
}

var claimed = new ConcurrentDictionary<string, bool>();
async Task ClaimFirst(string messageId)
{
    if (!claimed.TryAdd(messageId, true)) return;  // one atomic step: only one copy can win
    await ChargeCardAsync();
}

await Task.WhenAll(CheckThenAct("order-42"), CheckThenAct("order-42"));
Console.WriteLine($"check, then act: the card was charged {charges} time(s)");

charges = 0;
await Task.WhenAll(ClaimFirst("order-42"), ClaimFirst("order-42"));
Console.WriteLine($"claim first:     the card was charged {charges} time(s)");

async Task ChargeCardAsync() { await Task.Delay(100); Interlocked.Increment(ref charges); }
```

```text
check, then act: the card was charged 2 time(s)
claim first:     the card was charged 1 time(s)
```

What to notice:

- **A thread-safe dictionary doesn't make the handler safe.** Both copies passed `ContainsKey` before either recorded the ID. Every *call* is thread-safe; the *check-then-act sequence* is not atomic.
- **`TryAdd` makes the check and the claim one atomic step.** Only one copy can win, and the loser never reaches the effect.
- **In a database, the claim is an insert under a unique key, in the same transaction as the effect.** The second copy hits the unique violation and stops; if the effect fails, the rollback releases the claim, so the redelivery can try again. Chapter 3's *Idempotency Keys* shows the full pattern.
- **An effect outside your database can't join that transaction.** For a payment API, also pass the message's key downstream as the provider's idempotency key.

**Then do the chapter exercise.** [Chapter 51](#chapter-51-the-azure-casebook-real-incidents-real-fixes), *Exercises → Find the bug*, sample 2: a payment worker whose batch outlives its locks, verified against the same emulator.

## Three questions

**1.** Your handler charged the card, then `CompleteMessageAsync` threw `MessageLockLost`. What happened, what happens next, and why doesn't Service Bus duplicate detection protect you?

<details>
<summary>Answer</summary>

- **What happened.** The lock, a lease of `LockDuration` (1 minute by default), expired before the settlement. Either the handler ran longer than the lock, or the message had been waiting, already locked, in a received batch or a prefetch buffer: those locks start at receive time, not when your code reaches the message.
- **What happens next.** Service Bus has made the message visible again. A receiver gets it with `DeliveryCount + 1` and charges again, unless the handler is idempotent. After `MaxDeliveryCount` deliveries (10 by default) it is dead-lettered.
- **Why duplicate detection doesn't help.** It discards a newly *sent* message whose `MessageId` it has already seen within its window: a guard against a producer that retries. A redelivery is the same message delivered again, which duplicate detection never sees.
- **The fix, in two layers.** Use `ServiceBusProcessor`, which renews locks while the handler runs, with no oversized batches or prefetch. And make the handler idempotent: claim the message ID in the same transaction as the effect, and pass a payment idempotency key downstream.
</details>

**2.** Events are sent in order, yet the consumer applies `OrderShipped` before `OrderPaid`. How, and how do sessions fix it, at what cost?

<details>
<summary>Answer</summary>

- **How.** A FIFO queue hands messages out in order, but nothing makes them *finish* in order. Competing consumers, or one processor with `MaxConcurrentCalls` above 1, handle messages at the same time, and the faster one finishes first. A message that is abandoned or whose lock expires is processed again after the later messages that other receivers took meanwhile. With prefetch, an abandoned message goes to the back of the local buffer.
- **Sessions.** The sender sets `SessionId` (say, the order ID) on a session-enabled queue or subscription. A receiver accepts one session and holds an exclusive lock on all its messages, current and future, and receives them in order. One receiver per session at a time, many sessions in parallel.
- **The cost.** Throughput per key is one consumer. A message that keeps failing holds up the rest of its session until it is dead-lettered; Part 2 goes deeper into that head-of-line blocking. Sessions are chosen when the entity is created and can't be switched on or off later, and a session-enabled entity accepts only messages that carry a `SessionId`.
- **When strict order isn't needed**, carry a version or sequence number, and make the consumer ignore anything older than the state it already has.
</details>

**3.** A `POST` must save a record, publish `DocumentRequested`, and run a document generation that takes ten minutes. Why is "save, then publish, then generate, then return `200`" wrong, and what does the robust design look like end to end?

<details>
<summary>Answer</summary>

**Three failures:**

1. **A dual write.** The commit and the publish are separate operations. A crash or a failed publish between them leaves a record nobody hears about; publish first, and a rollback leaves an event for a record that doesn't exist.
2. **Ten minutes inside an HTTP request.** Front ends cut it: App Service at 230 seconds. Clients time out and retry, which duplicates the work: the server keeps working on a request whose client has given up. Every attempt holds a connection the whole time.
3. **A `200` that is only a hope.** It promises work that happens only if nothing crashes.

**The robust shape:**

1. The `POST` validates the request, then one database transaction writes the job row (`Pending`) and an outbox row.
2. The response is `202 Accepted`, with `Location: /jobs/{id}` and `Retry-After`.
3. After the commit, a relay publishes the outbox rows, at-least-once. Part 2 covers relays that read the database log (CDC).
4. A `BackgroundService` worker takes the job from the queue, runs it idempotently (claimed on the job ID), and updates the job's status. Transient failures are retried; a job that keeps failing is dead-lettered and marked `Failed`, with a reason.
5. `GET /jobs/{id}` returns `200` with the status while the job runs, and `303 See Other` to the result when it is done.
6. The client sends an `Idempotency-Key` with the `POST`, so a retried request gets the existing job's status URL instead of a second job.
</details>

## Check at work

**Inspect.** For each message consumer in your codebase, find where it deduplicates. Good: an insert under a unique key, or a conditional update, in the same transaction as the effect, plus an idempotency key passed to any external API it calls. Bad: `if (await AlreadyProcessed(id)) return;` followed by the effect and then a "mark processed" call, or no deduplication at all. While you are there, search for `ReceiveMessagesAsync(` with large batches, `PrefetchCount`, handlers that wrap everything in `catch (Exception)`, and endpoints that return `Accepted()` without a `Location`.

**Measure.** For each queue and subscription: the lock duration against the handler's p99 duration, `MaxDeliveryCount`, the dead-letter count (the `DeadletteredMessages` metric) and whether anything alerts on it, and how often your handlers log `DeliveryCount > 1`.
