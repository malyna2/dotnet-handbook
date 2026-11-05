#!/usr/bin/env bash
# Maintainer self-check: starts the emulator, runs the five experiments and the exercise on both
# variants. Expected: the starter fails only the exercise; the solution passes everything.
#   ACCEPT_EULA=Y ./verify.sh
set -euo pipefail
cd "$(dirname "$0")"
[[ "${ACCEPT_EULA:-}" == "Y" ]] || { echo "Set ACCEPT_EULA=Y to accept the Service Bus emulator and SQL Server EULAs." >&2; exit 2; }
export ACCEPT_EULA
OUT=reference-runs
trap 'docker compose down --volumes >/dev/null 2>&1 || true' EXIT

docker compose up -d
for _ in $(seq 1 60); do curl -fs http://localhost:5300/health >/dev/null && break; sleep 3; done
curl -fs http://localhost:5300/health >/dev/null || { docker compose logs servicebus | tail -30; exit 1; }

header() {
  echo "# Environment: $(grep -m1 'model name' /proc/cpuinfo | cut -d: -f2 | xargs), $(nproc) vCPU, $(free -g | awk '/Mem:/ {print $2}') GB RAM, Docker $(docker --version | cut -d' ' -f3 | tr -d ,)"
  echo "# Stack: servicebus-emulator:2.0.1 on SQL Server 2022 CU27; Azure.Messaging.ServiceBus 7.20.2; .NET SDK $(dotnet --version)"
  echo "# Captured: $(date -u '+%Y-%m-%d %H:%M UTC') by verify.sh (lock duration 5 s, scaled down from the 1-minute default)"
  echo
}
status=0
rm -f "$OUT/observations.txt"
{ header; dotnet test --project tests/ServiceBusLab.Tests -p:LabVariant=starter 2>&1 || true; } > "$OUT/starter.txt"
{ header; LAB_OBSERVATIONS="$PWD/$OUT/observations.txt" dotnet test --project tests/ServiceBusLab.Tests -p:LabVariant=solution 2>&1 || true; } > "$OUT/solution.txt"

grep -q "^failed .*A_declined_card_is_charged_once_and_dead_lettered_with_a_reason" "$OUT/starter.txt" \
  && [ "$(grep -c '^failed ' "$OUT/starter.txt")" = "1" ] \
  && echo "ok   starter fails exactly the exercise" || { echo "FAIL starter"; status=1; }
! grep -q '^failed ' "$OUT/solution.txt" && grep -q "succeeded: 6" "$OUT/solution.txt" \
  && echo "ok   solution passes all six" || { echo "FAIL solution"; status=1; }
exit $status
