# Lab kit — Chapter 45, Lab 2: Service Bus Settlement, Dead-Lettering and Sessions

Five experiments and one exercise against the **Service Bus emulator**, no Azure subscription needed: a message that is never completed comes back after its lock expires; abandoning it `MaxDeliveryCount` times dead-letters it; sessions keep each order's messages in sequence while sessions run in parallel; duplicate detection drops a resent `MessageId`; scheduled and deferred messages. The exercise: a declined card must be charged once and dead-lettered with a reason, not retried ten times.

> **The portfolio rule.** Your handler, your runs and your notes go in **your own public portfolio repo**, not here.

**Last verified:** 2026-11-05 — Ubuntu 24.04 container, 4 vCPU, Docker 29.9.2, `servicebus-emulator:2.0.1` on SQL Server 2022 CU27, Azure.Messaging.ServiceBus 7.20.2, .NET SDK 10.0.114. `ACCEPT_EULA=Y ./verify.sh`: the starter fails only the exercise, the solution passes 6 of 6 ([`reference-runs/`](reference-runs/), with the observed values in `observations.txt`).

The emulator is for development and tests. It has no Premium features and no SLA, and its quotas are smaller than the service's; check anything you measure here against the real service before quoting it.

## Run it

```bash
export ACCEPT_EULA=Y                                       # the emulator's and SQL Server's EULAs
docker compose up -d
curl -fs http://localhost:5300/health                      # wait until this answers
dotnet test --project tests/ServiceBusLab.Tests            # your code (starter)
dotnet test --project tests/ServiceBusLab.Tests -p:LabVariant=solution
```

Lock durations are 5 seconds instead of the default minute, so every experiment runs in seconds. Read each test before running it and predict what it prints; `LAB_OBSERVATIONS=$PWD/obs.txt dotnet test …` writes the observed values (redelivery time, delivery counts, dead-letter reasons, per-session order) to a file.

`starter/PaymentHandler.cs` is the file you change.
