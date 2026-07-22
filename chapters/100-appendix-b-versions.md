# Appendix B: .NET Version Comparison Cheat-Sheet

This appendix is a fast, interview-oriented reference for the modern .NET release line (.NET 5 and later — the unified, cross-platform successor to both .NET Core and .NET Framework). It is deliberately shallow: enough to answer "what changed between versions" crisply in an interview, not a migration guide. Every date and support designation below was verified against Microsoft's official support policy and release documentation (see **Sources**).

> **Last verified: July 2026.** This is the fastest-rotting page in the book — a new .NET version ships every November and support windows close on schedule. Before relying on a date here, cross-check the official support policy at `dotnet.microsoft.com/platform/support/policy`.

## Release cadence: how the versioning actually works

Since .NET 5, Microsoft ships one new major version every year in **November**, and the support tier alternates by parity. **Even-numbered versions are LTS (Long-Term Support) and get 3 years of support; odd-numbered versions are STS (Standard-Term Support)**, historically 18 months but **extended to 24 months starting with .NET 9** (announced September 2025). "Support" here means free servicing: security patches and bug fixes. The practical meaning for choosing a production version is simple: an LTS release gives you a stable, patched baseline you can sit on for three years without a forced major upgrade, whereas an STS release is a shorter-lived "latest and greatest" that you must upgrade off of sooner. Teams that value stability and a slow upgrade cadence standardize on LTS; teams that want the newest features immediately and are comfortable upgrading annually can ride STS. Note that both tiers receive the *same* quality of fixes while supported — LTS is not "more tested," it simply lives longer.

## Main comparison table

| Version | Release | Tier | Support ends | C# | Headline themes |
|---------|---------|------|--------------|-----|-----------------|
| .NET 5  | Nov 2020 | STS | May 10, 2022 (ended) | C# 9  | Unifies .NET Core + Framework into one platform; single BCL; big perf push |
| .NET 6  | Nov 2021 | LTS | Nov 12, 2024 (ended) | C# 10 | Minimal APIs; Hot Reload; unified SDK; global usings; first "one .NET" LTS |
| .NET 7  | Nov 2022 | STS | May 14, 2024 (ended) | C# 11 | Native AOT for console apps; large perf gains; rate limiting; .NET MAUI GA |
| .NET 8  | Nov 2023 | LTS | Nov 10, 2026 | C# 12 | Native AOT for ASP.NET Core; Blazor full-stack render modes; keyed DI; perf |
| .NET 9  | Nov 2024 | STS | Nov 10, 2026 | C# 13 | AI building blocks (Microsoft.Extensions.AI); perf; AOT & Blazor refinements |
| .NET 10 | Nov 2025 | LTS | Nov 14, 2028 | C# 14 | C# 14 extension members; JIT/perf; AOT, MAUI, ASP.NET Core maturation |

Notes on a couple of cells that surprise people:
- **.NET 9 ends the same day as .NET 8** (Nov 10, 2026) even though .NET 8 is LTS and .NET 9 is STS. That is the STS-to-24-months change landing exactly on the LTS date — a coincidence of the calendar, not a rule.
- **.NET 5 and .NET 7 are already out of support.** Do not ship new production work on them.

## What each version brought

