# Chapter 22: API Evolution, Real-Time and Serialization

@@TODO: write this chapter's introduction (what it makes the reader able to do, how its sections connect), then remove every @@ line.@@

@@SRC: introduction of old Chapter 24: Serialization & Schema Evolution@@

Every non-trivial system eventually stops being a single process holding objects in memory. The moment your data crosses a boundary — a socket, a message broker, a file on disk, a cache, an HTTP response — those in-memory objects have to be flattened into a sequence of bytes and reconstructed on the other side. That flattening is *serialization*, and the reconstruction is *deserialization*. It sounds mechanical, almost beneath a senior engineer's attention. It is not. The decisions you make here quietly determine how fast your service is, how much you pay for network and storage, whether two teams can deploy independently, and whether a schema change ships smoothly on a Tuesday or triggers a 2 a.m. incident.

This chapter is about making those decisions deliberately. We'll survey the formats you'll actually encounter in .NET — JSON, XML, Protocol Buffers, MessagePack, Avro — and then spend most of our time on the hard part that formats alone don't solve: **evolving a schema over time without breaking the systems that already depend on it.**

@@SRC: old Chapter 3: ASP.NET Core & Web APIs@@
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

@@SRC: old Chapter 3: ASP.NET Core & Web APIs@@
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

@@SRC: old Chapter 3: ASP.NET Core & Web APIs@@
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

@@SRC: old Chapter 20: Networking & Web Fundamentals@@
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

@@SRC: old Chapter 24: Serialization & Schema Evolution@@
## Why the Serialization Format Matters

When you pick a format, you're really trading off four properties, and you rarely get all four.

- **Interoperability.** Can a Python service, a browser, and a mainframe all read it? Text formats win here: anything can parse JSON. A proprietary binary layout that only your C# assembly understands is a liability the moment a second language shows up.
- **Size on the wire.** A field named `"customerAccountBalance"` repeated across a million records is a million copies of that string. Binary formats that reference fields by integer tags, not names, are dramatically smaller.
- **Encode/decode speed.** Parsing text means scanning characters, handling escapes, and allocating strings. Binary formats read fixed-width integers and lengths directly. On a hot path serving tens of thousands of requests per second, this difference is real CPU and real money.
- **Schema and evolution story.** Does the format have a first-class notion of "this is what the data looks like," and does it give you rules for changing that shape safely? This is where formats differ the most, and it's the property that matters most for long-lived systems.

A useful mental model: the format is the *encoding*; the schema is the *contract*. You can change your encoding (JSON to Protobuf) far more easily than you can change a contract that a dozen consumers depend on. Most of the pain in distributed systems comes from breaking contracts, not from choosing the wrong encoding.

> **Best practice:** Choose the format for the *boundary*, not for the whole system. A public REST API facing browsers wants JSON. An internal high-throughput service mesh wants Protobuf over gRPC. An event stream that many teams consume over years wants a schema-registry-backed format like Avro or Protobuf. One system can — and usually should — use different formats at different boundaries.

@@SRC: old Chapter 24: Serialization & Schema Evolution@@
## Text vs Binary: The Fundamental Split

**Text formats** (JSON, XML, CSV) encode data as human-readable characters. Their killer feature is that you can read them with your eyes, `curl` them, paste them into a bug report, and parse them in any language without a schema definition. Their costs are size (field names and delimiters repeated everywhere; numbers stored as digit strings) and speed (character-by-character parsing, escape handling, string allocation).

**Binary formats** (Protobuf, MessagePack, Avro, and the built-in binary paths) encode data compactly using length prefixes, integer tags, and native numeric layouts. They're smaller and faster but opaque — you generally need the schema (or at least a decoder) to make sense of the bytes.

The rule of thumb: **text at the edges where humans and heterogeneous clients live; binary in the interior where machines talk to machines at volume.**

@@SRC: old Chapter 24: Serialization & Schema Evolution@@
## JSON in Modern .NET: `System.Text.Json`

For most .NET developers in 2026, JSON means `System.Text.Json` (STJ), the high-performance serializer that shipped with .NET Core 3.0 and has been the default since. It replaced `Newtonsoft.Json` (Json.NET) as the recommended library for new code. Newtonsoft is still excellent and more feature-rich in some corners, but STJ is faster, allocates less, and is built on `Span<T>` and `Utf8JsonReader`/`Utf8JsonWriter` primitives that work directly on UTF-8 bytes without an intermediate UTF-16 string.

