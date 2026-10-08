# Chapter 26: Delivery and Platform

@@TODO: write this chapter's introduction (what it makes the reader able to do, how its sections connect), then remove every @@ line.@@

@@SRC: old Chapter 6: Architecture & Application Design@@
## The 12-Factor App

The Twelve-Factor App is a methodology for building software-as-a-service that is portable, disposable, and cloud-friendly; it predates Kubernetes but maps perfectly onto containerized .NET services. All twelve, at a glance:

| Factor | In one phrase |
|---|---|
| 1. Codebase | One repo, many deploys |
| 2. Dependencies | Declared explicitly via NuGet/`.csproj`; nothing preinstalled assumed |
| 3. Config | From the environment, not the codebase |
| 4. Backing services | Databases, queues, caches: attached, swappable resources |
| 5. Build, release, run | Artifact → bind config → execute; never edit a running server |
| 6. Processes | Stateless |
| 7. Port binding | Self-contained: Kestrel serves HTTP, no external web server |
| 8. Concurrency | Scale out with more processes, not bigger machines |
| 9. Disposability | Fast startup, graceful shutdown |
| 10. Dev/prod parity | Keep environments alike (containers) |
| 11. Logs | Event streams to stdout |
| 12. Admin processes | One-offs (migrations) run against the same code and config |

Four of these carry the .NET-specific weight:

- **Config (3).** Connection strings and secrets come from environment variables or a secret store, never a checked-in `appsettings.json`; .NET's layered configuration providers make this natural, so one artifact flows unchanged through every environment.
- **Statelessness + backing services (4, 6).** Nothing persisted in local memory or disk between requests; sessions and caches live in attached resources. This is the precondition for horizontal scaling (8).
- **Disposability (9).** Handle `SIGTERM`, finish in-flight work, release resources — the generic host's graceful-shutdown pipeline exists for this; it is what makes rolling deploys and elastic scaling safe.
- **Logs (11).** Structured logs to stdout; the platform aggregates. An app managing its own log files fights every orchestrator it runs under.

> **Why this matters for a senior .NET dev:** these factors are the contract that makes an app cloud-native. Violate them and no amount of Kubernetes will save you.

@@SRC: old Chapter 6: Architecture & Application Design@@
## .NET Aspire

Building distributed .NET systems means juggling many moving parts — several services, a database, Redis, a message broker, and the glue to wire them together locally and in the cloud. **.NET Aspire** is Microsoft's opinionated stack for exactly this: a cloud-ready framework for building observable, production-grade distributed applications. It is now GA and versioned independently of the annual .NET release (Aspire 9.x), so it ships on its own cadence rather than being pinned to a single .NET version.

Aspire's pieces:

- **App Host** — a C# project (the orchestrator) where you describe your application's topology in code: which projects, containers, and cloud resources exist and how they connect. During local development it spins them all up together.

```csharp
var builder = DistributedApplication.CreateBuilder(args);

var cache = builder.AddRedis("cache");
var db    = builder.AddPostgres("pg").AddDatabase("orders");

var api = builder.AddProject<Projects.OrdersApi>("orders-api")
                 .WithReference(db)
                 .WithReference(cache);

builder.AddProject<Projects.WebFrontend>("web")
       .WithReference(api);

builder.Build().Run();
```

- **Service Discovery & Configuration** — `WithReference` wires connection strings and endpoints automatically, so services find each other without hand-managed config.
- **Components/Integrations** — curated NuGet packages for common backing services (Redis, PostgreSQL, RabbitMQ, Azure resources) with sensible defaults, health checks, telemetry, and resilience baked in.
- **Dashboard** — a local developer dashboard showing every resource, its logs, distributed traces, and metrics via OpenTelemetry out of the box.
- **Deployment** — the same App Host model generates deployment manifests (e.g., to Azure Container Apps or Kubernetes via tools like Aspir8).

> **Where Aspire fits:** it directly addresses the *inner-loop* pain of distributed development (many-service orchestration, observability, configuration) and nudges you toward 12-Factor practices. It is not a service mesh or a runtime platform — it's a composition and developer-experience layer. For a team building a modular monolith or a handful of services, it dramatically lowers the friction of doing distributed .NET *well*.

@@SRC: old Chapter 11: Containers & Orchestration@@
## Kubernetes Fundamentals

Compose is wonderful for one machine. But production wants many machines, automatic restarts of crashed apps, rolling updates with zero downtime, scaling under load, and self-healing when a server dies. That is the job of an **orchestrator**, and **Kubernetes** (K8s) is the de facto standard.

The mental shift from Compose to Kubernetes is from *imperative* to *declarative*. You don't tell Kubernetes "start this container." You declare "I want three replicas of this app running," and Kubernetes' **control loops** continuously work to make reality match that declaration — restarting failed containers, rescheduling pods off dead nodes, all without you intervening.

### Cluster architecture

A Kubernetes cluster splits into a **control plane** (the brain) and **worker nodes** (the muscle).

The **control plane** components:

- **API server** — the front door. Every command and every component talks to the cluster through this REST API. `kubectl` is just a client of it.
- **etcd** — a distributed key-value store holding the entire cluster state: the single source of truth.
- **Scheduler** — decides which node each new pod should run on, based on resource requests, constraints, and affinity rules.
- **Controller manager** — runs the control loops (the Deployment controller, ReplicaSet controller, and more) that drive actual state toward desired state.

Each **worker node** runs:

- **kubelet** — the node's agent. It talks to the API server, starts the containers assigned to its node, and reports their health back.
- **Container runtime** — the software that actually runs containers (containerd, CRI-O).
- **kube-proxy** — programs the node's networking so that Service traffic reaches the right pods.

> **Analogy:** The control plane is a shipping company's dispatch office; the nodes are the trucks. You file a shipping order (a manifest) with dispatch (the API server). Dispatch records it (etcd), assigns it to a truck (scheduler), and the driver (kubelet) carries it out. If a truck breaks down, dispatch reassigns its load to another truck — you never had to know which truck.

### The core objects, from smallest to largest

**Pod** — the smallest deployable unit. A pod wraps one or more containers that share a network namespace (same IP, same localhost) and storage. Usually it's one app container, sometimes with helper "sidecar" containers. **Pods are ephemeral and disposable** — they get created and destroyed constantly, each with a new IP. You almost never create a pod directly.

**ReplicaSet** — ensures a specified number of identical pod replicas are running. If one dies, the ReplicaSet creates a replacement. You rarely manage these directly either.

**Deployment** — the object you actually work with. It manages ReplicaSets to give you **declarative updates and rollbacks**. Change the image in a Deployment and it performs a *rolling update*: spin up new pods, wait for them to become healthy, then retire the old ones — zero downtime. Something wrong? `kubectl rollout undo` reverts to the previous ReplicaSet.

**Service** — pods are ephemeral with changing IPs, so you can't point clients at a pod directly. A Service is a **stable network endpoint** — a fixed virtual IP and DNS name — that load-balances across a dynamic set of pods selected by labels. Three main types:

- **ClusterIP** (default) — reachable only *inside* the cluster. This is how your API talks to your database, or one microservice calls another.
- **NodePort** — opens a static port on every node's IP, exposing the service externally in a crude way. Mostly for dev or as a building block.
- **LoadBalancer** — provisions a real cloud load balancer (an Azure/AWS LB) with an external IP. The standard way to expose a service to the internet on a cloud provider.

**Ingress** — a LoadBalancer per service gets expensive and gives you no smart routing. An **Ingress** is an HTTP(S) layer-7 router: one entry point that routes by hostname and path (`api.example.com/orders` → orders service, `/users` → users service), terminates TLS, and does it all behind a single load balancer. It requires an **ingress controller** (NGINX, Traefik) running in the cluster to enforce the rules.

**ConfigMap** — externalizes non-secret configuration (feature flags, connection hosts, log levels) so you can change config without rebuilding the image.

**Secret** — like a ConfigMap but for sensitive values (passwords, API keys, tokens). Kubernetes stores them base64-encoded.

> **Pitfall:** Base64 is *encoding, not encryption*. Anyone with read access to Secrets can trivially decode them. Enable **encryption at rest** for etcd, lock Secret access down with RBAC, and for serious deployments integrate an external secrets manager (Azure Key Vault, HashiCorp Vault) rather than trusting raw Kubernetes Secrets alone.

