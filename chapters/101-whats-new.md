# What's New

This page is the handbook's changelog. When a new release lands, a popup announces it on your next visit. Under each release, **Site & functionality** items are plain notes, while **Content updates** link to every chapter that changed — a link is ticked off (✓, stored locally in your browser) once you visit it, so you can work through an update at your own pace and see what's still unread.

## Release — August 28, 2026

**🔧 Site & functionality**

- Exercise answers are collapsible. Chapters that now end with an **Exercises** block keep their answers hidden behind a click, so you can work the problem before you read the solution.

**📖 Content updates**

- [Chapter 19: Workflow patterns](#workflow-patterns-the-ground-between-one-call-and-an-agent) — The layer between a single call and an agent: prompt chaining, routing, parallelization, orchestrator-workers, and evaluator-optimizer, with a table for choosing between them.
- [Chapter 19: Memory](#memory-what-the-system-remembers-between-turns) — The four kinds of memory, extraction as a write path that invents facts, and memory as a tenancy, deletion, and per-turn cost problem.
- [Chapter 19: Running agents durably](#running-agents-durably) — Why an in-memory agent loop dies with the pod, and how Durable Task, idempotent tools, compensation, and persisted approval waits turn a run into a resumable workflow.
- [Chapter 19: The .NET AI stack, refreshed](#the-net-ai-stack) — A four-layer decision table covering Microsoft Agent Framework and Foundry Agent Service, and where Semantic Kernel now sits.
- [Chapter 19: Agent-to-agent interop](#agent-to-agent-interop-a2a) — What A2A is, how it differs from MCP, and why a plain HTTP API is usually the right answer inside one codebase.
- [Chapter 19: Cost mechanics](#cost-mechanics-caching-batching-and-thinking-budgets) — Prompt caching and the prompt-layout rule it imposes, batch APIs for non-interactive work, and matching thinking budgets to task type.
- [Chapter 25: Testing nondeterministic systems](#testing-nondeterministic-systems-evals-for-ai-features) — How to test an AI feature: fake the model for the deterministic 90%, and gate CI on an aggregate eval pass rate for the rest.
- [Chapter 18: Workflow assets](#workflow-assets-making-the-setup-a-team-artifact) — Turning your agentic workflow into checked-in repo artifacts, and the two ways a conventions file goes bad.
- [Chapter 18: Measuring whether any of this is working](#measuring-whether-any-of-this-is-working) — Why perceived productivity misleads, and which delivery metrics actually answer the question.
- [Chapter 35: Software Supply Chain Security](#chapter-35-software-supply-chain-security) — New chapter on the three surfaces an attacker uses — the packages you consume, the build that assembles them, and what you publish — and the .NET control that closes each.
- [Chapter 19: Securing AI features and agents](#securing-ai-features-and-agents) — Why prompt injection has no parameterization fix, the lethal trifecta as the design rule for when an agent is unsafe by construction, and authorizing tools in code rather than in the prompt.
- [Chapter 14: Crypto agility and the post-quantum migration](#crypto-agility-and-the-post-quantum-migration) — Harvest-now-decrypt-later sets the deadline by data retention, not by quantum hardware; the ML-KEM/ML-DSA standards; and the 47-day certificate clock already running.
- [Chapter 14: Zero trust and workload identity](#zero-trust-and-workload-identity) — SPIFFE/SPIRE attestation instead of secret zero, mTLS identity, and the OIDC trust-policy condition that is the entire boundary between CI and production.
- [Chapter 29: Accessibility](#accessibility-the-part-that-is-now-law) — WCAG 2.2 AA and the European Accessibility Act, semantic HTML before ARIA, and the two Blazor pitfalls that leave screen-reader users lost.
- [Chapter 25: Accessibility checks in CI](#accessibility-checks-in-the-same-run) — Wiring axe-core into Playwright, baselining so a retrofit doesn't go red on day one, and the ~30% ceiling on what automation can catch.
- [Chapter 12: Platform engineering and measuring delivery](#platform-engineering-and-measuring-delivery) — Golden paths and pave-don't-gate, service catalogs, and the four DORA metrics with a table of exactly how each one gets gamed.
- [Chapter 28: Green software](#part-c-green-software-the-same-levers-a-second-reason) — Utilization beats micro-efficiency, where and when you run outweighs how you code, and an honest ranking of which .NET levers actually move the number.
- [Chapter 20: Abuse, bots, and traffic you did not ask for](#abuse-bots-and-traffic-you-did-not-ask-for) — Rate limiting as an adversarial problem: what you key on, where the counter lives, DDoS by layer, credential stuffing, and denial of wallet.
- [Chapter 21: Chaos engineering](#verifying-resilience-chaos-engineering-in-practice) — Resilience code is the only code we ship without ever executing; the experiment method, Polly v8 fault injection, and why a game day finds more than a quarter of automation.
- [Chapter 10: Lock-in and the economics of leaving](#lock-in-and-the-honest-economics-of-leaving) — Lock-in as a switching cost rather than a binary, where that cost actually concentrates, and why a portability layer usually costs more than the lock-in.
- [Chapter 30: The EOL treadmill](#the-eol-treadmill-legacy-is-a-verb) — A system nobody changes still decays: the .NET 8 and 9 end-of-support date, and why an upgrade skipped four times costs far more than four upgrades.
- [Chapter 33: Scenario 10 — a poisoned dependency](#scenario-10-poisoned-well-a-dependency-you-never-chose-shipped-a-backdoor) — Answering "are we affected?" in thirty minutes from committed lockfiles and stored SBOMs, and rotating credentials without bargaining.
- [Chapter 33: Scenario 11 — the agent leaked customer data](#scenario-11-the-agent-leaked-customer-data-through-a-tool-call) — Containing an agent incident by removing capability rather than by fixing the prompt.
- [Chapter 33: Scenario 12 — the crawler that tripled the egress bill](#scenario-12-the-invisible-customer-an-ai-crawler-tripled-the-egress-bill) — A cost incident with no availability signal, where the real failure is in the alerting.
- [Chapter 8: Exercises](#chapter-8-asynchronous-concurrent-programming) — New practice block: find the sync-over-async bug that starves the thread pool, and a review call on cargo-culted `ConfigureAwait(false)`.
- [Chapter 4: Exercises](#chapter-4-data-access-databases) — New practice block: count the queries hiding in a nested N+1, and decide whether a cache or an index is the right fix.
- [Chapter 17: Exercises](#chapter-17-soft-skills-engineering-practices) — New practice block: the estimate you genuinely cannot give, and reviewing a new joiner's first pull request.

## Release — August 12, 2026

**🔧 Site & functionality**

- Chapters now always open at the beginning. The reader no longer reopens the chapter you last visited, and no longer restores your scroll position within a chapter.
- Sidebar progress bars are now one-way: they record how far through a chapter you have got, so scrolling back up — or reopening a chapter at the top — never winds them backwards.
- Chapters you have started now show two buttons in the sidebar on hover: **Continue** (→) jumps to the point the progress bar is showing, and **Reset** (↻) clears that chapter's progress. Continue is the deliberate version of the old automatic jump: you go back to where you got to only when you ask.
- The links below now open the exact section that changed instead of the top of the chapter — and a link to the chapter you happen to be reading already no longer does nothing at all.
- The "On this page" section list is now available on phones and tablets, where it appears under the chapter list in the menu drawer instead of being hidden. Tapping a section closes the drawer and jumps there.
- Fixed 12 dead links in Appendix A's table of contents — the anchors assumed a different slug format and silently went nowhere.

**📖 Content updates**

- [Chapter 4: PostgreSQL indexes and query plans](#postgresql-in-practice-indexes-and-query-plans) — New section on the heap/MVCC storage model, index types, partial and expression indexes, and reading `EXPLAIN (ANALYZE, BUFFERS)` on a worked 812 ms → 0.09 ms fix.
- [Chapter 4: Bulk writes and cascade behaviour](#bulk-inserts-and-the-limits-of-savechanges) — Why `Add` in a loop is quadratic, when to drop to `COPY`/`SqlBulkCopy`, and the full `DeleteBehavior` table including why a delete succeeds or fails depending on an `Include`.
- [Chapter 4: Dapper in depth](#the-parts-of-dapper-worth-knowing) — Multi-mapping, `QueryMultiple`, unbuffered reads, and how to run Dapper inside an EF Core transaction without silently committing outside it.
- [Chapter 4: Redis in practice](#redis-in-practice-key-design-data-types-and-eviction) — Key design as schema design, the data types worth using, TTL jitter, tag-based invalidation, and why `noeviction` turns a full cache into an outage.
- [Chapter 3: API versioning and backward compatibility](#api-versioning-backward-compatibility) — New section on what actually breaks a client, the four versioning schemes, `Asp.Versioning` wiring, expand–contract, and retiring a version with `Sunset` headers.
- [Chapter 3: Idempotency keys](#idempotency-keys-making-post-retry-safe) — How to make POST retry-safe: the request hash, the three outcomes, and why the key row must be inserted before the side effect.
- [Chapter 3: FluentValidation, deepened](#fluentvalidation) — Edge validation versus domain invariants, endpoint filters, rule composition, async rules as a check-then-act race, and testing validators.
- [Chapter 5: Exception handling strategy](#exception-handling-strategy) — New section answering where to catch, what to log, and what to surface, built on classifying the failure first.
- [Chapter 12: Azure Pipelines in practice](#azure-pipelines-in-practice) — A complete `azure-pipelines.yml` for a .NET service, the concepts that differ from GitHub Actions, and how to read and fix a failing build.
- [Chapter 18: Judging AI-generated code](#judging-ai-generated-code-a-reviewers-rubric) — A reviewer's rubric of the failure modes AI-generated .NET code actually has, in the order worth checking them.

## Release — August 4, 2026

**📖 Content updates**

- [Chapter 4: EF Core Include vs projections](#chapter-4-data-access-databases) — New section on why EF Core silently ignores `Include` when a query ends in a `Select` projection, and why those dead Includes mislead readers of shared base queries.

## Release — July 27, 2026

**🔧 Site & functionality**

- New **What's New** page (you're reading it): a release popup on your first visit after each update, this page at the end of the navigation menu, and locally-stored ✓ marks on the chapter links below once you've opened them.

**📖 Content updates**

- [Chapter 3: gRPC](#chapter-3-aspnet-core-web-apis) — Now shows the full server/client round trip with streaming code, HTTP/2 load-balancing consequences, deadlines, and the `RpcException` error model.
- [Chapter 3: SignalR](#chapter-3-aspnet-core-web-apis) — Added the missing client half, groups, `IHubContext<T>`, a backplane diagram, and a REST vs gRPC vs SignalR vs SSE decision table.
- [Chapter 3: CORS](#chapter-3-aspnet-core-web-apis) — Now explains origins, the preflight handshake, and how to read the "blocked by CORS policy" error.
- [Chapter 3: IHttpClientFactory](#chapter-3-aspnet-core-web-apis) — Explained the handler-pool mechanism, the typed-client-in-a-singleton pitfall, Polly strategy ordering, and `AddStandardResilienceHandler()`.
- [Chapter 3: Health checks](#chapter-3-aspnet-core-web-apis) — Expanded with the aggregation machinery, the `Degraded` status, a custom `IHealthCheck` example, and probe-cost pitfalls.
- [Chapter 3: ProblemDetails](#chapter-3-aspnet-core-web-apis) — New tip: `AddExceptionHandler` vs hand-rolled exception middleware.
