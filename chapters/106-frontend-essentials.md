# Chapter 6: Frontend Essentials

@@TODO: write this chapter's introduction (what it makes the reader able to do, how its sections connect), then remove every @@ line.@@

@@SRC: introduction of old Chapter 29: Frontend & Full-Stack for .NET Developers@@

You can spend a career on the server and be very good at it. But the moment your API meets a browser, a class of decisions lands on your desk that you cannot delegate away: how the client authenticates, what the payloads look like, why the SPA breaks in production but not locally, whether Blazor is a reasonable bet for the next project. A senior .NET developer does not need to be a frontend expert. They need enough literacy to design the boundary well, to talk credibly with the frontend team, and to pick the right UI technology instead of defaulting to whatever is fashionable.

This chapter gives you that literacy. We start with how the web actually works in a browser, move through integrating .NET APIs with JavaScript SPAs, then cover Blazor — plus a brief look at native clients from C# — so you know when a .NET-first UI is the smart choice and when it is not.

@@SRC: old Chapter 20: Networking & Web Fundamentals@@
## Cookies, Sessions, and the Same-Origin Policy

Because HTTP is stateless, **cookies** are how a server plants a small piece of data in the browser that gets sent back automatically on every subsequent request to that domain (via the `Cookie` header). The server sets them with `Set-Cookie`:

```
Set-Cookie: sessionId=abc123; HttpOnly; Secure; SameSite=Lax; Max-Age=3600
```

Those attributes are security-critical:

- **HttpOnly** — JavaScript cannot read the cookie (`document.cookie`), mitigating XSS token theft.
- **Secure** — only sent over HTTPS.
- **SameSite** — controls whether the cookie is sent on cross-site requests. `Lax` (a sensible default) blocks it on most cross-site requests, defending against **CSRF**; `Strict` is tighter; `None` (requires `Secure`) allows cross-site and is needed for some embedded scenarios.

A **session** is the server-side counterpart: the cookie holds only an opaque **session ID**, and the actual state (user identity, cart) lives server-side in a store keyed by that ID. Keep that store *shared* (Redis, SQL) rather than in-process memory, or sessions break the moment a load balancer sends the user to a different server.

### Same-Origin Policy and CORS (recap)

The browser's **Same-Origin Policy (SOP)** is the foundational security boundary of the web. An **origin** is the triple `(scheme, host, port)`. Script on `https://app.example.com` may freely talk to its own origin, but the SOP blocks it from *reading* responses from `https://api.other.com`. Without this, any malicious page you visited could quietly script requests to your bank using your logged-in cookies.

**CORS (Cross-Origin Resource Sharing)** is the *controlled relaxation* of the SOP. The server opts in by returning headers like `Access-Control-Allow-Origin`. For anything beyond a "simple" request, the browser first sends a **preflight** `OPTIONS` request asking permission before the real request. In ASP.NET Core:

```csharp
builder.Services.AddCors(options =>
    options.AddPolicy("api", policy => policy
        .WithOrigins("https://app.example.com")
        .AllowAnyHeader()
        .AllowMethods("GET", "POST")
        .AllowCredentials()));

// ...
app.UseCors("api");
```

> **Pitfall:** CORS is enforced *by the browser*, not the server — it is not an authorization mechanism. A `curl` or a malicious backend ignores it entirely. And `AllowAnyOrigin()` combined with `AllowCredentials()` is invalid (the spec forbids the `*` wildcard with credentials) precisely because it would be a security hole.

@@SRC: old Chapter 29: Frontend & Full-Stack for .NET Developers@@
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

@@SRC: old Chapter 29: Frontend & Full-Stack for .NET Developers@@
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

### Owning the contract: OpenAPI and typed clients

The single highest-leverage thing a backend dev can do for frontend velocity is **publish an accurate OpenAPI (Swagger) document** and let the client be *generated* from it. Hand-written fetch calls drift from the API and break silently. Generated clients break at *compile time* when the contract changes.

ASP.NET Core emits OpenAPI (via the built-in `Microsoft.AspNetCore.OpenApi` in .NET 9+, or Swashbuckle/NSwag). From that document, generate a TypeScript client:

