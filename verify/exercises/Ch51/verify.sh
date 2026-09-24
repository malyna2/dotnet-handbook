#!/usr/bin/env bash
# Maintainer self-check for Chapter 51's "Find the bug" samples: starts Azurite and the
# Service Bus emulator, then runs tests that show each defect on the buggy code and its
# absence on the fix. Requires Docker and the .NET 10 SDK.
#
# The Service Bus emulator needs SQL Server; both have EULAs. Accept them explicitly:
#   ACCEPT_EULA=Y ./verify.sh
set -euo pipefail
cd "$(dirname "$0")"

if [[ "${ACCEPT_EULA:-}" != "Y" ]]; then
  echo "Set ACCEPT_EULA=Y to accept the Service Bus emulator and SQL Server EULAs." >&2
  exit 2
fi
export ACCEPT_EULA

cleanup() { docker compose down --volumes >/dev/null 2>&1 || true; }
trap cleanup EXIT

docker compose up -d
echo "Waiting for the Service Bus emulator..."
for _ in $(seq 1 60); do
  if curl -fs http://localhost:5300/health >/dev/null; then break; fi
  sleep 3
done
curl -fs http://localhost:5300/health >/dev/null || { docker compose logs servicebus | tail -30; exit 1; }

dotnet test
