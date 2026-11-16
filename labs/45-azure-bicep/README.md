# Lab kit — Chapter 45, Lab 6: App Service, Azure SQL, Key Vault and Managed Identity in Bicep

One Bicep file that builds the shape most .NET teams run on Azure: an App Service web app with a system-assigned managed identity, Azure SQL with Entra-only authentication (no SQL logins, no password anywhere), Key Vault in RBAC mode with a role assignment for the app, Application Insights on a Log Analytics workspace, and a **budget** on the resource group that emails you at 80 % of actual and 100 % of forecast spend.

> **The portfolio rule.** Your Bicep, your deployment notes and screenshots go in **your own public portfolio repo**. Never commit a subscription ID, tenant ID or connection string.

**Last verified:** 2026-11-05 — `bicep build` and `bicep lint` pass with Bicep CLI 0.38.33 ([`reference-runs/bicep.txt`](reference-runs/bicep.txt)). **The deployment itself was not run here** (no Azure subscription in the maintainer's environment): `deploy.sh` and `cleanup.sh` are checked for syntax only. TODO(verify): deploy once in a pay-as-you-go subscription and record the outputs.

## Cost

While the resource group exists you pay for an App Service **B1** plan and a **Basic** SQL database, plus Application Insights ingestion. Deploy, do the lab, and run `./cleanup.sh` the same day. The budget alert is part of the lab: it is how you would notice if you forgot.

## Run it

```bash
az login
./deploy.sh rg-dotnet-lab westeurope you@example.com
# then the two manual steps it prints: the database user for the app's identity, and an app to deploy
./cleanup.sh rg-dotnet-lab
```

| File | What it is |
|---|---|
| `infra/main.bicep` | every resource, commented where a choice matters |
| `sql/grant-app.sql` | `CREATE USER … FROM EXTERNAL PROVIDER` for the app's identity, least-privilege roles |
| `deploy.sh`, `cleanup.sh` | create the group and deploy; delete the group and purge the vault |
| `verify.sh` | maintainer check: build and lint only |
