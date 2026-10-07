#!/usr/bin/env bash
# Maintainer self-check for the learning path (Part 1 and Part 2, chapters 70-98):
#   1. every program printed on a path page is the Program.cs compiled here, at most 30 lines; every
#      `## Read (≈ …)` time matches the sections it links; every experiment here is printed once;
#   2. every in-book link resolves the way the reader app resolves it (verify/check_links.py);
#   3. every experiment builds with warnings as errors;
#   4. a test runs each experiment as its own process, as a reader would, and checks both halves
#      of its output (the failure and the fix), so a change in either direction turns it red.
#
# Experiments marked `requires-docker` need SQL Server and the Service Bus emulator, which have EULAs:
#   ACCEPT_EULA=Y ./verify.sh       # everything (Docker)
#   ./verify.sh --no-docker         # only the experiments that need nothing but the .NET 10 SDK
set -euo pipefail
cd "$(dirname "$0")"

python3 check_path.py
python3 ../check_links.py
dotnet build Tests/LearningPath.Tests.csproj -c Release -nologo -v quiet -warnaserror

filter=()
if [[ "${1:-}" == "--no-docker" ]]; then
  filter=(-trait- "Requires=Docker")
else
  if [[ "${ACCEPT_EULA:-}" != "Y" ]]; then
    echo "Set ACCEPT_EULA=Y to accept the Service Bus emulator and SQL Server EULAs, or pass --no-docker." >&2
    exit 2
  fi
  export ACCEPT_EULA
  cleanup() { docker compose down --volumes >/dev/null 2>&1 || true; }
  trap cleanup EXIT
  docker compose up -d
  echo "Waiting for the Service Bus emulator (it starts after SQL Server)..."
  for _ in $(seq 1 60); do
    if curl -fs http://localhost:5300/health >/dev/null; then break; fi
    sleep 3
  done
  curl -fs http://localhost:5300/health >/dev/null || { docker compose logs servicebus | tail -30; exit 1; }
fi

Tests/bin/Release/net10.0/LearningPath.Tests -noColor ${filter[@]+"${filter[@]}"}
