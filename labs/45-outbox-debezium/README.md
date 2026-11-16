# Lab kit — Chapter 46, Lab 1: Outbox, Debezium and an Idempotent Consumer

An order API writes the order and an `OrderPlaced` outbox row in one PostgreSQL transaction. Debezium reads the outbox table from the write-ahead log through a replication slot, its Outbox Event Router turns each row into a Kafka message keyed by the order, and a .NET consumer awards loyalty points: idempotently, with retries (exponential backoff and jitter) for transient failures and a dead-letter topic for the rest.

> **The portfolio rule.** Don't push your fix here. Your consumer, your test runs and your write-up go in **your own public portfolio repo**. Stories about a real employer's system stay private.

**Last verified:** 2026-11-05 — Ubuntu 24.04 container, 4 vCPU, 15 GB RAM, Docker 29.9.2, `postgres:17.11`, `apache/kafka:3.9.1`, `debezium/connect:2.7.3.Final`, .NET SDK 10.0.114, Confluent.Kafka 2.15.1, Npgsql 10.0.3. `verify.sh`: the starter fails exactly the redelivery test, the solution passes 5 of 5 ([`reference-runs/`](reference-runs/)).

Debezium 2.7 is the last line published on Docker Hub; 3.x images are on `quay.io/debezium/connect`. The parts this lab uses (the PostgreSQL connector, `pgoutput`, the Outbox Event Router) work the same way in 3.x.

## What you need

Docker with Compose v2, .NET 10 SDK, about 3 GB of free RAM, ports 54345, 9092 and 8083 free.

## Quick start

```bash
docker compose up -d --wait postgres kafka
docker compose up -d connect
./register-connector.sh                                   # creates the replication slot and the publication

dotnet run --project src/Outbox.Api --urls http://localhost:5090 &
curl -s -X POST localhost:5090/orders -H 'Content-Type: application/json' -d '{"orderId":1,"customerId":7,"total":42.5}'

# See what Debezium produced: key = the order id, headers = event id and type, value = the payload.
docker compose exec kafka /opt/kafka/bin/kafka-console-consumer.sh --bootstrap-server kafka:19092 \
  --topic outbox.event.Order --from-beginning --property print.key=true --property print.headers=true

dotnet run --project src/Outbox.Worker -- loyalty --from-beginning   # the consumer; Ctrl+C to stop
```

## The exercise

`starter/LoyaltyHandler.cs` is the only file you change. It awards points correctly the first time and again on every redelivery. Run the tests (they need the stack above):

```bash
dotnet test --project tests/Outbox.Tests                         # your code (starter by default)
dotnet test --project tests/Outbox.Tests -p:LabVariant=solution  # the reference
```

| Test | What it proves |
|---|---|
| `The_outbox_row_reaches_Kafka_keyed_by_the_order` | the pipeline: one transaction in, one keyed message out |
| `A_redelivered_event_awards_points_once` | **the exercise**: fails on the starter |
| `A_poison_event_goes_to_the_dead_letter_topic_and_awards_nothing` | permanent failures skip the retries |
| `A_transient_failure_is_retried_and_succeeds_once` | two failures, then success, one effect |
| `Backoff_grows_exponentially_and_stays_under_the_cap` | the jitter stays inside its envelope |

## Break it

`./break-it-slot.sh` stops Debezium, writes 200,000 outbox rows and shows the replication slot holding the WAL for them (152 MB on the reference run), then starts Debezium and shows it catch up. It is the failure behind "the database disk filled up over the weekend".

## Files

| Path | What it is |
|---|---|
| `docker-compose.yml` | PostgreSQL with `wal_level=logical`, Kafka in KRaft mode, Kafka Connect with Debezium |
| `postgres/init.sql` | `orders`, `outbox` (the router's default column names), `processed_messages`, `loyalty_points` |
| `connector/outbox.json` | the connector: `pgoutput`, a named slot, `snapshot.mode=no_data`, the Outbox Event Router |
| `src/Outbox.Core` | `OrderStore` (the one-transaction write), `LoyaltyConsumer` (at-least-once loop, retries, DLQ), `Backoff` |
| `starter/`, `solution/` | `LoyaltyHandler.cs`, yours and the reference |
| `verify.sh`, `break-it-slot.sh` | maintainer checks; their output is in `reference-runs/` |
