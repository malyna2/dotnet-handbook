# Chapter 34: Frontend and Full-Stack in Depth

@@TODO: write this chapter's introduction (what it makes the reader able to do, how its sections connect), then remove every @@ line.@@

@@SRC: old Chapter 3: ASP.NET Core & Web APIs@@
## A Brief Note on Blazor

**Blazor** lets you build interactive web UIs in C# instead of JavaScript. **Blazor Server** runs your components on the server and streams UI diffs to the browser over a SignalR connection — tiny download, but every interaction is a round-trip and each user holds a stateful connection. **Blazor WebAssembly** runs the .NET runtime in the browser and calls your API like any SPA would — offline-capable, at the cost of a larger initial download. From this chapter's perspective, Blazor is just another consumer of your APIs or another host in your pipeline; Chapter 29 covers the render models, JS interop, and when to choose Blazor over a JavaScript SPA.

> **Capstone tie-in:** This chapter is exercised by ShopCore Step 1 (The Honest Monolith) — you'd build a single ASP.NET Core Web API exposing CRUD-plus-checkout endpoints for products, carts, and orders. See Chapter 32.

@@SRC: old Chapter 29: Frontend & Full-Stack for .NET Developers@@
## Integrating a .NET API with a JavaScript SPA — in depth

### Authentication for SPAs

This is the topic most often gotten wrong. Two broad approaches:

**Token-based (bearer tokens in JS).** The SPA obtains an access token (typically a JWT) and sends it in the `Authorization: Bearer` header. Simple to reason about, but the token must live somewhere in the browser. `localStorage` is readable by any JavaScript running on the page, so a single XSS vulnerability leaks it. This is the core weakness.

**Cookie-based.** The session lives in an `HttpOnly`, `Secure`, `SameSite` cookie that JavaScript cannot read and the browser attaches automatically. Immune to token theft via XSS, but you must defend against CSRF.

For obtaining tokens, the modern standard is **OIDC (OpenID Connect) with the Authorization Code flow plus PKCE**. PKCE (Proof Key for Code Exchange) protects the code exchange for public clients that cannot keep a secret — which is every browser app. The implicit flow is deprecated; do not use it.

The pattern the industry now recommends for browser SPAs is the **Backend-for-Frontend (BFF)**:

> **Best practice — the BFF pattern.** Put a lightweight server component (often your ASP.NET Core app) between the SPA and your APIs. The BFF performs the OIDC login, holds the tokens *server-side*, and issues the browser only an `HttpOnly` session cookie. The SPA never touches a token. This eliminates the entire class of token-exfiltration-via-XSS attacks and is the guidance echoed by the OAuth working group and Microsoft's own SPA samples.

Concretely: the SPA calls `/bff/api/orders`, the cookie authenticates the request, and the BFF forwards it to the downstream API with the real access token it kept safely. `Duende.BFF` packages this for .NET.

### API shape: REST vs. GraphQL

**REST** over JSON is the default and the right choice for most systems: resource URLs, HTTP verbs, status codes, cacheable. **GraphQL** lets clients request exactly the fields they need in one round-trip, which shines when you have many clients with divergent data needs or deeply nested graphs. It costs you caching simplicity and adds server complexity (`HotChocolate` is the leading .NET server). Default to REST; reach for GraphQL when field over-fetching across many screens is a demonstrated problem.

### File uploads

Uploads go as `multipart/form-data`, not JSON. On the server:

```csharp
app.MapPost("/api/upload", async (IFormFile file) =>
{
    await using var stream = File.Create(Path.Combine("uploads", file.FileName));
    await file.CopyToAsync(stream);
    return Results.Ok(new { file.FileName, file.Length });
}).DisableAntiforgery(); // or supply the token from the SPA
```

> **Pitfall:** Kestrel and IIS cap request body size (~28-30 MB by default). Large uploads need `RequestSizeLimit` raised, or better, a resumable/chunked strategy or a pre-signed direct-to-blob-storage upload so the file never transits your API at all.

### Real-time with SignalR

Polling wastes resources. For live updates — notifications, dashboards, chat — use **SignalR**, which abstracts WebSockets (falling back to Server-Sent Events / long polling) behind a hub. Server hub:

```csharp
public class NotificationHub : Hub
{
    public Task Broadcast(string message) =>
        Clients.All.SendAsync("ReceiveNotification", message);
}
// app.MapHub<NotificationHub>("/hubs/notifications");
```