Here's the everyday API:

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;

public record Order(int Id, string Customer, decimal Total, DateTimeOffset PlacedAt);

var order = new Order(42, "Acme", 199.99m, DateTimeOffset.UtcNow);

var options = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = false
};

string json = JsonSerializer.Serialize(order, options);
Order? round = JsonSerializer.Deserialize<Order>(json, options);
```

### Source Generators: JSON Without Reflection

By default STJ inspects your types at runtime with reflection to figure out how to read and write them. Reflection is flexible but has costs: a warm-up hit on first use, per-call overhead, and — critically — it doesn't survive **trimming** or **Native AOT**, because the trimmer can't prove which types you'll reflect over and may strip them.

The **source generator** solves this. You declare a partial `JsonSerializerContext`, annotate it with the types you serialize, and the compiler emits the serialization code at build time. No runtime reflection, faster startup, smaller allocations, and full AOT/trim compatibility.

```csharp
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(Order))]
internal partial class AppJsonContext : JsonSerializerContext { }

// Usage — note the generated metadata is passed in, so no reflection is needed:
string json = JsonSerializer.Serialize(order, AppJsonContext.Default.Order);
Order? round = JsonSerializer.Deserialize(json, AppJsonContext.Default.Order);
```

> **Best practice:** For any service on a hot path, and for *anything* targeting Native AOT or aggressive trimming, use the `System.Text.Json` source generator. It's a near-free performance and reliability win. In ASP.NET Core you can register the context via `services.ConfigureHttpJsonOptions(o => o.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonContext.Default))`.

A few STJ facts worth carrying in your head, because they bite people migrating from Newtonsoft:

- STJ is **case-sensitive by default** on property names (set `PropertyNameCaseInsensitive = true` to match Newtonsoft's behavior — but note it costs a little performance).
- It does **not** serialize fields or non-public members by default.
- `JsonStringEnumConverter` is needed to (de)serialize enums as strings; by default they're numbers.
- Cache and reuse your `JsonSerializerOptions` instance. Constructing a fresh one per call defeats internal caching and tanks throughput.

@@SRC: old Chapter 24: Serialization & Schema Evolution@@
## XML: Still Around, Still Sometimes Right

XML predates JSON as the interop lingua franca and hasn't vanished. You'll meet it in SOAP web services, legacy enterprise integrations, government and healthcare standards, and document formats (`.docx`, `.xlsx` are zipped XML). .NET's `System.Xml.Serialization.XmlSerializer` and `System.Runtime.Serialization.DataContractSerializer` handle it.

XML's genuine advantages over JSON are **namespaces** (avoiding element-name collisions when merging vocabularies), **attributes vs elements** (a modeling distinction JSON lacks), and **XSD schemas with mature tooling** for validation and code generation. Its costs are verbosity — closing tags double the structural overhead — and slower parsing.

```csharp
using System.Xml.Serialization;

var serializer = new XmlSerializer(typeof(Order));
using var writer = new StringWriter();
serializer.Serialize(writer, order);
string xml = writer.ToString();
```

For new internal APIs, reach for JSON or a binary format. Use XML when a standard or an existing partner demands it — that's a legitimate and common reason.

@@SRC: old Chapter 24: Serialization & Schema Evolution@@
## Protocol Buffers: Schema-First Binary

Protocol Buffers ("protobuf"), from Google, is the binary format most .NET developers meet through **gRPC**, where it's the default payload. Its defining characteristic is that it is **schema-first**: you write a `.proto` file describing your messages, and a code generator produces C# classes. The schema isn't optional documentation — it's the source of truth that both producer and consumer compile against.

```proto
syntax = "proto3";
option csharp_namespace = "Shop.Contracts";

