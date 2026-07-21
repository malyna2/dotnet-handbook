# Chapter 11: Containers & Orchestration

_⏱️ Estimated read time: ~35 min ·     5283 words (study pace)_

For most of computing history, "it works on my machine" was a punchline and a genuine source of pain. You'd build software against a particular version of the .NET runtime, a specific OpenSSL, a certain timezone database, and a filesystem laid out just so — and then ship it to a server that differed in a dozen invisible ways. Containers are the industry's collective answer to that problem: package the application *together with* everything it needs to run, then run that package identically everywhere.

This chapter takes you from the operating-system primitives that make containers possible, through Docker and the art of building lean .NET images, into Kubernetes and the machinery of running containers at scale. By the end you should be able to containerize a .NET service, wire up a local multi-service stack, and read (and write) the Kubernetes manifests that run it in production.

## What a Container Actually Is

The single most common misconception is that a container is a lightweight virtual machine. It is not, and understanding the difference is the foundation for everything else.

A **virtual machine** virtualizes *hardware*. A hypervisor (VMware, Hyper-V, KVM) presents fake CPUs, fake network cards, and fake disks to a **complete guest operating system**, kernel and all. If you run five VMs on a host, you are running five separate kernels, each consuming hundreds of megabytes of RAM before your application starts, each booting for tens of seconds.

A **container** virtualizes the *operating system*. There is no guest kernel. Every container on a host shares the **host's single Linux kernel**. What makes a container feel isolated — its own process list, its own network interfaces, its own root filesystem — is a set of kernel features, not a separate machine.

> **Analogy:** A VM is a detached house — its own foundation, plumbing, and electrical panel. A container is an apartment in a building: it has its own locked front door and feels private, but it shares the building's foundation, water main, and structure (the kernel) with every other apartment. Apartments are cheaper to build and faster to move into, but they all depend on the same building holding up.

### The two kernel features that make it work

**Namespaces** provide *isolation* — they control what a process can *see*. Linux has several kinds, and a container is essentially a process placed inside a fresh set of them:

- **PID namespace** — the container's main process sees itself as PID 1, and cannot see host processes.
- **Network namespace** — the container gets its own network stack: interfaces, routing table, and ports. Two containers can both bind port 80 without conflict.
- **Mount namespace** — the container has its own view of the filesystem tree, rooted at the container image rather than the host's root.
- **UTS namespace** — its own hostname.
- **User namespace** — maps user IDs, so root *inside* the container can map to an unprivileged user *outside*.

**Control groups (cgroups)** provide *limits* — they control what a process can *use*. A cgroup caps CPU shares, memory, and I/O bandwidth for a group of processes. When you tell Kubernetes a pod may use "500m CPU and 512Mi memory," that limit is ultimately enforced by cgroups.

Put simply: **namespaces decide what you can see; cgroups decide what you can consume.** A container is just a normal Linux process (or process tree) wrapped in namespaces for isolation and cgroups for resource limits. That's why containers start in milliseconds and cost almost nothing at idle — there's no second kernel to boot.

> **Pitfall:** Because containers share the host kernel, a Linux container cannot run natively on Windows or macOS. Docker Desktop quietly runs a lightweight Linux VM in the background and starts your containers *inside* it. On a Linux server there is no such VM — containers run directly on the host kernel.

### Images vs. Containers

These two words get used interchangeably in conversation, but they are precisely distinct:

- An **image** is a read-only template — a stack of filesystem layers plus metadata (the command to run, environment variables, exposed ports). It is inert, like a class definition or an installer.
- A **container** is a running (or stopped) *instance* of an image, with a thin writable layer on top. It is live, like an object instantiated from a class.

One image can spawn a thousand containers, just as one class can produce a thousand objects. When a container writes a file, it doesn't modify the image; the write lands in the container's own writable layer via **copy-on-write**. Delete the container and that writable layer vanishes — which is exactly why containers are called *ephemeral* and why persistent data must live in volumes, not inside the container.

## Docker: Building Images

Docker popularized containers by giving them an ergonomic build tool and a distribution format. You describe an image declaratively in a **Dockerfile**, and `docker build` executes it into an image.

### Layers and layer caching

Every instruction in a Dockerfile that changes the filesystem produces a new **layer** — a diff on top of the previous layer. Images are these layers stacked and stored by content hash. This layering drives Docker's most important performance characteristic: **the build cache**.

