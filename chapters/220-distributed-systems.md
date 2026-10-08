# Chapter 20: Distributed Systems

@@TODO: write this chapter's introduction (what it makes the reader able to do, how its sections connect), then remove every @@ line.@@

@@SRC: introduction of old Chapter 21: Distributed Systems Theory & Reliability Engineering@@

A single-process program lives in a comfortable universe. Memory reads are instantaneous, function calls always return, and if something crashes, the whole thing crashes together — you never have to reason about *half* your program being alive while the other half is dead. The moment you split that program across two machines connected by a network, you leave that comfortable universe forever. Messages get lost. Clocks disagree. One node thinks another is dead when it is merely slow. And crucially, **you can never tell the difference between a slow node and a dead one** — that single fact is the source of most of the pain in this chapter.

This chapter is the theory that separates a senior engineer from a mid-level one. A mid-level developer can wire up microservices with HTTP and a message bus. A senior developer knows *why* those services will betray them under load, and designs for it. We'll build up from the foundational lies we tell ourselves, through the hard limits imposed by physics and mathematics, and land on the concrete engineering practices — and .NET code — that keep systems standing when parts of them fall over.

@@SRC: old Chapter 3: ASP.NET Core & Web APIs@@
## REST, Status Codes, Versioning & OpenAPI — in depth

### Idempotency Keys: Making POST Retry-Safe

GET, PUT and DELETE are idempotent *by the definition of the verb*: `PUT /orders/42` with the same body leaves the same state whether it runs once or five times, and a second `DELETE /orders/42` finds nothing left to delete. POST is the exception — it means "process this as a new subordinate resource," and the whole point is that each call creates something.

That asymmetry stops being a semantic curiosity the moment a request times out. A timeout tells the client nothing useful: the request may never have arrived, may have executed with the response lost on the way back, or may still be running. The only thing the client knows is that it doesn't know. So it retries — the Polly pipeline from earlier in this chapter retries, the mobile app's network layer retries, the user hits the button again — and if that POST charged a card, the customer is charged twice. "The client retried after a timeout" is not an edge case; it is the *normal* behaviour of every HTTP client on a lossy network, which makes this a correctness problem rather than a nicety.

The fix is to let the client supply the identity of the **operation**, not just of the request:

```
POST /payments
Idempotency-Key: 5f3b8a1e-9c04-4f4a-8a0e-2b7c1d33e9a1
Content-Type: application/json

{ "orderId": 42, "amount": 19.99 }
```

The key is generated **once, before the first attempt**, and reused for every retry of that same logical operation. A key regenerated per HTTP attempt is worse than useless — it makes retries look like distinct operations, which is precisely what you were trying to prevent. Server-side you keep a record per key:

```csharp
public class IdempotencyRecord
{
    public string Endpoint { get; set; } = default!;    // same key on /refunds is a different op
    public string Key { get; set; } = default!;         // client-supplied
    public string RequestHash { get; set; } = default!; // SHA-256 of the canonical body
    public int StatusCode { get; set; }                 // 0 while in flight
    public DateTimeOffset LockedUntil { get; set; }     // the in-flight lease
    public string? ResponseBody { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
```

Three outcomes, and the third is the one people forget to implement:

| Repeat request with the same key | Response |
|---|---|
| Same body hash, first attempt completed | Replay the stored status and body verbatim; add `Idempotency-Replayed: true` so the caller can tell |
| Same body hash, first attempt still in flight | `409 Conflict` with `Retry-After` — "in progress, ask again shortly" |
| **Different** body hash | `422 Unprocessable Content` (or `409`) — the key was reused for a different operation, which is a client bug and must be surfaced loudly |

Storing the request hash is what makes that last row possible. Without it, a client that recycles keys — a hard-coded constant in a test harness, a key derived from a non-unique order number — silently receives someone else's response, and you will spend a long afternoon working out why.

**The concurrency detail that makes it actually work.** The naïve implementation reads the table, sees no row, does the work, then writes the row. That is the same check-then-act race as the uniqueness validator earlier in this chapter, except here the prize is a duplicate charge: two retries arriving 20 ms apart both read "no row" and both charge.

