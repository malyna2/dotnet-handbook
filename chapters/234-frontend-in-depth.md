# Chapter 34: Frontend and Full-Stack in Depth

[Chapter 6: Frontend Essentials](#chapter-6-frontend-essentials) gave you the browser's mechanisms: the rendering pipeline, the event loop, React's render-and-effect model, CORS and cookies from the browser's side. This chapter is about the decisions a senior full-stack developer makes on top of them, usually for a team: where HTML gets rendered, where state lives and how it is cached, what makes a frontend slow and how to prove it is fast, how to test a UI without a flaky suite, how to keep injected script from running, and how a SPA should authenticate against your API.

The first half is frontend architecture: rendering strategies, state management at scale, performance in depth, testing, and Content Security Policy. The second half is the full-stack boundary seen from the .NET side: SPA authentication and the Backend-for-Frontend, REST versus GraphQL, file uploads, the browser end of SignalR, then Blazor and native clients for when the UI itself is C#, and accessibility in depth. As in Chapter 6, the JavaScript and TypeScript samples run in the repository (`verify/frontend/ch34`), except the few that need a real browser or a running server, which say so.

## Rendering Strategies: CSR, SSR, SSG, Streaming and Hydration

Where the HTML is produced decides what the user sees first, how much JavaScript they must download before the page works, and what your servers do per request. Five terms come up in every architecture meeting.

- **CSR (client-side rendering).** The server sends a near-empty HTML shell and a JavaScript bundle; the browser downloads and runs the bundle, which fetches data from your API and builds the DOM. Hosting is static and cheap. The user stares at a blank or skeleton screen until all of that has happened, and a crawler that doesn't run JavaScript sees nothing.
- **SSR (server-side rendering).** The server runs the components for each request and sends real HTML, so content appears on the first paint. The same JavaScript then loads in the browser to make the page interactive. Every request costs server CPU, and the server needs the data before it can send the page.
- **SSG (static site generation).** The components run once, *at build time*, and the HTML is deployed as static files to a CDN. The fastest possible first byte, but the content is as old as the last build. Incremental variants re-generate pages on a schedule or on demand.
- **Streaming SSR.** Instead of waiting for the slowest data before sending anything, the server flushes HTML in chunks as each part is ready: the header and layout go out immediately, a slow product list follows when its query finishes, with a placeholder until then. React does this with `<Suspense>` boundaries on the server; Blazor's static SSR has *streaming rendering* for the same purpose.
- **Hydration** is the step that turns server-rendered HTML into a working app: the client-side JavaScript runs the same components again, walks the existing DOM, and attaches event handlers and state to it instead of creating new nodes.

```text
           first byte   content visible           interactive
 CSR       fast ──────► after JS download+run+fetch ─► same moment
 SSR       after data ─► on first paint ────────────► after JS download + hydration
 SSG       fastest ────► on first paint ────────────► after JS download + hydration
 Streaming fast ──────► shell first, parts as ready ─► per part, as each hydrates
```

> **Pay attention.** **Why SSR can look done and not respond to clicks.** With SSR the page *looks* ready on the first paint, but nothing is wired up until the JavaScript has downloaded, parsed and hydrated, which on a mid-range phone can take seconds. A click in that window does nothing, or is replayed later. So SSR improves LCP but does not, by itself, improve INP or reduce the JavaScript bill; it moves the cost. The mechanisms that do reduce it ship less JavaScript: *islands* (only interactive parts hydrate; the rest stays static HTML), React Server Components (components that run only on the server and send no JavaScript), and Blazor's static SSR with per-component interactivity.

Hydration has a correctness rule too: the client's first render must produce exactly the HTML the server produced. Anything that differs between the two environments breaks it:

```tsx
function LastUpdated() {
  return <p>Updated at {new Date().toISOString()}</p>;   // a different value on server and client
}
```

Rendered on the server at 10:00:00 and hydrated two seconds later, the text no longer matches. React reports a *hydration mismatch* as a recoverable error, throws away the server's HTML for that part and renders it again on the client, so you pay for the server render and the client render, and the content flickers (the repository's test shows the error and the client value replacing the server's). The usual culprits are times and dates (including time zones), random IDs, `window` or `localStorage` checks during render, and locale-dependent formatting. The fix is to render the same thing in both places, by passing the server's value down as data, or to render the environment-specific part only after hydration in an effect. `suppressHydrationWarning` silences a single unavoidable case, such as a timestamp, and React does not patch the text in that case.

| Content | Strategy |
|---|---|
| Marketing pages, docs, blog: public, changes rarely | SSG (with incremental regeneration if it changes daily) |
| Public, SEO-relevant, per-request data (product pages, search results) | SSR, streaming if some data is slow |
| Logged-in dashboard or line-of-business app | CSR is fine: nobody crawls it and interactivity dominates |
| Mostly static page with a few interactive widgets | Islands, or Blazor static SSR with interactive components |

> **Best practice.** Choose per route, not per company. A meta-framework (Next.js, Nuxt, Angular's SSR, Blazor Web Apps) lets the marketing pages be static, the product pages server-rendered and the account area client-rendered in one codebase. And measure before and after: SSR's server cost and hydration cost are real, so the decision belongs to LCP and INP numbers from real users, not to fashion.

## State Management at Scale

Most "state management" arguments mix two different kinds of state, and they need different tools.

- **Server state** is a client-side *copy* of data the server owns: the order list, the user's profile, today's prices. It can be out of date the moment it arrives, other users change it, and it must be fetched, cached, deduplicated, refreshed and invalidated. This is a caching problem, the same one [Chapter 18](#chapter-18-data-in-depth) discusses on the server.
- **Client state** exists only in the browser: which modal is open, the text in a half-filled form, the selected tab, a theme. It is owned by the UI, never stale, and usually local to a component.

Hand-written server state, a `useEffect` that fetches into `useState`, is where most SPA bugs live: the race from Chapter 6's exercise, a missing loading or error state, two components that each fetch the same list, a list that never refreshes after an edit. **Data-fetching libraries** treat server state as a cache keyed by a *query key*. TanStack Query (React, Vue and others), SWR, RTK Query and Angular's resource APIs all work on this principle:

```tsx
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";

type Order = { id: number; status: string };

function useOrders() {
  return useQuery({
    queryKey: ["orders"],                       // the cache key: same key, same cache entry
    queryFn: async (): Promise<Order[]> => {
      const res = await fetch("/api/orders");
      if (!res.ok) throw new Error(`Orders failed: ${res.status}`);
      return res.json();
    },
    staleTime: 30_000,                          // fresh for 30 s: no refetch on mount or focus
  });
}

function OrderCount() {
  const { data, isPending, error } = useOrders();
  if (isPending) return <p>Loading…</p>;
  if (error) return <p role="alert">{error.message}</p>;
  return <p>{data.length} orders</p>;
}

function CancelButton({ id }: { id: number }) {
  const queryClient = useQueryClient();
  const cancel = useMutation({
    mutationFn: () => fetch(`/api/orders/${id}/cancel`, { method: "POST" }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["orders"] }),  // refetch the list
  });
  return <button onClick={() => cancel.mutate()} disabled={cancel.isPending}>Cancel</button>;
}
```

The mechanism, as the repository's tests show it: two `OrderCount` components mounted together send **one** request, because both subscribe to the `["orders"]` entry; a third mounted within `staleTime` is served from the cache with no request; and the cancel mutation marks the entry stale, which refetches it for every subscriber. The library also handles what hand-written code forgets: request cancellation when a query is no longer needed, retries, and the loading and error states.

Its defaults are deliberately aggressive, and they explain the "why does it call my API so often" question you will get from the backend side. Cached data is **stale immediately** (`staleTime` 0), and stale queries refetch in the background when a new component mounts, when the window regains focus, and when the network reconnects; inactive entries are garbage-collected after 5 minutes; failed queries retry 3 times with exponential backoff. Every one of those refetches is a request to your API. Setting `staleTime` per query, from how often the data really changes, is the single most effective way to cut that traffic, and an API that sends `ETag`s lets the refetches that remain be cheap `304`s.

For client state, start with the simplest place that works and move up only when a real problem appears:

| Client state shared by | Put it in |
|---|---|
| One component | `useState` (Angular: a signal in the component) |
| A parent and a few children | Lift it to the parent, pass props down |
| A whole subtree, changing rarely (theme, current user, locale) | React context (Angular: a service) |
| Many distant components, changing often | A store: Redux Toolkit, Zustand; in Angular, a signal-based service or NgRx |
| Anything that should survive a reload or be shareable | The URL: route parameters and query string |

Context has a cost that decides the fourth row: every component that reads a context re-renders when its value changes, so a frequently changing value in a context near the root re-renders much of the app. Stores let each component subscribe to just the slice it reads. And the URL is underused state storage: filters, sort order, the selected tab and pagination belong in the query string, so Back, Refresh and a shared link all work.

> **Pitfall.** Copying server data into a global store "so every component can see it" recreates the cache problem by hand, without invalidation: the store holds whatever was fetched first, and every mutation must remember to update it. Keep server state in the data-fetching cache and client state in the store, and the store usually turns out to be small.

## Frontend Performance in Depth

[Chapter 6: Core Web Vitals](#core-web-vitals) defined the three numbers. This section is about what moves them, and how to show that a change did.

### Bundle Size Is Main-Thread Time

JavaScript is the most expensive byte on the web. An image is downloaded and decoded, mostly off the main thread. A script is downloaded, then **parsed, compiled and executed on the main thread**, the same thread that handles input and rendering. A large bundle therefore costs twice: it delays the first render of a CSR app (LCP) and it occupies the main thread while users try to interact (INP). Compression helps the download; it does nothing for parse and execution, which scale with the uncompressed size.

Three habits keep the bundle honest:

- **Look at it.** A bundle visualiser for your bundler (for Vite, `rollup-plugin-visualizer` works because Vite's plugin API follows Rollup's) shows which packages take the space. The usual finds: a date or utility library imported whole, two versions of the same dependency, a charting or editor library on every page.
- **Help tree-shaking.** The bundler can drop unused exports only from ES modules whose imports have no side effects. CommonJS packages, and packages without `"sideEffects": false` in their `package.json`, are often included whole. Import the specific function (`import debounce from "lodash-es/debounce"`), not the namespace.
- **Set a budget and enforce it in CI.** A size limit per entry chunk, checked on every pull request, catches the 300 KB dependency in review rather than in production.

### Code Splitting

Most users never visit most routes, so most of the code should not be in the first download. A dynamic `import()` tells the bundler "this module, and what only it needs, goes into a separate chunk, fetched when this line runs". React's `lazy` connects that to rendering, and `<Suspense>` shows a fallback while the chunk loads:

```tsx
import { lazy, Suspense } from "react";

const ReportsPage = lazy(() => import("./ReportsPage"));   // its own chunk, fetched on first render

function App({ route }: { route: string }) {
  return (
    <Suspense fallback={<p>Loading…</p>}>
      {route === "/reports" ? <ReportsPage /> : <p>Home</p>}
    </Suspense>
  );
}
```

The repository's tests show both halves: rendering the home route never loads the `ReportsPage` module, and the first render of `/reports` shows the fallback, loads the module, then renders the page; and a production `vite build` of this app emits `ReportsPage` as a separate chunk with a content hash in its file name. Angular does the same with `loadComponent: () => import(...)` in a route definition. Split by route first: it is the natural boundary and the one routers support directly. The trade-off is a request on first navigation to a route; preloading the likely next chunk on hover or when the browser is idle hides it.

### Images, Fonts and Caching

Images are usually the LCP element, and they cause most layout shift.

- **Give every image its dimensions** (`width` and `height` attributes, or a CSS `aspect-ratio`), so the browser reserves the space before the file arrives. This alone fixes most CLS.
- **Serve the right size and format.** `srcset` and `sizes` let the browser pick a file for the viewport and pixel density; AVIF and WebP are much smaller than JPEG and PNG for photos.
- **Lazy-load below the fold, never the LCP image.** `loading="lazy"` defers off-screen images, which is right for a product grid and wrong for the hero image, which should instead get `fetchpriority="high"` so it is requested before less important resources.
- **Fonts**: a web font that arrives late either hides text or swaps it, changing its size; `font-display` chooses between the two, and preloading the one font the first screen needs shortens the wait.

```html
<img src="/img/hero-1200.avif" srcset="/img/hero-800.avif 800w, /img/hero-1200.avif 1200w"
     sizes="100vw" width="1200" height="600" fetchpriority="high" alt="Spring collection">
<img src="/img/p-42.webp" width="300" height="300" loading="lazy" alt="Linen shirt, blue">
```

Caching is where the backend makes the frontend fast. Vite's content-hashed file names mean an asset's URL changes whenever its content does, so assets can be cached for a year as `immutable`, while `index.html`, which references the current hashes, must be revalidated on every load. Get it the wrong way round and users run yesterday's app until their cache expires. In ASP.NET Core, set this in the static file options (`OnPrepareResponse` lets you set `Cache-Control` per file), or at the CDN.

### Fixing INP

INP is the delay between an interaction and the next paint, and the cause is almost always a **long task**: JavaScript keeping the main thread busy for more than about 50 ms, so the browser cannot paint the response to the click. The tools, in the order to try them:

- **Do less per interaction.** In React, a state change high in the tree re-renders everything below it; moving state down, or splitting a component so the expensive part doesn't depend on the changing state, often removes most of the work. `memo`, `useMemo` and `useCallback` skip re-rendering when inputs are unchanged, but they are a scalpel, not a default.
- **Render less.** A table of 5,000 rows creates 5,000 rows of DOM. *Virtualisation* (react-window, TanStack Virtual, Angular CDK's virtual scroll) renders only the visible rows, and server-side pagination, your side of the API, often removes the need.
- **Yield.** Split unavoidable long work into chunks so the browser can paint between them (Chapter 6's event-loop section), or mark a non-urgent update as a transition (React's `startTransition`) so typing stays responsive while a large list re-renders.
- **Move it off the thread.** Parsing, sorting or crypto on large data can go to a Web Worker.

### Measuring

Lab tools and field data answer different questions, and senior work uses both.

- **Lighthouse** (in Chrome DevTools, or Lighthouse CI on every pull request) loads the page once under simulated throttling and reports LCP, CLS, Total Blocking Time and a list of opportunities. Repeatable, so it is good for regressions; synthetic, so a score is not your users' experience, and it cannot measure INP because nobody clicks. Total Blocking Time is its stand-in for responsiveness.
- **The DevTools Performance panel** records a trace: every task on the main thread, with long tasks flagged, layout and paint events, and which interaction was slow. This is where you find *why*.
- **Field data** comes from your real users' browsers. The `web-vitals` library reports each metric with the element or interaction responsible; send it to an endpoint you own and aggregate the 75th percentile per page and per device class:

```typescript
import { onCLS, onINP, onLCP, type Metric } from "web-vitals";

function report(metric: Metric) {
  const body = JSON.stringify({ name: metric.name, value: metric.value, rating: metric.rating,
                                id: metric.id, page: location.pathname });
  navigator.sendBeacon("/api/vitals", body);   // survives the page being closed
}

onCLS(report);
onINP(report);
onLCP(report);
```

(This one needs a real browser's performance APIs, so the repository does not run it.) The receiving endpoint is a few lines of ASP.NET Core writing to your metrics pipeline ([Chapter 25](#chapter-25-observability-and-testing-at-scale)); Chrome's public user-experience report gives the same three numbers for public sites with enough traffic, without any code.

> **Best practice.** Make one change, measure it in the field, then make the next. Frontend performance work is full of plausible changes that do nothing (or move the cost elsewhere, like SSR moving it to hydration), and the 75th-percentile field number is what users, and search ranking, see.

## Frontend Testing

The testing ideas from [Chapter 8: Testing](#chapter-8-testing) and the test-suite shape from [Chapter 25](#chapter-25-observability-and-testing-at-scale) apply unchanged; what differs is the tooling, and one principle that the tooling is built around.

**Unit tests** cover logic with no UI: formatting, validation rules, reducers, the mapping from a ProblemDetails body to field errors. They are plain functions run by Vitest or Jest, fast and stable, and most frontend logic should be pulled out of components into such functions so it can be tested this way.

**Component tests** render one component (or a small tree) into a simulated DOM, jsdom or happy-dom, act on it, and check what a user would see. **Testing Library** (for React, Angular, Vue and others) is the standard, and its principle is to test the way the user uses the page: find elements by **role and accessible name**, by label text, by visible text, never by CSS class or component internals:

```tsx
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { OrderList } from "./OrderList";

test("cancelling an order reports that order's id", async () => {
  const onCancel = vi.fn();
  render(<OrderList orders={[{ id: 7, customer: "Ada", total: 12 }]} onCancel={onCancel} />);

  await userEvent.click(screen.getByRole("button", { name: "Cancel" }));   // as a user would

  expect(onCancel).toHaveBeenCalledWith(7);
});
```

This is a real test of Chapter 6's `OrderList`, and it runs in the repository. `getByRole("button", { name: "Cancel" })` fails if the button becomes a `<div>` or loses its label, so the test checks accessibility as a side effect, exactly like Playwright's role locators in Chapter 25. `user-event` simulates the whole sequence a real click produces (pointer events, focus, click), where the lower-level `fireEvent` dispatches one event. Network calls are replaced at the network layer, not by mocking your own modules: stub `fetch`, or use Mock Service Worker, which intercepts requests and returns canned responses, so the component's real data-fetching code runs.

> **Gotcha.** jsdom is not a browser. It has a DOM and events but no layout engine, so sizes are zero, nothing is ever "visible" by geometry, and CSS media queries don't apply. Component tests prove behaviour and markup; anything about layout, real rendering or cross-browser behaviour belongs to end-to-end tests.

**End-to-end tests** drive a real browser against the running app and API. **Playwright** is the default for new work, in TypeScript on the frontend side or in C# ([Chapter 25: Playwright for .NET](#playwright-for-net) covers auto-waiting, role locators, traces and the axe-core integration, all of which are the same API in both languages):

```typescript
import { expect, test } from "@playwright/test";

test("a customer can place an order", async ({ page }) => {
  await page.goto("/products/42");
  await page.getByRole("button", { name: "Add to basket" }).click();
  await page.getByRole("link", { name: "Basket" }).click();
  await page.getByRole("button", { name: "Place order" }).click();
  await expect(page.getByRole("heading", { name: "Thank you" })).toBeVisible();
});
```

(The repository does not run this one: it needs browsers and a deployed app.) Keep the end-to-end layer to a handful of journeys that make money or would make the news if they broke; push everything else down to component and unit tests, which are faster and fail for one reason.

Two more kinds of test catch what those don't. **Contract drift** between SPA and API is best caught at compile time, by the client generated from your OpenAPI document in CI (Chapter 6), and a schema-diff step that flags breaking changes in the document itself ([Chapter 22](#api-versioning-backward-compatibility)). **Visual regression** tests (Playwright's screenshot comparison, Storybook-based services) catch the CSS change that broke a layout no assertion looks at; they need stable fonts and data, or they become the flaky suite everyone ignores.

## Content Security Policy

[Chapter 12: Security Headers](#security-headers) introduced CSP as the header that limits what injected script can do, and [Chapter 6: XSS from the Front End](#xss-from-the-front-end) showed how the injection happens. This is how a CSP actually works, and how to deploy one for a SPA without breaking it.

**The mechanism.** A `Content-Security-Policy` response header gives the browser rules about which resources the page may load and execute: `script-src` for scripts, `style-src` for styles, `connect-src` for `fetch` and WebSocket targets, `img-src`, `frame-ancestors` for who may embed the page, and so on. Anything not allowed is blocked and reported in the console. Once a policy has a `script-src` without `'unsafe-inline'`, **inline script stops running**: `<script>` blocks without a nonce or hash, `onclick="…"` attributes, and `javascript:` URLs. Most injected XSS payloads are exactly those, so they are inert even when the injection itself succeeds.

**Allowlists don't work; nonces and hashes do.** The obvious policy lists the domains you load scripts from (`script-src 'self' https://cdn.example.com …`). Research and experience show these policies are usually bypassable, because an allowed domain hosts something an attacker can abuse (an old library, a JSONP endpoint), and they grow unmaintainable. The recommended **strict CSP** trusts scripts by a per-response secret instead:

```text
Content-Security-Policy:
  script-src 'nonce-R4nd0mPerResponse' 'strict-dynamic';
  object-src 'none';
  base-uri 'none'
```

A script runs only if its tag carries `nonce="R4nd0mPerResponse"`, a random value the server generates for **every response**, so an attacker who injects a `<script>` tag can't know it. `'strict-dynamic'` extends that trust to scripts loaded *by* a trusted script, which is what makes bundlers' dynamic chunks and most third-party loaders work, and it makes the browser ignore host allowlists. Instead of a nonce, a policy can list the **hash** of each allowed script (`'sha256-…'`), which suits static pages whose scripts never change.

**SPAs and the nonce problem.** A nonce must be new on every response, so `index.html` can't be a static file served from a CDN. Two workable designs:

- **Serve `index.html` through ASP.NET Core** and stamp the nonce into it. The server generates the nonce, sends it in the header, and writes it into the script tags of the HTML it returns. This is safe because `index.html` is your build output, not user content: stamping a nonce into HTML that contains user input would bless injected tags too.
- **Static hosting with hashes.** Vite's build output loads the app with external module scripts and, in a typical setup, no inline script, so a hash-based or `'self'`-based policy can work from a CDN; it is weaker than `'strict-dynamic'` with nonces, but much better than none.

```csharp
app.Use(async (context, next) =>
{
    var nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));   // new for every response
    context.Items["csp-nonce"] = nonce;
    context.Response.Headers.ContentSecurityPolicy =
        $"script-src 'nonce-{nonce}' 'strict-dynamic'; object-src 'none'; base-uri 'none'";
    await next();
});

app.MapFallback(async context =>            // index.html for every client-side route, nonce stamped in
{
    var html = await File.ReadAllTextAsync(Path.Combine(app.Environment.WebRootPath, "app.html"));
    var nonce = (string)context.Items["csp-nonce"]!;
    context.Response.ContentType = "text/html; charset=utf-8";
    await context.Response.WriteAsync(html.Replace("<script ", $"<script nonce=\"{nonce}\" "));
});
```

(Checked against ASP.NET Core 10: each response gets a new nonce in both the header and the script tag. The template is named `app.html` so the static-file middleware never serves it un-stamped.)

**Roll it out in report-only mode.** `Content-Security-Policy-Report-Only` applies the policy without enforcing it and reports each violation, to the console and to an endpoint you declare with the `report-to` directive. Run it for a few weeks, fix or allow what legitimately breaks (an analytics snippet, a chat widget, inline styles from a component library), then switch the same policy to enforcing. Both headers can be sent at once, which is how you trial a stricter policy while the current one stays enforced.

**Trusted Types** close the remaining hole, DOM XSS in your own code. With `require-trusted-types-for 'script'` in the policy, the browser refuses plain strings at the dangerous sinks (`innerHTML`, `eval`, script `src`) and accepts only values produced by a policy function you define, typically one that runs a sanitizer. Support differs between browsers (check MDN before relying on it); where it is supported, it turns "remember never to pass user text to `innerHTML`" from a code-review rule into an error.

> **Pitfall.** `'unsafe-inline'` in `script-src` disables most of CSP's protection against XSS, and `'unsafe-eval'` re-enables `eval`. Both get added "temporarily" to unbreak a page and never removed. When a page breaks under the policy, find the inline script or the `eval` and move it into a file or give it a nonce; and keep an `object-src 'none'` and `base-uri 'none'` in every policy, since plugins and a hijacked `<base>` tag are classic bypasses.

## Integrating a .NET API with a JavaScript SPA — in depth

### Authentication for SPAs

This is the topic most often gotten wrong. There are two broad approaches, and [Chapter 6](#cookies-localstorage-and-sessionstorage-where-browser-state-lives) showed the storage trade-off behind them.

**Token-based (bearer tokens in JavaScript).** The SPA obtains an access token (typically a JWT) and sends it in the `Authorization: Bearer` header. Simple to reason about, but the token must live somewhere in the browser, and anything JavaScript can read, an XSS bug or a compromised dependency can read too. That is the core weakness.

**Cookie-based.** The session lives in an `HttpOnly`, `Secure`, `SameSite` cookie that JavaScript cannot read and the browser attaches automatically. Immune to token theft through XSS, but you must defend against CSRF.

For obtaining tokens, the standard is **OIDC with the Authorization Code flow plus PKCE** ([Chapter 12: Authorization Code Flow with PKCE](#authorization-code-flow-with-pkce)); the implicit flow is deprecated. The pattern now recommended for browser SPAs combines the two approaches:

> **Best practice — the BFF pattern.** Put a lightweight server component (often your ASP.NET Core app) between the SPA and your APIs. The BFF performs the OIDC login as a confidential client, holds the tokens *server-side*, and gives the browser only an `HttpOnly` session cookie. The SPA never touches a token, which removes the whole class of token-exfiltration-through-XSS attacks; the IETF draft *OAuth 2.0 for Browser-Based Applications* strongly recommends this architecture for business and sensitive applications and those handling personal data.

Concretely: the SPA calls `/bff/api/orders`, the cookie authenticates the request, and the BFF forwards it to the downstream API with the access token it kept. `Duende.BFF` packages this for .NET, and YARP can do the forwarding. The SPA and the BFF should share an origin, so cookies are first-party and no CORS is needed; the BFF then needs CSRF protection on its endpoints. The same IETF draft's approach is to require a custom request header: a cross-site page can only send one through a CORS preflight, which the BFF refuses. [Chapter 21: API Gateway and Backend for Frontend](#api-gateway-and-backend-for-frontend) covers the BFF as an architectural pattern beyond authentication.

### API Shape: REST vs. GraphQL

**REST** over JSON is the default and the right choice for most systems: resource URLs, HTTP verbs, status codes, HTTP caching. **GraphQL** lets clients request exactly the fields they need in one round-trip, which shines when you have many clients with divergent data needs or deeply nested graphs. It costs you HTTP caching (most queries are `POST`s to one URL) and adds server complexity: query cost limits, N+1 resolution handled by batching data loaders, and authorization per field (`HotChocolate` is the leading .NET server). Default to REST; reach for GraphQL when over-fetching across many screens is a demonstrated problem, or put a BFF in front of REST services that shapes responses per screen.

### File Uploads

Uploads go as `multipart/form-data`, not JSON; from the browser that is a `FormData` body with no hand-written `Content-Type` (Chapter 6). On the server:

```csharp
app.MapPost("/api/upload", async (IFormFile file) =>
{
    var name = $"{Guid.NewGuid()}{Path.GetExtension(file.FileName)}";   // never trust the client's name
    await using var stream = File.Create(Path.Combine("uploads", name));
    await file.CopyToAsync(stream);
    return Results.Ok(new { name, file.Length });
}).DisableAntiforgery(); // or supply the token from the SPA
```

The client's file name is attacker-controlled (`../../appsettings.json` is a file name too), so generate your own and keep the original only as display metadata.

> **Pitfall:** Kestrel and IIS cap request body size (~28-30 MB by default). Large uploads need `RequestSizeLimit` raised, or better, a resumable/chunked strategy or a pre-signed direct-to-blob-storage upload so the file never transits your API at all.

### Real-time with SignalR

For live updates (notifications, dashboards, chat), SignalR pushes from server to browser over WebSockets, falling back to Server-Sent Events or long polling. The hub, groups, `IHubContext` and scaling with a backplane are in [Chapter 22: SignalR](#signalr). The browser end is the `@microsoft/signalr` npm package, and in a SPA the part to get right is the connection's lifecycle, which is an effect from [Chapter 6](#effects-and-their-dependency-arrays): open on mount, close in the cleanup.

```tsx
import { HubConnectionBuilder } from "@microsoft/signalr";
import { useEffect, useState } from "react";

function Notifications() {
  const [messages, setMessages] = useState<string[]>([]);

  useEffect(() => {
    const conn = new HubConnectionBuilder()
      .withUrl("/hubs/notifications")              // same origin: the auth cookie goes along
      .withAutomaticReconnect()
      .build();
    conn.on("notify", (msg: string) => setMessages((m) => [...m, msg]));
    conn.start().catch((err) => console.error("SignalR connection failed", err));
    return () => { conn.stop(); };                 // unmount, or StrictMode's dev re-run
  }, []);

  return <ul>{messages.map((m, i) => <li key={i}>{m}</li>)}</ul>;
}
```

(Not run in the repository: it needs a running hub.) Without the cleanup, every remount leaks a connection, and StrictMode's development re-run opens two. `withAutomaticReconnect` retries on a schedule, but messages sent while disconnected are lost, so a dashboard should refetch its data when the connection comes back (the `onreconnected` callback). Bearer-token APIs can't use headers on a WebSocket from the browser, so the client passes the token through `accessTokenFactory` and SignalR sends it in the query string, which the server must then read; with a BFF and a same-origin cookie, none of that is needed. (Index keys are acceptable in this list because messages are only ever appended.)

## Blazor: C# in the Browser (and on the Server)

Blazor lets you build interactive web UI in C# and Razor instead of JavaScript. For a .NET team this is compelling: one language, shared models, shared validation. But "Blazor" is really a family of hosting and rendering models, and picking wrong is a common regret. Its render modes map directly onto the rendering strategies at the start of this chapter.

### The Two Classic Models

**Blazor Server** runs your components on the server. The browser holds a thin JS runtime connected over a SignalR WebSocket; UI events go to the server, C# runs, and a *diff of the DOM* is sent back. Tiny download, full server power and secrets, instant startup — but every interaction is a network round-trip (latency-sensitive), and each user holds an open connection consuming server memory. It scales in the "many concurrent connections" dimension, not the "cheap stateless" dimension.

**Blazor WebAssembly (WASM)** runs the .NET runtime in WebAssembly and runs your components *entirely in the browser*, like a normal SPA. It works offline, offloads work to the client, and needs only static hosting. The cost is a larger initial download (the runtime) and no direct access to server resources — it calls your API just like a React app would.

### .NET 8+ Unified Render Modes

.NET 8 unified these into one component model with per-component **render modes**, which is how you should think about Blazor today:

- **Static SSR** — components render to HTML on the server with *no interactivity*: server-side rendering with no hydration at all, with optional streaming rendering for slow data. Fast, SEO-friendly, great for content pages. This made Blazor a legitimate choice for traditional server-rendered sites.
- **Interactive Server** — the classic Blazor Server model (SignalR circuit), applied per-component.
- **Interactive WebAssembly** — the classic WASM model, per-component.
- **Auto** — starts with Interactive Server for a fast first load, then downloads the WASM runtime in the background and switches to client-side for subsequent visits. Best of both, at the cost of writing components that work under both (no direct server-only calls in interactive code).

> **Best practice:** Default new Blazor Web apps to **Static SSR**, and opt individual components into interactivity only where you need it. Most of a typical app is display; you pay the interactivity tax only on the interactive islands, which is the islands architecture from the rendering section, in C#.

### The Component Model

A Blazor component is a `.razor` file mixing markup and C#. State is just fields; changing them and calling `StateHasChanged` (often implicit, after an event handler) re-renders.

```razor
@* Counter.razor *@
<button class="btn" @onclick="Increment">Clicked @count times</button>

@code {
    [Parameter] public int Step { get; set; } = 1;
    private int count;
    private void Increment() => count += Step;
}
```

`[Parameter]` properties are the inputs (React's props). Components compose, raise `EventCallback`s to parents, and share state via cascading values or injected services. The mental model is the one Chapter 6 taught for React — render from state, diff, patch the DOM — with C# all the way down; `@key` plays the role of React's `key` in lists.

### JS Interop

Blazor cannot escape JavaScript entirely; the browser's APIs (geolocation, some charting libraries, `localStorage`) are JS. `IJSRuntime` bridges the gap:

```razor
@inject IJSRuntime JS

@code {
    async Task SaveDraft(string text) =>
        await JS.InvokeVoidAsync("localStorage.setItem", "draft", text);
}
```

Interop crosses a serialization boundary and is asynchronous (in Interactive Server it is a network round-trip). Use it deliberately, not as a habit — heavy interop erodes Blazor's single-language advantage.

### When Blazor Fits, and When a JS SPA Is Better

**Choose Blazor when:** your team is C#-heavy with little JS depth; you want to share DTOs and validation between client and server; it is a line-of-business app (admin panels, internal tools, dashboards) where the vast npm UI ecosystem is not decisive; and you value not context-switching languages.

**Choose a JS SPA (React/Angular/Vue) when:** you need the deep third-party component ecosystem (rich data grids, mapping, design systems); you are hiring in a market thick with JS talent; you need absolute control over bundle size and first paint for a public, performance-critical site; or you have an existing JS frontend and mobile-web parity matters.

> **Honest caveat:** Blazor WASM's runtime download and Blazor Server's latency/connection model are real constraints, not marketing footnotes. Prototype the *worst* interaction on a *realistic* network before committing an entire product to a model.

## Native Clients from C#: MAUI, Uno, Avalonia

Native desktop and mobile UI is its own discipline, and a backend-leaning book does not need a deep tour of it. What you need is to recognize the three frameworks a .NET shop reaches for, because sooner or later one of them will be calling your API:

- **.NET MAUI** is the evolution of Xamarin.Forms: iOS, Android, Windows, and macOS apps from a single C#/XAML codebase, rendered to real native controls. The typical encounter is a line-of-business mobile app maintained by the same .NET team that owns the backend. (Its **Blazor Hybrid** variant hosts your existing Blazor web components inside the native shell via `BlazorWebView`, trading platform look-and-feel for web-UI reuse.)
- **Uno Platform** targets mobile, desktop, *and* the browser (via WASM) from WinUI/XAML — broader reach than MAUI.
- **Avalonia** is a mature XAML-based cross-platform desktop framework, popular where Linux desktop support matters — a platform MAUI does not target.

The senior-relevant point is that all three are *API consumers*. What they depend on is your side of the boundary: a clean, documented OpenAPI contract; token-based auth flows that work without browser cookies; resilience to flaky mobile networks; and above all versioning discipline — an installed app cannot be force-refreshed like a SPA, so old client versions will hit your API for months. Design that boundary well and the client framework is their choice, not your problem.

## Accessibility: The Part That Is Now Law — in depth

[Chapter 6](#accessibility-the-part-that-is-now-law) covered the law, WCAG's principles, semantic HTML before ARIA, keyboard operability and accessible forms. This section covers what changes in a SPA, how to test, and the Blazor-specific traps.

WCAG 2.2 added a handful of criteria worth knowing because they catch modern UI patterns: focus must not be entirely hidden behind sticky headers, drag operations need a single-pointer alternative, click targets need a minimum size, and users must not be forced to re-enter information they already gave you in the same process.

### Route Changes in a SPA

In a server-rendered app, navigating to a new page resets focus to the top and the screen reader announces the new document's title. In a SPA, client-side navigation swaps part of the DOM and focus stays wherever it was, often on a link that no longer exists, while nothing is announced. Keyboard and screen-reader users are left on a page that changed without telling them. The fix, in any framework: after each navigation, move focus to the new page's main heading (give it `tabindex="-1"` so it can take focus without entering the Tab order), update `document.title`, and optionally announce the change through an `aria-live` region. Some frameworks do part of this for you (Blazor's router template includes a `FocusOnNavigate` component that focuses the new page's `<h1>`); check what yours does with a keyboard rather than assuming.

### Testing It

Automated checking is genuinely useful and genuinely limited, and knowing the ratio matters. Rules-based tools like **axe-core** reliably catch missing alt text, insufficient contrast, unlabelled inputs, duplicate IDs, and invalid ARIA — which is a real slice of the problem, and exactly the slice that regresses silently. Published analyses consistently put automated coverage at **roughly 30–40% of WCAG issues**. The rest — is the alt text *meaningful*, is the focus order *logical*, does the error message actually help, is this custom widget usable with a screen reader — requires a human.

So run both:

- **In CI**, axe-core against your key pages, failing the build on new violations. The wiring is in [Chapter 25: Accessibility checks in the same run](#accessibility-checks-in-the-same-run); component tests that find elements by role (the testing section above) add coverage for free.
- **By hand, periodically**: the keyboard-only pass from Chapter 6, a zoom-to-200% pass, and a screen reader pass (NVDA on Windows is free; VoiceOver ships on macOS). Half an hour with a screen reader on your own product is the most effective accessibility training available, and it is uncomfortable in a way that changes how you write markup afterwards.

### Blazor-Specific Pitfalls

Blazor generates HTML, so everything above applies unchanged. But its component model introduces two problems that catch teams out.

**Route changes don't announce themselves**, as in any SPA. The template's `<FocusOnNavigate RouteData="routeData" Selector="h1" />` in the router handles the common case; where it was removed, or a component needs to move focus itself, do it after render:

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

**Render modes change when your JS runs.** With `InteractiveServer` or `InteractiveWebAssembly`, the page is served as static HTML first and becomes interactive later — the hydration gap from the rendering section. Any accessibility behaviour you implemented in `OnAfterRenderAsync` or via JS interop does not exist during that window — and on a slow connection that window is seconds long. Prefer solutions that work in the initial markup (a real `<button>`, a real `<label>`) over ones that depend on interactivity having arrived.

Also: `NavLink` renders an `<a>`, which is correct — but a `NavLink` styled as a button, or an `<a>` with no `href` used as a click target, reintroduces the `div`-as-button problem in Razor syntax. And component libraries vary enormously in accessibility quality; check the one you adopt against a keyboard pass before it is load-bearing across forty screens.

## Sources & Further Reading

- **React documentation** (react.dev): `hydrateRoot` and hydration mismatches, `lazy` and `Suspense`, `startTransition`, Server Components.
- **TanStack Query documentation** (tanstack.com/query): "Important Defaults", query keys, invalidation.
- **web.dev**: rendering on the web, optimizing LCP, INP and CLS, image performance; the **`web-vitals`** library (GitHub `GoogleChrome/web-vitals`).
- **MDN Web Docs**: Content Security Policy (strict CSP, nonces, hashes, `'strict-dynamic'`, report-only), Trusted Types, `<img>` loading and `fetchpriority`.
- **Testing Library** (testing-library.com) and **Playwright** (playwright.dev) documentation.
- **Vite documentation** (vite.dev): building for production, code splitting.
- **IETF, OAuth 2.0 for Browser-Based Applications** (BFF and PKCE recommendations) and **Duende BFF** documentation.
- **ASP.NET Core documentation** (source: GitHub `dotnet/AspNetCore.Docs`): Blazor hosting models, render modes, components, JS interop and accessibility; CORS; SignalR's JavaScript client; secure SPAs.
- **.NET MAUI documentation** (single project, Blazor Hybrid); **Uno Platform** (platform.uno) and **Avalonia UI** (avaloniaui.net) project documentation.
- **OpenAPI Specification** and **NSwag** project documentation (client generation).

## Interview Questions

**SSR or CSR for this page, and what does hydration cost?**
Decide by content and audience: public and SEO-relevant pages want SSR or SSG for a fast LCP; a logged-in app can be CSR. Hydration means the client downloads and re-runs the components to attach handlers, so SSR improves first paint but not the JavaScript cost or INP, and a server/client mismatch makes React re-render that part on the client.

**Red flag:** "SSR makes the app faster." It makes content *visible* sooner; it does not make it interactive sooner, and it adds server cost.

**Why does the SPA call our API so often, and what would you change?**
A data-fetching cache like TanStack Query treats data as stale immediately by default and refetches on mount, window focus and reconnect. Set `staleTime` from how often the data really changes, invalidate on mutations instead of polling, and give the API `ETag`s so the remaining refetches are cheap.

**Server state versus client state?**
Server state is a cached copy of data the server owns: it needs fetching, deduplication, invalidation and refresh, so it belongs in a data-fetching cache. Client state belongs to the UI (open modals, form drafts) and lives as locally as possible; the URL holds anything that should survive a reload.

**How do you make a slow SPA fast?**
Measure first: field data for the 75th percentile, the Performance panel for causes. Then: split code by route, cut and tree-shake the bundle, give images dimensions and the right size, cache hashed assets forever and `index.html` never, and fix INP by doing less work per interaction (state placement, virtualisation, yielding).

**What does a strict CSP look like, and how do you deploy it on a SPA?**
`script-src 'nonce-…' 'strict-dynamic'; object-src 'none'; base-uri 'none'` with a fresh nonce per response, so injected inline script doesn't run. A SPA either serves `index.html` through the server to stamp the nonce in, or uses hashes from static hosting. Roll out in report-only mode first.

**Where should a browser SPA's tokens live?**
On the server, behind a BFF that does the OIDC code flow with PKCE as a confidential client and gives the browser only an `HttpOnly`, `Secure`, `SameSite` cookie; the BFF then needs CSRF protection.

**Red flag:** "The token is in `localStorage`, but we have a CSP, so XSS can't happen." CSP reduces XSS; it doesn't make storing a bearer token where any script can read it a good idea.

**How do you test a frontend without a flaky suite?**
Logic in unit tests; components with Testing Library, querying by role and label, network stubbed at the `fetch` layer; a handful of Playwright journeys end to end; and API contract drift caught at compile time by a generated client.
