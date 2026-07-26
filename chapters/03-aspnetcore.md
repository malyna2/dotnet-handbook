# Chapter 3: ASP.NET Core & Web APIs

_⏱️ Estimated read time: ~40 min · 5482 words (study pace)_

ASP.NET Core is the beating heart of most .NET server-side work. If you've been building APIs for a couple of years, you already know how to make an endpoint return JSON. This chapter is about the *why* underneath: how a request actually travels through your application, where the extension points live, and how the senior-level decisions (versioning, resilience, auth, real-time) fit together. By the end you should be able to reason about the framework rather than just use it.

## The Middleware Pipeline & Request Lifecycle

Everything in ASP.NET Core is built on one deceptively simple idea: **a request flows through a chain of components, each of which can do work before and after the next one runs.** This chain is the *middleware pipeline*, and understanding it is the single most important mental model in the framework.

Think of the pipeline like airport security lanes arranged in a line. Each checkpoint can inspect you, stamp your passport, send you back early (short-circuit), or wave you through to the next checkpoint. On the way *out*, you pass back through those same checkpoints in reverse order. That "in one order, out in reverse" behavior is often drawn as a set of Russian nesting dolls (matryoshka): the outermost middleware wraps everything inside it.

A middleware component is fundamentally just a function that takes the current `HttpContext` and a delegate to "the rest of the pipeline" (`RequestDelegate`, usually called `next`).

```csharp
public class RequestTimingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestTimingMiddleware> _logger;

    public RequestTimingMiddleware(RequestDelegate next, ILogger<RequestTimingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var start = Stopwatch.GetTimestamp();

        // Work BEFORE the rest of the pipeline runs.
        await _next(context); // Hand off to the next middleware.
        // Work AFTER control unwinds back to us.

        var elapsed = Stopwatch.GetElapsedTime(start);
        _logger.LogInformation("{Method} {Path} took {Ms} ms",
            context.Request.Method, context.Request.Path, elapsed.TotalMilliseconds);
    }
}
```

The `InvokeAsync` signature is a *convention*, not an interface (though `IMiddleware` exists for the factory-activated variant). The framework discovers it by name. The middleware itself is instantiated **once** as a singleton at startup; that's why you inject `RequestDelegate` and `ILogger` (singletons) in the constructor, but you must **not** inject scoped services there. To use a scoped service, inject it as a parameter of `InvokeAsync` instead, where the per-request scope is available.

You register it in the pipeline with `UseMiddleware`, or wrap it in an extension method:

```csharp
var app = builder.Build();

app.UseMiddleware<RequestTimingMiddleware>();
```

For quick, one-off logic you can use the inline lambda forms:

```csharp
// Passes control onward.
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Request-Id"] = Guid.NewGuid().ToString("N");
    await next(context);
});

// Terminal middleware — never calls next, ends the pipeline.
app.Run(async context =>
{
    await context.Response.WriteAsync("Nothing matched.");
});
```

There's also `Map` / `MapWhen` for branching the pipeline based on path or a predicate.

### Ordering is everything

The order in which you add middleware *is* the order requests flow through. This is the most common source of subtle bugs.

> **Best practice — canonical ordering.** Exception handling first (so it wraps everything), then HSTS/HTTPS redirection, static files, routing, CORS, authentication, authorization, and finally your endpoints. Authentication must come before authorization: you can't check *what someone is allowed to do* before you know *who they are*.

```csharp
app.UseExceptionHandler();      // Outermost: catches everything below.
app.UseHsts();
app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();               // Decides which endpoint matches.
app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();        // Who are you?
app.UseAuthorization();         // Are you allowed?
app.MapControllers();           // Terminal: executes the endpoint.
```

That registration order creates the matryoshka nesting from the start of the chapter:

```
       request                                    response
          |                                           ^
          v                                           |
+-- UseExceptionHandler ------------------------------|------+
|         |                                           |      |
|  +-- UseRouting / UseCors / UseRateLimiter ---------|---+  |
|  |      |                                           |   |  |
|  |  +-- UseAuthentication -> UseAuthorization ------|-+ |  |
|  |  |   |                                           | | |  |
|  |  |   +-----> MapControllers (endpoint) ----------+ | |  |
|  |  +-------------------------------------------------+ |  |
|  +------------------------------------------------------+  |
+------------------------------------------------------------+
```

If you put `UseAuthorization` before `UseRouting`, the authorization middleware has no endpoint metadata to inspect and your `[Authorize]` attributes silently do nothing. If you put `UseCors` after the endpoint that handles the request, preflight requests break. **When something "just doesn't apply," suspect ordering first.**

## Minimal APIs vs Controllers (MVC)

ASP.NET Core gives you two programming models that both compile down to the same endpoint routing infrastructure. Neither is "better" — they optimize for different things.

