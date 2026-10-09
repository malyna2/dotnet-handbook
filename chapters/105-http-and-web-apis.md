# Chapter 5: HTTP and Web APIs

@@TODO: write this chapter's introduction (what it makes the reader able to do, how its sections connect), then remove every @@ line.@@

@@SRC: introduction of old Chapter 3: ASP.NET Core & Web APIs@@

ASP.NET Core is the beating heart of most .NET server-side work. If you've been building APIs for a couple of years, you already know how to make an endpoint return JSON. This chapter is about the *why* underneath: how a request actually travels through your application, where the extension points live, and how the senior-level decisions (versioning, resilience, auth, real-time) fit together. By the end you should be able to reason about the framework rather than just use it.

@@SRC: introduction of old Chapter 20: Networking & Web Fundamentals@@

Most application bugs that keep senior engineers up at night are not really *code* bugs. They are *network* bugs wearing a code costume. A method that works flawlessly on your laptop times out in production. A service that handled a thousand requests per second suddenly throws `SocketException` under load. A cross-origin `fetch` gets blocked by the browser for reasons nobody on the team can quite articulate.

The difference between a mid-level developer and a senior one is often just this: the senior developer has a *mental model* of what happens between the moment their code calls `await httpClient.GetAsync(url)` and the moment bytes come back. This chapter builds that model. We will travel from the abstract layered models down to the wire, back up through DNS and HTTP, and finally into the operational machinery — load balancers, proxies, CDNs — that sits between your code and your users.

Here is the whole journey at a glance; the rest of the chapter unpacks each hop:

```
await httpClient.GetAsync(url)
  |
  |  DNS lookup      name -> IP (cached per TTL)       \
  |  TCP handshake   SYN / SYN-ACK / ACK      1 RTT     | skipped when a pooled
  |  TLS 1.3         ClientHello/ServerHello  1 RTT     | connection is reused
  |                                                     /  (SocketsHttpHandler)
  v
  HTTP request over the connection
  |    (HTTP/2: one of many multiplexed streams on one TCP connection)
  v
  load balancer (L4/L7, often terminates TLS)
  |
  v
  origin server --> response back down the same path --> your await resumes
```

Throughout, keep one idea in mind: **the network is a hostile, unreliable, shared medium that occasionally pretends to be a reliable function call.** Every abstraction in this chapter exists to manage that lie.

@@SRC: old Chapter 2: .NET Runtime & Internals@@
## Serialization: System.Text.Json vs Newtonsoft.Json

For years, **Newtonsoft.Json** (Json.NET) was the de facto standard. Since .NET Core 3.0, Microsoft ships **`System.Text.Json` (STJ)** in the box, designed for **high performance and low allocation** — it works directly over `Span<byte>`/UTF-8, avoiding the intermediate string conversions Newtonsoft performs, and is significantly faster with a smaller memory footprint.

Key differences to know:

- **Performance:** STJ is substantially faster and allocates less. It's the default in ASP.NET Core.
- **Defaults:** STJ is **stricter** by default — case-sensitive property matching (configurable), no comments or trailing commas unless enabled, and it doesn't handle some things Newtonsoft did permissively (like quoted numbers) without opting in.
- **Feature breadth:** Newtonsoft still has richer support for some advanced scenarios (`TypeNameHandling` for polymorphic type embedding, `[JsonConstructor]` flexibility, `DefaultValueHandling` nuances, `JObject`/`JToken` LINQ-to-JSON ergonomics), though STJ has closed most gaps and added polymorphism support (`[JsonDerivedType]`) and `JsonNode`.

```csharp
var options = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = false
};

string json = JsonSerializer.Serialize(order, options);
Order? back = JsonSerializer.Deserialize<Order>(json, options);
```

### Custom converters

When the default mapping doesn't fit (a custom date format, an enum-as-string, a domain primitive), you write a **`JsonConverter<T>`**:

```csharp
public sealed class DateOnlyConverter : JsonConverter<DateOnly>
{
    private const string Format = "yyyy-MM-dd";
    public override DateOnly Read(ref Utf8JsonReader reader, Type t, JsonSerializerOptions o)
        => DateOnly.ParseExact(reader.GetString()!, Format);
    public override void Write(Utf8JsonWriter writer, DateOnly value, JsonSerializerOptions o)
        => writer.WriteStringValue(value.ToString(Format));
}
```