message Order {
  int32 id = 1;
  string customer = 2;
  double total = 3;
  int64 placed_at_unix = 4;
}
```

The numbers after each field — `= 1`, `= 2` — are **field numbers** (tags), and they are the heart of protobuf's evolution story. On the wire, protobuf does *not* write field names. It writes, for each field, a small "tag" byte encoding the field number and its wire type, followed by the value. `customer = 2` becomes roughly "field 2, length-delimited, 4 bytes, A-c-m-e."

This has two enormous consequences:

1. **The field name is irrelevant to the bytes.** You can rename `customer` to `customerName` in the `.proto` and old and new binaries still interoperate perfectly, because they agree on the *number* 2, not the name.
2. **The field number is a permanent, load-bearing identity.** Change `customer` from `2` to `5`, and every existing consumer will look for field 2, find nothing, and silently see an empty customer. Reuse number 2 for a *different* field of a different type, and you get garbage or a decode error.

> **The single most important protobuf rule:** *Never change or reuse a field number once it's in production.* Field numbers are forever. If you delete a field, `reserve` its number (and ideally its name) so no one accidentally recycles it:
> ```proto
> message Order {
>   reserved 4;
>   reserved "placed_at_unix";
>   int32 id = 1;
>   string customer = 2;
>   double total = 3;
> }
> ```

In proto3, all fields are effectively optional and have **default values** (0, empty string, false). There's no way to distinguish "field absent" from "field set to zero" unless you mark it `optional` (which adds presence tracking) or wrap it. This default-value behavior is exactly what makes evolution work: a new consumer reading old data that lacks a field simply gets the default, and an old consumer reading new data ignores tags it doesn't recognize.

In .NET you'd add the `Grpc.Tools` package, drop the `.proto` into your project, and MSBuild generates the classes. The generated code is fast and allocation-light, and it round-trips through `Google.Protobuf`'s `IMessage` interface.

> **Worth knowing:** the long-running proto2/proto3 split is being retired by **Protobuf Editions** (Edition 2023 was the first, released in the second half of 2023). Instead of picking a syntax with fixed semantics, an edition lets you tune individual behaviours via feature flags while keeping the wire format unchanged — it's the forward path the two syntaxes are converging on, so a modern reader should recognize the term even if most existing `.proto` files still say `syntax = "proto3"`.

@@SRC: old Chapter 24: Serialization & Schema Evolution@@
## MessagePack: Binary JSON, No Schema File

MessagePack is best described as "JSON's data model, binary encoding." It has the same shape — maps, arrays, strings, numbers, booleans, null — but encoded compactly with length prefixes and type tags instead of text. Unlike protobuf, it doesn't require a separate schema file; in .NET the excellent **MessagePack-CSharp** library serializes your annotated C# types directly, much like a serializer rather than a code generator.

```csharp
using MessagePack;

[MessagePackObject]
public class Order
{
    [Key(0)] public int Id { get; set; }
    [Key(1)] public string Customer { get; set; } = "";
    [Key(2)] public decimal Total { get; set; }
    [Key(3)] public DateTimeOffset PlacedAt { get; set; }
}

