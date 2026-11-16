# The Practice Gym — lab kits

Starter kits for the lab chapters of *The Middle → Senior .NET Developer Handbook*. The folders keep the numbers they were created with (links point at them); the table names the chapter each kit belongs to. Read the chapter first, then work here.

| Kit | Chapter | What you produce |
|---|---|---|
| [`36-evidence-portfolio/`](36-evidence-portfolio/) | Chapter 37: The Story Bank & Evidence Portfolio | A private story bank, a weekly brag doc, a public portfolio repo, and scored mock interviews |
| [`37-execution-plans/`](37-execution-plans/) | Chapter 19: The Slow-Query Lab — Reading Execution Plans | Nine slow EF Core queries fixed and proven: a before/after results table with plans, fix costs, and an optional SQL Server comparison |
| [`45-outbox-debezium/`](45-outbox-debezium/) | Chapter 45, Lab 1: Outbox, Debezium and an Idempotent Consumer | An outbox pipeline to Kafka with an idempotent consumer, retries with jitter and a DLQ, and a replication-slot experiment |
| [`45-service-bus/`](45-service-bus/) | Chapter 45, Lab 2: Service Bus Settlement, Dead-Lettering and Sessions | Five emulator experiments with predictions and observations, and a handler that dead-letters permanent failures |
| [`45-runtime-diagnostics/`](45-runtime-diagnostics/) | Chapter 45, Labs 3 and 4: Thread Pool Starvation and a Memory Leak | Before/after counters and heap snapshots, and the fixed endpoints |
| [`45-azure-bicep/`](45-azure-bicep/) | Chapter 45, Lab 6: Azure Infrastructure as Code with a Budget | A deployed and cleaned-up App Service + Azure SQL + Key Vault + managed identity stack, with its cost |
| [`45-incident-drill/`](45-incident-drill/) | Chapter 45, Lab 7: Incident Drill | A runbook, an incident timeline and a blameless post-mortem |

More kits land here as their chapters are written.

## The portfolio rule

**Your solutions do not belong in this repository.** Your query plans, test runs, post-mortems, review comments, design docs and rewritten prose go in **your own public portfolio repo**, where an interviewer can read them. That is the whole point of doing the labs: leaving evidence behind.

Keep one distinction: **lab artifacts are public; stories about a real employer are private.** Anything that names a customer, a colleague, an internal system or a number your employer has not published stays in a private repo or a private notes folder.

## Prerequisites

The code labs need:

- **Docker** with Compose v2 (`docker compose version`).
- **.NET 10 SDK** (`dotnet --version` → `10.0.x`). .NET 10 is the current LTS; .NET 8 and 9 leave support on 10 November 2026.
- About 8 GB of free RAM and 20 GB of free disk for the database labs.

The writing and interview kits need nothing but a text editor and, for the mock interviews, any capable AI chat assistant.

## Layout of a code kit

```
<NN-slug>/
  README.md          what to run, and when it was last verified
  docker-compose.yml pinned image tags
  starter/           what you work on — compiles; its tests fail until you fix it
  solution/          a reference solution — don't open it until you are done
  tests/             the acceptance tests you must turn green
  verify.sh          the maintainers' self-check (starter fails as intended, solution passes)
  reference-runs/    raw output behind every number quoted in the chapter
```
