# Reference run

The raw output behind every number in Chapter 40: `scripts/health-check.sh --build` from a clean clone of `dotnet/eShop` at `b4a40872005d4bb29e5b1fa1ff7e244143d39215`. The environment header is in `00-environment.txt` (4 vCPU Intel Xeon 2.10 GHz, 16 GB RAM, Ubuntu 24.04.4, .NET SDK 10.0.112, 2026-09-26 UTC).

**Trimmed:** `02-packages-all.json` (268 KB) and `02-packages-outdated.json` (320 KB) are not kept. Their counts are in `02-dependencies-summary.txt`; re-run the script to get the full files. `02-packages-vulnerable.json` and `02-packages-deprecated.json` are kept because they are small (both empty lists of findings).

**Not assessed in this run:** eShop's functional and Playwright tests (they need a container runtime and a browser), the MAUI projects (they need workloads), and anything about a running deployment.
