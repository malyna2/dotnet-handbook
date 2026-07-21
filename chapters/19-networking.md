# Chapter 19: Networking & Web Fundamentals

_⏱️ Estimated read time: ~26 min ·     4479 words (study pace)_

Most application bugs that keep senior engineers up at night are not really *code* bugs. They are *network* bugs wearing a code costume. A method that works flawlessly on your laptop times out in production. A service that handled a thousand requests per second suddenly throws `SocketException` under load. A cross-origin `fetch` gets blocked by the browser for reasons nobody on the team can quite articulate.

The difference between a mid-level developer and a senior one is often just this: the senior developer has a *mental model* of what happens between the moment their code calls `await httpClient.GetAsync(url)` and the moment bytes come back. This chapter builds that model. We will travel from the abstract layered models down to the wire, back up through DNS and HTTP, and finally into the operational machinery — load balancers, proxies, CDNs — that sits between your code and your users.

Throughout, keep one idea in mind: **the network is a hostile, unreliable, shared medium that occasionally pretends to be a reliable function call.** Every abstraction in this chapter exists to manage that lie.

## The Layered Model: OSI and TCP/IP

Networking is taught as a stack of layers because that is genuinely how it is built. Each layer solves one problem and hands a clean abstraction to the layer above, like nested Russian dolls.

The classic **OSI model** has seven layers, but in practice you only need to internalize a simplified **TCP/IP model** with four:

| Layer | Job | Examples |
|-------|-----|----------|
| Application | What the bytes *mean* | HTTP, DNS, gRPC, WebSocket |
| Transport | Getting bytes to the right *program* on a host, reliably or not | TCP, UDP, QUIC |
| Internet | Getting packets to the right *host* across networks | IP, ICMP |
| Link | Getting bits across one physical hop | Ethernet, Wi-Fi |

The analogy that sticks: sending a letter. The **Link** layer is the mail truck driving between two post offices. The **Internet** layer (IP) is the addressing system that routes the envelope city-to-city — best effort, no guarantee it arrives. The **Transport** layer is the internal office mail room that makes sure the letter reaches *Bob in accounting* (a port number) and, in TCP's case, that missing pages get re-sent. The **Application** layer is the actual language written inside the letter that Bob understands.

> **Why this matters for you:** When you debug, ask *which layer is failing?* "Connection refused" is transport/host (nothing is listening on that port). "No such host is known" is DNS/application. A 500 is application. Confusing these wastes hours.

## TCP vs UDP

Both TCP and UDP ride on top of IP, and both use **port numbers** to route to a specific process. That is where the similarity ends.

**TCP (Transmission Control Protocol)** is a *reliable, ordered, connection-oriented stream.* Before any data flows, TCP performs a **three-way handshake** (`SYN` → `SYN-ACK` → `ACK`) to establish a connection. After that, it guarantees:

- **Reliability** — lost packets are detected (via acknowledgements) and retransmitted.
- **Ordering** — bytes arrive in the order sent, even if underlying packets take different routes.
- **Flow & congestion control** — TCP throttles itself so it neither overwhelms the receiver nor the network.

The cost is *latency* and *state*. That handshake is a full round-trip before your first byte. Head-of-line blocking (more on this later) means one lost packet stalls everything behind it.

**UDP (User Datagram Protocol)** is *fire-and-forget datagrams.* No handshake, no ordering, no retransmission, no congestion control. You send a packet; maybe it arrives, maybe it doesn't, maybe it arrives twice, maybe out of order. What you gain is minimal overhead and no head-of-line blocking.

Think of TCP as a phone call — you establish a connection, take turns, and confirm you heard each other. UDP is shouting across a crowded room: fast, but you have no idea if anyone heard.

**When to use which:**

- **TCP:** HTTP, database connections, file transfer, anything where correctness beats latency. This is 95% of what you write.
- **UDP:** DNS queries, real-time video/voice (a dropped frame is better than a stalled one), gaming, and — importantly — **QUIC**, the foundation of HTTP/3, which rebuilds reliability *on top of* UDP to escape TCP's limitations.

## DNS Resolution

Humans use names (`api.example.com`); IP routing uses numbers (`93.184.216.34`). **DNS (Domain Name System)** is the distributed phone book that translates one to the other. It is a hierarchy, resolved from right to left.

When your app resolves `api.example.com`:

