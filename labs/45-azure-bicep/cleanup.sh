#!/usr/bin/env bash
# Deletes everything the lab created: the whole resource group. The Key Vault stays soft-deleted for
# 7 days (its name is unavailable until then); purge it if you want the name back at once.
#   ./cleanup.sh <resource-group>
set -euo pipefail
RG=${1:?resource group}
VAULTS=$(az keyvault list -g "$RG" --query "[].name" -o tsv)
az group delete -n "$RG" --yes
for v in $VAULTS; do az keyvault purge -n "$v" || true; done
echo "Deleted $RG. Check Cost Management the next day: the last hours are billed after deletion."
