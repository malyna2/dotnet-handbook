# Part 1 · Module 4: Messaging and Long-Running Work

> **What this module makes you able to do.** Put a queue between two parts of a system, and write a handler that stays correct when a message arrives twice, late or out of order. Move work that takes longer than a request into a worker behind `202 Accepted`, without losing or duplicating it.

**Time:** reading ≈ 40 min; hands-on ≈ 1 h 25 min — the experiments 25 min, the chapter exercise 15, the questions 15, the check at work 30.

## Covers

- why a queue at all: temporal decoupling, load levelling, competing consumers, and what they cost;
- peek-lock versus receive-and-delete, the lock as a lease (1 minute by default, 5 at most), redelivery, `MaxDeliveryCount` and the dead-letter queue;
- why every handler sees duplicates, why check-then-act deduplication fails, and the claim-first handler;
- why a FIFO queue doesn't give ordered processing, and what sessions do about it;
- the dual write behind "saved but never published", and the outbox idea;
- long-running work over HTTP: `202 Accepted`, a status URL, `Retry-After`, a queue, a `BackgroundService` worker, and an idempotency key on the `POST`.

## The mechanism to explain without notes

**A queue hands out leases, not messages: a message is gone only when a handler settles it while its lock still holds. Every other path is a redelivery, so the handler must make its effect happen once, by claiming the message's ID atomically with the effect.**

A queue lets a producer and its consumers be up, and fast, at different times. The producer's send succeeds while the consumer is down (temporal decoupling), and a burst waits in the queue while consumers drain it at their own rate (load levelling). Several instances reading one queue are *competing consumers*: each message goes to one of them.

In peek-lock mode, receiving a message locks it for the entity's lock duration: 1 minute by default, 5 at most. `Complete` inside the lock deletes the message. Anything else — the handler throws and the message is abandoned, the process dies, the settlement is lost, the lock expires — makes it visible again with `DeliveryCount + 1`. Past `MaxDeliveryCount` (10 by default) it moves to the dead-letter queue, and stays there until someone acts. `ReceiveAndDelete` removes the message as it is delivered: no duplicates, and a crash loses it. Peek-lock delivery is therefore at-least-once by construction, and each trap in *Covers* follows from that:

- **Every handler sees duplicates.** The effect must be idempotent: insert the message ID under a unique key, in the same transaction as the effect. Checking first and recording afterwards fails, because two copies can both pass the check before either records the ID.
- **Order is lost.** Competing consumers, concurrency above 1, retries and prefetch make messages finish in a different order from the one they were sent in. Sessions restore order per key; a version number makes order irrelevant.
- **The producer has the mirror-image problem.** Committing to the database and publishing to the broker are two operations, and a crash between them loses one of them. The outbox writes the message into the same transaction as the data, and a relay publishes it after the commit: at-least-once again.
- **Long work doesn't belong in a request.** Record a job, enqueue it, answer `202 Accepted` with a status URL and `Retry-After`, and let a `BackgroundService` worker run it while the client polls. An idempotency key on the `POST` makes a retried request find the job it already created.

> **Pay attention.** **Duplicate detection is not idempotency.** Service Bus duplicate detection drops a newly *sent* message whose `MessageId` it has already seen within its window: a guard against a producer that retries a send. A redelivery after an expired lock or a crash is the *same* message delivered again, which duplicate detection never sees. Keep it for producers, and make every handler idempotent anyway.

## Read (≈ 40 min)

1. [Chapter 9: Why Messaging at All?](#why-messaging-at-all): temporal coupling, and what a queue costs.
2. [Chapter 9: Competing Consumers](#competing-consumers), [Dead-Letter Queues (DLQ)](#dead-letter-queues-dlq) and [Message Ordering](#message-ordering).
3. [Chapter 9: Delivery Guarantees](#delivery-guarantees), including *Why Exactly-Once Is (Almost) a Myth* and *Deduplication*.
4. [Chapter 9: Idempotent Consumers](#idempotent-consumers) and [The Transactional Outbox](#the-transactional-outbox).
5. [Chapter 50: Service Bus](#service-bus): the peek-lock diagram, the lock duration, sessions, duplicate detection and the processor defaults.
6. [Chapter 51: Case 5 — Customers charged twice](#case-5-customers-charged-twice-the-batch-that-outlived-its-locks) and [Case 6 — 40,000 messages in the dead-letter queue](#case-6-40000-messages-in-the-dead-letter-queue-and-nobody-knew).
7. [Chapter 3: Idempotency Keys: Making POST Retry-Safe](#idempotency-keys-making-post-retry-safe): why the claim goes in *before* the effect, under a unique index, in the same transaction. The same reasoning applies to a message ID.
8. [Chapter 51: Case 11 — Large uploads fail at almost exactly four minutes](#case-11-large-uploads-fail-at-almost-exactly-four-minutes): the 230-second front-end limit, and why long work leaves the request.
9. [Chapter 22: `IHostedService` and `BackgroundService`](#ihostedservice-and-backgroundservice) and [Async Request-Reply: 202, a Status Resource, and Retry-After](#async-request-reply-202-a-status-resource-and-retry-after): the worker, and the HTTP contract around it end to end.

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