JavaScript client (`@microsoft/signalr` from npm):

```typescript
import { HubConnectionBuilder } from "@microsoft/signalr";

const conn = new HubConnectionBuilder().withUrl("/hubs/notifications").build();
conn.on("ReceiveNotification", (msg: string) => showToast(msg));
await conn.start();
```

@@SRC: old Chapter 29: Frontend & Full-Stack for .NET Developers@@
## Blazor: C# in the Browser (and on the Server)

Blazor lets you build interactive web UI in C# and Razor instead of JavaScript. For a .NET team this is compelling — one language, shared models, shared validation. But "Blazor" is really a family of hosting and rendering models, and picking wrong is a common regret.

### The two classic models

**Blazor Server** runs your components on the server. The browser holds a thin JS runtime connected over a SignalR WebSocket; UI events go to the server, C# runs, and a *diff of the DOM* is sent back. Tiny download, full server power and secrets, instant startup — but every interaction is a network round-trip (latency-sensitive), and each user holds an open connection consuming server memory. It scales in the "many concurrent connections" dimension, not the "cheap stateless" dimension.

**Blazor WebAssembly (WASM)** compiles the .NET runtime to WebAssembly and runs your components *entirely in the browser*, like a normal SPA. It works offline, offloads work to the client, and needs only static hosting. The cost is a larger initial download (the runtime) and no direct access to server resources — it calls your API just like a React app would.

### .NET 8+ unified render modes

.NET 8 unified these into one component model with per-component **render modes**, which is how you should think about Blazor today:

- **Static SSR** — components render to HTML on the server with *no interactivity*. Fast, SEO-friendly, great for content pages. This made Blazor a legitimate choice for traditional server-rendered sites.
- **Interactive Server** — the classic Blazor Server model (SignalR circuit), applied per-component.
- **Interactive WebAssembly** — the classic WASM model, per-component.
- **Auto** — starts with Interactive Server for a fast first load, then downloads the WASM runtime in the background and switches to client-side for subsequent visits. Best of both, at the cost of writing components that work under both (no direct server-only calls in interactive code).

> **Best practice:** Default new Blazor Web apps to **Static SSR**, and opt individual components into interactivity only where you need it. Most of a typical app is display; you pay the interactivity tax only on the interactive islands.

### The component model

A Blazor component is a `.razor` file mixing markup and C#. State is just fields; changing them and calling `StateHasChanged` (often implicit) re-renders.

```razor
@* Counter.razor *@
<button class="btn" @onclick="Increment">Clicked @count times</button>

@code {
    [Parameter] public int Step { get; set; } = 1;
    private int count;
    private void Increment() => count += Step;
}
```

`[Parameter]` properties are the inputs (like React props). Components compose, raise `EventCallback`s to parents, and share state via cascading values or injected services. The mental model is close to modern component frameworks — the difference is it is C# all the way down.

### JS interop

Blazor cannot escape JavaScript entirely; the browser's APIs (geolocation, some charting libraries, `localStorage`) are JS. `IJSRuntime` bridges the gap:

```razor
@inject IJSRuntime JS

@code {
    async Task SaveDraft(string text) =>
        await JS.InvokeVoidAsync("localStorage.setItem", "draft", text);
}
```

Interop crosses a serialization boundary and, in WASM, JS calls are async. Use it deliberately, not as a habit — heavy interop erodes Blazor's single-language advantage.

### When Blazor fits, and when a JS SPA is better

**Choose Blazor when:** your team is C#-heavy with little JS depth; you want to share DTOs and validation between client and server; it is a line-of-business app (admin panels, internal tools, dashboards) where the vast npm UI ecosystem is not decisive; and you value not context-switching languages.

**Choose a JS SPA (React/Angular/Vue) when:** you need the deep third-party component ecosystem (rich data grids, mapping, design systems); you are hiring in a market thick with JS talent; you need absolute control over bundle size and first paint for a public, performance-critical site; or you have an existing JS frontend and mobile-web parity matters.

> **Honest caveat:** Blazor WASM's runtime download and Blazor Server's latency/connection model are real constraints, not marketing footnotes. Prototype the *worst* interaction on a *realistic* network before committing an entire product to a model.