```bash
# NSwag example: OpenAPI -> typed TS client
nswag openapi2tsclient /input:swagger.json /output:src/api-client.ts
```

Now the SPA gets fully typed methods and DTOs. Rename a property on the server, regenerate, and TypeScript flags every broken usage. This is the contract discipline that separates a smooth full-stack team from a finger-pointing one.

**Versioning.** Once external clients depend on you, breaking changes need a strategy. URL versioning (`/api/v1/orders`) is the most visible; header-based versioning keeps URLs clean. Use `Asp.Versioning` to manage it. The rule: additive changes (new optional fields) are safe; removing or retyping fields is a new version.

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

@@SRC: old Chapter 29: Frontend & Full-Stack for .NET Developers@@
## Accessibility: The Part That Is Now Law

Accessibility is the one frontend topic that has moved, in the last few years, from "good practice we should get to" into "a legal requirement with a date attached." Two things drove that.

The **European Accessibility Act** became applicable in June 2025. It obliges a broad set of consumer-facing products and services sold in the EU — e-commerce, banking, transport ticketing, e-books, telecoms — to meet accessibility requirements, with the harmonised standard EN 301 549 pointing at **WCAG 2.1/2.2 level AA**. Public-sector bodies in the EU were already covered by the Web Accessibility Directive; the EAA extends it to the private sector. In the US, Section 508 covers federal procurement, and ADA litigation over inaccessible websites has been a steady feature of the landscape for a decade.

The practical consequence for a backend-leaning developer who occasionally builds UI: **inaccessible markup is now a compliance defect, not a polish item**, and "the designer didn't specify it" stopped being an answer.

### WCAG, and how to actually think about it

WCAG is organised under four principles — the **POUR** acronym — and they are worth knowing as a reasoning tool rather than a checklist:

- **Perceivable.** Can the user receive the information at all? Text alternatives for images, captions for video, sufficient colour contrast, not conveying meaning by colour alone.
- **Operable.** Can they drive it? Everything reachable and usable by keyboard, no traps, enough time, no seizure-inducing flashing, skip links past repeated navigation.
- **Understandable.** Is it predictable? Consistent navigation, labelled inputs, errors identified in text and explained, no surprising context changes on focus.
- **Robust.** Will assistive technology parse it? Valid markup, correct name/role/value for every control.

Conformance comes in levels A, AA, AAA. **AA is the target** — it is what the regulations reference, and AAA includes requirements (like 7:1 contrast) that are not achievable for most designs.

WCAG 2.2 added a handful of criteria worth knowing because they catch modern UI patterns: focus must not be entirely hidden behind sticky headers, drag operations need a single-pointer alternative, click targets need a minimum size, and users must not be forced to re-enter information they already gave you in the same process.

### Semantic HTML first, ARIA second

Almost every accessibility bug I have seen in a .NET shop's UI comes from the same root cause: a `<div>` with a click handler doing the job of a `<button>`.

```html
<!-- Not focusable, not keyboard-operable, no role, no state.
     A screen reader announces nothing useful. -->
<div class="btn" onclick="submit()">Save</div>

<!-- Focusable, Enter/Space activated, announced as a button,
     disabled state understood — all of it for free. -->
<button type="button" onclick="submit()">Save</button>
```

The native element carries a *name*, a *role*, and *state* that browsers and assistive technology already agree on. Recreating that with ARIA means reimplementing focus handling, keyboard activation, `aria-pressed`/`aria-expanded` state, and disabled semantics — correctly, in every browser and screen reader combination.

This is why the **first rule of ARIA** is: don't use ARIA. If a native element or attribute exists with the semantics you need, use it. ARIA adds *semantics* to markup; it never adds *behaviour*. `role="button"` on a `<div>` does not make Enter activate it, does not make it focusable, and does not make it a button — it makes a screen reader announce "button" for something that then does nothing when the user presses Enter, which is worse than saying nothing at all.