**Namespace** — a virtual cluster within the cluster, for isolating environments or teams (e.g. `dev`, `staging`, `team-payments`). Names must be unique within a namespace, not across the whole cluster, and you can apply resource quotas and access policies per namespace.

@@SRC: old Chapter 11: Containers & Orchestration@@
## Kubernetes YAML for a .NET Deployment

Let's deploy the API. We'll define a ConfigMap, a Secret, a Deployment, and a Service. Kubernetes manifests are declarative YAML; you apply them with `kubectl apply -f`.

```yaml
# configmap.yaml
apiVersion: v1
kind: ConfigMap
metadata:
  name: myapi-config
  namespace: production
data:
  ASPNETCORE_ENVIRONMENT: "Production"
  Logging__LogLevel__Default: "Information"
  ConnectionStrings__Redis: "redis-service:6379"
---
# secret.yaml
apiVersion: v1
kind: Secret
metadata:
  name: myapi-secrets
  namespace: production
type: Opaque
stringData:
  # stringData lets you write plaintext; K8s base64-encodes it for you.
  ConnectionStrings__Postgres: "Host=postgres-service;Database=appdb;Username=app;Password=super-secret"
```

```yaml
# deployment.yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: myapi
  namespace: production
  labels:
    app: myapi
spec:
  replicas: 3
  selector:
    matchLabels:
      app: myapi
  template:                       # the pod template
    metadata:
      labels:
        app: myapi                # must match the selector above
    spec:
      containers:
        - name: myapi
          image: myregistry.azurecr.io/myapi:1.4.0
          ports:
            - containerPort: 8080
          envFrom:
            - configMapRef:
                name: myapi-config
            - secretRef:
                name: myapi-secrets
          resources:
            requests:             # guaranteed minimum, used for scheduling
              cpu: "100m"
              memory: "128Mi"
            limits:               # hard ceiling, enforced by cgroups
              cpu: "500m"
              memory: "256Mi"
          livenessProbe:
            httpGet:
              path: /healthz/live
              port: 8080
            initialDelaySeconds: 10
            periodSeconds: 10
          readinessProbe:
            httpGet:
              path: /healthz/ready
              port: 8080
            initialDelaySeconds: 5
            periodSeconds: 5
          startupProbe:
            httpGet:
              path: /healthz/live
              port: 8080
            failureThreshold: 30
            periodSeconds: 2
---
# service.yaml
apiVersion: v1
kind: Service
metadata:
  name: myapi-service
  namespace: production
spec:
  type: ClusterIP
  selector:
    app: myapi                    # routes to pods with this label
  ports:
    - port: 80                    # the service's port
      targetPort: 8080            # the container's port
```

The connective tissue to notice: the Deployment's `selector.matchLabels` and the pod template's `labels` must agree, and the Service's `selector` uses that same label to find the pods it should route to. **Labels are the glue** that binds these loosely-coupled objects together. The `envFrom` block injects every key from the ConfigMap and Secret as environment variables — and thanks to the `__` convention, they slot straight into .NET configuration.

### Health probes: liveness, readiness, startup

Kubernetes needs to know two different things about your app, and it uses two different probes:

- **Liveness probe** — "Is this container *alive*, or is it wedged?" If the liveness probe fails, Kubernetes **kills and restarts** the container. Use it to recover from deadlocks and unrecoverable hangs. Point it at a *cheap* endpoint that reflects only whether the process itself is functioning.
- **Readiness probe** — "Is this container ready to *serve traffic right now*?" If it fails, Kubernetes **removes the pod from the Service's load balancer** but does *not* restart it. Use it when the app is alive but temporarily can't serve — still warming up, or a dependency is briefly unavailable. When it recovers, traffic resumes.
- **Startup probe** — "Has the app *finished starting*?" Slow-booting apps need this. Until the startup probe succeeds, the liveness and readiness probes are suspended. This prevents a slow starter from being killed by an impatient liveness probe. Here `failureThreshold: 30 × periodSeconds: 2` grants up to 60 seconds to start before liveness takes over.

> **Pitfall:** Don't make your liveness probe check downstream dependencies like the database. If the database blips, every pod's liveness probe fails at once, Kubernetes restarts them *all* in a storm, and you turn a small outage into a cascading one. Dependency health belongs in the *readiness* probe (drain traffic), never in liveness (which kills). ASP.NET Core's health-check middleware supports separate `/healthz/live` and `/healthz/ready` endpoints for exactly this split.

### Resource requests and limits

- **`requests`** are what the pod is *guaranteed*. The scheduler uses requests to decide which node has room; a node won't accept a pod whose requests don't fit.
- **`limits`** are the *hard ceiling*. Exceed the memory limit and the kernel **OOM-kills** the container. Exceed the CPU limit and you get *throttled* (slowed), not killed.

> **Best practice:** Always set requests and limits. Without requests, the scheduler can overpack a node and starve your app. Without limits, one runaway pod can consume a whole node and take its neighbors down. Set memory `requests` and `limits` equal for predictable, guaranteed-QoS behavior; give CPU some headroom between request and limit since CPU is compressible.

### Horizontal Pod Autoscaler (HPA)

Fixed replica counts waste money at night and fall over at peak. The **HPA** automatically adjusts the number of replicas based on observed metrics — most commonly CPU utilization:

```yaml
# hpa.yaml
apiVersion: autoscaling/v2
kind: HorizontalPodAutoscaler
metadata:
  name: myapi-hpa
  namespace: production
spec:
  scaleTargetRef:
    apiVersion: apps/v1
    kind: Deployment
    name: myapi
  minReplicas: 3
  maxReplicas: 20
  metrics:
    - type: Resource
      resource:
        name: cpu
        target:
          type: Utilization
          averageUtilization: 70   # target 70% of the CPU *request*
```

The HPA watches average CPU across the pods and, when it drifts above 70% of each pod's CPU *request*, adds replicas (up to 20); when load drops, it scales back down (never below 3). Note that "70% utilization" is measured against the `requests` value — another reason setting requests correctly matters. The HPA depends on the **metrics-server** add-on being installed to supply those numbers.

@@SRC: old Chapter 11: Containers & Orchestration@@
## Helm and Kustomize: Managing Manifests at Scale

You've now got a stack of YAML: deployment, service, configmap, secret, HPA, ingress. Now multiply it by three environments (dev, staging, prod) that differ only in replica counts, image tags, and hostnames. Copy-pasting and hand-editing five files across three environments is a recipe for drift and mistakes. Two tools solve this differently.

**Helm** is the "package manager for Kubernetes." A **chart** is a bundle of *templated* manifests plus a `values.yaml` of defaults. The templates use placeholders; you override values per environment:

```yaml
# templates/deployment.yaml (excerpt)
spec:
  replicas: {{ .Values.replicaCount }}
  template:
    spec:
      containers:
        - name: myapi
          image: "{{ .Values.image.repository }}:{{ .Values.image.tag }}"
```

```yaml
# values-prod.yaml
replicaCount: 5
image:
  repository: myregistry.azurecr.io/myapi
  tag: "1.4.0"
```

Deploy with `helm install myapi ./mychart -f values-prod.yaml`. Helm tracks each install as a versioned **release**, so `helm rollback myapi` reverts an entire application to a prior state in one command. Helm's strength is templating and lifecycle management, and there are thousands of ready-made charts (Postgres, Redis, ingress controllers) you can install as dependencies.

**Kustomize** takes the opposite philosophy: no templating, no placeholders. You write plain, valid YAML as a **base**, then apply **overlays** that *patch* it per environment. It's built into `kubectl`:

```yaml
# overlays/prod/kustomization.yaml
resources:
  - ../../base
patches:
  - patch: |-
      - op: replace
        path: /spec/replicas
        value: 5
    target:
      kind: Deployment
      name: myapi
images:
  - name: myregistry.azurecr.io/myapi
    newTag: "1.4.0"
```

Apply with `kubectl apply -k overlays/prod`. The base stays untouched and valid on its own; the overlay layers changes on top.

> **Best practice:** Reach for **Kustomize** when environments differ by simple, structural tweaks (replica counts, tags, resource sizes) and you value plain readable YAML. Reach for **Helm** when you need real templating logic, want to distribute a packaged application for others to install, or need release/rollback tracking. Many teams use both — Helm to install third-party dependencies, Kustomize for their own apps.

@@SRC: old Chapter 11: Containers & Orchestration@@
## Essential kubectl Commands

`kubectl` is your primary interface to the cluster. The commands you'll use daily:

