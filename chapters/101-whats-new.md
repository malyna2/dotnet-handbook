# What's New

This page is the handbook's changelog. When a new release lands, a popup announces it on your next visit. Under each release, **Site & functionality** items are plain notes, while **Content updates** link to every chapter that changed — a link is ticked off (✓, stored locally in your browser) once you visit it, so you can work through an update at your own pace and see what's still unread.

## Release — July 27, 2026

**🔧 Site & functionality**

- New **What's New** page (you're reading it): a release popup announces each update on your first visit after it ships, this page sits at the end of the navigation menu, and chapter links below get a locally-stored ✓ once you've opened them.

**📖 Content updates**

- [Chapter 3: ASP.NET Core & Web APIs](#chapter-3-aspnet-core-web-apis) — Major deepening pass on the sections readers found hardest. gRPC now shows the full round trip (generated server base class, client with `await foreach` streaming, why HTTP/2 and its load-balancing consequences, deadlines and `RpcException`). SignalR gains its missing client half (JS snippet, groups, `IHubContext<T>`, a backplane diagram) plus a REST vs gRPC vs SignalR vs SSE decision table. CORS now explains origins and the preflight handshake with a debugging tip. IHttpClientFactory explains the handler-pool mechanism behind the "rotating handlers" claim, with a typed-client-in-singleton pitfall and `AddStandardResilienceHandler()`. Health checks got the full machinery (worst-status-wins aggregation, `Degraded`, a custom `IHealthCheck`, probe-cost pitfalls), and ProblemDetails a tip on `AddExceptionHandler` vs hand-rolled middleware.
