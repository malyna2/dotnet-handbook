# Practice Roadmap — Part XI "The Practice Gym"

> **Status: APPROVED 2026-09-24** (all recommendations in §7 accepted). **M1 and M2 done** — see the session log in §8. Next: **M3**.
> One milestone per session, finished end to end and verified. At the end of each session: tick the boxes below, and report what was verified and what was not.

**Why this exists.** The book is broad on theory and thin on practice: only Chapters 4, 8 and 17 end with `## Exercises`, and nothing asks the reader to produce evidence. The middle→senior gap is rarely knowledge; it is *proof*: incidents handled, decisions defended, systems measured, things written in English. Every addition below must make the reader **do** something and leave an **artifact** they could show in an interview.

---

## 1. Findings from the read-through

Read in full: `CLAUDE.md`, `build_site.py`, chapters 4, 8, 17, 32, 33, 34, 99 (Appendix A), plus 00, 100 (Appendix B), 101 (What's New) and `site/app.js` (parts, cards, `render()`, `wn*`).

| # | Finding | Consequence for the plan |
|---|---|---|
| F1 | `## Exercises` exists only in Ch 4, 8, 17 (fixed shape: *Find the bug* / *What would you do* / *Go check*, answers in `<details>`). | M8 covers the **31** remaining chapters 1–35, except Ch 32 (M9 gives it its own practice blocks). |
| F2 | `00-frontmatter.md` **no longer has a per-chapter TOC.** Its "Contents" section defers to the sidebar and the generated "Browse chapters" cards (`site/app.js`). The `build_site.py` docstring still mentions a "prose table of contents". | "Update the hand-maintained TOC" means: update the Contents blurb to introduce Part XI. The cards pick up new chapters automatically. |
| F3 | The total-study-time line says **~18 hours**. The build's own per-chapter formula sums to **~23.8 h** today. | M1 recomputes it from the build formula. Part XI gets a **separate practice-time figure** (hours of hands-on work), because the build's reading-time estimate says nothing about lab time. |
| F4 | GitHub Pages uploads **only `site/`** (`.github/workflows/pages.yml`). | Chapters must link to lab kits with **absolute GitHub URLs** (`https://github.com/malyna2/dotnet-handbook/tree/main/labs/…`). A relative `labs/…` link would 404 on the site. This assumes the repo is public (see Decision D8). |
| F5 | Section links resolve across chapters only when **exactly one** chapter owns a heading slug (`headingOwner` in `app.js`). | Lab chapters share headings ("Time budget", "Evidence to keep", …). What's New bullets for labs must link the **chapter slug**, never a shared section slug. |
| F6 | Parameter sniffing is never named. Ch 4 only touches it indirectly (Npgsql auto-prepare → generic plans, `plan_cache_mode`). | M2 gets a parameter-sniffing rung, and the chapter explains the mechanism on both engines. |
| F7 | Ch 32 Step 7 and Appendix A recommend **MassTransit**. | Labs hand-roll the outbox and consumer, which is the point of the lab, so they carry no framework dependency. Any licensing statement about MassTransit gets **verified first** in M10, or left as a TODO for you. |
| F8 | Appendix B says "Last verified: July 2026" and has **no .NET 11 row**. | M10 adds .NET 11 marked **preview/RC** (see E6). |
| F9 | Ch 33 has no dedicated lock-contention scenario. Lock contention appears only as "not GC" in Scenario 3 and as the DB hotspot in Scenario 1. | The M4 fault map names the *closest* scenario honestly and cross-links Ch 4's locking sections. |

## 2. Environment facts (checked 2026-09-24 in this container)

| # | Item | Status | Consequence |
|---|---|---|---|
| E1 | Docker 29.3.1 + Compose v5.1.1 | ✅ Works once the daemon is started. Pulled and ran `postgres:17-alpine` (17.11). | The SessionStart hook (`.claude/hooks/session-start.sh`, D4) starts `dockerd` in every cloud session. |
| E2 | .NET SDK | `builds.dotnet.microsoft.com` / `dotnetcli.azureedge.net` are **blocked** (403), so `dotnet-install.sh` fails. ✅ Ubuntu 24.04 apt has `dotnet-sdk-10.0` **10.0.112**; the SessionStart hook installs it (28 s cold, 0.2 s warm). | ✅ Verified end to end: `dotnet new xunit` + NuGet restore through the proxy + a Testcontainers PostgreSQL test passed (Testcontainers.PostgreSql 4.15.0, Npgsql 10.0.3). |
| E3 | NuGet (`api.nuget.org`), MCR (`mcr.microsoft.com`), Docker Hub | ✅ Reachable | Restores and image pulls work. |
| E4 | `ghcr.io` (Toxiproxy), `quay.io` (Debezium) | ⚠️ Not yet tested | Test at the start of M3 and M4. Fallbacks are listed in Risks. |
| E5 | `learn.microsoft.com`, `devblogs.microsoft.com` | ❌ Blocked for both curl and WebFetch | M10 uses the **GitHub source repos that Learn is built from**: `dotnet/docs`, `dotnet/AspNetCore.Docs`, `dotnet/EntityFramework.Docs`, `dotnet/core` release notes (all ✅ via `raw.githubusercontent.com`). A fact only confirmable on Learn → TODO for you. |
| E6 | .NET support status (from `dotnet/core/release-notes/releases-index.json`) | **10.0** active LTS, 10.0.12, EOL 2028-11-14 · **9.0** and **8.0** EOL **2026-11-10** · **11.0** `go-live` STS, **11.0.0-rc.1** released 2026-09-08 | Labs target **`net10.0`**: .NET 8 leaves support in 7 weeks. .NET 11 appears only in M10, marked RC. |
| E7 | Hardware | 4 vCPU, 15 GB RAM, ~30 GB free disk, cgroup v1 | Every timing and plan in the text is published with this environment header. Seed sizes have to fit. |
| E8 | Containers reaching the proxy | Containers cannot reach the agent proxy or trust its CA without `--network host` plus CA injection. | Affects only a `docker build` that runs `dotnet restore` **here**. Committed Dockerfiles stay standard. I build on the host, or use an uncommitted local override. |

## 3. Structural changes

### 3.1 `PART_RANGES` in `build_site.py`

```diff
-    (35,  98,  "Part X — Trust, Supply Chain & Provenance"),
+    (35,  35,  "Part X — Trust, Supply Chain & Provenance"),
+    (36,  98,  "Part XI — The Practice Gym"),
```

### 3.2 New chapters and lab kits

| Ch | File | Title | Lab kit | Milestone |
|---|---|---|---|---|
| 36 | `chapters/36-evidence-portfolio.md` | Chapter 36: The Story Bank & Evidence Portfolio | `labs/36-evidence-portfolio/` (templates, prompts) | M1 |
| 37 | `chapters/37-lab-execution-plans.md` | Chapter 37: The Slow-Query Lab — Reading Execution Plans | `labs/37-execution-plans/` | M2 |
| 38 | `chapters/38-lab-idempotent-messaging.md` | Chapter 38: Lab — Idempotent Messaging End to End | `labs/38-idempotent-messaging/` | M3 |
| 39 | `chapters/39-lab-incident-gym.md` | Chapter 39: Lab — The Incident Gym | `labs/39-incident-gym/` | M4 |
| 40 | `chapters/40-lab-code-review.md` | Chapter 40: Lab — The Code Review Gym | `labs/40-code-review-gym/` | M5 |
| 41 | `chapters/41-lab-english.md` | Chapter 41: Lab — English for Senior Engineers | `labs/41-english/` (templates, phrase bank) | M6 |
| 42 | `chapters/42-lab-system-design.md` | Chapter 42: Lab — System Design Reps | `labs/42-system-design/` (worksheet) | M7 |

The story bank comes first (Ch 36) on purpose: it sets up the brag doc and the portfolio repo, and every later lab's "Evidence to keep" and "Interview hook" feed into it.

### 3.3 Shared lab layout

```
labs/
  README.md                  index, prerequisites (Docker, .NET 10 SDK), the portfolio rule
  Directory.Build.props      net10.0, nullable enable, TreatWarningsAsErrors for solutions
  Directory.Packages.props   central package pinning (one version per package, all labs)
  <NN-slug>/
    README.md                what to run, "Last verified: <date>, <env>", the portfolio rule
    docker-compose.yml       pinned image tags
    starter/                 what the reader works on (compiles; tests fail for the intended reason)
    solution/                reference solution (see Decision D2)
    tests/                   the acceptance tests the reader must turn green
    verify.sh                maintainer self-check: starter fails as intended, solution passes
    reference-runs/          raw outputs behind every number/plan quoted in the chapter
verify/
  exercises/ChNN/            M8: each "Find the bug" sample compiled + a test proving the defect
```

**The portfolio rule.** Every lab says it in its README and in its chapter: *your solutions, plans, numbers and write-ups belong in **your own public portfolio repo**, not in this one.* Ch 36 adds one distinction: stories about real employers stay **private**; only lab artifacts go public.

### 3.4 `CLAUDE.md` additions (applied as the first commit of M1)

A new `## Practice Gym (Part XI)` section that records:
- the lab chapter shape (Goal & senior signal · Time budget · Setup · Tasks L1–L3 with checkable acceptance criteria · Break it · Evidence to keep · Interview hook as STAR + 3 follow-ups · Hints/answers in `<details>`), and "keep prose tight";
- the `labs/<NN-slug>/` layout above, absolute GitHub links from chapters (F4), and the portfolio rule;
- the quality gates: `net10.0`; pinned images and packages; `verify.sh` must prove "starter fails for the intended reason, solution passes"; numbers and plans only from real runs, with the raw output in `reference-runs/`; no fabricated metrics in examples (use placeholders);
- the What's New rule for labs: link the chapter slug (F5);
- M8 exercise verification lives in `verify/exercises/`.

### 3.5 `00-frontmatter.md` (M1)

Update the Contents blurb to introduce Part XI. Recompute the reading-time total from the build formula (F3), and add a separate line for practice time, e.g. *"Part XI: ~N hours of hands-on work"*, summed from the labs' time budgets. The figure gets updated as each lab lands.

---

## 4. Milestones

Legend: `[ ]` open · `[x]` done and verified · `[~]` done with stated verification gaps (listed in the session report).

### M1 — Story bank & evidence portfolio (Ch 36) — `[~]` done; one verification gap (§8)

- [x] `CLAUDE.md`: Practice Gym conventions (§3.4)
- [x] `build_site.py`: `PART_RANGES` change (§3.1)
- [x] `chapters/36-evidence-portfolio.md`
- [x] `labs/36-evidence-portfolio/`: `templates/` (STAR worksheet, weekly brag doc, CV-bullet worksheet, portfolio `README` evidence index) and `prompts/` (AI interviewer prompts as plain files for copy-paste)
- [x] `00-frontmatter.md`: Contents blurb and study-time lines (§3.5)
- [x] Added beyond the plan: `scripts/mine-git.sh` (story candidates from local git history), and the D7 cross-link from Ch 34's Behavioral section
- [~] Prompts tested against a real model — **not done** (§8)

Outline:
- **Goal / senior signal:** turning experience into specific, defensible, measured stories. *Proof over knowledge.*
- **Time budget:** ~5 h to set up and draft; then 15 min/week for the brag doc and 30 min/week for a mock interview.
- **Mining everyday work:** a sources table (merged PRs, `git log --author … --since …`, incident tickets, review threads, calendar, 1:1 notes); story triggers (something surprised you, a number moved, you disagreed, someone grew); a coverage matrix of 5 stories × 9 questions.
- **STAR worksheets:** one for each Ch 34 behavioral question (hard bug, disagreement, bad technical decision, mentoring, tech debt, pushing back on scope or deadline) and for its follow-up (a decision without complete information). Two senior staples are added: an incident you owned, and influencing without authority. Each worksheet has: what the question really probes, STAR prompts, "numbers to go find", red flags, and 3 likely follow-ups.
- **Weekly brag doc template.**
- **From artifact to CV bullet:** *problem → decision → measured result*, with weak→strong rewrites. Examples use `[placeholders]`, not invented numbers.
- **Honesty rules:** calendar tenure (overlapping jobs are not additive); titles exactly as held; "I" vs "we"; **lab work is presented as lab work, never as production experience**; only numbers you can re-derive; NDA-safe anonymization; AI-assisted text that you can defend line by line.
- **Mock-interview protocol:** timebox, one question at a time, voice if possible, record, re-take. Ready-to-paste prompts: behavioral interviewer, "drill into my story", system-design interviewer, hostile follow-ups, and a debrief/scoring prompt with a 1–4 rubric (structure, personal ownership, specificity/numbers, trade-offs, reflection).
- **Tasks:** L1: 5 STAR drafts and a coverage matrix with no empty row. L2: each story survives an AI mock with ≥3/4 on every rubric line. L3: public portfolio repo with an evidence index, and 3 CV bullets that each link to an artifact.
- **Verification:** templates render on the site; prompts tested by one real run each (transcript kept locally, not committed).

### M2 — Lab: reading execution plans (Ch 37) — `[~]` done; verification gaps in §8

- [x] `labs/37-execution-plans/`: compose with PostgreSQL 18.6 (`pg_stat_statements` + `auto_explain` preloaded) and SQL Server 2025 CU9 behind the `sqlserver` profile; pinned tags
- [x] Deterministic seed (hash-based, no `random()`), `small | medium | large`; medium = 1 M customers / 5 M orders / ~18 M order lines / 50 k products. Skew: customer 1 has 5% of orders, key accounts 2–11 have 10–40 lines per order, product 1 is in 8% of lines, recent orders are mostly open or paid
- [x] `QueryLab` .NET 10 harness (EF Core 10 + Npgsql 10). Per rung it prints fix cost (build time and index size), median wall time, per-call statements, rows and buffers from `pg_stat_statements`, EF Core warnings, the SQL with typed parameters, and the real plan (rung 9 replays the prepared statement). `starter/` and `solution/` share the harness; `--property:Rungs=solution` switches
- [x] Acceptance tests (xunit.v3 on Microsoft.Testing.Platform, Testcontainers, small scale). Each rung is checked for correct rows against a raw-SQL reference and for a *work* criterion, never time. `verify.sh`: starter 9/9 fail with `ACCEPTANCE:`, solution 9/9 pass
- [x] SQL Server track: four scripts (`nvarchar` vs `varchar`, key lookups and the tipping point, sniffing in a procedure plus a PSP check, `OFFSET` vs keyset) with a formatter for `STATISTICS PROFILE/IO`
- [x] Break-it and sidebar scripts that restore the seeded state (stale statistics, the visibility map, four keyset forms, correlated columns)
- [x] `reference-runs/`: `capture.sh` regenerates every file behind a number in the chapter, each with an environment header
- [x] `chapters/37-lab-execution-plans.md` in the lab shape; `labs/README.md` row; front-matter study and practice time
- [x] `.github/workflows/labs.yml` (D3): ShellCheck + `mine-git.sh` smoke test, and `verify.sh` for this lab
- [~] Runs on macOS/Windows/ARM64, and the CI workflow on GitHub — **not verified** (§8)

**How the plan changed on contact with real runs**

| Planned | What shipped | Why |
|---|---|---|
| 10 candidate rungs | 9 rungs | Correlated columns (planned rung 9) became a sidebar: on this data a 5× misestimate did **not** change the plan, which is a better lesson as "a misestimate matters only under a decision" than as a rung with no fix to measure |
| `run <rung> --variant slow\|fast` | `starter/` vs `solution/` implementations of fixed contracts, and acceptance tests | The reader edits real code; the tests hold the starter to "right rows, too much work" |
| SARGability via a function on a date column | `ToLower()` on email (rung 3) | One clear case; the date variant added nothing new to the plan reading |
| PG implicit conversion "to be proven" | Proven: C# widens `long` to `decimal`, and EF Core emits `o.id::numeric = @p` → Seq Scan (rung 4) | |
| L3: find the worst query with `pg_stat_statements` in a mixed workload | L3: N+1 as seen in `pg_stat_statements` (a 50:1 calls ratio), rung 9 caught by `auto_explain`, pricing `force_custom_plan` against the index, and the SQL Server track | The harness resets `pg_stat_statements` per rung, so a "mixed workload" needed a separate driver; these three tasks train the same skill on artifacts the kit already produces |
| One ADR-style note | Evidence table in the chapter (`RESULTS.md`, plans, fix costs, Level 3 excerpts, STAR worksheet) | |

### M3 — Lab: idempotent messaging end to end (Ch 38)

- [ ] Broker decision made **by trying** the Azure Service Bus emulator (plus its SQL Server dependency) in compose here, as you asked. If it runs, use it; otherwise use RabbitMQ. The chapter explains the choice, including reader portability (ARM laptops, RAM).
- [ ] `labs/38-idempotent-messaging/`: `Orders.Api` (order + outbox row in one transaction) → **polling publisher** (`FOR UPDATE SKIP LOCKED`, publisher confirms, mark sent) → broker → `Billing.Consumer` (dedup store: `processed_messages` PK in the **same transaction** as the effect; ack after commit)
- [ ] Testcontainers tests proving **exactly-once effects** under: duplicate delivery; reordering (a version guard or a parked message, never a state regression); a crash between handling and ack (commit, then drop the connection without acking → redelivery → deduplicated); a publisher crash between publish and mark-sent (duplicate publish → deduplicated)
- [ ] `starter/` = naive dual write + a non-idempotent consumer, with the named tests red; `solution/` all green; `verify.sh` asserts both
- [ ] `chapters/38-lab-idempotent-messaging.md`

- **Tasks:** L1: make the duplicate test green. L2: all four failure tests green, plus an outbox lag metric. L3: replace polling with **CDC (Debezium, outbox event router)**. If the Debezium image or sink can't be verified here, L3 is marked unverified and the chapter says so.
- **Break it:** kill the consumer mid-batch; stop the broker for 5 minutes under load, then recover.
- **Evidence:** the test run output, an ADR ("outbox + inbox, why not 2PC"), and a sequence diagram.

### M4 — Lab: incident gym (Ch 39)

- [ ] `labs/39-incident-gym/`: `Gym.Api` (.NET 10; orders + catalog; EF Core → Postgres **via Toxiproxy**; outbox → RabbitMQ; a consumer worker; a fake payment gateway behind Toxiproxy), an OpenTelemetry backend (`grafana/otel-lgtm`, one container; Aspire Dashboard as a light alternative), and a k6 load container
- [ ] `roulette.sh`: `start` injects one random fault and records a sealed timestamp; `detect` / `mitigate` stamp your times; `reveal` shows the fault and computes TTD/TTM
- [ ] Each fault verified to produce a visible signal in logs, metrics or traces within ~2 minutes on this hardware, before its card is written
- [ ] Post-mortem template (blameless) and scoring rubric
- [ ] `chapters/39-lab-incident-gym.md`

| Fault | Injection | Ch 33 mapping |
|---|---|---|
| DB latency → pool exhaustion | Toxiproxy `latency` toxic | Scenario 1 |
| Downstream timeouts | Toxiproxy on the payment gateway | Scenario 4 |
| Broker down | stop the broker / Toxiproxy disable | Scenario 4 |
| Lock contention | background job holds `FOR UPDATE` on hot rows | Scenario 1 (closest; F9) + Ch 4 locking |
| Thread-pool starvation (sync-over-async) | runtime flag switches a path to `.Result` | Scenario 4 + Scenario 3 "GC costume" + Ch 8 |
| Memory leak | unbounded static cache keyed per request (accelerated) | Scenario 7 |
| Poison message | malformed message on the queue | Scenario 4 (DLQ) |
| Retry storm | gateway returns 503 + aggressive client retries | Scenario 4 (+1) |

- **Rules:** diagnose from logs, metrics and traces only; don't read the fault flags.
- **Scoring:** TTD; TTM (automatic: p95 and error rate back under threshold); root-cause correctness (3 = exact mechanism, 2 = right subsystem, 1 = symptom only); post-mortem quality (0–3 on each of timeline, impact, root cause, contributing factors, owned and dated action items, blameless language).
- **Tasks:** L1: 3 faults detected and mitigated. L2: all 8, each with a post-mortem. L3: add the alert or runbook entry that would have cut your TTD in half, and re-run to prove it.
- **Evidence:** post-mortems, a TTD/TTM table, one runbook page.

### M5 — Lab: code review gym (Ch 40)

- [ ] `labs/40-code-review-gym/prs/NN-slug/`: `PR.md` (the author's description, sometimes misleading), `before/` and `after/` (**both compile**), `diff.patch` generated from them, and a hidden test that proves each defect where one can be written
- [ ] 10 PRs, with defects distributed as mixed severity plus nitpick distractors, and **one clean PR** that should be approved with nits:

| PR | Seeded defect(s) |
|---|---|
| Order export job | captive dependency (scoped `DbContext` in a singleton) |
| Welcome email on signup | `async void` + missing `CancellationToken` |
| Pricing service client | `HttpClient` per request + sync-over-async |
| Bulk price import | EF change-tracking leak (long-lived context) |
| Payment webhook | idempotency hole (check-then-act, no unique key) |
| Admin search | `FromSqlRaw` with interpolation → SQL injection |
| Renewal scheduler | `DateTime`/time-zone bug (DST, `Kind`, `timestamptz`) |
| Invoice totals | `double` for money |
| Request logging middleware | PII in logs |
| Rename + formatting | none: nits only (calibration) |

- [ ] Answer key ranked by severity (Critical → Nit), each with a model **Conventional Comments** review comment (Ch 17)
- [ ] `chapters/40-lab-code-review.md`

- **Tasks:** L1: review 5 PRs in writing. L2: all 10, scored against the key (severity calibration matters as much as detection; a false "blocking:" costs points). L3: `setup-prs.sh` pushes the before/after trees as branches into **your own** repo; you open real PRs there and review inline, so the evidence is public.
- **Evidence:** your review comments, and a scored table against the key.

### M6 — Lab: English for senior engineers (Ch 41)

- [ ] `labs/41-english/`: templates, checklists, phrase bank, AI feedback prompts
- [ ] Writing drills, each as **bad example → rewrite → checklist**: ADR, one-page design doc, post-mortem, PR description, client status update, pushing back on a deadline, disagree-and-commit
- [ ] Speaking drills: a 2-minute trade-off explanation; running a 15-minute design review; answering "why is this late?". Protocol: record, transcribe, self-score, re-take. Includes a phrase bank (clarifying, hedging, disagreeing, committing, saying no, status)
- [ ] A short section on the errors that recur for Ukrainian and other Slavic L1 speakers (articles, present perfect with past time markers, false friends: *actual*, *eventually*, *control*)
- [ ] `chapters/41-lab-english.md`

- **Tasks:** L1: 3 rewrites that pass their checklists. L2: all 7 written drills and 3 recorded speaking drills. L3: a real artifact from your job, rewritten and shipped: a PR description, an ADR or a status update.
- **Evidence:** before/after pairs in the portfolio repo (employer-sensitive ones stay private).

### M7 — Lab: system design reps (Ch 42)

- [ ] 8 timed 45-minute problems in typical .NET domains: multi-tenant ERP month-end close; checkout with payments; notification fan-out; rate-limited public API; large report/export generation; RAG over per-tenant documents; outbound webhook delivery; plus one of your choosing. Six are required, two optional.
- [ ] Each one: requirements → estimates → API → data model → bottlenecks → failure modes → trade-offs; a rubric; a **model answer in `<details>`** with an ASCII diagram and internally consistent estimates; a follow-up ladder ("and then what breaks?", 3–5 rungs)
- [ ] `labs/42-system-design/`: blank worksheet, estimate cheat-sheet, AI interviewer prompt (reused from M1)
- [ ] `chapters/42-lab-system-design.md`

- **Tasks:** L1: 3 problems on the clock. L2: all 6 required, each self-scored and re-done a week later. L3: one design turned into a real design doc (M6 template) and reviewed by someone else.
- **Evidence:** worksheets, diagrams, and your rubric scores over time.

### M8 — `## Exercises` for every chapter that lacks one

Shape: *Find the bug* / *What would you do* / *Go check*. Bugs must be real and plausible in production code, never trivia. Every C# sample is compiled in `verify/exercises/ChNN/`. The bug is **proven by a test that shows the defect on the buggy code and its absence on the fix**, so the suite stays green and a regression in either direction is visible. Non-C# samples (Dockerfile, YAML, SQL, manifests) are verified by running the relevant tool where one exists here; otherwise the gap is reported. Each answer covers the mechanism, the fix, how you'd detect it in production, and a link to the section to reread. *Go check* gives a concrete command, query or metric.

Planned as one session per batch:

- [ ] **M8a** Part I: Ch 1, 2, 3
- [ ] **M8b** Part II: Ch 5, 6, 7
- [ ] **M8c** Part III: Ch 9, 10, 11, 12
- [ ] **M8d** Part IV: Ch 13, 14, 15
- [ ] **M8e** Part V: Ch 16, 18, 19
- [ ] **M8f** Part VI: Ch 20, 21, 22, 23, 24, 25
- [ ] **M8g** Part VII: Ch 26, 27, 28, 29, 30, 31
- [ ] **M8h** Parts IX–X: Ch 33, 34, 35 (see Decision D6)

### M9 — Chapter 32 + Appendix A (Chapter 99)

- [ ] Ch 32: after each ShopCore step (1–8) add **Break it** (which M4 faults to try), **Defend it** (3 interviewer questions) and **Evidence** (what to commit: tag, README section, benchmark numbers, ADR)
- [ ] Appendix A: an **evidence matrix**, competency × Middle/Senior × the evidence that proves it (an artifact, a story or a metric; never "read chapter X"), linked to Part XI labs (see Decision D5)

### M10 — Accuracy pass on version-specific facts

- [ ] Collect every version-specific claim (grep for `.NET \d`, `EF Core \d`, `C# \d`, `dotnet-*` tools and flags, package names, dates) into a working checklist
- [ ] Verify against `dotnet/core` release notes and the Learn **source** repos (E5); check tool flags by **running the tool's `--help` here**; check package versions via the NuGet API
- [ ] Appendix B: add the .NET 11 row marked **RC (go-live), GA expected November 2026**, refresh 8/9 end-of-support wording, and bump "Last verified"
- [ ] Never invent an API or flag. Anything that can't be confirmed from a reachable official source becomes `TODO(verify)` in the report, for you
- [ ] Found during M2: Ch 4 *The .NET Side* says Npgsql "promotes a statement to a server-side prepared statement after it has been executed a few times (`Max Auto Prepare`)", which reads as on by default. The default is `Max Auto Prepare=0` (off) and `Auto Prepare Min Usages=5` (checked in `npgsql/doc`). Reword the sentence
- [ ] Found during M2: Ch 4 overflows 22 px at a 390 px viewport: an inline code span (`ctx.Orders.Where(o => o.Status == OrderStatus.Abandoned).Exe…`) that cannot wrap. Move it into a code block

---

## 5. Working protocol (every session)

1. Start `dockerd`, install `dotnet-sdk-10.0` via apt (E1, E2), then re-read `CLAUDE.md` and this file.
2. Build the lab kit first, prove it runs (`verify.sh`), and only then write the chapter from the real outputs.
3. `python3 build_site.py` after every chapter edit. **One commit per meaningful chapter change** (each maps to one What's New bullet), always with the regenerated `main.md` and `site/content.js`.
4. **No push** unless you ask. What's New is written at release time per `CLAUDE.md`, one bullet per content commit, linking chapter slugs for lab chapters (F5).
5. End of session: tick this file, and report **verified / not verified** item by item.

## 6. Risks and mitigations

| Risk | Mitigation |
|---|---|
| No SDK and no running Docker at session start (E1, E2) | apt install plus manual `dockerd` at the start of every session; optional SessionStart hook (D4). |
| learn.microsoft.com blocked (E5) | Use the Learn source repos on GitHub. Anything unconfirmable becomes `TODO(verify)`, never a guess. |
| Timings depend on hardware and PG version | Pin image tags. Publish every number with an environment header (CPU, RAM, PG version, scale, warm or cold cache). Keep raw outputs in `reference-runs/`. Tell readers that plan **shapes** and **ratios** transfer, absolute milliseconds don't. |
| The ASB emulator is heavy (SQL Server dependency), may not run here, and may not run natively on ARM laptops | Decide by trying (M3); RabbitMQ fallback. Portability is explained in the chapter. |
| SQL Server container may fail here (cgroup v1, memory) | The SQL Server track is optional and marked unverified if it can't run. |
| `ghcr.io` / `quay.io` untested (E4) | Test at the start of M3 and M4. Toxiproxy fallback: its Docker Hub image, or Polly/`Microsoft.Extensions.Resilience` chaos strategies. Debezium unreachable → L3 marked unverified. |
| Faults that are too subtle to detect on a laptop within minutes | Tune each fault until its signal shows in ≤2 min under the k6 load, and verify on the dashboard before writing its card. |
| M8 is large (31 chapters) | Split into M8a–M8h (one Part per session). |
| Lab rot (images, packages, APIs) | Central package pinning, pinned tags, a "Last verified" line per lab README, `verify.sh`; optional CI (D3). |
| Fabricated numbers leaking into career examples | M1 examples use `[placeholders]`; only lab `reference-runs/` numbers appear as real numbers. |
| Readers presenting lab work as production experience | An explicit honesty rule in Ch 36, repeated in every lab's "Evidence to keep". |
| Unpushed work lost when this container is reclaimed | Your call: I push only when you ask (a feature-branch push does not deploy Pages; only `main` does). |

## 7. Decisions (all approved 2026-09-24, as recommended)

| # | Question | My recommendation |
|---|---|---|
| D1 | Approve the chapter numbers and titles in §3.2 (story bank first, as Ch 36)? | Yes, as listed. |
| D2 | Ship reference solutions in `labs/<NN>/solution/`? | **Yes.** Without them `verify.sh` can't prove the tests are passable. The READMEs say "don't open until done", the same contract as `<details>`. |
| D3 | Add `.github/workflows/labs.yml` (build + test `verify/` and lab solutions on PRs touching `labs/**` or `verify/**`; `SCALE=small`)? | **Yes.** It is cheap, and the only real defence against lab rot. Lands with the first .NET code (M2). |
| D4 | Add a SessionStart hook (`.claude/`) that installs the SDK and starts `dockerd` in cloud sessions? | Yes. ✅ Done in the M1 session (commit `7f682f3`); takes effect for new sessions once merged to `main`. |
| D5 | Appendix A: add the evidence matrix **alongside** the topic checklist, or **replace** it? | **Alongside**: the matrix goes first, and the topic lists stay below as "what to know". Replacing would drop content, and its existing heading anchors are linked. |
| D6 | Give Ch 33 (scenarios) and Ch 34 (interview bank) an `## Exercises` block too? | Yes, but adapted. Ch 33: *Find the bug* is the code behind a scenario. Ch 34: a flawed live-coding answer to diagnose. |
| D7 | Allow one-line cross-links **added** (not rewrites) to existing chapters: Ch 34 Behavioral → Ch 36; Ch 32 intro → Part XI; Ch 17 (review, writing) → Ch 40/41; Ch 33 intro → Ch 39? | Yes. Ch 34 → Ch 36 ✅ done. The others land with their target chapters (Ch 32's in M9). |
| D8 | Is the repo public? Chapters will link lab kits by absolute GitHub URL (F4). | ✅ **Confirmed public** (GitHub API, `visibility: public`). Absolute links are in use from Ch 36. |

## 8. Session log

### Session 1 — 2026-09-24 — plan + M1

**Commits** (local, not pushed): `ddcd158` roadmap · `2396d58` CLAUDE.md conventions · `8c88f70` Ch 36 + kit + `PART_RANGES` · `935497b` Ch 34 cross-link · `52dc89d` front matter · `7f682f3` SessionStart hook · this roadmap update.

**What's New bullets owed at the next release** (per `CLAUDE.md`; lab chapters link the chapter slug):
- Site & functionality: the sidebar has a new *Part XI — The Practice Gym*; the Contents page states reading and practice time separately.
- `8c88f70` → Chapter 36 (new): The Story Bank & Evidence Portfolio.
- `935497b` → Chapter 34: a pointer from the behavioral questions to Chapter 36's worksheets (may be skipped as trivial).
- `52dc89d` → Preface & Contents: corrected study-time figures (the old ~18 h is now ~25 h) and the Part XI intro.

**Verified**
- `python3 build_site.py` after every change; `main.md` and `site/content.js` committed with each source change.
- Link check mirroring `site/app.js` resolution (chapter slugs, same-page ids, unique cross-chapter ids): **0 broken links** in the whole book before and after. The checker was itself tested: it flags a missing target and a duplicated slug.
- Headless Chromium render of Ch 36: 6 `<details>` blocks, 8 tables, 11 code blocks, no raw-HTML leak, Part XI in the sidebar, cross-chapter links land on their targets (Ch 36 → Ch 34 and Ch 34 → Ch 36), **no horizontal scroll** at 390 px and no code block overflowing at 1280 px, no JS errors. Diagrams inspected visually from screenshots.
- `mine-git.sh`: run on this repo and on a 124-commit fixture (reverts, weekend and late-night commits, keyword false positives, `--author` as a name, a non-repo path, no args, `--help`); passes ShellCheck 0.11. Found and fixed a real bug on the way: `head` truncation under `pipefail` killed the script with SIGPIPE (exit 141) on repos with many matches.
- Front matter figures recomputed from the build's formula; the cover-to-cover/skim method was reverse-engineered from the old figures (it reproduces 13.6 h / 10.2 h on the text they were written for) and is now stated in the line.
- SessionStart hook: cold run 28 s (installs SDK, starts `dockerd`), warm run 0.2 s, no-op outside cloud sessions; ShellCheck clean; `dotnet test` with Testcontainers + PostgreSQL passes; `dotnet format` runs.

**Not verified**
- **The six AI interviewer prompts were not run against a model.** Running them needs an AI assistant session; I did not spawn one. Suggested check for you: paste `debrief-scorer.md` with the canary answer from Ch 36 into your assistant — it should score ≤ 2 everywhere.
- `mine-git.sh` was not run on macOS (bash 3.2 / BSD awk). It avoids bash-4 features and gawk extensions, but that is by construction, not by test.
- The Jira JQL and GitHub search qualifiers in Ch 36's mining table were written from knowledge of those tools, not executed.
- The hook's effect on a *new* cloud session is untested until it is merged into `main`.

**Carried into M2:** add `labs/Directory.Build.props`, `labs/Directory.Packages.props` and the labs CI workflow (D3) with the first .NET code; check `ghcr.io`/`quay.io` reachability (E4) at the start of M3/M4.

### Session 2 — 2026-09-24 — M2

**Commits** (local, not pushed): `668af2d` lab 37 kit + shared labs build settings + Labs CI · `bc7c510` Ch 37 + front matter · `e5a3ba2` seed time as measured · this roadmap update.

**What's New bullets owed at the next release** (lab chapters link the chapter slug `#chapter-37-the-slow-query-lab-reading-execution-plans`):
- Site & functionality: nothing reader-facing (the Labs CI workflow is repo-only).
- `bc7c510` → Chapter 37 (new): The Slow-Query Lab — nine slow EF Core queries on a 5-million-order PostgreSQL dataset, with before/after plans, fix costs and a SQL Server track.
- `bc7c510` → Preface & Contents: study time is now ~26 h, and practice time includes the lab's ~10 h (may be merged into the Ch 37 bullet).
- `e5a3ba2` is trivial; skip it.

**Verified**
- `verify.sh` on the final code: the starter's 9 acceptance tests fail, each with an `ACCEPTANCE:` message (right rows, too much work); the solution's 9 pass. Small scale, in Testcontainers.
- Every number in Ch 37 was re-read from `reference-runs/`. Those files come from one `capture.sh` run at 12:35–12:36 UTC; two self-restoring scripts were re-captured at 12:44 after a fix.
  - Environment: 4 vCPU Xeon 2.1 GHz, 16 GB, PostgreSQL 18.6, SQL Server 2025 CU9 (17.0.5005.3), .NET 10.0.12 / SDK 10.0.112, EF Core 10.0.12, Npgsql 10.0.3.
  - The medium database was freshly restored. Buffer counts reproduce exactly between captures: rungs 6 and 7 read 6,386 and 2,791 again after the layout was restored.
- The medium seed completed in 3.8 min of statement time; the data directory is 4.5 GB. The SQL Server track ran end to end.
- The site build plus a link check (mirroring `site/app.js`) found **0 broken links** in the whole book.
- Headless Chromium render of Ch 37: 13 `<details>`, 10 tables, 26 code blocks, no raw-HTML leak, the Part XI sidebar present. The cross-chapter link to Ch 4 `#reading-explain` lands on its target, and there is **no horizontal scroll** at 390 px (after moving one long connection string from inline code into a code block). No JS errors.
- actionlint 1.7 on `labs.yml` and ShellCheck on every lab script: clean.
- Behaviour claims were checked on the running systems:
  - PG 18 `EXPLAIN ANALYZE` includes buffers by default, prints fractional `rows=`, and adds `Index Searches:`.
  - `EXPLAIN (GENERIC_PLAN)` works.
  - Npgsql `Options=-c plan_cache_mode=…` works.
  - EF Core's `ILike` without an escape character emits `ESCAPE ''`.
  - `EF.Functions.LessThan` over `ValueTuple`s becomes a row comparison.
  - An index on `(created_at, id)` turns that row comparison into the `Index Cond`.
  - An `INCLUDE` index is 697 MB against 120 MB for the deduplicated one.
  - A rolled-back `UPDATE` leaves page splits behind in every index (which is why the visibility-map script uses `DELETE`).
  - Tiny `VACUUM`s skip index cleanup, so dead index entries survive — which is why the stale-statistics script now loads orders for an existing customer instead of inserting one.
- Documentation-based claims were checked against primary sources (all reachable via `raw.githubusercontent.com`):
  - the ring-buffer threshold (`NBuffers / 4` in `heapam.c`);
  - "INCLUDE indexes can never use deduplication" (`btree.sgml`);
  - BUFFERS on by default with ANALYZE (`explain.sgml`);
  - `GENERIC_PLAN` added in PG 16 (`release-16.sgml`);
  - skip scan wording and when it pays off (`release-18.sgml`, `indices.sgml`);
  - the five-custom-plans rule (`prepare.sgml`);
  - autovacuum scale factors 0.2 / 0.1 (`config.sgml`);
  - the `log_analyze` overhead warning (`auto-explain.sgml`);
  - pg_trgm with no extractable trigrams (`pgtrgm.sgml`);
  - split-query consistency (`dotnet/EntityFramework.Docs`);
  - `DbString { IsAnsi = true }` (Dapper README);
  - the Npgsql auto-prepare defaults (`npgsql/doc`).

  No `TODO(verify)` was needed.

**Not verified**
- **The Labs CI workflow has never run on GitHub.** It runs on the first push or PR that touches `labs/**`. Watch the `execution-plans` job, which needs Docker on `ubuntu-latest` and takes about 10 minutes here.
- **macOS, Windows and ARM64** were not run. Docker Desktop's default memory limit may be too small for the medium scale; the README asks for ~6 GB.
- **The `large` scale seed** (2 M customers / 20 M orders) was not run; only `small` (tests) and `medium` (reference runs).
- The **"about 10 hours" practice time** is the chapter's time budget, not a timed walkthrough by a reader.
- The SQL Server **PSP optimization** did not engage here (`psp_dispatcher: no`); the chapter reports that observation without explaining why.

**Carried into M3:** check `ghcr.io`/`quay.io` reachability (E4), and whether the Azure Service Bus emulator runs in compose here (D-decision for the broker). Reuse `labs/Directory.*.props` and extend `labs.yml` with the new kit's `verify.sh`.

