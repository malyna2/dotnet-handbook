#!/usr/bin/env bash
# Break it: stop the consumer of a replication slot and watch PostgreSQL keep WAL for it.
# The slot remembers the last position Debezium confirmed; nothing after it can be recycled.
set -euo pipefail
cd "$(dirname "$0")"
trap 'docker compose down --volumes >/dev/null 2>&1 || true' EXIT
docker compose up -d --wait postgres kafka >/dev/null 2>&1
docker compose up -d connect >/dev/null 2>&1
./register-connector.sh >/dev/null

slot() { docker compose exec -T postgres psql -U lab -d shop -At -c \
  "SELECT active, pg_size_pretty(pg_wal_lsn_diff(pg_current_wal_lsn(), confirmed_flush_lsn)) FROM pg_replication_slots WHERE slot_name = 'outbox_slot';"; }
write() { docker compose exec -T postgres psql -U lab -d shop -q -c \
  "INSERT INTO outbox (id, aggregatetype, aggregateid, type, payload)
   SELECT gen_random_uuid(), 'Order', g::text, 'OrderPlaced', jsonb_build_object('OrderId', g, 'CustomerId', 1, 'Total', 10, 'Pad', repeat('x', 500))
   FROM generate_series(1, $1) g;"; }

echo "# Captured: $(date -u '+%Y-%m-%d %H:%M UTC'); postgres:17.11, debezium/connect:2.7.3.Final"
echo "slot (active | WAL behind) with Debezium running:   $(slot)"
docker compose stop connect >/dev/null 2>&1
write 200000
echo "after stopping Debezium and writing 200,000 rows:    $(slot)"
docker compose start connect >/dev/null 2>&1
for _ in $(seq 1 60); do
  sleep 5
  behind=$(slot)
  case "$behind" in t\|*bytes|t\|*kB) break;; esac
done
echo "after starting Debezium again and letting it catch up: $(slot)"
