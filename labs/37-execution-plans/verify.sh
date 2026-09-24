#!/usr/bin/env bash
# Maintainers' self-check for Lab 37. (Readers: you don't need this — use dotnet test; see README.)
#
# Proves the lab is solvable and honest:
#   1. the starter builds, and every rung test FAILS with an "ACCEPTANCE:" message —
#      the code returns the right rows, it just does too much work;
#   2. the reference solution builds, and every rung test PASSES;
#   3. the CLI builds against both, and the compose file is valid.
# Needs Docker (the tests start their own PostgreSQL via Testcontainers), the .NET 10 SDK and python3.
set -euo pipefail
cd "$(dirname "$0")"

out=".verify"
rm -rf "$out"
mkdir -p "$out"

docker compose config --quiet
echo "compose file: valid"

for impl in starter solution; do
  echo "== $impl: build"
  dotnet build src/QueryLab.Cli -c Release -p:Rungs="$impl" -nologo -v quiet > "$out/$impl.cli-build.log" 2>&1 \
    || { cat "$out/$impl.cli-build.log"; exit 1; }
  dotnet build tests/QueryLab.Tests -c Release -p:Rungs="$impl" -o "$out/$impl" -nologo -v quiet > "$out/$impl.build.log" 2>&1 \
    || { cat "$out/$impl.build.log"; exit 1; }
  echo "== $impl: test"
  "$out/$impl/QueryLab.Tests" -noColor -result-ctrf "$out/$impl.ctrf.json" > "$out/$impl.console.log" 2>&1 || true
done

python3 - "$out" <<'PY'
import json, sys
out = sys.argv[1]
ok = True
def load(impl):
    return json.load(open(f"{out}/{impl}.ctrf.json"))["results"]["tests"]
for impl, want in (("starter", "failed"), ("solution", "passed")):
    tests = load(impl)
    print(f"{impl}: {len(tests)} tests")
    if len(tests) != 9:
        print(f"  expected 9 rung tests, found {len(tests)}"); ok = False
    for t in sorted(tests, key=lambda t: t["name"]):
        name = t["name"].rsplit(".", 1)[-1]
        msg = (t.get("message") or "").strip().splitlines()
        first = msg[0] if msg else ""
        good = t["status"] == want and (want == "passed" or first.startswith("ACCEPTANCE:"))
        ok &= good
        print(f"  {'ok ' if good else 'BAD'} {t['status']:<6} {name}" + (f" — {first}" if first else ""))
print("verify: OK" if ok else "verify: FAILED")
sys.exit(0 if ok else 1)
PY
