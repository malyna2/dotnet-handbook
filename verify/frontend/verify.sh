#!/usr/bin/env bash
# Maintainer self-check for the frontend chapters (Chapter 6 and Chapter 34): every JavaScript and
# TypeScript sample printed in them is the code tested here, it type-checks, and the tests show
# what the text claims, including each "Find the bug" defect on the printed code and its absence
# on the fix. Needs Node 22 and npm; installs the pinned packages from package-lock.json.
set -euo pipefail
cd "$(dirname "$0")"

python3 check_samples.py
npm ci --no-audit --no-fund
npx tsc --noEmit -p .
npx vitest run
