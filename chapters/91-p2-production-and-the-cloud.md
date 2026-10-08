# Part 2 · Module 6: Production and the Cloud

> **What this module makes you able to do.** Decide how a .NET service runs, connects, authenticates and ships on Azure — which compute, which identity, which network path, which deployment strategy — and diagnose the incidents that come from the platform rather than from the code: a timeout you didn't set, a port budget you didn't know you had, a token from the wrong identity, a DNS answer from the wrong zone.

**Time:** reading ≈ 1 h 30 min; hands-on ≈ 5 h 35 min — Chapter 51's two bugs 1 h, its two decisions 45, the pipeline 2 h 30, the questions 20, the decision 15, the check at work 45.

## Covers

- App Service's limits that cause real incidents — the 230-second front end, 128 SNAT ports per instance and destination, the order of a slot swap — and why none of them shows up on a CPU graph;
- Functions: why scale-out multiplies concurrency against everything downstream, and how to bound it;
- identity end to end: managed identity, the `DefaultAzureCredential` chain, role assignments that take minutes to apply, and private endpoints whose DNS decides whether they work;
- Azure SQL and Cosmos DB at the decision level: failovers you must retry through, and a partition key you can't change later;
- containers and Kubernetes as a senior backend developer needs them: namespaces and cgroups, probes, requests and limits, the shutdown signal;
- CI/CD that makes a small change cheap and a rollback cheaper; zero trust, workload identity and mTLS; the supply chain and Linux as the next layer down.

## The mechanism to explain without notes

**In production your code runs inside platform mechanisms it never calls — a front end with a timeout, a NAT with a port budget, a token endpoint, a DNS zone, a scheduler that scales, kills and moves it — and most cloud incidents are one of them doing exactly what its documentation says.**

Each mechanism has a limit, a timer or an identity, and none of them is in your code:

- **The front end** closes any App Service request without a response at 230 seconds, while your code keeps running (Chapter 51, Case 11).
- **SNAT** gives an instance 128 preallocated ports per destination for outbound connections to public addresses, and holds each port for four minutes after its connection closes. The ceiling is on *new connections per four minutes*, so CPU and memory stay green while requests time out (Case 3).
- **A slot swap** applies settings, restarts, warms up, switches routing, and only then recycles the old production code. Every "minute of 500s after a deployment" is one of those steps (Case 4).
- **A token** comes from a local endpoint, for whichever identity the credential chain found first; a role assignment takes minutes to apply; a private endpoint works only if its name resolves to the private IP *from where the app runs* (Case 10).
- **Azure SQL** moves your database between nodes routinely, so transient errors are the normal case and the retry belongs in the design (Case 14).
- **Kubernetes** kills a container at its memory limit, throttles it at its CPU limit, restarts it when liveness fails and sends `SIGTERM` with a grace period before it moves it.

So diagnosis starts from the symptom and the platform's mechanism — Chapter 51's triage card — not from the business logic. The same view makes change and trust cheap. Build one immutable artifact and promote it; separate deploy from release with flags; keep schema changes expand-then-contract so the previous build still runs; then rollback is faster than a fix forward, changes get smaller, and each failure is small. On the security side, network position confers no trust: each workload gets a platform-attested identity with short-lived tokens (managed identity, workload identity federation in CI, mTLS inside a mesh), and every call is authorized for that identity, so a leaked credential expires before it is useful. The supply chain (Chapter 35) applies the same idea to what you build from, and Linux (Chapter 31) is the layer under all of it: signals, permissions, cgroups.

