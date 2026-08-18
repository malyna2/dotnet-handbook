# What's New

This page is the handbook's changelog. When a new release lands, a popup announces it on your next visit. Under each release, **Site & functionality** items are plain notes, while **Content updates** link to every chapter that changed — a link is ticked off (✓, stored locally in your browser) once you visit it, so you can work through an update at your own pace and see what's still unread.

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
