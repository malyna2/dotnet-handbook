#!/usr/bin/env bash
# Maintainers' self-check for Lab 62. (Readers: you don't need this; run scripts/health-check.sh.)
#
# Proves the kit still works:
#   1. health-check.sh runs against the pinned eShop commit and writes every expected file;
#   2. the sections that depend only on the pinned commit (inventory, supply chain, hotspots,
#      architecture, tests, code signals, CI) match reference-runs/ byte for byte, so the numbers
#      quoted in Chapter 62 still come out of the script;
#   3. with --full, the dotnet sections (restore, list package, build, unit tests) run too and
#      their summaries are compared with reference-runs/ (these drift as NuGet publishes new
#      versions, so a difference is reported, not failed).
# Needs bash, git, jq, curl; dotnet (SDK 10) for --full. Uses shellcheck when present.
set -uo pipefail
cd "$(dirname "$0")" || exit 1

full=0; [ "${1:-}" = "--full" ] && full=1
out=".verify/out"
rm -rf .verify; mkdir -p "$out"
ok=1
bad() { echo "BAD  $*"; ok=0; }
good() { echo "ok   $*"; }

if command -v shellcheck >/dev/null 2>&1; then
  shellcheck -S warning scripts/health-check.sh verify.sh && good "shellcheck" || bad "shellcheck"
else
  echo "--   shellcheck not installed, skipped"
fi

if [ "$full" = 1 ]; then args=(--build); else args=(--no-dotnet); fi
scripts/health-check.sh "${args[@]}" --out "$out" > .verify/run.log 2>&1 && good "health-check.sh ${args[*]} exited 0" || bad "health-check.sh exited non-zero (see .verify/run.log)"

expected="00-environment.txt 01-inventory-runtime.txt 02-dependencies-summary.txt 03-supply-chain.txt
  04-hotspots.tsv 04-hotspots-all-history.tsv 04-churn-by-folder.txt 05-architecture.txt 06-tests.txt
  07-code-signals.txt 08-build-ci-cost.txt SUMMARY.md"
[ "$full" = 1 ] && expected="$expected 02-packages-vulnerable.json 02-packages-deprecated.json 02-packages-outdated.json 02-restore.log 09-build-tests.txt"
for f in $expected; do [ -s "$out/$f" ] && good "$f written" || bad "$f missing or empty"; done

grep -q 'commit      : b4a40872005d4bb29e5b1fa1ff7e244143d39215' "$out/00-environment.txt" \
  && good "pinned commit checked out" || bad "wrong commit in 00-environment.txt"

# Deterministic for a pinned commit: must match the reference run exactly.
for f in 03-supply-chain.txt 04-hotspots.tsv 04-hotspots-all-history.tsv 04-churn-by-folder.txt \
         05-architecture.txt 06-tests.txt 07-code-signals.txt 08-build-ci-cost.txt; do
  if diff -q "$out/$f" "reference-runs/$f" >/dev/null; then good "$f matches reference-runs"
  else bad "$f differs from reference-runs:"; diff "reference-runs/$f" "$out/$f" | head -10 | sed 's/^/       /'; fi
done
# 01 contains the live releases-index table, which changes with every Patch Tuesday: compare the rest.
if diff -q <(sed '/## Support status/,$d' "$out/01-inventory-runtime.txt") <(sed '/## Support status/,$d' reference-runs/01-inventory-runtime.txt) >/dev/null; then
  good "01-inventory-runtime.txt (above the live support table) matches"
else bad "01-inventory-runtime.txt differs above the support table"; fi
grep -q $'^  10.0\t' "$out/01-inventory-runtime.txt" && good "releases-index.json fetched" \
  || echo "--   releases-index.json not fetched (offline?): not a failure"

if [ "$full" = 1 ]; then
  for f in 02-dependencies-summary.txt 09-build-tests.txt; do
    if diff -q <(grep -v -E 'duration|wall time' "$out/$f") <(grep -v -E 'duration|wall time' "reference-runs/$f") >/dev/null; then good "$f matches reference-runs"
    else echo "DRIFT $f differs from reference-runs (new package versions or network): review, then re-capture"
         diff <(grep -v -E 'duration|wall time' "reference-runs/$f") <(grep -v -E 'duration|wall time' "$out/$f") | head -12 | sed 's/^/       /'; fi
  done
fi

[ "$ok" = 1 ] && echo "verify: OK" || echo "verify: FAILED"
[ "$ok" = 1 ]
