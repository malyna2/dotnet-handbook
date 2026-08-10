# Chapter 3: ASP.NET Core & Web APIs

_⏱️ Estimated read time: ~1 h 15 min · 10751 words (study pace)_

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

**What a validator is actually for.** Before the API surface, settle the layering question, because it is the one teams get wrong. A request validator answers *"is this request well-formed?"* — the transport-level question. Is the JSON shaped right, are the strings within length, is the date parseable, is the enum a member of the set. A domain invariant answers a different question: *"is this state legal?"* — an order cannot ship before it is paid, a balance cannot go negative, a discount cannot exceed the line total. The first is about the message; the second is about the model.

The reason to keep them apart is mechanical, not aesthetic. A validator runs only on the code path where you remembered to invoke it, and your HTTP endpoint is not the only way state changes: a background job, a message consumer, an admin script, and next quarter's gRPC endpoint all mutate the same aggregate, and none of them go through `CreateProductValidator`. If the only thing standing between your system and an illegal state is a validator hanging off one controller, that illegal state is one new code path away.

So validate at the edge *and* enforce in the domain. The edge validator's job is to hand the caller a good `400` with a field-level error list instead of a `500` from a constructor throw. The domain's job is to make the illegal state unrepresentable no matter who calls it — guard clauses and value objects ([Chapter 5: Design Patterns, Principles & Clean Code](#chapter-5-design-patterns-principles-clean-code)) and invariants enforced on the aggregate root ([Chapter 6: Architecture & Application Design](#chapter-6-architecture-application-design)). Checking "name is 3–120 characters" in both places is not duplication to be refactored away; they are two checks with different jobs and different failure modes. **Validation at the edge does not excuse an anaemic domain.**

**Wiring it up.** Validators are registered by assembly scan:

```csharp
builder.Services.AddValidatorsFromAssemblyContaining<CreateProductValidator>();
```

That registers every `AbstractValidator<T>` in the assembly as `IValidator<T>` — scoped by default, so validators may take DI dependencies. What it deliberately does *not* do is hook into MVC's model-binding pipeline. The old `AddFluentValidationAutoValidation()` integration is deprecated, and the reason is instructive: it ran inside model binding, which is synchronous, so async rules had to be blocked on; it fired for *every* bound complex type whether you wanted it or not; and it reported failures through `ModelState`, a mechanism it had to reverse-engineer. The modern posture is explicit — resolve `IValidator<T>` and call it where you decide:

```csharp
products.MapPost("/", async (CreateProductRequest request,
    IValidator<CreateProductRequest> validator, IProductService service, CancellationToken ct) =>
{
    var result = await validator.ValidateAsync(request, ct);
    if (!result.IsValid)
        return Results.ValidationProblem(result.ToDictionary());

    return Results.Ok(await service.CreateAsync(request, ct));
});
```

`ToDictionary()` produces the `field → messages` map that `Results.ValidationProblem` renders as `ValidationProblemDetails` — the same RFC 7807 shape `[ApiController]` emits, so a mixed app returns one error format rather than two.

Writing those four lines in every endpoint gets old, so lift them into an endpoint filter (the Minimal API filter from earlier in this chapter):

```csharp
public class ValidationFilter<T> : IEndpointFilter where T : class
{
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
    {
        var model = ctx.Arguments.OfType<T>().FirstOrDefault();
        var validator = ctx.HttpContext.RequestServices.GetService<IValidator<T>>();
        if (model is null || validator is null) return await next(ctx);

        var result = await validator.ValidateAsync(model, ctx.HttpContext.RequestAborted);
        return result.IsValid
            ? await next(ctx)
            : TypedResults.ValidationProblem(result.ToDictionary());
    }
}

products.MapPost("/", ...).AddEndpointFilter<ValidationFilter<CreateProductRequest>>();
```

The controller equivalent is an `IAsyncActionFilter` that pulls the model out of `context.ActionArguments`; or, if you prefer validators that throw, let a `ValidationException` escape and map it with the `IExceptionHandler` shown later in this chapter.

> **Gotcha.** That filter *fails open* — no registered validator means the request sails through. That is the right default for a generic filter (you don't want every parameter-less endpoint to 500), but it means a mistyped validator class silently disables validation for an endpoint, and nothing fails. If you apply the filter by convention across a group, add a startup test that asserts every request DTO reachable from your endpoints has a registered `IValidator<T>`.

**Composition.** The fluent API earns its keep on rules that DataAnnotations can't express at all:

```csharp
public class CreateOrderValidator : AbstractValidator<CreateOrderRequest>
{
    public CreateOrderValidator(IValidator<OrderLineDto> lineValidator)
    {
        ClassLevelCascadeMode = CascadeMode.Continue;   // report every bad property...
        RuleLevelCascadeMode  = CascadeMode.Stop;       // ...but one message per property

        RuleFor(x => x.CustomerId).NotEmpty().WithErrorCode("order.customer_required");
        RuleFor(x => x.Lines).NotEmpty().WithErrorCode("order.lines_empty");

        RuleForEach(x => x.Lines).SetValidator(lineValidator);   // one child validator per element

        When(x => x.Coupon is not null, () =>
        {
            RuleFor(x => x.Coupon!.Code)
                .Matches("^[A-Z0-9]{4,12}$").WithErrorCode("coupon.malformed");
            RuleFor(x => x.Coupon!.Amount)
                .LessThanOrEqualTo(x => x.Lines.Sum(l => l.UnitPrice * l.Quantity))
                .WithErrorCode("coupon.exceeds_total");
        });

        RuleSet("Admin", () =>
            RuleFor(x => x.BackdatedAt).NotNull().WithErrorCode("order.backdate_required"));
    }
}
```

- **`RuleForEach(...).SetValidator(...)`** delegates each collection element to its own validator and prefixes the failure's `PropertyName` with the index — `Lines[2].Quantity` — so the client can highlight the offending row instead of the whole array. `SetValidator` on a single property does the same for a nested object.
- **`When` / `Unless`** gate rules on a predicate. Prefer the block form above over a `.When(...)` tacked onto each rule: the condition is stated once, reads as one branch, and won't drift when someone adds a rule inside it.
- **`RuleSet`** names a group that runs only when asked: `validator.ValidateAsync(order, o => o.IncludeRuleSets("Admin"), ct)`. Handy when the same DTO arrives from two callers with different privileges — though two DTOs is often the cleaner answer.
- **Cascade modes** decide what happens *after* a failure. `RuleLevelCascadeMode = Stop` ends a single property's chain at its first failure, so a null `Name` reports "required" rather than "required" *and* "must be 3–120 characters". `ClassLevelCascadeMode = Stop` abandons the entire validator after the first bad property — almost never what an API wants, because the caller would rather fix everything in one round trip than play whack-a-mole.

> **Best practice — stable error codes.** `WithErrorCode` attaches a machine-readable identifier alongside the human message, and it is what lets a client branch on *which* rule failed instead of string-matching `"Coupon exceeds order total"`. Messages get localized, reworded by product, and tweaked by whoever last touched the file; codes are a contract. Surface them — an `errors` array of `{ code, field, message }` objects carries more than the flat field→messages dictionary — and treat renaming a code as a breaking change (see the versioning section below).

**Async rules, and the trap inside them.** Validators can hit the database, because they are DI services:

```csharp
RuleFor(x => x.Sku)
    .MustAsync(async (sku, ct) => !await db.Products.AnyAsync(p => p.Sku == sku, ct))
    .WithErrorCode("product.sku_taken")
    .WithMessage("SKU '{PropertyValue}' is already in use.");
```

This is worth having: it turns a constraint violation into a friendly, field-attributed `400` instead of a `500` from a `DbUpdateException`. What it is *not* is a uniqueness guarantee. Between the `AnyAsync` that answers "free" and the `SaveChangesAsync` that inserts, another request can do exactly the same thing — textbook check-then-act, with a race window as wide as the rest of your request handling. Two concurrent requests carrying the same SKU both pass validation and both insert.

The only thing that actually enforces uniqueness is a **unique index in the database** ([Chapter 4: Data Access & Databases](#chapter-4-data-access-databases)), because it is the sole check that happens inside the same atomic operation as the write. So run both: the validator produces the good error message for the overwhelmingly common case, the index produces correctness for the rest, and you catch the resulting `DbUpdateException` and map it onto the same payload the validator would have returned. If you only build one of the two, build the index.

> **Pitfall — one DbContext, one operation at a time.** A validator that injects `AppDbContext` shares the request's *scoped* instance with the handler and with every other validator in that request. A single `ValidateAsync` is safe because FluentValidation awaits rules sequentially — but the moment you fan out (`Task.WhenAll` over several validators, or validating a batch request's items in parallel), two `MustAsync` rules can touch the context simultaneously and you get *"A second operation was started on this context instance."* Either keep validation sequential, or inject `IDbContextFactory<AppDbContext>` and open a short-lived context per check (Chapter 4 covers context lifetime and pooling). Also note that one `MustAsync` makes the whole validator async: calling the synchronous `Validate()` on it throws.

**Testing them.** Validators are plain objects with no HTTP anywhere near them, which makes them the cheapest unit tests in the codebase. `FluentValidation.TestHelper` gives you assertions expressed as expressions over the model:

```csharp
[Fact]
public void Rejects_blank_name()
{
    var validator = new CreateProductValidator();

    var result = validator.TestValidate(new CreateProductRequest { Name = "", Price = 10m });

    result.ShouldHaveValidationErrorFor(x => x.Name)
          .WithErrorCode("product.name_required");
    result.ShouldNotHaveValidationErrorFor(x => x.Price);
}
```

Because the property is named by lambda rather than by string, renaming `Name` is a compile error instead of a test that quietly passes against a stale `"Name"` literal. Assert on `WithErrorCode`, not `WithErrorMessage`, for the same reason you gave clients codes in the first place: the wording will change. And test the *negative* cases — the empty string, the boundary value, the null coupon, the conditional branch that only fires when `Coupon` is set — because those are the branches production will find for you otherwise.

**Which mechanism, when.**

| Approach | Good at | Where it runs out |
|---|---|---|
| **DataAnnotations** | Declarative shape checks sitting next to the DTO; zero wiring under `[ApiController]`; the attributes flow into the OpenAPI schema, so `[Required]`/`[StringLength]` show up in generated client SDKs | Cross-field and conditional rules (`IValidatableObject` or a custom attribute — both awkward); no DI, so no async or data-backed rules; one fixed rule set per type, so it can't vary by caller or use case |
| **FluentValidation** | Conditional, cross-field, and per-element collection rules; DI and async; several validators for one shape; stable error codes; trivial to unit test | Invisible to OpenAPI unless you add a schema filter; must be explicitly invoked, so it guards only the paths you wired; still check-then-act against the database |
| **Domain guard / value object** | Holds for *every* caller — HTTP, consumer, job, test; makes illegal state unrepresentable; the rule lives next to the concept it constrains | Throws on the first violation rather than accumulating them, so it yields a poor error document; fires too late to give the client a field-level list |

They are layers, not alternatives. DataAnnotations (or nothing) for trivial DTOs, FluentValidation at the edge to produce a good error document, and domain guards as the thing you would actually bet correctness on.

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

### Idempotency Keys: Making POST Retry-Safe

GET, PUT and DELETE are idempotent *by the definition of the verb*: `PUT /orders/42` with the same body leaves the same state whether it runs once or five times, and a second `DELETE /orders/42` finds nothing left to delete. POST is the exception — it means "process this as a new subordinate resource," and the whole point is that each call creates something.

That asymmetry stops being a semantic curiosity the moment a request times out. A timeout tells the client nothing useful: the request may never have arrived, may have executed with the response lost on the way back, or may still be running. The only thing the client knows is that it doesn't know. So it retries — the Polly pipeline from earlier in this chapter retries, the mobile app's network layer retries, the user hits the button again — and if that POST charged a card, the customer is charged twice. "The client retried after a timeout" is not an edge case; it is the *normal* behaviour of every HTTP client on a lossy network, which makes this a correctness problem rather than a nicety.

The fix is to let the client supply the identity of the **operation**, not just of the request:

```
POST /payments
Idempotency-Key: 5f3b8a1e-9c04-4f4a-8a0e-2b7c1d33e9a1
Content-Type: application/json

{ "orderId": 42, "amount": 19.99 }
```

The key is generated **once, before the first attempt**, and reused for every retry of that same logical operation. A key regenerated per HTTP attempt is worse than useless — it makes retries look like distinct operations, which is precisely what you were trying to prevent. Server-side you keep a record per key:

```csharp
public class IdempotencyRecord
{
    public string Endpoint { get; set; } = default!;    // same key on /refunds is a different op
    public string Key { get; set; } = default!;         // client-supplied
    public string RequestHash { get; set; } = default!; // SHA-256 of the canonical body
    public int StatusCode { get; set; }                 // 0 while in flight
    public string? ResponseBody { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
```

Three outcomes, and the third is the one people forget to implement:

| Repeat request with the same key | Response |
|---|---|
| Same body hash, first attempt completed | Replay the stored status and body verbatim; add `Idempotency-Replayed: true` so the caller can tell |
| Same body hash, first attempt still in flight | `409 Conflict` with `Retry-After` — "in progress, ask again shortly" |
| **Different** body hash | `422 Unprocessable Content` (or `409`) — the key was reused for a different operation, which is a client bug and must be surfaced loudly |

Storing the request hash is what makes that last row possible. Without it, a client that recycles keys — a hard-coded constant in a test harness, a key derived from a non-unique order number — silently receives someone else's response, and you will spend a long afternoon working out why.

**The concurrency detail that makes it actually work.** The naïve implementation reads the table, sees no row, does the work, then writes the row. That is the same check-then-act race as the uniqueness validator earlier in this chapter, except here the prize is a duplicate charge: two retries arriving 20 ms apart both read "no row" and both charge.

What arbitrates is a **unique index on `(Endpoint, Key)`** combined with the ordering — *insert the key row first, inside the same transaction as the side effect*:

```csharp
await using var tx = await db.Database.BeginTransactionAsync(ct);

var record = new IdempotencyRecord
{
    Endpoint = "POST /payments", Key = key, RequestHash = hash,
    StatusCode = 0, CreatedAt = DateTimeOffset.UtcNow
};
db.IdempotencyRecords.Add(record);

try
{
    await db.SaveChangesAsync(ct);        // The unique index arbitrates HERE.
}
catch (DbUpdateException ex) when (IsUniqueViolation(ex))  // 23505 on Npgsql, 2601/2627 on SQL Server
{
    await tx.RollbackAsync(ct);
    return await ReplayOrConflictAsync(key, hash, ct);      // The loser never reaches the charge.
}

var payment = await _payments.ChargeAsync(request, ct);     // The side effect.

record.StatusCode = StatusCodes.Status201Created;
record.ResponseBody = JsonSerializer.Serialize(payment);
await db.SaveChangesAsync(ct);
await tx.CommitAsync(ct);
```

The ordering is the entire trick, so walk the second concurrent request through it. It attempts the same insert; the unique index rejects it, so it learns — atomically, with no read-then-write window anywhere — that it lost the race. It rolls back and reads the existing row. If that row carries a status code, the first attempt finished and the loser replays it. If the status is still `0`, the first attempt is in flight and the loser answers `409` with `Retry-After: 1`. Either way it never reaches `ChargeAsync`.

Now invert the order and do the work first: both requests charge the card, and *then* one of them discovers it lost. The damage is already done and you are writing a refund. The row must go in before the effect, in the same transaction, or the pattern buys you nothing.

```
request A ──┬─ INSERT (POST /payments, key) ──► accepted ──► charge ──► store 201 ──► COMMIT
            │
request B ──┴─ INSERT (POST /payments, key) ──► unique violation
                                                    │
                                       status 0? ───┴──► 409 + Retry-After
                                       status set? ────► replay stored response
```

Two loose ends remain.

**A first attempt that never finishes.** If the process dies between the insert and the update, the row sits at status `0` forever and every retry gets a `409`. Give the record a lease — store a `LockedUntil` and treat an expired in-flight row as reclaimable — or run a sweeper that ages stale rows out. Whether reclaiming is safe depends on whether re-running the side effect is safe, which is why the strongest version of this pattern forwards the same key downstream: most payment gateways accept an idempotency key of their own, so you hand yours through and let them deduplicate the charge you may or may not have made.

**Retention.** Idempotency records are a cache, not an audit log. Keep them long enough to cover any plausible retry window — Stripe uses 24 hours, and 24–72 hours suits most systems — then delete them from a background job with a batched `ExecuteDeleteAsync`, never a cascade on the request path. This table takes a write on the hot path of every mutating request, so unbounded growth is a genuine operational problem rather than a tidiness concern.

> **Best practice.** Scope the key by caller as well as endpoint — `(TenantId, Endpoint, Key)`. Keys are client-generated, and one client's copy-pasted GUID must never be able to replay another client's response. Decide explicitly, too, whether a replay re-runs authorization: it should, because a stored `201` must not be handed to a caller who has since lost the entitlement.

> **Gotcha.** Idempotency is not the same as "it worked." Replaying a stored `500` on retry is almost always wrong — a genuine server error is exactly the case where the client *should* get a fresh attempt. Record only deterministic outcomes: successes and client errors. Leave 5xx unstored (release the key) so the retry re-executes.

This is the HTTP-facing sibling of a pattern that shows up twice more in this book — idempotent message consumers in [Chapter 9: Messaging & Distributed Systems](#chapter-9-messaging-distributed-systems), which dedupe on a message ID with the same unique-index backstop, and the general treatment in [Chapter 6: Architecture & Application Design](#chapter-6-architecture-application-design). One mechanism (record the operation's identity atomically with its effect), three transports.

### Versioning and OpenAPI

**API versioning** protects existing clients when you evolve. It is also a decision that is expensive to reverse and easy to get subtly wrong, so it gets the next section to itself — including the more useful question of how to avoid needing a new version at all.

**OpenAPI/Swagger** documents your API in a machine-readable contract. .NET now ships built-in OpenAPI document generation (`AddOpenApi` / `MapOpenApi`); Swagger UI or Scalar renders it for humans. Rich metadata (`WithName`, `Produces`, XML comments, `TypedResults`) makes the generated spec — and any client SDKs generated from it — accurate.

## API Versioning & Backward Compatibility

Versioning is the mechanism teams reach for; backward compatibility is the actual goal. Get the second right and you need far less of the first, so start there.

### What actually counts as a breaking change

A change is breaking if a *correctly written, already deployed* client stops working. The surprise is how asymmetric that turns out to be — the same edit is harmless in one direction and fatal in the other.

| Change | Verdict | Why |
|---|---|---|
| Add an **optional** request field | Safe | Old clients omit it; the server already has a default |
| Add a **required** request field | **Breaking** | Every request in flight is now invalid |
| Add a field to a response | Usually safe | Tolerant readers ignore it; strict or generated clients may not |
| Remove or rename a response field | **Breaking** | Clients read fields by name |
| **Tighten** validation (`maxLength` 200 → 100, add a regex) | **Breaking** | Requests that were legal yesterday now `400` |
| Loosen validation | Safe | Strictly widens what is accepted |
| Change a field's type or format (`int` → `string`, epoch → ISO-8601) | **Breaking** | Deserialization fails, or silently coerces |
| Start returning `null` for a field that was always populated | **Breaking** | The client dereferences it without checking |
| Add a new **enum value** | **Breaking for strict readers** | A generated client mapping to a closed enum throws on the unknown member |
| Add a new endpoint or optional query parameter | Safe | Nobody calls what they don't know about |
| Change a success status code (`200` → `202`) | **Breaking** | Clients switch on the code, and `201` vs `200` changes where they look for the resource |
| Change the error shape (bare string → `ProblemDetails`) | **Breaking** | Error handling is contract too — and it is the part nobody thinks to version |
| Change default page size or default ordering | **Breaking in practice** | Not in the schema, but pagination loops and tests depend on it |
| Change a field's *meaning*, keeping its name and type | **The worst kind** | `amount` in dollars becomes `amount` in cents |

Two rows deserve emphasis. **Tightening validation** is the one that slips through review, because it looks like a bug fix: someone notices `Description` accepts 10 000 characters and caps it at 500. Every client happily sending 800 now gets a `400`, and you changed the contract without touching a single type — which is also why an OpenAPI diff won't flag it unless you compare constraints, not just shapes. And **changing semantics silently** is the only entry with no failure mode at all: nothing throws, no alert fires, and finance reconciliation finds it three weeks later. If a field's meaning changes, give it a new name. Always.

### The tolerant reader

Postel's law — "be conservative in what you send, liberal in what you accept" — is usually quoted at servers, but the leverage sits on the client side. A **tolerant reader** deserializes only the fields it actually uses, ignores everything else, and does not fall over on an unknown enum member.

`System.Text.Json` is tolerant by default: unknown JSON properties are dropped silently unless you opt into `JsonSerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow`. That default is a feature. Resist the urge to "tighten" it in the name of strictness — every additive change your provider makes then becomes a non-event for you instead of a deployment.

The usual failure mode is generated code. An SDK generated from an OpenAPI document models enums as a closed C# enum and, depending on the generator, throws on a value it has never heard of — which is why "just adding an enum member" sits in the breaking column. Provider-side defences: use string values rather than ints on the wire, and document that consumers must tolerate unknown members. Consumer-side: deserialize such a field as `string` and map it yourself with an explicit `_ => Unknown` arm. There is a second, quieter hazard on the consumer side too — a client that deserializes a payload into a strict model and later re-serializes it (a read-modify-write PUT, say) silently *drops* every field it didn't model, wiping data it never knew existed.

> **Best practice.** Both halves being forgiving is what makes evolution cheap: the provider only ever adds, and the consumer only ever reads what it needs. Break either half of that bargain — a provider that renames, or a consumer that round-trips through a strict model — and every change turns into a coordinated deployment.

### Choosing a versioning scheme

| Scheme | Looks like | Pros | Cons |
|---|---|---|---|
| **URL path** | `/v2/products` | Visible in logs, browsers, and `curl`; part of the CDN cache key for free; routing is ordinary routing; two versions can be split at the proxy and deployed independently | Breaks the "one URI per resource" ideal — the same product has two URLs; the version leaks into every link you emit |
| **Query string** | `/products?api-version=2.0` | Unobtrusive; naturally defaults when absent | Easy to lose — proxies and caches may normalize or ignore it; clutters every URL; awkward inside hypermedia links |
| **Custom header** | `X-Api-Version: 2.0` | Keeps URLs clean and stable across versions | Invisible in an address bar and in most access logs; caches ignore it unless you set `Vary`; "send me the curl that fails" support requests get harder |
| **Media type** | `Accept: application/vnd.acme.product.v2+json` | The purest model — the version belongs to the *representation*, not the resource; gives per-resource granularity | Almost nobody does it; thin tooling support; confusing to casual consumers; one more content-negotiation path to get wrong |

The honest ranking: **URL path** for public APIs, because debuggability and cache behaviour beat purity, and because a version in the path is what lets a proxy route v1 and v2 to different deployments. **Media type** if you have sophisticated consumers and genuinely per-resource versioning needs — accept that you will be explaining it forever. Header and query string are defensible middle grounds. What matters far more than the choice is picking one and applying it uniformly.

> **Gotcha — caching.** Any scheme that puts the version *outside* the URL needs `Vary` on the responses, or a shared cache will happily serve a v1 body to a v2 request. This is the quiet reason URL versioning keeps winning arguments it should lose on aesthetics.

### Wiring it up with Asp.Versioning

```csharp
builder.Services.AddApiVersioning(o =>
{
    o.DefaultApiVersion = new ApiVersion(1, 0);
    o.AssumeDefaultVersionWhenUnspecified = true;
    o.ReportApiVersions = true;                  // api-supported-versions / api-deprecated-versions
    o.ApiVersionReader = ApiVersionReader.Combine(
        new UrlSegmentApiVersionReader(),                  // /v2/products
        new HeaderApiVersionReader("X-Api-Version"),
        new QueryStringApiVersionReader("api-version"));
})
.AddApiExplorer(o =>                             // Asp.Versioning.Mvc.ApiExplorer
{
    o.GroupNameFormat = "'v'VVV";                // v1, v1.1, v2 — becomes the OpenAPI group name
    o.SubstituteApiVersionInUrl = true;          // resolves {version:apiVersion} in the docs
});
```

`ApiVersionReader.Combine` accepts *any* of the configured sources, which is the pragmatic default during a migration: you can move a consumer from the header to the URL without a flag day. `ReportApiVersions` makes every response advertise what exists, so a client can discover a new version without reading your changelog.

For Minimal APIs, versions hang off a **version set** shared by a group:

```csharp
var versions = app.NewApiVersionSet()
    .HasApiVersion(new ApiVersion(1, 0))
    .HasApiVersion(new ApiVersion(2, 0))
    .Build();

var products = app.MapGroup("/api/v{version:apiVersion}/products")
                  .WithApiVersionSet(versions);

products.MapGet("/{id:int}", GetProductV1).MapToApiVersion(new ApiVersion(1, 0));
products.MapGet("/{id:int}", GetProductV2).MapToApiVersion(new ApiVersion(2, 0));
```

Two handlers on the same route template is not a conflict — version matching resolves it. Controllers use the attribute form: `[ApiVersion("2.0")]` on the class, `[MapToApiVersion("1.0")]` on any action that stayed behind, over a `[Route("api/v{version:apiVersion}/[controller]")]` template.

Per-version OpenAPI documents then fall out of the API explorer, which stamps each endpoint with the group name from `GroupNameFormat`:

```csharp
builder.Services.AddOpenApi("v1");
builder.Services.AddOpenApi("v2");   // one document per version
// ...
app.MapOpenApi();                    // /openapi/v1.json, /openapi/v2.json
```

Each document picks up the endpoints whose group matches its name. The payoff is that generated client SDKs become version-specific: a consumer regenerates against `v2.json` when *it* is ready, not when you ship.

### Expand and contract: how to not need a new version

Most changes that feel like they demand a `v2` don't. **Expand–contract** (also called parallel change) is the same manoeuvre the zero-downtime schema migrations of [Chapter 23: Data at Scale & Multi-Tenancy](#chapter-23-data-at-scale-multi-tenancy) use, applied to a wire contract instead of a table:

```
expand    add the new field/endpoint alongside the old one; write both, read either
migrate   move consumers to the new one, individually, at their own pace
contract  once telemetry shows nobody reads the old one, delete it
```

Renaming `name` to `fullName` in a response is the canonical example. As a version bump it costs a parallel v2 surface, a duplicated route table, a second set of tests, and a migration deadline imposed on every consumer. As expand–contract it costs one release that emits *both* fields, a window in which you watch which one consumers actually read, and a second release that drops `name`. No version, no deadline, no coordination meeting.

The same move absorbs most of the breaking table above. Tightening validation? Log the violations for one release without rejecting them, see who trips, then enforce. Changing units? New field, new name, deprecate the old. Splitting one endpoint into two? Ship the pair, leave the old endpoint delegating to them, retire it when it goes quiet.

Reserve a new version for the changes expand–contract genuinely cannot absorb: a restructured resource model, a different auth scheme, a workflow whose steps changed shape. Every version you create is a code path you maintain, a test matrix you run, and a deprecation conversation you will eventually have to have. **The cheapest version is the one you didn't need.**

### Retiring a version

Shipping v2 is the easy half. Deleting v1 is where teams stall, sometimes for years, and the reason is almost never technical.

Announce the retirement in the responses themselves, not only in a blog post. RFC 8594 defines the `Sunset` header — the date the resource stops working — usually paired with a `Deprecation` header and a `Link` to the migration guide. Note that these must be written *before* the response starts, so hook `OnStarting` rather than setting them after `await next`:

```csharp
app.Use((ctx, next) =>
{
    ctx.Response.OnStarting(() =>
    {
        if (ctx.GetRequestedApiVersion()?.MajorVersion == 1)
        {
            ctx.Response.Headers["Deprecation"] = "true";
            ctx.Response.Headers["Sunset"] = "Wed, 31 Dec 2026 23:59:59 GMT";
            ctx.Response.Headers["Link"] =
                "<https://docs.acme.com/api/v2-migration>; rel=\"deprecation\"";
        }
        return Task.CompletedTask;
    });
    return next(ctx);
});
```

`ReportApiVersions = true` complements this automatically with `api-supported-versions: 1.0, 2.0` and `api-deprecated-versions: 1.0` on every response, and marking a version deprecated is one attribute — `[ApiVersion("1.0", Deprecated = true)]`, or `.HasDeprecatedApiVersion(...)` on a version set. A well-behaved client can then alert on its own, before your sunset date arrives.

**The part most teams miss.** None of that tells you whether it is *safe* to delete v1, and that is the actual blocker. "Is anyone still on v1?" is answerable from an aggregate counter. The question you actually need answered is "*who* is still on v1, how much, and doing what?" — and you cannot answer it retroactively. From the day v2 ships, tag your request telemetry with the resolved API version **and** a consumer identity: the client id from the token, an API key, a mandated `User-Agent`. Retirement then becomes a report rather than a debate — three consumers, two of them internal, one making forty calls a day — and you email them instead of guessing. Without per-version, per-consumer telemetry, the honest answer to "can we delete v1?" is permanently "we don't know," and "we don't know" always loses to "leave it running." [Chapter 13: Observability](#chapter-13-observability) covers the instrumentation.

> **Pitfall — cardinality.** Consumer identity is exactly the kind of unbounded value that wrecks a metrics backend (Chapter 13's cardinality warning applies directly). With a handful of known partners, a metric tag is fine. With a large or open consumer base, put version and consumer on the *log or span* instead and answer the retirement question with a query over traces — high-cardinality data belongs there, not in a time series.

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

Under the hood there are three moving parts, and no magic. Every check is an **`IHealthCheck`** — a single method returning a result — and `AddCheck`/`AddNpgSql` merely register implementations in DI (the community `AspNetCore.HealthChecks.*` packages ship prebuilt checks for nearly every dependency you can name). When a probe hits the endpoint, **`HealthCheckService`** runs every registered check whose tags pass the `Predicate` — *concurrently*, each receiving a `CancellationToken`. The endpoint then aggregates: **the worst individual status wins**, and maps to HTTP — `Healthy` and `Degraded` return `200`, `Unhealthy` returns `503`.

That third status is the one people forget. **`Degraded`** means "working, but not well" — replica lag, a slow dependency, a queue backing up. Because it still returns `200`, the orchestrator won't kill or drain the instance; it exists as a signal for dashboards and alerting, a yellow light between green and red. Writing a custom check is just implementing the interface:

```csharp
public class QueueBacklogHealthCheck(IQueueClient queue) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext ctx, CancellationToken ct)
    {
        var depth = await queue.GetDepthAsync(ct);
        return depth switch
        {
            < 1_000  => HealthCheckResult.Healthy(),
            < 10_000 => HealthCheckResult.Degraded($"Backlog: {depth}"),
            _        => HealthCheckResult.Unhealthy($"Backlog: {depth}")
        };
    }
}
// builder.Services.AddHealthChecks()
//     .AddCheck<QueueBacklogHealthCheck>("queue-backlog", tags: ["ready"]);
```

By default the endpoint's body is the bare aggregate as plain text (`Healthy`). For a per-check JSON breakdown — which check failed, why, and how long it took — plug in a `ResponseWriter` (the `HealthChecks.UI.Client` package ships a ready-made one). For *push*-based monitoring, `IHealthCheckPublisher` inverts the flow: the app runs its checks on a timer and publishes results to your telemetry instead of waiting to be probed.

> **Pitfall.** Probes hit these endpoints every few seconds, on every instance. An expensive readiness check — a full table scan, an uncached remote call — now runs at probe frequency × server count, and can itself become the load that takes a wobbly dependency down. Keep checks cheap, bound them with timeouts, and cache the verdict of any costly one. And keep dependencies *out of liveness*: Chapter 11's probe section shows how a database blip otherwise becomes a cluster-wide restart storm.

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
