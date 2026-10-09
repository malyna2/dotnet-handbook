# Lab kit — Chapter 40: The .NET Health Check

A script that collects the raw evidence for a .NET health check, templates for turning it into a client-ready report, and the reference run behind every number in Chapter 40. The target is Microsoft's [eShop](https://github.com/dotnet/eShop) reference app, pinned to commit `b4a40872005d4bb29e5b1fa1ff7e244143d39215` (`main`, 2026-08-28).

> **The portfolio rule.** Nothing you write for this lab goes into *this* repository. Your eShop report, finding cards and evidence folder go in **your own public portfolio repo**. An assessment of a real employer's or client's code is confidential: keep it private, and use it in your portfolio only as an anonymized story.

## What you need

- bash, git, `jq`, `curl` (grep, awk and sort from any Linux or macOS).
- .NET 10 SDK for the package and build sections. Without it, run with `--no-dotnet`.
- Network access to GitHub, nuget.org and raw.githubusercontent.com. Docker is not needed.

## Quick start

```bash
scripts/health-check.sh --build     # clone eShop into .work/, check out the pinned commit, run every check
ls .work/out/                       # one evidence file per area, plus SUMMARY.md

cp templates/*.md ~/my-portfolio/eshop-health-check/   # then fill them in (Chapter 40, Tasks)
```

Other targets:

```bash
scripts/health-check.sh --target ~/src/my-app --solution MyApp.sln --out ~/hc/my-app   # a local checkout (nothing is cloned)
scripts/health-check.sh --repo https://github.com/<owner>/<repo>.git --commit <sha>     # another public repo
scripts/health-check.sh --no-dotnet                                                     # git and grep sections only, ~10 s
```

## What the script writes

| File | Area | Chapter |
|---|---|---|
| `00-environment.txt` | machine, OS, SDKs, target and commit | — |
| `01-inventory-runtime.txt` | projects, target frameworks, `global.json`, support phases and recent patches from `dotnet/core` | 30, App. B |
| `02-dependencies-summary.txt`, `02-packages-*.json`, `02-restore.log` | `dotnet list package --outdated/--vulnerable/--deprecated --include-transitive` | 35 |
| `03-supply-chain.txt` | sources, source mapping, pinning, audit switches, action pins, base images | 35 |
| `04-hotspots.tsv`, `04-hotspots-all-history.tsv`, `04-churn-by-folder.txt` | churn × branch count, last 365 days and whole history | 30 |
| `05-architecture.txt` | project references, fan-in, size per project | 6 |
| `06-tests.txt` | test projects, attributes, test vs production lines | 7, 25 |
| `07-code-signals.txt` | security, observability and performance **leads** (production code only) | 14, 13, 15, 4 |
| `08-build-ci-cost.txt` | CI, build settings, what the AppHost provisions, AI clients | 11, 12, 28 |
| `09-build-tests.txt`, `09-build.log` | with `--build`: does it build, and do the unit-test projects pass | 7, 12 |

Every line of output is a lead. Open the file before it becomes a finding.

## Templates

| File | Use it for |
|---|---|
| `templates/finding-card.md` | One card per finding: observation, evidence, business impact, likelihood, impact, recommendation, effort |
| `templates/executive-summary.md` | The one page an executive reads |
| `templates/report-template.md` | The full report: scope, context, risk matrix, findings by area, roadmap, appendices |
| `templates/checklist.md` | Coverage across the eleven areas, so "not assessed" is a decision rather than an accident |

## Troubleshooting

- **"A compatible .NET SDK was not found" inside `.work/eShop`.** eShop's `global.json` pins SDK 10.0.302 (`latestFeature`), and Ubuntu's apt package is 10.0.112. The script runs `dotnet` from the output directory, where the pin doesn't apply, and notes it in `01-inventory-runtime.txt`. To build by hand, do the same: `cd /tmp && dotnet build <path>/eShop.Web.slnf`.
- **`LIB002 ... could not be resolved by the "cdnjs" provider`.** `Identity.API` downloads front-end libraries from cdnjs at build time. Behind a proxy that blocks cdnjs, `Identity.API` and every test project that references it fail to build. That's a finding, not a kit bug.
- **`ClientApp.UnitTests: BUILD FAILED`.** It needs the MAUI workloads (`maui-tizen` among them). The web solution doesn't include it.
- **Your `02-*` files differ from `reference-runs/`.** They will: NuGet publishes new versions every day. Files `03`–`08` depend only on the pinned commit and should match exactly.

## For maintainers

- `verify.sh` runs the script with `--no-dotnet` and checks that every expected file is written, that the pinned commit is checked out, and that the commit-only files (`03`–`08`, and `01` above the live support table) match `reference-runs/` byte for byte. `verify.sh --full` also runs the dotnet sections with `--build` and reports drift in `02` and `09` without failing, because those depend on NuGet's current state and the network.
- To re-capture after changing the script: `git -C .work/eShop clean -xdf && scripts/health-check.sh --build && cp -r .work/out/. reference-runs/`, then delete `reference-runs/02-packages-all.json` and `02-packages-outdated.json` (about 590 KB together; the summary keeps the numbers). Re-check every number quoted in Chapter 40.

## Last verified

2026-09-26 — `health-check.sh --build` and `verify.sh --full` (verify: OK) on 4 vCPU Intel Xeon 2.10 GHz / 16 GB RAM, Ubuntu 24.04.4, bash 5.2, git 2.43.0, jq, .NET SDK 10.0.112, against eShop `b4a4087`. Both scripts pass ShellCheck 0.9.0 at warning level. Not run on macOS (GNU `date -d` is used for the window labels; the rest avoids GNU-only flags but was not tested with BSD tools).