```bash
kubectl apply -f deployment.yaml        # create/update resources from a file
kubectl apply -k overlays/prod          # apply a kustomize overlay
kubectl get pods -n production          # list pods in a namespace
kubectl get pods -o wide                # ...with node and IP columns
kubectl describe pod myapi-abc123       # full detail + recent events (great for debugging)
kubectl logs myapi-abc123               # container logs
kubectl logs -f deploy/myapi            # follow logs across the deployment
kubectl exec -it myapi-abc123 -- sh     # shell into a container (if it has one)
kubectl rollout status deploy/myapi     # watch a rolling update progress
kubectl rollout undo deploy/myapi       # roll back to the previous revision
kubectl scale deploy/myapi --replicas=5 # imperative manual scale
kubectl port-forward svc/myapi-service 8080:80  # tunnel a service to localhost
kubectl get events --sort-by=.lastTimestamp     # recent cluster events
```

> **Best practice:** When a pod misbehaves, `kubectl describe pod` first — the **Events** section at the bottom usually names the problem (image pull failure, failed probe, insufficient resources, `CrashLoopBackOff`) before you ever reach for logs.

@@SRC: old Chapter 11: Containers & Orchestration@@
## Service Mesh: Awareness

As microservices multiply, cross-cutting networking concerns pile up: mutual TLS between every service, retries and timeouts, fine-grained traffic splitting for canary releases, and detailed request-level telemetry. Building all of that into each application — across multiple languages — is repetitive and inconsistent.

A **service mesh** (Istio, Linkerd) moves those concerns *out* of your app and into the infrastructure. It injects a **sidecar proxy** (typically Envoy) next to each pod; all traffic flows through these proxies, which a central control plane configures. The mesh then provides mutual TLS everywhere, automatic retries and circuit breaking, traffic-shifting for canaries and blue-green, and uniform observability — **without a single line of change in your .NET code.**

The trade-off is real complexity and per-pod resource overhead from all those proxies. **Linkerd** favors simplicity and low overhead; **Istio** is more powerful and more configurable at the cost of a steeper learning curve. You don't need a mesh for a handful of services — but as a system grows into dozens of services with strict security and traffic-management requirements, a mesh becomes compelling. For now, know what it is and when to reach for it.

@@SRC: old Chapter 11: Containers & Orchestration@@
## .NET Aspire: Orchestration for Local Development

Docker Compose is language-agnostic, which means it doesn't know anything about your .NET projects. **.NET Aspire** is Microsoft's opinionated stack for building and running cloud-native, multi-service .NET apps — and it dramatically improves the *inner-loop* (local development) experience. It is GA and versioned independently of the .NET release (Aspire 9.x), on its own cadence rather than tied to a specific .NET version.

You describe your application's topology in C#, in an **AppHost** project, rather than in YAML:

```csharp
// AppHost Program.cs
var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("db").AddDatabase("appdb");
var redis = builder.AddRedis("cache");

builder.AddProject<Projects.MyApi>("api")
       .WithReference(postgres)   // injects the connection string automatically
       .WithReference(redis);

builder.Build().Run();
```

Run it, and Aspire starts your projects, spins up Postgres and Redis in containers, **wires the connection strings into each project via service discovery automatically**, and opens a **dashboard** showing every service's logs, distributed traces, and metrics (OpenTelemetry is wired in out of the box). `WithReference` is doing what you'd otherwise hand-write as environment variables in Compose — but type-safe and discovered automatically.

Two clarifications that matter:

- Aspire is primarily a **development-time and composition** tool, plus a set of "components" (resilient, telemetry-instrumented client libraries for Redis, Postgres, service bus, and so on). It is *not* itself a production runtime.
- For production, Aspire can **generate deployment manifests** — it integrates with tools that publish to Kubernetes or Azure Container Apps. So the same C# app model that runs your inner loop also informs your deployment.

> **Best practice:** Use Aspire to make local multi-service development pleasant and observable, and to standardize resilient, instrumented client configuration across services. Still learn Kubernetes and its manifests — that's where your app ultimately runs, and Aspire complements that knowledge rather than replacing it.

> **Capstone tie-in:** This chapter is exercised by ShopCore Steps 3 (Dockerize) and 8 (Deploy with Infrastructure as Code) — you'd package the API with a multi-stage Dockerfile and Compose, then run the services on a managed Kubernetes cluster or cloud container service. See Chapter 32.

@@SRC: old Chapter 12: DevOps & CI/CD@@
## Azure Pipelines in Practice

If you work on .NET professionally there is a good chance the build you are asked to fix is an Azure Pipelines build, not a GitHub Actions one. The reasons are historical and structural: Azure DevOps predates Actions, Microsoft shipped first-class .NET tasks for it, and it grew the enterprise machinery—approvals, audited environments, Key Vault-backed variable groups, org-wide templates—that regulated shops need. The concepts you just learned all transfer. What changes is the vocabulary and, in one important place, the shape of the file.

### Coming from GitHub Actions: A Translation Table

| GitHub Actions | Azure Pipelines | Notes |
|---|---|---|
| Workflow (`.github/workflows/*.yml`) | Pipeline (`azure-pipelines.yml`) | One repo can have many pipelines; each is registered in the UI and points at a YAML file. |
| — (no equivalent) | **Stage** | A real grouping layer above jobs, with its own `dependsOn`, `condition`, and variables. |
| Job | Job | Same idea: a unit that gets one machine. |
| Step / action (`uses:`) | Step / task (`- task: X@1`) | Tasks are versioned by major number (`@2`), not by tag. `- script:` is the shell escape hatch. |
| Runner (`runs-on:`) | Agent (`pool:`) | `pool: { vmImage: ubuntu-latest }` for Microsoft-hosted, `pool: { name: my-pool }` for self-hosted. |
| `secrets.FOO` | Variable group, ideally Key Vault-linked | Groups are defined in Library and referenced by name; secret variables are masked and *not* exposed as env vars automatically. |
| `environment:` with protection rules | `environment:` used by a `deployment:` job | Carries approvals, business-hours gates, Azure Function/REST checks, and deployment history per resource. |
| Reusable workflow / composite action | Template (`extends:` / `template:`) | Templates take *typed* parameters and are expanded at queue time, not called at runtime. |
| `actions/cache` | `Cache@2` | Same restore-key semantics, different input names. |
| `actions/upload-artifact` | `PublishPipelineArtifact@1` | Downloaded with `- download:` or `DownloadPipelineArtifact@2`. |

The one structural difference worth internalizing is **stages**. GitHub Actions has jobs and nothing above them; a multi-environment release is expressed by convention, as a chain of `needs:` between jobs. Azure Pipelines makes that layer explicit:

```
pipeline
 └── stage: Build            ── dependsOn: []
      └── job: build          ── runs on one agent
           └── step / task    ── runs in the job's working directory
 └── stage: DeployStaging    ── dependsOn: Build,  environment gate
 └── stage: DeployProd       ── dependsOn: DeployStaging,  approval required
```

Because a stage is a first-class object, it can be re-run on its own, it has its own approval gates via environments, and the UI renders the release flow as a pipeline of boxes rather than a graph of jobs. That is why enterprise release flows—build once, promote through four environments, each with a different approver—land in Azure Pipelines rather than Actions.

### A Complete `azure-pipelines.yml` for a .NET Service

