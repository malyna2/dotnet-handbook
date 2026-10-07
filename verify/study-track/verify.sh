#!/usr/bin/env bash
# Maintainer self-check for the "Prove it" experiments in STUDY_TRACK.md:
#   1. every experiment printed in STUDY_TRACK.md is the Program.cs compiled here, at most 30 lines,
#      and every link in STUDY_TRACK.md resolves;
#   2. every experiment builds with warnings as errors;
#   3. a test runs each experiment as its own process, as a reader would, and checks both halves
#      of its output (the failure and the fix), so a change in either direction turns it red.
#
# PeekLock and SeekVsScan need the Service Bus emulator and SQL Server, which have EULAs:
#   ACCEPT_EULA=Y ./verify.sh       # everything (Docker)
#   ./verify.sh --no-docker         # only the experiments that need nothing but the .NET 10 SDK
set -euo pipefail
cd "$(dirname "$0")"

python3 check_track.py
dotnet build StudyTrack.slnx -c Release -nologo -v quiet -warnaserror

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

Tests/bin/Release/net10.0/StudyTrack.Tests -noColor ${filter[@]+"${filter[@]}"}