byte[] bytes = MessagePackSerializer.Serialize(order);
Order back = MessagePackSerializer.Deserialize<Order>(bytes);
```

Those `[Key(0)]` integers play the same role as protobuf field numbers: they're the compact wire identity, and **the same "never reuse a key" discipline applies.** MessagePack-CSharp also supports string keys (`[Key("customer")]`), which are more self-describing but larger — a JSON-like trade-off within the format.

MessagePack-CSharp is famous for raw speed; it uses a source-generator/IL-emit path and is one of the fastest serializers available on .NET. It's a great choice for caching (compact Redis payloads), internal RPC (it's the default for SignalR's binary protocol and for the MagicOnion framework), and anywhere you want binary compactness without maintaining separate `.proto` files. The trade-off versus protobuf is that the schema lives in your C# attributes rather than a language-neutral IDL, so cross-language contracts are slightly less formal.

> **Faster still, for .NET-to-.NET:** **MemoryPack** (from neuecc, the author of MessagePack-CSharp) is a "zero-encoding" binary serializer that leans on modern C# and a pure source-generator path — no runtime IL emit — which makes it **Native AOT-friendly** and, on many payloads, several times faster than MessagePack. It's the sharper tool when both ends are .NET and you don't need cross-language interop; the format is .NET-specific by design.

@@SRC: old Chapter 24: Serialization & Schema Evolution@@
## Apache Avro: Built for Evolution

Avro comes from the Hadoop world and is the canonical choice in Kafka-based data platforms. Its distinguishing idea: **the schema travels with the data, and reading requires both a writer's schema and a reader's schema.**

When Avro serializes, it writes the raw values with essentially no per-field overhead — no field tags at all — because the schema defines the exact order and types. To *read* those bytes you need the **writer's schema** (what the data was written with) and your **reader's schema** (what your code expects). Avro's resolution rules then reconcile the two: fields present in the writer but not the reader are skipped; fields in the reader but not the writer are filled from **defaults**; renamed fields are matched via **aliases**.

This is a genuinely different and powerful model. Because reading is a negotiation between two schemas rather than a fixed decode, Avro can handle a wider range of evolution automatically — but only if you *have* both schemas. In a file, Avro embeds the writer schema in a header. In a stream like Kafka, embedding the full schema in every message would be wasteful, so instead each message carries a small **schema ID** that points into a **Schema Registry** (more on that shortly).

Avro schemas are themselves JSON:

```json
{
  "type": "record",
  "name": "Order",
  "namespace": "shop.contracts",
  "fields": [
    { "name": "id", "type": "int" },
    { "name": "customer", "type": "string" },
    { "name": "total", "type": "double" },
    { "name": "note", "type": ["null", "string"], "default": null }
  ]
}
```

That `note` field, typed as a union of `null` and `string` with `default: null`, is the textbook safe addition — old readers ignore it, new readers get `null` when it's absent.

@@SRC: old Chapter 24: Serialization & Schema Evolution@@
## Benchmark Intuition: The Size/Speed Landscape

Don't over-index on any single microbenchmark — your data shape, object sizes, and access patterns dominate — but the *ordering* is stable and worth internalizing:

- **Size:** Protobuf and Avro are typically the smallest (integer tags or no tags, packed encoding). MessagePack is close behind. JSON is substantially larger — often 2–5x the size of a binary encoding for the same data, more when field names are long. XML is the largest.
- **Speed:** MessagePack-CSharp and protobuf are the fastest to encode/decode on .NET, especially with source generation. STJ with its source generator is remarkably competitive for a text format and far ahead of Newtonsoft. XML `XmlSerializer` is the slowest of the mainstream options.
- **Human-readability & tooling:** JSON and XML win outright; binary formats need a decoder.

> **Best practice:** Measure with *your* payloads using BenchmarkDotNet before you optimize. But as a default: JSON (source-generated) for public/edge APIs, Protobuf for gRPC and cross-language internal services, MessagePack for internal .NET-to-.NET RPC and caching, Avro for Kafka data pipelines with a schema registry.

@@SRC: old Chapter 24: Serialization & Schema Evolution@@
## The Core Topic: Schema and Contract Evolution

Here's the situation that makes all of this hard. You deploy version 1 of a producer and version 1 of a consumer. They agree. Then you need to change the data. But you **cannot atomically upgrade every producer and consumer at once** — that's the whole point of a distributed system. During the rollout, and often for a long time after, old and new versions coexist. Old producers send data new consumers read; new producers send data old consumers read. Your job is to change the schema so that *every* combination keeps working.

### Compatibility Directions

We name compatibility by whose perspective we take and which side is newer:

- **Backward compatible:** New code can read data written by old code. (You can upgrade *consumers* first.) Concretely: don't require fields that old writers won't send — add fields with defaults, don't remove required fields.
- **Forward compatible:** Old code can read data written by new code. (You can upgrade *producers* first.) Concretely: old readers must tolerate fields they don't know about — so *ignoring unknown fields* is the key mechanism, and you must not add newly-*required* fields that old readers demand.
- **Full compatibility:** Both directions hold. This is what you want for independent, any-order deployment, and it's the setting many teams configure in their schema registry.

Think of it as a matrix over "which schema wrote it" × "which schema reads it." Backward covers new-reads-old; forward covers old-reads-new; full covers both.

### Safe Changes vs Breaking Changes

The rules are remarkably consistent across protobuf, Avro, and well-behaved JSON:

**Generally safe:**
- **Adding an optional field with a default.** Old readers ignore it; new readers fall back to the default when it's missing. This is the workhorse of schema evolution.
- **Removing an optional field** (as long as no consumer *requires* it) — and reserving its identity so it's never reused.
- **Renaming a field** *when identity is by number/tag* (protobuf, MessagePack) — the name is cosmetic. In Avro use **aliases**. In JSON, renaming is breaking unless you keep the old name too.

**Breaking — do not do these to a live contract:**
- **Reusing or renumbering a field number/key.** Catastrophic and silent. Reserve deleted numbers.
- **Changing a field's type** (int to string, or narrowing int64 to int32). The bytes mean different things.
- **Adding a *required* field.** Old producers won't send it; you've broken backward compatibility.
- **Removing a field something still requires**, or changing its semantics while keeping the name (a subtler, nastier break — the schema check passes but behavior is wrong).

**Enums deserve special care.** A producer on a newer schema may emit an enum value the consumer has never heard of. If your consumer does an exhaustive `switch` with no default, it may throw. Design for the unknown:

```csharp
public OrderStatus MapStatus(string wireValue) => wireValue switch
{
    "pending"   => OrderStatus.Pending,
    "shipped"   => OrderStatus.Shipped,
    "delivered" => OrderStatus.Delivered,
    _           => OrderStatus.Unknown   // tolerate values added later
};
```

Protobuf leans into this: an unrecognized enum value in proto3 is preserved as its underlying integer rather than rejected, so it survives a round-trip through a consumer that doesn't understand it yet. Always model an `Unknown`/`Unspecified` zero value in your enums.

The rules compress into a matrix once you remember what each format uses as a field's identity: the *name* (JSON, Avro) or the *number/key* (Protobuf, MessagePack). Everything below follows from that.

| Change | JSON (STJ) | Protobuf | MessagePack (int keys) | Avro |
|---|---|---|---|---|
| Add optional field with default | ✅ | ✅ | ✅ (append) | ✅ (declare default) |
| Remove a field | ⚠️ if-unused | ⚠️ reserve number | ⚠️ reserve key | ⚠️ if-unused |
| Rename a field | ⚠️ keep-old-name | ✅ (number is identity) | ✅ (key is identity) | ⚠️ alias |
| Change a field's type | ❌ | ❌ | ❌ | ❌ |
| Reuse a removed field's tag/name | ❌ | ❌ silent garbage | ❌ silent garbage | ❌ |
| Make an optional field required | ❌ | ❌* | ❌ | ❌ |

\* proto3 can't even express `required` on the wire — the break surfaces in your validation layer instead, which makes it sneakier, not safer.

### Handling Unknown Fields

Forward compatibility hinges on what a reader does with data it wasn't told about.

- **Protobuf** preserves unknown fields by default — a proxy that deserializes and re-serializes a message won't lose fields it doesn't understand. This makes it excellent for middle-tier services.
- **`System.Text.Json`** ignores unknown properties by default when deserializing (it won't throw). If you need to *preserve* them for a round-trip, capture them with `[JsonExtensionData]`:

```csharp
public class Order
{
    public int Id { get; set; }
    public string Customer { get; set; } = "";

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}
```

Everything the schema didn't name lands in `Extra` and is written back out on serialize. This is the JSON expression of the **tolerant reader** pattern.

### The Tolerant Reader Pattern

Coined in the web-services era, the tolerant reader principle is simply: **be liberal in what you accept.** Read only the fields you actually need, ignore everything else, don't fail on extra data, and don't assume field ordering. A consumer written this way survives a huge class of producer changes without any code change at all. Its opposite — a strict reader that validates the whole payload against an exact schema and rejects anything unexpected — turns every additive producer change into a coordinated breaking release. Prefer tolerant readers for anything you don't fully control.

@@SRC: old Chapter 24: Serialization & Schema Evolution@@
## Versioning Strategies for REST APIs

REST is a contract too, and it evolves. The common strategies:

- **URL path versioning:** `/api/v1/orders`, `/api/v2/orders`. Explicit, cache-friendly, trivially visible in logs and browsers. Downside: it's arguably not "the same resource" at two URLs, and it can proliferate. It's by far the most common in practice because it's the most obvious.
- **Query string:** `/api/orders?api-version=2`. Easy to default, but easy to overlook and clutters URLs.
- **Custom header:** `X-Api-Version: 2` (or a vendor header). Keeps URLs clean, but versions become invisible to a casual `curl` and harder to route on.
- **Media-type / content negotiation:** `Accept: application/vnd.myshop.order.v2+json`. The most RESTfully "correct" — you're negotiating a representation — but the least approachable and the hardest for tooling.

`Asp.Versioning` (the successor to `Microsoft.AspNetCore.Mvc.Versioning`) supports all of these:

```csharp
builder.Services.AddApiVersioning(o =>
{
    o.DefaultApiVersion = new ApiVersion(1, 0);
    o.AssumeDefaultVersionWhenUnspecified = true;
    o.ReportApiVersions = true; // emits api-supported-versions header
    o.ApiVersionReader = ApiVersionReader.Combine(
        new UrlSegmentApiVersionReader(),
        new HeaderApiVersionReader("X-Api-Version"));
});
```

> **Best practice:** Prefer *additive, non-breaking* changes so you rarely need a new version at all — a tolerant JSON contract with source-generated STJ and optional fields absorbs most changes. When you must break, pick one versioning scheme and apply it consistently. URL-path versioning is the pragmatic default; reserve a new major version for genuinely breaking changes and keep old versions alive long enough for clients to migrate.

@@SRC: old Chapter 24: Serialization & Schema Evolution@@
## Versioning Events and Messages

Asynchronous messages are harder than REST because you often can't see or coordinate with your consumers, the messages may be persisted for years (especially in event sourcing), and there's no synchronous request to negotiate a version on. Several patterns, usually combined:

**Version in the envelope.** Wrap every message in a small envelope carrying metadata — a type name and a schema version — around an opaque payload. Consumers dispatch on those:

```csharp
public record EventEnvelope(
    string Type,        // "OrderPlaced"
    int SchemaVersion,  // 2
    DateTimeOffset OccurredAt,
    JsonElement Payload);
