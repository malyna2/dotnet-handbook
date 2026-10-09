<!-- Tick each line with a pointer to evidence, or write "not assessed: [why]". A blank line is worse than "not assessed". -->

**Runtime and end of support** (Ch 24, Appendix)
- [ ] Every target framework listed, with its support phase and end-of-support date from `releases-index.json`
- [ ] Runtime patch level in production vs the latest patch (images, App Service stack, installed runtime)
- [ ] `global.json` pin: does it resolve on the machines and images the team really uses?

**Dependencies** (Ch 27)
- [ ] `dotnet list package --vulnerable --include-transitive`: count by severity, and which are reachable
- [ ] `--deprecated` and `--outdated`: top-level upgrades split into major / minor / patch
- [ ] Prerelease packages in production code, and why
- [ ] Commercially licensed packages and their licence terms

**Supply chain** (Ch 27)
- [ ] Package sources, `packageSourceMapping`, lock files or central transitive pinning
- [ ] NuGet audit warnings: on, and not silenced by `NoWarn`
- [ ] CI actions pinned by SHA; workflow `permissions:` set; Dependabot/Renovate config and whether its PRs get merged
- [ ] Base images: pinned, current, who rebuilds them

**Change hotspots** (Ch 24)
- [ ] Churn x complexity top 10, generated code excluded, for a window that has enough commits
- [ ] Knowledge concentration: hotspot files with one active author

**Architecture and coupling** (Ch 21)
- [ ] Project reference graph; fan-in of shared libraries; cycles
- [ ] Where the domain logic lives; shared databases between services

**Tests** (Ch 8, Ch 25)
- [ ] Do they build and run from a clean clone? How long? Do they pass?
- [ ] What kind (unit / integration / functional / e2e) and where there are none
- [ ] Do the hotspot files have tests?

**Security posture** (Ch 12)
- [ ] Secrets in source or config; how production secrets are supplied
- [ ] Token validation settings (issuer, audience, lifetime, HTTPS metadata)
- [ ] Endpoints without authorization; CORS; raw SQL

**Observability** (Ch 9)
- [ ] Traces, metrics and logs exported; health endpoints; what alerts exist and on what
- [ ] Structured logging (no interpolated log messages); custom business metrics

**Performance** (Ch 17, Ch 7, Ch 4)
- [ ] Sync-over-async, `async void`, `HttpClient` lifetime
- [ ] EF Core query shape: tracking, includes, N+1, pagination (Ch 19 for plans)
- [ ] Any real measurement: p95/p99, error rate, resource use under load

**Build, CI and containers** (Ch 14, Ch 13)
- [ ] What CI builds and tests on every PR; how long; flaky tests
- [ ] Warnings as errors, nullable, analyzers
- [ ] How images are built and deployed; manual steps

**Cost drivers** (Ch 31)
- [ ] Resources the app provisions (AppHost, IaC), and which scale with traffic
- [ ] Paid APIs (AI models, third-party services) and their per-call cost drivers
- [ ] Licences
