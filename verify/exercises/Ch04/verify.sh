#!/usr/bin/env bash
# Maintainer self-check for Chapter 4's "Find the bug" sample: the code printed in the chapter is the
# code tested here, and the tests show the nested N+1 (and the empty result without lazy loading) on
# it and their absence on the fix. Needs only the .NET 10 SDK (SQLite in memory).
set -euo pipefail
cd "$(dirname "$0")"

python3 ../check_samples.py Ch04
dotnet test -c Release