```

**Schema registry.** In Kafka ecosystems, the **Confluent Schema Registry** stores every schema version centrally. Each message carries only a compact schema ID (a few bytes) instead of the full schema; consumers fetch and cache the schema by ID. The registry's real power, though, is **governance**: you configure a compatibility mode (`BACKWARD`, `FORWARD`, `FULL`, or their transitive variants) per subject, and when a producer tries to register a new schema, **the registry rejects it at deploy time if it would break the configured compatibility.** This is the crucial shift: instead of discovering a breaking change in production when a consumer chokes, you find out when CI tries to register the schema. It moves contract enforcement left, and it decouples producer and consumer teams — they agree on the compatibility policy once, then evolve independently within its rules.

```csharp
// Producer side (Confluent.Kafka + Confluent.SchemaRegistry.Serdes)
using var registry = new CachedSchemaRegistryClient(
    new SchemaRegistryConfig { Url = "http://schema-registry:8081" });

using var producer = new ProducerBuilder<string, Order>(producerConfig)
    .SetValueSerializer(new AvroSerializer<Order>(registry))
    .Build();
// Registering an incompatible schema fails here, not in the consumer at 2 a.m.
```

**Consumer/producer coupling.** The deep reason schema registries and compatibility rules matter is that a message contract is a coupling point between teams. Without enforcement, that coupling is *implicit and undocumented* — Team A changes a field and Team B breaks, discovering the dependency only via an incident. A registry makes the coupling *explicit and enforced*: the rules are the interface, and the tooling won't let either side violate them.

### Upcasting in Event Sourcing

Event sourcing raises the stakes: events are your **source of truth**, stored forever, and replayed to rebuild state. You will still be reading `OrderPlacedV1` events years after the code that wrote them is gone. You can't rewrite history casually, and you don't want every aggregate cluttered with conditionals for ancient formats.

The standard technique is **upcasting**: on read, transform old event versions into the current shape *before* they reach your domain logic, so the domain only ever sees the latest version.

```csharp
public interface IUpcaster
{
    bool CanUpcast(string type, int version);
    (string Type, int Version, JsonElement Payload) Upcast(
        string type, int version, JsonElement payload);
}