```yaml
trigger:
  branches:
    include: [ main, release/* ]
  paths:
    exclude: [ docs/*, README.md ]

pr:
  branches:
    include: [ main ]

variables:
  # A variable group defined in Library. Link it to Azure Key Vault and the
  # secret names in the vault become variables here, fetched at queue time.
  - group: order-api-secrets
  - name: buildConfiguration
    value: Release
  - name: NUGET_PACKAGES
    value: $(Pipeline.Workspace)/.nuget/packages
  - name: DOTNET_NOLOGO
    value: true

stages:
- stage: Build
  displayName: Build and test
  jobs:
  - job: build
    pool:
      vmImage: ubuntu-latest
    timeoutInMinutes: 30
    steps:
    - task: UseDotNet@2
      displayName: Install the SDK pinned in global.json
      inputs:
        packageType: sdk
        useGlobalJson: true

    - task: Cache@2
      displayName: Cache NuGet packages
      inputs:
        key: 'nuget | "$(Agent.OS)" | **/packages.lock.json'
        restoreKeys: |
          nuget | "$(Agent.OS)"
        path: $(NUGET_PACKAGES)

    - task: NuGetAuthenticate@1
      displayName: Authenticate to Azure Artifacts

    - script: dotnet restore --locked-mode
      displayName: Restore

    - script: dotnet build -c $(buildConfiguration) --no-restore
      displayName: Build

    - script: >
        dotnet test -c $(buildConfiguration) --no-build
        --logger trx --results-directory $(Agent.TempDirectory)/TestResults
        --collect:"XPlat Code Coverage"
      displayName: Test

    - task: PublishTestResults@2
      displayName: Publish test results
      condition: succeededOrFailed()      # publish even when tests failed
      inputs:
        testResultsFormat: VSTest
        testResultsFiles: '$(Agent.TempDirectory)/TestResults/**/*.trx'
        failTaskOnFailedTests: true

    - task: PublishCodeCoverageResults@2
      displayName: Publish code coverage
      condition: succeededOrFailed()
      inputs:
        summaryFileLocation: '$(Agent.TempDirectory)/TestResults/**/coverage.cobertura.xml'

    - script: >
        dotnet publish src/OrderApi/OrderApi.csproj
        -c $(buildConfiguration) --no-build
        -o $(Build.ArtifactStagingDirectory)/app
      displayName: Publish

    - task: PublishPipelineArtifact@1
      displayName: Publish pipeline artifact
      inputs:
        targetPath: $(Build.ArtifactStagingDirectory)/app
        artifactName: order-api

    # Named step + isOutput=true is what makes this readable from another stage.
    - script: echo "##vso[task.setvariable variable=version;isOutput=true]$(Build.BuildNumber)"
      name: meta
      displayName: Record the version being shipped

- stage: DeployStaging
  displayName: Deploy to staging
  dependsOn: Build
  condition: and(succeeded(), eq(variables['Build.SourceBranch'], 'refs/heads/main'))
  variables:
    # Runtime expression: only legal in a variables block or a condition.
    version: $[ stageDependencies.Build.build.outputs['meta.version'] ]
  jobs:
  - deployment: deployStaging
    environment: staging          # approvals and checks hang off this name
    pool:
      vmImage: ubuntu-latest
    strategy:
      runOnce:
        deploy:
          steps:
          - download: current
            artifact: order-api
          - task: AzureWebApp@1
            displayName: Deploy $(version) to App Service
            inputs:
              # Service connection using workload identity federation:
              # no client secret is stored anywhere.
              azureSubscription: sc-order-api-staging
              appName: order-api-staging
              package: $(Pipeline.Workspace)/order-api
```

A few things in there are not obvious.

**`UseDotNet@2` with `useGlobalJson: true`** installs exactly the SDK your repo pins in `global.json` instead of whatever happens to be baked into the agent image. Agent images are refreshed roughly every three weeks and SDKs come and go; pinning is the difference between a build that is reproducible and a build that breaks on a Tuesday for no reason you changed.

**`deployment:` instead of `job:`** is what unlocks environments. A `deployment` job records what version landed where, shows deployment history on the environment page, and honors that environment's approvals and checks—the pipeline literally pauses, mid-run, until an approver clicks. `runOnce` is the simplest strategy; `rolling` and `canary` also exist and map onto the deployment strategies discussed later in this chapter. A plain `job:` with an `environment:` key is not a thing; the gate only exists on deployment jobs.

**`condition: succeededOrFailed()`** on the two publish tasks matters because the default condition is `succeeded()`. Without it, a failing test run would skip result publishing and you would be left staring at a red build with no test report—the exact moment you most need one.

### Where Azure Pipelines Diverges

**`dependsOn` and `condition` interact in a way that bites people.** Every stage and job has an implicit `condition: succeeded()`. The moment you write your own `condition:`, you *replace* that default—you do not add to it. So `condition: eq(variables['Build.SourceBranch'], 'refs/heads/main')` on a deploy stage will happily deploy after a failed build. You almost always want `and(succeeded(), <your check>)`. Related: `succeeded()` is scoped to the things you depend on, `succeededOrFailed()` also runs after failure but not after cancellation, and `always()` runs even when the run is cancelled—use it only for cleanup that genuinely must happen. By default each stage depends on the one above it in the file; `dependsOn: []` breaks that and starts a stage immediately, which is how you fan out.

**Output variables are the classic "why is my variable empty".** Four conditions must all hold. The producing step needs a `name:`. The logging command needs `isOutput=true`. The consumer must use a *runtime* expression `$[ ... ]`, which is only evaluated in a `variables:` block or a `condition:`—dropping `$[ ... ]` inline into a script does nothing. And the consuming stage must actually depend on the producing stage, because the `stageDependencies` object only contains stages you declared a dependency on.

```yaml
# same job:        $(meta.version)
# different job:   $[ dependencies.build.outputs['meta.version'] ]
# different stage: $[ stageDependencies.Build.build.outputs['meta.version'] ]
```

> **Gotcha.** If the *producer* is a `deployment:` job, the key gains an extra segment for the lifecycle hook or resource: `stageDependencies.Deploy.deployStaging.outputs['deployStaging.meta.version']`. When a cross-stage variable comes back empty and everything looks right, add a temporary `- script: env` step and read the actual variable names the agent sees, rather than guessing at the nesting.

**Templates are expanded, not called.** A template is a YAML fragment pulled in at queue time; `- template: steps/build.yml` splices steps in place, while `extends:` makes your whole pipeline an instantiation of someone else's skeleton. Parameters are typed, which is the real advantage over Actions' stringly-typed inputs:

```yaml
# templates/dotnet-build.yml
parameters:
- name: projects
  type: string
  default: '**/*.csproj'
- name: configuration
  type: string
  default: Release
  values: [ Debug, Release ]      # rejected at queue time if violated
- name: runTests
  type: boolean
  default: true

steps:
- script: dotnet build ${{ parameters.projects }} -c ${{ parameters.configuration }}
- ${{ if eq(parameters.runTests, true) }}:
  - script: dotnet test -c ${{ parameters.configuration }} --no-build
```

```yaml
# azure-pipelines.yml
extends:
  template: templates/dotnet-build.yml@templates   # from a repository resource
  parameters:
    configuration: Release
```

Note `${{ }}`—compile-time expansion—versus `$[ ]` for runtime and `$( )` for simple macro substitution. Three sigils, three evaluation phases, and mixing them up is a large share of the confusing errors in this platform. `${{ }}` values are baked into the YAML before any agent starts, so they cannot see anything produced during the run.

> **Best practice.** Put the security-relevant scaffolding in a template that pipelines `extends`, and set a *required template check* on your protected environments and service connections. Because `extends` templates can constrain what steps a pipeline is allowed to run, this is the mechanism that stops a pull request from adding a step that exfiltrates a production credential.

**Caching only pays off if restore is deterministic.** `Cache@2` keys on the content of `packages.lock.json`. If you have no lock files, the key is unstable or too broad and you cache the wrong thing; if you have lock files but restore without `--locked-mode`, NuGet is still free to resolve different versions than the lock file records, and the cache silently stops corresponding to what you build. Lock files plus `--locked-mode` also turn "someone published a new patch version" from a mystery build failure into an explicit, reviewable diff.

**Private feeds need `NuGetAuthenticate@1`.** Azure Artifacts feeds are not anonymous. The task injects credentials for the build identity into the NuGet provider so a plain `dotnet restore` works; without it you get `NU1101` (package not found), because an unauthenticated feed returns nothing rather than a 401. If the feed lives in another organization, you also need a service connection and to name it in the task's `nuGetServiceConnections` input.

