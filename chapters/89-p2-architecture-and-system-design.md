# Part 2 · Module 4: Architecture, API Evolution and System Design

> **What this module makes you able to do.** Choose where a system's boundaries go — layers, slices, modules, services, API versions — and how contracts change across them without breaking callers, then defend the choice in a design review and write it down so the next team knows why.

**Time:** reading ≈ 1 h; hands-on ≈ 3 h 50 min — the ADR 1 h, the design exercise 1 h 30 min, the questions 20, the decision 15, the check at work 45.

## Covers

- what layered and clean architecture, vertical slices, a modular monolith and microservices each cost, and when each pays;
- DDD's tactical building blocks, and why an aggregate is a transaction boundary;
- CQRS, and what MediatR does and doesn't give you;
- API evolution: what counts as breaking, the tolerant reader, expand and contract, versioning schemes and retirement;
- REST, gRPC or SignalR for a given caller;
- a system-design method: requirements, estimates, design, bottlenecks, trade-offs.

## The mechanism to explain without notes

**Architecture is the cost of change, decided in advance: every boundary makes change cheap on one side of it and expensive across it, so the senior decision is to put boundaries where the business changes independently, and to evolve the contracts that cross them without breaking the callers on the other side.**

Part 1's design basics module covered SOLID, dependency injection and the common patterns. Here they become system-level choices.

- **Each style draws the boundary somewhere else.**
  - *Layered and clean architecture* draw it around technology: the dependency rule points inward, so the domain can be tested without a database and infrastructure can be replaced. The cost is indirection and mapping between layers, paid on every feature.
  - *Vertical slices* draw it around features: everything one use case needs sits together, so a change touches one folder. The cost is duplication, and shared domain rules need a deliberate home.
  - *A modular monolith* draws it around business capabilities inside one deployable: each module owns its tables and exposes a narrow API. Calls stay in-process, a transaction is still available, and deployment stays single. The cost is discipline, enforced by tooling, because nothing physical stops a shortcut.
  - *Microservices* make the boundary physical: independent deployment and scaling, one team per service. The cost is everything in Module 3 — the network, eventual consistency, sagas, idempotency — and an operations bill per service. Conway's law decides whether it pays: services follow teams.
- **An aggregate is a consistency boundary.** It is the unit that one transaction changes, with its invariants checked inside it. Other aggregates are referenced by ID and updated through domain events, eventually. Bounded contexts are the same idea one level up, and they are the first candidates for module or service boundaries.
- **CQRS separates the model you write through from the model you read from.** It can be two models over one database, or a separate read store fed by events, which brings eventual consistency with it. MediatR is an in-process dispatcher with a pipeline: it decouples a controller from its handler and gives cross-cutting behaviour one place to live. It doesn't give you CQRS, a read store, scaling or consistency, and new major versions have been commercially licensed since 2025 (Chapter 5).
- **A contract breaks when a client that worked yesterday fails today**, whatever the schema says. Tightened validation, a new enum value for a strict generated client, a changed status code or error shape, a new default page size: all break someone. The safe path is the same expand and contract as for a database: add the new form, serve both, measure who still uses the old one, and remove it only when nobody does. A new version is for a change in meaning, not for a change in shape.
- **The protocol follows the caller.** REST for browsers, partners and anything cacheable; gRPC for internal service-to-service calls, with a protobuf contract, HTTP/2 and streaming; SignalR for pushing to browsers, which needs a backplane or a managed service once it scales out.

> **Pay attention.** **A service split doesn't remove coupling; it moves it onto the network.** Two services that share a database deploy together, because a schema change breaks both. A synchronous chain of three services, each available 99.9% of the time, is available about 99.7% of the time (0.999³), and as slow as the sum of its calls. That is a distributed monolith: the costs of microservices with the coupling of a monolith. Before splitting, check that the candidate owns its data, can answer its requests without calling its neighbours synchronously, and changes on a different schedule from them.

> **Pay attention.** **Whether a change breaks is decided by the client's reader, not by your schema.** A tolerant reader (ignore unknown fields, map unknown enum values to a default) survives an added field or enum value. A client generated from your OpenAPI document into closed enums and strict models doesn't. "Adding is safe" holds only for clients you know are tolerant; for the rest, a new enum value needs the same care as a removed field.

