#!/usr/bin/env bash
# Maintainer self-check: compiles and lints the Bicep. It does NOT deploy: that needs a subscription,
# and the chapter says which parts were verified only by compilation.
set -euo pipefail
cd "$(dirname "$0")"
BICEP=${BICEP:-bicep}
command -v "$BICEP" >/dev/null || { echo "Bicep CLI not found (download it from the Azure/bicep GitHub releases, or set BICEP=)"; exit 2; }
{
  echo "# Bicep CLI: $($BICEP --version)"
  echo "# Captured: $(date -u '+%Y-%m-%d %H:%M UTC') by verify.sh"
  echo
  "$BICEP" build infra/main.bicep --stdout > /dev/null && echo "build: ok"
  "$BICEP" lint infra/main.bicep && echo "lint: no warnings"
  bash -n deploy.sh && bash -n cleanup.sh && echo "scripts: syntax ok"
} 2>&1 | tee reference-runs/bicep.txt