1. The OS checks its local cache and `hosts` file.
2. If not cached, it asks a **recursive resolver** (often your ISP's or `8.8.8.8`).
3. The resolver asks a **root** server: "who handles `.com`?"
4. The **TLD** server for `.com` answers: "ask the authoritative name server for `example.com`."
5. The **authoritative** server returns the actual IP (an `A` record for IPv4, `AAAA` for IPv6).
6. The answer is cached at each level according to its **TTL** (time to live).

That is potentially several round-trips — which is why caching is everywhere and why the first request to a new host is slower.

> **Senior-level gotcha in .NET:** `HttpClient` and its underlying connection pool can cache DNS results for the *lifetime of a connection*. If a DNS record changes (a failover, a blue-green deploy), long-lived pooled connections may keep hitting the old IP. The fix is `PooledConnectionLifetime`, which we cover under connection pooling below. This exact issue has caused countless "why is traffic still going to the dead server?" incidents.

## How HTTP Works

**HTTP (HyperText Transfer Protocol)** is a *request-response, text-based (semantically), stateless* application protocol. A client sends a request; a server sends exactly one response. That is the whole contract.

A raw HTTP/1.1 request looks like this:

```
GET /users/42 HTTP/1.1
Host: api.example.com
Accept: application/json
Authorization: Bearer eyJhbGci...

```

And a response:

```
HTTP/1.1 200 OK
Content-Type: application/json
Content-Length: 27
Cache-Control: max-age=60

{"id":42,"name":"Ada"}
```

Every request has a **method** (verb), a **path**, **headers** (metadata as key-value pairs), and an optional **body**. Responses have a **status code**, headers, and a body.

**Statelessness** is the crucial architectural property. HTTP itself remembers nothing between requests. Each request must carry everything the server needs to understand it. Cookies, tokens, and sessions all exist to *simulate* state on top of a stateless protocol. This statelessness is exactly what makes horizontal scaling possible — any server can handle any request because none of them hold conversation state (assuming you keep session data in a shared store, not in-process memory).

### HTTP Methods and Idempotency

The methods carry semantic meaning that the whole ecosystem (caches, proxies, retries) relies on:

- **GET** — read, no side effects, *safe* and *cacheable*.
- **POST** — create or "do something"; **not** idempotent.
- **PUT** — replace a resource wholesale; idempotent.
- **PATCH** — partial update.
- **DELETE** — remove; idempotent.

> **Best practice:** *Idempotency* means calling N times has the same effect as calling once. It is not academic — it decides whether it is safe to auto-retry. A proxy or your Polly retry policy can safely retry a GET or PUT after a timeout; retrying a POST might charge a credit card twice. Design your APIs so that anything retriable is idempotent, and use idempotency keys for POSTs that must not double-execute.

## HTTP/1.1 vs HTTP/2 vs HTTP/3: A History of Fixing Head-of-Line Blocking

Each HTTP version exists to fix the performance sins of the previous one. Understanding the progression tells you *why* modern web performance looks the way it does.

**HTTP/1.1** sends requests as plaintext over a TCP connection, one request-response at a time per connection. Its big feature was **persistent connections** (keep-alive) so you did not pay the TCP handshake for every request. But it has a fatal flaw: **application-layer head-of-line blocking.** A connection can only work on one request at a time. Browsers hacked around this by opening ~6 parallel connections per host — wasteful and still limited.

**HTTP/2** (2015) fixed application-layer blocking with **multiplexing.** It introduced a **binary framing layer**: a single TCP connection carries many independent **streams** simultaneously, each request/response chopped into interleaved frames. It also added **header compression (HPACK)** — because HTTP headers are hugely repetitive — and **server push** (largely deprecated now). One connection, many concurrent requests, no more opening six sockets.

But HTTP/2 still runs over TCP, and TCP has its *own* head-of-line blocking one layer down. If a single TCP packet is lost, TCP holds back *all* streams until it is retransmitted — even streams whose data already arrived. On a lossy network (mobile, Wi-Fi), HTTP/2 can actually feel worse than several HTTP/1.1 connections.

**HTTP/3** (2022) attacks the problem at the root by abandoning TCP entirely. It runs over **QUIC**, a new transport built on **UDP**. QUIC reimplements reliability, ordering, and congestion control *per stream*, so a lost packet only blocks *its own* stream — true independence. QUIC also **merges the transport and TLS handshakes**, cutting connection setup to often a single round-trip (or zero on resumption), and it supports **connection migration**: a phone switching from Wi-Fi to cellular keeps the same QUIC connection via a connection ID instead of the IP:port tuple.

| Version | Transport | Concurrency | Key fix |
|---------|-----------|-------------|---------|
| HTTP/1.1 | TCP | 1 req/connection | Persistent connections |
| HTTP/2 | TCP | Multiplexed streams | App-layer HOL blocking, header compression |
| HTTP/3 | QUIC/UDP | Independent streams | Transport-layer HOL blocking, faster handshake, migration |

In .NET, HTTP/2 is well supported and HTTP/3 is available; you can opt in per request:

```csharp
using var client = new HttpClient();
var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com")
{
    Version = HttpVersion.Version30,
    VersionPolicy = HttpVersionPolicy.RequestVersionOrLower
};
var response = await client.SendAsync(request);
```

`RequestVersionOrLower` means "try HTTP/3, but gracefully fall back" — important, because HTTP/3 depends on the server advertising support (via the `Alt-Svc` header) and on UDP not being blocked by intermediary firewalls.

## HTTPS and the TLS Handshake, Step by Step

**HTTPS is just HTTP inside a TLS tunnel.** **TLS (Transport Layer Security)**, the successor to SSL, provides three guarantees: **confidentiality** (encryption), **integrity** (tamper detection), and **authentication** (you are really talking to `example.com`, verified by a certificate).

Here is a **TLS 1.3** handshake, the modern default, which is faster than its predecessors (one round-trip):

1. **ClientHello** — The client sends supported TLS versions, a list of cipher suites, a random nonce, and — a TLS 1.3 optimization — its **key share** (an ephemeral public key guess) up front.
2. **ServerHello** — The server picks a cipher suite, sends its own key share and random nonce. At this point both sides can derive the shared symmetric key via **Diffie-Hellman** — crucially, without ever sending the secret over the wire.
3. **Certificate** — The server sends its **X.509 certificate**, which binds its domain name to a public key and is signed by a **Certificate Authority (CA)** the client trusts. The client verifies the signature chain up to a trusted root in its store, checks the domain matches, and checks expiry/revocation.
4. **Finished** — Both sides confirm they derived the same keys. From here, all application data is encrypted with fast **symmetric** encryption (e.g., AES-GCM).

The elegant trick: **asymmetric** cryptography (slow) is used only to authenticate and to agree on a shared secret; then **symmetric** cryptography (fast) does the bulk encryption. You get the security of public-key crypto with the speed of symmetric ciphers.

> **Best practice:** Never disable certificate validation to "make it work" (`ServerCertificateCustomValidationCallback` returning `true`). That silently defeats the entire authentication guarantee and invites man-in-the-middle attacks. If you have a self-signed cert in dev, trust it properly in the machine store instead.

**TLS 1.3** also supports **0-RTT resumption**, where a returning client can send data in its very first packet — great for latency, but 0-RTT data is vulnerable to replay, so never use it for non-idempotent requests.

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

## Status Codes and Headers That Matter

Status codes group into five families. Senior developers use them *precisely* because tooling depends on them:

- **1xx** Informational (rare; `101 Switching Protocols` for WebSocket upgrade).
- **2xx** Success — `200 OK`, `201 Created` (with a `Location` header), `204 No Content`.
- **3xx** Redirection — `301` permanent, `302`/`307` temporary, `304 Not Modified` (caching).
- **4xx** Client error — `400` bad request, `401` unauthenticated, `403` authenticated-but-forbidden, `404` not found, `409` conflict, `422` unprocessable, `429` too many requests.
- **5xx** Server error — `500` unhandled, `502` bad gateway (proxy got garbage upstream), `503` unavailable (overloaded/deploying), `504` gateway timeout.

> **Best practice:** The `401` vs `403` distinction trips people up. `401` means "I don't know who you are — authenticate." `403` means "I know who you are, and you may not do this." Returning the wrong one confuses clients and leaks information.

Headers worth knowing cold: `Content-Type` and `Accept` (content negotiation), `Authorization`, `Cache-Control` and `ETag` (caching, below), `Content-Encoding` (gzip/brotli compression), `Retry-After` (paired with `429`/`503`), and `X-Forwarded-For`/`X-Forwarded-Proto` (the client's real IP/scheme, injected by proxies — trust these only from proxies you control).

## Keep-Alive, Connection Pooling, and Socket Exhaustion

Opening a TCP connection (and worse, a TLS handshake) is expensive — multiple round-trips before a single byte of your data moves. **Keep-alive** (persistent connections, the default in HTTP/1.1) reuses one connection for many requests. **Connection pooling** takes this further: a pool of warm, reused connections shared across requests.

This is where .NET developers hit one of the most infamous bugs in the ecosystem: **socket exhaustion.**

The naive pattern looks innocent:

```csharp
// DO NOT DO THIS in a loop / per request
using (var client = new HttpClient())
{
    return await client.GetStringAsync(url);
}
```

`HttpClient` is `IDisposable`, so the instinct is to `using` it. But disposing it does **not** immediately release the underlying TCP socket — the socket lingers in the OS `TIME_WAIT` state for up to ~4 minutes. Under load, you create thousands of sockets faster than the OS reclaims them, exhaust the ephemeral port range, and start throwing `SocketException: Only one usage of each socket address is normally permitted`. Ironically, the "correct-looking" disposal code causes the leak.

**The fix is `IHttpClientFactory`.** It manages a pool of long-lived `HttpMessageHandler` instances (which own the connections) behind short-lived `HttpClient` façades. You get a fresh, cheap `HttpClient` per use, but connections are pooled and reused underneath:

```csharp
// Registration
builder.Services.AddHttpClient("github", client =>
{
    client.BaseAddress = new Uri("https://api.github.com/");
    client.DefaultRequestHeaders.Add("User-Agent", "MyApp");
});

// Usage
public class GitHubService(IHttpClientFactory factory)
{
    public async Task<string> GetUserAsync(string login)
    {
        var client = factory.CreateClient("github");
        return await client.GetStringAsync($"users/{login}");
    }
}
```

This also solves the **DNS staleness** problem from earlier. The factory recycles handlers on a configurable schedule (default 2 minutes), forcing periodic DNS re-resolution. If you instead keep a single static `HttpClient` for the whole app lifetime (also valid, and avoids exhaustion), set `PooledConnectionLifetime` on a `SocketsHttpHandler` so pooled connections are retired and DNS is refreshed:

```csharp
var handler = new SocketsHttpHandler
{
    PooledConnectionLifetime = TimeSpan.FromMinutes(2),
    MaxConnectionsPerServer = 20
};
var client = new HttpClient(handler);
```

> **Best practice:** Do **not** create a new `HttpClient` per request, and do **not** wrap it in `using`. Either use `IHttpClientFactory` (preferred, integrates with typed clients and Polly resilience), or use one long-lived instance with `PooledConnectionLifetime` set. The factory also cleanly layers in retries, circuit breakers, and timeouts via `Microsoft.Extensions.Http.Resilience`.

## Load Balancers, Reverse Proxies, API Gateways, and CDNs

Between your users and your servers sits a stack of infrastructure whose job is to distribute, protect, and accelerate traffic. Senior engineers need to know what each box does.

### Load Balancers: L4 vs L7

A **load balancer** spreads incoming requests across a pool of backend servers, providing scale and fault tolerance. The key distinction is which OSI layer it operates on:

- **Layer 4 (transport)** balancers route based on IP and TCP/UDP port. They are blazing fast because they just forward packets without inspecting content — they cannot see the HTTP path, headers, or cookies. Think of a receptionist who routes calls purely by which line they came in on.
- **Layer 7 (application)** balancers understand HTTP. They can route based on URL path (`/api` → service A, `/images` → service B), hostname, headers, or cookies (for sticky sessions), terminate TLS, and rewrite requests. More CPU cost, far more flexibility.

### Reverse Proxies and YARP

A **reverse proxy** sits in front of your servers and forwards client requests to them, often adding TLS termination, compression, caching, and header manipulation. (A *forward* proxy sits in front of *clients*; a *reverse* proxy fronts *servers*.) **nginx** is the classic choice.

In the .NET world, **YARP (Yet Another Reverse Proxy)** is Microsoft's reverse proxy toolkit — a library you build a customized proxy from, running as an ASP.NET Core app. It shines when you want proxy logic expressed in C# and integrated with your existing middleware. A minimal config-driven setup:

```csharp
// Program.cs
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

var app = builder.Build();
app.MapReverseProxy();
app.Run();
```

```json
// appsettings.json
{
  "ReverseProxy": {
    "Routes": {
      "api-route": {
        "ClusterId": "api-cluster",
        "Match": { "Path": "/api/{**catch-all}" }
      }
    },
    "Clusters": {
      "api-cluster": {
        "LoadBalancingPolicy": "RoundRobin",
        "Destinations": {
          "d1": { "Address": "https://backend1.internal:5001/" },
          "d2": { "Address": "https://backend2.internal:5002/" }
        }
      }
    }
  }
}
```

This routes anything under `/api/` across two backends with round-robin balancing and built-in health checks — an L7 load balancer and reverse proxy in a few lines.

### API Gateways

An **API gateway** is a specialized reverse proxy that centralizes cross-cutting API concerns: authentication, rate limiting, request aggregation, API key management, versioning, and protocol translation. In a microservices architecture it gives clients a single entry point rather than exposing dozens of services. YARP is frequently used as the foundation for building one.

### CDNs and Caching Headers

A **CDN (Content Delivery Network)** is a globally distributed network of caching servers (edge nodes) that store copies of your content close to users. A user in Tokyo hits a Tokyo edge node instead of your origin in Virginia, cutting latency dramatically and shielding your origin from load. CDNs cache static assets aggressively and increasingly cache dynamic/API responses too.

CDNs and browsers obey HTTP **caching headers**:

- **`Cache-Control`** is the master switch: `max-age=3600` (cache for an hour), `no-cache` (revalidate before using), `no-store` (never cache — for sensitive data), `public`/`private` (may a shared CDN cache it, or only the user's browser?), `immutable` (never revalidate, for fingerprinted assets).
- **`ETag`** is a content fingerprint (a hash or version). The browser stores it and, on the next request, sends `If-None-Match: "<etag>"`. If the content is unchanged, the server replies `304 Not Modified` with an empty body — the client reuses its cached copy and you save the bandwidth of resending it. `Last-Modified`/`If-Modified-Since` is the timestamp-based equivalent.

```csharp
app.MapGet("/report/{id}", (int id, HttpContext ctx) =>
{
    var report = GetReport(id);
    var etag = $"\"{report.Version}\"";

    if (ctx.Request.Headers.IfNoneMatch == etag)
        return Results.StatusCode(StatusCodes.Status304NotModified);

    ctx.Response.Headers.ETag = etag;
    ctx.Response.Headers.CacheControl = "public, max-age=60";
    return Results.Ok(report);
});
```

> **Best practice:** Fingerprint static assets (`app.a1b2c3.js`) and serve them with `Cache-Control: immutable, max-age=31536000`. Because the filename changes when content changes, you can cache forever with zero staleness risk. Reserve short/`no-cache` TTLs for HTML and API responses that change.

## Real-Time: WebSockets vs SSE vs Long-Polling

Plain HTTP is client-initiated: the server cannot push. For chat, live dashboards, and notifications, you need server-to-client push. Three techniques, in ascending order of power:

- **Long-polling** — the client sends a request; the server *holds it open* until it has data (or a timeout), then responds; the client immediately re-requests. It simulates push over ordinary HTTP and works everywhere, but is inefficient (constant request churn, header overhead).
- **Server-Sent Events (SSE)** — a single long-lived HTTP response that the server streams events down over time (`text/event-stream`). It is **one-directional** (server → client only), text-only, but simple, auto-reconnecting, and rides normal HTTP infrastructure.
- **WebSockets** — a genuine **full-duplex, bidirectional** connection. The client sends an HTTP request with `Upgrade: websocket`; the server responds `101 Switching Protocols`; from then on both sides send messages freely over a persistent TCP connection. This is the right tool when the client also sends frequently (multiplayer, collaborative editing, chat).

In .NET, you rarely hand-code these. **SignalR** is the high-level real-time library that abstracts all three: it prefers WebSockets and *automatically falls back* to SSE or long-polling if the connection can't upgrade (a corporate proxy blocks WebSockets, say). You write hub methods and call clients as if they were local:

```csharp
public class ChatHub : Hub
{
    public async Task SendMessage(string user, string message) =>
        await Clients.All.SendAsync("ReceiveMessage", user, message);
}
// app.MapHub<ChatHub>("/chat");
```

> **Best practice:** Reach for SSE when you only need server→client streaming (notifications, progress, live prices) — it is lighter and simpler. Choose WebSockets/SignalR when the client talks back frequently. Skip raw long-polling unless you must support ancient infrastructure.

## Rate Limiting and Timeouts at the Edge

Two defensive controls belong at the network edge, protecting your services from abuse and from themselves.

**Rate limiting** caps how many requests a client may make in a window, returning `429 Too Many Requests` (ideally with a `Retry-After` header) when exceeded. It protects against abuse, runaway clients, and cascading overload. Common algorithms: **fixed window**, **sliding window**, **token bucket** (allows bursts up to a bucket size, refilling at a steady rate), and **concurrency** limits. ASP.NET Core has built-in middleware:

```csharp
builder.Services.AddRateLimiter(options =>
{
    options.AddTokenBucketLimiter("api", o =>
    {
        o.TokenLimit = 100;
        o.TokensPerPeriod = 20;
        o.ReplenishmentPeriod = TimeSpan.FromSeconds(1);
    });
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});
app.UseRateLimiter();
```

**Timeouts** ensure a slow or dead dependency does not tie up your resources forever. Every network call needs a bound. Without timeouts, one hung upstream can exhaust your thread/connection pool and take the whole service down — a classic cascading failure. Set them explicitly:

```csharp
using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
var response = await client.GetAsync(url, cts.Token);
```

> **Best practice:** Combine timeouts, retries (with **exponential backoff and jitter** so retries don't stampede in lockstep), and **circuit breakers** (stop hammering a failing dependency) — the resilience trio. In .NET, `Microsoft.Extensions.Http.Resilience` (built on Polly) wires all three into `IHttpClientFactory` declaratively.

## The Fallacies of Distributed Computing

We close with the mental model that should underpin every networked design decision. In the 1990s, engineers at Sun Microsystems catalogued the **Fallacies of Distributed Computing** — false assumptions that developers repeatedly make. Three deserve special emphasis:

**1. "The network is reliable."** It is not. Packets drop, connections reset, cables get cut, servers reboot mid-request. A remote call can fail *after* the server processed it but *before* you got the response — you genuinely cannot tell whether it succeeded. This is why idempotency, retries, and timeouts are not optional extras; they are load-bearing. Design for failure as the normal case.

**2. "Latency is zero."** It is not. Light itself takes ~70ms to cross the Atlantic and back; add TCP/TLS handshakes, DNS, and queuing, and a "quick call" is tens to hundreds of milliseconds. Making 50 sequential network calls to render one page — the **N+1 network problem** — is why some apps feel slow no matter how fast the code is. Batch, parallelize, and cache. A remote call is *not* a method call, no matter how much the syntax pretends otherwise.

**3. "Bandwidth is infinite" and "the network is free."** They are not. Data has cost — in transfer time, in cloud egress bills (often the biggest surprise line item), and in serialization overhead. Sending a 5 MB JSON blob to render a 20-row table is a real cost, not an abstraction.

The other fallacies — the network is secure, topology doesn't change, there is one administrator, transport cost is zero — round out the list. Internalize all of them, and you will design systems that degrade gracefully instead of collapsing the first time reality asserts itself.

> **The senior mindset in one sentence:** Treat every network call as an *unreliable, slow, expensive, insecure* operation that will eventually fail — then be pleasantly surprised when it works.

## Sources & Further Reading

- **Microsoft Learn** — "Use IHttpClientFactory to implement resilient HTTP requests," "HttpClient guidelines for .NET," and "Guidelines for using HttpClient" (learn.microsoft.com).
- **Microsoft Learn** — YARP (Yet Another Reverse Proxy) documentation (learn.microsoft.com).
- **Microsoft Learn** — ASP.NET Core CORS, Rate limiting middleware, SignalR, and Response caching documentation (learn.microsoft.com).
- **MDN Web Docs** — HTTP overview, HTTP caching, `Cache-Control`, `Set-Cookie`, `SameSite`, CORS, Same-origin policy, and Server-Sent Events (developer.mozilla.org).
- **RFC 9110** — HTTP Semantics; **RFC 9112** — HTTP/1.1; **RFC 9113** — HTTP/2; **RFC 9114** — HTTP/3; **RFC 9000** — QUIC.
- **RFC 8446** — The Transport Layer Security (TLS) Protocol Version 1.3.
- **RFC 6455** — The WebSocket Protocol.
- **RFC 1034 / RFC 1035** — Domain Names (DNS) concepts and specification.
- **RFC 793 / RFC 9293** — Transmission Control Protocol (TCP); **RFC 768** — User Datagram Protocol (UDP).
- Peter Deutsch and James Gosling (Sun Microsystems) — *The Fallacies of Distributed Computing.*