> **Pitfall.** The most common harmful pattern is ARIA applied to paper over a structural problem: `aria-label` on a `<div>` acting as a heading, `role="navigation"` on something that should be a `<nav>`, or `aria-live` sprinkled everywhere to force announcements. Bad ARIA is measurably worse than no ARIA — the WebAIM annual survey has consistently found pages using ARIA average *more* detected errors than pages without it.

Reach for ARIA when you genuinely have no native equivalent: a tab set, a combobox with an autocomplete listbox, a tree view, a live region for asynchronous status. And when you do, follow the **ARIA Authoring Practices Guide** patterns exactly — including the keyboard interaction table, which is the part people skip and the part users notice.

### Keyboard operability and focus

Test this today, on the app you are working on: put your mouse down and try to complete your primary user journey. This single exercise finds most of the serious problems.

What to look for:

- **Everything interactive is reachable** by Tab, in an order that matches the visual layout. If you find yourself reaching for `tabindex="3"` to fix the order, the DOM order is wrong — fix that instead. The only `tabindex` values you should normally use are `0` (put this in the natural order) and `-1` (focusable by script only, not by Tab).
- **Focus is visible.** `outline: none` with no replacement is the single most damaging line of CSS for keyboard users. If the default ring is ugly, style `:focus-visible` — don't remove it.
- **Modals trap focus while open, and return it on close.** Open a dialog, Tab through it: focus must not escape to the page behind. When it closes, focus goes back to the element that opened it, or the user is dumped at the top of the document with no idea where they are. The native `<dialog>` element with `showModal()` handles most of this for you.
- **Skip links.** A "skip to main content" link as the first focusable element saves keyboard users from tabbing through forty navigation items on every page.
- **No focus traps you didn't intend** — the classic being an embedded third-party widget you can Tab into but not out of.

### Forms, where it matters most

Forms are where inaccessible UI stops being an inconvenience and starts costing people money.

```html
<!-- The label is programmatically associated: clicking it focuses the
     input, and a screen reader announces the label with the field. -->
<label for="email">Email address</label>
<input id="email" name="email" type="email"
       autocomplete="email"
       aria-describedby="email-hint email-error"
       aria-invalid="true" />
<p id="email-hint">We'll only use this for order updates.</p>
<p id="email-error" class="error">Enter an email address in the format name@example.com.</p>
```

The rules that carry most of the weight: every input has a real `<label>` (placeholder text is not a label — it disappears when the user types and is often too low-contrast); errors are associated with their field via `aria-describedby` and `aria-invalid`, not just coloured red; error text says what to do, not "invalid input"; `autocomplete` attributes are set so browsers and password managers can fill fields; and required fields are marked in text, not only with an asterisk whose meaning is explained in a legend nobody reads.

For asynchronous validation and status messages, an `aria-live="polite"` region announces changes without stealing focus. Use it sparingly and only for genuine status; a live region on a chat log that fires on every keystroke is a torture device.

### Testing it

Automated checking is genuinely useful and genuinely limited, and knowing the ratio matters. Rules-based tools like **axe-core** reliably catch missing alt text, insufficient contrast, unlabelled inputs, duplicate IDs, and invalid ARIA — which is a real slice of the problem, and exactly the slice that regresses silently. Published analyses consistently put automated coverage at **roughly 30–40% of WCAG issues**. The rest — is the alt text *meaningful*, is the focus order *logical*, does the error message actually help, is this custom widget usable with a screen reader — requires a human.

So run both:

- **In CI**, axe-core against your key pages, failing the build on new violations. The wiring is in Chapter 25.
- **By hand, periodically**: the keyboard-only pass described above, a zoom-to-200% pass, and a screen reader pass (NVDA on Windows is free; VoiceOver ships on macOS). Half an hour with a screen reader on your own product is the most effective accessibility training available, and it is uncomfortable in a way that changes how you write markup afterwards.

> **Best practice.** Fix accessibility in your shared components, not in your pages. A design system where the `Button`, `Modal`, `Field` and `Table` components are correct once means hundreds of screens are correct by default — and it turns accessibility from a per-feature tax into a solved infrastructure problem. This is the same leverage argument as any other cross-cutting concern in this book.

@@SRC: old Chapter 29: Frontend & Full-Stack for .NET Developers@@
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
