#!/usr/bin/env bash
# Maintainer self-check for Chapters 50 and 51: extracts every C# sample from the chapter
# Markdown and compiles it against the pinned Azure SDK versions; checks that the Find-the-bug
# samples match the code the exercise tests run; builds and lints every Bicep sample if the
# Bicep CLI is on PATH (https://github.com/Azure/bicep/releases). Nothing here talks to Azure.
set -euo pipefail
cd "$(dirname "$0")"

python3 extract.py
dotnet build -warnaserror

if command -v bicep >/dev/null; then
  for f in Generated/*.bicep; do
    bicep build "$f" --stdout >/dev/null
    bicep lint "$f"
    echo "  $f builds and lints clean"
  done
else
  echo "bicep CLI not found: Bicep samples NOT checked" >&2
fi
