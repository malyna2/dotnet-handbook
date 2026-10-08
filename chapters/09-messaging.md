# Chapter 9: Messaging & Distributed Systems

_⏱️ Estimated read time: ~36 min ·     5051 words (study pace)_

Somewhere along the road from junior to senior, you stop asking "how do I call this API?" and start asking "what happens when this API is down, slow, or lying to me?" That shift in mindset is the heart of distributed systems. This chapter is about the tools and patterns we use to build systems out of many independent parts that keep working even when some of those parts fail.

Messaging is the connective tissue. Instead of components shouting directly at each other and waiting for an answer, they drop notes in mailboxes and get on with their lives. That single change — from a phone call to a postal system — has profound consequences for how resilient, scalable, and maintainable your system becomes. Let's build up the "why" before we touch a single broker.

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

### Saga: Managing Long-Running Distributed Transactions

You can't use a database transaction across five microservices. So how do you keep a multi-step business process consistent — e.g., "reserve inventory, charge payment, allocate shipping" — when any step might fail?

A **Saga** breaks the process into local transactions, each with a **compensating action** that undoes it. If step 3 fails, you run the compensations for steps 2 and 1 (refund the payment, release the inventory). You don't get atomicity; you get *eventual* consistency through explicit rollback logic.

There are two flavors:

**Choreography** — no central coordinator. Each service listens for events and reacts, emitting its own events. The workflow is emergent.

```
OrderPlaced ─▶ [Inventory] ─InventoryReserved─▶ [Payment] ─PaymentCharged─▶ [Shipping]
                    ▲                                  │
                    └────── on PaymentFailed ──────────┘  (compensate: release stock)
```

- Pros: no single point of failure, services stay decoupled.
- Cons: the overall flow is implicit and hard to follow. "Where is this order stuck?" becomes an archaeology project across many logs.

**Orchestration** — a central **saga orchestrator** holds the state machine and tells each service what to do next via commands.

```
              ┌──────────── Saga Orchestrator (state machine) ───────────┐
              │  state: AwaitingPayment                                  │
              └──────────────────────────────────────────────────────────┘
                 │ ReserveInventory   │ ChargePayment   │ ArrangeShipping
                 ▼                    ▼                  ▼
            [Inventory]           [Payment]          [Shipping]
```

- Pros: the whole workflow lives in one place; easy to reason about, monitor, and change.
- Cons: the orchestrator is a component you must build and keep available.

> **Rule of thumb:** use **choreography** for simple flows with two or three steps and few branches. Reach for **orchestration** as soon as the workflow has real complexity, branching, or timeouts — the centralized visibility pays for itself. MassTransit's `MassTransitStateMachine` (Automatonymous-style) is an excellent orchestration tool.

Here's the shape of a MassTransit state machine saga:

```csharp
public class OrderStateMachine : MassTransitStateMachine<OrderState>
{
    public State AwaitingPayment { get; private set; } = default!;
    public State Completed { get; private set; } = default!;

    public Event<OrderPlaced> OrderPlaced { get; private set; } = default!;
    public Event<PaymentCharged> PaymentCharged { get; private set; } = default!;

    public OrderStateMachine()
    {
        InstanceState(x => x.CurrentState);

        Initially(
            When(OrderPlaced)
                .Then(ctx => ctx.Saga.OrderId = ctx.Message.OrderId)
                // Send a command to the payment service to start step 2.
                .Send(ctx => new ChargePayment { OrderId = ctx.Message.OrderId })
                .TransitionTo(AwaitingPayment));

        During(AwaitingPayment,
            When(PaymentCharged)
                .Then(ctx => Console.WriteLine("Payment done, arranging shipping"))
                .TransitionTo(Completed)
                .Finalize());
    }
}

// The persisted saga state — survives restarts, stored in a DB.
public class OrderState : SagaStateMachineInstance
{
    public Guid CorrelationId { get; set; } // required by MassTransit
    public string CurrentState { get; set; } = default!;
    public Guid OrderId { get; set; }
}
```

The saga's state is *persisted*, so the process survives crashes and restarts. That's the whole point — a long-running transaction that can pause for hours (waiting for a slow payment) without holding any locks.

### Resilience Patterns: Retry, Circuit Breaker, Bulkhead

These come from the world of resilient clients, and Chapter 21 covers the mechanics — the full Polly pipeline via `Microsoft.Extensions.Http.Resilience`, how the strategies layer, and why jitter matters. Here, the short version and the messaging angle:

