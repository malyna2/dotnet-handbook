# Chapter 24: Serialization & Schema Evolution

_⏱️ Estimated read time: ~29 min ·     4534 words (study pace)_

Every non-trivial system eventually stops being a single process holding objects in memory. The moment your data crosses a boundary — a socket, a message broker, a file on disk, a cache, an HTTP response — those in-memory objects have to be flattened into a sequence of bytes and reconstructed on the other side. That flattening is *serialization*, and the reconstruction is *deserialization*. It sounds mechanical, almost beneath a senior engineer's attention. It is not. The decisions you make here quietly determine how fast your service is, how much you pay for network and storage, whether two teams can deploy independently, and whether a schema change ships smoothly on a Tuesday or triggers a 2 a.m. incident.

This chapter is about making those decisions deliberately. We'll survey the formats you'll actually encounter in .NET — JSON, XML, Protocol Buffers, MessagePack, Avro — and then spend most of our time on the hard part that formats alone don't solve: **evolving a schema over time without breaking the systems that already depend on it.**

## Why the Serialization Format Matters

When you pick a format, you're really trading off four properties, and you rarely get all four.

- **Interoperability.** Can a Python service, a browser, and a mainframe all read it? Text formats win here: anything can parse JSON. A proprietary binary layout that only your C# assembly understands is a liability the moment a second language shows up.
- **Size on the wire.** A field named `"customerAccountBalance"` repeated across a million records is a million copies of that string. Binary formats that reference fields by integer tags, not names, are dramatically smaller.
- **Encode/decode speed.** Parsing text means scanning characters, handling escapes, and allocating strings. Binary formats read fixed-width integers and lengths directly. On a hot path serving tens of thousands of requests per second, this difference is real CPU and real money.
- **Schema and evolution story.** Does the format have a first-class notion of "this is what the data looks like," and does it give you rules for changing that shape safely? This is where formats differ the most, and it's the property that matters most for long-lived systems.

A useful mental model: the format is the *encoding*; the schema is the *contract*. You can change your encoding (JSON to Protobuf) far more easily than you can change a contract that a dozen consumers depend on. Most of the pain in distributed systems comes from breaking contracts, not from choosing the wrong encoding.

> **Best practice:** Choose the format for the *boundary*, not for the whole system. A public REST API facing browsers wants JSON. An internal high-throughput service mesh wants Protobuf over gRPC. An event stream that many teams consume over years wants a schema-registry-backed format like Avro or Protobuf. One system can — and usually should — use different formats at different boundaries.

## Text vs Binary: The Fundamental Split

**Text formats** (JSON, XML, CSV) encode data as human-readable characters. Their killer feature is that you can read them with your eyes, `curl` them, paste them into a bug report, and parse them in any language without a schema definition. Their costs are size (field names and delimiters repeated everywhere; numbers stored as digit strings) and speed (character-by-character parsing, escape handling, string allocation).

**Binary formats** (Protobuf, MessagePack, Avro, and the built-in binary paths) encode data compactly using length prefixes, integer tags, and native numeric layouts. They're smaller and faster but opaque — you generally need the schema (or at least a decoder) to make sense of the bytes.

The rule of thumb: **text at the edges where humans and heterogeneous clients live; binary in the interior where machines talk to machines at volume.**

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

## Benchmark Intuition: The Size/Speed Landscape

Don't over-index on any single microbenchmark — your data shape, object sizes, and access patterns dominate — but the *ordering* is stable and worth internalizing:

- **Size:** Protobuf and Avro are typically the smallest (integer tags or no tags, packed encoding). MessagePack is close behind. JSON is substantially larger — often 2–5x the size of a binary encoding for the same data, more when field names are long. XML is the largest.
- **Speed:** MessagePack-CSharp and protobuf are the fastest to encode/decode on .NET, especially with source generation. STJ with its source generator is remarkably competitive for a text format and far ahead of Newtonsoft. XML `XmlSerializer` is the slowest of the mainstream options.
- **Human-readability & tooling:** JSON and XML win outright; binary formats need a decoder.

> **Best practice:** Measure with *your* payloads using BenchmarkDotNet before you optimize. But as a default: JSON (source-generated) for public/edge APIs, Protobuf for gRPC and cross-language internal services, MessagePack for internal .NET-to-.NET RPC and caching, Avro for Kafka data pipelines with a schema registry.

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

## Summary

Serialization is where your data model meets the outside world, and the format you choose trades off interoperability, size, speed, and evolvability. Use text (JSON via `System.Text.Json`, ideally source-generated) at edges where humans and diverse clients live; use binary (Protobuf for gRPC and cross-language, MessagePack for fast internal .NET, Avro for Kafka pipelines) in the machine-to-machine interior. But the format is only the encoding — the durable challenge is evolving the *contract* without breaking the systems already depending on it. Master the compatibility directions, keep field identities permanent, add optional fields with defaults, ignore what you don't understand, and let schema registries and upcasters enforce and absorb change. Do that, and your schema can grow for years without a single 2 a.m. page.

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
