#!/usr/bin/env bash
# Regenerates reference-run.txt: the load-test numbers Chapter 8's Find-the-bug answer quotes,
# with the environment they were measured on.
set -euo pipefail
cd "$(dirname "$0")"

dotnet build -c Release -nologo -v quiet -warnaserror
{
  echo "# Environment: $(grep -m1 'model name' /proc/cpuinfo | cut -d: -f2 | xargs), $(nproc) vCPU," \
       "$(awk '/MemTotal/ { printf "%.0f GB", $2 / 1048576 }' /proc/meminfo) RAM," \
       "$(sed -n 's/^PRETTY_NAME="\(.*\)"$/\1/p' /etc/os-release), .NET SDK $(dotnet --version)"
  echo "# Captured: $(date -u '+%Y-%m-%d %H:%M UTC') by capture.sh"
  echo "# Each load: 5 x vCPU concurrent requests; per request 200 ms of report I/O, then 4 emails of 200 ms each."
  echo
  bin/Release/net10.0/Ch08.Exercises.Tests -noColor -showLiveOutput -method "*StarvationTests*" 2>&1 \
    | grep -E 'buggy:|fixed:|Total:' | grep -v '\[FAIL\]' | sed 's/^ *//'
} > reference-run.txt
cat reference-run.txt
