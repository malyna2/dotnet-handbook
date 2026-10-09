#!/usr/bin/env bash
# Regenerates reference-run.txt: three runs of the measurement, with the environment header.
set -euo pipefail
cd "$(dirname "$0")"
dotnet build -c Release -nologo -v q >/dev/null
{
  echo "# Environment: $(grep -m1 'model name' /proc/cpuinfo | cut -d: -f2 | xargs), $(nproc) vCPU, $(free -g | awk '/Mem:/{print $2}') GB RAM, $(. /etc/os-release; echo "$PRETTY_NAME"), .NET runtime $(dotnet --list-runtimes | awk '/NETCore.App/{v=$2} END{print v}'), SDK $(dotnet --version)"
  echo "# Captured: $(date -u '+%Y-%m-%d %H:%M UTC') by capture.sh; Release build, no debugger; 20,000 iterations per cell after warm-up"
  for run in 1 2 3; do echo; echo "## Run $run"; dotnet bin/Release/net10.0/ExceptionCost.dll; done
} > reference-run.txt
cat reference-run.txt