- **Retry with exponential backoff and jitter.** When a call fails transiently, retry — but back off exponentially (1s, 2s, 4s, 8s) so the struggling service gets room to recover, and add jitter so a thousand clients that failed at the same instant don't retry in perfect unison — the "thundering herd."
- **Circuit breaker.** When a downstream service is clearly down, retrying is pointless and harmful. A breaker watches the failure rate, "trips" once it crosses a threshold, fails fast for a cooldown period, then lets a trial request through and resumes if it succeeds. This protects both you (fail fast instead of hanging) and the struggling service (you stop piling on load).
- **Bulkhead.** Named after a ship's watertight compartments: isolate resources per dependency so one misbehaving service can't consume *all* your threads or connections and sink the whole application.

> **Pitfall:** retrying a *non-idempotent* operation can double-charge a customer. Only retry operations you know are safe to repeat — which loops us right back to idempotency.

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

## Consistency in a Distributed World

Replicating data across a network forces a trade-off between consistency and availability — the territory of the CAP theorem and its PACELC refinement, which Chapter 21 covers in full. Here the point is the consequence you accept the moment you adopt messaging: **eventual consistency**. If you stop writing, all parts of the system *eventually* converge on the same state; in the meantime, reads might be stale. Your account balance updated on your phone might take a moment to appear on the website.

This is exactly the model that messaging gives you. When the checkout publishes `OrderPlaced` and the analytics service processes it 200ms later, the system is *temporarily inconsistent* — the order exists but analytics doesn't know yet — and then converges. Accepting this is the price of decoupling, and for most business domains it's a fine price. The senior skill is identifying the few places where it *isn't* acceptable and applying stronger consistency there.

## Distributed Caching and Session State

The last piece of the distributed puzzle: shared state across many stateless instances.

The moment you run more than one instance of your app behind a load balancer, **in-memory state becomes a liability**. If instance A stores a user's session in its own RAM and the next request hits instance B, the session is gone. This is why we externalize shared state into a **distributed cache** — most commonly **Redis**.

```
        ┌── App Instance 1 ──┐
Client ─┤   App Instance 2   ├──▶ [ Redis ] ← single source of shared truth
        └── App Instance 3 ──┘       (cache + session store)
```

.NET gives you `IDistributedCache` as the abstraction:

```csharp
builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = "localhost:6379";
    options.InstanceName = "shop:";
});

public class ProductCatalog
{
    private readonly IDistributedCache _cache;
    public ProductCatalog(IDistributedCache cache) => _cache = cache;

    public async Task<Product?> GetProductAsync(int id)
    {
        var key = $"product:{id}";
        var cached = await _cache.GetStringAsync(key);
        if (cached is not null)
            return JsonSerializer.Deserialize<Product>(cached); // cache hit

        var product = await _repository.LoadAsync(id);           // cache miss
        await _cache.SetStringAsync(key, JsonSerializer.Serialize(product),
            new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10)
            });
        return product;
    }
}
```

This is the **cache-aside** pattern: check the cache, on a miss load from the source and populate the cache. Key considerations:

- **Expiration / TTL.** Cached data goes stale. Set a TTL appropriate to how fresh the data must be.
- **Invalidation.** "There are only two hard things in computer science: cache invalidation and naming things." When the underlying product changes, you must evict or update the cache entry — often by publishing a `ProductUpdated` event that a consumer uses to invalidate the key. Messaging and caching working together.
- **Stampede protection.** When a hot key expires, a thousand requests may all miss simultaneously and hammer the database. Guard hot keys with a lock or "early recompute" so only one caller rebuilds the value.

For **session state**, the same idea: configure ASP.NET Core sessions to use the distributed cache so any instance can serve any user. This keeps your app tier **stateless** — the property that makes horizontal scaling trivial. Stateless app servers plus externalized state plus asynchronous messaging is, in a sentence, the architecture of nearly every scalable modern system.

> **Capstone tie-in:** This chapter is exercised by ShopCore Step 7 (Split Into Microservices) — you'd carve out Catalog, Ordering, and Payments services communicating over RabbitMQ with MassTransit, with the Outbox pattern and idempotent consumers. See Chapter 32.

## Wrapping Up

Zoom out and a coherent philosophy emerges. Distributed systems fail in parts, so we design for *partial failure*: decouple with messaging so a downstream outage doesn't cascade; accept *at-least-once* delivery and make consumers *idempotent* rather than chasing the mirage of exactly-once; use the *outbox* to bridge database and broker atomically; coordinate multi-step work with *sagas* and compensations instead of impossible distributed transactions; protect ourselves with *retries, circuit breakers, and bulkheads*; and embrace *eventual consistency* as the natural, affordable state of a decoupled system — reserving stronger guarantees for the rare places that truly need them.

None of these patterns is exotic once you've internalized the core insight: **the network is unreliable, and every design decision is a negotiation with that fact.** Master that negotiation, and you're thinking like a senior engineer.