**Service connections are the credential boundary.** A service connection is a stored, permissioned identity that tasks use to talk to Azure, AWS, Docker registries, or Kubernetes. The old form stored a service-principal client secret that someone had to rotate. The modern form is **workload identity federation**: the connection is configured to trust tokens issued by your Azure DevOps organization for a specific service connection, so the agent exchanges a short-lived OIDC token for an Azure access token at run time and *no secret exists to leak or rotate*. Convert your Azure connections to workload identity federation; it removes an entire category of incident. This is the same reasoning as the managed-identity advice in *Secrets in Pipelines* below; the trust chain it rests on — and the trust-policy condition that is the whole security boundary — is worked through in the *Zero Trust and Workload Identity* section of [Chapter 14: Security](#chapter-14-security).

### Reading and Fixing the Build

Most of the time the build is not a design problem, it is a reading problem. A senior engineer diagnoses a red pipeline in two minutes; a junior scrolls for twenty.

**Find the first error, not the last.** This is the single highest-leverage habit. A failed `dotnet restore` leaves the packages folder incomplete, so the compile step then emits dozens of `CS0246: The type or namespace name 'X' could not be found` errors. Every one of those is noise. The web view drops you at the *end* of the log, which is precisely the wrong end. Collapse the tasks, find the first one with a red icon, and read its first `##[error]` line.

**Know the log markers.** Agents structure logs with logging commands: `##[error]` and `##[warning]` are what the UI turns red and yellow, `##[section]` starts a task, and `##[group]`/`##[endgroup]` fold a region. The task list on the left of the run view is the index—each entry is one task, with its own duration and exit code. A task that took 0 seconds and is grey was *skipped* by its condition, not run and passed; that distinction explains a lot of "but I published the artifact" confusion.

**Turn on debug logging.** Queue the pipeline with the variable `system.debug` set to `true` (the "Variables" box in the Run pipeline dialog). You then get `##[debug]` lines showing every variable's resolved value, the exact command line each task executed, condition evaluation results, and file-matching decisions for glob patterns. When a `testResultsFiles` pattern matches nothing, this is how you see the directory the task actually looked in.

**Download the raw logs.** The web view truncates long output and struggles past a few megabytes. "Download logs" on the run gives you a zip with one text file per task—grep-able, complete, and the only reliable way to read a 200 MB log from a chatty MSBuild run at `/v:diag`.

**Re-run only what failed.** Use "Rerun failed jobs" rather than re-queueing the whole pipeline. It reuses the successful stages, which both saves minutes and preserves the evidence you were looking at. For genuinely flaky infrastructure this is the right first move; for a flaky *test* it is a way of hiding a real bug, so pair it with a note.

**Reproduce locally with the same SDK.** Read `global.json`, install that exact SDK, then run the same commands the pipeline ran—copy them out of the log rather than approximating. Two differences remain: the agent starts from a clean checkout (so `git clean -xdf` locally before you claim it reproduces), and the agent is Linux while you may be on Windows or macOS, which changes path casing, file-name length limits, and line endings.

### Common .NET Pipeline Failures

| Symptom | Cause | Fix |
|---|---|---|
| `NU1101: Unable to find package X` | The feed hosting it is missing from `nuget.config`, or the agent is not authenticated so the feed returns an empty result | Add the feed; add `NuGetAuthenticate@1` before restore; check the build identity has Reader on the feed |
| `NU1605: Detected package downgrade` | A transitive dependency demands a higher version than a direct `PackageReference` pins | Raise the direct reference to at least the transitive requirement, or centralize versions with `Directory.Packages.props` |
| `MSB3277: conflicts between different versions of the same assembly` | Two packages bind to different major versions of one assembly | Read the `/v:detailed` output for the winning version, unify via CPM, and only reach for `binding redirects`/`AutoGenerateBindingRedirects` on .NET Framework targets |
| `A compatible .NET SDK was not found` / `global.json` mismatch | The pinned SDK is not on the agent image | `UseDotNet@2` with `useGlobalJson: true`; or add `rollForward: latestFeature` to `global.json` |
| `The active test run was aborted` | The test host process crashed—stack overflow from recursion, a `AccessViolation` in a native dependency, or `Environment.Exit` in a test | Re-run with `--blame-crash --blame-hang-timeout 5m`; the resulting sequence file names the test that killed the host |
| Testcontainers tests fail with "Cannot connect to the Docker daemon" | The job is on a `windows-latest` agent, which has no Linux Docker daemon for Linux containers | Move the integration-test job to `ubuntu-latest`, or use a self-hosted agent with Docker configured—see [Chapter 7: Testing](#chapter-7-testing) |
| `No space left on device` mid-build | Microsoft-hosted agents give you ~10 GB total; layered Docker builds, NuGet caches, and coverage output eat it fast | Prune between steps (`docker system prune -af`), avoid `--self-contained` publishes you do not need, or move to a self-hosted agent |
| `The job running on agent ... exceeded the maximum time of 60 minutes` | The free tier caps a private-project job at 60 minutes regardless of `timeoutInMinutes` | Split the work into parallel jobs, cache aggressively, or buy a parallel job (which raises the cap to 360 minutes) |

> **Pitfall.** `timeoutInMinutes: 120` on a Microsoft-hosted free-tier job does nothing. The platform limit wins, and the job dies at 60 minutes with a message that looks like a configuration error but is a billing one. Splitting a long test suite across two jobs is usually cheaper than the license.

### Azure Pipelines or GitHub Actions?

Both are mature and both will build .NET well; the honest answer depends on where your code and your governance live.

| Choose Azure Pipelines when | Choose GitHub Actions when |
|---|---|
| Your source is in Azure Repos, or your work items and releases are tracked in Azure Boards | Your source is on GitHub and you want PR checks, releases, and code review in one place |
| You need staged promotion with per-environment approvers, audit trails, and required-template checks | Your deployment flow is simple enough to express as a chain of jobs |
| You want an org-wide template that pipelines must `extends`, enforced centrally | You want to assemble a pipeline quickly from Marketplace actions |
| You need self-hosted agents inside a corporate network, or Windows agents with specific tooling | You are fine on hosted runners, or already run Actions runners |
| Compliance requires a named approval record per production deployment | Environment protection rules are sufficient |

The pragmatic middle ground is common and works well: keep the code and pull-request checks on GitHub Actions, where developers already live, and let Azure Pipelines own the deployment stages where the approvals and audit trail matter. Both can consume the same immutable artifact from the same registry—which is the point of building once and promoting, and the reason the choice is less consequential than it feels.

@@SRC: old Chapter 12: DevOps & CI/CD@@
## NuGet in Depth

NuGet is .NET's package manager. As a senior engineer you should be comfortable on both sides: consuming packages and producing them.

**Consuming.** `PackageReference` items in a `.csproj` declare dependencies. `dotnet restore` reads them, resolves the dependency graph, and downloads packages into the global cache. Adding a lock file (`RestorePackagesWithLockFile` true) produces `packages.lock.json`, pinning the exact resolved versions so CI restores are deterministic and reproducible—which also makes your cache keys stable.

**Creating and publishing.** For a library, `dotnet pack` produces a `.nupkg`. Package metadata lives in the `.csproj`:

```xml
<PropertyGroup>
  <PackageId>Contoso.Ordering.Client</PackageId>
  <Version>2.3.1</Version>
  <Authors>Contoso Platform Team</Authors>
  <Description>Typed client for the Ordering API.</Description>
  <PackageLicenseExpression>MIT</PackageLicenseExpression>
</PropertyGroup>
```

Then push it to a feed:

```bash
dotnet nuget push ./nupkgs/Contoso.Ordering.Client.2.3.1.nupkg \
  --api-key $NUGET_API_KEY \
  --source https://api.nuget.org/v3/index.json
```

**Private feeds.** Internal libraries usually should not go to the public nuget.org. Private feeds—Azure Artifacts, GitHub Packages, MyGet, or a self-hosted feed—host them for your organization. A `nuget.config` at the repo root points restore at the right feeds:

```xml
<configuration>
  <packageSources>
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
    <add key="contoso" value="https://pkgs.dev.azure.com/contoso/_packaging/internal/nuget/v3/index.json" />
  </packageSources>
</configuration>
```

> **Pitfall — dependency confusion:** If an internal package name also exists on the public feed, a misconfigured restore can pull the *public* one, which an attacker may have planted. Defend against this by using upstream sources correctly and by reserving your package name prefixes (package ID prefix reservation) on public feeds.

@@SRC: old Chapter 12: DevOps & CI/CD@@
## Deployment Strategies

Getting a validated artifact built is only half the job; *how* you replace the running version determines whether users notice.

**Rolling deployment** replaces instances gradually. In a fleet of ten servers you update two at a time, letting the new version take traffic while the rest still run the old version, until all are updated. It needs no extra capacity but means both versions serve traffic simultaneously for a while—your app and database must tolerate that.

**Blue-green deployment** runs two complete environments: *blue* (current, live) and *green* (new). You deploy to green, test it in isolation, then flip the router so all traffic goes to green in one atomic switch. Rollback is instant—flip back to blue. The cost is running double the infrastructure during the transition.

**Canary deployment** releases the new version to a small slice of traffic first—say 5%—while watching error rates, latency, and business metrics. If the canary stays healthy you progressively increase the share to 25%, 50%, 100%. If it misbehaves you route everyone back to the stable version, having exposed only a fraction of users to the problem. Canaries pair naturally with feature flags and good observability.

> **Best practice:** Whatever strategy you choose, make **rollback cheaper and faster than fixing forward**. The measure of a mature deployment process is not that failures never happen, but that a bad release can be reverted in seconds without a heroic effort.

**Artifact management** underpins all of this. Build an artifact *once*—a container image, a NuGet package, a published zip—store it in a registry or artifact feed, and promote that *same* immutable artifact through environments (dev → staging → production). Tag it by commit SHA or SemVer so you know exactly what is running. Rebuilding per environment reintroduces the risk that staging and production differ.

@@SRC: old Chapter 12: DevOps & CI/CD@@
## Feature Flags

Trunk-based development and continuous deployment rely on decoupling *deploy* from *release*. You merge and deploy incomplete or risky code, but keep it dark behind a **feature flag** until you deliberately turn it on—for everyone, or for a chosen cohort.

.NET has first-class support via **`Microsoft.FeatureManagement`**:

```csharp
// Registration
builder.Services.AddFeatureManagement();

// Usage
public class CheckoutController(IFeatureManager features) : ControllerBase
{
    public async Task<IActionResult> Checkout()
    {
        if (await features.IsEnabledAsync("NewPricingEngine"))
            return Ok(await _newPricing.QuoteAsync());

        return Ok(await _legacyPricing.QuoteAsync());
    }
}
```

Flags are configured externally—`appsettings.json`, Azure App Configuration, or a dedicated platform like **LaunchDarkly**, which adds targeting rules (enable for internal users, or 10% of traffic), audit trails, and instant kill switches without a redeploy. This is precisely the mechanism that lets a canary rollout turn a feature on for a small percentage and dial it up.

> **Pitfall:** Feature flags are debt if you never remove them. A codebase littered with stale flags becomes an unreadable maze of dead branches. Track flags and delete both the flag and the losing code path once a feature is fully rolled out and stable.

@@SRC: old Chapter 12: DevOps & CI/CD@@
## Platform Engineering and Measuring Delivery

Everything so far in this chapter is machinery: pipelines, artifacts, gates, secrets. This section is about the two questions that sit above the machinery and get asked of senior engineers rather than of pipelines — **who builds and owns this for everyone?** and **how do we know any of it is working?**

### The problem platform engineering exists to solve

"You build it, you run it" was a corrective to a real dysfunction: developers throwing code over a wall at an operations team who had no context and no way to say no. It worked. Then it kept going, and the accumulated result is a backend developer who is also expected to be fluent in Terraform, Kubernetes, Helm, a service mesh, three observability products, an IaC linter, a secrets manager, two cloud IAM models, and the CI DSL of the week — while shipping features.

That is a **cognitive load** problem, and it does not resolve by hiring more senior people. Past a certain organizational size, every team independently solving the same infrastructure problems produces twelve slightly different, slightly wrong solutions, and the cost is paid forever in maintenance and incidents.

**Platform engineering** is the response: a small team builds and operates an internal product whose customers are the other engineers. The word *product* is load-bearing — it implies users you can talk to, adoption you have to earn, a roadmap driven by demand, and the possibility of building the wrong thing.

| | DevOps (the practice) | SRE | Platform engineering |
|---|---|---|---|
| Core idea | Dev and ops share ownership | Reliability as an engineering discipline | Infrastructure capability as an internal product |
| Primary output | Culture, automation, feedback loops | SLOs, error budgets, toil reduction | Golden paths, self-service tooling |
| Fails when | It becomes a job title for one team | Error budgets are advisory only | The platform team becomes a ticket queue |

They are complements, not alternatives. SRE gives you the reliability vocabulary (Chapter 13); platform engineering gives you the leverage to apply it consistently.

### Golden paths, and why paved beats gated

A **golden path** is the supported, opinionated way to do a common thing: create a service, add a database, expose an endpoint, ship to production. It is not the *only* way — that distinction matters enormously — it is the way that is already solved.

A good golden path for a new .NET service delivers, from one command, a repository with the company's project layout and analyzer settings, a working CI pipeline, containerization, health checks and OpenTelemetry wired up, an entry in the service catalog, a dashboard, an on-call rotation, and a deployment to a dev environment. What used to take a competent engineer two weeks of copying from a neighbouring repo takes an afternoon, and — the real prize — the twentieth service is configured the same way as the first.

The design principle that decides whether this succeeds:

> **Best practice — pave, don't gate.** Make the supported path so obviously easier than the alternatives that people choose it. The moment the platform's primary mechanism is *refusing* things, engineers route around it, and you have built a bureaucracy that also has an on-call rotation.

That does not mean no guardrails. It means guardrails should be *defaults* rather than *approvals*: the template already has the right IAM scope, the base image is already hardened, the pipeline already runs the security gates. Reserve hard blocks (admission control, required checks) for the small set of things that genuinely must never happen — an unsigned image reaching production, a secret in a commit — and let everything else be a default that a team can deviate from with a written reason.

**Golden paths rot.** A template generated a year ago is a snapshot; a hundred services generated from it drift into a hundred variations. Budget for propagating changes — a tool that can re-apply template updates to existing repositories and open PRs — or accept that your golden path describes only new services, which is a much smaller benefit than it looked.

### Service catalogs and ownership

The most valuable thing an internal platform holds is not the tooling — it is the answer to *"who owns this?"*. Every organization past about thirty services has some component that nobody can confidently claim, and it is invariably load-bearing.

**Backstage** (the CNCF project originating at Spotify) is the common open-source implementation, and it is a big commitment — a Node application your team maintains, with plugins to build. Several commercial alternatives exist. Before adopting any of them, be clear about what makes a catalog useful, because it is not the software:

- Ownership is **current** — enforced by CI (a `CODEOWNERS` or catalog entry required for the pipeline to run), not maintained by goodwill.
- It is **generated** from things that are already true (repositories, deployments, dashboards) rather than typed in by hand.
- People actually **land in it** during real work — from an alert, from a dependency graph, from a "who do I ask about this" moment.

A catalog nobody consults because its data is nine months stale is worse than none, because it answers questions confidently and wrongly.

### DORA: four metrics, and exactly how each is gamed

The DORA research programme identified four measures that correlate with software delivery performance. They are the industry's common language, and knowing how each one breaks is more useful than knowing the definitions.

| Metric | What it measures | How it gets gamed |
|---|---|---|
| **Deployment frequency** | How often you release to production | Deploy the same artifact repeatedly; count no-op deploys; redefine "deployment" |
| **Lead time for changes** | Commit → running in production | Start the clock at PR-open rather than first commit, hiding the weeks of work before it |
| **Change failure rate** | Share of deployments causing degradation | Reclassify incidents as "planned maintenance"; raise the bar for what counts as a failure |
| **Failed deployment recovery time** | How long to restore service | Close incidents when mitigated rather than resolved; split one incident into several short ones |

Two structural warnings.

**They are throughput and stability, not value.** A team can hit elite numbers on all four while shipping features nobody uses. DORA measures how well your delivery machine runs, not whether it is pointed anywhere useful. It was never intended as a proxy for value, and using it that way is the most common misreading.

**They stop measuring the moment they become targets.** This is Goodhart's law and it is not avoidable by choosing better metrics. The mitigation is to use them as a *team's own diagnostic*, trended over time, discussed in retrospectives — and specifically **not** to compare teams against each other or attach them to performance reviews. The instant lead time appears on a manager's dashboard next to individual names, you are measuring reporting behaviour.

> **Gotcha.** Change failure rate and deployment frequency are a *pair*. Improving one at the expense of the other is not improvement, and looking at either alone rewards exactly the wrong behaviour — either reckless shipping or paralysis. Read them together, always.

### SPACE: the corrective

SPACE was proposed by researchers (including some of the DORA authors) precisely because single-dimension metrics distort. It says productivity is multidimensional and you should sample across five dimensions rather than optimize one:

- **S**atisfaction and well-being — how do developers feel about their tools and work? Burnout precedes attrition, which destroys delivery.
- **P**erformance — outcomes: did the change work, is quality holding?
- **A**ctivity — counts of things done. Necessary but the most misleading alone.
- **C**ommunication and collaboration — review latency, discoverability, how knowledge moves.
- **E**fficiency and flow — uninterrupted time, wait states, handoffs.

The practical guidance: pick **at least three dimensions, including at least one from a survey**, and never report activity alone. Developer surveys are not soft data here — they are frequently the only instrument that detects the thing actually blocking a team, and DORA's own research consistently finds the biggest constraints are organizational rather than technical.

### Measuring whether AI assistance is helping

This is the live version of the measurement problem, and it is where the discipline above earns its keep. The evidence is genuinely mixed — including a 2025 randomized trial in which experienced developers working on codebases they knew well were *slower* with AI assistance while believing they had been substantially faster. Perceived speed is not evidence.

The mechanics of measuring it honestly — which metrics mislead (lines of code, percentage AI-generated, PR count), which help (cycle time paired with change failure rate, review latency as the leading indicator, token spend per merged PR), and why the answer varies with codebase familiarity — are worked through in [Chapter 18](#chapter-18-the-ai-native-developer-thriving-in-the-ai-era). The point to carry here is structural: **AI assistance moves the bottleneck from writing to reviewing**, and if your delivery metrics show PRs arriving faster while review latency climbs, you have not increased throughput. You have grown a queue.

### Feedback loop time is a first-class engineering problem

The least glamorous, highest-return thing a platform team can do is make the loop shorter. A developer waiting 25 minutes for CI does not wait — they context-switch, and the cost of that switch dwarfs the CI time itself. A suite slow enough to discourage running it locally is a suite that stops catching things.

Where the time usually goes, in rough order of payoff:

- **Cache what is deterministic.** NuGet restore keyed on `packages.lock.json` (see *Caching only pays off if restore is deterministic*, above), Docker layers ordered so source changes don't invalidate dependency layers, and the build output itself.
- **Parallelize.** Independent jobs should not be sequential stages. xUnit runs test collections in parallel by default; check you haven't disabled it with a shared fixture.
- **Run the right subset on the right trigger.** Unit tests on every push; integration and E2E on PR; the full matrix nightly. Affected-project selection (from the changed paths) is a large win in a solution with many projects.
- **Right-size the runner.** A build that is CPU-bound on a two-core runner is an easy purchase decision — engineer-hours cost more than compute.
- **Measure it.** Track p50 and p95 pipeline duration as a metric your team actually looks at, the same way you'd track service latency. Slow CI degrades continuously and silently until someone charts it.

**Monorepo or many repos** shapes all of this. A monorepo gives atomic cross-service changes, one dependency version, and trivially consistent tooling, at the cost of needing affected-target selection and good ownership boundaries to stay fast. Many repositories give independence and simple CI at the cost of coordinating changes that cross boundaries, and of versioning your internal libraries as if they were public. Both work at scale; what does not work is a monorepo without build-graph tooling, or polyrepo without a way to propagate a change across forty repositories. Pick the failure mode you can afford to engineer around.

@@SRC: old Chapter 20: Networking & Web Fundamentals@@
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

@@SRC: old Chapter 20: Networking & Web Fundamentals@@
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

> **Best practice:** Combine timeouts, retries (with **exponential backoff and jitter** so retries don't stampede in lockstep), and **circuit breakers** (stop hammering a failing dependency) — the resilience trio. In .NET, `Microsoft.Extensions.Http.Resilience` (built on Polly) wires all three into `IHttpClientFactory` declaratively; Chapter 21 builds the full pipeline and explains how the strategies layer.

@@SRC: old Chapter 20: Networking & Web Fundamentals@@
## Abuse, Bots, and Traffic You Did Not Ask For

The rate limiter in the previous section is configured for a *cooperative* world: a well-meaning client with a runaway retry loop, a mobile app polling too eagerly, a partner integration that misread the docs. Set a limit, return `429`, they back off, everyone is happy.

This section is about the other case, where the client on the other end does not want to back off, controls more IP addresses than you do, and is reading your responses to work out what your limits are. The controls look superficially similar and the design thinking is completely different.

### The traffic mix has changed

If you have not looked at your logs recently, the composition may surprise you: across the public web, automated traffic is now roughly half of all requests, and a growing share of it is AI-related — crawlers building training corpora, retrieval bots fetching pages on behalf of a user's question, and agents browsing on someone's behalf. Sites hosting documentation, product catalogs, or any substantial body of text routinely see these bots requesting every page, repeatedly, ignoring the caching semantics a browser would respect.

The result is a genuinely new operational problem: **a capacity and cost event that is not an attack**. Nobody is trying to hurt you. Your origin is being hammered, your egress bill is up, your database is serving cache-missing queries for pages no human has read in a year, and there is no malice to point at.

**`robots.txt` is a request, not a control.** It is a convention that well-behaved crawlers honour voluntarily. Compliance among AI-related crawlers is inconsistent — some respect it, some respect it only for the crawler you have named and not for their retrieval fetcher, and some ignore it. Publishing a `robots.txt` is worth doing and settles nothing.

**User-agent blocking is barely better.** The user agent is a string the client chooses. It is useful for *identifying cooperative* bots, and useless against anything that would rather not be identified.

What actually works, in order of robustness:

- **Verified identity for the crawlers that support it.** The major crawlers publish either IP ranges or a reverse-DNS verification procedure (resolve the client IP to a hostname, confirm it is in their domain, resolve that hostname back). This lets you *allow* the ones you want — search engines you benefit from — with confidence, and treat everything claiming to be them without proof as unidentified.
- **Behavioural signals**, which are hard to fake because they are properties of the traffic rather than claims about it: request rate per source, breadth of URL space touched (a human reads a handful of pages; a crawler walks your sitemap), absence of asset requests (bots fetch HTML and skip the CSS, fonts and images a browser would), session shape, and cache-header indifference.
- **Cost asymmetry.** Make the expensive things cheap for you and expensive for them: serve aggressively cached, CDN-fronted responses to unidentified clients so the origin is never touched, and reserve dynamic, database-backed rendering for authenticated sessions.
- **Proof-of-work or challenge interstitials** for unidentified clients, which invert the economics — a challenge costs a real user a moment and costs a scraper CPU time per page across millions of pages.

> **Best practice.** Decide your *policy* before you reach for tooling: which bots do you want (search engines that send you traffic), which are you indifferent to, and which are pure cost? Then implement the policy at the CDN, not in your application. An origin that never sees the request is the only origin that scales.

### DDoS, by layer

"We got DDoS'd" describes two quite different events, and the distinction determines who can do anything about it.

**Volumetric / protocol attacks (L3–L4)** aim to saturate your bandwidth or exhaust connection state: UDP floods, SYN floods, amplification via DNS or NTP reflectors. The defining property is that the traffic never reaches your application, and often never reaches your network at all — the pipe fills first. **You cannot mitigate this in your code.** It is absorbed upstream, by your provider's scrubbing capacity (AWS Shield, Azure DDoS Protection, Cloudflare and similar). Your engineering job is done in advance: be behind such a service, know whether the tier you are on includes what you think it does, and know who to call.

**Application-layer attacks (L7)** send requests that look legitimate but are chosen to be expensive: your search endpoint with pathological queries, your report generator, your login endpoint, a URL pattern that misses every cache. Volume can be modest — a few thousand requests per second of the *right* requests will fall over a service that handles a hundred thousand of the wrong ones. This one *is* yours, and it is where the rest of this section lives.

> **Gotcha.** The most common self-inflicted L7 amplifier is a cache key that includes something the client controls freely — a tracking query parameter, a random cache-buster, a header you varied on. Every request becomes a miss, and your CDN faithfully forwards all of it to your origin. Normalize cache keys, and strip unknown query parameters at the edge.

### Rate limiting against someone who is trying

Three design decisions separate a limiter that inconveniences an attacker from one that merely inconveniences your users.

**What you key on decides everything.** The choice is a trade between how easily an attacker escapes it and how much collateral damage it does:

| Key | Attacker escapes by | Collateral damage |
|---|---|---|
| IP address | Using a botnet, a proxy pool, or IPv6 (where a single customer may hold a /64 — billions of addresses) | High: corporate NAT, university networks, and mobile carrier CGNAT put thousands of real users behind one IP |
| API key / account | Registering more accounts | Low, but only covers authenticated traffic |
| Tenant | — | Low; the right unit for a B2B product, and the one that protects tenants from each other |
| Device or session fingerprint | Clearing state (cheap) | Moderate |

The practical answer is layered: a generous IP-based limit as a blunt backstop, a real per-account or per-tenant limit as the meaningful control, and — critically — for unauthenticated endpoints, an IPv6 limit applied to the **/64 prefix** rather than the individual address. Limiting per IPv6 address is close to no limit at all.

**Where the counter lives decides whether it works.** ASP.NET Core's built-in rate limiter holds its state **in the process**. With ten replicas behind a load balancer, a "100 requests per minute" policy is really up to 1,000 per minute, and it resets whenever a pod is recycled — which an attacker with any patience will discover. In-process limiting is a fine *self-protection* mechanism (it stops one instance from being overwhelmed) but it is not a system-wide policy. For that you need a shared counter — Redis, or the limiter your API gateway/CDN provides — and the further out you push it, the less of the attack reaches anything you pay for.

```
  attacker ──► [ CDN / WAF ]  ← cheapest place to say no; attack never costs you
                    │
                    ▼
              [ API gateway ]  ← shared counters, per-key policy
                    │
                    ▼
              [ your service ] ← in-process limiter as self-protection only
```

**The algorithm should match the shape of legitimate use.** Fixed windows are the simplest and have a boundary flaw an attacker will find — a client can send a full window's allowance at 11:59:59 and again at 12:00:00, doubling the intended rate at the seam. Sliding windows fix that at the cost of more state. Token buckets are usually the right default for APIs because real clients are bursty: a burst allowance that refills steadily accommodates a page load firing twelve requests at once without permitting a sustained flood. And **concurrency limits** are the underrated one — for expensive endpoints, "at most N of these running at a time" protects the resource far better than a rate does, because it bounds the actual thing that runs out.

> **Pitfall.** Do not leak your limits in the failure path. A `429` is fine and correct. A `429` whose body explains the exact policy, plus headers counting down remaining quota, hands an attacker the tuning parameters for their script. Publish limits in your documentation for legitimate integrators; don't narrate them per-request to unauthenticated clients.

### Credential stuffing and account takeover

Someone else's breach is your incident. Attackers take a leaked email/password corpus and replay it against your login endpoint, relying on password reuse; a success rate of a fraction of a percent across millions of attempts is a profitable afternoon.

What distinguishes it from a brute-force attack is the shape: **one or two attempts per account, across an enormous number of accounts**, from many source addresses. Per-account lockout — the classic defence — barely registers against it, because no account is attacked twice.

Defences that match the actual shape:

- **Check passwords against breach corpora** at registration and at password change (the Have I Been Pwned range API does this without you ever sending a password — you send the first five characters of the SHA-1 hash and search the returned suffixes locally). This removes the attack's entire premise for your users.
- **Passkeys / WebAuthn**, which have no shared secret to stuff (Chapter 14). This is the real fix, and it is now practical.
- **Rate limit on the global failure rate for the endpoint**, not just per account: a sudden jump in the ratio of failed to successful logins is the signal, and it is visible even when every individual account looks quiet.
- **Risk-based friction** — a challenge or a second factor when the request comes from an unfamiliar device, an unusual geography, or a source already failing elsewhere — rather than uniform friction that trains users to click through.

> **Gotcha — lockout is a denial-of-service vector.** "Five failed attempts locks the account" means anyone who knows a user's email can lock them out at will. If you must lock, lock the *attempt source* rather than the account, use exponential backoff rather than a hard block, and make sure your recovery flow is not itself the easier attack.

### Denial of wallet

Elastic infrastructure changed the objective. Against a fixed-capacity server, an attacker's win is making it fall over. Against an autoscaling one, the service stays up and *you pay for the attack*. Nothing alerts, because nothing is broken — the graph you would notice is on a finance dashboard nobody watches hourly.

The endpoints that make this profitable are the ones where a small request buys a large amount of work:

- **LLM endpoints**, where one crafted request can trigger a long retrieval, a large context, and a multi-step agent loop — dollars per request, and the reason Chapter 19 treats unbounded consumption as a first-class risk.
- **Search and report generation**, where a pathological query scans everything.
- **Export and download**, which converts directly into egress charges.
- **Image and document processing**, where a small upload becomes minutes of CPU.
- **Anything that fans out** to paid third-party APIs on your account.

The defences are unremarkable and must exist before launch rather than after the invoice: hard per-user and per-tenant quotas on expensive operations specifically (your global API rate limit is not sized for them), a bounded cost budget per request, request-size and complexity limits (including query depth if you expose GraphQL), and **alerting on rate of spend rather than absolute spend** — a monthly budget alarm tells you about last night four weeks late. Chapter 28 covers the cost-management side.

### Shed load deliberately

When capacity does run out — from attack, from a launch, from a dependency slowing down — the difference between a bad hour and an outage is whether you chose what to drop.

The default behaviour is the worst one: every request is accepted, every request queues, every request times out, and nobody is served while all the work is done anyway. Under overload, **rejecting early is a service, not a failure.** Return `429` or `503` with `Retry-After` promptly rather than accepting work you cannot finish.

Then choose your priority order in advance, because you will not design it well at 3 a.m.: authenticated over anonymous, paying tenants over free, checkout over browsing, writes over analytics. Wire it as a queue policy or a concurrency limiter per class of traffic, and — this is the part teams miss — **load-test the degraded path**. A graceful degradation nobody has exercised is a hypothesis. Chapter 21's material on failure injection is how you turn it into a fact.

@@SRC: old Chapter 31: Linux & the Command Line for .NET Developers@@
## systemd: Running a .NET App as a Service

When you deploy to a plain Linux VM instead of a container, you need your app to start on boot, restart on crash, and log properly. **systemd** is the init system that manages this. You describe your service in a **unit file** under `/etc/systemd/system/`.

```ini
# /etc/systemd/system/myapp.service
[Unit]
Description=My ASP.NET Core App
After=network.target

[Service]
WorkingDirectory=/var/www/myapp
ExecStart=/usr/bin/dotnet /var/www/myapp/MyApp.dll
Restart=always
RestartSec=10
User=www-data
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=ASPNETCORE_URLS=http://localhost:5000

[Install]
WantedBy=multi-user.target
```

Then manage it:

```bash
sudo systemctl daemon-reload        # tell systemd to re-read unit files
sudo systemctl enable myapp         # start automatically on boot
sudo systemctl start myapp          # start it now
sudo systemctl status myapp         # is it running? recent log lines
sudo systemctl restart myapp        # restart after a deploy
```

`Restart=always` gives you crash recovery; `User=www-data` runs the app unprivileged. Note that systemd sends **SIGTERM** on `stop`, so the same graceful-shutdown handling from containers applies here.

**Container vs. service:** in a container you don't use systemd — the container runtime *is* your process supervisor, and your app is PID 1. Use systemd for traditional VM deployments; use the container orchestrator's restart policy for containerized ones. Don't try to run systemd inside a container.

@@SRC: old Chapter 31: Linux & the Command Line for .NET Developers@@
## Shell Scripting for Automation

A shell script bundles commands into a reusable file. You'll write these for deploys, health checks, and CI glue. Here's an annotated deploy-and-verify script:

```bash
#!/usr/bin/env bash
# The line above (shebang) tells the OS to run this with bash.

set -euo pipefail
# -e  : exit immediately if any command fails
# -u  : error on use of an unset variable (catches typos)
# -o pipefail : a pipeline fails if ANY stage fails, not just the last

APP_DIR="/var/www/myapp"
HEALTH_URL="http://localhost:5000/health"

echo "Building..."
dotnet publish -c Release -o "$APP_DIR"

echo "Restarting service..."
sudo systemctl restart myapp

echo "Waiting for health check..."
for i in {1..10}; do
  if curl -sf "$HEALTH_URL" > /dev/null; then
    echo "App is healthy."
    exit 0
  fi
  echo "  attempt $i failed, retrying in 3s..."
  sleep 3
done

echo "App failed to become healthy." >&2
exit 1
```

Key ideas: the **shebang** picks the interpreter; **`set -euo pipefail`** is the single most important line for robust scripts (fail fast, fail loud); `$VAR` reads variables; the `for` loop with `curl -sf` (silent, fail-on-error) polls the health endpoint; a non-zero `exit` tells CI the deploy failed.

> **Best practice:** Start every non-trivial script with `set -euo pipefail`. Without it, a failed command in the middle is silently ignored and the script marches on, often making things worse. This one line turns sloppy scripts into safe ones.