// V1 had no Currency field; V2 adds it, defaulting legacy orders to USD.
public sealed class OrderPlacedV1ToV2 : IUpcaster
{
    public bool CanUpcast(string type, int version)
        => type == "OrderPlaced" && version == 1;

    public (string, int, JsonElement) Upcast(string type, int version, JsonElement payload)
    {
        var dict = payload.Deserialize<Dictionary<string, JsonElement>>()!;
        dict["currency"] = JsonSerializer.SerializeToElement("USD");
        return ("OrderPlaced", 2, JsonSerializer.SerializeToElement(dict));
    }
}
```

Chain upcasters (V1→V2→V3) so each step is small and independently testable, and your live handlers only ever handle the newest version. The old events on disk never change; the transformation happens in the read pipeline.

@@SRC: old Chapter 24: Serialization & Schema Evolution@@
## A Concrete Example: Evolving an Order Event Safely

Let's evolve a protobuf `OrderPlaced` event through three realistic changes and see the discipline in action.

**Version 1** — the original:

```proto
message OrderPlaced {
  int32 order_id = 1;
  string customer = 2;
  double total = 3;
}
```

**Version 2** — we need a currency, and we're deprecating `total` in favor of a `Money` that pairs amount with currency. The *wrong* move is to change field 3's type or reuse it. The right move is **additive**:

```proto
message OrderPlaced {
  int32 order_id = 1;
  string customer = 2;
  double total = 3;              // kept for old consumers; still populated
  string currency = 4;           // new, optional, defaults to "" -> treat as USD
  Money money = 5;               // new richer representation
}

