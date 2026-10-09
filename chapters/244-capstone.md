# Chapter 44: Capstone — One Project, Growing Up

Reading about architecture teaches you vocabulary. Evolving a single real system teaches you judgment. This chapter is one project carried through every stage of its life, so that you feel the pain that motivates each technique before you reach for it. You will build **ShopCore**, a small e-commerce backend — products, carts, orders, and payments. The domain is deliberately familiar so your attention stays on the engineering.

The eight steps follow the order of the book. Steps 1–5 use what Part 1 teaches — a Web API on EF Core, tests, a container, a pipeline, then caching, authentication and observability — and leave you with a system a middle developer can own alone. Steps 6–8 use Part 2: refactor toward clean boundaries, split along them into services that talk through an outbox, and deploy the result to the cloud from code. Do the steps in order. Each names the chapters it exercises and comes with acceptance criteria: concrete, checkable statements that tell you the step is genuinely done, not merely "compiling on my machine."

> **The portfolio rule.** ShopCore, its numbers and its write-ups belong in *your own* public repository, not in this one. It is a natural centrepiece for the evidence portfolio from [Chapter 37](#chapter-37-the-story-bank-evidence-portfolio): each step's acceptance criteria are the evidence.

## Step 1 — The Honest Monolith

Start with a single ASP.NET Core Web API project, EF Core with the Npgsql provider, and a PostgreSQL database in a local container. Model products, carts, and orders. Expose CRUD-plus-checkout endpoints. Keep it a modular monolith: separate folders or projects per feature, no premature service boundaries.

*Exercises:* ASP.NET Core ([Chapter 5](#chapter-5-http-and-web-apis)), EF Core and data modeling ([Chapter 7](#chapter-7-data-access)), money as a type ([Chapter 15](#chapter-15-dates-money-and-strings)), basic design patterns — repository only where it earns its place ([Chapter 10](#chapter-10-design-basics)).

*Acceptance criteria:*
- A client can create a product, add it to a cart, and place an order end to end.
- EF Core migrations create the schema from scratch on an empty database.
- Money is modeled correctly (decimal, currency-aware), and orders capture a price snapshot rather than referencing live product prices.
- The solution builds and runs with a single `dotnet run` after the database container is up.

## Step 2 — Prove It Works: Tests

Now make the system trustworthy. Add unit tests for domain logic (pricing, cart totals, order-state transitions) with xUnit. Add integration tests that spin up a real PostgreSQL using Testcontainers and exercise the API through `WebApplicationFactory`, hitting the actual database rather than mocks.

*Exercises:* unit and integration testing with Testcontainers ([Chapter 8](#chapter-8-testing)), async end to end ([Chapter 4](#chapter-4-async-essentials)), design for testability ([Chapter 10](#chapter-10-design-basics)).

*Acceptance criteria:*
- Domain rules have unit tests; a deliberately broken rule turns a test red.
- At least one integration test places an order through the HTTP surface against a Testcontainers PostgreSQL instance.
- The whole suite runs with `dotnet test` and finishes in under a minute.
- Test data is isolated per test; runs are order-independent and repeatable.

## Step 3 — Dockerize

Package the API as a container using a multi-stage Dockerfile (SDK image to build, runtime image to run). Add a `docker-compose.yml` that brings up the API and PostgreSQL together. This is the moment "works on my machine" becomes "works anywhere."

*Exercises:* images, multi-stage builds, Compose and non-root containers ([Chapter 14](#chapter-14-containers-and-linux)).

*Acceptance criteria:*
- `docker compose up` starts the API and database with no manual steps.
- The runtime image is based on a slim/aspnet base and does not carry the SDK.
- The image runs as a non-root user and reads configuration (connection strings, secrets) from environment variables, not baked-in files.

## Step 4 — CI/CD with GitHub Actions

Automate the path from commit to artifact. Create a GitHub Actions workflow that restores, builds, runs the full test suite (Testcontainers works on the runner), and builds and pushes the Docker image to a registry (GitHub Container Registry) on merges to main.

*Exercises:* CI/CD and GitHub Actions ([Chapter 13](#chapter-13-git-and-cicd)), container registries ([Chapter 14](#chapter-14-containers-and-linux)).

*Acceptance criteria:*
- Every pull request runs build and tests; a failing test blocks the merge.
- A merge to main publishes a tagged image to the registry.
- The pipeline is defined in version-controlled YAML, and its runtime is under roughly ten minutes.

## Step 5 — Caching, Auth, and Observability

Harden the running system. Add Redis as a distributed cache for hot read paths (product catalog) with sensible invalidation. Add JWT-based authentication and role-based authorization so only authenticated users check out and only admins mutate the catalog. Replace ad-hoc logging with structured logging (Serilog), and instrument the app with OpenTelemetry for distributed tracing and metrics, exporting to a local collector (Jaeger or the OTEL Collector plus Prometheus).

*Exercises:* caching and invalidation ([Chapter 7](#chapter-7-data-access); Redis in depth in [Chapter 18](#chapter-18-data-in-depth)), authentication, authorization and token handling ([Chapter 12](#chapter-12-security-essentials)), structured logging ([Chapter 9](#chapter-9-exceptions-logging-and-first-diagnosis)), traces and metrics ([Chapter 25](#chapter-25-observability-and-testing-at-scale)).

*Acceptance criteria:*
- Cached catalog reads demonstrably avoid database round-trips, and a write invalidates the relevant cache entry.
- Protected endpoints reject missing or invalid tokens with 401; forbidden roles get 403.
- Logs are structured (queryable by fields like order id), and a single checkout produces one connected trace spanning the API, database, and cache.

## Step 6 — Refactor Toward Clean Architecture and DDD

The monolith now works and is observable — a perfect time to improve its internal structure without changing behavior. Introduce clear layers: a Domain project with entities, value objects (Money, Address), and aggregates (Order as an aggregate root enforcing its own invariants); an Application layer of use-case handlers (consider MediatR or hand-rolled command/query handlers); and Infrastructure for EF Core and external concerns. Dependencies point inward. Lean on your Step 2 tests as a safety net — this is where they pay for themselves.

*Exercises:* clean architecture and dependency inversion ([Chapter 10](#chapter-10-design-basics)), DDD tactical patterns ([Chapter 21](#chapter-21-architecture)).

*Acceptance criteria:*
- The Domain project references no infrastructure and has no EF Core or ASP.NET dependency.
- Business invariants (an order cannot be paid twice; a cart cannot check out empty) live in the domain and are enforced there, not in controllers.
- Every test from Step 2 still passes unchanged — the refactor preserved behavior.

## Step 7 — Split Into Microservices

Only now, with clean boundaries already drawn, is it safe to split. Carve out two or three services along the seams the DDD work revealed — for example **Catalog**, **Ordering**, and **Payments**. They communicate asynchronously over RabbitMQ using MassTransit. Critically, apply the **Outbox pattern**: a service writes domain changes and outgoing messages in the same database transaction, and a relay publishes them afterward, so you never lose or double-fire events across the network.

*Exercises:* messaging and the outbox ([Chapter 11](#chapter-11-messaging-and-background-work)), idempotency and delivery guarantees across services ([Chapter 20](#chapter-20-distributed-systems)), where to draw service boundaries ([Chapter 21](#chapter-21-architecture)).

*Acceptance criteria:*
- Placing an order in Ordering publishes an event that Catalog (stock) and Payments consume, with no synchronous HTTP call between them for that flow.
- Message publishing is transactional via the outbox: killing a service mid-checkout leaves no order without its corresponding event, and no event without its order.
- Consumers are idempotent — redelivering the same message does not create a duplicate payment or double-decrement stock.
- Each service owns its own database; no service reaches into another's tables.

## Step 8 — Deploy with Infrastructure as Code

Take the system to a real environment. Write Terraform to provision the infrastructure — a managed Kubernetes cluster or a cloud container service (Azure Container Apps, AWS ECS), plus managed PostgreSQL, a message broker, and Redis. Deploy the three services, wire up config and secrets, and expose an ingress. Your CI/CD pipeline from Step 4 now promotes images into this environment.

*Exercises:* cloud services and infrastructure as code ([Chapter 28](#chapter-28-cloud-fundamentals-aws-azure); on Azure, [Chapter 29](#chapter-29-azure-in-depth-for-net-developers)), Kubernetes and deployment strategies ([Chapter 26](#chapter-26-delivery-and-platform)), observability in production ([Chapter 25](#chapter-25-observability-and-testing-at-scale)).

*Acceptance criteria:*
- The entire environment can be created and destroyed with `terraform apply` / `terraform destroy`; nothing critical is clicked together by hand.
- All three services run in the target platform, reachable through a single ingress or gateway.
- Secrets come from a managed secret store, never from committed files.
- Traces and metrics from Step 5 flow to a hosted backend, so you can watch a real request cross service boundaries.

By the end of Step 8 you have not read about distributed systems — you have built, broken, and operated one. That experience is what interviewers and teammates recognize as senior.