What arbitrates is a **unique index on `(Endpoint, Key)`** combined with the ordering: *claim the key before the effect*. Where the claim is committed depends on where the effect lives. When the effect is a row in the same database (an order, a ledger entry), the claim and the effect share one transaction, as in [Chapter 9's idempotent consumer](#idempotent-consumers): the second request's insert waits on the first one's uncommitted key, fails once it commits, and replays the result. A card charge is a call to another system, and no database transaction can include it, so the claim is committed on its own, before the call:

```csharp
var record = new IdempotencyRecord
{
    Endpoint = "POST /payments", Key = key, RequestHash = hash, StatusCode = 0,
    LockedUntil = DateTimeOffset.UtcNow.AddSeconds(30), CreatedAt = DateTimeOffset.UtcNow
};
db.IdempotencyRecords.Add(record);
try
{
    await db.SaveChangesAsync(ct);        // Its own short transaction: the unique index arbitrates HERE.
}
catch (DbUpdateException ex) when (IsUniqueViolation(ex))  // 23505 on Npgsql, 2601/2627 on SQL Server
{
    db.ChangeTracker.Clear();
    return await ReplayOrConflictAsync(key, hash, ct);      // The loser never reaches the charge.
}

Payment payment;
try
{
    payment = await _payments.ChargeAsync(request, idempotencyKey: key, ct); // The side effect.
}
catch
{
    db.IdempotencyRecords.Remove(record); // No deterministic outcome: release the key.
    await db.SaveChangesAsync(CancellationToken.None);
    throw;
}

record.StatusCode = StatusCodes.Status201Created;
record.ResponseBody = JsonSerializer.Serialize(payment);
await db.SaveChangesAsync(ct);
```

The ordering is the entire trick, so walk the second concurrent request through it. It attempts the same insert. The claim was committed before the charge started, so the unique index rejects it at once, and it learns, atomically and with no read-then-write window anywhere, that it lost the race. It reads the existing row. If that row carries a status code, the first attempt finished and the loser replays it. If the status is still `0` and the lease is live, the first attempt is in flight and the loser answers `409` with `Retry-After: 1`. Either way it never reaches `ChargeAsync`.

> **Pay attention.** **Why the claim is committed on its own.** Wrap the claim, the charge and the result in one transaction and no other request ever sees status `0`. The second insert blocks on the first one's uncommitted key, holding its connection for as long as the charge takes, then replays or, if the first rolled back, charges. The `409` row can never fire, a crash leaves no row to reclaim, and a commit that fails after a successful charge erases the only record that the charge happened. Commit the claim first, and forward the key to the payment provider: the one window left, a crash after the charge and before the result is stored, is then closed downstream.

Now invert the order and do the work first: both requests charge the card, and *then* one of them discovers it lost. The damage is already done and you are writing a refund. The claim must be committed before the effect starts, or the pattern buys you nothing.

```
request A ──┬─ INSERT (POST /payments, key) + COMMIT ──► charge (key forwarded) ──► UPDATE: 201
            │
request B ──┴─ INSERT (POST /payments, key) ──► unique violation
                                                    │
                                status 0, lease live? ──┴──► 409 + Retry-After
                                       status set? ────► replay stored response
```

Two loose ends remain.

**A first attempt that never finishes.** If the process dies between the claim and the update, the row sits at status `0`, and without a lease every retry would get a `409` forever. `LockedUntil` is that lease: once it has passed, a retry reclaims the row with a conditional update (`SET LockedUntil = @newLease WHERE … AND StatusCode = 0 AND LockedUntil < @now`, so only one reclaimer wins) and runs the charge again. Whether reclaiming is safe depends on whether re-running the side effect is safe, which is why the strongest version of this pattern forwards the same key downstream: most payment gateways accept an idempotency key of their own, so you hand yours through and let them deduplicate the charge you may or may not have made.

**Retention.** Idempotency records are a cache, not an audit log. Keep them long enough to cover any plausible retry window — Stripe uses 24 hours, and 24–72 hours suits most systems — then delete them from a background job with a batched `ExecuteDeleteAsync`, never a cascade on the request path. This table takes a write on the hot path of every mutating request, so unbounded growth is a genuine operational problem rather than a tidiness concern.

> **Best practice.** Scope the key by caller as well as endpoint — `(TenantId, Endpoint, Key)`. Keys are client-generated, and one client's copy-pasted GUID must never be able to replay another client's response. Decide explicitly, too, whether a replay re-runs authorization: it should, because a stored `201` must not be handed to a caller who has since lost the entitlement.

> **Gotcha.** Idempotency is not the same as "it worked." Replaying a stored `500` on retry is almost always wrong — a genuine server error is exactly the case where the client *should* get a fresh attempt. Record only deterministic outcomes: successes and client errors. Leave 5xx unstored (release the key) so the retry re-executes.

This is the HTTP-facing sibling of a pattern that shows up twice more in this book — idempotent message consumers in [Chapter 9: Messaging & Distributed Systems](#chapter-9-messaging-distributed-systems), which dedupe on a message ID with the same unique-index backstop, and the general treatment in [Chapter 6: Architecture & Application Design](#chapter-6-architecture-application-design). One mechanism (record the operation's identity atomically with its effect), three transports.

@@SRC: old Chapter 6: Architecture & Application Design@@
## Distributed Data Patterns

When you cross service or aggregate boundaries, the comforting single-database ACID transaction disappears. These patterns are how senior engineers keep distributed systems correct.

### Eventual Consistency

In a distributed system you usually cannot have immediate consistency across services. Instead you accept **eventual consistency**: after a change, the system will *become* consistent given time, but there's a window where different parts disagree. Your UI and your business rules must be designed to tolerate that window ("Your order is being processed"). Fighting eventual consistency with distributed locks and two-phase commit usually trades availability and performance for a consistency you rarely truly need.

### The Saga Pattern

A business transaction that spans multiple services — place order, reserve inventory, charge payment, arrange shipping — can't be one ACID transaction. A **Saga** models it as a sequence of local transactions, each with a **compensating action** to undo it if a later step fails. There's no rollback; there's "do the opposite."

**Orchestration** — a central coordinator (the orchestrator) tells each service what to do and reacts to results.

```
        +------------------ Order Saga Orchestrator ------------------+
        |                                                             |
        v                    v                    v                   v
  Reserve Inventory --> Charge Payment --> Arrange Shipping --> Confirm Order
        |                    |
   (on failure)          (on failure -> compensate: release inventory)
```

**Choreography** — no central brain; each service listens for events and reacts, emitting its own events.

```
 OrderPlaced --> [Inventory] --> InventoryReserved --> [Payment]
                                                          |
                                                    PaymentCharged --> [Shipping]
```

> **Trade-off:** Orchestration centralizes the workflow — easy to see and change the whole process in one place, but the orchestrator becomes a hub of coupling. Choreography is loosely coupled and scales organizationally, but the end-to-end workflow is *emergent* — no single place tells you what happens, which makes debugging and reasoning harder. Use orchestration for complex, evolving workflows; choreography for simple, stable reactions.

### The Outbox Pattern

Here's a subtle bug that bites everyone eventually. Your handler saves an order to the database *and* publishes an `OrderPlaced` message to a broker. These are two separate systems. If the DB commit succeeds but the broker publish fails (or vice versa), you have inconsistency — an order with no event, or an event for an order that rolled back. You cannot atomically write to a database and a message broker.

The **Outbox Pattern** solves this. In the *same database transaction* that saves the order, you also insert the outbound message into an `Outbox` table. Because it's one transaction, either both happen or neither does. A separate background process then reads unpublished rows from the Outbox and pushes them to the broker, marking them sent.

```
  BEGIN TX
    INSERT INTO Orders ...
    INSERT INTO Outbox (event = OrderPlaced, published = false)
  COMMIT                                   <- atomic: both or neither
        |
   Background relay polls Outbox --> publish to broker --> mark published
```

> **Best practice:** the Outbox guarantees *at-least-once* delivery — the relay may occasionally publish a message twice (e.g., it crashed after publishing but before marking it sent). That is not a flaw to eliminate but a reality to design for, which leads directly to idempotency.

### Idempotency

An operation is **idempotent** if performing it multiple times has the same effect as performing it once. In distributed systems, messages get redelivered, clients retry on timeout, and relays double-publish. If "charge payment" runs twice, you've double-charged a customer. The defense is to make consumers idempotent — typically by recording a unique message/operation ID and ignoring duplicates.

The obvious version is wrong: "if the ID was processed, return; charge; record the ID" is check-then-act. Two copies delivered at the same time both pass the check before either records the ID, and both charge. The record must *be* the check — insert the ID under a unique key first, in the same transaction as the effect:

```csharp
public async Task Handle(ChargePayment cmd, CancellationToken ct)
{
    await using var tx = await _db.Database.BeginTransactionAsync(ct);
    _db.ProcessedMessages.Add(new ProcessedMessage(cmd.MessageId));      // unique key on MessageId
    try { await _db.SaveChangesAsync(ct); }                               // the claim: a duplicate stops here
    catch (DbUpdateException e) when (IsUniqueViolation(e)) { return; }   // already handled -> no-op

    _db.Payments.Add(Payment.Requested(cmd.OrderId, cmd.Amount));        // the effect, in the same transaction
    await _db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
}
```

A second copy's insert waits on the first's uncommitted key, then fails if the first commits; if the effect fails, the rollback releases the claim and the redelivery retries. An effect outside the database (the card network itself) can't join the transaction, so pass the same ID downstream as the provider's idempotency key. [Chapter 9: Idempotent Consumers](#idempotent-consumers) walks the race; [Chapter 3: Idempotency Keys](#idempotency-keys-making-post-retry-safe) applies the same mechanism to HTTP.

> **Idempotency is the safety net that makes at-least-once messaging, retries, and the Outbox pattern viable.** Design every message handler and every mutating API endpoint (via an idempotency key) to tolerate being called more than once. This is non-negotiable in a distributed system.

@@SRC: old Chapter 9: Messaging & Distributed Systems@@
## Distributed Patterns Every Senior Should Know — in depth

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

@@SRC: old Chapter 9: Messaging & Distributed Systems@@
## Consistency in a Distributed World

Replicating data across a network forces a trade-off between consistency and availability — the territory of the CAP theorem and its PACELC refinement, which Chapter 21 covers in full. Here the point is the consequence you accept the moment you adopt messaging: **eventual consistency**. If you stop writing, all parts of the system *eventually* converge on the same state; in the meantime, reads might be stale. Your account balance updated on your phone might take a moment to appear on the website.

This is exactly the model that messaging gives you. When the checkout publishes `OrderPlaced` and the analytics service processes it 200ms later, the system is *temporarily inconsistent* — the order exists but analytics doesn't know yet — and then converges. Accepting this is the price of decoupling, and for most business domains it's a fine price. The senior skill is identifying the few places where it *isn't* acceptable and applying stronger consistency there.

@@SRC: old Chapter 20: Networking & Web Fundamentals@@
## The Fallacies of Distributed Computing

We close with the mental model that should underpin every networked design decision: the **Fallacies of Distributed Computing**, the catalogue of false assumptions — the network is reliable, latency is zero, bandwidth is infinite, and five more — that Sun Microsystems engineers compiled in the 1990s. Chapter 21 works through all eight; for now, internalize the three this chapter has been circling all along. The network is *not* reliable — a call can fail *after* the server processed it but *before* you got the response, which is why idempotency, retries, and timeouts are load-bearing, not optional. Latency is *not* zero — 50 sequential calls to render one page (the **N+1 network problem**) is why some apps feel slow no matter how fast the code is; batch, parallelize, and cache. And bandwidth is neither infinite nor free — cloud egress bills (often the biggest surprise line item) make that painfully concrete.

> **The senior mindset in one sentence:** Treat every network call as an *unreliable, slow, expensive, insecure* operation that will eventually fail — then be pleasantly surprised when it works.

@@SRC: old Chapter 21: Distributed Systems Theory & Reliability Engineering@@
## The Eight Fallacies of Distributed Computing

In the 1990s, engineers at Sun Microsystems (L. Peter Deutsch and colleagues) catalogued the false assumptions that new distributed-systems programmers reliably make. They are worth memorizing because *every* production outage you will ever debug is, at root, one of these assumptions leaking into code:

1. **The network is reliable.** Packets drop. Connections reset. Your `await httpClient.GetAsync(...)` will throw, and not rarely.
2. **Latency is zero.** A call across a data center is ~0.5ms; across the planet it's ~150ms. A chatty API that makes 50 sequential remote calls has silently signed up for seconds of wall time.
3. **Bandwidth is infinite.** That innocent `SELECT *` returning a 4MB payload per request will saturate a link at scale.
4. **The network is secure.** Assume every hop is hostile; encrypt and authenticate.
5. **Topology doesn't change.** Nodes are added, removed, and rescheduled constantly in a Kubernetes world. IPs you cached are stale.
6. **There is one administrator.** In reality, the network team, the cloud provider, and three other squads all touch the path between your services.
7. **Transport cost is zero.** Serialization, TLS handshakes, and marshaling all burn CPU and time.
8. **The network is homogeneous.** Different protocols, MTUs, and hardware behave differently.

> **Best practice:** Treat every remote call as a *fallible operation that can hang forever*, not as a method call that happens to be slow. This single mindset shift — "the call might never return" — forces you toward timeouts, retries, and circuit breakers instead of hopeful synchronous code.

@@SRC: old Chapter 21: Distributed Systems Theory & Reliability Engineering@@
## CAP and PACELC: The Physics of Distributed State

Suppose you replicate data across two nodes for durability. Now a network partition splits them — they can't talk. A write arrives at node A. You have exactly two choices:

- **Accept the write** (stay *Available*), knowing node B now serves stale data — you've sacrificed **C**onsistency.
- **Reject the write** (stay *Consistent*), refusing to serve until the partition heals — you've sacrificed **A**vailability.

This is the **CAP theorem** (Brewer, formalized by Gilbert and Lynch): during a **P**artition, you must choose **C** or **A**. You cannot have both. Note the common misreading — CAP is *not* "pick two of three." Partitions are not optional; the network *will* partition. So the real choice is only ever C-vs-A, and *only during a partition*.

The subtler and more practical framing is **PACELC** (Abadi): **if** **P**artition, choose **A** or **C**; **E**lse (normal operation), choose between **L**atency and **C**onsistency. This matters because partitions are rare, but the latency-vs-consistency tradeoff is paid on *every single request*. A system that synchronously replicates a write to a quorum before acknowledging (strong consistency) pays latency on every write. One that acknowledges locally and replicates in the background (eventual consistency) is fast but can serve stale reads.

| System | Partition behavior | Normal behavior |
|---|---|---|
| DynamoDB (default) | PA — stay available | EL — low latency, eventual |
| A relational DB with sync replication | PC — refuse writes | EC — consistent, higher latency |

There is no universally correct answer. A shopping cart wants availability (PA/EL) — a stale cart is fine. A bank ledger wants consistency (PC/EC).

@@SRC: old Chapter 21: Distributed Systems Theory & Reliability Engineering@@
## Consistency Models: What "The Data Is Correct" Even Means

"Consistency" in CAP is a specific, strong guarantee (linearizability). But there is a whole spectrum, and choosing the right point on it is a core senior-level skill.

- **Strong consistency (linearizability):** Every read sees the most recent write, as if there were a single copy of the data. Intuitive, but expensive — it requires coordination on every operation.
- **Eventual consistency:** If writes stop, all replicas *eventually* converge. Cheap and highly available, but a read right after a write may return the old value. Amazon's shopping cart and DNS are classic examples.
- **Causal consistency:** Operations that are *causally related* are seen in order by everyone, but unrelated operations may be seen in different orders. If Alice posts a comment and Bob replies, no one sees Bob's reply before Alice's comment — but two unrelated comments might appear in different orders on different screens. This is often the sweet spot: strong enough to avoid nonsense, weak enough to stay fast.
- **Read-your-writes consistency:** A session guarantee — *you* always see your own writes, even if others don't yet. This is why, after you edit your profile, *you* see the change immediately even though a friend might see the old version for a few seconds. Often implemented by sticky-routing a user's reads to the replica that took their write.

> **Pitfall:** Developers assume strong consistency by default because that's how a local database feels. In a replicated, cached, or CQRS system, the default is usually *eventual*. Design your UI and business logic to tolerate reading slightly stale data — or explicitly pay for stronger guarantees where correctness demands it (e.g., inventory decrements, payments).

@@SRC: old Chapter 21: Distributed Systems Theory & Reliability Engineering@@
## Consensus: Getting Nodes to Agree

Many of the guarantees above require multiple nodes to **agree** on a single value or a single ordering of events — who is the leader, what is the next entry in the log, did this transaction commit? This is the **consensus** problem, and it is genuinely hard because of that opening truth: you can't distinguish a crashed node from a slow one.

Why do we even need it? Imagine three replicas of a database and no coordination. Two clients write different values "simultaneously" to different replicas. Which one wins? Without an agreement protocol, the replicas diverge permanently. Consensus gives us a way for a majority to commit to *one* answer that survives node failures.

The two canonical algorithms are **Paxos** (Lamport) and **Raft** (Ongaro and Ousterhout). Paxos is famously hard to understand; Raft was explicitly designed to be teachable, and it's what backs etcd (and therefore much of Kubernetes) and Consul. The intuition:

**Leader election.** Nodes are *followers* by default. Each runs a randomized election timer. If a follower hears nothing from a leader before its timer fires, it becomes a *candidate*, increments a **term** number (a logical epoch), and asks everyone to vote for it. If it collects votes from a **majority** (a quorum), it becomes leader. The randomized timeouts make it unlikely two candidates tie repeatedly. The majority requirement is the magic: because any two majorities of a 5-node cluster must overlap in at least one node, two different leaders can never both be elected in the same term.

**Replicated log.** All writes go to the leader. The leader appends the write to its log and sends it to followers. Once a *majority* have persisted it, the leader marks it **committed** and tells followers. Because commits require a majority, the system tolerates the loss of a minority — a 5-node cluster survives 2 failures. This is why consensus clusters are almost always odd-sized (3, 5, 7): you're buying fault tolerance of `floor(N/2)`, and an even number just adds a node without adding tolerance.

> **Best practice:** Do not implement consensus yourself. Ever. Use etcd, ZooKeeper, Consul, or a database that embeds Raft/Paxos. The edge cases (split votes, log divergence, leader lease expiry) have consumed careers. Your job is to *understand* it so you use these tools correctly — for example, knowing that a quorum write is slower and that a cluster loses availability if it can't form a majority.

@@SRC: old Chapter 21: Distributed Systems Theory & Reliability Engineering@@
## Distributed Time: Why You Can't Trust the Clock

Here is a trap that snares even experienced engineers: using wall-clock timestamps to order events across machines. Every server's clock drifts. NTP corrects it, but corrections can jump the clock *backwards*, and two servers can easily disagree by tens or hundreds of milliseconds — sometimes seconds. If you decide "the write with the later timestamp wins," you can silently discard a newer write because the machine that made it had a slightly slow clock.

> **Pitfall:** "Last write wins" using `DateTime.UtcNow` from different servers is a data-corruption bug waiting to happen. Physical clocks tell you *roughly when*, never reliably *what happened before what*.

The theory's answer is to track *causality* directly rather than time:

- **Lamport clocks** are a simple integer counter per node. On every local event, increment. On every message send, attach your counter; on receive, set your counter to `max(local, received) + 1`. This guarantees: if event A *caused* B, then `clock(A) < clock(B)`. The catch — the converse isn't true. A smaller Lamport value doesn't prove causality, so it can't detect concurrent (conflicting) events.
- **Vector clocks** fix that. Each node keeps a vector of counters, one per node. This captures the full "happened-before" relationship and can *detect concurrency*: if neither vector dominates the other, the two events were concurrent and you have a genuine conflict to resolve (merge, or ask the user). This is how Dynamo-style systems detect sibling versions.

Cloud providers also offer tightly-synchronized clocks (Google's **TrueTime**, AWS Time Sync) that expose an *uncertainty interval* — "the real time is somewhere in this ±ε window" — and wait out the uncertainty to safely order events. But unless you're building a Spanner, prefer logical clocks or a single source of truth (like a database sequence) for ordering.

@@SRC: old Chapter 21: Distributed Systems Theory & Reliability Engineering@@
## Distributed Locks Are Dangerous

You'll eventually want to ensure "only one worker processes this job at a time" across machines, and reach for a distributed lock (often in Redis). Be careful — distributed locks are far more treacherous than in-process locks.

The core problem: a lock has a lease (a timeout), because if the holder crashes you can't leave the lock held forever. But now consider: worker A acquires the lock, then experiences a long GC pause or gets descheduled for 30 seconds. Its lease expires. Worker B acquires the lock and starts working. Then A wakes up — *it still believes it holds the lock* — and writes to the shared resource. Now two workers act simultaneously. The lock protected nothing.

The defense is a **fencing token**: the lock service hands out a monotonically increasing number with each grant. Every write to the protected resource includes its token, and the resource *rejects any token smaller than the highest it has seen*. When stale worker A shows up with token 33 after B already wrote with token 34, the resource refuses A's write. The resource itself enforces mutual exclusion — the lock is only an optimization.

```csharp
// The resource, not the lock, is the source of truth.
if (incomingFenceToken <= lastSeenToken)
    throw new StaleTokenException(); // reject the zombie writer
lastSeenToken = incomingFenceToken;
ApplyWrite(payload);
```

This is the heart of the **Redlock debate**: Martin Kleppmann argued Redlock (a multi-Redis distributed-lock algorithm) is unsafe for correctness because it relies on bounded clock drift and process pauses that don't hold in practice; Redis's Antirez defended it for the efficiency use case. The senior takeaway isn't picking a side — it's understanding that **a distributed lock without fencing cannot guarantee mutual exclusion**, so use locks for *efficiency* (avoid redundant work) and fencing/idempotency for *correctness*.

@@SRC: old Chapter 21: Distributed Systems Theory & Reliability Engineering@@
## Idempotency: The Antidote to "Did That Actually Happen?"

Because the network is unreliable, you constantly face the ambiguous failure: you sent a "charge the card" request, and got a timeout. Did it succeed? You don't know. If you retry and it *did* succeed, you double-charge. If you don't retry and it *didn't*, you drop the payment.

The escape hatch is **idempotency** — designing an operation so that performing it multiple times has the same effect as performing it once. Reads are naturally idempotent; so is `SET balance = 100`. But `balance = balance + 50` is not.

For operations that aren't naturally idempotent, use an **idempotency key**: the client generates a unique key (a GUID) for the logical operation and sends it with every retry. The server records processed keys and, on seeing a duplicate, returns the *original stored result* instead of re-executing.

```csharp
public async Task<PaymentResult> Charge(string idempotencyKey, decimal amount, CancellationToken ct)
{
    await using var tx = await _db.Database.BeginTransactionAsync(ct);
    var record = new IdempotencyRecord { Key = idempotencyKey };            // unique index on Key
    _db.IdempotencyRecords.Add(record);
    try { await _db.SaveChangesAsync(ct); }                                 // the claim, BEFORE the effect
    catch (DbUpdateException e) when (IsUniqueViolation(e))
    {
        await tx.RollbackAsync(ct);
        return await ReplayStoredResultAsync(idempotencyKey, ct);          // safe replay, no second charge
    }

    record.Result = await _gateway.Charge(amount, idempotencyKey, ct);     // forward the key: the gateway dedupes too
    await _db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return record.Result;
}
```

The order is the whole trick. The tempting version — look the key up, charge, then save the result — is check-then-act: two retries 20 ms apart both find nothing and both charge. Here the unique index, not a read, decides the winner: a concurrent retry's insert waits on the first attempt's uncommitted key and fails once it commits, so it replays the stored result instead of charging. [Chapter 3: Idempotency Keys](#idempotency-keys-making-post-retry-safe) has the full HTTP version, with request hashing, scoping and retention.

Stripe's API famously works this way. This ties directly to the **delivery guarantees** from Chapter 9: networks and message brokers give you *at-least-once* delivery in practice (exactly-once is largely a myth end-to-end). At-least-once means *duplicates will happen*. Idempotent consumers turn the achievable "at-least-once delivery" into the effective "exactly-once *processing*" you actually want.

> **Best practice:** Make every message consumer and every mutating API endpoint idempotent. It is the single most impactful reliability pattern in a message-driven system, because it lets you retry aggressively without fear.

@@SRC: old Chapter 21: Distributed Systems Theory & Reliability Engineering@@
## Detecting Failure and Retrying Without Making It Worse

Since you can't distinguish slow from dead, failure detection is heuristic: you set a **timeout** and declare a node dead if it doesn't respond. Too short, and you kill healthy-but-busy nodes; too long, and you hang. Combine timeouts with **heartbeats** (periodic "I'm alive" pings) to track liveness.

When a call fails, you retry — but naive retries are dangerous. Consider **retry storms** and the **thundering herd**: a downstream service hiccups, and thousands of clients all retry *at the same instant*, and all retry again in lockstep, hammering the recovering service into the ground. The retries cause the very overload they're reacting to.

The fixes:

- **Exponential backoff:** wait 1s, then 2s, 4s, 8s… giving the downstream room to recover.
- **Jitter:** add randomness to each delay so clients *desynchronize* instead of retrying in a synchronized wave. AWS's well-known analysis showed full jitter dramatically reduces contention.
- **Retry budgets / caps:** limit total retries and only retry *idempotent* operations. Retrying a non-idempotent charge is how you double-bill customers.

```csharp
// delay = random(0, min(cap, base * 2^attempt))  -- "full jitter"
TimeSpan Backoff(int attempt) =>
    TimeSpan.FromMilliseconds(_rng.Next(0,
        (int)Math.Min(30_000, 100 * Math.Pow(2, attempt))));
```

@@SRC: old Chapter 21: Distributed Systems Theory & Reliability Engineering@@
## From Theory to Practice: Reliability & SRE

Everything above tells you *why* things fail. **Site Reliability Engineering** (codified by Google) tells you how to *run* systems that fail gracefully. The shift in thinking is from "prevent all failure" (impossible) to "engineer for failure and control its impact."

### SLIs, SLOs, SLAs, and Error Budgets

- **SLI (Indicator):** a measured metric — e.g., "proportion of requests served under 300ms," or "successful-request rate."
- **SLO (Objective):** your internal target for that SLI — "99.9% of requests succeed over 30 days."
- **SLA (Agreement):** a *contractual* promise to customers, with penalties. Always looser than your SLO, so you have margin before you owe refunds.
- **Error budget:** the inverse of the SLO. A 99.9% SLO permits 0.1% failure — about **43 minutes of downtime per month**. That budget is a *currency you get to spend*.

The error budget is the brilliant political innovation of SRE: it turns "reliability vs. velocity" from an argument into arithmetic. If you're under budget, *ship features fast* — you have reliability to spare. If you've blown the budget, *freeze features and fix reliability*. It aligns dev and ops around one number instead of pitting them against each other.

> **Best practice:** Don't chase 100% reliability. It's infinitely expensive and users can't tell 99.99% from 100% because their own ISP and Wi-Fi are less reliable than that. Pick an SLO that matches user expectations and *deliberately spend the remaining budget* on shipping.

### Patterns for Graceful Failure

- **Graceful degradation:** when a dependency is down, serve a reduced experience rather than an error page. Recommendations service down? Show a generic bestsellers list. The core purchase flow still works.
- **Load shedding:** when overloaded, *deliberately reject* some requests fast (HTTP 429) to protect the rest. A restaurant that seats everyone and serves no one is worse than one that turns some diners away and serves the rest well.
- **Backpressure:** signal upstream to *slow down* rather than silently buffering until you run out of memory. Bounded queues (`System.Threading.Channels` with a bounded capacity) are the idiomatic .NET mechanism — a full channel blocks or drops producers instead of exploding the heap.
- **Bulkheads:** partition resources so one failure can't sink the ship (the term comes from ship compartments). Give each downstream dependency its *own* connection/thread pool. If the slow "reporting" service exhausts its 10-connection pool, the "checkout" service's separate pool is untouched. Without bulkheads, one slow dependency consumes *all* your threads and takes down everything — the classic cascading failure.
- **Circuit breakers:** wrap a failing dependency so that after N consecutive failures the breaker "opens" and fails fast for a cooldown, instead of every request waiting for a timeout. After the cooldown it goes "half-open," letting one trial request through; success closes it, failure re-opens it. This both protects *you* (no thread pile-ups) and *the dependency* (you stop hammering it while it recovers).

### A Concrete .NET Resilience Pipeline with Polly

In .NET, **Polly** (now integrated with `Microsoft.Extensions.Http.Resilience`) composes these patterns declaratively. Order matters — think of it as an onion the request passes through:

```csharp
var pipeline = new ResiliencePipelineBuilder<HttpResponseMessage>()
    // 1. Outermost: overall time budget for the whole attempt-with-retries.
    .AddTimeout(TimeSpan.FromSeconds(10))
    // 2. Circuit breaker: stop calling a dependency that's clearly down.
    .AddCircuitBreaker(new CircuitBreakerStrategyOptions<HttpResponseMessage>
    {
        FailureRatio = 0.5,                       // open if 50% fail
        MinimumThroughput = 20,                   // ...over at least 20 calls
        SamplingDuration = TimeSpan.FromSeconds(30),
        BreakDuration = TimeSpan.FromSeconds(15), // cooldown before half-open
    })
    // 3. Retry with exponential backoff + jitter (idempotent calls only!).
    .AddRetry(new RetryStrategyOptions<HttpResponseMessage>
    {
        MaxRetryAttempts = 3,
        BackoffType = DelayBackoffType.Exponential,
        UseJitter = true,                         // desynchronize the herd
        Delay = TimeSpan.FromMilliseconds(200),
    })
    // 4. Innermost: per-try timeout so one hung call can't eat the budget.
    .AddTimeout(TimeSpan.FromSeconds(2))
    .Build();

var response = await pipeline.ExecuteAsync(
    async ct => await httpClient.GetAsync("/inventory", ct), cancellationToken);
```

Read the layering carefully. The *inner* timeout (2s) bounds each individual attempt; the retry sits outside it so a hung call is cancelled and retried; the circuit breaker sits outside the retry so it can count failures *including* exhausted retries and trip; the *outer* timeout caps the total elapsed time across all retries so the user isn't left waiting 30 seconds. Add a **bulkhead** (`RateLimiter`/concurrency limiter) around it, and every theoretical pattern from this chapter is now enforced in production code.

### Blast Radius

Every resilience pattern above is a claim about behaviour under failure, and claims need verifying — the *Verifying Resilience* section below is about how. What every experiment is bounded by, and what most reliability work is ultimately about, is blast radius.

**Blast radius** is the amount of the system a single failure can damage. Great reliability engineering is largely *blast-radius reduction*: cell-based / sharded architectures, bulkheads, per-tenant isolation, and gradual (canary) rollouts all exist to ensure that when — not if — something breaks, it breaks *small*.

### Redundancy, Failover, and Disaster Recovery

- **Redundancy** removes single points of failure: run N+1 instances across multiple availability zones so losing one changes nothing.
- **Failover** is the automatic promotion of a standby when the primary dies — but test it, because untested failover *is a bug*. The graveyard of outages is full of standbys that were misconfigured and never actually took over.
- **Disaster recovery** planning is quantified by two numbers you must be able to state for any critical system:
  - **RTO (Recovery Time Objective):** how long you can be down before it's unacceptable — the target *time* to restore service.
  - **RPO (Recovery Point Objective):** how much *data* you can afford to lose, measured in time. An RPO of 5 minutes means backups/replication must be no more than 5 minutes stale.

An RPO near zero demands synchronous replication (and CAP/PACELC latency costs — the theory comes full circle). A generous RPO of an hour lets you use cheap periodic backups. Match the cost of your DR strategy to the actual business value at risk; not every system deserves multi-region synchronous replication, and pretending otherwise just burns money you should spend elsewhere.

@@SRC: old Chapter 21: Distributed Systems Theory & Reliability Engineering@@
## Verifying Resilience: Chaos Engineering in Practice

Look back at the Polly pipeline earlier in this chapter. Timeout, retry with jitter, circuit breaker, fallback, bulkhead — perhaps forty lines of configuration, sitting in the request path of every call your service makes.

Now answer honestly: **when did that code last run?**

Not "when was it deployed." When did the circuit breaker last open? When did the fallback last return a degraded response? For most services, the answer is "during an incident, which is also when we discovered the breaker's threshold was wrong."

Resilience code is the only code we routinely ship without executing. Its failure paths run rarely by design, unit tests exercise the happy path, and integration tests run against dependencies that are up. So the retry policy that retries a non-idempotent operation, the circuit breaker whose threshold is so high it never trips, the fallback that throws a `NullReferenceException`, and the timeout that is longer than the caller's timeout — all of these sit in production, untested, until the day they are needed and don't work. Frequently they make the incident *worse*: a retry storm turning a slow dependency into a dead one is one of the most common ways a partial outage becomes a total one.

**Chaos engineering** is the practice of executing that code deliberately, on your terms, at 10 a.m. on a Tuesday with the right people watching.

### It is an experiment, not vandalism

The name has done the discipline a disservice; it sounds like breaking things for fun, and the version that involves randomly killing production instances on a Friday is what most people picture. That is the mature end of the practice, not the entry point. The actual method is closer to science than to sabotage, and the structure is what separates it from an outage you caused yourself:

1. **Define steady state.** A measurable property of the *system's behaviour*, expressed in user-visible terms: "checkout success rate stays above 99.5%," "p99 latency stays under 400ms." Not "the pods are running" — internal health is not steady state, because a system whose pods are all healthy can still be failing every user.
2. **Form a hypothesis.** "When the recommendations service becomes unavailable, checkout success rate is unaffected and p99 latency rises by less than 50ms." Write it down *before* you run it. A hypothesis you write afterwards is a description.
3. **Define the blast radius and the abort condition.** Which slice of traffic, which environment, how long — and the specific signal that stops the experiment immediately. Know how to stop it before you start it.
4. **Inject the fault** in the smallest scope that can test the hypothesis.
5. **Compare against the hypothesis.** The experiment "fails" when reality disagrees with what you wrote down — and a failed experiment is the entire point. It found something a real incident would otherwise have found for you.
6. **Fix, then re-run.** An experiment you never re-run tells you what was broken in March.

> **Best practice.** The experiments that find the most bugs are the boring ones close to home: your immediate dependencies, one at a time. Start with "what happens when the cache is down" — not "what happens when we lose a region." Almost every team that runs that first experiment finds something, usually that a cache miss path nobody tested is either far slower than assumed or throws.

### Prerequisites, honestly stated

Chaos engineering is not the first reliability investment a team should make, and running it without the following is how it becomes theatre — or an incident:

- **Observability good enough to see the effect.** If you cannot measure your steady-state metric in near real time, you cannot detect that the experiment broke it. You will either miss the finding or panic at the wrong dashboard. Chapter 13 is a prerequisite, not a companion.
- **A rollback path that works.** The abort condition is only useful if aborting is fast. A feature flag that takes a deployment to flip is not an abort mechanism.
- **Somewhere to run it that is not production.** Start in staging. Yes, staging differs from production and will therefore miss things — that is an argument for eventually running in production, not an argument for starting there.
- **Organizational consent.** Announce the experiment, its window, and its abort condition. An unannounced experiment is indistinguishable from an incident, and the second time you cause a page at 3 p.m. the practice gets banned.

### Fault injection in .NET with Polly

The nice property of the Polly v8 chaos strategies is that they compose into the *same* pipeline as your resilience strategies — so you inject the fault at the exact layer the resilience is supposed to handle, in your own process, with no infrastructure required.

Four strategies cover most needs: **latency** (slow a call), **fault** (throw), **outcome** (return a specific result, e.g. a `503`), and **behavior** (run arbitrary code, for the exotic cases).

```csharp
builder.Services.AddHttpClient<RecommendationsClient>()
    .AddResilienceHandler("recommendations", (pipeline, context) =>
    {
        // Real resilience strategies first — these are what we are testing.
        pipeline.AddTimeout(TimeSpan.FromSeconds(2));
        pipeline.AddRetry(new HttpRetryStrategyOptions
        {
            MaxRetryAttempts = 3,
            BackoffType = DelayBackoffType.Exponential,
            UseJitter = true
        });
        pipeline.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
        {
            FailureRatio = 0.5,
            SamplingDuration = TimeSpan.FromSeconds(30),
            MinimumThroughput = 10
        });

        // Chaos strategies are added LAST, which makes them the innermost
        // strategies (Polly runs the first-added strategy outermost): the fault
        // is introduced closest to the dependency, and every strategy added
        // before it reacts to it, exactly as it would in a real outage.
        var options = context.ServiceProvider
            .GetRequiredService<IOptionsMonitor<ChaosOptions>>();

        pipeline.AddChaosLatency(new ChaosLatencyStrategyOptions
        {
            // Injection is controlled at run time, not at deploy time.
            EnabledGenerator = _ => ValueTask.FromResult(options.CurrentValue.LatencyEnabled),
            InjectionRateGenerator = _ => ValueTask.FromResult(options.CurrentValue.Rate),
            Latency = TimeSpan.FromSeconds(5)
        });

        pipeline.AddChaosOutcome(new ChaosOutcomeStrategyOptions<HttpResponseMessage>
        {
            EnabledGenerator = _ => ValueTask.FromResult(options.CurrentValue.OutcomeEnabled),
            InjectionRateGenerator = _ => ValueTask.FromResult(options.CurrentValue.Rate),
            OutcomeGenerator = static _ => ValueTask.FromResult<Outcome<HttpResponseMessage>?>(
                Outcome.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)))
        });
    });
```

Three details that decide whether this is safe:

- **`EnabledGenerator` reads from configuration on every call**, so injection is turned on and off at run time — through a feature flag or `IOptionsMonitor` — with no deployment. That is your abort switch, and it must be instant.
- **The injection rate is a percentage**, so you can start at 1% of calls and turn it up. That is your blast radius control.
- **Gate it by environment as well as by flag.** A chaos strategy that can be enabled in production by a config change is a chaos strategy that will be enabled in production by an accidental config change. Belt and braces: `if (env.IsProduction() && !explicitlyApprovedChaosWindow) return;`

> **Gotcha.** Strategies added earlier wrap the ones added later, so a chaos strategy added *first* sits outermost and tests nothing useful: its fault never passes through the retry or the breaker, and you have only proven that an exception reaches the caller. Add chaos strategies last, so they sit innermost, closest to the dependency, inside the strategies whose behaviour you want to observe.

### Platform-level injection

Polly tests how *your process* responds to a misbehaving dependency. It cannot test what happens when your process disappears, when a node's disk fills, or when two services can reach the database but not each other. That needs the layer below:

- **Pod and node termination** — does the load balancer notice fast enough? Do in-flight requests drain, or are they dropped? Does your `IHostApplicationLifetime` shutdown path actually finish what it started (Chapter 22)?
- **Network latency and partition** between specific services — the case that finds split-brain bugs and timeout misconfigurations. A service mesh can inject latency and abort responses declaratively between named services.
- **Resource exhaustion** — CPU, memory, disk pressure on a node, which is how you discover that your pod has no memory limit and takes its neighbours down with it.
- **Dependency-level failure** — a managed database failover, a broker restart. Cloud providers offer these as a service (AWS Fault Injection Service, Azure Chaos Studio), which is safer than doing it by hand because they include the stop button.

Chaos Mesh and LitmusChaos are the common open-source options in the Kubernetes world; both express experiments as CRDs, which means they live in git and run in a pipeline like anything else.

### Game days

The highest-value version of all this involves no automation. A **game day** is a scheduled exercise where a team injects a realistic failure and works the resulting incident with their real tooling and real runbooks.

It works because it tests the parts no fault injector reaches: whether the on-call engineer can find the dashboard, whether the runbook's first step still exists, whether anyone knows who owns the failing service, whether the escalation path works on a Friday evening, whether the status page can actually be updated by the person who needs to update it. These are, in practice, where incident time actually goes — and they are invisible to every technical control in this chapter.

A workable format: pick a scenario a week ahead and tell people the window but not the scenario; nominate an incident commander who is deliberately *not* the person who knows the system best; run it for a fixed 60–90 minutes with a facilitator holding the stop button; and write up findings as ordinary backlog items with owners. The output is not a score. It is a list of specific, unglamorous gaps — an out-of-date runbook, an alert that fires to a rotation that no longer exists, a dashboard nobody has permission to view.

> **Takeaway.** Run one game day before you automate anything. It will produce more actionable findings than a quarter of fault injection, it costs one afternoon, and it tells you whether your organization is ready for the automated version.

### Where it is theatre

Being honest about the failure modes of the practice itself:

- **Chaos without observability** proves nothing. You broke something, nothing obvious happened, you declared success. Whether the error budget moved is unknown.
- **Chaos as a compliance checkbox** — a quarterly experiment run against a scenario known to pass, so the audit line is green.
- **Chaos in an environment nothing depends on**, with no traffic and no real data, testing a topology production does not have.
- **Findings without owners.** The experiment failed, everyone agreed it was interesting, nobody filed the ticket. This is the most common one by a wide margin.

The practice earns its keep when a failed experiment reliably produces a fix, and when the same experiment is re-run afterwards to confirm it. Everything else is a demonstration.

@@SRC: old Chapter 21: Distributed Systems Theory & Reliability Engineering@@
## Putting It Together

The through-line of this chapter is a single, humbling idea: **in a distributed system, partial failure is the normal state, not an exception.** The theory — CAP, PACELC, consistency models, consensus, logical clocks — tells you precisely which guarantees are *achievable* and what they cost. The engineering — idempotency, backoff with jitter, circuit breakers, bulkheads, error budgets, chaos testing, measured RTO/RPO — builds systems that stay *useful* while individual parts fail underneath them.

The mid-level instinct is to make the network invisible and hope. The senior instinct is to assume the network is actively trying to ruin your day, and to design a system that shrugs, degrades gracefully, retries safely, and keeps its promises anyway — right up to, but not past, the reliability you actually promised.

@@SRC: old Chapter 21: Distributed Systems Theory & Reliability Engineering@@
## Sources & Further Reading

- *Designing Data-Intensive Applications* by Martin Kleppmann — the definitive treatment of consistency, consensus, replication, and distributed time.
- *Site Reliability Engineering* (the "Google SRE Book") and *The Site Reliability Workbook* — SLIs/SLOs, error budgets, and operational practice. Available free at sre.google.
- L. Peter Deutsch et al., "The Eight Fallacies of Distributed Computing."
- Eric Brewer, "CAP Twelve Years Later"; Daniel Abadi's writing on PACELC.
- Diego Ongaro and John Ousterhout, "In Search of an Understandable Consensus Algorithm (Raft)"; Leslie Lamport, "Paxos Made Simple" and "Time, Clocks, and the Ordering of Events in a Distributed System."
- Martin Kleppmann, "How to do distributed locking" (the Redlock analysis), and Salvatore Sanfilippo's response.
- AWS Architecture Blog, "Exponential Backoff and Jitter"; the AWS Well-Architected Framework — Reliability Pillar.
- Microsoft Learn: "Cloud Design Patterns" (Circuit Breaker, Bulkhead, Retry, Throttling) and the Polly / `Microsoft.Extensions.Http.Resilience` documentation.
- Azure Well-Architected Framework — Reliability pillar (RTO/RPO, failover, redundancy).
- Netflix Technology Blog on Chaos Engineering; *Chaos Engineering* by Rosenthal and Jones (O'Reilly) — the origin of the hypothesis-driven method used above.
- **Principles of Chaos Engineering** (principlesofchaos.org) — the short, canonical statement of the discipline.
- **Polly v8 chaos strategies** documentation (`Polly.Simmy` lineage) — latency, fault, outcome, and behavior injection composed into a resilience pipeline. https://www.pollydocs.org/chaos/
- **Azure Chaos Studio** and **AWS Fault Injection Service** documentation; **Chaos Mesh** and **LitmusChaos** for Kubernetes-native experiments.

@@SRC: old Chapter 22: Background Processing, Scheduling & the Actor Model@@
## Part B — Scheduling & Job Frameworks

Workers are great at "keep processing whatever shows up." They are clumsy at "run this at 02:00 every night," "run this once in 15 minutes," or "let me see which jobs failed last week." That is the domain of scheduling frameworks.

### Cron, briefly

A cron expression is a compact schedule spec. The classic Unix form has five fields; Quartz.NET uses a six/seven-field variant that adds seconds (and optionally a year):

```
┌ minute (0-59)
│ ┌ hour (0-23)
│ │ ┌ day of month (1-31)
│ │ │ ┌ month (1-12)
│ │ │ │ ┌ day of week (0-6, Sun=0)
│ │ │ │ │
* * * * *      # every minute
0 3 * * *      # 03:00 every day
*/15 * * * *   # every 15 minutes
0 9 * * 1-5    # 09:00 Monday-Friday
```

> **Pitfall:** Cron runs in a specific time zone. Servers usually run UTC, but "midnight" to a business often means local wall-clock time - which drifts by an hour across daylight-saving transitions. Always be explicit about the zone, and be aware that a job scheduled for 02:30 local may run twice or zero times on DST change days.

### Hangfire

Hangfire's pitch is "background jobs backed by persistent storage, with almost no ceremony." You enqueue a job as a plain method call; Hangfire serializes it, stores it (SQL Server, PostgreSQL, Redis, and others), and a server component picks it up and runs it. Because the job lives in storage, it **survives process restarts** and retries automatically on failure - a categorical upgrade over an in-memory channel.

```csharp
builder.Services.AddHangfire(cfg => cfg
    .UsePostgreSqlStorage(c => c.UseNpgsqlConnection(connectionString)));
builder.Services.AddHangfireServer();

var app = builder.Build();
app.UseHangfireDashboard("/jobs"); // secure this in production!
```

Hangfire distinguishes several job kinds:

```csharp
// Fire-and-forget: runs once, as soon as a worker is free.
BackgroundJob.Enqueue<IEmailSender>(s => s.SendWelcome(userId));

// Delayed: runs once, after a delay.
BackgroundJob.Schedule<IInvoiceService>(
    s => s.ChargeAsync(orderId, CancellationToken.None),
    TimeSpan.FromMinutes(15));

// Continuation: runs after a parent job succeeds.
var parent = BackgroundJob.Enqueue<IReportBuilder>(r => r.Build(reportId));
BackgroundJob.ContinueJobWith<IEmailSender>(parent, s => s.SendReport(reportId));

// Recurring: cron-scheduled, identified by a stable key.
RecurringJob.AddOrUpdate<INightlyCleanup>(
    "nightly-cleanup",
    j => j.RunAsync(CancellationToken.None),
    "0 3 * * *",
    new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });
```

The recurring-job *key* (`"nightly-cleanup"`) is important: it makes the registration idempotent. Deploy ten instances that all call `AddOrUpdate` with the same key and you get one schedule, not ten. The dashboard at `/jobs` gives you a live view of enqueued, processing, succeeded, and failed jobs, with the ability to requeue failures by hand - invaluable operationally.

> **Best practice:** Enqueue *interface method calls* (`Enqueue<IEmailSender>(...)`), not concrete types. Hangfire resolves the implementation from DI at execution time, and your job code stays testable. Also keep job arguments small and serializable - pass an `orderId`, not a fat `Order` object graph.

Hangfire coordinates multiple servers automatically: any number of Hangfire servers can share one storage, and each job is executed by exactly one of them. That distributed coordination out of the box is a large part of its appeal.

### Quartz.NET

Quartz.NET is the .NET port of the venerable Java Quartz scheduler. It is lower-level and more explicit than Hangfire, separating three concepts cleanly:

- A **Job** is the unit of work (`IJob`).
- A **Trigger** decides *when* the job fires (simple interval, cron, calendar-based).
- The **Scheduler** binds jobs to triggers and runs them.

Quartz integrates with the Generic Host via `Quartz.Extensions.Hosting`.

```csharp
public sealed class ReindexJob : IJob
{
    private readonly ISearchIndexer _indexer;
    private readonly ILogger<ReindexJob> _logger;

    public ReindexJob(ISearchIndexer indexer, ILogger<ReindexJob> logger)
        => (_indexer, _logger) = (indexer, logger);

    public async Task Execute(IJobExecutionContext context)
    {
        _logger.LogInformation("Reindex starting at {Time}", DateTimeOffset.UtcNow);
        await _indexer.RebuildAsync(context.CancellationToken);
    }
}
```

```csharp
builder.Services.AddQuartz(q =>
{
    var jobKey = new JobKey("reindex");
    q.AddJob<ReindexJob>(opts => opts.WithIdentity(jobKey));

    q.AddTrigger(t => t
        .ForJob(jobKey)
        .WithIdentity("reindex-nightly")
        .WithCronSchedule("0 0 2 * * ?", x => x.InTimeZone(TimeZoneInfo.Utc)));
});

// Waits for running jobs to finish on shutdown - graceful by default.
builder.Services.AddQuartzHostedService(o => o.WaitForJobsToComplete = true);
```

Quartz's cron format is the six-field Quartz variant (note the leading seconds field and the `?` in a day-of-week/day-of-month slot). Its trigger model is richer than Hangfire's: misfire policies (what to do if the scheduler was down when a trigger should have fired), calendars to exclude holidays, and priority ordering. With `AddPersistentStore` (an ADO.NET job store), Quartz persists schedules and, crucially, uses database locks so that in a clustered deployment **a given trigger fires on exactly one node**.

> Choose Quartz.NET when you need precise, complex scheduling semantics and are comfortable with more configuration. Choose Hangfire when you want fire-and-forget/delayed jobs plus a dashboard with minimal setup. They are not mutually exclusive - some systems run both.

### Hosted service vs Hangfire vs cloud scheduler

| Need | Reach for |
|------|-----------|
| Continuous processing loop, queue consumer | `BackgroundService` / Worker Service |
| Fire-and-forget & delayed jobs, retries, ops dashboard | Hangfire |
| Rich, precise cron/calendar scheduling with misfire handling | Quartz.NET |
| Trigger work without keeping a process alive; serverless | Cloud scheduler (Azure Functions Timer trigger, AWS EventBridge, Kubernetes CronJob) |

The cloud-scheduler option deserves emphasis. If your workload is "run this container for two minutes every hour," standing up an always-on host with an in-process scheduler is wasteful and adds an availability concern (the scheduler node must stay up). A managed cron trigger that spins your job up on demand is cheaper and more robust. The trade-off: you lose the shared in-memory state and the tight feedback of an embedded dashboard, and you take on the cloud provider's scheduling guarantees.

### Ensuring a job runs once across instances

This is the recurring headache of scheduled work in a scaled-out world. Three copies of your service, each with a Quartz scheduler, each firing the nightly cleanup at 02:00 - now it runs three times. Some approaches:

- **Persistent job store with clustering** (Quartz `AddPersistentStore` + `UseClustering`, or Hangfire's shared storage). The framework itself uses DB locks to ensure single execution. This is the simplest correct answer when available.
- **Distributed lock.** Have the job try to acquire a named lock (a row with a unique constraint, a Redis `SET NX PX`, a `SqlServerDistributedLock`); only the winner runs. Release on completion or let a TTL expire.

```csharp
public async Task RunAsync(CancellationToken ct)
{
    await using var handle = await _lockProvider.TryAcquireAsync(
        "nightly-cleanup", TimeSpan.Zero, ct);
    if (handle is null) return; // another instance holds it - stand down

    await DoCleanupAsync(ct);
}
```

- **Leader election.** Elect one instance as leader (via a lease in a coordination store like ZooKeeper, etcd, Consul, or a Kubernetes `Lease` object). Only the leader schedules. This centralizes the decision rather than racing per-job.

> **Best practice:** Give distributed locks a **timeout / TTL** shorter than your cron interval but longer than a normal run. A lock with no expiry, held by an instance that crashes mid-job, deadlocks your schedule forever.

---

@@SRC: old Chapter 22: Background Processing, Scheduling & the Actor Model@@
## Part C — The Actor Model & Microsoft Orleans

Everything so far treats concurrency as a hazard to be managed with locks, idempotency keys, and database transactions. The actor model offers a different bargain: **structure your system so that shared mutable state, and therefore locks, largely disappear.**

### What is an actor?

An actor is a unit that combines three things:

1. **Private state** - no other actor can touch it directly.
2. **A mailbox** - an inbox of messages addressed to it.
3. **Single-threaded processing** - the actor handles one message at a time, to completion, before starting the next.

Because an actor processes its mailbox serially, the code inside it never runs concurrently with itself. There is *no data race on its state*, because there is never a second thread inside it. You get the correctness of a lock without writing a lock - the concurrency control is structural, baked into how messages are dispatched.

Actors communicate only by sending messages (asynchronously). One actor cannot reach into another and mutate it; it can only ask. This gives you a system of small, isolated, sequential islands that scale by *multiplying* rather than by *sharing*. The archetype is a bank account: model each account as an actor, and concurrent transfers against the same account serialize naturally through its mailbox - no `lock`, no optimistic-concurrency retry loop.

### Microsoft Orleans and "virtual actors" (grains)

Traditional actor frameworks (Erlang, Akka) make you manage actor lifecycles: create them, supervise them, dispose them, worry about where they live. Orleans, born at Microsoft Research and the engine behind Halo's cloud services, introduced the **virtual actor** to remove that burden. In Orleans, an actor is called a **grain**, and the model has a beautiful property: **grains always exist.**

You never create or destroy a grain. You simply address one by identity and call it:

```csharp
var account = client.GetGrain<IAccountGrain>("acct-42");
await account.Deposit(100m);
```

If grain `acct-42` is not currently in memory, the Orleans runtime **activates** it - instantiates it on some server and, if it has persistent state, loads that state. If a grain sits idle, the runtime **deactivates** it to reclaim memory. This activation lifecycle is automatic and invisible to your calling code. From the caller's perspective the grain is eternal; activation is just a caching detail.

Key guarantees the runtime provides:

- **Single-threaded per grain.** By default, only one call executes inside a given grain activation at a time - the same serial-mailbox guarantee, so grain state needs no locks.
- **Location transparency.** A grain reference works the same whether the grain lives on this server or another. The runtime handles routing.
- **Placement & clustering.** Orleans servers are called **silos**. A group of silos forms a **cluster**. The runtime places activations across silos, balances load, and - when a silo dies - reactivates its grains elsewhere. Your code doesn't change as you scale from one silo to fifty.
- **Persistence.** A grain can declare persistent state; the runtime loads it on activation and you write it back explicitly.

### A small grain example

An interface (the contract, marked with a key type) and an implementation:

```csharp
// Contract - shared between silo and clients.
public interface IAccountGrain : IGrainWithStringKey
{
    Task Deposit(decimal amount);
    Task<bool> Withdraw(decimal amount);
    Task<decimal> GetBalance();
}
```

```csharp
// Implementation, with runtime-managed persistent state.
public sealed class AccountGrain : Grain, IAccountGrain
{
    private readonly IPersistentState<AccountState> _state;

    public AccountGrain(
        [PersistentState("account", "accountStore")]
        IPersistentState<AccountState> state) => _state = state;

    public async Task Deposit(decimal amount)
    {
        _state.State.Balance += amount;
        await _state.WriteStateAsync(); // persist the mutation
    }

    public async Task<bool> Withdraw(decimal amount)
    {
        if (_state.State.Balance < amount) return false; // no lock needed:
        _state.State.Balance -= amount;                  // one call at a time
        await _state.WriteStateAsync();
        return true;
    }

    public Task<decimal> GetBalance() => Task.FromResult(_state.State.Balance);
}

[GenerateSerializer]
public sealed class AccountState
{
    [Id(0)] public decimal Balance { get; set; }
}
```

Notice there is not a single `lock`, `Interlocked`, or transaction in that `Withdraw` - yet two concurrent transfers against `acct-42` cannot corrupt the balance, because Orleans runs them one after another inside the one activation. That is the whole payoff of the model made concrete.

Hosting a silo is a few lines on the Generic Host (Orleans is now versioned alongside the .NET release — v8 with .NET 8, v9 with .NET 9 (2024-2025) — and integrates directly with the modern host builder):

```csharp
var builder = Host.CreateApplicationBuilder(args);
builder.UseOrleans(silo =>
{
    silo.UseLocalhostClustering()          // real deployments use Azure/ADO.NET clustering
        .AddMemoryGrainStorage("accountStore"); // real deployments use a durable store
});
using var host = builder.Build();
await host.RunAsync();
```

Swap `UseLocalhostClustering` for a clustering provider (Azure Table Storage, ADO.NET, Redis) and `AddMemoryGrainStorage` for a durable store, and the *same grain code* now runs across a multi-silo cluster with failover. That continuity from laptop to production cluster is Orleans' signature strength.

> **Scheduling inside a grain:** Orleans' idiomatic in-grain scheduling is **grain timers** (non-persistent, for periodic work while an activation is alive) and **reminders** (persistent, durable across deactivation and full cluster restarts). They are the actor-model counterpart to the job frameworks in Part B — the way a grain schedules its own future work without an external scheduler.

### When Orleans fits - and when it doesn't

Orleans shines when you have **many small, independently-addressable, stateful entities** with high throughput and low latency, where keeping state in memory (rather than round-tripping a database on every call) is a decisive win:

- **Gaming** - player sessions, matches, leaderboards (its origin story).
- **IoT** - a grain per device, holding the device's live state.
- **Real-time systems** - chat rooms, collaborative documents, live dashboards, ride-hailing dispatch.
- **Per-entity workflows** - a grain per order, per shopping cart, per user session, coordinating that entity's lifecycle.

It is a poor fit when:

- Your workload is **stateless request/response** over a shared database - a plain ASP.NET Core service is simpler and you gain nothing from grains.
- You need **set-based operations** - "sum every account's balance" fights the model, which is built around addressing entities one at a time, not scanning them.
- Your team is small and the operational cost of running a stateful cluster (clustering provider, storage, monitoring, understanding activation/placement) outweighs the benefit. Actors are a genuine paradigm shift with a real learning curve.

> **Pitfall:** A grain's single-threaded guarantee is per-activation, not global. If you make a hot "singleton" grain that every request must call, you have re-created a bottleneck - serialized through one mailbox. Model for *many* grains with well-distributed keys, not a few god-grains.

### Neighbors: Akka.NET and Dapr actors

Orleans is not the only actor game in .NET. **Akka.NET** is a faithful port of the JVM's Akka: it exposes classic actors with explicit lifecycles, hierarchical supervision (parents restart failed children per a strategy), and location transparency. It gives you more control - and more responsibility - than Orleans' virtual actors, and it's the natural choice if you want the traditional supervision-tree model or you're porting from Akka.

**Dapr** (Distributed Application Runtime) offers actors as one building block among many, delivered as a **language-agnostic sidecar**. Dapr's virtual-actor model is conceptually close to Orleans (turn-based single-threaded access, automatic activation, state persistence) but you interact with it over HTTP/gRPC from any language, and it slots into a broader platform of pub/sub, state stores, and service invocation. Choose Dapr when polyglot services and a portable, infrastructure-managed runtime matter more than the deep, .NET-native ergonomics Orleans provides.

---

@@SRC: old Chapter 22: Background Processing, Scheduling & the Actor Model@@
## Wrapping Up

The arc of this chapter is a maturation in how you think about "later" work. A `BackgroundService` runs a loop; but the senior questions are about shutdown, duplicates, and scale-out. Job frameworks like Hangfire and Quartz.NET give you persistence, retries, cron, and - critically - single-execution guarantees across a cluster, so you stop hand-rolling schedulers. And the actor model, realized in Orleans' virtual grains, inverts the concurrency problem entirely: instead of guarding shared state with locks, you partition state into single-threaded islands where races cannot occur by construction. Reach for each when its shape matches your problem, and always design as if the process will crash mid-operation and run twice - because in production, eventually, it will.

@@SRC: old Chapter 22: Background Processing, Scheduling & the Actor Model@@
## Sources & Further Reading

- Microsoft Learn — *Worker Services in .NET* and *Background tasks with hosted services in ASP.NET Core* (`IHostedService`, `BackgroundService`, graceful shutdown).
- Microsoft Learn — *Implement the outbox pattern* and .NET microservices architecture guidance (transactional outbox, at-least-once, idempotency).
- Azure Architecture Center — *Asynchronous Request-Reply pattern* (`202 Accepted`, `Location`, `Retry-After`, the status endpoint and `303 See Other`); RFC 9110 for the status-code semantics.
- Microsoft Learn — *Microsoft Orleans documentation*: Overview, Grains, Grain persistence, Silos & clustering, and the "Hello World" / minimal application tutorials (https://learn.microsoft.com/dotnet/orleans/).
- Hangfire Documentation — Background Methods (fire-and-forget, delayed, recurring, continuations), Dashboard, and persistent storage providers (https://docs.hangfire.io/).
- Quartz.NET Documentation — Jobs and Triggers, Cron Triggers, and hosted-service integration (https://www.quartz-scheduler.net/documentation/).
- Akka.NET Documentation — Actors and supervision (https://getakka.net/).
- Dapr Documentation — Actors building block (https://docs.dapr.io/developing-applications/building-blocks/actors/).

@@SRC: old Chapter 34: Interview Questions & How to Answer Them@@
## Interview Questions

*Revise: Ch. 9 — Messaging & Distributed Systems · Ch. 21 — Distributed Systems Theory & Reliability Engineering*

**Explain the CAP theorem.**
Under a network **P**artition, a distributed system must choose between **C**onsistency (every read sees the latest write) and **A**vailability (every request gets a response). You can't have both during a partition. In practice systems are CP (refuse/stall to stay consistent) or AP (serve possibly-stale data to stay up); the choice is per-operation, and PACELC extends it to the latency trade-off when there's no partition.

**Red flag:** "You pick any two of C, A, and P" — partition tolerance isn't optional; the real choice is C vs A *during* a partition.

**How do you scale a web application?**
Vertical first (bigger box — simple, limited), then horizontal: run many stateless instances behind a load balancer. Add caching (in-memory, distributed), read replicas or sharding for the database, a CDN for static assets, and async processing via queues to smooth spikes. Statelessness is the enabler for horizontal scale.

**Why must services be stateless to scale horizontally?**
So any instance can handle any request and you can add/remove instances freely behind a load balancer. Session state kept in-process ties a user to one instance (sticky sessions) and breaks on scale-down or failover. Push state to a shared store (Redis, DB) or a signed token so instances stay interchangeable.

**Caching strategies and the hard part?**
Strategies: cache-aside (app loads on miss, most common), read-through/write-through, write-behind. The hard part is **invalidation** — knowing when cached data is stale. Tools: TTL expiry, event-driven eviction on write, and versioned keys. "There are only two hard things: cache invalidation and naming things." Also plan for stampedes (many misses at once) with locking or staggered TTLs.

**Why introduce a message queue?**
To decouple producer from consumer, absorb load spikes (buffering), enable async processing, and add resilience — if the consumer is down, messages wait. It also enables independent scaling of producers and consumers and retry/dead-letter handling. Cost: eventual consistency and added operational surface.

**Is exactly-once delivery real?**
Not in a strict end-to-end sense over an unreliable network. Practically you get **at-least-once** delivery plus **idempotent** consumers, which yields exactly-once *processing* — the effect happens once even if the message arrives twice. Design consumers to dedupe (idempotency keys, processed-message table).

**Red flag:** "Just configure the broker for exactly-once delivery" — no broker setting survives an unreliable network end-to-end; the guarantee comes from idempotent consumers.

**What is the circuit breaker pattern?**
A wrapper around a remote call that, after repeated failures, "trips" and fails fast for a cooldown period instead of hammering a struggling dependency — then allows a trial request (half-open) to test recovery. It protects both caller (no piling-up threads) and callee (room to recover). Pair with timeouts, retries with backoff, and bulkheads (Polly implements these).

**Traffic is about to spike 5x for a launch — what do you do?**
Load-test to find the current ceiling first. Then: scale out stateless tiers (and pre-warm/auto-scale), add caching to cut DB load, protect the database with read replicas and connection-pool limits, move non-critical work to queues, add rate limiting and graceful degradation, and set up a CDN. Have a rollback and a "shed load" plan. Verify with the load test, don't hope.

**A downstream dependency goes down — how does your service behave?**
It should degrade gracefully, not cascade-fail. Use timeouts (never wait forever), a circuit breaker to fail fast, retries with exponential backoff and jitter for transient blips, a fallback (cached/default response) where the business allows, and bulkheads to isolate the failure to one feature. The goal: your service stays up and honest about reduced functionality.

---

@@SRC: practice from old module page Part 2 · Module 3: Distributed Consistency@@

## Practice

**1. The batch that outlives its locks (45 min).** [Chapter 51](#chapter-51-the-azure-casebook-real-incidents-real-fixes)'s second *Find the bug*: answer it before opening the answer, then read the verified code in [`verify/exercises/Ch51`](https://github.com/malyna2/dotnet-handbook/tree/main/verify/exercises/Ch51). The reference run (a 5-second lock, a 1-second handler) turned 12 messages into 14 handler calls with the batch, and exactly 12 with `ServiceBusProcessor`. To run it yourself you need Docker and the Service Bus emulator:

```bash
ACCEPT_EULA=Y verify/exercises/Ch51/verify.sh
```

**2. A written design (1 h 30 min).** One page, for a flow you know or for "place order → reserve stock → charge → ship": the outbox and its relay (polling or CDC, and why), where each consumer claims its message ID and how long the claim is kept, what must be ordered and by which key, the saga's compensations (and the step that can't be compensated), and one retry policy per call with the layer it lives in. Finish with a table: failure, what happens, what the user sees.

**Evidence to keep**, in your own public portfolio repo, not in this one: the design page and your answer to the exercise.

The Practice Gym's planned lab *Idempotent Messaging End to End* (M3 in `PRACTICE_ROADMAP.md`) will become this module's lab when it ships.

Later, if you need it: [Chapter 50: Service Bus](#service-bus) for sessions, duplicate detection and the processor defaults, [Chapter 21: Consensus](#consensus-getting-nodes-to-agree), and [Chapter 22: Ensuring a job runs once across instances](#ensuring-a-job-runs-once-across-instances).

## Three questions

**1.** With Debezium's outbox event router, why does `aggregateid` become the Kafka message key and not the event's own `id`? What ordering do you get end to end, and where can it still break?

<details>
<summary>Answer</summary>

- **The key picks the partition, and Kafka orders only within a partition.** Keyed by `aggregateid`, all events of order 42 go to one partition in the order they were committed, which the router reads from the log. Keyed by the event ID, they would scatter across partitions and arrive in any order.
- **End to end, you get per-aggregate order, at least once.** The relay can publish a row again after a crash, so a consumer can see 1, 2, 2, 3: deduplicate on the `id` header.
- **Where it breaks.** A consumer that processes one partition's messages concurrently (several handlers per partition, or a thread pool behind a single poll) reorders them itself. Changing the partition count moves keys to other partitions, so order across that change isn't guaranteed. And events of *different* aggregates have no order at all: a consumer that needs "the customer before the order" must tolerate either.
</details>

**2.** A payment consumer deduplicates with an inbox table purged after 7 days. Three weeks after an incident, the team resubmits 40,000 dead-lettered messages, and some customers are charged twice. Why, and what would have prevented it?

<details>
<summary>Answer</summary>

- **The claim outlived its memory.** Some of those messages had been processed before they were dead-lettered (for example, the effect succeeded and the settlement failed until the delivery count ran out). Their IDs were purged after 7 days, so on resubmission the inbox saw new IDs and the effect ran again.
- **Size retention from the longest way back.** Dead-letter resubmission, relay replays and restores are all ways back; the window must exceed the longest you allow, or the resubmit tool must refuse anything older than it.
- **Make the effect itself idempotent.** Pass the payment ID as the provider's idempotency key, or check a natural key ("this order already has a captured payment") in the same transaction. Those never expire. The inbox is the fast path; the natural key is the guarantee.
</details>

**3.** A downstream service slows down, and within a minute it receives ten times its normal traffic and falls over completely. The callers use retries with exponential backoff and jitter. What happened, and what do you change?

<details>
<summary>Answer</summary>

- **Retries multiplied.** Every layer that retries multiplies the attempts of the layer above: three layers of 4 attempts each is up to 64 calls per user action. Jitter spread them out in time but didn't reduce them, and the slowdown made each call slower, so in-flight requests piled up as well.
- **Timeouts made it worse.** A caller that times out and retries leaves the first request running on the server, so the struggling service does both.
- **Change:** retry at one layer only; give the others a total time budget; put a circuit breaker on the call so the callers stop once the failure ratio crosses its threshold; cap concurrency per dependency with a bulkhead; and on the server, shed load early (`429` or `503` with `Retry-After`) instead of queueing work it can't finish. Retry only idempotent operations.
</details>

## Decide

An inventory service consumes `OrderPlaced`, `OrderAmended` and `OrderCancelled` events for about two million orders a day, on a Service Bus topic subscription with `MaxConcurrentCalls = 16`. A few times a day an amendment is applied after a cancellation, and stock is reserved for a cancelled order. Three proposals:

1. Turn on sessions with `SessionId` = order ID.
2. Put a version on every event and apply an event only if it is newer than the state already stored.
3. Set `MaxConcurrentCalls = 1`.

<details>
<summary>How a senior engineer weighs it</summary>

**What each costs.**

- *Sessions:* strict per-order order, and still many orders in parallel. Sessions are fixed when the entity is created, so this is a new subscription and a migration of the producers, which must all set `SessionId`. A message that keeps failing blocks its order until it is dead-lettered, and a hot order becomes a hot session.
- *Version checks:* a version (or a sequence number from the producer's aggregate) in every event, and a conditional update in the consumer. No broker change, no head-of-line blocking, full concurrency. It drops stale updates rather than reordering them, so it only works when the latest state is all you need, and a dropped event must be one you can afford to ignore.
- *One consumer:* a configuration change, and it doesn't work. Redelivery after a lock expiry or an abandon still puts an older message behind a newer one, and throughput collapses to one message at a time.

**What decides it here:** the consumer needs the latest state of the order, not every intermediate step, and the producer already owns the order aggregate, which can number its versions.

**The choice.** Version checks: the producer stamps each event with the aggregate's version, and the consumer updates only `WHERE Version < @v`, inside the same transaction as its inbox claim.

**What would change it.** If each intermediate event triggers its own effect (an email per amendment), the latest state isn't enough, and sessions are worth their cost. If the producer can't produce a reliable version, sessions are the only way to get order.
</details>

## Check at work

**Inspect.** For each consumer in your service: where is the duplicate check, does it commit in the same transaction as the effect, and how long is a claim kept against the longest way a message can come back? Which consumers assume order, and what enforces it: a session, a partition key, a version check, or hope? Count the layers that retry one call, from the client to the database, and multiply their attempts.

**Measure.** For one queue or subscription: the share of deliveries with `DeliveryCount > 1`, the dead-letter count and its age, and the outbox's oldest unpublished row at peak, which is your relay's real latency.
