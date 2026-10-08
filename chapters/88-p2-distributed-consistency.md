# Part 2 · Module 3: Distributed Consistency

> **What this module makes you able to do.** Design a flow that spans services and a broker so that duplicates, reordering, partial failure and a dependency outage cost nothing worse than latency, and defend what each guarantee costs in storage, throughput and operations.

**Time:** reading ≈ 45 min; hands-on ≈ 3 h 50 min — the emulator exercise 45 min, the written design 1 h 30 min, the questions 20, the decision 15, the check at work 1 h.

## Covers

- the outbox at scale: a polling relay against CDC with Debezium's outbox event router, and what each costs;
- the inbox: claiming a message ID with the effect, and the retention window that decides how long a duplicate is still recognised;
- ordering: Service Bus sessions and head-of-line blocking, partition keys, and version checks that drop stale updates;
- sagas, orchestrated or choreographed, and compensations that can't undo everything;
- why exactly-once is a myth, and what effectively-once costs;
- retry with jitter, the circuit breaker and the bulkhead, retry storms, and the theory (CAP, PACELC, consistency models) that changes a decision.

## The mechanism to explain without notes

**Between services, every hop delivers at least once and in no guaranteed order, so correctness has to live in the receiver: an effect that is claimed once, an update that knows its version, and a failure that stays inside its bulkhead.**

Part 1's messaging basics module covered peek-lock, redelivery, the dead-letter queue, claim-first deduplication and the outbox idea. This module is what those become at scale and across services.

- **The outbox moves the dual write into one transaction, and the relay is where the cost lives.**
  - *A polling relay* reads unpublished rows, publishes, and marks them. Latency is the poll interval. Two instances polling the same table publish twice unless rows are claimed (`FOR UPDATE SKIP LOCKED`, or an atomic `UPDATE … SET LockedBy`). The table needs cleaning.
  - *CDC* reads the database's log after commit, so nothing polls. Debezium's outbox event router expects an insert-only table: `aggregateid` becomes the Kafka message key, so all events of one aggregate land in one partition, in order; `aggregatetype` names the topic (`outbox.event.<aggregatetype>` by default); `id` travels as a header for deduplication; deletes are ignored, so cleaning the table emits nothing. The cost is a Kafka Connect deployment and a replication slot that holds the log on the database while the connector is down.

  Both are at-least-once: a relay that publishes and crashes before marking the row publishes again.
- **The inbox makes the effect idempotent, for as long as it remembers.** The consumer inserts the message ID under a unique key in the same transaction as the business change, so the second copy hits the violation and a failed effect releases the claim. The table can't grow forever, and its retention window is a promise: a duplicate that arrives after its ID was purged is processed as new. Natural keys ("does order 42 already have a payment?") and version checks never expire.
- **Order exists only per key, and only if you pay for it.** A Service Bus session (`SessionId`, say the order ID) gives one receiver an exclusive lock on the whole session, so its messages are processed in order, one at a time. A failing message blocks its session: it is redelivered at the head until it is dead-lettered (10 deliveries by default). Kafka orders within a partition, so the key decides what is ordered. Where strict order isn't needed, carry a version and drop anything older than the state you hold (`UPDATE … WHERE Id = @id AND Version < @v`).
- **A saga trades atomicity for compensations.** Each step commits locally; a failure runs the compensations of the steps before it. A compensation is a new business action, not a rollback: it must be idempotent, it can fail and need its own retry, and some effects (a sent email, a shipped parcel) can only be answered, not undone.
- **Resilience patterns keep one failure from becoming everyone's.** Retries with backoff and jitter absorb blips. A circuit breaker fails fast when a dependency is clearly down. A bulkhead caps how much of your threads and connections one dependency can hold.

> **Pay attention.** **Retries multiply across layers.** A gateway, a service and its client library that each make up to 4 attempts (3 retries) can send 4 × 4 × 4 = 64 requests to the bottom dependency for one user action, at exactly the moment it is struggling. That is a retry storm, and jitter doesn't fix it: jitter desynchronises clients, it doesn't reduce the count. Retry at one layer, usually the one closest to the failing call; give the others a total timeout; let a circuit breaker stop the attempts once the failure rate says the dependency is down. Where the breaker sits relative to the retry decides what it counts: inside the retry it sees every attempt, outside it sees only the exhausted sequence (Chapter 21's pipeline puts it outside, and says why).

> **Pay attention.** **The dedup window is shorter than the redelivery window.** An inbox purged after 7 days, and a dead-letter queue resubmitted after three weeks (Chapter 51's Case 6), means every resubmitted message is processed again. Size the retention from the longest path a message can take back to you: lock expiry, dead-letter resubmission, a relay replay, a restored backup. Where that is unbounded, make the effect itself idempotent: a natural key, a version check, or an idempotency key passed to the downstream API.

## Read (≈ 45 min)

1. [Chapter 9: Saga: Managing Long-Running Distributed Transactions](#saga-managing-long-running-distributed-transactions): orchestration against choreography, and a persisted state machine.
2. [Chapter 9: Resilience Patterns: Retry, Circuit Breaker, Bulkhead](#resilience-patterns-retry-circuit-breaker-bulkhead), [Why Exactly-Once Is (Almost) a Myth](#why-exactly-once-is-almost-a-myth), [Deduplication](#deduplication) and [Consistency in a Distributed World](#consistency-in-a-distributed-world).
3. [Chapter 21: CAP and PACELC](#cap-and-pacelc-the-physics-of-distributed-state) and [Consistency Models](#consistency-models-what-the-data-is-correct-even-means): PACELC is the trade-off you pay on every request, not only during a partition.
4. [Chapter 21: Distributed Time](#distributed-time-why-you-cant-trust-the-clock) and [Distributed Locks Are Dangerous](#distributed-locks-are-dangerous): why ordering by timestamps and locking without fencing both fail.
5. [Chapter 21: Idempotency](#idempotency-the-antidote-to-did-that-actually-happen) and [Detecting Failure and Retrying Without Making It Worse](#detecting-failure-and-retrying-without-making-it-worse): full jitter. The idempotency sample claims the key before the charge, in the same transaction: that order is the whole point.
6. [Chapter 21: Patterns for Graceful Failure](#patterns-for-graceful-failure) and [A Concrete .NET Resilience Pipeline with Polly](#a-concrete-net-resilience-pipeline-with-polly): the layering of timeouts, breaker and retry.
7. [Chapter 22: The outbox-driven worker](#the-outbox-driven-worker) and [Scaling workers, at-least-once delivery, and idempotency](#scaling-workers-at-least-once-delivery-and-idempotency): the polling relay and its competing instances.
8. [Chapter 23: Change Data Capture (CDC)](#change-data-capture-cdc): Debezium as the relay, and outbox against raw CDC.
9. [Chapter 33: Scenario 2 — The lost write](#scenario-2-the-lost-write-the-user-got-200-but-the-data-never-saved) and [Scenario 4 — The broker is down](#scenario-4-the-broker-is-down-a-critical-dependency-has-failed): the outbox as a durable buffer, and containing a dead dependency.
10. [Chapter 51: Case 5 — Customers charged twice](#case-5-customers-charged-twice-the-batch-that-outlived-its-locks) and [Case 6 — 40,000 messages in the dead-letter queue](#case-6-40000-messages-in-the-dead-letter-queue-and-nobody-knew).

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