When Docker executes a build, for each instruction it checks whether it has already built an identical layer (same instruction, same input). If so, it reuses the cached layer and skips the work. The moment one instruction's input changes, that layer and **every layer after it** are invalidated and rebuilt.

This single rule dictates how you should order a Dockerfile: **put the things that change rarely near the top, and the things that change constantly near the bottom.** For a .NET app, your project files and restored NuGet packages change far less often than your source code. So you copy the `.csproj` and restore *first*, then copy the rest of the source. That way, editing a `.cs` file doesn't force a full package restore.

> **Best practice:** Copy dependency manifests and restore packages *before* copying application source. This keeps the expensive `dotnet restore` layer cached across the many builds where only your code changed.

### Common Dockerfile instructions

- `FROM` — the base image to build on. Every Dockerfile starts here.
- `WORKDIR` — sets the working directory for subsequent instructions (and creates it).
- `COPY` / `ADD` — copy files from the build context into the image. Prefer `COPY`; `ADD` has surprising extra behavior (URL fetching, auto-extracting tarballs).
- `RUN` — execute a command at *build* time, producing a new layer (e.g. `dotnet restore`).
- `ENV` — set an environment variable baked into the image.
- `ARG` — a build-time variable, available only during the build.
- `EXPOSE` — documents which port the app listens on (metadata only; it doesn't actually publish the port).
- `USER` — sets the user for subsequent instructions and the running container.
- `ENTRYPOINT` / `CMD` — define what runs when the container starts. `ENTRYPOINT` is the fixed executable; `CMD` supplies default arguments.

### .dockerignore

The **build context** is the set of files Docker sends to the build engine before executing the Dockerfile. If you build from a folder containing `bin/`, `obj/`, `.git/`, and `node_modules/`, all of that gets shipped to the daemon — slowing the build and risking secrets or stale binaries leaking into your image. A `.dockerignore` file excludes them, exactly like `.gitignore`:

```gitignore
# .dockerignore
**/bin/
**/obj/
**/.vs/
**/.git/
**/node_modules/
**/*.user
Dockerfile
docker-compose*.yml
README.md
.env
```

> **Pitfall:** Without a `.dockerignore`, a stray `bin/Debug` folder copied by a broad `COPY . .` can shadow the freshly published output or bloat the context by hundreds of megabytes. Always add one.

### Multi-stage builds

Here is the tension: to *build* a .NET app you need the whole SDK (compilers, NuGet, MSBuild) — hundreds of megabytes. To *run* it you need only the much smaller runtime. A **multi-stage build** lets you use a fat SDK image to compile, then copy just the published output into a slim runtime image, discarding the SDK entirely. The final image contains none of the build tooling.

Think of it as a workshop and a display case: you do all the messy cutting and welding in the workshop (the build stage), then move only the finished product to the clean display case (the runtime stage). Nobody ships the workshop.

## Containerizing a .NET Application

Let's build a production-grade Dockerfile for an ASP.NET Core service. We'll layer in every best practice: multi-stage, cache-friendly ordering, non-root user, and a minimal final image.

```dockerfile
# syntax=docker/dockerfile:1

# ---- Stage 1: build & publish ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy only the project files first so restore is cached
# when only source code changes.
COPY ["MyApi/MyApi.csproj", "MyApi/"]
COPY ["MyApi.Core/MyApi.Core.csproj", "MyApi.Core/"]
RUN dotnet restore "MyApi/MyApi.csproj"

# Now copy the rest of the source and publish.
COPY . .
WORKDIR /src/MyApi
RUN dotnet publish "MyApi.csproj" \
    -c Release \
    -o /app/publish \
    --no-restore \
    /p:UseAppHost=false

# ---- Stage 2: runtime ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# Copy only the published output from the build stage.
COPY --from=build /app/publish .

# Run as the built-in non-root user shipped in the base image.
USER $APP_UID

# Kestrel listens here; ASP.NET Core reads ASPNETCORE_HTTP_PORTS.
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "MyApi.dll"]
```

A few details worth understanding:

- **`AS build` / `AS final`** name the stages. `COPY --from=build` reaches into the earlier stage to grab only `/app/publish`. The SDK never reaches the final image.
- **`--no-restore`** on publish avoids a redundant second restore, since we already restored in a cached layer.
- **`USER $APP_UID`** — modern Microsoft base images define a non-root user via the `APP_UID` environment variable (UID 1654). Running as this user is a critical security control (more below).
- **Port 8080, not 80** — since .NET 8, the default Microsoft images run as non-root, and non-root users cannot bind privileged ports (below 1024). The images default to 8080 for exactly this reason.

### Running as non-root

By default a container's process runs as **root** — root inside the container, which (absent user namespaces) is a genuine risk. If an attacker escapes the container through a kernel vulnerability, they land on the host with root privileges. Running as an unprivileged user shrinks that blast radius dramatically.

> **Best practice:** Never run production containers as root. Use `USER $APP_UID` with Microsoft's images, or create a dedicated user. Combine it at runtime with a read-only root filesystem and dropped Linux capabilities for defense in depth.

### Chiseled and distroless images: shrinking the attack surface

A standard `aspnet:10.0` image is based on Debian and includes a shell, a package manager, and dozens of system utilities. Your app needs almost none of them — but an attacker who breaks in can use them. **Chiseled** images (Microsoft's take on "distroless") strip the image down to the bare minimum: the .NET runtime and its direct dependencies, with **no shell, no package manager, and a non-root user by default.**

```dockerfile
# Runtime stage using an Ubuntu Chiseled image.
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled AS final
WORKDIR /app
COPY --from=build /app/publish .
# Chiseled images already run as non-root (UID 1654) and default to port 8080.
ENTRYPOINT ["dotnet", "MyApi.dll"]
```

The payoffs are substantial: images are tens of megabytes smaller, there's a far smaller surface for CVEs, and non-root is the default. The trade-off is that you **cannot `docker exec` a shell into the container** to poke around — there is no `/bin/sh`. For debugging you attach an ephemeral debug container or rely on logs and metrics.

> **Best practice:** For production, prefer chiseled/distroless runtime images. Smaller images pull faster, cost less to store, and present a smaller attack surface. Keep a shell-equipped image variant only if your ops workflow genuinely needs one.

### Image size optimization, summarized

- Use multi-stage builds so build tooling never reaches the final image.
- Choose the smallest viable base (`-alpine`, `-chiseled`).
- Order instructions for cache friendliness; restore before copying source.
- Add a thorough `.dockerignore`.
- Combine related `RUN` commands and clean up in the same layer (a file deleted in a *later* layer still occupies space in the earlier one).

## Docker Compose for Local Development

A real application is rarely one process. Yours might need a Postgres database and a Redis cache alongside it. Starting each by hand — with the right ports, environment variables, and startup order — is tedious and error-prone. **Docker Compose** describes a multi-container stack in a single YAML file and brings it all up with one command.

```yaml
# docker-compose.yml
services:
  api:
    build:
      context: .
      dockerfile: MyApi/Dockerfile
    ports:
      - "8080:8080"          # host:container
    environment:
      ASPNETCORE_ENVIRONMENT: Development
      ConnectionStrings__Postgres: "Host=db;Port=5432;Database=appdb;Username=app;Password=devsecret"
      ConnectionStrings__Redis: "cache:6379"
    depends_on:
      db:
        condition: service_healthy
      cache:
        condition: service_started

  db:
    image: postgres:16-alpine
    environment:
      POSTGRES_DB: appdb
      POSTGRES_USER: app
      POSTGRES_PASSWORD: devsecret
    ports:
      - "5432:5432"
    volumes:
      - pgdata:/var/lib/postgresql/data
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U app -d appdb"]
      interval: 5s
      timeout: 3s
      retries: 5

  cache:
    image: redis:7-alpine
    ports:
      - "6379:6379"
    command: ["redis-server", "--save", "60", "1"]

volumes:
  pgdata:
```

Key concepts illustrated here:

- **Service discovery by name.** Compose puts all services on a shared network where each is reachable by its service name. The API connects to Postgres at host `db` and Redis at host `cache` — no IP addresses, no `localhost`. That's why the connection string says `Host=db`.
- **`depends_on` with `condition: service_healthy`** — Postgres takes a moment to accept connections after its container starts. The `healthcheck` runs `pg_isready` until the database is truly ready, and the API waits for that healthy state rather than merely for the container to exist.
- **Named volume `pgdata`** — database files live in a Docker-managed volume so your data survives `docker compose down` and container recreation. Without it, every restart would wipe the database.
- **Environment as configuration.** The double-underscore in `ConnectionStrings__Postgres` maps to .NET's hierarchical configuration (`ConnectionStrings:Postgres`), so `IConfiguration` reads it seamlessly.

Bring the stack up with `docker compose up --build`, and tear it down (keeping the volume) with `docker compose down`. Add `-v` to also delete volumes.

> **Pitfall:** `depends_on` without a health condition only waits for the container to *start*, not for the service inside to be *ready*. Your app can still race ahead of a not-yet-listening database. Use healthchecks — and build retry logic into your app's startup regardless, because in production (Kubernetes) `depends_on` doesn't exist at all.

## Container Registries

You've built an image locally. To run it on a server or in Kubernetes, you push it to a **registry** — a versioned store for images, like NuGet for containers. Images are named `registry/repository:tag`.

- **Docker Hub** — the default public registry. Great for open-source base images; beware anonymous pull rate limits and prefer official/verified publishers.
- **Azure Container Registry (ACR)** — Microsoft's managed registry, tightly integrated with Azure AD and AKS. Login: `az acr login --name myregistry`, image name `myregistry.azurecr.io/myapi:1.4.0`.
- **Amazon ECR** — the AWS equivalent, integrated with IAM and EKS.
- **GitHub Container Registry (GHCR)** — `ghcr.io/owner/myapi:1.4.0`, convenient when your CI already lives in GitHub Actions.

The push/pull cycle:

```bash
docker build -t myregistry.azurecr.io/myapi:1.4.0 .
docker push myregistry.azurecr.io/myapi:1.4.0
docker pull myregistry.azurecr.io/myapi:1.4.0
```

> **Best practice:** Tag images with an immutable, meaningful version — a semver like `1.4.0` or the git commit SHA. Avoid deploying `latest` to production: it's a moving target, so you can never be certain which build is actually running, and rollbacks become guesswork.

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

## Service Mesh: Awareness

As microservices multiply, cross-cutting networking concerns pile up: mutual TLS between every service, retries and timeouts, fine-grained traffic splitting for canary releases, and detailed request-level telemetry. Building all of that into each application — across multiple languages — is repetitive and inconsistent.

A **service mesh** (Istio, Linkerd) moves those concerns *out* of your app and into the infrastructure. It injects a **sidecar proxy** (typically Envoy) next to each pod; all traffic flows through these proxies, which a central control plane configures. The mesh then provides mutual TLS everywhere, automatic retries and circuit breaking, traffic-shifting for canaries and blue-green, and uniform observability — **without a single line of change in your .NET code.**

The trade-off is real complexity and per-pod resource overhead from all those proxies. **Linkerd** favors simplicity and low overhead; **Istio** is more powerful and more configurable at the cost of a steeper learning curve. You don't need a mesh for a handful of services — but as a system grows into dozens of services with strict security and traffic-management requirements, a mesh becomes compelling. For now, know what it is and when to reach for it.

## .NET Aspire: Orchestration for Local Development

Docker Compose is language-agnostic, which means it doesn't know anything about your .NET projects. **.NET Aspire** is Microsoft's opinionated stack for building and running cloud-native, multi-service .NET apps — and it dramatically improves the *inner-loop* (local development) experience.

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

## Summary

Containers are ordinary processes wrapped in kernel **namespaces** (isolation) and **cgroups** (limits) — not miniature VMs — which is why they're fast and cheap. **Docker** builds them from layered, cache-friendly Dockerfiles; **multi-stage builds** and **chiseled, non-root** images give you small, secure .NET containers. **Docker Compose** orchestrates a local multi-service stack, while **registries** distribute your images.

At scale, **Kubernetes** takes over: you *declare* desired state — Deployments of Pods fronted by Services, configured with ConfigMaps and Secrets, kept healthy by **probes**, bounded by **resource requests and limits**, and scaled by the **HPA** — and its control loops make reality match. **Helm** and **Kustomize** tame the resulting YAML across environments, a **service mesh** handles cross-service networking without touching your code, and **.NET Aspire** makes the local development loop for all of this genuinely enjoyable. Master these layers and "it works on my machine" finally becomes "it works everywhere."
