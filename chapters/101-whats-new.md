# What's New

This page is the handbook's changelog. When a new release lands, a popup announces it on your next visit. Under each release, **Site & functionality** items are plain notes, while **Content updates** link to every chapter that changed — a link is ticked off (✓, stored locally in your browser) once you visit it, so you can work through an update at your own pace and see what's still unread.

## Release — October 8, 2026

**🔧 Site & functionality**

- A new bar under the header switches between **Part 1 (Junior → Middle)**, **Part 2 (Middle → Senior)** and **Full book**; each part has its own sidebar and pager, and the home page has a card for each.
- Clicking a link to a section now lands its heading below both bars instead of under them.
- Long code identifiers in prose now wrap on phone screens instead of widening the page.
- Every short program printed on a learning-path page is compiled and tested in the repository, and a new checker resolves every in-book link the way the site does.

**📖 Content updates**

- [Preface & Contents](#the-middle-senior-net-developer-handbook) — A short introduction to the two learning paths.
- [Part 1: Junior → Middle](#part-1-junior-middle) — New: what Part 1 makes you able to do, how a module works, and the 13 modules with their time budgets.
- [Part 1 · Module 1: Async Essentials](#part-1-module-1-async-essentials) — New module: where an async exception goes, what `WhenAll` reports, thread-pool starvation and socket exhaustion.
- [Part 1 · Module 2: EF Core Essentials](#part-1-module-2-ef-core-essentials) — New module: N+1 counted statement by statement, tracking, and loading strategies.
- [Part 1 · Module 3: SQL and Indexes](#part-1-module-3-sql-and-indexes) — New module: seek versus scan, composite index order and implicit conversions.
- [Part 1 · Module 4: Messaging and Long-Running Work](#part-1-module-4-messaging-and-long-running-work) — New module: message locks, claim-first idempotency and the 202 request-reply pattern.
- [Part 1 · Module 5: Working Like a Middle Developer](#part-1-module-5-working-like-a-middle-developer) — New module: review comments, estimates and vague tickets.
- [Part 1 · Module 6: Exceptions, Logging and First Diagnosis](#part-1-module-6-exceptions-logging-and-first-diagnosis) — New module: `throw` versus `throw ex`, message templates and finding the first cause.
- [Part 1 · Module 7: C# Essentials](#part-1-module-7-c-essentials) — New module: equality, deferred execution, `IQueryable` versus `IEnumerable`, and nullable gaps.
- [Part 1 · Module 8: Web API Basics](#part-1-module-8-web-api-basics) — New foundation module: DI lifetimes, middleware order and options validation.
- [Part 1 · Module 9: Testing Essentials](#part-1-module-9-testing-essentials) — New foundation module: why the in-memory provider passes and why test classes race.
- [Part 1 · Module 10: Design Basics](#part-1-module-10-design-basics) — New foundation module: SOLID by its failure modes and patterns only when the pain repeats.
- [Part 1 · Module 11: Git and Everyday Tooling](#part-1-module-11-git-and-everyday-tooling) — New foundation module: what a rebase rewrites, the reflog and the .NET tools you run daily.
- [Part 1 · Module 12: Security Essentials](#part-1-module-12-security-essentials) — New foundation module: what JWT validation checks, IDOR, SQL injection and what CORS doesn't stop.
- [Part 1 · Module 13: Dates, Money and Strings](#part-1-module-13-dates-money-and-strings) — New foundation module: time zones and DST, `decimal` and rounding rules, culture-sensitive strings.
- [Part 1 · Pay Attention To](#part-1-pay-attention-to) — New: Part 1's traps in one table of trap, mechanism and fix.
- [Part 2: Middle → Senior](#part-2-middle-senior) — New: what Part 2 makes you able to decide, and its 7 modules with their time budgets.
- [Part 2 · Module 1: Runtime and Concurrency Internals](#part-2-module-1-runtime-and-concurrency-internals) — New module: thread injection, the GC and the large object heap.
- [Part 2 · Module 2: Data in Depth](#part-2-module-2-data-in-depth) — New module: isolation levels, lost updates and plan regressions.
- [Part 2 · Module 3: Distributed Consistency](#part-2-module-3-distributed-consistency) — New module: outbox relays, inbox retention, ordering and retry amplification.
- [Part 2 · Module 4: Architecture, API Evolution and System Design](#part-2-module-4-architecture-api-evolution-and-system-design) — New module: service boundaries, expand and contract, and system design trade-offs.
- [Part 2 · Module 5: Observability and Testing at Scale](#part-2-module-5-observability-and-testing-at-scale) — New module: SLO burn-rate alerts, sampling and open-model load tests.
- [Part 2 · Module 6: Production and the Cloud](#part-2-module-6-production-and-the-cloud) — New module: SNAT, scaling consumers against downstream limits and real rollbacks.
- [Part 2 · Module 7: Senior Behaviours](#part-2-module-7-senior-behaviours) — New module: estimating with ranges, legacy changes and decisions made for a team.
- [Part 2 · Pay Attention To](#part-2-pay-attention-to) — New: Part 2's traps in one table of trap, mechanism and fix.
- [Chapter 8: Asynchronous & Concurrent Programming](#chapter-8-asynchronous-concurrent-programming) — Audited: the mechanisms behind `async void`, `WhenAll` and starvation, and 21% shorter.
- [Chapter 5: Design Patterns, Principles & Clean Code](#chapter-5-design-patterns-principles-clean-code) — The Mechanics That Bite is 20% shorter and leaves `WhenAll` to Chapter 8.
- [Chapter 20: Networking & Web Fundamentals](#chapter-20-networking-web-fundamentals) — Socket exhaustion now explains `TIME_WAIT` and the connection-rate ceiling.
- [Chapter 3: ASP.NET Core Web APIs](#chapter-3-aspnet-core-web-apis) — The `IHttpClientFactory` section links the TCP mechanism and fixes the singleton remedy.
- [Chapter 34: Interview Questions & How to Answer Them](#chapter-34-interview-questions-how-to-answer-them) — Diagnosing slowness now shows the starvation fingerprint with current counter names.
- [Chapter 51: The Azure Casebook](#chapter-51-the-azure-casebook-real-incidents-real-fixes) — Case 3 explains the SNAT reclaim delay and clears `SqlConnection` as a suspect.
- [Chapter 9: Messaging & Distributed Systems](#chapter-9-messaging-distributed-systems) — Idempotent consumers now claim the message ID first instead of checking then acting.
- [Chapter 6: Architecture & Application Design](#chapter-6-architecture-application-design) — The idempotency section now claims the key first instead of checking then acting.
- [Chapter 21: Distributed Systems Theory & Reliability Engineering](#chapter-21-distributed-systems-theory-reliability-engineering) — Idempotency claims the key before the effect.
- [Chapter 22: Background Processing, Scheduling & the Actor Model](#chapter-22-background-processing-scheduling-the-actor-model) — New section on async request-reply: `202`, a status resource and `Retry-After`.
- [Chapter 17: Soft Skills & Engineering Practices](#chapter-17-soft-skills-engineering-practices) — New section on finding what to say in a code review.
- [Chapter 17: Soft Skills & Engineering Practices](#chapter-17-soft-skills-engineering-practices) — New section on asking questions that unblock you.
- [Chapter 17: Soft Skills & Engineering Practices](#chapter-17-soft-skills-engineering-practices) — The sections Part 1, Module 5 links were audited and tightened.
- [Chapter 18: The AI-Native Developer](#chapter-18-the-ai-native-developer-thriving-in-the-ai-era) — Corrected: an `async void` exception ends the process instead of being swallowed.
- [Chapter 1: C# Language Mastery](#chapter-1-c-language-mastery) — Audited: equality, nullable gaps, the static `Where` and `Dispose`, with current versions.
- [Chapter 2: .NET Runtime Internals](#chapter-2-net-runtime-internals) — Audited: where a captive instance lives, options validation and release dates.
- [Chapter 2: .NET Runtime Internals](#chapter-2-net-runtime-internals) — Logging now explains what a disabled level still costs.
- [Chapter 3: ASP.NET Core Web APIs](#chapter-3-aspnet-core-web-apis) — Audited: what each wrong middleware order does, and the automatic 400.
- [Chapter 3: ASP.NET Core Web APIs](#chapter-3-aspnet-core-web-apis) — ProblemDetails now shows what reaches the caller and the log, per RFC 9457.
- [Chapter 3: ASP.NET Core Web APIs](#chapter-3-aspnet-core-web-apis) — Cancellation tokens: a token stops only the calls it reaches.
- [Chapter 4: Data Access & Databases](#chapter-4-data-access-databases) — Indexes and execution plans now explain the leftmost-prefix rule, implicit conversions and collation.
- [Chapter 4: Data Access & Databases](#chapter-4-data-access-databases) — The *Find the bug* exercise is now verified, and its answer corrected.
- [Chapter 5: Design Patterns, Principles & Clean Code](#chapter-5-design-patterns-principles-clean-code) — New: reading a stack trace; filler cut and the cost of exceptions flagged for checking.
- [Chapter 7: Testing](#chapter-7-testing) — Audited: why the in-memory provider passes and why test classes race.
- [Chapter 9: Messaging & Distributed Systems](#chapter-9-messaging-distributed-systems) — Audited: the mechanisms behind ordering, dead-lettering and the scope of duplicate detection.
- [Chapter 12: DevOps & CI/CD](#chapter-12-devops-cicd) — Git, Properly Understood now shows the push a rebase breaks.
- [Chapter 13: Observability](#chapter-13-observability) — Audited: the template as event type, levels as a filter, the trace ID as correlation ID.
- [Chapter 13: Observability](#chapter-13-observability) — Corrected: a ratio sampler ignores the parent's decision, so wrap it in `ParentBasedSampler`.
- [Chapter 14: Security](#chapter-14-security) — Audited: what the JWT settings really check, IDOR, and `FromSql` versus `FromSqlRaw`.
- [Chapter 16: Tooling & Productivity](#chapter-16-tooling-productivity) — Audited: where .NET tools live, the diagnostic port and analyzer defaults.
- [Chapter 20: Networking & Web Fundamentals](#chapter-20-networking-web-fundamentals) — Safe versus idempotent methods, and who may retry a request.
- [Chapter 21: Distributed Systems Theory & Reliability Engineering](#chapter-21-distributed-systems-theory-reliability-engineering) — Corrected: chaos strategies sit innermost in a resilience pipeline.
- [Chapter 25: Advanced & Specialized Testing](#chapter-25-advanced-specialized-testing) — Corrected: one HTTP client for the whole load test, and Stryker's `--break-at`.
- [Chapter 26: Real-World Engineering Essentials](#chapter-26-real-world-engineering-essentials) — Audited: DST gaps, two rounding rules and the overloads that pick a culture.
- [Chapter 50: Azure in Depth for .NET Developers](#chapter-50-azure-in-depth-for-net-developers) — Service Bus: the processor's abandon rule and the duplicate-detection window's default.

## Release — September 27, 2026

**🔧 Site & functionality**

- A new part in the sidebar, **Part XIII — The Trusted Advisor**, holds the chapters on client-facing expertise; its lab chapter links to a starter kit in the repository.

**📖 Content updates**

- [Preface & Contents](#the-middle-senior-net-developer-handbook) — An introduction to Part XIII, updated study-time figures, and short bridges from Chapters 17, 28, 34 and 36 to the new part.
- [Chapter 64: The Advisory Casebook](#chapter-64-the-advisory-casebook) — New chapter: twelve composite client situations, each with the tempting wrong move, the advisor's reasoning and words you could use.
- [Chapter 60: Having a Point of View](#chapter-60-having-a-point-of-view) — New chapter: how to form and defend opinions, with an eight-position .NET opinion canon and a personal tech radar.
- [Chapter 61: Discovery and Diagnosis](#chapter-61-discovery-and-diagnosis) — New chapter: diagnose before you prescribe, with a discovery question bank and a one-page problem-statement template.
- [Chapter 63: Recommendations, Proposals and Estimates](#chapter-63-recommendations-proposals-and-estimates) — New chapter: options memos, writing for executives, commercial models, scope traps and estimating for clients.
- [Chapter 65: Positioning and Public Proof](#chapter-65-positioning-and-public-proof) — New chapter: choose a niche, write your positioning statement, and build public proof with a 90-day plan.
- [Chapter 62: Lab — The .NET Health Check](#chapter-62-lab-the-net-health-check) — New lab chapter: assess Microsoft's eShop at a pinned commit and turn the raw findings into a one-page, business-ranked report.

## Release — September 25, 2026

**🔧 Site & functionality**

- A new part in the sidebar, **Part XII — Cloud in Depth: Azure**, holds the Azure deep-dive chapters.

**📖 Content updates**

- [Chapter 50: Azure in Depth for .NET Developers](#chapter-50-azure-in-depth-for-net-developers) — New chapter: identity, compute, storage, Cosmos DB, Azure SQL, messaging, networking and observability explained down to the mechanism, with 15 exam-style self-check questions.
- [Chapter 51: The Azure Casebook](#chapter-51-the-azure-casebook-real-incidents-real-fixes) — New chapter: sixteen real Azure incidents, each with symptoms, cause, how to confirm it and the fix, plus emulator-verified exercises.
- [Preface & Contents](#the-middle-senior-net-developer-handbook) — An introduction to Part XII, updated study-time figures, and a link from Chapter 10 to the new Azure chapters.

## Release — September 24, 2026

**🔧 Site & functionality**

- A new part in the sidebar, **Part XI — The Practice Gym**, collects hands-on labs; each lab chapter links to a starter kit in the repository.
- The Contents page now states reading time and practice time separately.

**📖 Content updates**

- [Chapter 36: The Story Bank & Evidence Portfolio](#chapter-36-the-story-bank-evidence-portfolio) — New lab chapter: turn your work into STAR stories that survive follow-up questions, keep a weekly brag doc, and rehearse with a scored AI mock interviewer.
- [Chapter 34: Behavioral questions](#behavioral-seniority) — A pointer to Chapter 36's worksheet for each behavioral question.
- [Preface & Contents](#the-middle-senior-net-developer-handbook) — Corrected study-time figures, and an introduction to Part XI.
- [Chapter 37: The Slow-Query Lab](#chapter-37-the-slow-query-lab-reading-execution-plans) — New lab chapter: fix nine slow EF Core queries on a 5-million-order PostgreSQL database and prove each fix with before/after plans, with an optional SQL Server track.

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
