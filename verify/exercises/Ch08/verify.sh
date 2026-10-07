#!/usr/bin/env bash
# Maintainer self-check for Chapter 8's "Find the bug" sample: the code printed in the chapter is
# the code tested here, and the tests show each defect on it and its absence on the fix. Needs
# only the .NET 10 SDK; the load tests take about ten seconds.
set -euo pipefail
cd "$(dirname "$0")"

python3 ../check_samples.py Ch08
dotnet test -c Release