**Controllers (MVC)** organize endpoints into classes, lean on convention (attribute routing, filters, model binding by attribute), and shine when you have many endpoints that share cross-cutting behavior.

```csharp
[ApiController]
[Route("api/[controller]")]
public class ProductsController : ControllerBase
{
    private readonly IProductService _service;
    public ProductsController(IProductService service) => _service = service;

    [HttpGet("{id:int}")]
    public async Task<ActionResult<ProductDto>> GetById(int id)
    {
        var product = await _service.FindAsync(id);
        return product is null ? NotFound() : Ok(product);
    }

    [HttpPost]
    public async Task<ActionResult<ProductDto>> Create(CreateProductRequest request)
    {
        var created = await _service.CreateAsync(request);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }
}
```

The `[ApiController]` attribute is doing a lot of quiet work here: automatic model-state validation (returning a 400 with problem details when binding fails), inference of binding sources (body vs route vs query), and `ProblemDetails` responses. `ControllerBase` gives you the helper methods `Ok`, `NotFound`, `CreatedAtAction`, etc.

**Minimal APIs** express endpoints as lambdas directly on the app or a route group. They have less ceremony, a smaller call stack (faster startup, marginally faster per request), and read top-to-bottom.

```csharp
var products = app.MapGroup("/api/products").WithTags("Products");

products.MapGet("/{id:int}", async (int id, IProductService service) =>
    await service.FindAsync(id) is { } p ? Results.Ok(p) : Results.NotFound());

products.MapPost("/", async (CreateProductRequest request, IProductService service) =>
{
    var created = await service.CreateAsync(request);
    return Results.CreatedAtRoute("GetProduct", new { id = created.Id }, created);
})
.WithName("CreateProduct");
```

Notice dependencies (`IProductService`) are just parameters — the framework resolves them from DI by type. `Results` is the minimal-API equivalent of the `ControllerBase` helpers, and `TypedResults` is its strongly-typed cousin (better for testing and OpenAPI inference).

> **When to use which.** Reach for **Minimal APIs** for microservices, small focused services, and BFF/gateway layers where terseness and startup speed matter. Reach for **Controllers** for large APIs with lots of shared conventions, when your team relies heavily on filters, or when you value the discoverability of a class-per-resource layout. They can coexist in the same app.

## Routing & Endpoint Routing

Routing is a **two-phase** process in modern ASP.NET Core, and this two-phase design is why middleware between them can see *which* endpoint will run. `UseRouting` matches the incoming URL to an endpoint and stashes the result on `HttpContext`. Later middleware (auth, CORS) can inspect that endpoint's metadata. Finally the endpoint middleware (added implicitly by `MapControllers`/`MapGet`) executes it.

Route templates support **constraints** that filter matches by type or pattern:

```csharp
app.MapGet("/orders/{id:guid}", ...);          // Only matches valid GUIDs.
app.MapGet("/reports/{year:int:min(2000)}", ...); // int >= 2000.
app.MapGet("/files/{*path}", ...);             // Catch-all segment.
app.MapGet("/users/{name:alpha:length(3,20)}", ...);
```

