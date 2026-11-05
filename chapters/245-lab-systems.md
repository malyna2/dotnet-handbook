# Chapter 45: The Systems Lab: Brokers, Diagnostics, Azure and Incidents

Reading how Service Bus settles a message or how Debezium holds a replication slot moves you from *used it* to *understand it*. This chapter moves you one step further, to *built it and broke it*: seven labs, each with a goal, steps with checkable acceptance, the things you must be able to explain afterwards, and an artifact for your portfolio. Five of them come with a kit in `labs/` that starts with one command and has been run end to end; one uses [Chapter 19](#chapter-19-the-slow-query-lab-reading-execution-plans)'s kit; one is a drill on paper and on your machine.

> **🎯 Interview · P1.** Each lab turns a topic from the [Appendix: Interview Priority Map](#appendix-interview-priority-map) into something you did and can describe: brokers (P1-3, P1-14), async and memory diagnostics (P1-4, P1-6), SQL plans (P1-1), Azure (P2-10) and incidents (P2-12).

> **The portfolio rule.** Your solutions, test runs, counter output, plans and write-ups belong in **your own public portfolio repo**, not in this one. A story about a real employer's system stays private; these labs exist so that you have a public one to tell.

## Goal and the senior signal it trains

The goal is to be able to answer "how does it work, and what happens when it fails?" from experience rather than from a diagram. The signal an interviewer reads is *evidence*: "I stopped the connector, wrote 200,000 rows, and the slot held 152 MB of WAL" is a different answer from "replication slots can grow". Each lab ends with something you can show: a repository with a README, numbers with their environment, and a short write-up of what you broke and why.

## Time budget

| Lab | Topic | Kit | Time |
|---|---|---|---|
| 1 | Outbox, Debezium, Kafka, idempotent consumer, retries, DLQ | [`labs/45-outbox-debezium`](https://github.com/malyna2/dotnet-handbook/tree/main/labs/45-outbox-debezium) | 3 h |
| 2 | Service Bus: lock expiry, dead-lettering, sessions, duplicates | [`labs/45-service-bus`](https://github.com/malyna2/dotnet-handbook/tree/main/labs/45-service-bus) | 2 h |
| 3 | Thread pool starvation | [`labs/45-runtime-diagnostics`](https://github.com/malyna2/dotnet-handbook/tree/main/labs/45-runtime-diagnostics) | 1 h 30 min |
| 4 | A memory leak through a static event | same kit | 1 h 30 min |
| 5 | Execution plans: key lookup, covering index, parameter sniffing | [`labs/37-execution-plans`](https://github.com/malyna2/dotnet-handbook/tree/main/labs/37-execution-plans) | 2 h (rungs 3, 4 and 9) |
| 6 | Azure: App Service, SQL, Key Vault, managed identity, App Insights, budget, in Bicep | [`labs/45-azure-bicep`](https://github.com/malyna2/dotnet-handbook/tree/main/labs/45-azure-bicep) | 2 h 30 min |
| 7 | Incident drill: runbook and blameless post-mortem | [`labs/45-incident-drill`](https://github.com/malyna2/dotnet-handbook/tree/main/labs/45-incident-drill) | 1 h 30 min |

## Setup

Docker with Compose v2, the .NET 10 SDK and about 6 GB of free RAM cover Labs 1 to 5. Lab 2 runs on the Service Bus emulator and needs no Azure subscription; it asks you to accept the emulator's and SQL Server's EULAs with `ACCEPT_EULA=Y`. Lab 6 needs the Azure CLI, the Bicep CLI and a subscription where a few hours of a B1 App Service plan and a Basic SQL database are acceptable; its kit has a cleanup script and a budget alert. Every kit's README has a *Last verified* line with the environment it was run in, and its `reference-runs/` folder holds the raw output behind every number quoted below.

## Tasks

### Lab 1: Outbox, Debezium and an Idempotent Consumer

**Goal.** Build the whole path from a database transaction to a consumer's effect, and prove that a redelivered event has one effect.

**Steps.**

- **Level 1.** Start the stack, register the connector and post an order. Read the Kafka message with the console consumer and name where each part came from: the topic from `aggregatetype`, the key from `aggregateid`, the `id` header from the outbox row's `id`, the value from `payload`. *Acceptance:* you can point to each field in `connector/outbox.json` and `postgres/init.sql`.
- **Level 2.** Run the tests. `A_redelivered_event_awards_points_once` fails on the starter: the same event awards points twice. Fix `starter/LoyaltyHandler.cs`. *Acceptance:* all five tests pass, and your fix inserts the claim in the same transaction as the effect.
- **Level 3.** Run `./break-it-slot.sh`, then explain the three numbers it prints. Add an alert definition (in whatever tool you use) on the slot's lag in bytes. *Acceptance:* the write-up says what fills the disk, why `max_slot_wal_keep_size` trades one failure for another, and what the heartbeat setting is for.

**Explain afterwards.** Why the outbox removes the dual write; why Debezium is at-least-once and where duplicates come from; why the key is the order ID; what a tombstone is and why this connector turns them off; what `snapshot.mode=no_data` skips and when you would want `initial`; why permanent failures skip the retries; why backoff needs jitter. [Debezium Under the Hood](#debezium-under-the-hood) and [Idempotent Consumers](#idempotent-consumers) have the mechanisms.

**Artifact.** A repository with your handler, the test output before and after, the slot experiment's numbers with their environment, and a README diagram of the pipeline.

### Lab 2: Service Bus Settlement, Dead-Lettering and Sessions

**Goal.** See each Service Bus delivery mechanism happen, then fix a handler that retries a failure that can never succeed.

**Steps.**

- **Level 1.** Before running each experiment, write down what you expect: when the uncompleted message comes back and with what delivery count, what the dead-letter reason will be, in what order each session's messages arrive. Run the tests with `LAB_OBSERVATIONS` set. *Acceptance:* your predictions next to the observed values. On the reference run the message came back after 5.0 seconds (the lock duration) with delivery count 2, and a message abandoned three times under `MaxDeliveryCount = 3` was dead-lettered as `MaxDeliveryCountExceeded`.
- **Level 2.** Fix `starter/PaymentHandler.cs` so a declined card is charged once and dead-lettered with the reason `CardDeclined`. *Acceptance:* all six tests pass.
- **Level 3.** Add an experiment of your own: a topic with two subscriptions, one with a correlation filter and one with the default rule, or a queue with a short TTL and dead-lettering on expiry, and show which messages each receives. *Acceptance:* a test that passes and states the rule it demonstrates.

**Explain afterwards.** What the lock is and who holds it; what each settlement does; why at-least-once forces idempotency even with duplicate detection on (duplicate detection catches a resent *send*, not a redelivery); what sessions guarantee and what they cost; when you would use ReceiveAndDelete; Standard versus Premium. [Service Bus Under the Hood](#service-bus-under-the-hood).

**Artifact.** The test project with your experiment and a README table: experiment, prediction, observation, mechanism.

### Lab 3: Thread Pool Starvation

**Goal.** Recognise starvation from the outside, with the tools you'd have in production, before you look at the code.

**Steps.**

- **Level 1.** Start the starter build, open `dotnet-counters monitor` on it, and run the load generator (400 requests, 100 concurrent). *Acceptance:* you record p50 and p95, the maximum thread-pool queue length and the thread count. The reference run: p95 3978 ms, queue length up to 89, 58 pool threads afterwards.
- **Level 2.** Find the blocking call without reading `Endpoints.cs` first: take a `dotnet-dump` during the load and run `parallelstacks` ([The Thread Pool Under Load](#the-thread-pool-under-load)). Then fix it. *Acceptance:* the test passes, and the same load gives p95 within a few round trips of the 100 ms dependency (the reference: 154 ms, queue length 0, 5 threads).
- **Level 3.** Explain why raising `ThreadPool.SetMinThreads` makes the starter faster and why it is still the wrong fix. *Acceptance:* a paragraph with a measurement.

**Explain afterwards.** Why latency rises while CPU stays low; how the pool injects threads; why `.Result` holds a thread for the whole I/O; why async I/O needs no thread while it waits.

**Artifact.** A README with the before/after table, the counters output and the stack excerpt that showed the culprit.

### Lab 4: A Memory Leak Through a Static Event

**Goal.** Prove a leak with heap snapshots and find what roots the leaked objects.

**Steps.**

- **Level 1.** Send 500 `POST /watchers` to the starter build, then call `/stats`. *Acceptance:* the subscriber count and the heap after a full collection, before and after.
- **Level 2.** Take two `dotnet-gcdump` snapshots, one before and one after another 500 requests, open them in Visual Studio, PerfView or dotMemory, and find the retention path from a leaked `PriceWatcher` to its root. *Acceptance:* you name the root (the static event's delegate list). The reference report: 34,473,840 bytes of GC heap and 501 large `Byte[]` objects for the starter, 1,442,726 bytes and one for the fix.
- **Level 3.** Fix it, then list two other ways the same leak appears in real code (a long-lived service subscribing a scoped object, a static cache without eviction, a timer whose callback captures `this`). *Acceptance:* the test passes and the list has the mechanism for each.

**Explain afterwards.** Why the GC can't collect an object a static field reaches; why `GC.Collect()` doesn't help; what a gcdump contains and doesn't (types and references, not values); the step-by-step plan for an OOM in production ([Scenario 7](#scenario-7-the-slow-leak-memory-keeps-growing-until-the-pod-is-oom-killed)).

**Artifact.** The two snapshots' comparison, the retention path and your fix.

### Lab 5: Execution Plans

**Goal.** Read a plan, remove a key lookup with a covering index, and catch a sniffed parameter.

**Steps.** Use [Chapter 19](#chapter-19-the-slow-query-lab-reading-execution-plans) and its kit: the rungs on missing and covering indexes, and rung 9 (parameter sniffing, on the SQL Server track). *Acceptance:* the chapter's criteria for those rungs, plus your own one-paragraph explanation of [Parameter Sniffing](#parameter-sniffing) using the plan you captured.

**Explain afterwards.** Seek versus scan; why a key lookup multiplies with rows; what `INCLUDE` adds and what it costs on writes; estimated versus actual rows; the fixes for a sniffed plan and their trade-offs.

**Artifact.** The before/after plans and logical reads, as Chapter 19 describes.

### Lab 6: Azure Infrastructure as Code with a Budget

**Goal.** Deploy the typical .NET shape on Azure with no secret anywhere, observe it, and tear it down.

**Steps.**

- **Level 1.** Read `infra/main.bicep` and, before deploying, write down which identity reads the Key Vault secret, which role it needs, and how the app signs in to SQL. Deploy with `deploy.sh`. *Acceptance:* the Key Vault reference shows as resolved in the app's configuration, and the deployment created no SQL login.
- **Level 2.** Run `sql/grant-app.sql`, deploy any .NET 10 app with a `/health` endpoint and a query to the database, and find its requests and the SQL dependency calls in Application Insights. *Acceptance:* a screenshot or KQL query result with a request and its SQL dependency under one operation ID.
- **Level 3.** Run `cleanup.sh`, and the next day check Cost Management for what the lab cost. *Acceptance:* the resource group is gone, the vault is purged, and you can state the cost.

**Explain afterwards.** Managed identity versus a service principal with a secret; Key Vault RBAC versus access policies; why Entra-only SQL authentication; what the budget does and doesn't do (it alerts; it never stops anything); why the firewall rule here is a lab shortcut and what a private endpoint changes.

**Artifact.** Your Bicep (with any changes), the deployment outputs, the KQL query and the cost.

The kit's Bicep was compiled and linted; the deployment was not run in the maintainer's environment, so treat its first run as part of the lab.

### Lab 7: Incident Drill

**Goal.** Run an incident in the right order, and write the two documents a team keeps afterwards.

**Steps.** Follow [the kit's README](https://github.com/malyna2/dotnet-handbook/tree/main/labs/45-incident-drill): deploy Lab 3's starter build as "the bad release", put production-like load on it, and run the incident with the runbook template, keeping a timeline. Roll back (start the solution build) before you know the cause, confirm recovery with the same load, then find the cause and write the post-mortem. *Acceptance:* the kit's Levels 1 to 3: timestamps for detection, communication, mitigation and recovery, with mitigation before root cause; a runbook someone else could follow; a blameless post-mortem whose action items remove the class of failure, each with an owner and a date.

**Explain afterwards.** Why mitigation comes before diagnosis; what a good first status update contains; why "the developer forgot" is never a root cause; how an action item differs from a promise to be careful.

**Artifact.** The runbook, the timeline and the post-mortem.

## Break it

- **Lab 1:** stop Debezium for a "weekend" (`break-it-slot.sh`); delete the connector without dropping the slot and watch the WAL grow with nobody reading it; restart the consumer group from the beginning and confirm your handler holds.
- **Lab 2:** raise `PrefetchCount` to 100 with a slow handler and watch redeliveries appear; set a 10-second TTL on a queue without dead-lettering on expiry and stop the consumer.
- **Lab 3:** put `ConfigureAwait(false)` on the blocking call and observe that it changes nothing here (ASP.NET Core has no synchronization context; the thread is blocked either way).
- **Lab 4:** fix the leak by making the event non-static but keep the subscriber in a singleton service, and find the same leak again.

## Evidence to keep

In your own public portfolio repo: one folder per lab with its README (goal, what you did, the numbers with their environment header, what you'd explain in an interview), the test output before and after, and for Lab 7 the three documents. Link each folder from your story bank ([Chapter 37](#chapter-37-the-story-bank-evidence-portfolio)).

## Interview hook

The story to tell from these labs, in STAR, with your own numbers:

```text
Situation: I had used Service Bus and Debezium on projects where the infrastructure already existed,
           and I wanted to understand how they fail, not only how to call them.
Task:      Build the outbox pipeline end to end and make the consumer correct under redelivery.
Action:    I built [PostgreSQL → Debezium → Kafka → a .NET consumer], wrote a test that replays the topic,
           saw [points awarded twice], and fixed it with a unique claim in the same transaction as the effect.
           Then I stopped Debezium and wrote [n] rows: the replication slot held [size] of WAL.
Result:    The consumer passes the redelivery test; I can explain [the slot / settlement / starvation]
           from what I measured. The repository is [link].
```

Three follow-ups to prepare: *"Why not just check Redis before processing?"* (the check and the effect can't commit together), *"What would you monitor in production?"* (slot lag in bytes, consumer-group lag, dead-letter depth, free disk on the database), and *"What did you not build that a real system needs?"* (schema registry, connector high availability, alert routing, the DLQ resubmission tool).

## Hints and answers

<details>
<summary>Lab 1: the idempotent handler</summary>

Insert the event ID into `processed_messages` first, with `ON CONFLICT (event_id) DO NOTHING`, inside the transaction that writes the points; if it inserted nothing, return without the effect. A concurrent copy of the same event waits on the first copy's uncommitted row and then sees the conflict. The kit's `solution/LoyaltyHandler.cs` is the reference.

</details>

<details>
<summary>Lab 2: the declined card</summary>

Catch the business exception and call `DeadLetterMessageAsync(message, "CardDeclined", e.Message)`; let transient exceptions escape so the processor abandons the message and Service Bus retries it.

</details>

<details>
<summary>Lab 3: why SetMinThreads is the wrong fix</summary>

It lets the pool create threads without its injection delay, so the blocked requests get threads sooner and the symptom shrinks. Each request still holds a thread for the whole I/O, so memory and context switching grow with load, and the next, bigger spike starves the pool again. Removing the block removes the need for the threads.

</details>

<details>
<summary>Lab 4: the root</summary>

The static `PriceChanged` event holds a delegate list, each delegate holds its target, the `PriceWatcher`, and each watcher holds its 64 KB buffer. A static field is a GC root, so everything it reaches stays alive. `Dispose` removes the delegate, so the watcher becomes unreachable at the end of the request.

</details>