## Read (≈ 1 h)

1. [Chapter 6: Why Architecture Matters: Coupling and Cohesion](#why-architecture-matters-coupling-and-cohesion), [Layered / N-Tier Architecture](#layered-n-tier-architecture) and [Clean, Onion, and Hexagonal Architecture](#clean-onion-and-hexagonal-architecture): the dependency rule, and what a project structure enforces.
2. [Chapter 6: Domain-Driven Design](#domain-driven-design): aggregates as consistency boundaries, bounded contexts.
3. [Chapter 6: CQRS and Event Sourcing](#cqrs-and-event-sourcing), [Vertical Slice Architecture](#vertical-slice-architecture), [Monolith vs Microservices vs Modular Monolith](#monolith-vs-microservices-vs-modular-monolith) and [API Gateway and Backend for Frontend](#api-gateway-and-backend-for-frontend).
4. Chapter 5's application patterns: [Repository & Unit of Work](#repository-unit-of-work), [Specification](#specification) and [Mediator](#mediator), for MediatR, its pipeline and its licence. The CQRS subsection under *Enterprise & Application Patterns* is worth the two minutes too.
5. [Chapter 3: API Versioning & Backward Compatibility](#api-versioning-backward-compatibility): the breaking-change table, the tolerant reader, schemes, expand and contract, retiring a version.
6. [Chapter 3: gRPC](#grpc) and [Choosing between REST, gRPC, and SignalR](#choosing-between-rest-grpc-and-signalr).
7. [Chapter 27: System Design Fundamentals](#system-design-fundamentals): the repeatable approach, the building blocks, and a worked URL shortener.
8. [Chapter 32: The Capstone: One Project, Growing Up](#the-capstone-one-project-growing-up): one system taken from monolith to services, step by step.

## Practice

**1. An ADR for one decision (1 h).** Take a decision your team made, or the one in *Decide* below, and write it in the shape of [Chapter 17's Architecture Decision Records](#architecture-decision-records-adrs): context, the options with their costs, the decision, the consequences, and what would make you revisit it. Keep it to one page; an ADR is read by someone in a hurry.

**2. A system design in 90 minutes (1 h 30 min).** Pick a prompt you haven't seen designed, for example "deliver webhooks to partners, with retries, at a few hundred thousand events a day". Follow Chapter 27's approach and write each step down: requirements (functional and not), estimates (requests per second, storage per year), the API, the data model, the components, the first two bottlenecks, and for each major choice the option you rejected and why. Then mark every place where Module 3's guarantees (outbox, idempotency, ordering) apply.

**3. The capstone, as a longer project.** [Chapter 32](#chapter-32-putting-it-all-together-a-capstone-learning-path)'s Steps 6 and 7 ([Refactor Toward Clean Architecture and DDD](#step-6-refactor-toward-clean-architecture-and-ddd) and [Split Into Microservices](#step-7-split-into-microservices)) are this module's material built for real; write an ADR for each split you make.

**Evidence to keep**, in your own public portfolio repo, not in this one: the ADRs and the design write-up. Decisions about a real employer's system stay private; publish a version about the capstone or an invented system.

Later, if you need it: [Chapter 6: The 12-Factor App](#the-12-factor-app), [.NET Aspire](#net-aspire), and [Chapter 24: Versioning Strategies for REST APIs](#versioning-strategies-for-rest-apis).

## Three questions

**1.** A team split its monolith into five services, and now every release still needs all five deployed together, and an outage in one takes the others down. What went wrong, and how do you tell a real service boundary from a fake one?

<details>
<summary>Answer</summary>

- **The coupling moved instead of disappearing.** Usually the services share a database (a schema change breaks all of them), or call each other synchronously in chains (one slow service stalls the rest), or share a library of domain types that must be upgraded in lockstep. That is a distributed monolith.
- **A real boundary has three properties.** The service owns its data, and nobody else reads its tables. It can serve its requests from its own state, getting other services' data through events it has already received rather than through calls on the request path. And it changes on its own schedule, which usually means one team owns it.
- **The way back.** Merge services that always change together into one, or into a module of a modular monolith; move shared tables behind one owner; replace synchronous chains with events and local copies of the data (Module 3's outbox and idempotent consumers).
</details>

**2.** A team says it does CQRS because every request goes through MediatR. What does MediatR give them, what doesn't it, and what would real CQRS change?

<details>
<summary>Answer</summary>

- **What MediatR gives.** An in-process dispatch from a request object to its single handler, so controllers don't depend on services directly, and pipeline behaviours that wrap every request with validation, logging or a transaction.
- **What it doesn't.** Separate models: the commands and queries may still share one entity model and one `DbContext`. It adds no read store, no scaling, no consistency guarantee, and a level of indirection on every call. New major versions are commercially licensed.
- **What CQRS changes.** Queries read a model shaped for the screen (a projection, a view, or a separate store), and commands go through the domain model that enforces invariants. With a separate store, reads become eventually consistent, and the UI and the business rules must tolerate that. It pays where reads and writes have very different shapes or loads; for CRUD, it is ceremony.
</details>

**3.** A public API returns `customerName`. The business now wants `givenName` and `familyName`. How do you ship it without a v2, which steps break whom, and how do you know when the old field can go?

<details>
<summary>Answer</summary>

- **Expand.** Add `givenName` and `familyName` to responses next to `customerName`, and accept either form on requests. Adding response fields is safe for tolerant readers; check that your known clients are.
- **Migrate.** Mark `customerName` deprecated in the OpenAPI document and the changelog, tell the consumers, and log which consumers still read or send it, on logs or traces rather than metric tags if there are many consumers.
- **Contract.** Remove `customerName` only when that telemetry has shown no use for an agreed period, after the announced date.
- **When a version is right instead.** If the meaning changes (a name is no longer one string anywhere in the domain), or the old field can't be derived from the new ones, a new version with a published retirement date for the old one is more honest than a field that lies.
</details>

## Decide

A team of twelve developers works on one ASP.NET Core monolith with one database. Deploys take an afternoon, merge conflicts are frequent, and a bug in invoicing blocked a release of the catalogue last month. Three proposals:

1. Split it into six microservices along the current folders.
2. Turn it into a modular monolith: modules with their own schemas and narrow APIs, boundaries checked by architecture tests, still one deployable.
3. Keep the structure and fix the delivery pipeline: faster tests, trunk-based development, feature flags.

<details>
<summary>How a senior engineer weighs it</summary>

**What each costs.**

- *Six services:* months of work and a permanent operations bill (pipelines, monitoring, on-call, contracts and versioning between services, Module 3's consistency machinery). The folders are not known to be business boundaries, so the split risks a distributed monolith. It is also the only option that lets parts deploy and scale independently.
- *Modular monolith:* weeks to draw and enforce boundaries, with a lot of untangling where modules read each other's tables. It addresses merge conflicts and the blast radius of a bug, keeps one deployment and transactions across modules, and makes a later split cheap, because a module with its own schema and API is most of a service.
- *Fix the pipeline:* days to weeks. It addresses the slow deploys and blocked releases directly (a feature flag would have shipped the catalogue with invoicing switched off), and changes nothing about coupling.

**What decides it here:** the pains are delivery and coupling, not scale. Nothing says one part needs to scale or deploy independently of the others.

**The choice.** Fix the pipeline first, because it pays within weeks, and move to a modular monolith in parallel, one module at a time, starting with the most independent one. Record both decisions as ADRs.

**What would change it.** A part with a genuinely different scaling or availability profile, a separate team that must ship on its own schedule, or a compliance boundary would justify extracting that one module into a service — and the modular boundary is what makes that extraction cheap.
</details>

## Check at work

**Inspect.** Does your domain project reference EF Core, ASP.NET Core or an HTTP client? Which modules or services read another's tables? Which decisions of the last year exist only in someone's memory? Pick one and write its ADR. For your API: is there a written list of what counts as a breaking change, and does anything in the pipeline (an OpenAPI diff, contract tests) catch one before release?

**Measure.** For each API version or deprecated field you serve: how many consumers called it in the last 30 days. For a request path that crosses services: how many synchronous calls it makes, and the end-to-end latency against the sum of its parts.