Constraints are for *disambiguation*, not validation. `{id:int}` failing to match returns a 404 (the route simply didn't apply) — it does not return a helpful 400 telling the caller their ID was malformed. Use constraints to route correctly; use model validation to give good error messages.

## Model Binding & Validation

Model binding is the process that turns raw HTTP text — route values, query string, headers, form fields, JSON body — into your C# method parameters and objects. With `[ApiController]`, binding sources are *inferred*: complex types come from the body, simple types from route/query. You can be explicit with `[FromBody]`, `[FromRoute]`, `[FromQuery]`, `[FromHeader]`, `[FromServices]`.

### DataAnnotations

The built-in validation approach decorates properties with attributes:

```csharp
public class CreateProductRequest
{
    [Required, StringLength(120, MinimumLength = 3)]
    public string Name { get; set; } = string.Empty;

    [Range(0.01, 100_000)]
    public decimal Price { get; set; }

    [EmailAddress]
    public string? ContactEmail { get; set; }
}
```

With `[ApiController]`, a failing model automatically produces a `400 Bad Request` with a validation `ProblemDetails` payload — you never write `if (!ModelState.IsValid)`. In Minimal APIs there's no automatic model-state check by default (you opt in via the validation support added in .NET 10, or validate manually / with a filter).

### FluentValidation

DataAnnotations get awkward once rules become conditional or cross-field ("discount is only valid when the item is on sale"). **FluentValidation** moves rules into a dedicated class with a fluent, testable API:

```csharp
public class CreateProductValidator : AbstractValidator<CreateProductRequest>
{
    public CreateProductValidator()
    {
        RuleFor(x => x.Name).NotEmpty().Length(3, 120);
        RuleFor(x => x.Price).GreaterThan(0);
        RuleFor(x => x.ContactEmail)
            .EmailAddress()
            .When(x => x.ContactEmail is not null);
    }
}
```

> **Best practice.** Keep validators free of infrastructure. If a rule needs a database check (e.g. "SKU must be unique"), that's arguably a domain/business concern better handled in your service layer, not in a validator that runs on every bind. Validators are for *shape and format*; business invariants belong deeper.

## CancellationToken Propagation

Every request carries an implicit expiry: the moment the client disconnects, times out, or navigates away, any work you're still doing on its behalf is wasted. The framework tells you when that happens — `HttpContext.RequestAborted` is a `CancellationToken` that trips when the connection drops — and both Minimal APIs and MVC will bind it for you: declare a `CancellationToken` parameter on your endpoint or action and the framework wires it to `RequestAborted` automatically.

The token only helps if you *pass it through*. EF Core queries, `HttpClient` calls, stream reads — essentially every awaited I/O API — accept one:

```csharp
app.MapGet("/reports/{id:int}", async (int id, AppDbContext db,
    ReportRenderer renderer, CancellationToken ct) =>
{
    var data = await db.ReportRows
        .Where(r => r.ReportId == id)
        .ToListAsync(ct);                    // Stops the query if the client is gone.

    return Results.Ok(await renderer.RenderAsync(data, ct));
});
```

When the client disconnects mid-query, EF Core cancels the database command; the connection returns to the pool and the request's threads free up. Without the token, the query runs to completion for a caller that will never read the response.

Why this matters operationally: picture your API slowing down under load. Clients hit their own timeouts, abandon their requests, and *retry*. If your server doesn't observe cancellation, every abandoned request keeps executing — the original query is still hammering the database while the retry starts a duplicate. Load effectively doubles at precisely the moment the system is already struggling, and a slowdown snowballs into an outage. Propagating the token is what lets abandoned work actually stop, turning a retry storm into a manageable blip instead of a self-inflicted amplification attack.

> **Gotcha:** Not everything should be cancellable. If you've charged a payment and are about to write the outbox record, cancelling *mid-write* because the client hung up is far worse than finishing wasted work — you'd take the money and lose the event. For operations that must run to completion once started, deliberately pass `CancellationToken.None` (or a token decoupled from the request) past that point of no return. The skill isn't "always pass the token"; it's knowing which operations are safe to abandon and which have already committed you.

## Filters

Filters run *inside* the MVC/endpoint execution, giving you hooks that are aware of model binding and action results — something raw middleware can't see. They form their own mini-pipeline with a defined order: **Authorization → Resource → Action → (endpoint) → Result**, plus **Exception** filters that catch throws.

```csharp
public class AuditActionFilter : IAsyncActionFilter
{
    private readonly ILogger<AuditActionFilter> _logger;
    public AuditActionFilter(ILogger<AuditActionFilter> logger) => _logger = logger;

    public async Task OnActionExecutionAsync(
        ActionExecutingContext context, ActionExecutionDelegate next)
    {
        _logger.LogInformation("Executing {Action}", context.ActionDescriptor.DisplayName);
        var executed = await next(); // Runs the action.
        if (executed.Exception is null)
            _logger.LogInformation("Completed {Action}", context.ActionDescriptor.DisplayName);
    }
}
```

- **Authorization filters** run first and decide whether the request may proceed (this is what `[Authorize]` plugs into).
- **Resource filters** wrap model binding — useful for caching or short-circuiting expensive work early.
- **Action filters** run around the action method, with access to bound arguments and the result.
- **Result filters** run around result execution (e.g. formatting the response).
- **Exception filters** catch unhandled exceptions from actions and let you convert them to a response.

The Minimal API analog is the **endpoint filter** (`IEndpointFilter` / `AddEndpointFilter`), a lighter chain that wraps a single endpoint or group.

> **Filter vs middleware — which do I use?** If the logic needs to know about the *action, its arguments, or its result*, use a filter. If it's truly cross-cutting and content-agnostic (timing, correlation IDs, compression), use middleware. Middleware is broader and cheaper; filters are more contextual.

## Authentication & Authorization

These two words get conflated constantly. **Authentication** answers "who are you?" and produces a `ClaimsPrincipal`. **Authorization** answers "are you allowed to do this?" using that principal. They are separate middleware, separate concerns, and separate mental steps.

A `ClaimsPrincipal` carries one or more `ClaimsIdentity` objects, each a bag of **claims** — simple key/value statements like `sub=42`, `role=admin`, `email=x@y.com`. Claims are the currency of authorization; you make decisions based on what claims a user carries, not by re-querying a database on every request.

### JWT Bearer

For APIs, the dominant scheme is **JWT bearer tokens**. The client sends `Authorization: Bearer <token>`; the token is a signed (and base64url-encoded) set of claims the server validates without a lookup.

```csharp
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"]!))
        };
    });
```

> **Pitfall.** A JWT is *signed*, not *encrypted*. Anyone can decode and read its claims. Never put secrets in a token, and always validate the signature (`ValidateIssuerSigningKey`) — otherwise an attacker can forge claims.

### Cookies and OAuth2/OIDC

For server-rendered apps, **cookie authentication** stores an encrypted session identifier in a cookie. For delegated identity — "log in with Google/Microsoft/your corporate IdP" — you use **OAuth2** (authorization framework) and its identity layer **OpenID Connect (OIDC)**. In the typical Authorization Code flow the user authenticates at the identity provider, which redirects back with a short-lived code your app exchanges for tokens. In practice you configure `.AddOpenIdConnect(...)` and let the middleware handle the redirect dance. The key insight for a senior: your API should *trust tokens from a known issuer*, not manage passwords itself.

### Policy-based and role-based authorization

**Role-based** is the classic coarse check: `[Authorize(Roles = "Admin")]`. It works but roles are blunt instruments. **Policy-based** authorization is the flexible, recommended approach — you name a policy and define what satisfies it:

```csharp
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdultsOnly", policy =>
        policy.RequireAssertion(ctx =>
            ctx.User.HasClaim(c => c.Type == "age") &&
            int.Parse(ctx.User.FindFirstValue("age")!) >= 18));

    options.AddPolicy("CanDeleteProducts", policy =>
        policy.RequireClaim("permission", "products:delete"));
});

// Applied declaratively:
products.MapDelete("/{id:int}", ...).RequireAuthorization("CanDeleteProducts");
```

For complex rules, implement `IAuthorizationRequirement` plus an `AuthorizationHandler<T>` — this lets you inject services and evaluate against resources (e.g. "can edit *this specific* document because you own it"). That resource-based check is done imperatively via `IAuthorizationService.AuthorizeAsync(user, resource, policy)`.

## IHttpClientFactory & Resilience with Polly

Calling other services over HTTP is where many production incidents are born. The naïve `new HttpClient()` per call **exhausts sockets** (each instance holds a connection pool and sockets linger in `TIME_WAIT`); a single static instance **doesn't respect DNS changes**. `IHttpClientFactory` solves both by pooling and rotating the underlying handlers.

To see *why* that works, you need one fact: `HttpClient` itself is a cheap, disposable wrapper. The real resources — the connection pool, the open sockets — live in the `HttpMessageHandler` underneath it. The factory hands you a fresh `HttpClient` every time, but behind it shares a pool of handlers, so sockets are reused instead of exhausted; and it retires each handler after two minutes (tunable via `SetHandlerLifetime`), so new connections re-resolve DNS and a failed-over dependency doesn't leave you talking to a dead IP.

```
CatalogClient ──> HttpClient          (new each time — cheap wrapper)
                      │
                      ▼
              HttpMessageHandler      (pooled & shared — owns the sockets;
                                       recycled every ~2 min → fresh DNS)
```

**Named clients** let you configure a client by string key. **Typed clients** wrap an `HttpClient` in a strongly-typed service — cleaner and my default recommendation:

```csharp
public class CatalogClient
{
    private readonly HttpClient _http;
    public CatalogClient(HttpClient http) => _http = http;

    public async Task<Product?> GetProductAsync(int id, CancellationToken ct) =>
        await _http.GetFromJsonAsync<Product>($"products/{id}", ct);
}

builder.Services.AddHttpClient<CatalogClient>(c =>
{
    c.BaseAddress = new Uri("https://catalog.internal/");
    c.Timeout = TimeSpan.FromSeconds(10);
});
```

> **Pitfall — typed clients are transient.** Don't inject a typed client into a singleton. The singleton captures one `HttpClient` — and the handler behind it — forever, which quietly reintroduces the stale-DNS problem the factory exists to solve. Keep the consuming service scoped or transient, or inject `IHttpClientFactory` itself and create clients per use.

### Resilience with Polly

Networks fail transiently. **Polly** (integrated via `Microsoft.Extensions.Http.Resilience`) adds resilience strategies to the handler pipeline:

```csharp
builder.Services.AddHttpClient<CatalogClient>(...)
    .AddResilienceHandler("catalog", pipeline =>
    {
        pipeline.AddRetry(new HttpRetryStrategyOptions
        {
            MaxRetryAttempts = 3,
            BackoffType = DelayBackoffType.Exponential,
            UseJitter = true            // Avoid synchronized retry storms.
        });
        pipeline.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
        {
            FailureRatio = 0.5,
            SamplingDuration = TimeSpan.FromSeconds(30),
            BreakDuration = TimeSpan.FromSeconds(15)
        });
        pipeline.AddTimeout(TimeSpan.FromSeconds(3)); // Per-attempt timeout.
    });
```

The three core patterns, and their intent:
- **Retry** with exponential backoff and jitter handles brief blips. The default `HttpRetryStrategyOptions` retries the transient failures — 5xx, 408, 429, and `HttpRequestException` — not 4xx client errors. Only retry *idempotent* operations, or you may duplicate side effects.
- **Circuit breaker** stops hammering a service that's clearly down. After a failure threshold it "opens" and fails fast for a cooldown, giving the downstream time to recover. Without it, retries amplify an outage into a cascade.
- **Timeout** bounds how long any single attempt may take, so one slow dependency can't tie up your threads.

> **Order matters here too.** Strategies added *earlier* sit *outside* those added later. In the example above, retry is outermost, so the 3-second timeout is a *per-attempt* budget — each retry gets a fresh 3 seconds — while the client's `Timeout` of 10 seconds from the previous section caps the whole operation, retries included. A per-attempt timeout belongs inside the retry; a total timeout belongs outside.

> **Best practice.** Unless you have specific numbers in mind, start with `.AddStandardResilienceHandler()` — one line that applies Microsoft's recommended pipeline (rate limiter, total timeout, retry, circuit breaker, per-attempt timeout) with sensible defaults — and tune only when you have evidence the defaults don't fit.

## Cross-Cutting HTTP Concerns

**CORS** (Cross-Origin Resource Sharing) controls which browser origins may call your API. An **origin** is the scheme + host + port triple (`https://app.example.com`), and the browser's **Same-Origin Policy** forbids JavaScript on one origin from reading responses from another. CORS is how your server *opts in* to specific cross-origin callers: for anything beyond a "simple" request, the browser first sends a **preflight** `OPTIONS` request — "may `https://app.example.com` send a `POST` here with an `Authorization` header?" — the server answers with `Access-Control-Allow-*` headers, and only then does the real request go out. That preflight is why `UseCors` must run before endpoint execution (recall the ordering section): the `OPTIONS` probe has no endpoint of its own — the CORS middleware must answer it.

```csharp
builder.Services.AddCors(o => o.AddPolicy("spa", p => p
    .WithOrigins("https://app.example.com")        // Explicit — never "*" on a real API.
    .WithMethods("GET", "POST")
    .WithHeaders("Authorization", "Content-Type")));
// ...
app.UseCors("spa");
```

CORS is a *browser* enforcement mechanism — it doesn't secure anything server-side, it just tells the browser what's allowed. That cuts both ways. When the console shows *"blocked by CORS policy,"* it means the **browser refused to hand the response to your JavaScript** — the request itself usually still reached your server and executed; check the server logs before assuming nothing happened. And conversely, CORS does nothing against `curl` or another backend — it is not authorization. Chapter 14 covers the security-hardening angle.

> **Pitfall.** `AllowAnyOrigin()` combined with `AllowCredentials()` is forbidden by the spec and won't work. Never reflexively allow all origins in production.

> **Gotcha — CORS is not CSRF protection.** If browsers reach your endpoints with *cookie* authentication, they need **antiforgery** protection, and ASP.NET Core ships it: automatic in Razor Pages/MVC form tag helpers, and available to Minimal APIs via the antiforgery services added in .NET 8. Token-authenticated APIs — where the client sends an `Authorization` header — are not CSRF-vulnerable, because browsers never attach that header automatically to cross-site requests. Chapter 14 gives CSRF its full treatment.

**Rate limiting** (built-in since .NET 7) protects you from abuse and thundering herds. Algorithms include fixed window, sliding window, token bucket, and concurrency limiters:

```csharp
builder.Services.AddRateLimiter(o =>
    o.AddFixedWindowLimiter("api", opt =>
    {
        opt.PermitLimit = 100;
        opt.Window = TimeSpan.FromMinutes(1);
        opt.QueueLimit = 0;
    }));
app.UseRateLimiter();
```

**Output caching** stores the *rendered response* server-side and replays it for matching requests — great for expensive, rarely-changing GETs. It differs from *response caching*, which sets HTTP cache headers and trusts clients/proxies.

**Response compression** (Brotli/Gzip) shrinks payloads. Note that if a reverse proxy (nginx, YARP) already compresses, doing it again in-app is wasted CPU — know your topology.

## REST, Status Codes, Versioning & OpenAPI

**REST** is a set of constraints, not a law, but a few principles pay dividends: model your API around **resources** (nouns) not actions; use HTTP **verbs** for intent (GET read, POST create, PUT replace, PATCH partial update, DELETE remove); make GET/PUT/DELETE **idempotent**; and lean on the **status code** to communicate outcome.

Use the right codes: `200 OK`, `201 Created` (with a `Location` header), `204 No Content` for a successful DELETE, `400` for malformed input, `401` unauthenticated, `403` authenticated-but-forbidden, `404` not found, `409` conflict, `422` semantic validation failure, `429` rate limited, `500` for your bugs. Returning `200` with an error body inside is a common anti-pattern that breaks clients and tooling.

**API versioning** protects existing clients when you evolve. Use `Asp.Versioning.*` packages and pick a strategy — URL segment (`/v1/products`), query string, or header. URL versioning is the most discoverable:

```csharp
builder.Services.AddApiVersioning(o =>
{
    o.DefaultApiVersion = new ApiVersion(1, 0);
    o.AssumeDefaultVersionWhenUnspecified = true;
    o.ReportApiVersions = true; // Emits api-supported-versions header.
});
```

**OpenAPI/Swagger** documents your API in a machine-readable contract. .NET now ships built-in OpenAPI document generation (`AddOpenApi` / `MapOpenApi`); Swagger UI or Scalar renders it for humans. Rich metadata (`WithName`, `Produces`, XML comments, `TypedResults`) makes the generated spec — and any client SDKs generated from it — accurate.

## gRPC

**gRPC** is a contract-first, binary RPC framework built on HTTP/2 and Protocol Buffers. You define the service in a `.proto` file; tooling generates strongly-typed client and server code. It's dramatically more compact and faster than JSON-over-HTTP/1.1, and it natively supports **streaming** in both directions.

```proto
service PriceService {
  rpc GetPrice (PriceRequest) returns (PriceReply);           // Unary
  rpc StreamPrices (PriceRequest) returns (stream PriceReply); // Server streaming
}
```

"Contract-first" becomes concrete when you see both halves. From that `.proto`, the build generates a base class; your implementation is just another endpoint on the pipeline you already know:

```csharp
public class PriceGrpcService : PriceService.PriceServiceBase   // Generated base class.
{
    public override Task<PriceReply> GetPrice(PriceRequest req, ServerCallContext ctx) =>
        Task.FromResult(new PriceReply { Symbol = req.Symbol, Price = 42.17 });

    public override async Task StreamPrices(PriceRequest req,
        IServerStreamWriter<PriceReply> stream, ServerCallContext ctx)
    {
        while (!ctx.CancellationToken.IsCancellationRequested)
        {
            await stream.WriteAsync(new PriceReply { Symbol = req.Symbol, Price = Next() });
            await Task.Delay(1000, ctx.CancellationToken);
        }
    }
}
// app.MapGrpcService<PriceGrpcService>();
```

The client gets the mirror image — no URLs, no serialization, just method calls, with server streams surfacing as `await foreach`:

```csharp
using var channel = GrpcChannel.ForAddress("https://prices.internal");
var client = new PriceService.PriceServiceClient(channel);

var reply = await client.GetPriceAsync(new PriceRequest { Symbol = "MSFT" }); // Unary

using var call = client.StreamPrices(new PriceRequest { Symbol = "MSFT" });
await foreach (var price in call.ResponseStream.ReadAllAsync(ct))             // Server streaming
    Render(price);
```

HTTP/2 is not an implementation detail — it's what makes this possible: many concurrent **multiplexed streams** over one connection are exactly the plumbing that long-lived streaming calls need (Chapter 20 dissects HTTP/2 itself). It also brings gRPC's two operational gotchas. First, you need **end-to-end HTTP/2**: a proxy that downgrades to HTTP/1.1 breaks gRPC. Second, a channel is one long-lived connection, so an L4 (connection-level) load balancer pins *all* of a client's calls to a single server; you need L7, gRPC-aware balancing (Envoy, YARP, Linkerd) to spread the *calls* rather than the *connections*.

Two idioms replace their HTTP cousins. **Deadlines** are gRPC's timeouts — `client.GetPriceAsync(req, deadline: DateTime.UtcNow.AddSeconds(3))` — and, unlike `HttpClient.Timeout`, they *propagate*: the remaining budget travels with the call, and on the server it surfaces as `ServerCallContext.CancellationToken` (which is why the streaming example honors it — the CancellationToken discipline from earlier in this chapter carries straight over). Errors travel as `RpcException` with a `StatusCode` (`NotFound`, `Unavailable`, `DeadlineExceeded`, …) rather than HTTP status codes.

Use gRPC for **internal service-to-service** communication where you control both ends and want performance and a strict contract, or for streaming workloads. It's a poor fit for browser clients (gRPC-Web and JSON transcoding exist, but they cost you much of the elegance) and public APIs where human-readable JSON and broad tooling matter more. The rule of thumb: **gRPC inside the datacenter, REST at the edge.** Protobuf's schema-evolution rules — how to add fields without breaking already-deployed clients — get their full treatment in Chapter 24.

## SignalR

**SignalR** provides real-time, bidirectional communication — the server can push to connected clients, not just respond to requests. It abstracts over WebSockets (falling back to Server-Sent Events or long polling — Chapter 20 compares the transports) so you code against a clean *hub* API.

```csharp
public class NotificationsHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        // A "group" is just a named set of connection ids, tracked server-side.
        await Groups.AddToGroupAsync(Context.ConnectionId, "traders");
        await base.OnConnectedAsync();
    }

    public async Task SendToGroup(string group, string message) =>   // Client → server.
        await Clients.Group(group).SendAsync("notify", message);     // Server → client(s).
}
// app.MapHub<NotificationsHub>("/hubs/notifications");
```

The other half of the conversation lives in the client, which connects once and *registers handlers by event name* — `"notify"` above is not magic, it's the name the client subscribed to:

```ts
const conn = new signalR.HubConnectionBuilder()
    .withUrl("/hubs/notifications").withAutomaticReconnect().build();
conn.on("notify", msg => showToast(msg));          // Handles SendAsync("notify", ...).
await conn.start();
await conn.invoke("SendToGroup", "traders", "hi"); // Calls the hub method.
```

So the model is symmetric: hub methods are client→server calls, and `Clients.*.SendAsync` fires named handlers client-side. (Chapter 29 builds out a full TypeScript client.) One more piece completes the picture: most real pushes don't originate inside a hub at all — a background job finishes, an order ships. For that, inject `IHubContext<NotificationsHub>` anywhere and use the same `Clients` API:

```csharp
public class OrderShippedHandler(IHubContext<NotificationsHub> hub)
{
    public Task Handle(OrderShipped e) =>
        hub.Clients.Group("traders").SendAsync("notify", $"Order {e.Id} shipped");
}
```

Reach for SignalR for dashboards, chat, live collaboration, notifications, and progress updates. The scaling caveat: connections are **stateful and pinned** to one server. Run three instances, and when server 2 wants to notify a user whose WebSocket lives on server 1, it simply can't reach them. A **backplane** (Redis pub/sub) fixes this by republishing every message to every server, each of which forwards it to its own connections — or Azure SignalR Service takes the connections off your servers entirely.

```
   client A ──ws── server 1 ──┐
   client B ──ws── server 2 ──┼── Redis backplane (pub/sub)
   client C ──ws── server 3 ──┘
   server 2 publishes → all servers receive → each pushes to its own sockets
```

> **Gotcha.** Unless you restrict transports to WebSockets-only, the fallback transports involve multiple HTTP requests per connection, so the load balancer needs **sticky sessions**.

### Choosing between REST, gRPC, and SignalR

| You need | Reach for |
|---|---|
| Public API, diverse clients, human-debuggable payloads | REST + JSON |
| Internal service-to-service calls, strict contract, performance, streaming | gRPC |
| Server push to browsers (dashboards, chat, notifications) | SignalR |
| Server→client streaming only, minimal moving parts | SSE — see Chapter 20 |

## Error Handling with ProblemDetails (RFC 7807)

Every API needs a *consistent* error shape. **RFC 7807 ProblemDetails** is the standard: a JSON object with `type`, `title`, `status`, `detail`, and `instance`. Standardizing on it means clients (and tools) can parse errors uniformly instead of guessing.

```csharp
builder.Services.AddProblemDetails();

app.UseExceptionHandler(); // With AddProblemDetails, emits RFC 7807 on unhandled errors.
```

For richer control, implement `IExceptionHandler` (a clean, testable seam) rather than stuffing logic into middleware:

```csharp
public class ValidationExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext ctx, Exception ex, CancellationToken ct)
    {
        if (ex is not FluentValidation.ValidationException vex) return false;

        var problem = new ValidationProblemDetails(
            vex.Errors.GroupBy(e => e.PropertyName)
                      .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray()))
        {
            Status = StatusCodes.Status400BadRequest
        };
        ctx.Response.StatusCode = problem.Status.Value;
        await ctx.Response.WriteAsJsonAsync(problem, ct);
        return true; // Handled — stop the chain.
    }
}
// builder.Services.AddExceptionHandler<ValidationExceptionHandler>();
```

> **Tip — `AddExceptionHandler` vs writing your own exception middleware.** These aren't two independent mechanisms: `UseExceptionHandler()` *is* the middleware, and `AddExceptionHandler<T>()` registers handlers that plug into it — called in registration order until one returns `true`, with anything unhandled falling through to the default ProblemDetails response. A hand-rolled `try/catch` middleware can do the same job, but then you own everything the built-in one already does: safe defaults (status 500, cache headers cleared), the awkward edge case where the response has already started streaming, content negotiation via `IProblemDetailsService`, and the diagnostics logs and metrics observability tooling expects. `IExceptionHandler` classes are also plain DI services — unit-testable with no `RequestDelegate` plumbing, one focused class per exception family instead of a growing `switch`. Reserve custom middleware for concerns that aren't "map this exception to an HTTP response" — releasing a resource or enriching telemetry on every failure, say — or for pre-.NET 8 targets, where the `UseExceptionHandler(errorApp => ...)` lambda overload fills the same role.

> **Best practice.** Never leak stack traces or internal messages to callers in production. `detail` should be safe to show a client; log the gory details server-side with a correlation ID that the client can quote to support.

## Health Checks

Orchestrators (Kubernetes, load balancers) need to know if your app is alive and ready. ASP.NET Core distinguishes two questions:

- **Liveness** — "is the process healthy, or should it be restarted?" It should *not* depend on external systems; a failed database shouldn't cause a restart loop.
- **Readiness** — "can this instance serve traffic right now?" This *does* check dependencies (DB, message broker), so a not-ready instance is pulled from rotation without being killed.

```csharp
builder.Services.AddHealthChecks()
    .AddCheck("self", () => HealthCheckResult.Healthy(), tags: ["live"])
    .AddNpgSql(connString, tags: ["ready"]);

app.MapHealthChecks("/health/live",
    new() { Predicate = c => c.Tags.Contains("live") });
app.MapHealthChecks("/health/ready",
    new() { Predicate = c => c.Tags.Contains("ready") });
```

Tagging checks and filtering by tag is what keeps liveness and readiness cleanly separated.

## Observability Wiring

ASP.NET Core is instrumented *out of the box*. Kestrel and the hosting layer emit metrics (request rate, duration, active connections) through `System.Diagnostics.Metrics` and the older EventCounters, and every request runs inside an `Activity` — .NET's native distributed-tracing span. The instrumentation is always there; what's missing by default is something *listening*. That's what OpenTelemetry provides:

```csharp
builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("shop-api"))
    .WithTracing(t => t
        .AddAspNetCoreInstrumentation()      // Span per incoming request.
        .AddHttpClientInstrumentation()      // Span per outbound call.
        .AddOtlpExporter())                  // Ship to your collector.
    .WithMetrics(m => m
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddOtlpExporter());
```

With those few lines, every request produces a trace: a root span from the ASP.NET Core instrumentation, child spans for each `HttpClient` call, all exported over OTLP to whatever backend you run (Jaeger, Tempo, an APM vendor — the wire format is standard). Better still, propagation is automatic: `HttpClient` injects the **W3C trace-context** `traceparent` header on outbound calls and ASP.NET Core reads it on inbound ones, so when service A calls service B, both ends land in the *same* trace without either team writing propagation code. Combine that with the `IHttpClientFactory` discipline from earlier in this chapter and a slow endpoint stops being a mystery — the trace shows you exactly which downstream call ate the time budget.

Chapter 13 covers observability as a discipline — custom `ActivitySource` spans, metrics design, log correlation, propagating context through message queues. The point here is narrower but important: the web framework *participates natively*. A few lines in `Program.cs` and every request carries a trace.

## A Brief Note on Blazor

**Blazor** lets you build interactive web UIs in C# instead of JavaScript. **Blazor Server** runs your components on the server and streams UI diffs to the browser over a SignalR connection — tiny download, but every interaction is a round-trip and each user holds a stateful connection. **Blazor WebAssembly** runs the .NET runtime in the browser and calls your API like any SPA would — offline-capable, at the cost of a larger initial download. From this chapter's perspective, Blazor is just another consumer of your APIs or another host in your pipeline; Chapter 29 covers the render models, JS interop, and when to choose Blazor over a JavaScript SPA.

> **Capstone tie-in:** This chapter is exercised by ShopCore Step 1 (The Honest Monolith) — you'd build a single ASP.NET Core Web API exposing CRUD-plus-checkout endpoints for products, carts, and orders. See Chapter 32.

## Summary

The through-line of this chapter is that ASP.NET Core is a **pipeline of composable components**, and nearly every feature — auth, CORS, rate limiting, error handling — is just middleware or a filter slotted into that pipeline in the right order. Master the request lifecycle and the rest becomes a matter of choosing the right tool: Minimal APIs or Controllers, JWT or cookies, policies over roles, REST at the edge and gRPC within, resilience on every outbound call, cancellation tokens propagated through every awaited I/O, a trace on every request, and consistent ProblemDetails when things go wrong. Those are the instincts that separate a senior engineer from someone who merely returns JSON.