### Source-generated serialization

Reflection-based serialization has two costs: **startup reflection overhead**, and **incompatibility with trimming/Native AOT** (the trimmer can't see reflection-driven access). STJ's **source generator** solves both. You declare a partial `JsonSerializerContext` with `[JsonSerializable]` attributes, and at *compile time* the generator emits fast, reflection-free, trim-safe serialization code.

```csharp
[JsonSerializable(typeof(Order))]
[JsonSerializable(typeof(Customer))]
public partial class AppJsonContext : JsonSerializerContext { }

// Usage — no runtime reflection, AOT-safe, faster startup:
string json = JsonSerializer.Serialize(order, AppJsonContext.Default.Order);
Order? o = JsonSerializer.Deserialize(json, AppJsonContext.Default.Order);
```

> **Best practice:** for new projects, default to `System.Text.Json`. Reach for source generation in AOT/trimmed apps and hot serialization paths. Keep Newtonsoft only where you depend on a feature STJ lacks or a library that requires it — and be aware of the strictness differences when migrating existing code.

@@SRC: old Chapter 3: ASP.NET Core & Web APIs@@
## The Middleware Pipeline & Request Lifecycle

**A request flows through a chain of components, each of which can do work before and after the next one runs.** This chain is the *middleware pipeline*. A component can also short-circuit: answer without calling the rest. On the way out, the response passes back through the same components in reverse order, so the outermost middleware wraps everything inside it.

A middleware component is a function that takes the current `HttpContext` and a delegate to "the rest of the pipeline" (`RequestDelegate`, usually called `next`).

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

The order in which you add middleware *is* the order requests flow through.

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

That registration order creates this nesting:

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

Each middleware sees only what the ones before it have set, so a wrong order fails in a predictable way:

- **`UseAuthorization` before `UseAuthentication`:** authorization reads `HttpContext.User`, which authentication has not filled in yet. The user looks anonymous, so every `[Authorize]` endpoint answers `401`, even to a valid token.
- **`UseAuthorization` before `UseRouting`:** no endpoint has been selected yet, so there is no `[Authorize]` metadata to evaluate. The endpoint middleware notices that authorization never saw the endpoint and throws *"Endpoint … contains authorization metadata, but a middleware was not found that supports authorization"*: a `500` on every protected endpoint, not a silent bypass. It runs the same check for CORS metadata.
- **`UseExceptionHandler` anywhere but first:** it works by wrapping `await next(context)` in a `try`, so it can't catch what middleware registered before it throws.

**When something "just doesn't apply," suspect ordering first.**

> **Pay attention.** **`WebApplication` orders the defaults for you, until you call one yourself.** With no explicit calls, `WebApplicationBuilder` wraps your middleware: `UseDeveloperExceptionPage` (Development only), `UseRouting`, then `UseAuthentication` and `UseAuthorization` when their services are registered, then everything in `Program.cs`, then the endpoints. That is why a minimal app with `AddAuthentication()` works with no `Use…` calls at all. Call `app.UseRouting()`, `UseAuthentication()` or `UseAuthorization()` yourself and the automatic one is skipped: your order is now the order, with the failures above. Either call none of them or call all three, in order.

@@SRC: old Chapter 3: ASP.NET Core & Web APIs@@
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

@@SRC: old Chapter 3: ASP.NET Core & Web APIs@@
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

@@SRC: old Chapter 3: ASP.NET Core & Web APIs@@
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

With `[ApiController]`, a failing model automatically produces a `400 Bad Request` with a validation `ProblemDetails` payload — you never write `if (!ModelState.IsValid)`. Minimal APIs validate nothing by default. Since .NET 10, `builder.Services.AddValidation()` runs the same DataAnnotations (and `IValidatableObject`) on query, header and body parameters and answers `400` with the error details; before that, validate manually or with a filter.

> **Pay attention.** **The automatic `400` is an action filter, so your action never runs.** `[ApiController]` adds a filter that checks `ModelState` after binding and before the action: a breakpoint in the action never hits, and a log line in it never prints. Binding failures land in the same place: an unparseable body or a wrong JSON type is a `400` from that filter, not an exception. To change the response shape, configure `ApiBehaviorOptions.InvalidModelStateResponseFactory`, rather than adding `if (!ModelState.IsValid)` checks that can never be reached.

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

@@SRC: old Chapter 3: ASP.NET Core & Web APIs@@
## CancellationToken Propagation

Once the client disconnects, times out or navigates away, work done on its behalf is wasted. `HttpContext.RequestAborted` is a `CancellationToken` that trips when the connection drops, and both Minimal APIs and MVC bind it to any `CancellationToken` parameter of an endpoint or action. It helps only if you *pass it through*: EF Core queries, `HttpClient` calls and stream reads all accept one.

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

When the client disconnects mid-query, EF Core cancels the database command and the connection returns to the pool. Without the token, the query runs to completion for a caller that will never read the response.

This matters most under load. Clients time out, abandon their requests and *retry*; if the server ignores cancellation, each abandoned query keeps running while its retry starts a duplicate. Load doubles exactly when the system is already struggling, and a slowdown snowballs into an outage. Propagating the token lets abandoned work stop.

Cancellation is cooperative, so the token stops only the calls it reaches: [CancellationToken: Cooperative Cancellation](#cancellationtoken-cooperative-cancellation) in Chapter 4 has the mechanism, and the analyzer that finds a token left behind.

> **Gotcha:** Not everything should be cancellable. If you've charged a payment and are about to write the outbox record, cancelling *mid-write* because the client hung up is far worse than finishing wasted work: you'd take the money and lose the event. Past such a point of no return, pass `CancellationToken.None` (or a token decoupled from the request). The skill is knowing which operations are safe to abandon and which have already committed you.

@@SRC: old Chapter 3: ASP.NET Core & Web APIs@@
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

@@SRC: old Chapter 3: ASP.NET Core & Web APIs@@
## IHttpClientFactory & Resilience with Polly

A `new HttpClient()` per call **exhausts sockets**: each opens and closes its own connections, and a closed connection keeps its port for a while ([Chapter 20](#keep-alive-connection-pooling-and-socket-exhaustion) has the TCP mechanism). One static instance avoids that but, by default, never retires a busy connection, so it **misses DNS changes**.

`HttpClient` is a cheap wrapper; the connection pool lives in the `HttpMessageHandler` underneath. `IHttpClientFactory` fixes both problems: it hands out a new `HttpClient` each time over a shared handler, so connections are reused (disposing a factory client is harmless, since it doesn't own the handler), and it retires each handler after two minutes (`SetHandlerLifetime`), so new connections re-resolve DNS and a failed-over dependency doesn't leave you talking to a dead IP.

```
CatalogClient ──> HttpClient          (new each time — cheap wrapper)
                      │
                      ▼
              HttpMessageHandler      (pooled & shared — owns the sockets;
                                       recycled every ~2 min → fresh DNS)
```

**Named clients** are configured by string key. **Typed clients** wrap an `HttpClient` in a strongly typed service, which is cleaner and my default:

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

> **Pitfall — typed clients are transient.** A singleton that takes a typed client captures one `HttpClient`, and the handler behind it, forever: the stale-DNS problem is back. Keep the consumer scoped or transient, inject `IHttpClientFactory` and create clients per use, or (.NET 8+) set `PooledConnectionLifetime` through `.UseSocketsHttpHandler(...)` so the connections rotate instead of the handler.

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

@@SRC: old Chapter 3: ASP.NET Core & Web APIs@@
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

@@SRC: old Chapter 3: ASP.NET Core & Web APIs@@
## REST, Status Codes, Versioning & OpenAPI

**REST** is a set of constraints, not a law, but a few principles pay dividends: model your API around **resources** (nouns) not actions; use HTTP **verbs** for intent (GET read, POST create, PUT replace, PATCH partial update, DELETE remove); make GET/PUT/DELETE **idempotent**; and lean on the **status code** to communicate outcome.

Use the right codes: `200 OK`, `201 Created` (with a `Location` header), `204 No Content` for a successful DELETE, `400` for malformed input, `401` unauthenticated, `403` authenticated-but-forbidden, `404` not found, `409` conflict, `422` semantic validation failure, `429` rate limited, `500` for your bugs. ASP.NET Core's automatic validation answers `400`, not `422`; if you adopt `422`, change it everywhere, so clients see one convention. Returning `200` with an error body inside is a common anti-pattern: retry policies, caches and error-rate dashboards read the status code, never the body, so all of them count the failure as a success.

### Versioning and OpenAPI

**API versioning** protects existing clients when you evolve. It is also a decision that is expensive to reverse and easy to get subtly wrong, so it gets the next section to itself — including the more useful question of how to avoid needing a new version at all.

**OpenAPI/Swagger** documents your API in a machine-readable contract. .NET now ships built-in OpenAPI document generation (`AddOpenApi` / `MapOpenApi`); Swagger UI or Scalar renders it for humans. Rich metadata (`WithName`, `Produces`, XML comments, `TypedResults`) makes the generated spec — and any client SDKs generated from it — accurate.

@@SRC: old Chapter 3: ASP.NET Core & Web APIs@@
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

@@SRC: old Chapter 3: ASP.NET Core & Web APIs@@
## Summary

The through-line of this chapter is that ASP.NET Core is a **pipeline of composable components**, and nearly every feature — auth, CORS, rate limiting, error handling — is just middleware or a filter slotted into that pipeline in the right order. Master the request lifecycle and the rest becomes a matter of choosing the right tool: Minimal APIs or Controllers, JWT or cookies, policies over roles, REST at the edge and gRPC within, resilience on every outbound call, cancellation tokens propagated through every awaited I/O, a trace on every request, and consistent ProblemDetails when things go wrong. Those are the instincts that separate a senior engineer from someone who merely returns JSON.

@@SRC: old Chapter 20: Networking & Web Fundamentals@@
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

@@SRC: old Chapter 20: Networking & Web Fundamentals@@
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

@@SRC: old Chapter 20: Networking & Web Fundamentals@@
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

@@SRC: old Chapter 20: Networking & Web Fundamentals@@
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

**Statelessness** is the crucial property: each request carries everything the server needs, and cookies, tokens and sessions *simulate* state on top. It is what makes horizontal scaling possible — any server can handle any request, as long as session data lives in a shared store, not in-process memory.

### HTTP Methods and Idempotency

Caches, proxies and retry policies act on two properties of the method (RFC 9110):

- **Safe** — the client asks for no state change: `GET`, `HEAD`, `OPTIONS`, `TRACE`. Safe responses can be cached and prefetched.
- **Idempotent** — N identical requests have the same effect on the server as one: every safe method, plus `PUT` and `DELETE`. It is about the effect, not the response: a second `DELETE` may answer `404` and is still idempotent.
- **Neither** — `POST` ("process this"), and `PATCH` unless you design it to be.

> **Pay attention.** **Idempotency decides who may retry without asking.** After a timeout the client can't know whether the request ran, so HTTP lets clients and proxies repeat idempotent requests automatically and tells them not to repeat the others. Retrying a `PUT` is harmless; retrying a `POST` may charge a card twice. Make every retriable operation idempotent, and give a `POST` that must not run twice an idempotency key ([Chapter 3: Idempotency Keys: Making POST Retry-Safe](#idempotency-keys-making-post-retry-safe)).

@@SRC: old Chapter 20: Networking & Web Fundamentals@@
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

@@SRC: old Chapter 20: Networking & Web Fundamentals@@
## Status Codes and Headers That Matter

Status codes group into five families. Clients, proxies and dashboards act on the code without reading the body:

- **1xx** Informational (rare; `101 Switching Protocols` for WebSocket upgrade).
- **2xx** Success — `200 OK`, `201 Created` (with a `Location` header), `204 No Content`.
- **3xx** Redirection — `301` permanent, `302`/`307` temporary, `304 Not Modified` (caching).
- **4xx** Client error — `400` bad request, `401` unauthenticated, `403` authenticated-but-forbidden, `404` not found, `409` conflict, `422` unprocessable, `429` too many requests.
- **5xx** Server error — `500` unhandled, `502` bad gateway (proxy got garbage upstream), `503` unavailable (overloaded/deploying), `504` gateway timeout.

> **Best practice:** The `401` vs `403` distinction trips people up. `401` means "I don't know who you are — authenticate", and it must carry a `WWW-Authenticate` header naming how; clients react by refreshing a token or prompting. `403` means "I know who you are, and you may not do this": re-authenticating won't help, so clients shouldn't try.

Headers worth knowing cold: `Content-Type` and `Accept` (content negotiation), `Authorization`, `Cache-Control` and `ETag` (caching, below), `Content-Encoding` (gzip/brotli compression), `Retry-After` (paired with `429`/`503`), and `X-Forwarded-For`/`X-Forwarded-Proto` (the client's real IP/scheme, injected by proxies — trust these only from proxies you control).

@@SRC: old Chapter 20: Networking & Web Fundamentals@@
## Keep-Alive, Connection Pooling, and Socket Exhaustion

Handshakes cost round-trips (TCP, then TLS), so HTTP clients keep connections alive and pool them for requests to share. Skip the pool and you get one of the most infamous bugs in .NET, **socket exhaustion**:

```csharp
// DO NOT DO THIS in a loop / per request
using (var client = new HttpClient())
{
    return await client.GetStringAsync(url);
}
```

Each `new HttpClient()` gets its own handler and connection pool, so every call opens a connection and `Dispose` closes it. The side that closes first holds the connection in `TIME_WAIT`, so that stray packets can't reach a new one, and its local port with it: 60 s on Linux (a kernel constant), 4 minutes by default on Windows. Open connections faster than ports come back and the ephemeral range runs dry: `HttpRequestException: Cannot assign requested address` on Linux (`SocketError.AddressNotAvailable`), typically *Only one usage of each socket address…* on Windows. The correct-looking `using` is the cause.

> **Pay attention.** **Why it passes every test and fails at the peak.**
>
> `TIME_WAIT` turns a per-call habit into a ceiling on *new connections per second*:
>
> - **Linux:** 28,232 ephemeral ports (32768–60999) ÷ 60 s ≈ 470 per second to each destination.
> - **Windows:** 16,384 (49152–65535) ÷ 240 s ≈ 68 per second.
> - **Azure App Service:** 128 preallocated SNAT ports per instance and destination, reclaimed four minutes after close: above about one new connection every two seconds, the instance waits for Azure to allocate more ([Case 3](#case-3-intermittent-timeouts-under-load-with-every-dashboard-green) in Chapter 51).
>
> Tests and dev boxes never reach these rates; a traffic peak does. Only a load test with real outbound calls finds it, and only connection reuse fixes it.

**The fix is `IHttpClientFactory`**, which pools the handlers that own the connections behind cheap `HttpClient` façades ([Chapter 3](#ihttpclientfactory-resilience-with-polly) has the design and typed clients). A named client:

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

`HttpClient` resolves DNS only when it opens a connection, and closes a pooled one only after a minute idle, so it never retires a busy connection. The factory's handler rotation (every two minutes by default) is what refreshes DNS, the [gotcha](#dns-resolution) above. One long-lived `HttpClient`, also valid, needs `PooledConnectionLifetime` instead:

```csharp
var handler = new SocketsHttpHandler
{
    PooledConnectionLifetime = TimeSpan.FromMinutes(2),
    MaxConnectionsPerServer = 20
};
var client = new HttpClient(handler);
```

> **Best practice:** Never `new` up an `HttpClient` per request. Use `IHttpClientFactory` (typed clients, plus resilience via `Microsoft.Extensions.Http.Resilience`), or one long-lived instance with `PooledConnectionLifetime` set.

@@SRC: old Chapter 20: Networking & Web Fundamentals@@
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

@@SRC: old Chapter 34: Interview Questions & How to Answer Them@@
## Interview Questions

*Revise: Ch. 3 — ASP.NET Core & Web APIs · Ch. 20 — Networking & Web Fundamentals*

**Explain the middleware pipeline.**
Middleware components form a chain; each gets the `HttpContext`, can act on the request, call `next()` to pass control down, and act on the response on the way back out — like nested layers. Order matters: exception handling first, then routing, auth (authentication before authorization), then endpoints. A component can short-circuit by not calling `next()`.

**DI lifetimes — the three, and the trap?**
**Singleton** (one instance for the app), **Scoped** (one per request), **Transient** (a new one each time). The trap is the **captive dependency**: injecting a Scoped (or Transient) service into a Singleton captures it for the app's lifetime, so a per-request service like `DbContext` leaks across requests and breaks. Never inject shorter-lived into longer-lived.

**Red flag:** "Make everything singleton, it's faster" — a captured `DbContext` then leaks across requests; that's a correctness bug, not an optimization.

**How does model binding work?**
ASP.NET Core maps incoming request data — route values, query string, form fields, JSON body, headers — onto action parameters and model properties by name, then runs validation attributes. You steer the source with `[FromBody]`, `[FromQuery]`, `[FromRoute]`, etc. Binding failures populate `ModelState`, which you check before acting.

**What are filters, and when over middleware?**
Filters run within the MVC action pipeline (authorization, resource, action, exception, result filters) and have access to MVC context like the action and model state. Use a filter for cross-cutting concerns that need MVC context — validation, action-level auth, result shaping. Use middleware for concerns that apply to *all* requests regardless of MVC — logging, compression, global exception handling.

**Minimal APIs vs controllers?**
Minimal APIs are lightweight endpoint definitions with less ceremony — great for small services and microservices. Controllers give more structure: attribute conventions, filters, model binding features, and familiar organization for large apps. Both share the same underlying routing and DI; pick by team size and app complexity, not performance.

**JWT vs cookie auth?**
Cookies are stateful-ish, browser-managed, sent automatically, and easy to revoke server-side — good for classic web apps (guard against CSRF). JWTs are self-contained bearer tokens carried in the `Authorization` header — stateless and ideal for APIs and cross-service auth, but hard to revoke before expiry, so keep them short-lived and pair with refresh tokens.

**Red flag:** "JWTs are secure because the payload is encrypted" — it's only Base64-encoded and *signed*; anyone can read the claims.

**REST: which status codes and idempotency?**
200 OK, 201 Created (with `Location`), 204 No Content, 400 Bad Request, 401 Unauthorized, 403 Forbidden, 404 Not Found, 409 Conflict, 422 Unprocessable, 500 Server Error. `GET`, `PUT`, `DELETE` are idempotent (repeating them yields the same state); `POST` is not. Idempotency matters for retries — clients retry on network failure, so unsafe non-idempotent operations need an idempotency key.

**What is CORS and why does it block things?**
CORS is a browser security mechanism: a page on origin A calling an API on origin B is blocked unless the API returns headers explicitly allowing origin A. It's enforced by the browser, not the server — so it protects users, and it's why your JS gets a CORS error while `curl` works fine. Configure allowed origins/methods/headers server-side; avoid `AllowAnyOrigin` with credentials.

**Red flag:** "CORS is server-side security that stops attackers calling the API" — it's a browser protection for users; any non-browser client bypasses it entirely.

---

@@SRC: practice from old module page Part 1 · Module 8: Web API Basics@@

## Prove it

Two programs. Predict each output before you run it.

**1. A new `HttpClient` per request leaves a socket behind every time.**

`verify/path/HttpClientPerRequest/Program.cs` · run it from `verify/path` with `dotnet run --project HttpClientPerRequest`:

```csharp
using System.Net.NetworkInformation;

// Prove it: a new HttpClient per request opens (and closes) one TCP connection per request, and
// every closed connection then sits in TIME_WAIT. A shared client reuses its pooled connections.
int port = Random.Shared.Next(20_000, 30_000);     // a fresh port, so earlier runs don't count
var builder = WebApplication.CreateSlimBuilder();
builder.Logging.ClearProviders();
builder.WebHost.UseUrls($"http://127.0.0.1:{port}");
var server = builder.Build();
server.MapGet("/", () => "ok");
await server.StartAsync();

var shared = new HttpClient();
await Measure("one shared HttpClient", () => shared.GetStringAsync($"http://127.0.0.1:{port}/"));
await Measure("new HttpClient per request", async () =>
{
    using var perRequest = new HttpClient();
    return await perRequest.GetStringAsync($"http://127.0.0.1:{port}/");
});

async Task Measure(string label, Func<Task<string>> call)
{
    int before = SocketsInTimeWait();
    for (int i = 0; i < 500; i++) await call();
    Console.WriteLine($"{label,-27} 500 requests, new sockets in TIME_WAIT: {SocketsInTimeWait() - before}");
}

int SocketsInTimeWait() => IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpConnections()
    .Count(c => c.State == TcpState.TimeWait && c.RemoteEndPoint.Port == port);
```

```text
one shared HttpClient       500 requests, new sockets in TIME_WAIT: 0
new HttpClient per request  500 requests, new sockets in TIME_WAIT: 500
```

What to notice:

- **One shared client: zero new sockets.** It reused one pooled connection for all 500 requests.
- **A client per request: 500 sockets in `TIME_WAIT`.** Each client opened a connection and closed it on `Dispose`. The side that closes keeps the socket, and its local port, in `TIME_WAIT`: 60 s on Linux.
- **At production rates the ports run out.** Ports come back only as fast as `TIME_WAIT` expires, which caps new connections per second to one destination; on App Service the cap is far lower, 128 SNAT ports per instance and destination, each reclaimed four minutes after its connection closes. A test suite never reaches either rate; a traffic peak does.

**2. A singleton that takes a scoped service keeps one instance of it.** The program registers a scoped `AppDb` and a singleton `PriceCache` that takes one, resolves both in two request scopes, then repeats the resolve with scope validation on. Predict the three lines before you run it.

`verify/path/CaptiveDependency/Program.cs` · run it from `verify/path` with `dotnet run --project CaptiveDependency`:

```csharp
// Prove it: a singleton that takes a scoped service keeps ONE instance of it for every scope; ValidateScopes refuses it.
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection()
    .AddScoped<AppDb>()                    // one per request, like a DbContext
    .AddSingleton<PriceCache>();           // one per process, and it asks for an AppDb

using (var root = services.BuildServiceProvider())     // ValidateScopes = false: the default outside Development
{
    for (int request = 1; request <= 2; request++)
    {
        using var scope = root.CreateScope();          // ASP.NET Core opens one scope per request
        AppDb own = scope.ServiceProvider.GetRequiredService<AppDb>();
        AppDb captured = scope.ServiceProvider.GetRequiredService<PriceCache>().Db;
        Console.WriteLine($"request {request}: its own AppDb #{own.Id}, the singleton's AppDb #{captured.Id}");
    }
}

using (var root = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true }))
using (var scope = root.CreateScope())
{
    try { scope.ServiceProvider.GetRequiredService<PriceCache>(); }
    catch (InvalidOperationException e) { Console.WriteLine($"ValidateScopes = true: {e.GetType().Name}: {e.Message}"); }
}

sealed class AppDb { private static int s_created; public int Id { get; } = ++s_created; }
sealed class PriceCache(AppDb db) { public AppDb Db { get; } = db; }
```

```text
request 1: its own AppDb #1, the singleton's AppDb #2
request 2: its own AppDb #3, the singleton's AppDb #2
ValidateScopes = true: InvalidOperationException: Cannot consume scoped service 'AppDb' from singleton 'PriceCache'.
```

What to notice:

- **#2 in both requests.** Each scope got its own `AppDb` (#1, then #3), while the singleton holds #2 for both. With a real `DbContext`, that is one context for every request and every thread.
- **#2 belongs to no request.** It was created with the singleton, in the root scope (the runtime's source calls it a scoped service "promoted to singleton"), so neither request's scope disposed it.
- **The same registrations, one option apart.** With `ValidateScopes` on, resolving the singleton throws. `WebApplicationBuilder` turns it on, together with `ValidateOnBuild`, only in Development, where `builder.Build()` fails with this message wrapped in an `AggregateException` before the first request. In Production nothing checks, and the bug ships.

## Check at work

**Inspect.** List the singletons in one service: `AddSingleton` registrations, hosted services and convention-based middleware. Check every constructor for a scoped dependency: a `DbContext`, a repository, anything registered with `AddScoped` or `AddDbContext`. Then start the service once with `ASPNETCORE_ENVIRONMENT=Development`: with `ValidateOnBuild` on, a captive dependency fails `Build()` there instead of shipping. In `Program.cs`, check that the exception handler is registered first and authentication before authorization, and that every bound options class calls `ValidateOnStart()`.

**Measure.** Break the `http.server.request.duration` metric (built into ASP.NET Core since .NET 8) down by `http.route` and `http.response.status_code`. A `5xx` on an endpoint that only rejects bad input is a bug that should have been a `400`. An endpoint that never answers anything but `200` deserves a look for errors hidden in its bodies.
