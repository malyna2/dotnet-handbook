#!/usr/bin/env bash
# Maintainer self-check: (1) the tests show both bugs on the starter and their absence on the solution;
# (2) the real app, under load, shows each bug in the tools a reader would use in production.
set -euo pipefail
cd "$(dirname "$0")"
OUT=reference-runs
dotnet tool restore >/dev/null
PORT=5080

header() {
  echo "# Environment: $(grep -m1 'model name' /proc/cpuinfo | cut -d: -f2 | xargs), $(nproc) vCPU, $(free -g | awk '/Mem:/ {print $2}') GB RAM"
  echo "# .NET SDK $(dotnet --version); dotnet-counters and dotnet-gcdump 10.0.745401; Release build"
  echo "# Captured: $(date -u '+%Y-%m-%d %H:%M UTC') by verify.sh"
  echo
}

status=0
{ header; dotnet test --project tests/DiagLab.Tests -p:LabVariant=starter 2>&1 || true; } > "$OUT/tests-starter.txt"
{ header; dotnet test --project tests/DiagLab.Tests -p:LabVariant=solution 2>&1 || true; } > "$OUT/tests-solution.txt"
[ "$(grep -c '^failed ' "$OUT/tests-starter.txt")" = "2" ] && echo "ok   starter fails both tests" || { echo "FAIL starter"; status=1; }
! grep -q '^failed ' "$OUT/tests-solution.txt" && echo "ok   solution passes both" || { echo "FAIL solution"; status=1; }

dotnet build src/DiagLab.Load -c Release -nologo -v q >/dev/null
for variant in starter solution; do
  dotnet build src/DiagLab.Api -c Release -p:LabVariant=$variant -nologo -v q >/dev/null
  dotnet src/DiagLab.Api/bin/Release/net10.0/DiagLab.Api.dll --urls http://localhost:$PORT >/dev/null 2>&1 &
  pid=$!
  for _ in $(seq 1 50); do curl -fs localhost:$PORT/stats >/dev/null && break; sleep 0.2; done
  {
    header
    echo "## Variant: $variant (pid $pid)"
    echo; echo "## Load: 400 GET /quote, 100 concurrent"
    dotnet tool run dotnet-counters collect -p $pid --counters System.Runtime --refresh-interval 1 --format csv \
      -o "$OUT/counters-$variant.csv" --duration 00:00:25 >/dev/null 2>&1 &
    cpid=$!
    sleep 3
    dotnet src/DiagLab.Load/bin/Release/net10.0/DiagLab.Load.dll "http://localhost:$PORT/quote?sku=abc" 400 100
    wait $cpid || true
    echo; echo "## Thread pool, sampled every second during the load (max of each instrument)"
    python3 - "$OUT/counters-$variant.csv" <<'PY'
import csv, sys
rows = list(csv.reader(open(sys.argv[1])))
peak = {}
for r in rows[1:]:
    if len(r) < 5: continue
    name = r[2]
    if "thread_pool" in name or "cpu" in name.lower() or "gc.heap" in name and "last_collection.heap.size" in name:
        try: peak[name] = max(peak.get(name, 0.0), float(r[4]))
        except ValueError: pass
for k in sorted(peak): print(f"{k}: max {peak[k]:g}")
PY
    echo; echo "## Leak: 500 POST /watchers, then a heap snapshot"
    dotnet src/DiagLab.Load/bin/Release/net10.0/DiagLab.Load.dll "http://localhost:$PORT/watchers" 500 20 POST
    curl -s localhost:$PORT/stats; echo
    dotnet tool run dotnet-gcdump collect -p $pid -o "$OUT/heap-$variant.gcdump" >/dev/null 2>&1
    echo; echo "## dotnet-gcdump report (top of the list)"
    dotnet tool run dotnet-gcdump report "$OUT/heap-$variant.gcdump" 2>&1 | head -14
  } > "$OUT/tools-$variant.txt"
  rm -f "$OUT/heap-$variant.gcdump"
  kill $pid; wait $pid 2>/dev/null || true
done
exit $status
