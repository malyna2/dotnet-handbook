#!/usr/bin/env bash
# Registers (or re-registers) the Debezium connector once Kafka Connect answers.
set -euo pipefail
cd "$(dirname "$0")"
for _ in $(seq 1 60); do curl -fs localhost:8083/connectors >/dev/null && break; sleep 2; done
curl -fs -X DELETE localhost:8083/connectors/outbox-connector >/dev/null 2>&1 || true
curl -fs -X POST -H "Content-Type: application/json" --data @connector/outbox.json localhost:8083/connectors >/dev/null
for _ in $(seq 1 30); do
  state=$(curl -fs localhost:8083/connectors/outbox-connector/status | python3 -c "import json,sys; s=json.load(sys.stdin); print(s['tasks'][0]['state'] if s['tasks'] else 'NONE')" 2>/dev/null || echo NONE)
  [ "$state" = "RUNNING" ] && { echo "connector RUNNING"; exit 0; }
  sleep 2
done
echo "connector did not start:"; curl -s localhost:8083/connectors/outbox-connector/status; exit 1