> **Pay attention.** **Serverless scale multiplies your concurrency against everything downstream.** The Service Bus trigger runs up to `maxConcurrentCalls` messages per instance — 16 by default, effectively multiplied by the core count — and the platform adds instances while the queue is deep. At 40 two-core instances that is 1,280 concurrent executions against one database (Chapter 50's arithmetic). The queue was supposed to level the load; unbounded consumers pass the burst straight through. Fix: write down each consumer's maximum concurrency (per-instance concurrency × maximum instance count), size it against the downstream limit, and cap both settings (Chapter 51, Case 7).

## Read (≈ 1 h 30 min)

1. [Chapter 51: The Azure Triage Card](#the-azure-triage-card): start from the symptom; keep it open while you read the rest.
2. [Chapter 50: Compute: Choosing It and Running It](#compute-choosing-it-and-running-it): the decision table; App Service (the plan, the swap order, health check, the 230-second and SNAT limits); Functions (isolated worker, plans, concurrency, Durable Functions); Container Apps.
3. [Chapter 51: Case 3](#case-3-intermittent-timeouts-under-load-with-every-dashboard-green), [Case 4](#case-4-a-minute-of-500s-after-every-deployment), [Case 7](#case-7-functions-scaled-out-and-took-the-database-down) and [Case 11](#case-11-large-uploads-fail-at-almost-exactly-four-minutes): the compute limits as incidents.
4. [Chapter 50: Identity: Entra ID, Managed Identity and RBAC, Mechanically](#identity-entra-id-managed-identity-and-rbac-mechanically): the objects, the token endpoint, the credential chain, RBAC's delay, protecting your own API, federation in CI.
5. [Chapter 50: Networking for Application Developers](#networking-for-application-developers) and [Chapter 51: Case 10](#case-10-the-private-endpoint-that-made-things-worse): inbound against outbound, and DNS as the part that breaks.
6. [Chapter 50: Azure SQL Database](#azure-sql-database) and [Chapter 51: Case 14](#case-14-the-database-is-not-currently-available-every-few-days): failover as routine, and the execution strategy.
7. [Chapter 50: The model: partitions and request units](#the-model-partitions-and-request-units) and [Choosing the partition key](#choosing-the-partition-key-the-decision-you-cannot-easily-undo): Cosmos DB at the level of the one decision you can't undo.
8. [Chapter 51: Case 16 — The region went down](#case-16-the-region-went-down-a-design-review-after-the-fact): availability as a property of the whole request path.
9. [Chapter 11: What a Container Actually Is](#what-a-container-actually-is), [Kubernetes Fundamentals](#kubernetes-fundamentals) and [Kubernetes YAML for a .NET Deployment](#kubernetes-yaml-for-a-net-deployment): namespaces and cgroups, probes, requests and limits, the autoscaler.
10. [Chapter 12: What CI/CD Actually Means](#what-cicd-actually-means), [Deployment Strategies](#deployment-strategies), [Feature Flags](#feature-flags) and [DORA: four metrics, and exactly how each is gamed](#dora-four-metrics-and-exactly-how-each-is-gamed), with [Chapter 4: Migrations in CI/CD](#migrations-in-cicd) for expand-then-contract.
11. [Chapter 14: Zero Trust and Workload Identity](#zero-trust-and-workload-identity): attestation instead of secrets, SPIFFE, mTLS, OIDC federation in CI.

## Practice

**1. Chapter 51's two *Find the bug* samples (1 h).** In the *Exercises* of [Chapter 51](#chapter-51-the-azure-casebook-real-incidents-real-fixes), review both samples as pull requests before you open the answers: the lost update on a shared blob, and the batch that outlives its locks. Then run the tests that show each defect on the buggy code and its absence on the fix, against Azurite and the Service Bus emulator ([`verify/exercises/Ch51`](https://github.com/malyna2/dotnet-handbook/tree/main/verify/exercises/Ch51); needs the .NET 10 SDK and Docker, and both emulators have EULAs):

```bash
ACCEPT_EULA=Y verify/exercises/Ch51/verify.sh
```

**2. Chapter 51's two *What would you do* (45 min).** Answer the second — the document-processing service — as a one-page decision before you read the chapter's: the compute, the path around the 230-second limit, scaling and its cap, identity, and what would change your mind. The first — "just give the app Owner" — practises the answer you give under deadline pressure.

**3. A deployment pipeline that makes rollback cheap (2 h 30).** Chapter 12 has no exercise section; this one is built on it. Start from its [A Complete GitHub Actions Workflow for .NET](#a-complete-github-actions-workflow-for-net) (or the Azure Pipelines one) and extend it for one service:

- build and test once, publish one immutable artifact, and promote that same artifact through the environments;
- sign in to Azure with workload identity federation, so the pipeline stores no secret;
- deploy to a staging slot, warm it up on a health endpoint that touches the database, swap, and roll back by swapping again;
- run migrations as a gated step, expand-then-contract, so the previous build still runs against the new schema;
- put one change behind a feature flag and release it without a deployment.

Done when you can show a rollback in minutes and no stored secret. Chapter 32's capstone takes the same route in its steps 4 and 8 ([The Capstone](#the-capstone-one-project-growing-up)).

The pipeline, your decision memo and your notes belong in **your own public portfolio repo**, not in this one; anything about a real employer's system stays private.

Later, if you need it: Chapter 31's [Processes, Signals, Graceful Shutdown](#processes-signals-graceful-shutdown), [Permissions](#permissions-why-your-container-app-cant-write-that-file) and [Live Container Triage](#live-container-triage-a-walkthrough); Chapter 35, starting with [The Build Is Part of the Attack Surface](#the-build-is-part-of-the-attack-surface); Chapter 50's [Configuration and Secrets](#configuration-and-secrets-key-vault-and-app-configuration) with Chapter 51's [Case 13](#case-13-secret-rotation-took-production-down); Chapter 11's [Containerizing a .NET Application](#containerizing-a-net-application); then the rest of Chapter 51's cases.

## Three questions

**1.** An App Service API calls a partner's public API. Under load, intermittent connection timeouts appear; CPU sits at 30%. Scaling out from 2 to 4 instances makes them disappear for a few weeks. Why does scaling "fix" it, why does it come back, and what is the real fix?

<details>
<summary>Answer</summary>

- **The mechanism.** Every new outbound connection to a public address takes a SNAT port. An instance has 128 preallocated per destination, and a closed connection's port returns only four minutes later. The ceiling is on new connections per four minutes, per instance and destination, so CPU and memory stay green. A new `HttpClient`, SQL connection without pooling or SDK client per request means a new connection per request.
- **Why scaling out hides it.** Each instance brings its own ports: twice the instances, twice the budget. Traffic grows into the new budget, and the timeouts return, on a bigger bill.
- **The fix, in order.** Reuse connections (`IHttpClientFactory` or one long-lived client, singleton SDK clients), so the steady state needs a handful of ports. Use private endpoints for Azure services, whose traffic stays in the VNet and skips SNAT. Add a NAT gateway (64,512 ports per public IP) only if the rate of genuinely new connections still exceeds the budget. Confirm with *Diagnose and solve problems → SNAT Port Exhaustion*.
</details>

**2.** A function app with a Service Bus trigger drains a deep import queue, and the Azure SQL database it shares with checkout starts returning error 10928. Walk through the mechanism, and say what you change.

<details>
<summary>Answer</summary>

- **Concurrency is instances × per-instance concurrency.** The trigger runs up to `maxConcurrentCalls` messages per instance (16 by default, effectively per core), and the platform scales out on queue depth. Dozens of instances turn into over a thousand concurrent executions, each with a connection and a session.
- **The database has a fixed ceiling.** 10928 is the worker limit: the import has taken every worker, and checkout fails on the same database.
- **The fix.** Cap `maxConcurrentCalls` in `host.json` and the maximum instance count, and size their product from the database's limits with headroom for checkout. Batch the writes (one round trip per batch). Isolate the workload: a separate database, an elastic pool with per-database limits, or a time window.
- **The principle.** A queue levels load only if its consumers drain it at a bounded rate. Unbounded consumers make the queue a delay line in front of the same overload.
</details>

**3.** Your team says it can always roll back by redeploying the previous build. Name three kinds of change that make that false, and what keeps rollback cheap.

<details>
<summary>Answer</summary>

- **A schema change the old build can't run against:** a dropped or renamed column, a new `NOT NULL` column. Expand then contract: add it nullable, backfill, switch the code, and remove the old shape in a later release (Chapter 4).
- **A contract change others already depend on:** a renamed response field or message property. Rolling back the producer doesn't recall the messages already in queues, and consumers may have deployed against the new shape. Make changes additive (expand–contract on the API, Chapter 3) and readers tolerant.
- **Settings and data that moved:** a setting that wasn't a slot setting followed the build into production (Chapter 51, Case 4), or a data migration rewrote rows. Mark environment settings sticky; make data migrations idempotent, and decide in advance whether they are reversible or fix-forward only.

What keeps rollback cheap: one immutable artifact, release separated from deploy by a flag (so many rollbacks are a flag flip), a schema that stays compatible with the previous build for one release, and a rollback you have actually rehearsed.
</details>

## Decide

Orders (App Service) calls Pricing (Container Apps); both are your team's and sit in the same VNet. Today Pricing checks a shared API key that both read from Key Vault. A security review asks you to "move to zero trust" this quarter. Which do you do?

- **A.** Keep the key, rotate it monthly, and restrict Pricing's ingress to the VNet.
- **B.** Managed identity and Entra ID: Orders requests a token for Pricing's application ID URI with its managed identity; Pricing validates it and authorizes on an app role assigned to Orders' identity.
- **C.** Move both services to AKS with a service mesh, and use mTLS with mesh authorization policies.

<details>
<summary>Answer</summary>

**The cost of each.**
- **A** is the cheapest change and keeps the castle-and-moat: anything inside the VNet that holds the key *is* Orders, a leaked key works until the next rotation, and every rotation is a chance to take production down (Chapter 51, Case 13). It answers the review with a schedule, not an architecture.
- **B** costs an app registration for Pricing with an app role, a role assignment to Orders' identity (an admin with the right to grant it), token validation in Pricing, and the propagation delay on the first deployment (a user-assigned identity created before the deployment avoids it). It removes the secret, issues short-lived tokens per caller, and lets Pricing authorize each caller separately.
- **C** costs a cluster and a mesh to operate — full Kubernetes responsibility — to solve an authentication problem for two services. mTLS authenticates the service, not the user, so you would still need token-based authorization for user context.

**What decides it here:** the platform you already run and the size of the estate. Two services on App Service and Container Apps already have platform-attested identities; the platform's own mechanism is the cheapest correct one.

**The choice: B.** Then turn the API key off, so nothing can fall back to it.

**What would change it.** Dozens of services on Kubernetes with a platform team: a mesh for mTLS between workloads, with B-style tokens still carrying authorization. A caller outside Azure: workload identity federation from its own identity provider, still with no shared secret.
</details>

## Check at work

**Inspect.** Walk Chapter 51's triage card over one service of your own. Which identity does each environment's app run as — decode a token's `oid`, or list the identity's role assignments — and is any key or secret-bearing connection string still in its settings? From inside the app, does each private endpoint's name resolve to a private IP? Which settings are slot settings? For each queue consumer, write per-instance concurrency × maximum instances, and compare it with the downstream database's limit.

**Measure.** The time from "roll back" to the previous version serving users, from your last real rollback. If there was none, schedule a rehearsal: a rollback you have never run is a hope. On App Service, also open *Diagnose and solve problems → SNAT Port Exhaustion* for your busiest app.
