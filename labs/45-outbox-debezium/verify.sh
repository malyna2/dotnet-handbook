#!/usr/bin/env bash
# Maintainer self-check: starts the stack, registers the connector, and proves both directions:
# the starter fails exactly the idempotency test, the solution passes everything.
set -euo pipefail
cd "$(dirname "$0")"
OUT=reference-runs
trap 'docker compose down --volumes >/dev/null 2>&1 || true' EXIT

docker compose up -d --wait postgres kafka
docker compose up -d connect
for t in outbox.event.Order outbox.event.Order.dlq; do
  docker compose exec -T kafka /opt/kafka/bin/kafka-topics.sh --bootstrap-server kafka:19092 \
    --create --if-not-exists --topic "$t" --partitions 3 --replication-factor 1 >/dev/null
done
./register-connector.sh

header() {
  echo "# Environment: $(grep -m1 'model name' /proc/cpuinfo | cut -d: -f2 | xargs), $(nproc) vCPU, $(free -g | awk '/Mem:/ {print $2}') GB RAM, $(docker --version | cut -d' ' -f3 | tr -d ,)"
  echo "# Stack: postgres:17.11, apache/kafka:3.9.1, debezium/connect:2.7.3.Final; .NET SDK $(dotnet --version)"
  echo "# Captured: $(date -u '+%Y-%m-%d %H:%M UTC') by verify.sh"
  echo
}

status=0
# The API: one POST, one transaction.
dotnet build src/Outbox.Api -nologo -v q >/dev/null
dotnet src/Outbox.Api/bin/Debug/net10.0/Outbox.Api.dll --urls http://localhost:5090 >/dev/null 2>&1 &
api=$!
for _ in $(seq 1 50); do curl -fs -o /dev/null -X POST localhost:5090/orders -H 'Content-Type: application/json' \
  -d '{"orderId":1,"customerId":7,"total":42.5}' -w "%{http_code}" > "$OUT/api-post.txt" 2>/dev/null && break; sleep 0.3; done
kill $api; wait $api 2>/dev/null || true
grep -q 201 "$OUT/api-post.txt" && echo "ok   POST /orders returns 201" || { echo "FAIL POST /orders"; status=1; }
rm -f "$OUT/observations.txt"
{ header; dotnet test --project tests/Outbox.Tests -p:LabVariant=starter 2>&1 || true; } > "$OUT/starter.txt"
{ header; LAB_OBSERVATIONS="$PWD/$OUT/observations.txt" dotnet test --project tests/Outbox.Tests -p:LabVariant=solution 2>&1 || true; } > "$OUT/solution.txt"

grep -q "failed Outbox.Tests.OutboxTests.A_redelivered_event_awards_points_once" "$OUT/starter.txt" \
  && echo "ok   starter fails the redelivery test" || { echo "FAIL starter should fail the redelivery test"; status=1; }
[ "$(grep -c '^failed ' "$OUT/starter.txt")" = "1" ] \
  && echo "ok   starter fails nothing else" || { echo "FAIL starter fails other tests too"; status=1; }
grep -q "failed: 0\|succeeded: 5" "$OUT/solution.txt" && ! grep -q '^failed ' "$OUT/solution.txt" \
  && echo "ok   solution passes every test" || { echo "FAIL solution"; status=1; }

# What Debezium put on the wire, and the replication slot it holds.
{ header
  echo "## One event as the consumer sees it (key | headers | value)"
  docker compose exec -T kafka /opt/kafka/bin/kafka-console-consumer.sh --bootstrap-server kafka:19092 \
    --topic outbox.event.Order --from-beginning --max-messages 1 \
    --property print.key=true --property print.headers=true --property key.separator=' | ' 2>/dev/null
  echo; echo "## The replication slot"
  docker compose exec -T postgres psql -U lab -d shop -c \
    "SELECT slot_name, plugin, slot_type, active, pg_size_pretty(pg_wal_lsn_diff(pg_current_wal_lsn(), confirmed_flush_lsn)) AS behind FROM pg_replication_slots;"
} > "$OUT/wire-and-slot.txt"
exit $status
