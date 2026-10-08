# Chapter 40: Lab — The .NET Health Check

A health check is the most sellable thing a senior .NET engineer can produce on their own. It is a written, evidence-backed assessment of a codebase that tells a client what is fine, what is risky, and what to do first. It turns the point of view from [Chapter 60](#chapter-60-having-a-point-of-view) and the diagnosis from [Chapter 61](#chapter-61-discovery-and-diagnosis) into a deliverable a CTO can forward to their CFO. And it is the natural first paid engagement that [Chapter 63](#chapter-63-recommendations-proposals-and-estimates) prices and [Chapter 65](#chapter-65-positioning-and-public-proof) tells you to publish.

In this lab you assess a real codebase: Microsoft's **eShop** reference application, pinned to one commit. A script collects the raw evidence in about two minutes. The rest is the part no script does: deciding which of the fifty things you found matter to *this* client, and writing it so an executive reads one page and acts on it.

The kit is at **[labs/62-health-check](https://github.com/malyna2/dotnet-handbook/tree/main/labs/62-health-check)**.

```
  evidence                    findings                     decisions
  ────────                    ────────                     ─────────
  health-check.sh  ──►  finding cards (~50)  ──►  risk matrix  ──►  top 5  ──►  1-page summary
  + reading code        observation, evidence,     likelihood x      what the        + detail
  + interviews          impact, likelihood,        impact, in the    executive       + roadmap
                        recommendation, effort     client's terms    reads
```

## Goal and the senior signal it trains

**Goal.** Produce a client-ready health-check report on `dotnet/eShop` at commit `b4a4087`: a one-page executive summary with five prioritized risks, a risk matrix, finding cards with evidence a stranger can re-check, and a two-week plan. Then present it, out loud, to someone playing the client.

**The senior signal.** Middle engineers find problems. Senior engineers decide which problems matter, for whom, and in what order, and they can defend that ordering with evidence. A report with eighty findings and no ranking shows effort. A report with five ranked risks tied to the client's goal shows judgment, and judgment is what gets you invited back (Chapter 60). The lab trains three things interviewers and clients probe for:

- **Evidence discipline.** Every claim points to a file, a command output or an interview note. Nothing is "it looked messy."
- **Business translation.** "Audience validation is disabled" becomes "a token issued for one service is accepted by all of them."
- **Tact.** You describe code without judging the people who wrote it, and the team who built the system ends up supporting your recommendations.

> **The portfolio rule.** Your report on eShop goes in **your own public portfolio repo**, not in this one. It is one of the best public artifacts you can have ([Chapter 65](#chapter-65-positioning-and-public-proof) calls it a "public assessment"). An assessment of a real employer's or client's code is confidential: it stays private, and in a portfolio it appears only as an anonymized story ([Chapter 36](#chapter-36-the-story-bank-evidence-portfolio)).

## Time budget

| Part | Time | Output |
|---|---|---|
| Setup and the automated run | 30 min | `.work/out/` evidence folder |
| Level 1: read the evidence, write finding cards | 3–4 h | 25–50 raw findings |
| Level 2: score, cut to five, write the report | 3–4 h | Risk matrix, one-page summary, report |
| Level 3: present it, and reuse the method | 2–3 h | Recorded readout, a run on a second codebase |
| **Total** | **~1.5–2 days** | A public report you can link from a CV |

A paid version of this on a client system usually runs about two weeks (Chapter 65). The extra time goes on interviews, access, production telemetry and a readout with the team. The lab skips those because eShop has no production and no team to interview, and the Tasks say where to simulate them.

## Setup

You need git, bash, `jq`, `curl` and the .NET 10 SDK. Docker is not required: the script doesn't run eShop's functional tests.

```bash
git clone https://github.com/malyna2/dotnet-handbook.git
cd dotnet-handbook/labs/62-health-check
scripts/health-check.sh --build        # ~2 minutes with a warm NuGet cache
ls .work/out/
```

The script clones `dotnet/eShop` into `.work/eShop` (git-ignored), checks out the pinned commit, and writes one evidence file per area into `.work/out/`. Every section is tolerant: a missing tool writes `SKIPPED: <reason>` instead of stopping the run. Point it at your own code with `--target ~/src/my-app` (nothing is cloned), or at another public repo with `--repo <url> --commit <sha>`.

The numbers below come from the maintainer's run in `reference-runs/`, on 4 vCPU (Intel Xeon 2.10 GHz), 16 GB RAM, Ubuntu 24.04, .NET SDK 10.0.112, on 2026-09-26. The code-derived files are identical on any machine for the pinned commit. The package files change as NuGet publishes new versions, so yours will differ in the "latest version" column, and that's expected.

> **Pitfall.** eShop's `global.json` pins SDK `10.0.302` with `rollForward: latestFeature`. The Ubuntu apt package installs `10.0.112`, which is a lower feature band, so every `dotnet` command *inside* the repo fails with "A compatible .NET SDK was not found." The script runs `dotnet` from the output directory instead, where the repo's `global.json` doesn't apply, and it records this in `01-inventory-runtime.txt`. That is your first finding, and you haven't read a line of code yet: a new developer or a build agent on distro packages cannot build this repo without changing something.

### Why eShop, and why this commit

eShop is Microsoft's reference .NET application: an Aspire AppHost, ten services and web apps, PostgreSQL with pgvector, Redis, RabbitMQ, Duende IdentityServer, a Blazor front end, a .NET MAUI client and optional AI features. It is well known, actively maintained, restores cleanly, and is big enough to have real trade-offs without being too big to read in a day. Commit `b4a40872005d4bb29e5b1fa1ff7e244143d39215` is `main` on 2026-08-28. Pinning it means everyone doing this lab looks at the same code, and your report stays true after the repo moves on.

### The brief you are answering

A health check without a question is a static-analysis dump. So the lab gives you one, as a labelled composite scenario:

> *[Retailer], a mid-size online shop, forked eShop at this commit to replace its ageing storefront. A team of [n] developers plans to go live in [n] months on Azure Container Apps, using the AppHost's built-in deployment. The CTO asks you: "Is this fit to take to production, and what do we fix first?"*

Every score you give is relative to that brief. eShop's maintainers built it to *teach*, and their README says the container deployment "is intended for evaluation and demonstrations, not production data." Choices that are sensible in a teaching app become risks in a production fork. Holding those two things together (respect for the builders, honesty about the risk) is the tact lesson of this lab.

## The eleven areas

For each area: what to look at, where the script puts the evidence, what eShop showed at the pinned commit, and the chapter that explains the mechanism. The script output is a list of **leads**. A lead becomes a finding only after you open the file and confirm it.

### Runtime and end of support

**Look at:** target frameworks, the SDK pin, and the support phase and end-of-support date of each channel, from `dotnet/core`'s `releases-index.json` (the same data behind [Appendix B](#appendix-b-net-version-comparison-cheat-sheet)). Then the patch level: is the app on the latest patch, and was that patch a security release? Mechanism: [Chapter 30](#chapter-30-working-with-legacy-brownfield-code) (the EOL treadmill).

**eShop showed** (`01-inventory-runtime.txt`): 26 project files target `net10.0`, and the MAUI projects multi-target `net10.0-*`. .NET 10 is `active`, LTS, with an end of support of 2028-11-14. For contrast, .NET 8 and .NET 9 both reach end of support on **2026-11-10**, six weeks after this run: a client on either one has a finding with a date attached. The latest .NET 10 runtime is 10.0.12, released 2026-09-08 as a security release fixing 6 CVEs. eShop's ASP.NET Core packages are at 10.0.11 (`02-dependencies-summary.txt`), which was current on the commit date and one security patch behind by the run date.

> **Best practice.** State which date you measured "outdated" against. "One patch behind" is a finding about the *team's process* if the patch shipped two months ago, and a non-finding if it shipped a week after the snapshot. Write both dates on the finding card.

### Dependencies

**Look at:** `dotnet list package --vulnerable`, `--deprecated` and `--outdated`, each with `--include-transitive`. Count unique packages, not rows: a package used by ten projects appears in ten rows. Split top-level upgrades by size, because a patch bump and a major bump are different conversations. Mechanism: [Chapter 35](#chapter-35-software-supply-chain-security).

**eShop showed** (`02-dependencies-summary.txt`, web solution, 25 projects): 69 unique top-level and 273 unique transitive packages, 286 unique in total. **Zero** vulnerable, **zero** deprecated. 60 top-level packages are behind their latest stable version: 8 major, 25 minor, 24 patch, and 3 where `--outdated` found no stable version at all, because they are prereleases. Seven top-level packages in use are prereleases, among them `Asp.Versioning.*` at `10.0.0-preview.2` and the Foundry hosting and OpenAI client packages at `13.5.3-preview.*`. The major bumps include Duende IdentityServer 7.3.2 → 8.0.8 and MediatR 13 → 14.

Read that carefully before writing it up. "60 outdated packages" sounds alarming and means little: 24 are patch bumps that a Dependabot group would take in one PR. The real questions are the prereleases in a production path, and the major-version upgrades, which carry migration work and, for IdentityServer, licensing terms (see *Cost drivers*).

### Supply chain

**Look at:** package sources and `packageSourceMapping`, lock files or central transitive pinning, whether NuGet audit warnings can fail the build, how CI actions are pinned, what writes to the build from the network, and base images. Mechanism: [Chapter 35](#chapter-35-software-supply-chain-security).

**eShop showed** (`03-supply-chain.txt`, `09-build-tests.txt`):

- **Good:** `nuget.config` clears inherited sources and maps `*` to nuget.org, which closes the dependency-confusion door. Central package management is on, with `CentralPackageTransitivePinningEnabled`. Dependabot runs weekly for NuGet with sensible groups.
- **Audit silenced:** `Directory.Build.props` sets `TreatWarningsAsErrors` *and* `<NoWarn>NU1901;NU1902;NU1903;NU1904</NoWarn>`, with the comment "Temporarily disable security warnings for transitive packages." Those four codes are NuGet's vulnerability warnings, low to critical. Today there are no vulnerable packages, so nothing is hidden. The next advisory, though, won't fail the build, and nobody will see it unless they run `--vulnerable` by hand.
- **Actions:** 0 of 10 `uses:` references are pinned to a commit SHA; all use moving tags like `actions/checkout@v4`. Only 1 of 4 workflows declares `permissions:`.
- **The build downloads from a CDN.** `Identity.API` restores jQuery, Bootstrap and jQuery Validation from cdnjs at build time through LibMan (`libman.json`). In this environment egress to cdnjs is blocked, and the build failed with four `LIB002` errors. That also blocked every test project that references `Identity.API`. A build that depends on a third-party CDN being reachable, and serving the same bytes, is both a reliability and an integrity risk.
- **Containers:** no Dockerfiles. Images come from the SDK's container support and the AppHost deploy, so the base-image question becomes "which image does the SDK pick, and who rebuilds it when a runtime patch ships?"

### Change hotspots

**Look at:** churn × complexity from git history ([Chapter 30](#chapter-30-working-with-legacy-brownfield-code)). The script counts commits per `.cs` file in a window, multiplies by a cheap branch count (`if`, `case`, loops, `catch`, `&&`, `||`, `??`) and lists authors. It excludes migrations and `*.g.cs`/`*.Designer.cs`.

**eShop showed** (`04-hotspots.tsv`, `04-hotspots-all-history.tsv`, `04-churn-by-folder.txt`): in the 365 days before the commit there were only **16 commits by 8 authors**, and no file changed more than 3 times. That is too little history to rank anything, so the script also ranks the whole history (347 commits since 2023-10-18). There, `src/Catalog.API/Apis/CatalogApi.cs` leads (10 commits × 35 branches, 406 lines, 8 authors), followed by `src/ClientApp/Services/Basket/Protos/Basket.cs`, which is gRPC-generated code checked into the repo that the filter didn't catch. `src/eShop.AppHost/Program.cs` has the highest churn on the list (21 commits, 15 authors) but only 10 branches.

Two lessons. First, check the window has enough commits before you trust a ranking: a slow year is a finding about the project, not a hotspot map. Second, eShop has no severe hotspot. The top one is 406 non-blank lines with 35 branches, and change is spread across many authors. "No hotspot concentration, no single-author files" is a *strength* worth writing down.

### Architecture and coupling

**Look at:** the project reference graph, fan-in of shared libraries, cycles, where domain logic lives, and whether services share databases. Mechanism: [Chapter 6](#chapter-6-architecture-application-design).

**eShop showed** (`05-architecture.txt`): a clean graph with no cycles. `eShop.ServiceDefaults` has a fan-in of 9 and `EventBusRabbitMQ` 7, which is by design: they are the shared cross-cutting and messaging layers. Only Ordering is split into API, Domain and Infrastructure projects; the other services are single projects. That is a deliberate choice about where to spend DDD ceremony, not a defect. The largest code base by far is the MAUI `ClientApp` (6,003 non-blank lines of 15,314 production lines), and the web solution doesn't build it.

The finding here isn't about the graph. It's the question the brief raises: does [Retailer] need the MAUI app and the webhooks sample at all? Every project you keep is code you patch and upgrade for years.

### Tests

**Look at:** do tests build and run from a clean clone, how long, what kinds exist, and do the hotspot files have tests? Mechanism: [Chapter 7](#chapter-7-testing) and [Chapter 25](#chapter-25-advanced-specialized-testing).

**eShop showed** (`06-tests.txt`, `09-build-tests.txt`): 9 test projects, with 87 `[TestMethod]`, 15 `[Fact]` and 18 `[Theory]` attributes, mixing MSTest and xUnit v3. There are 3,065 non-blank test lines against 15,314 production lines. Functional tests use `WebApplicationFactory` and Aspire, and need a container runtime. In this run, `Basket.UnitTests` (8 tests) and `Ordering.UnitTests` (43 tests) built and passed in about half a second each. `Application.UnitTests` and `eShop.AppHost.UnitTests` failed to build because they reference `Identity.API` (the cdnjs problem above), and `ClientApp.UnitTests` needs the `maui-tizen` workload.

The script's "projects without a same-named test project" list is a heuristic and it misleads here: it lists `Identity.API`, but `Application.UnitTests` and `Ordering.FunctionalTests` do reference it. Always open the references before you write "untested."

### Security posture

**Look at:** secrets in source, token validation settings, endpoints without authorization, CORS, raw SQL, rate limiting. Mechanism: [Chapter 14](#chapter-14-security).

**eShop showed** (`07-code-signals.txt`, then reading the files):

- OIDC client secrets are the literal `"secret"`, in `src/WebApp/Extensions/Extensions.cs:78` and `src/WebhookClient/Extensions/Extensions.cs:54`, and IdentityServer's client definitions use `new Secret("secret".Sha256())` three times.
- `RequireHttpsMetadata = false` appears in 3 files, and `src/eShop.ServiceDefaults/AuthenticationExtensions.cs:49` sets `ValidateAudience = false` for every API that uses the shared JWT setup.
- No rate limiting anywhere (0 matches for `AddRateLimiter`, `RequireRateLimiting` or `[EnableRateLimiting]`).
- No raw SQL, no `AllowAnyOrigin`.

For a teaching app these are reasonable shortcuts. For the brief they are the top of the report. Audience validation off means a token issued for any eShop API is accepted by all of them. A known client secret means anyone who has read this public repo can present themselves as the web app to the identity server.

### Observability signals

**Look at:** traces, metrics and logs exported, health endpoints, structured logging, custom business telemetry, and what alerts exist. Mechanism: [Chapter 13](#chapter-13-observability).

**eShop showed:** OpenTelemetry tracing and metrics, and health checks, wired once in `eShop.ServiceDefaults` and used by every service (health-check calls in 10 files). No interpolated log messages were found, and `[LoggerMessage]` is used in one file. There is no custom `ActivitySource` or `Meter`, so there are no business metrics: orders placed, payments failed, basket-to-order conversion. There are no alert definitions in the repo, which is expected for a sample and is a question for [Retailer]'s team: "Who is paged, and on what?"

### Performance smells

**Look at:** sync-over-async, `async void`, `HttpClient` lifetime, EF Core query shape, and *any* real measurement. Mechanism: [Chapter 15](#chapter-15-performance-optimization), [Chapter 4](#chapter-4-data-access-databases), [Chapter 8](#chapter-8-asynchronous-concurrent-programming).

**eShop showed:** the `.Result` grep found 3 hits in 2 files, all false positives (`context.Result` on a filter context and a custom `Result` property). All 8 `async void` methods are MAUI lifecycle overrides and event handlers in `ClientApp`, where `async void` is the framework's pattern. The two `new HttpClient(` calls are in the MAUI client too. The services use `IHttpClientFactory` through service discovery. `AsNoTracking` never appears, and `.Include(` appears 5 times in 3 files.

The honest verdict is "no evidence of a performance problem, and no measurement either." Grep can't find a slow query. The next step for [Retailer] is a load test of the checkout path and a look at the catalog queries' plans ([Chapter 37](#chapter-37-the-slow-query-lab-reading-execution-plans)), not a finding invented from a grep count.

> **Pitfall.** Reporting grep counts as findings. Before the script excluded test code, the `.Result` count was several times higher, mostly MSTest assertions on `result.Result`, and every one of the three production hits is a false positive. A client engineer who checks one claim and finds it wrong will discount the whole report.

### Build, CI and containers

**Look at:** what CI runs on every PR, warnings-as-errors, nullable, analyzers, how images are built and deployed, manual steps. Mechanism: [Chapter 12](#chapter-12-devops-cicd), [Chapter 11](#chapter-11-containers-orchestration).

**eShop showed** (`08-build-ci-cost.txt`): the PR workflow builds `eShop.Web.slnf` and runs `dotnet test` on it. There are separate workflows for the MAUI app, Playwright end-to-end tests and markdown lint. `TreatWarningsAsErrors` is on globally, `<Nullable>enable` is set in 12 of 28 project files, and there's no `AnalysisLevel` setting. Deployment is `aspire deploy` to Azure Container Apps from the AppHost model. No IaC files (Bicep, Terraform, Helm) are checked in, because the AppHost *is* the infrastructure definition.

### Cost drivers

**Look at:** what the app provisions, what scales with traffic, paid per-call APIs, and licences. Mechanism: [Chapter 28](#chapter-28-compliance-data-privacy-cloud-cost-finops).

**eShop showed:** the AppHost provisions PostgreSQL, Redis and RabbitMQ as containers, an Azure Container Apps environment, and, when enabled, Microsoft Foundry deployments of `gpt-4.1-mini` and `text-embedding-3-small` (or local Ollama models). When AI is enabled, the catalog's semantic search (`CatalogApi.cs`, `GetItemsBySemanticRelevance`) calls the embedding model **once per search request**. That endpoint needs no authentication, and there's no rate limiting, so an anonymous client can generate paid API calls at will. Duende IdentityServer is commercial software. Check its current licence terms against [Retailer]'s size before go-live, and certainly before the 7 → 8 major upgrade.

> **Best practice.** Cost findings need the client's numbers, which you don't have yet. Write the mechanism ("one embedding call per search, unauthenticated, no rate limit") and leave the amount as a placeholder: "[searches/day] × [price per 1K tokens] × [avg tokens]". Never invent the figure.

## The finding card

Each finding gets one card (`templates/finding-card.md`). The order of fields is deliberate:

```text
**Finding S-02: A token issued for one API is accepted by every API**
**Area** security
**Observation** The shared JWT setup disables audience validation for all services.
**Evidence** src/eShop.ServiceDefaults/AuthenticationExtensions.cs:49 at b4a4087
             (ValidateAudience = false), used by Basket, Catalog, Ordering, Webhooks.
**Business impact** A token stolen from, or over-scoped for, one service works against
             all of them: a compromise of the least important API reaches orders.
**Likelihood** 3 possible: needs a leaked token, but tokens leak (logs, browser storage).
**Impact** 4 major: order data and actions for any customer whose token is taken.
**Risk** 3 x 4 = 12      **Confidence** high (code read, one line)
**Recommendation** Set per-API audiences in IdentityServer and enable ValidateAudience.
**Effort** S (under a day, plus a test per API)     **Owner** tech lead
**If we do nothing** Stays latent until the first leaked token; worse with each new API.
```

- **Observation** is a neutral statement of fact. No "unfortunately," no "the developers forgot."
- **Evidence** lets a stranger check you: path and line *at a commit*, or the command and its output file.
- **Business impact** is the consequence in the client's terms. If you can't write it, the finding is probably noise for this brief.
- **Likelihood and impact** are scored separately because they're argued separately, and a one-line reason for each score is what makes it defensible.
- **Confidence** says how sure you are. A finding inferred from a grep count is "low" until you read the code.
- **Effort** uses T-shirt sizes. A "rough order of magnitude" is honest at this stage; [Chapter 63](#chapter-63-recommendations-proposals-and-estimates) covers turning it into an estimate.

## The risk matrix

Score likelihood and impact from 1 to 5, *against the brief*, and place each finding ID in the grid (the template is in `templates/report-template.md`):

```text
                    impact →   1          2          3           4          5
likelihood                     negligible minor      moderate    major      severe
5 already happening            .          .          .           .          .
4 likely                       .          B-01       .           .          D-01
3 possible                     .          D-03       B-02, C-01  S-02, S-03 S-01
2 unlikely                     .          .          .           C-02       .
1 rare                         .          .          .           .          .
```

The top-right corner is your executive summary. Everything in the bottom-left goes to the appendix. Two rules keep the matrix honest:

1. **Impact is business impact, not technical severity.** A CVSS 9.8 vulnerability in a package used only by a build-time tool, whose code never runs in production, is not a 5. A "low" finding that stops the only person who understands billing from being on holiday may be. Write the impact as the client would feel it.
2. **Likelihood includes the client's context.** Hard-coded secrets in a private repo of a five-person team, and in a public fork, are the same code with different likelihoods.

> **Pitfall.** Multiplying scores and sorting blindly. The product is a conversation starter, not an answer. Two findings at 12 aren't equal if one takes an hour to fix and the other takes a quarter. The cut to five uses risk *and* effort.

## From fifty findings to five

A first pass on eShop gives 30–50 raw cards. An executive reads five. The cut is where your judgment shows, so do it in explicit steps:

1. **Drop the non-findings.** False positives from grep, style preferences, and anything whose business impact line you couldn't write. Keep them in an appendix under "checked, not a concern." That shows coverage without costing attention.
2. **Merge by root cause.** "Client secret is `secret`," "`RequireHttpsMetadata = false`" and "audience validation off" are one story: *the identity setup is the sample's demo configuration*. One finding, one recommendation ("replace the demo identity configuration before go-live"), three pieces of evidence.
3. **Score against the brief** and place each on the matrix.
4. **Rank by risk, then break ties by effort and by date.** A cheap fix to a high risk goes first. A finding with a deadline (end of support, an audit, a peak season) beats an equal one without.
5. **Check the five together.** Do they tell the CTO what to do in the next two weeks? Is there anything on the list that the team already knows and is fixing? Then say so and credit them instead.
6. **Write the "what's in good shape" list.** Three specific strengths with evidence. It isn't flattery: it tells the client what *not* to change, and it proves you looked at the whole system.

## The report: one page, then detail

The structure is in `templates/report-template.md` and `templates/executive-summary.md`. It follows the pyramid principle from [Chapter 63](#chapter-63-recommendations-proposals-and-estimates): the answer first, then the reasons, then the evidence.

- **Page one, the executive summary:** the bottom line in two sentences, three strengths, the top five risks in a table (risk in business terms, score, recommendation, effort), the first two weeks, what you didn't look at, and the one decision you need. If it doesn't fit on one page, cut findings, not font size.
- **Scope and method:** the question asked, the commit and environment, what you didn't assess, how you collected evidence. This protects you. "We didn't assess production data or load" is what lets a reader trust the rest.
- **Context:** what "healthy" means for this brief, in two or three sentences.
- **Risk matrix, then findings by area,** each area opening with a one-line verdict: good, watch or act.
- **Roadmap:** first two weeks, next quarter, and "later, only if [trigger]."
- **Strengths to keep,** then appendices: all findings, the evidence index, open questions.

> **Best practice.** Put the evidence folder in the appendix *as files*, with the environment header, not as pasted screenshots. The engineer who reads your report on the client side will re-run one command to check you. Make that easy, and make sure it matches.

## Presenting it without insulting the team

The people who built the system will read your report, and usually they're in the room when it's presented. They know things you don't: the deadline that forced the shortcut, the migration that was cancelled, the reason that odd setting exists. If they feel judged, they'll spend the meeting defending the past, and your recommendations will die in the next sprint planning. If they feel represented, they'll implement them for you.

- **Pre-wire the technical lead.** Walk them through the draft before the executive readout. Ask them to correct facts and add context. Nobody should hear a finding about their code for the first time in front of their boss ([Chapter 17](#chapter-17-soft-skills-engineering-practices) covers influence without authority).
- **Describe the code, not the coders.** "The shared JWT setup disables audience validation," not "the team disabled audience validation." Passive voice is usually weak writing. Here it's the right tool.
- **Assume a reason.** Chesterton's fence: before you recommend removing something, find out why it's there. eShop's `NoWarn` comment says "temporarily," so ask what it was working around. The answer may change the recommendation.
- **Give the context that made it reasonable.** "This configuration is the sample's demo setup, which is appropriate for evaluation; for production it needs replacing" credits the original decision and still says what has to change.
- **Lead with strengths that are specific.** "Central package management with transitive pinning and source mapping is better supply-chain hygiene than most production codebases" is credible. "Great work overall!" is filler, and the team will hear it that way.
- **Separate the finding from the fix owner.** Recommend *what*. Let the team propose *how* and *when* in the roadmap discussion. Recommendations they helped shape get done.
- **Never surprise, never gloat.** Present a contradiction between the story and the evidence as a question (Chapter 61): "I expected X and I'm seeing Y. Does that match what you've seen?"

## Tasks

Keep your work in your own portfolio repo, in a folder such as `eshop-health-check/`, with the evidence folder copied in.

### Level 1 — Evidence to findings

1. Run `scripts/health-check.sh --build`. Compare your `03`–`08` files with `reference-runs/`. They should be identical, and if not, explain why before continuing.
2. Read every evidence file. For each area, open at least three of the files the script points to and confirm or reject each lead.
3. Write one finding card per confirmed issue, and a short "checked, not a concern" line for each rejected lead.

**Acceptance criteria:**
- [ ] At least 25 finding cards covering at least 9 of the 11 areas, each with evidence as `path:line @ b4a4087` or a command plus its output file.
- [ ] Every card has a business-impact line tied to the [Retailer] brief, not a generic one.
- [ ] At least three leads marked as false positives, with the reason.
- [ ] The checklist (`templates/checklist.md`) is filled in, with "not assessed: [why]" rather than blanks.

### Level 2 — Findings to decisions

1. Score every card (likelihood, impact, confidence, effort) and build the risk matrix.
2. Merge by root cause, then cut to the top five using the steps above.
3. Write the one-page executive summary and the full report from the templates.
4. Write the two-week plan: which actions, in which order, and what you'd measure to show each one worked.

**Acceptance criteria:**
- [ ] The executive summary fits on one printed page and starts with a bottom line of two sentences at most.
- [ ] Each top-five risk is phrased as a business consequence. No package names or config keys appear in the risk column.
- [ ] At least one pair of findings is merged by root cause, and the report says so.
- [ ] The report states the commit, the environment, the run date and what was not assessed.
- [ ] Three specific strengths, each with evidence.
- [ ] Every number in the report can be found in your evidence folder.

### Level 3 — Present, then reuse

1. Present the summary in ten minutes to a friend or a model playing the CTO, and another ten minutes to one playing the eShop tech lead who wrote the identity setup. Record it or keep a transcript. The tech lead's job is to push back on every finding about "their" code.
2. Revise the report after the tech-lead session. Keep a changelog of what changed and why.
3. Run the script against a second codebase (your own side project or another OSS .NET app, via `--repo`/`--commit` or `--target`) and write only the one-page summary. Time yourself.
4. Optional, and only if the findings would help them: open one well-evidenced issue or PR upstream (for example the SHA-pinning of actions, or the `NoWarn` question). Be courteous, and remember that it's their teaching app, not your client.

**Acceptance criteria:**
- [ ] A transcript or recording of both sessions, with at least two changes to the report that came from the tech lead's pushback.
- [ ] A one-page summary of a second codebase, done in under a working day, with its own evidence folder.
- [ ] A published report in your portfolio, linked from your portfolio README ([Chapter 36](#chapter-36-the-story-bank-evidence-portfolio)).

## Break it

Three broken versions of the deliverable. Build each on purpose from your own cards, look at it as the CTO would, and write down what's wrong with it before you check the answers.

1. **The eighty-finding report.** Put every card, un-merged and un-ranked, into one long list grouped by area, with the grep counts included. What does the CTO do with it on Monday morning?
2. **Graded by technical severity.** Rank by "how bad is this in the abstract": critical for anything security, high for prereleases and old packages, low for the build depending on a CDN. Now compare the order with your brief-based matrix. Which finding moved the most, and why?
3. **The hotspot report from a thin window.** Present `04-hotspots.tsv` (the last 365 days) as "the riskiest code in the system." What would a client engineer say, and what did the generated `Basket.cs` do to the all-history ranking?

## Evidence to keep

In your **public portfolio repo**, not this one:

- `eshop-health-check/report.md`: the full report, with the executive summary as page one.
- `eshop-health-check/evidence/`: your script output, with its environment header, plus the command you ran.
- `eshop-health-check/findings/`: all finding cards, including the rejected leads.
- `eshop-health-check/CHANGELOG.md`: what changed after the tech-lead session.
- The one-page summary of your second codebase.
- A short README that says what it is: "a health check of Microsoft's eShop reference app at commit `b4a4087`, against a hypothetical production brief."

In your **private** story bank: the STAR story below, and anything about a real employer's or client's system. A real client's assessment is confidential by default, even when anonymized, unless the client agrees in writing.

## Interview hook

**The story (STAR):**

- **Situation.** "A client [or: I, as a public exercise] was about to take Microsoft's eShop reference app to production as the base of a shop, and asked whether it was fit to go live."
- **Task.** "Deliver a written assessment in [n] days: what's fine, what's risky, what to fix first, with evidence."
- **Action.** "I automated evidence collection across eleven areas (dependencies, supply chain, churn hotspots, tests, security and so on), confirmed each lead by reading the code, and wrote [n] finding cards. Then I scored them against the business goal, not technical severity, merged them by root cause, and cut to five. I pre-wired the tech lead before the readout."
- **Result.** "A one-page summary with five risks and a two-week plan. The top risk, the demo identity configuration, was [fixed in the first sprint / accepted with a date]. [Measurable outcome in placeholders, e.g. 'build no longer depends on a third-party CDN; audit warnings fail the build again.']"

**Three likely follow-ups:**

1. *"How did you decide what went in the top five?"* Walk through the cut: drop the non-findings, merge by root cause, score against the brief, then break ties by effort and date. Give one example of a finding that looked severe and was demoted, like zero vulnerable packages despite 60 outdated ones.
2. *"How do you know your findings were right?"* Evidence at a commit, false positives you rejected (the `.Result` hits), and a reviewer on the client side re-running the same commands.
3. *"What if the team disagreed with you?"* The pre-wire, the tech lead's corrections that changed the report, and the difference between a disagreement about facts (fix the report) and one about priorities (the client decides, and the report records the trade-off).

**The client hook.** The same work sells as a **fixed-price health check**: one system, a fixed scope (the eleven areas, [n] interviews, read access to the repo, pipelines and dashboards), about two weeks, a written report and a one-hour readout for [price]. It's small enough for a client to approve without a procurement cycle, and it often leads to the follow-on work, because the roadmap in it is a ready-made statement of work. [Chapter 63](#chapter-63-recommendations-proposals-and-estimates) covers pricing, scope traps and paid discovery. The public eShop report is your sample deliverable: "here is exactly what you'd receive."

## Hints and answers

<details>
<summary>Hint: my risk matrix has everything in the middle row</summary>

You're scoring likelihood in the abstract. Re-read the brief: a public fork going live on the AppHost's default deployment. "The demo configuration reaches production" is likely (4) because the documented deploy path takes the code as it is. "A GitHub action tag is re-pointed to malicious code" is unlikely (2), but the impact is major if the workflow token can write to the repo, and only 1 of 4 workflows sets `permissions:`. Scores should spread out once each one has a one-line reason.

</details>

<details>
<summary>A worked top five for the [Retailer] brief</summary>

One defensible answer. Yours can differ if your reasons are explicit.

| # | Risk, in business terms | L × I | Recommendation | Effort |
|---|---|---|---|---|
| 1 | Customer and order data live in database containers that the sample itself says are for demos, not production data | 4 × 5 | Move PostgreSQL, Redis and RabbitMQ to managed services with backups before go-live | L |
| 2 | Anyone who has read the public repo can impersonate the web app to the login service, and a token for one API works on all of them | 3 × 5 | Replace the demo identity configuration: secrets from a vault, per-API audiences, HTTPS metadata | M |
| 3 | A release can be blocked, or silently altered, by a third-party CDN during the build | 3 × 3 | Vendor the four front-end libraries or restore them from a pinned, checked source | S |
| 4 | The next security advisory for a dependency won't fail the build, so it can ship unnoticed | 3 × 4 | Remove `NoWarn NU1901–1904`, and add a scheduled `--vulnerable` job | S |
| 5 | Anonymous visitors can generate paid AI calls without limit when AI search is on | 3 × 3 | Rate-limit and cache semantic search; set a spend alert; keep AI off until priced | S–M |

Merged into these: the three identity settings (→ 2) and the audit switch plus the unpinned actions (→ 4, with actions as a watch item). Left out on purpose: 60 outdated packages (24 are patches that Dependabot handles, no vulnerabilities), the SDK pin (a real but cheap onboarding fix, in the two-week plan), and the prerelease `Asp.Versioning` packages (watch).

Strengths: .NET 10 LTS everywhere, supported to 2028-11-14. Central package management with transitive pinning and source mapping. OpenTelemetry and health checks wired once in ServiceDefaults. Warnings as errors. CI builds and tests every PR, and the unit tests that built ran 51 tests in about a second.

</details>

<details>
<summary>Answer: Break it 1, the eighty-finding report</summary>

The CTO can't act on it. With no ranking, every item looks equally urgent, so either nothing happens or the team picks the easiest items (patch bumps) and the top risk waits. It also hides your judgment: the client paid for the cut, and handed them the raw material instead. And the grep counts invite someone to check one, find a false positive, and discount the rest. The fix is the five-step cut, with everything else in an appendix.

</details>

<details>
<summary>Answer: Break it 2, graded by technical severity</summary>

The CDN dependency moves the most. By abstract severity it's "low, a build concern." Against the brief, it already broke the build in a locked-down environment and it's an integrity path into what you ship, so it lands in the top five. Moving the other way, the smells in the MAUI client (`async void`, `new HttpClient`) fall out entirely, because the brief doesn't include the mobile app. Technical severity answers "how bad is this in general?" The client asked "what should *we* do first?"

</details>

<details>
<summary>Answer: Break it 3, the thin-window hotspot report</summary>

Sixteen commits in a year and a maximum churn of 3 can't rank anything: one commit more or less reorders the list. A client engineer would point out that the "riskiest" files are the ones someone happened to touch last spring. Over the whole history, generated gRPC code (`Basket.cs`, 1,033 lines, 124 branches) ranks second, because the filter only excludes `*.g.cs`. The correct write-up: "change is low and spread across many authors, and there is no hotspot concentration" (a strength), with generated code excluded and the window stated.

</details>

<details>
<summary>Hint: the tech-lead role-play keeps winning</summary>

Good, that's the point. Sort their objections into three piles: *facts* ("that setting is only used in development"): check it, and fix the report if they're right. *Context* ("we had two weeks"): add it to the finding and keep the recommendation. *Priority* ("that's not important"): keep your ranking, add their view, and let the client decide. Write the pile next to each change in your changelog.

</details>

## Further reading for assessors

- **Within this book:** [Chapter 30](#chapter-30-working-with-legacy-brownfield-code) (hotspots, EOL), [Chapter 35](#chapter-35-software-supply-chain-security) (dependencies and supply chain), [Chapter 61](#chapter-61-discovery-and-diagnosis) (the first-day map this lab extends), [Chapter 63](#chapter-63-recommendations-proposals-and-estimates) (executive writing, pricing), [Chapter 65](#chapter-65-positioning-and-public-proof) (publishing the report).
- **Adam Tornhill, *Your Code as a Crime Scene*:** churn × complexity and behavioral code analysis.
- **Barbara Minto, *The Pyramid Principle*:** answer first, then the grouped reasons.
- **David Maister, Charles Green and Robert Galford, *The Trusted Advisor*:** why a report the team feels represented in gets implemented.
- **`dotnet/core` release notes** (`release-notes/releases-index.json` and `release-notes/<channel>/releases.json`): the source for every support date and patch in this chapter.
