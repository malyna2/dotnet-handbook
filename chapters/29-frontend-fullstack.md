# Chapter 29: Frontend & Full-Stack for .NET Developers

_⏱️ Estimated read time: ~20 min · 3201 words (study pace)_

You can spend a career on the server and be very good at it. But the moment your API meets a browser, a class of decisions lands on your desk that you cannot delegate away: how the client authenticates, what the payloads look like, why the SPA breaks in production but not locally, whether Blazor is a reasonable bet for the next project. A senior .NET developer does not need to be a frontend expert. They need enough literacy to design the boundary well, to talk credibly with the frontend team, and to pick the right UI technology instead of defaulting to whatever is fashionable.

This chapter gives you that literacy. We start with how the web actually works in a browser, move through integrating .NET APIs with JavaScript SPAs, then cover Blazor — plus a brief look at native clients from C# — so you know when a .NET-first UI is the smart choice and when it is not.

## The Web the Browser Sees

Three languages run in every browser, and they have distinct jobs.

**HTML** is the document structure: a tree of nested elements (`<header>`, `<article>`, `<button>`). **CSS** is presentation: selectors match elements and apply styling rules (`color`, `flex`, `grid`). **JavaScript** is behavior: it runs code, reacts to user input, and mutates the page.

When the browser parses HTML, it builds the **DOM** (Document Object Model), an in-memory tree of objects representing the document. JavaScript does not edit your HTML text; it manipulates this live tree:

```javascript
const btn = document.querySelector("#save");
btn.addEventListener("click", () => {
  document.querySelector("#status").textContent = "Saving...";
});
```

Every visible change on a modern web page is ultimately a DOM mutation. Understanding this one fact demystifies most of frontend.

### The event loop

JavaScript is single-threaded. It has one call stack and processes work from a queue via the **event loop**. Synchronous code runs to completion; asynchronous results (a timer firing, a `fetch` resolving, a click handler) are queued as callbacks and picked up when the stack is empty.

> **Why this matters to you:** a long synchronous loop on the client *freezes the entire UI*, including scrolling and clicks. When a frontend colleague says "the page hangs," they are describing a blocked event loop. And because it is single-threaded, race conditions in JS look different from your `Task`/`lock` world — they are about *ordering of callbacks*, not parallel threads stepping on shared memory.

`async`/`await` in JavaScript is syntactic sugar over Promises (its `Task` equivalent). The mental model transfers cleanly from C#, with one caveat: there is no thread pool doing the waiting — the event loop is.

### SPA vs. the classic request/response

The **traditional web app** (think classic Razor Pages or MVC) renders full HTML on the server for every navigation. Click a link, the browser throws away the current page and loads a new one.

A **Single-Page Application (SPA)** loads once, then takes over navigation itself. JavaScript intercepts clicks, fetches JSON from your API, and re-renders parts of the DOM without a full page reload. React, Angular, and Vue are the dominant frameworks for building SPAs. The upside is app-like fluidity; the cost is complexity, a large initial JavaScript download, and SEO/first-paint challenges.

### Rendering strategies: CSR, SSR, SSG, streaming, hydration

This is the vocabulary you will hear in architecture meetings.

- **CSR (Client-Side Rendering):** the server sends a near-empty HTML shell plus a JS bundle. The browser runs the JS, which renders everything. Fast to deploy, but the user stares at a blank screen until the bundle downloads and executes, and search crawlers may see nothing.
- **SSR (Server-Side Rendering):** the server renders real HTML for the first request, so the user sees content immediately. The JS then loads and takes over.
- **SSG (Static Site Generation):** HTML is rendered once at *build time* and served as static files. Ideal for content that rarely changes (docs, marketing).
- **Streaming SSR:** the server flushes HTML in chunks as it becomes ready, rather than waiting for the whole page. The user sees the header while the slow product list is still being computed.
- **Hydration:** the process where client-side JS "attaches" to server-rendered HTML — wiring up event handlers to already-present DOM — so the static markup becomes interactive. Hydration is where SSR's cost hides: the browser downloads the JS anyway and does bookkeeping to reconcile it with the existing DOM.

> **Best practice:** Match the strategy to the content. A public marketing page wants SSG/SSR for speed and SEO. A logged-in dashboard behind auth can happily be CSR — nobody is crawling it, and interactivity dominates. Do not let one team religion pick this for every screen.

### Bundlers, build tools, and npm

Browsers historically could not load hundreds of small module files efficiently, and they cannot run TypeScript, JSX, or Sass directly. A **bundler** solves this: it walks your import graph, transpiles modern syntax down to what browsers run, tree-shakes dead code, and emits a handful of optimized files.

- **webpack** was the long-standing default: powerful, configurable, and slow on large projects.
- **Vite** is the current favorite: it uses native ES modules for near-instant dev startup and `esbuild`/Rollup for production builds. When someone says "the dev server has hot reload," this is the machinery.

**npm** is the package registry and CLI (like NuGet for JS). `package.json` is the project manifest; `package-lock.json` pins exact versions for reproducible installs. The ecosystem is enormous and shallow — a small app can pull thousands of transitive dependencies.

> **Pitfall:** The npm dependency tree is a real supply-chain surface. Pin versions, commit the lockfile, and treat `npm audit` findings seriously. "It's just a frontend package" is how credential-stealing build scripts get in.

## Integrating a .NET API with a JavaScript SPA

Here is where your expertise actually lives: owning the contract between the ASP.NET Core backend and whatever SPA consumes it.

### CORS

Browsers enforce the **Same-Origin Policy**: JavaScript on `https://app.example.com` cannot, by default, read a response from `https://api.example.com` (different origin). **CORS (Cross-Origin Resource Sharing)** is the server's mechanism to opt specific origins in, via response headers. In ASP.NET Core:

```csharp
builder.Services.AddCors(options =>
    options.AddPolicy("spa", p => p
        .WithOrigins("https://app.example.com")
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials())); // needed for cookies

// ...
app.UseCors("spa");
```

> **Pitfall:** `AllowAnyOrigin()` combined with `AllowCredentials()` is invalid and will silently fail — the spec forbids the wildcard when credentials are sent. Always name explicit origins in production. And remember CORS is a *browser* protection; it does nothing against a non-browser client like curl or your integration tests.

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

### Owning the contract: OpenAPI and typed clients

The single highest-leverage thing a backend dev can do for frontend velocity is **publish an accurate OpenAPI (Swagger) document** and let the client be *generated* from it. Hand-written fetch calls drift from the API and break silently. Generated clients break at *compile time* when the contract changes.

ASP.NET Core emits OpenAPI (via the built-in `Microsoft.AspNetCore.OpenApi` in .NET 9+, or Swashbuckle/NSwag). From that document, generate a TypeScript client:

```bash
# NSwag example: OpenAPI -> typed TS client
nswag openapi2tsclient /input:swagger.json /output:src/api-client.ts
```

Now the SPA gets fully typed methods and DTOs. Rename a property on the server, regenerate, and TypeScript flags every broken usage. This is the contract discipline that separates a smooth full-stack team from a finger-pointing one.

**Versioning.** Once external clients depend on you, breaking changes need a strategy. URL versioning (`/api/v1/orders`) is the most visible; header-based versioning keeps URLs clean. Use `Asp.Versioning` to manage it. The rule: additive changes (new optional fields) are safe; removing or retyping fields is a new version.

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

### A small end-to-end example

The API endpoint (minimal API):

```csharp
app.MapGet("/api/orders/{id:int}", async (int id, IOrderService svc) =>
{
    var order = await svc.GetAsync(id);
    return order is null ? Results.NotFound() : Results.Ok(order);
});
```

The SPA call, using `fetch`:

```typescript
async function loadOrder(id: number): Promise<Order> {
  const res = await fetch(`/api/orders/${id}`, { credentials: "include" });
  if (!res.ok) throw new Error(`Order ${id} failed: ${res.status}`);
  return (await res.json()) as Order;
}
```

`credentials: "include"` sends the auth cookie (the BFF world). `axios` is a popular alternative to `fetch` that adds interceptors and automatic JSON handling, but native `fetch` is entirely sufficient for most needs.

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

## Native Clients from C#: MAUI, Uno, Avalonia

Native desktop and mobile UI is its own discipline, and a backend-leaning book does not need a deep tour of it. What you need is to recognize the three frameworks a .NET shop reaches for, because sooner or later one of them will be calling your API:

- **.NET MAUI** is the evolution of Xamarin.Forms: iOS, Android, Windows, and macOS apps from a single C#/XAML codebase, rendered to real native controls. The typical encounter is a line-of-business mobile app maintained by the same .NET team that owns the backend. (Its **Blazor Hybrid** variant hosts your existing Blazor web components inside the native shell via `BlazorWebView`, trading platform look-and-feel for web-UI reuse.)
- **Uno Platform** targets mobile, desktop, *and* the browser (via WASM) from WinUI/XAML — broader reach than MAUI.
- **Avalonia** is a mature XAML-based cross-platform desktop framework, popular where Linux desktop support matters — a platform MAUI does not target.

The senior-relevant point is that all three are *API consumers*. What they depend on is your side of the boundary: a clean, documented OpenAPI contract; token-based auth flows that work without browser cookies; resilience to flaky mobile networks; and above all versioning discipline — an installed app cannot be force-refreshed like a SPA, so old client versions will hit your API for months. Design that boundary well and the client framework is their choice, not your problem.

## How Much Frontend Should You Actually Learn?

You are optimizing for *effectiveness at the boundary*, not for becoming a frontend engineer. A practical target for a backend-leaning senior:

- **Fluent:** HTML/CSS enough to read a component and make small changes; JavaScript/TypeScript enough to read a SPA, write a `fetch` call, and debug in browser dev tools; the network tab and the console — these are your first stop when "the frontend is broken."
- **Deep:** the API contract. OpenAPI, generated clients, versioning, auth flows (OIDC/PKCE/BFF), CORS, SignalR. This is *your* territory and you should own it decisively.
- **Aware:** how React/Angular/Vue structure an app (components, state, effects) at a level that lets you review PRs and design APIs that fit them well; the rendering strategies and the build pipeline conceptually.

> **The single most valuable investment:** owning the contract boundary. A well-documented, versioned, typed API with clear auth turns frontend integration from a negotiation into a formality. That is where a senior backend dev creates the most cross-team leverage.

### Picking the UI stack

A short decision guide:

1. **Content-heavy, public, SEO-critical?** Server-rendered — Razor Pages/MVC, Blazor Static SSR, or a JS meta-framework with SSR.
2. **Internal line-of-business app, .NET team?** Blazor (Static SSR + interactive islands, or Auto) is a strong, low-friction default.
3. **Rich, public, ecosystem-hungry SPA with JS talent available?** React/Angular/Vue against a REST API, ideally behind a BFF.
4. **Cross-platform desktop/mobile from one C# codebase?** MAUI, or Blazor Hybrid if reusing web UI; Avalonia if Linux desktop matters; native if platform polish is the product.

There is no universally correct answer — there is the answer that fits *this* team, *this* audience, and *this* performance budget. Your job as a senior is to make that tradeoff explicitly rather than by default.

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