### .NET 6 (LTS)
- **Minimal APIs** — build a working HTTP endpoint in a handful of lines, no controllers or `Startup.cs` ceremony.
- **Hot Reload** — edit code and see changes without restarting the app.
- **Global usings and implicit usings** (via C# 10) and file-scoped namespaces cut boilerplate.
- First LTS of the fully unified platform; the SDK, runtime, and BCL are one story across Windows, Linux, and macOS.
- Broad performance improvements and better single-file/trimmed publishing.

### .NET 7 (STS)
- **Native AOT** debuts for console applications — compile ahead of time to a native binary with no JIT and no runtime install, yielding fast startup and small memory footprint.
- One of the largest raw-performance releases: hundreds of runtime and library optimizations.
- **Built-in rate limiting** (`System.Threading.RateLimiting`) for ASP.NET Core.
- **.NET MAUI** (the cross-platform UI framework, successor to Xamarin.Forms) reaches general availability.
- Minimal API improvements: filters, typed results, better OpenAPI.

### .NET 8 (LTS)
- **Native AOT expands to ASP.NET Core** — you can now AOT-compile certain web APIs, not just console apps.
- **Blazor unifies** into a single full-stack model with **render modes** (server, WebAssembly, auto) and static server-side rendering, so one Blazor app can mix interactivity strategies.
- **Keyed dependency injection** — register and resolve multiple implementations of a service by key.
- Continued performance gains (notably in the JIT, GC, and core types); new primitives like `TimeProvider` for testable time.
- The current "safe default" LTS for most production workloads until .NET 10 adoption settles.

### .NET 9 (STS)
- **AI building blocks**: `Microsoft.Extensions.AI` gives a common abstraction for chat/embeddings across providers, reflecting .NET's push into LLM/AI app development.
- Further **performance** work across the runtime and the newer garbage-collection tuning.
- Incremental improvements to **Native AOT** (smaller binaries, wider compatibility) and to **Blazor** (better reconnection, static SSR refinements).
- Improved observability and OpenTelemetry integration.
- As an STS, it is a feature preview of where .NET 10 heads — useful, but shorter-lived.

### .NET 10 (LTS)
- **C# 14** with **extension members** — extension properties, static extensions, and a new `extension` block syntax that generalizes the old extension-method model.
- Continued **JIT and runtime performance** improvements (loop optimizations, inlining, devirtualization).
- Maturation of **Native AOT**, **ASP.NET Core**, and **.NET MAUI** rather than a single flagship feature — a "polish and consolidate" LTS.
- The newest LTS baseline; the recommended target for greenfield production apps going forward.

## Interview answers

**"What's the difference between .NET 6 and .NET 8?"**
Both are LTS releases two years apart. .NET 6 was the first unified LTS and introduced minimal APIs and Hot Reload; .NET 8 built on that with Native AOT for ASP.NET Core, the unified Blazor render-mode model, keyed DI, and substantial performance gains. In short, .NET 8 is a faster, more AOT-capable, and more feature-complete evolution of the same platform — and, unlike .NET 6, it is still in support (until November 2026).

**".NET Framework vs .NET (Core)?"**
.NET Framework (up to 4.8.x) is the legacy, Windows-only runtime that ships with Windows and is in maintenance mode — no new major versions. Modern .NET (Core, then .NET 5+) is the cross-platform, open-source, higher-performance rewrite that receives all new investment. New development should target modern .NET; .NET Framework remains only for existing Windows-bound apps.

**"What is LTS?"**
LTS (Long-Term Support) is the even-numbered annual release that Microsoft supports with fixes and security patches for three years — versus STS (Standard-Term Support), the odd-numbered release supported for a shorter window (historically 18 months, now 24 months starting with .NET 9). LTS is the version you pick when you want stability and a slow, predictable upgrade cadence.

**"Should I use .NET 8 or .NET 9 in production?"**
Prefer the LTS. .NET 8 (LTS) and .NET 9 (STS) actually reach end of support on the same date (November 10, 2026), so today the more forward-looking choice for a long-lived app is the newest LTS, **.NET 10**. If you are already on .NET 8, there is no urgency to jump to .NET 9; plan your move to .NET 10 instead.

**"What is Native AOT?"**
Native AOT (Ahead-Of-Time) compiles your app directly to a self-contained native executable at build time, eliminating the JIT and the need for a separate runtime install. The payoff is very fast startup, lower memory use, and small deployment size — ideal for containers, serverless, and CLI tools. The trade-offs are reduced runtime reflection and dynamic-code support and a smaller set of compatible libraries. It arrived for console apps in .NET 7 and expanded to ASP.NET Core in .NET 8.

## The one-line takeaway

**For production, default to the latest LTS** (currently **.NET 10**, supported through November 2028). Choose an STS release only when you specifically need its newest features and you are prepared to upgrade within roughly two years.

## Sources

- Microsoft — *.NET and .NET Core official support policy* (dotnet.microsoft.com/platform/support/policy/dotnet-core) — release dates, LTS/STS designations, and end-of-support dates.
- Microsoft — *The official .NET support policy* (dotnet.microsoft.com/platform/support/policy) — LTS vs STS definitions and annual November cadence.
- .NET Blog — *".NET STS releases supported for 24 months"* (devblogs.microsoft.com/dotnet) — STS support extension from 18 to 24 months, effective .NET 9.
- .NET Blog — *".NET 8 and .NET 9 will reach End of Support on November 10, 2026"* (devblogs.microsoft.com/dotnet) — shared end-of-support date.
- Microsoft Learn — *"What's new in .NET 6 / 7 / 8 / 9 / 10"* and *"What's new in C# 9 through 14"* documentation pages — headline features and C# version mapping.
- Microsoft Learn — *"Native AOT deployment"* documentation — Native AOT capabilities and version history.