@@SRC: old Chapter 29: Frontend & Full-Stack for .NET Developers@@
## Native Clients from C#: MAUI, Uno, Avalonia

Native desktop and mobile UI is its own discipline, and a backend-leaning book does not need a deep tour of it. What you need is to recognize the three frameworks a .NET shop reaches for, because sooner or later one of them will be calling your API:

- **.NET MAUI** is the evolution of Xamarin.Forms: iOS, Android, Windows, and macOS apps from a single C#/XAML codebase, rendered to real native controls. The typical encounter is a line-of-business mobile app maintained by the same .NET team that owns the backend. (Its **Blazor Hybrid** variant hosts your existing Blazor web components inside the native shell via `BlazorWebView`, trading platform look-and-feel for web-UI reuse.)
- **Uno Platform** targets mobile, desktop, *and* the browser (via WASM) from WinUI/XAML — broader reach than MAUI.
- **Avalonia** is a mature XAML-based cross-platform desktop framework, popular where Linux desktop support matters — a platform MAUI does not target.

The senior-relevant point is that all three are *API consumers*. What they depend on is your side of the boundary: a clean, documented OpenAPI contract; token-based auth flows that work without browser cookies; resilience to flaky mobile networks; and above all versioning discipline — an installed app cannot be force-refreshed like a SPA, so old client versions will hit your API for months. Design that boundary well and the client framework is their choice, not your problem.

@@SRC: old Chapter 29: Frontend & Full-Stack for .NET Developers@@
## Accessibility: The Part That Is Now Law — in depth

### Blazor-specific pitfalls

Blazor generates HTML, so everything above applies unchanged. But its component model introduces two problems that catch teams out:

**Route changes don't announce themselves.** In a server-rendered app, navigating to a new page resets focus and the screen reader announces the new document. In an interactive Blazor app (as in any SPA), navigation swaps the DOM and focus stays wherever it was — often on a link that no longer exists. The fix is to move focus to the new page's `<h1>` after navigation and announce the change:

```razor
@inject NavigationManager Nav

<h1 @ref="_heading" tabindex="-1">@Title</h1>

@code {
    private ElementReference _heading;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
            await _heading.FocusAsync();   // tabindex="-1" makes this possible
    }
}
```

Pair it with an `aria-live` region that announces the new page title, so users who don't move with focus still learn where they are.

**Render modes change when your JS runs.** With `InteractiveServer` or `InteractiveWebAssembly`, the page is served as static HTML first and becomes interactive later. Any accessibility behaviour you implemented in `OnAfterRenderAsync` or via JS interop does not exist during that window — and on a slow connection that window is seconds long. Prefer solutions that work in the initial markup (a real `<button>`, a real `<label>`) over ones that depend on interactivity having arrived.

Also: `NavLink` renders an `<a>`, which is correct — but a `NavLink` styled as a button, or an `<a>` with no `href` used as a click target, reintroduces the `div`-as-button problem in Razor syntax. And component libraries vary enormously in accessibility quality; check the one you adopt against a keyboard pass before it is load-bearing across forty screens.

@@SRC: old Chapter 29: Frontend & Full-Stack for .NET Developers@@
## Sources & Further Reading

- **Microsoft Learn — ASP.NET Core Blazor** (hosting models, render modes, components, JS interop): learn.microsoft.com/aspnet/core/blazor
- **Microsoft Learn — .NET MAUI documentation** (single project, Blazor Hybrid): learn.microsoft.com/dotnet/maui
- **Microsoft Learn — Enable CORS in ASP.NET Core** and **API versioning with Asp.Versioning**
- **Microsoft Learn — Overview of ASP.NET Core SignalR**
- **Microsoft Learn — Secure SPAs / Backend-for-Frontend guidance** and **Duende BFF** documentation
- **MDN Web Docs** — HTML, CSS, JavaScript, the DOM, the event loop, Fetch API, CORS, and Same-Origin Policy references: developer.mozilla.org
- **React documentation** (component model, rendering, hydration): react.dev
- **Vite documentation** (dev server, bundling): vitejs.dev
- **OpenAPI Specification** and **NSwag** project documentation (client generation)
- **IETF OAuth 2.0 for Browser-Based Apps** (BFF and PKCE recommendations)
- **Uno Platform** (platform.uno) and **Avalonia UI** (avaloniaui.net) project documentation