message Money { double amount = 1; string currency_code = 2; }
```

Old consumers keep reading `total` (field 3) and never notice fields 4 and 5. New consumers prefer `money` (field 5) and fall back to `total` + `currency` when `money` is absent (an old producer). Both `currency` and `money` are safe because unset fields decode to defaults, and old readers ignore unknown tags. This is simultaneously backward *and* forward compatible — full compatibility — so producers and consumers can deploy in any order.

**Version 3** — `total` and `currency` are now redundant; every producer has migrated to `money`. Once you've *confirmed* no live consumer reads fields 3 or 4 (this is an operational check, not just a code check — grep won't tell you about that one lagging service), retire them and **reserve their numbers forever**:

```proto
message OrderPlaced {
  reserved 3, 4;
  reserved "total", "currency";
  int32 order_id = 1;
  string customer = 2;
  Money money = 5;
}
```

Field 5 stayed 5. We never renumbered, never reused, never changed a type. Each step was independently deployable. That is what safe schema evolution looks like in practice: a sequence of additive changes, a deliberate deprecation window, and permanent reservations — never a big-bang rename.

> **Contract testing** — verifying that a specific consumer and producer actually agree on the contract, using tools like **Pact** — is a complementary safety net to everything in this chapter. Schema compatibility rules prove your schemas *can* evolve safely; contract tests prove two concrete services *do* agree right now. We cover Pact and consumer-driven contract testing in depth in the Advanced Testing chapter.

@@SRC: old Chapter 24: Serialization & Schema Evolution@@
## Summary

Serialization is where your data model meets the outside world, and the format you choose trades off interoperability, size, speed, and evolvability. Use text (JSON via `System.Text.Json`, ideally source-generated) at edges where humans and diverse clients live; use binary (Protobuf for gRPC and cross-language, MessagePack for fast internal .NET, Avro for Kafka pipelines) in the machine-to-machine interior. But the format is only the encoding — the durable challenge is evolving the *contract* without breaking the systems already depending on it. Master the compatibility directions, keep field identities permanent, add optional fields with defaults, ignore what you don't understand, and let schema registries and upcasters enforce and absorb change. Do that, and your schema can grow for years without a single 2 a.m. page.

@@SRC: old Chapter 24: Serialization & Schema Evolution@@
## Sources & Further Reading

- **Microsoft Learn — System.Text.Json:** "How to serialize and deserialize JSON in .NET," "How to use source generation in System.Text.Json," and the migration guidance from Newtonsoft.Json. https://learn.microsoft.com/dotnet/standard/serialization/system-text-json/
- **Microsoft Learn — gRPC and Protobuf in .NET:** "Create Protobuf messages for .NET apps" and "Versioning gRPC services." https://learn.microsoft.com/aspnet/core/grpc/
- **Microsoft Learn — ASP.NET Core API versioning** (`Asp.Versioning`). https://learn.microsoft.com/aspnet/core/
- **Protocol Buffers documentation** — Language Guide (proto3), including field numbers, reserved fields, default values, and the "Updating a Message Type" rules. https://protobuf.dev/
- **MessagePack specification** (msgpack.org) and the **MessagePack-CSharp** library README on GitHub (neuecc/MessagePack-CSharp).
- **Apache Avro specification** — schema resolution, aliases, and defaults. https://avro.apache.org/docs/
- **Confluent Schema Registry documentation** — schema compatibility types (BACKWARD, FORWARD, FULL, and transitive variants) and .NET Serdes usage. https://docs.confluent.io/platform/current/schema-registry/
- **Martin Fowler**, "Tolerant Reader" and "Schemaless Data Structures." https://martinfowler.com/
- **Pact** — consumer-driven contract testing (covered in the Advanced Testing chapter). https://docs.pact.io/
