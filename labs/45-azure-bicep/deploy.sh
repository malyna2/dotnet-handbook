#!/usr/bin/env bash
# Deploys the lab into a NEW resource group. Needs the Azure CLI, signed in (az login), and a subscription
# you are allowed to spend a little on. Cost: an App Service B1 plan and a Basic SQL database run while
# the group exists; run ./cleanup.sh the same day.
#   ./deploy.sh <resource-group> <region> <budget-email>
set -euo pipefail
cd "$(dirname "$0")"
RG=${1:?resource group}; LOCATION=${2:?region, e.g. westeurope}; EMAIL=${3:?budget email}

az group create -n "$RG" -l "$LOCATION" -o none
ME_LOGIN=$(az ad signed-in-user show --query userPrincipalName -o tsv)
ME_ID=$(az ad signed-in-user show --query id -o tsv)
az deployment group create -g "$RG" -f infra/main.bicep -o table \
  -p sqlAdminLogin="$ME_LOGIN" sqlAdminObjectId="$ME_ID" budgetEmail="$EMAIL" budgetStartDate="$(date -u +%Y-%m-01)"

WEB=$(az deployment group show -g "$RG" -n main --query properties.outputs.webAppName.value -o tsv)
SQL=$(az deployment group show -g "$RG" -n main --query properties.outputs.sqlServerFqdn.value -o tsv)
cat <<NEXT

Deployed. Two steps Bicep cannot do for you:
1. Give the web app's identity a user in the database. Connect as yourself (Entra) and run sql/grant-app.sql
   with APP_NAME=$WEB, for example:
     sqlcmd -S $SQL -d shop -G -v APP_NAME=$WEB -i sql/grant-app.sql
2. Deploy any .NET 10 app with a /health endpoint:  az webapp deploy -g $RG -n $WEB --src-path app.zip --type zip

Then check: the Key Vault reference resolved (Configuration → Partner__ApiKey shows a green tick),
requests appear in Application Insights, and the budget exists (Cost Management → Budgets).
NEXT
