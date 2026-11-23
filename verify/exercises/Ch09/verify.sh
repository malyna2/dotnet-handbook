#!/usr/bin/env bash
# Maintainer self-check for Chapter 9's "Find the bug" sample: the code printed in the chapter is the
# code tested here, and the tests show each defect on it (a flat message with no exception and no
# order, a cancellation reported as a failed payment) and its absence on the fix. Needs only the .NET 10 SDK.
set -euo pipefail
cd "$(dirname "$0")"

python3 ../check_samples.py Ch09
dotnet test -c Release
