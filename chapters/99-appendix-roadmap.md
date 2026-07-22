# Appendix A: Quick-Reference Roadmap & Checklist

> This appendix reproduces the original one-page roadmap the book was built from. Use it as a checklist to track what you've leveled up. Each section maps to a full chapter above.

A comprehensive checklist of patterns, technologies, frameworks, and tools that a mid-level .NET developer should master to grow toward senior level.

> **How to use this:** Don't try to learn everything at once. Pick a section, go deep, build something with it, then move on. Depth beats breadth. Items marked ⭐ are high-priority / high-impact.

---

## Table of Contents
1. [C# Language Mastery](#1-c-language-mastery)
2. [.NET Runtime & Internals](#2-net-runtime--internals)
3. [ASP.NET Core & Web APIs](#3-aspnet-core--web-apis)
4. [Data Access & Databases](#4-data-access--databases)
5. [Design Patterns](#5-design-patterns)
6. [Architecture & Application Design](#6-architecture--application-design)
7. [Testing](#7-testing)
8. [Asynchronous & Concurrent Programming](#8-asynchronous--concurrent-programming)
9. [Messaging & Distributed Systems](#9-messaging--distributed-systems)
10. [Cloud (AWS & Azure)](#10-cloud-aws--azure)
11. [Containers & Orchestration](#11-containers--orchestration)
12. [DevOps & CI/CD](#12-devops--cicd)
13. [Observability](#13-observability)
14. [Security](#14-security)
15. [Performance & Optimization](#15-performance--optimization)
16. [Tooling & Productivity](#16-tooling--productivity)
17. [Soft Skills & Practices](#17-soft-skills--practices)
18. [Suggested Learning Path](#18-suggested-learning-path)

---

## 1. C# Language Mastery

The foundation. A middle dev uses C#; a senior *understands* it.

- ⭐ **Types & memory**: value vs. reference types, boxing/unboxing, `struct` vs `class`, `readonly struct`, `ref struct` (e.g. `Span<T>`)
- ⭐ **Generics**: constraints, covariance/contravariance (`in`/`out`), generic math (`INumber<T>`)
- ⭐ **LINQ**: deferred vs. immediate execution, `IEnumerable` vs `IQueryable`, expression trees
- ⭐ **Delegates, events, `Func`/`Action`/`Predicate`**, lambdas, closures (and their capture pitfalls)
- **Nullable reference types** (`?`, `!`, `[NotNull]` attributes) — enable and respect them
- **Pattern matching**: switch expressions, property/positional/list patterns, `is`/`when`
- **Records** (`record`, `record struct`), `with` expressions, value equality
- **Tuples**, deconstruction
- **`Span<T>` / `Memory<T>` / `stackalloc`** — zero-allocation slicing
- **`IDisposable` / `IAsyncDisposable`**, `using` declarations, the Dispose pattern
- **Iterators** (`yield return`), custom enumerators
- **Extension methods**, static abstract members in interfaces
- **Attributes & reflection**, `System.Reflection`, `TypeDescriptor`
- **Source Generators** & Roslyn analyzers (compile-time codegen)
- **`unsafe` code & pointers** (know it exists, rarely needed)
- Newer syntax: **primary constructors**, **collection expressions** (`[..]`), **required members**, **file-scoped namespaces**, **raw string literals**, **global usings**

**Books:** *C# in Depth* (Jon Skeet), *Pro C#* (Troelsen).

---

## 2. .NET Runtime & Internals

- ⭐ **Garbage Collection**: generations (0/1/2), LOH (Large Object Heap), workstation vs server GC, background GC
- ⭐ **Memory management**: the stack vs. the managed heap, `GC.Collect` (and why not to call it)
- **CLR / CoreCLR**, JIT compilation, **Tiered Compilation**, ReadyToRun, **AOT (Native AOT)**
- **Assemblies, `AssemblyLoadContext`**, strong naming
- **`IConfiguration`** & the configuration system (JSON, env vars, user secrets, providers)
- ⭐ **Dependency Injection** (built-in `Microsoft.Extensions.DependencyInjection`): lifetimes (Singleton/Scoped/Transient), captive dependencies
- **`IHostedService` / `BackgroundService`**, the Generic Host
- **`ILogger` / `Microsoft.Extensions.Logging`** abstraction
- **`System.Text.Json`** (default) vs. **Newtonsoft.Json** — serialization, converters, source-gen serialization
- .NET versions: know **.NET 8 (LTS)** and **.NET 9/10**; understand LTS vs STS release cadence

---

## 3. ASP.NET Core & Web APIs

- ⭐ **Middleware pipeline** & request lifecycle
- ⭐ **Minimal APIs** vs **Controllers (MVC)** — know both, know when to use each
- **Model binding & validation** (DataAnnotations, `FluentValidation`)
- **Filters** (action, res/exception/authorization filters)
- ⭐ **Routing**, endpoint routing, route constraints
- **Authentication & Authorization**: JWT bearer, cookies, OAuth2/OIDC, policy-based & role-based auth
- **`IHttpClientFactory`** — typed/named clients, resilience with **Polly** (retries, circuit breaker, timeout)
- **CORS**, rate limiting (built-in in .NET 7+), output caching, response compression
- ⭐ **REST** principles, proper status codes, **API versioning**, **OpenAPI/Swagger** (Swashbuckle / NSwag)
- **gRPC** — contract-first, streaming, when to prefer over REST
- **SignalR** — real-time websockets
- **GraphQL** with **HotChocolate** (nice-to-have)
- **`ProblemDetails`** (RFC 7807) for error responses
- **Blazor** (Server & WASM) — know it exists; learn if doing UI
- **Health checks** (`/health`, readiness vs liveness)

---

## 4. Data Access & Databases

- ⭐ **Entity Framework Core**: DbContext, change tracking, migrations, `AsNoTracking`, eager/lazy/explicit loading, the **N+1 problem**, compiled queries, split queries
- ⭐ **SQL fundamentals**: joins, indexes, execution plans, transactions & isolation levels, deadlocks
- **Dapper** — micro-ORM for performance-critical / control-heavy queries
- **Database design**: normalization, foreign keys, constraints, when to denormalize
- **NoSQL**: MongoDB (document), Redis (cache/kv), Elasticsearch (search)
- ⭐ **Caching**: `IMemoryCache`, `IDistributedCache`, Redis, cache invalidation strategies, cache-aside pattern
- **Migrations strategy** in CI/CD (EF migrations vs. tools like **DbUp**, **Flyway**, **Liquibase**)
- **Connection pooling**, `DbContext` lifetime (Scoped!), pooling with `AddDbContextPool`
- **Concurrency**: optimistic (`RowVersion`/`Timestamp`) vs pessimistic locking
- **Stored procedures**, views, and when raw SQL is justified

---

## 5. Design Patterns

### Gang of Four (know the important ones)
**Creational**
- ⭐ Factory Method / Abstract Factory
- ⭐ Builder
- Singleton (know the pitfalls — prefer DI)
- Prototype

**Structural**
- ⭐ Adapter
- ⭐ Decorator
- Facade
- Proxy
- Composite
- Bridge, Flyweight

**Behavioral**
- ⭐ Strategy
- ⭐ Observer (and how it relates to events / `IObservable`)
- ⭐ Mediator (see MediatR)
- Command
- Template Method
- Chain of Responsibility (mirrors ASP.NET middleware)
- State, Visitor, Iterator, Memento

### Enterprise / application patterns
- ⭐ **Repository** & **Unit of Work** (and the debate about using them over EF Core)
- ⭐ **Dependency Injection / Inversion of Control**
- **Specification** pattern
- **CQRS** (Command Query Responsibility Segregation)
- **Options** pattern (`IOptions<T>`, `IOptionsSnapshot`, `IOptionsMonitor`)
- **Result** pattern / railway-oriented programming (vs. exceptions for flow control)
- **Null Object**, **Guard clauses**

### Principles
- ⭐ **SOLID** (know each letter with a real example)
- ⭐ **DRY, KISS, YAGNI**
- **Separation of Concerns**, **Law of Demeter**
- **Composition over inheritance**

**Book:** *Head First Design Patterns*, *Dependency Injection Principles, Practices, and Patterns* (Seemann).

---

## 6. Architecture & Application Design

- ⭐ **Layered / N-tier architecture**
- ⭐ **Clean Architecture** / **Onion Architecture** / **Hexagonal (Ports & Adapters)**
- ⭐ **Domain-Driven Design (DDD)**: entities, value objects, aggregates, domain events, bounded contexts, ubiquitous language
- **CQRS** + **Event Sourcing** (advanced)
- **Vertical Slice Architecture**
- **Microservices** vs **Monolith** vs **Modular Monolith** — trade-offs, when to split
- **API Gateway**, **Backend for Frontend (BFF)**
- **12-Factor App** methodology
- **Saga pattern** / distributed transactions, **Outbox pattern**
- **Idempotency**, eventual consistency
- **.NET Aspire** — cloud-native orchestration for local dev & deployment (new, worth learning)

**Books:** *Clean Architecture* (Uncle Bob), *Implementing DDD* (Vernon), *Patterns of Enterprise Application Architecture* (Fowler).

---

## 7. Testing

- ⭐ **Unit testing**: xUnit (preferred), NUnit, MSTest
- ⭐ **Mocking**: Moq, **NSubstitute** (Moq alternative), FakeItEasy
- ⭐ **Assertions**: FluentAssertions / Shouldly
- **Test data**: AutoFixture, Bogus (fake data)
- ⭐ **Integration testing**: `WebApplicationFactory`, `TestServer`
- **Testcontainers** for .NET — spin up real DBs/queues in Docker during tests
- **Test doubles**: mocks vs stubs vs fakes vs spies
- **TDD** (red-green-refactor) and **BDD** (SpecFlow / Reqnroll)
- **Snapshot testing** (Verify)
- **Mutation testing** (Stryker.NET)
- **Code coverage** (Coverlet, ReportGenerator) — coverage as a signal, not a goal
- **Test naming & AAA** (Arrange-Act-Assert) conventions

---

## 8. Asynchronous & Concurrent Programming

- ⭐ **`async`/`await`** deeply — the state machine, `Task` vs `ValueTask`
- ⭐ **`ConfigureAwait(false)`** — when & why
- ⭐ **Deadlocks** from `.Result` / `.Wait()` — avoid sync-over-async
- **`CancellationToken`** — propagation everywhere
- **`Task.WhenAll` / `Task.WhenAny`**, throttling with `SemaphoreSlim`
- **`IAsyncEnumerable<T>`** & `await foreach`
- **TPL** (Task Parallel Library), `Parallel.For/ForEach`, `Parallel.ForEachAsync`
- **`System.Threading.Channels`** — producer/consumer pipelines
- **Thread safety**: `lock`, `Interlocked`, `Concurrent*` collections, `Volatile`
- **`Reactive Extensions (Rx.NET)`** (nice-to-have)

---

## 9. Messaging & Distributed Systems

- ⭐ **Message brokers**: RabbitMQ (AMQP), Apache Kafka (event streaming), Azure Service Bus, AWS SQS/SNS
- ⭐ **Messaging abstractions**: **MassTransit** ⭐, NServiceBus, Rebus
- **Messaging patterns**: pub/sub, request/response, competing consumers, dead-letter queues
- **Event-driven architecture**, event streaming vs message queues
- **Distributed patterns**: Saga, Outbox/Inbox, Circuit Breaker, Retry with backoff, Bulkhead
- **Consistency**: CAP theorem, eventual consistency, idempotent consumers
- **Distributed caching** & session state

---

## 10. Cloud (AWS & Azure)

Pick one deeply; be conversant in the other.

### AWS basics ⭐
- **EC2** (VMs), **S3** (object storage), **RDS** (managed SQL), **DynamoDB** (NoSQL)
- **Lambda** (serverless) + **API Gateway**
- **ECS / Fargate** (containers), **EKS** (Kubernetes)
- **SQS / SNS** (messaging), **EventBridge**
- **IAM** (identity & permissions) ⭐, roles, policies
- **CloudWatch** (logs/metrics), **CloudFormation / CDK** (IaC)
- **VPC** basics, **Secrets Manager / Parameter Store**
- **AWS SDK for .NET**, `AWSSDK.*` NuGet packages

### Azure (the "native" .NET cloud)
- **App Service**, **Azure Functions** (serverless)
- **Azure SQL**, **Cosmos DB**
- **Blob Storage**, **Azure Service Bus**, **Event Hubs / Event Grid**
- **AKS** (Kubernetes), **Container Apps**
- **Entra ID** (formerly Azure AD), Managed Identity ⭐
- **Key Vault**, **Application Insights** ⭐
- **Azure DevOps** & **ARM/Bicep** (IaC)

### Cross-cutting
- **Infrastructure as Code**: Terraform ⭐ (cloud-agnostic), Pulumi (C#!), CloudFormation, Bicep
- **Serverless** trade-offs, cold starts
- **Cost awareness** & the shared-responsibility security model

---

## 11. Containers & Orchestration

- ⭐ **Docker**: images, containers, `Dockerfile`, multi-stage builds, layer caching, `.dockerignore`
- ⭐ Containerizing .NET apps (official `mcr.microsoft.com/dotnet` images, chiseled/distroless images)
- **Docker Compose** for local multi-service dev
- **Container registries**: Docker Hub, ACR, ECR, GHCR
- ⭐ **Kubernetes**: pods, deployments, services, ingress, ConfigMaps, Secrets, namespaces
- **Helm** (K8s package manager), **Kustomize**
- **kubectl** fluency, liveness/readiness probes, resource limits
- **Service mesh** (Istio/Linkerd) — awareness level
- **.NET Aspire** — great local orchestration story for .NET microservices

---

## 12. DevOps & CI/CD

- ⭐ **Git** deeply: branching strategies (GitFlow, trunk-based), rebase vs merge, interactive rebase, resolving conflicts
- ⭐ **CI/CD pipelines**: GitHub Actions ⭐, Azure DevOps Pipelines, GitLab CI, Jenkins
- **Build automation**: `dotnet build/test/publish`, MSBuild basics, `Directory.Build.props`
- **NuGet**: consuming, creating & publishing packages, private feeds, `PackageReference`, central package management
- **Semantic versioning**, GitVersion
- **Artifact management**, deployment strategies (blue-green, canary, rolling)
- **Feature flags** (LaunchDarkly, `Microsoft.FeatureManagement`)
- **Secrets management** in pipelines (never commit secrets!)
- **Static analysis in CI**: SonarQube, code coverage gates

---

## 13. Observability

The "three pillars" — essential for distributed/microservice systems.

- ⭐ **Structured logging**: **Serilog** ⭐, NLog, sinks, log enrichment, correlation IDs
- ⭐ **Metrics**: Prometheus + Grafana, `System.Diagnostics.Metrics`
- ⭐ **Distributed tracing**: **OpenTelemetry** ⭐ (the standard), `Activity`/`ActivitySource`, W3C Trace Context
- **APM tools**: Application Insights, Datadog, New Relic, Jaeger, Zipkin
- **Centralized logging**: ELK/Elastic stack, Loki, Seq (great for .NET dev)
- **Correlation & context propagation** across services
- **Alerting & SLIs/SLOs**

---

## 14. Security

- ⭐ **OWASP Top 10** — know each vulnerability and the .NET mitigation
- ⭐ **Authentication vs Authorization**, OAuth 2.0, OpenID Connect, JWT (structure & validation)
- **Identity providers**: **IdentityServer / Duende**, Auth0, Entra ID, Keycloak, ASP.NET Core Identity
- ⭐ **Secrets management**: user-secrets (dev), Key Vault / Secrets Manager (prod), never hardcode
- **HTTPS/TLS**, HSTS, certificate management
- **Data protection**: hashing (bcrypt/Argon2 for passwords), encryption at rest/in transit, `IDataProtector`
- **Input validation, output encoding**, parameterized queries (SQL injection), anti-forgery (CSRF), XSS prevention
- **CORS** done correctly, security headers (CSP)
- **Dependency scanning**: `dotnet list package --vulnerable`, Dependabot, Snyk
- **Principle of least privilege**, secure defaults

---

## 15. Performance & Optimization

- ⭐ **Benchmarking**: **BenchmarkDotNet** ⭐ — measure, don't guess
- ⭐ **Profiling**: dotnet-trace, dotnet-counters, dotnet-dump, dotnet-gcdump, PerfView, Visual Studio Profiler, JetBrains dotTrace/dotMemory
- **Memory**: minimizing allocations, `Span<T>`, object pooling (`ArrayPool<T>`, `ObjectPool<T>`), `StringBuilder`
- **Async performance**: `ValueTask`, avoiding unnecessary allocations
- **EF Core perf**: `AsNoTracking`, projection, batching, compiled queries, avoiding N+1
- **Caching strategies** (already covered — huge lever)
- **Native AOT** & trimming for startup/size-sensitive workloads
- **Load testing**: k6, NBomber (C#!), JMeter
- Understand **Big-O** and pick the right data structures/collections

---

## 16. Tooling & Productivity

- ⭐ **IDE**: Visual Studio, **Rider** (JetBrains), VS Code + C# Dev Kit
- **Refactoring & linting**: ReSharper, Roslyn analyzers, `.editorconfig`, StyleCop, SonarLint
- **Formatting**: `dotnet format`, EditorConfig enforcement in CI
- **API testing**: Postman, Insomnia, `.http` files (built into VS/Rider), Bruno
- **CLI tools**: the `dotnet` CLI, global tools (`dotnet tool install -g`)
- **Diff/merge tools**, Git GUIs (Fork, GitKraken, `lazygit`)
- **Diagramming**: PlantUML, Mermaid, draw.io / C4 model
- **Local dev**: Testcontainers, LocalStack (AWS locally), Azurite (Azure storage emulator)
- **AI-assisted dev**: GitHub Copilot / Claude — use them well, review their output critically

---

## 17. Soft Skills & Practices

The differentiator between middle and senior is rarely just technical.

- ⭐ **Code review**: giving *and* receiving constructive feedback
- ⭐ **Communication**: explaining technical trade-offs to non-technical people
- **Estimation & breaking down work** into shippable increments
- **Reading & documenting code**: ADRs (Architecture Decision Records), READMEs, diagrams
- **Debugging methodically** (hypothesis-driven, not guess-and-check)
- **Refactoring safely** under test coverage
- **Mentoring** juniors, pairing
- **Agile/Scrum/Kanban** fluency
- **Knowing when *not* to add complexity** (YAGNI in practice)
- **Ownership**: from ticket to production to monitoring

---

## 18. Suggested Learning Path

**See Chapter 32.** The capstone chapter turns this checklist into a five-phase learning path and a single evolving project (ShopCore, a monolith that grows into a distributed system) with concrete acceptance criteria per step. Working through one project that grows with you touches ~80% of this list — far more valuable than isolated tutorials.

---

## Recommended Resources

- **Docs**: [Microsoft Learn](https://learn.microsoft.com/dotnet/) (free, official, excellent)
- **Books**: *C# in Depth*, *Dependency Injection Principles Practices & Patterns*, *Clean Architecture*, *Designing Data-Intensive Applications* (Kleppmann — the distributed-systems bible)
- **Blogs/People**: Andrew Lock, Steve Gordon, Nick Chapsas (YouTube), Milan Jovanović, Jimmy Bogard, David Fowler (Twitter/X)
- **Practice**: Exercism (C# track), build real projects, read open-source .NET repos on GitHub (e.g. `eShop`, `dotnet/runtime`)

---

*Remember: you won't master all of this — nobody has. The goal is broad awareness plus deep expertise in the areas your work demands. Revisit this list every few months and mark what you've leveled up.* 🚀
