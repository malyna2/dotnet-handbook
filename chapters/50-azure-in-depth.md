# Chapter 50: Azure in Depth for .NET Developers

_⏱️ Estimated read time: ~1 h 30 min · 13510 words (study pace)_

[Chapter 10](#chapter-10-cloud-aws-azure) gave you the map: what App Service, Functions, Cosmos DB and Service Bus *are*, and how they line up against AWS. A map gets you through a conversation. It does not get you through the first week of owning a production system on Azure, where the questions sound like this: *why does the app get a 403 from Blob Storage when its identity is a Contributor on the subscription? Why did the slot swap cause a minute of 500s? Why does Cosmos DB throttle at 3,000 RU/s when we provisioned 20,000?*

This chapter is the territory. For each service a .NET developer touches every week, it explains the **mechanism** (what actually happens when you call it), shows the **.NET code** you would write, lists the **limits that bite**, and ends with a **decision table**. The level it aims for is a strong middle developer who can build, ship and debug an Azure system without a platform team holding their hand, and who can explain their choices in an interview. [Chapter 51](#chapter-51-the-azure-casebook-real-incidents-real-fixes) then puts all of it to work on real incidents.

```
                        how a typical .NET system on Azure is layered
  ┌─────────────────────────────────────────────────────────────────────────────┐
  │ EDGE        Front Door / Application Gateway (WAF)  ·  API Management       │
  ├─────────────────────────────────────────────────────────────────────────────┤
  │ COMPUTE     App Service  ·  Functions  ·  Container Apps  ·  AKS            │
  ├───────────────────────────────┬─────────────────────────────────────────────┤
  │ DATA        Azure SQL         │ MESSAGING   Service Bus  (commands)         │
  │             Cosmos DB         │             Event Hubs   (streams)          │
  │             Blob Storage      │             Event Grid   (notifications)    │
  │             Managed Redis     │                                             │
  ├───────────────────────────────┴─────────────────────────────────────────────┤
  │ CROSS-CUTTING   Entra ID + Managed Identity + RBAC   ·   Key Vault          │
  │                 App Configuration  ·  Azure Monitor / Application Insights  │
  │                 VNet + Private Endpoints + Private DNS   ·   Bicep / azd    │
  └─────────────────────────────────────────────────────────────────────────────┘
        the cross-cutting row is where most production incidents actually live
```

Read the cross-cutting row carefully. Most developers learn the services in the middle rows first. In practice, the failures that take hours to debug come from the bottom row: identity, networking, configuration and missing telemetry.

## Certifications: What They Measure, and Where This Chapter Stands

A certification proves you have the vocabulary. An interview, and your first incident, test whether you understand the mechanism behind it. This chapter aims at the second, but it is laid out so that the first comes along for free.

The landscape changed in 2026, so check the current state yourself before you book anything. As of September 2026, the official study-guide pages on Microsoft Learn could not be opened from the environment this chapter was written in, so the table below comes from several independent third-party summaries that agree with each other. Confirm it on the official study guide before you rely on it.

| Exam | Status (September 2026) | What it measures | Where this chapter covers it |
|---|---|---|---|
| **AZ-900** Azure Fundamentals | Active. The skills outline was revised in July 2026. | Cloud concepts (~25–30%); Azure architecture and services (~35–40%); management and governance (~30–35%). | *The Control Plane* and *Cost* sections, plus the first paragraph of every service section. |
| **AZ-204** Developing Solutions for Azure | **Retired on 31 July 2026.** A certification you already hold stays valid until it expires. | *(was)* compute, storage, security, monitoring, and connecting to Azure and third-party services. | The whole chapter. Its topics are still what the job needs. |
| **AI-200** Developing AI Cloud Solutions on Azure → *Azure AI Cloud Developer Associate* | The suggested successor to AZ-204. | Containerized solutions (Container Registry, Container Apps, AKS); data management for AI (Cosmos DB for NoSQL, PostgreSQL with pgvector, Azure Managed Redis vector search); event-driven solutions (Service Bus, Event Grid, Event Hubs, Functions); security and monitoring (Key Vault, RBAC, Azure Monitor, KQL). | Everything except the vector-search parts, which [Chapter 19](#chapter-19-building-ai-powered-systems) covers. |

The practical takeaway: **AZ-900 is a reasonable one-weekend goal** once you have read this chapter, and it is useful when your CV has to get past a recruiter's keyword filter. The developer-level exam has moved towards AI workloads, but the platform underneath it is exactly what this chapter teaches: containers, Cosmos DB, messaging, Functions, Key Vault, RBAC and KQL. Learn the platform first. The AI-specific parts are thin layers on top of it.

> **Best practice.** Use the free Microsoft Learn sandbox and the exam's official practice assessment rather than a question dump. Dumps teach you to recognise answers. This chapter's *Self-check* section at the end, and Chapter 51's cases, teach you to derive them, which is also what an interviewer checks.

## The Control Plane: How Azure Is Organised

Every Azure resource sits in a four-level hierarchy, under one Microsoft Entra tenant:

```
  Microsoft Entra tenant  (the identity boundary: users, groups, apps, managed identities)
   └── Management groups  (optional, nestable: policy + RBAC for many subscriptions at once)
        └── Subscription   (the billing and quota boundary; one trust relationship with a tenant)
             └── Resource group   (a lifecycle boundary: things that are deployed and deleted together)
                  └── Resources   (a storage account, a web app, a Service Bus namespace …)

  RBAC role assignments and Azure Policy assignments made at any level are INHERITED downward.
```

Three consequences come up again and again in real work:

- **A resource group is a lifecycle boundary, not a network or region boundary.** Put things that live and die together in one group (an app, its plan, its Application Insights, its Key Vault) so that tearing down an environment is `az group delete`. Resources in a group can sit in different regions; the group's own location only says where its metadata lives.
- **A subscription is the unit of billing and quota.** Many limits are per subscription per region (vCPU quotas, for example). Teams often use one subscription per environment (dev, test, prod), which gives hard isolation and a separate bill for each.
- **Permissions flow down.** Someone who is *Contributor* on a subscription is Contributor on every resource group and every resource in it. That is why "just give me Contributor on the subscription" is a request a senior engineer pushes back on.

### ARM: one API behind every tool

The portal, the Azure CLI, PowerShell, Bicep, Terraform and the management SDKs all end up making HTTPS calls to the same endpoint, **Azure Resource Manager** (`management.azure.com`). ARM authenticates you with Entra ID, checks RBAC and Policy, and forwards the request to the resource provider (`Microsoft.Storage`, `Microsoft.Web`, …). This is why everything you can click in the portal can also be scripted, and why IaC tools can see drift.

This leads to the single most useful distinction in Azure, and the one behind a large share of "why do I get 403?" incidents:

| | **Control plane** | **Data plane** |
|---|---|---|
| What it does | Manages the *resource*: create a storage account, change its SKU, list its keys, add a queue | Uses the *resource*: read a blob, send a message, read a secret, query a container |
| Endpoint | `management.azure.com` (ARM) | The service's own endpoint: `<account>.blob.core.windows.net`, `<ns>.servicebus.windows.net`, `<vault>.vault.azure.net` |
| RBAC roles | *Owner*, *Contributor*, *Reader*, and service-specific management roles | Roles that include `DataActions`: *Storage Blob Data Contributor*, *Azure Service Bus Data Receiver*, *Key Vault Secrets User* … |

A *Contributor* on a storage account can delete the account, but cannot read a single blob with an Entra token, because Contributor carries no data actions. It can still reach the data *indirectly*: Contributor may list the account keys, and a key opens everything. This is one reason to turn off shared-key access (see *Identity*).

### Governance: RBAC, Policy, locks and tags

These four tools get confused, because all of them can stop you from doing something. They answer different questions:

| Tool | Question it answers | Example |
|---|---|---|
| **RBAC** | *Who* may perform *which action* at *which scope*? | The app's managed identity may read secrets in this one vault. |
| **Azure Policy** | *What* configurations are allowed, whoever makes them? | Storage accounts must disable public network access; resources may only be created in West Europe and North Europe; every resource group needs a `costCenter` tag. |
| **Resource lock** | Should this resource be protected from deletion or changes, even by an Owner? | `CanNotDelete` on the production SQL server. |
| **Tags** | What is this resource, who owns it, who pays for it? | `env=prod`, `owner=payments-team`, `costCenter=4711`. |

Policy has *effects*: `Deny` blocks the request, `Audit` only reports it, `Modify` changes it (for example, it adds a missing tag), and `DeployIfNotExists` deploys a companion resource (for example, diagnostic settings). A developer usually meets Policy as a failed deployment with a `RequestDisallowedByPolicy` error. The error names the policy assignment, and that name is where to start.

> **Gotcha.** Locks protect the **resource**, not the **data** in it. Microsoft's documentation is explicit: a read-only or cannot-delete lock on a storage account does not stop blobs, queues or tables from being deleted or modified. A read-only lock also has side effects that surprise people. It blocks *List Keys*, which is a `POST` on the control plane, so tools that authenticate with account keys stop working.

### Regions, availability zones and SLAs

A **region** is a set of datacenters in one geography. Most regions have three **availability zones**: physically separate datacenters with independent power, cooling and networking. Many regions are also paired with a second region in the same geography, and the pair matters for geo-redundant storage and for the order in which Microsoft rolls out platform updates.

Services come in three flavours with respect to zones. **Zone-redundant** resources are spread across zones for you (zone-redundant storage, zone-redundant App Service plans, Azure SQL with zone redundancy). **Zonal** resources are pinned to one zone you choose (a VM, a disk). **Non-zonal** resources carry no zone guarantee. An architecture survives a zone outage only if every component on the request path is zone-redundant, or is duplicated across zones by you.

**Composite SLAs are multiplied, which catches people out.** If a request passes through App Service (99.95%) and then Azure SQL (99.99%), and *both* must work, the path's availability is at most:

```
0.9995 × 0.9999 = 0.9994   → 99.94%  (about 26 minutes of downtime in a 30-day month, against 22 for App Service alone)
```

Every component you add *in series* lowers the number. Components *in parallel* (two regions behind Front Door, each one enough on its own) raise it: two independent 99.94% paths give `1 − (0.0006 × 0.0006) ≈ 99.99996%`, on paper. That figure holds only if failover works and the paths really are independent, which is why [Chapter 21](#chapter-21-distributed-systems-theory-reliability-engineering) treats reliability as something you test, not something you calculate. The SLA percentages themselves change, so read them on the current SLA page for each service.

### Pricing models, at the level an exam and a code review need

| Model | What it is | When it fits |
|---|---|---|
| Pay-as-you-go | Per second, hour or operation, with no commitment | Dev/test, spiky or unknown load |
| Reservations | Commit to 1 or 3 years of a specific resource type (VMs, SQL vCores, Cosmos RU/s …) for a large discount | Stable baseline production capacity |
| Savings plan for compute | Commit to an hourly spend on compute, which applies across VM sizes and regions | Stable spend, changing shapes |
| Spot | Spare capacity at a deep discount that can be evicted at short notice | Interruptible batch work; never for request serving |
| Azure Hybrid Benefit | Reuse existing Windows Server or SQL Server licences | Lift-and-shift from on-premises |

**Budgets alert; they do not stop spending.** A budget in Cost Management sends email or triggers an action group when actual or forecast spend crosses a threshold. It will not switch anything off unless you wire an automation to it. [Chapter 28](#chapter-28-compliance-data-privacy-cloud-cost-finops) covers FinOps properly. The *Cost* section near the end of this chapter lists the specific decisions developers make that show up on the bill.

## Identity: Entra ID, Managed Identity and RBAC, Mechanically

Chapter 10 showed the headline pattern: managed identity plus `DefaultAzureCredential`, with no secrets in code. This section explains what happens under that one line, because when it fails, it fails with errors that only make sense if you know the mechanism.

### The objects: app registration, service principal, managed identity

- An **app registration** is the *global definition* of an application in its home tenant: its client ID, its redirect URIs, the API permissions it asks for, the app roles it exposes, and its credentials (secrets, certificates, federated credentials).
- A **service principal** (shown in the portal as an *Enterprise application*) is the *instance* of that application in a particular tenant. Role assignments and consent attach to the service principal, not to the registration.
- A **managed identity** is a service principal whose credential Azure creates, stores and rotates for you. Your code never sees it. There are two kinds:

| | System-assigned | User-assigned |
|---|---|---|
| Lifecycle | Created with the resource, deleted with it | A standalone resource; you create and delete it |
| Sharing | One resource only | Any number of resources |
| Role assignments | Can be made only after the resource exists | Can be made **before** the app is deployed, in the same IaC run |
| Typical use | Simple, single-resource apps | Many instances or slots that need identical access; blue/green; avoiding the propagation delay during a deployment |

User-assigned identities are the better default for anything that is deployed repeatedly. With a system-assigned identity, every newly created resource gets a brand-new principal, and its role assignments can take several minutes to take effect (see below). So the first minutes of a fresh environment fail with 403s. A user-assigned identity already exists, and its roles are already in place.

### How the token actually arrives

On an Azure host, your code never talks to Entra ID directly. The platform exposes a **local token endpoint**. On VMs this is the Instance Metadata Service at the non-routable address `169.254.169.254`. On App Service and Functions it is a local endpoint whose address and secret header the platform injects as the environment variables `IDENTITY_ENDPOINT` and `IDENTITY_HEADER`. The Azure SDK's `ManagedIdentityCredential` calls that endpoint, asks for a token for a **resource** (the audience, such as `https://storage.azure.com/`), and caches the result until it nears expiry.

That token is a JWT. The claims to read when debugging are:

| Claim | Meaning | Typical surprise |
|---|---|---|
| `aud` | Which API the token is for | A token for `https://management.azure.com/` is useless against `vault.azure.net`. |
| `oid` | The object ID of the principal (for a managed identity, its service principal) | Role assignments must target this ID, not the app registration's object ID. |
| `roles` | App roles granted to an application (client credentials flow) | Empty, because the role was granted to the wrong principal, or admin consent is missing. |
| `scp` | Delegated scopes (only when a *user* is signed in) | Present in user tokens, absent in app-only tokens. The API must check the right one. |

Decode a token locally, with a tool you trust or a few lines of code, rather than pasting production tokens into a website.

### `DefaultAzureCredential`: what the chain really is

`DefaultAzureCredential` tries a list of credentials in order and uses the first one that returns a token. In the current `Azure.Identity` (1.21, whose credential types ship in `Azure.Core`), the order is:

```
  EnvironmentCredential          AZURE_CLIENT_ID / AZURE_TENANT_ID / AZURE_CLIENT_SECRET (or certificate)
  WorkloadIdentityCredential     AKS workload identity (federated token file)
  ManagedIdentityCredential      the platform's token endpoint (App Service, Functions, VMs, Container Apps …)
  VisualStudioCredential         developer tools, only useful on your machine
  VisualStudioCodeCredential
  AzureCliCredential             `az login`
  AzurePowerShellCredential
  AzureDeveloperCliCredential    `azd auth login`
  (BrokerCredential, when the Azure.Identity.Broker package is referenced)
  InteractiveBrowserCredential   excluded unless you opt in
```

The SDK's own documentation says that in production it is better to use something else. There are three reasons:

1. **Wrong identity, silently.** If someone leaves `AZURE_CLIENT_ID` and `AZURE_CLIENT_SECRET` in the app settings from an old experiment, `EnvironmentCredential` wins, and the app runs as a forgotten service principal instead of its managed identity.
2. **Slow failure.** On a host where managed identity is not configured, the chain moves on to the developer credentials, and each one has to fail before the next is tried. That shows up as a slow first request and a confusing combined error message.
3. **User-assigned identities need an ID.** With more than one user-assigned identity, or with one and no system-assigned identity, the credential has to be told *which* one to use: `ManagedIdentityClientId` in the options, or the `AZURE_CLIENT_ID` environment variable.

The fix is to be explicit in production and convenient in development. Newer versions of `Azure.Identity` support an environment variable, `AZURE_TOKEN_CREDENTIALS`: set it to `prod` to keep only the deployed-service credentials (environment, workload identity, managed identity), to `dev` to keep only the developer tools, or to the name of a single credential such as `ManagedIdentityCredential`. When it names `ManagedIdentityCredential`, the credential also skips its initial probe request and retries with exponential backoff instead.

```csharp
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Azure;

var builder = WebApplication.CreateBuilder(args);

// One credential for the whole app. In Azure: exactly the user-assigned managed identity we
// deployed. On a laptop: whoever ran `az login` / signed in to Visual Studio.
TokenCredential credential = builder.Environment.IsDevelopment()
    ? new DefaultAzureCredential()
    : new ManagedIdentityCredential(
        ManagedIdentityId.FromUserAssignedClientId(builder.Configuration["Identity:ClientId"]!));

// Azure SDK clients are thread-safe and meant to be singletons. AddAzureClients registers them
// once, wires their logging into ILogger, and shares the credential.
builder.Services.AddAzureClients(clients =>
{
    clients.AddBlobServiceClient(new Uri(builder.Configuration["Storage:BlobEndpoint"]!));
    clients.AddServiceBusClientWithNamespace(builder.Configuration["ServiceBus:Namespace"]!);
    clients.AddSecretClient(new Uri(builder.Configuration["KeyVault:Uri"]!));
    clients.UseCredential(credential);
});
```

> **Pitfall.** Creating a `new DefaultAzureCredential()` or a `new BlobServiceClient(...)` for every request. Each credential instance keeps its own token cache, so a credential per request means a token request per request. Under load that means throttling from the identity endpoint, plus latency on every call. Credentials and clients are singletons.

### RBAC, precisely

A **role assignment** has three parts: *who* (a security principal: user, group, service principal or managed identity), *what* (a role definition: a list of `Actions`, `NotActions`, `DataActions` and `NotDataActions`), and *where* (a scope: management group, subscription, resource group, or a single resource, and for some services a sub-resource such as one blob container). Access is the union of all assignments that apply at or above the scope. Azure also has *deny assignments*, but you cannot create those directly; they come from features such as deployment stacks and managed applications.

Least privilege in practice looks like this for a typical API:

| The app needs to … | Assign | At scope |
|---|---|---|
| read and write blobs in one container | Storage Blob Data Contributor | that container (`…/blobServices/default/containers/uploads`) |
| receive from one queue | Azure Service Bus Data Receiver | that queue |
| send to one topic | Azure Service Bus Data Sender | that topic |
| read secrets | Key Vault Secrets User | that vault (or a single secret) |
| pull images | AcrPull | that registry |

**Role assignments are eventually consistent.** Microsoft's troubleshooting guide says changes can take up to 10 minutes to take effect. Tokens that were already issued also keep their contents until they expire. So "I just assigned the role and it still says 403" is often correct behaviour, not a misconfiguration. Wait, or restart the app to force a new token, before you start changing things.

**Cosmos DB is the exception to remember.** Its data-plane RBAC for the NoSQL API uses Cosmos DB's *own* role definitions and assignments (created with `az cosmosdb sql role assignment create`), not Azure RBAC role assignments. Assigning *Contributor* in the portal's IAM blade does not let an identity read items.

### Turn off keys once identity works

Most data services accept two kinds of credential: Entra tokens, and a shared secret (storage account keys, Service Bus and Event Hubs SAS keys, Cosmos DB primary keys). A shared secret bypasses RBAC completely: whoever holds the key can do everything, and nothing ties the actions to a person. Once your apps use managed identity, **disable local authentication**: `allowSharedKeyAccess: false` on storage accounts, `disableLocalAuth: true` on Service Bus, Event Hubs and Cosmos DB accounts. An Azure Policy with a `Deny` effect keeps it that way. [Chapter 14](#chapter-14-security) explains why a secret you do not have is the only secret you cannot leak.

### Protecting your own API

Your API is also a resource that other callers need tokens for. With `Microsoft.Identity.Web`, validating Entra tokens takes one line:

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.Identity.Web;

builder.Services.AddAuthentication()
    .AddMicrosoftIdentityWebApi(builder.Configuration.GetSection("AzureAd"));   // TenantId, ClientId (the API's app registration), Audience

builder.Services.AddAuthorization(options =>
{
    // Service-to-service callers get an *app role* (the "roles" claim), granted by an admin.
    options.AddPolicy("OrdersWriter", p => p.RequireRole("Orders.Write"));
});

app.MapPost("/orders", [Authorize(Policy = "OrdersWriter")] (CreateOrder cmd) => Results.Accepted());
```

The flows to know by name:

| Flow | Who is calling | Token contains |
|---|---|---|
| **Client credentials** | A service as itself: a daemon, a Function, another API calling with its managed identity | `roles` (app roles), no user |
| **Authorization code + PKCE** | A user through a web or SPA front end | `scp` (delegated scopes) + the user |
| **On-behalf-of (OBO)** | Your API calls a downstream API *as the signed-in user* | A new token for the downstream API, with the same user |

A managed identity can use the client credentials flow against *your* API too: the caller asks for a token with the audience `api://<your-api-client-id>/.default`, and an admin assigns your API's app role to the caller's managed identity.

### Workload identity federation: no secret in CI either

The last long-lived secret in most teams is the one their CI uses to deploy. **Workload identity federation** removes it. You add a *federated credential* to an app registration or a user-assigned managed identity that says, in effect, "trust tokens issued by `token.actions.githubusercontent.com` for the repository `org/repo` on the environment `production`". The GitHub Actions job then asks GitHub for an OIDC token, exchanges it with Entra ID, and gets an Azure token. No secret is stored anywhere. The `azure/login` action does this when you give it `client-id`, `tenant-id` and `subscription-id` and grant the workflow `id-token: write`. [Chapter 12](#chapter-12-devops-cicd) covers the pipeline itself, and [Chapter 35](#chapter-35-software-supply-chain-security) explains why a stolen CI credential is one of the most damaging supply-chain attacks there is.

## Compute: Choosing It and Running It

Start with the decision and learn the details of the option you pick:

| You have … | Reach for | Because |
|---|---|---|
| A standard ASP.NET Core web app or API, steady traffic, a team without Kubernetes skills | **App Service** | Least operational work; slots, autoscale, auth and TLS are built in |
| Event-driven work (a queue message, a blob upload, a timer), bursty or rare | **Azure Functions** (Flex Consumption) | Scale per event, down to zero; bindings remove boilerplate |
| Several containerised services, event-driven scaling, scale to zero, no wish to run Kubernetes | **Container Apps** | Kubernetes and KEDA underneath, without cluster operations |
| A platform team, many services, custom networking or operators, strong Kubernetes skills | **AKS** | Full Kubernetes control, and full Kubernetes responsibility |
| An OS-level dependency, a legacy Windows service, licensing tied to a machine | **Virtual Machines** | IaaS: maximum control, maximum patching work |

### App Service

**The plan is the machine.** An *App Service plan* is a set of VM instances of one size (the SKU: B1, S1, P1v3 …). Every app in the plan runs on *every* instance of the plan. Two consequences follow. First, a noisy app starves its neighbours. Second, you pay for the plan whether the apps are busy or idle. *Scale up* changes the SKU (a bigger machine). *Scale out* changes the number of instances, manually or with autoscale rules on metrics such as CPU or HTTP queue length.

**Configuration becomes environment variables.** App settings are injected as environment variables, and ASP.NET Core's configuration reads them, so nested keys use a double underscore: the app setting `Payments__Stripe__Timeout` overrides `Payments:Stripe:Timeout`. Connection strings are injected with a type prefix, such as `SQLAZURECONNSTR_` or `CUSTOMCONNSTR_`. The environment-variable configuration provider recognises these prefixes and maps them into `ConnectionStrings:<name>`, so `GetConnectionString("Default")` still works.

**Key Vault references** keep secrets out of app settings. You set an app setting's value to a reference instead of the secret:

```
@Microsoft.KeyVault(SecretUri=https://<vault>.vault.azure.net/secrets/<name>)
```

The platform resolves it with the app's managed identity, which needs *Key Vault Secrets User* on the vault, and your code sees a plain environment variable. If you leave the version out of the URI, the platform picks up rotated values, though not instantly.

**Deployment slots** are live apps with their own host names that share the plan's instances. The swap is worth understanding step by step, because the failures it causes are all explained by the order of the steps. Microsoft documents the swap from a *source* slot (staging) into a *target* slot (production) like this:

```
  1. Apply the TARGET's slot-specific settings to every SOURCE instance  → the source restarts
  2. Wait for every source instance to finish restarting (any failure → swap aborted, rolled back)
  3. Local cache initialisation, if enabled (another restart)
  4. Warm up: request the application root on every source instance (or the path set in
     WEBSITE_SWAP_WARMUP_PING_PATH, accepting only WEBSITE_SWAP_WARMUP_PING_STATUSES)
  5. Switch the routing rules                     ← the only instant step; production now serves the new build
  6. Apply the settings to the old production code, now in the source slot, and restart it
```

What that means in practice:

- **Mark environment-specific settings as *deployment slot settings*** (sticky). Everything else *moves with the code*. A connection string that is not sticky will follow the staging build into production.
- **Point the warm-up at an endpoint that exercises the app**, such as a health endpoint that opens a database connection and primes caches. The default warm-up request to `/` only proves that the process started.
- **The old production instances are recycled in the last step.** Long-running work on them is abandoned. The documentation says so explicitly, and it also applies to function apps, so background work must be restartable (see [Chapter 22](#chapter-22-background-processing-scheduling-the-actor-model)).
- **Swap with preview** stops after step 1, so you can test the source slot running with production's settings before you complete the swap.

**Health check.** Configure a health-check path, and App Service will take an instance out of the load balancer after it fails 10 checks in a row (by default), and replace it if it stays unhealthy. To protect the remaining instances, it never removes more than half of the instances at once. Make the endpoint check the dependencies the app cannot work without, and keep it cheap, because every instance is checked at one-minute intervals.

**Settings that belong in every production checklist:**

| Setting | Why |
|---|---|
| **Always On** (Basic tier and above) | Without it, the app is unloaded after 20 minutes without requests, and the next request pays the whole start-up cost. |
| **ARR affinity off** for stateless APIs | Otherwise a cookie pins each client to one instance, which defeats scale-out and makes one instance hot. |
| **HTTPS only**, minimum TLS 1.2 | Obvious, and still missed. |
| **Health check path** | See above. |
| **Zone redundancy** on the plan (on tiers that support it; at least 2 instances) | Survive a zone outage. Instances are spread across zones, so one zone can go down. |

**Two limits that cause real incidents:**

- **230 seconds.** The Azure front-end load balancer closes an HTTP request that has not produced a response within 230 seconds. Your code keeps running, but the client gets an error. The same limit applies to HTTP-triggered functions, whatever the function's own timeout is. For long work, accept the request, return `202 Accepted` with a status URL, and do the work in the background (a queue plus a worker, or Durable Functions).
- **SNAT ports.** Outbound connections to public endpoints go through source NAT. Microsoft's troubleshooting guide says each instance gets a *preallocated* 128 ports. An app that opens a new outbound connection per request (`new HttpClient()` per call, a new SQL connection without pooling, a new SDK client per request) exhausts them. The symptoms are intermittent connection timeouts and `SocketException`s that appear only under load, and no CPU or memory alarm fires. The fixes are to reuse connections (`IHttpClientFactory`, singleton SDK clients), to use private endpoints for Azure services (traffic that stays in the VNet does not use SNAT), or to add a NAT gateway (64,512 ports per public IP). [Chapter 51](#chapter-51-the-azure-casebook-real-incidents-real-fixes) walks through this exact incident.

### Azure Functions

**Use the isolated worker model.** Your functions run in a separate .NET process from the Functions host, on whatever .NET version you target, with normal dependency injection and middleware. Support for the older in-process model **ends on 10 November 2026**, so any in-process function app is now a migration item.

```csharp
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = FunctionsApplication.CreateBuilder(args);

builder.Services.AddHttpClient<PartnerApiClient>();   // pooled connections: see SNAT above
builder.Services.AddSingleton<IOrderRepository, SqlOrderRepository>();

builder.Build().Run();
```

```csharp
public sealed class OrderPlacedFunction(IOrderRepository orders, ILogger<OrderPlacedFunction> log)
{
    [Function(nameof(OrderPlacedFunction))]
    public async Task Run(
        [ServiceBusTrigger("orders", Connection = "ServiceBus")] ServiceBusReceivedMessage message,
        CancellationToken ct)
    {
        var order = message.Body.ToObjectFromJson<OrderPlaced>()!;

        // The trigger completes the message if this returns, and abandons it if this throws.
        // After MaxDeliveryCount abandons the message is dead-lettered. So: be idempotent.
        if (await orders.ExistsAsync(order.OrderId, ct))
        {
            log.LogInformation("Order {OrderId} already processed; skipping duplicate", order.OrderId);
            return;
        }
        await orders.InsertAsync(order, ct);
    }
}
```

The `Connection = "ServiceBus"` setting is the *name* of a configuration prefix, not a connection string. With `ServiceBus__fullyQualifiedNamespace` set to `<ns>.servicebus.windows.net` (plus `ServiceBus__clientId` for a user-assigned identity), the trigger authenticates with managed identity and no secret.

**Hosting plans.** Pick the plan before you write the code, because it decides timeouts, networking and cold starts:

| Plan | Scale | Default / max timeout | Networking | Notes |
|---|---|---|---|---|
| **Flex Consumption** | Event-driven, per function, to zero; optional always-ready instances | 30 min / unbounded | VNet integration | The recommended serverless plan for new apps. Linux. |
| Consumption (legacy) | Event-driven, to zero | 5 min / 10 min | None | Documented as legacy. **Linux Consumption retires on 30 September 2028.** |
| Premium (Elastic Premium) | Pre-warmed instances, never to zero | 30 min / unbounded | VNet integration | Predictable latency. You pay for the minimum instances. |
| Dedicated (App Service plan) | Your plan's autoscale | 30 min / unbounded | As App Service | Reuse spare capacity in an existing plan. |
| Container Apps | KEDA | 30 min / unbounded | Container Apps environment | Functions as a container, next to your other container apps. |

Whatever the plan, an **HTTP-triggered function has 230 seconds** to respond (the same load balancer as App Service). "Unbounded" timeouts still come with grace periods: when an instance is scaled in on Flex Consumption or Premium, a running execution gets 60 minutes to finish, and during platform updates it gets 10.

**Scale multiplies concurrency.** The Service Bus trigger processes up to `maxConcurrentCalls` messages at a time *per instance*. The default is 16, and the documentation notes that it is effectively multiplied by the instance's core count (32 on a two-core instance). Meanwhile the platform adds instances as the queue grows. At 40 two-core instances that is 1,280 concurrent executions, all opening connections to the same database. The trigger scales. Your database does not. When the downstream system has a fixed capacity, cap the product of the two: set `maxConcurrentCalls` in `host.json`, and cap the instance count (Flex Consumption has a maximum-instance setting for this). Then measure. Chapter 51 has the incident where this goes wrong.

```json
{
  "version": "2.0",
  "extensions": {
    "serviceBus": {
      "maxConcurrentCalls": 8,
      "maxAutoLockRenewalDuration": "00:05:00"
    }
  }
}
```

**Durable Functions** let you write a workflow (call A, then fan out to B×100, wait for a human's approval with a 3-day timeout, then call C) as ordinary-looking `async` code. The mechanism is **event sourcing plus replay**. Each time the orchestrator wakes up, the framework *re-runs the orchestrator function from the start* and feeds it the recorded results of every step already completed. So the orchestrator code must be **deterministic**:

```csharp
[Function(nameof(ApproveRefund))]
public static async Task<string> ApproveRefund([OrchestrationTrigger] TaskOrchestrationContext context)
{
    var request = context.GetInput<RefundRequest>()!;

    // ✗ DateTime.UtcNow, Guid.NewGuid(), Random, HttpClient, Task.Delay, reading config or the DB:
    //   they return something different on replay and break the orchestration.
    // ✓ Use the context's replay-safe versions, and put all I/O in activities.
    DateTime deadline = context.CurrentUtcDateTime.AddDays(3);
    Guid refundId = context.NewGuid();

    await context.CallActivityAsync(nameof(NotifyApprover), new ApprovalRequest(refundId, request));

    using var timeoutCts = new CancellationTokenSource();
    Task<bool> approval = context.WaitForExternalEvent<bool>("ApprovalDecision");
    Task timeout = context.CreateTimer(deadline, timeoutCts.Token);

    if (await Task.WhenAny(approval, timeout) == approval)
    {
        timeoutCts.Cancel();
        return approval.Result
            ? await context.CallActivityAsync<string>(nameof(IssueRefund), refundId)
            : "rejected";
    }
    return "expired";
}
```

The patterns to know by name are *function chaining*, *fan-out/fan-in*, *async HTTP API* (the framework provides the `202` + status-URL endpoints), *monitor* (a polling loop that uses durable timers), and *human interaction* (the example above).

### Container Apps

Container Apps runs your containers on a managed Kubernetes cluster that you never see, with **KEDA** for scaling and optional **Dapr** for service-to-service calls, pub/sub and state.

- An **environment** is the shared boundary: one VNet, one Log Analytics workspace, and internal DNS for all the apps inside it. Apps in the same environment call each other by name.
- A **revision** is an immutable snapshot of an app's template (its image and configuration). In *single* revision mode, a new revision replaces the old one after it becomes healthy. In *multiple* revision mode, you split traffic by weight (90/10 canaries) or by label (a `green` URL for testing before any real traffic reaches it).
- **Scale rules** are KEDA scalers: HTTP concurrency, CPU or memory, Service Bus queue length, Event Hubs lag, cron, and many more. `minReplicas: 0` gives scale to zero (and cold starts). `minReplicas: 1` keeps one replica warm.
- **Jobs** are containers that run to completion: manually, on a cron schedule, or triggered by events (one execution per batch of queue messages).
- **Ingress** is *external* (public) or *internal* (inside the environment's VNet only). Pull images from Azure Container Registry with a managed identity that has *AcrPull*, not with admin credentials.

It is the natural home for a .NET system of a handful of services, which is exactly what [Chapter 11](#chapter-11-containers-orchestration) builds towards. Move to AKS when you need something Container Apps does not expose: custom operators, service-mesh control, node-level tuning, or a platform team that wants the whole Kubernetes API.

## Storage Accounts and Blob Storage

A **storage account** is a namespace (`<name>` must be globally unique, 3–24 lowercase letters and digits) that exposes several services at separate endpoints: Blob (`.blob.core.windows.net`), Queue, Table, Files, and Data Lake (`.dfs.`). Use general-purpose v2 accounts unless you have a specific reason not to.

### Redundancy: how many copies, and where

| Option | Copies | Survives | Notes |
|---|---|---|---|
| **LRS** | 3 in one datacenter | A disk or rack failure | Cheapest. A datacenter-level event makes the data unavailable. |
| **ZRS** | 3 across 3 availability zones | A zone outage | The sensible default for production in regions with zones. |
| **GRS** | LRS + 3 more (LRS) in the paired region | A region outage, after failover | Replication to the secondary is **asynchronous**. |
| **GZRS** | ZRS + LRS in the paired region | Zone and region failures | The strongest option. |
| **RA-GRS / RA-GZRS** | As above, and the secondary is readable at `<name>-secondary.blob.core.windows.net` | Region outage for **reads**, without a failover | Your code must know to read from the secondary. |

Geo-replication is asynchronous, so a regional disaster can lose recent writes. Microsoft's documentation calls this interval the recovery point objective, and only an optional feature (*geo priority replication*) guarantees it: 15 minutes or less, for block blobs. Without that feature, there is no guaranteed RPO. Failing over to the secondary is an action *you* start, and it is a decision with consequences: after an unplanned failover, the copy in the original region is deleted, and the account is only locally redundant in the new primary until you reconfigure geo-redundancy. Rehearse it before you need it.

### Access tiers: cost versus latency

| Tier | For | Minimum storage period (else an early-deletion charge) | Read latency |
|---|---|---|---|
| Hot | Frequently accessed | none | ms |
| Cool | Infrequent access | 30 days | ms |
| Cold | Rare access, still online | 90 days | ms |
| Archive | Keep for years, rarely read | 180 days | **hours**: the blob must be *rehydrated* first |

Rehydrating an archived blob with standard priority can take up to 15 hours. High priority can finish in under an hour for blobs under 10 GB, and costs more. Use **lifecycle management policies** (rules such as "move blobs under `logs/` to cool after 30 days and delete them after 365") instead of writing a cleanup job. The early-deletion charge also applies when a blob is *overwritten*, so a job that rewrites a cool-tier blob every day pays for 30 days each time.

### Authorising access: prefer Entra, then user delegation SAS

| Method | Use it for | Revocation |
|---|---|---|
| **Entra ID (RBAC)** | Your own services, through a managed identity | Remove the role assignment |
| **User delegation SAS** | Giving a *client* (a browser, a partner) time-limited access to specific blobs | Revoke the user delegation keys; the SAS is valid for at most 7 days |
| Service SAS with a **stored access policy** | Legacy integrations that need longer-lived access | Delete or change the policy |
| Account key / account SAS | Nothing new | Rotate the key, which breaks everyone using it |

The pattern behind every "upload a large file" feature: the API does **not** stream the file through itself (think of the 230-second limit and the memory it would need). It hands the client a short-lived, narrowly scoped SAS, and the client uploads directly to Blob Storage:

```csharp
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;

public sealed class UploadUrlIssuer(BlobServiceClient blobService)   // authenticated with managed identity
{
    public async Task<Uri> IssueAsync(string tenantId, string fileName, CancellationToken ct)
    {
        // A user delegation key is signed by Entra ID, not by the account key. The identity needs
        // a role with the generateUserDelegationKey action (Storage Blob Delegator, or a Data role).
        DateTimeOffset now = DateTimeOffset.UtcNow;
        UserDelegationKey key = await blobService.GetUserDelegationKeyAsync(
            startsOn: now.AddMinutes(-5), expiresOn: now.AddHours(1), ct);

        BlobClient blob = blobService
            .GetBlobContainerClient("uploads")
            .GetBlobClient($"{tenantId}/{Guid.NewGuid():N}/{fileName}");

        var sas = new BlobSasBuilder
        {
            BlobContainerName = blob.BlobContainerName,
            BlobName = blob.Name,
            Resource = "b",                                // this one blob
            StartsOn = now.AddMinutes(-5),                 // allow for clock skew
            ExpiresOn = now.AddMinutes(15),
        };
        sas.SetPermissions(BlobSasPermissions.Create | BlobSasPermissions.Write);   // no read, no list, no delete

        return new BlobUriBuilder(blob.Uri)
        {
            Sas = sas.ToSasQueryParameters(key, blobService.AccountName),
        }.ToUri();
    }
}
```

An Event Grid subscription on `Microsoft.Storage.BlobCreated` then tells your backend that the upload finished. There is no polling, and the backend never touches the bytes until it needs to process them.

### Concurrency: ETags and leases

Two instances of your service that read, modify and write the same blob will lose updates, unless you use one of the two concurrency mechanisms Blob Storage provides:

- **Optimistic: ETags.** Every write returns a new `ETag`. Pass the ETag you read as `IfMatch` on the write. If someone else wrote in between, the service answers `412 Precondition Failed`, and you re-read and retry. This is cheap, and correct for low contention.
- **Pessimistic: leases.** Acquire a lease (15–60 seconds, or infinite) and hold it while you work. Writes without the lease ID fail. Leases are also a common building block for leader election ("only the instance holding the lease on `leader.lock` runs the scheduler").

Chapter 51's first *Find the bug* exercise is the missing ETag, verified against Azurite.

### Queue Storage or Service Bus?

| | Queue Storage | Service Bus queue |
|---|---|---|
| Message size | 64 KB | 256 KB (Standard); up to 100 MB (Premium) |
| Ordering | None | FIFO per session |
| Dead-lettering | None built in (you inspect `DequeueCount` yourself) | Automatic after MaxDeliveryCount, plus a DLQ per entity |
| Duplicate detection, transactions, scheduled messages, topics | No | Yes |
| Cost and scale | Very cheap; very large queues | More features; per-operation or per-messaging-unit pricing |

Choose Queue Storage for simple, high-volume, "just do this eventually" work. Choose Service Bus as soon as a business process depends on the message.

## Cosmos DB for NoSQL

Cosmos DB is not "a NoSQL database you can put anything in". It is a partitioned key-value and document store with a price for every operation. It rewards designs that respect its model, and it punishes designs that ignore it with throttling and bills.

### The model: partitions and request units

```
  account ─► database ─► container (partition key path, e.g. /tenantId)
                              │
          logical partitions: all items with the same partition key value
          (/tenantId = "acme", /tenantId = "globex", …), up to 20 GB each
                              │   many logical partitions are hashed onto each
          physical partitions: up to 10,000 RU/s and 50 GB each; Cosmos splits them as you grow
```

- **Request units (RU)** are the currency. Every operation costs RUs: a point read of a small item costs about 1 RU, and writes, queries and larger items cost more. You provision RU/s, or use serverless and pay per RU. Every SDK response carries the charge in `RequestCharge`, so log it in development and look at it in code review.
- **A single logical partition cannot exceed 10,000 RU/s**, because it lives on exactly one physical partition. Provisioning 50,000 RU/s does not help a workload that sends all its traffic to one partition key value. That is a **hot partition**, and it throttles at a fraction of what you pay for.
- **A logical partition is limited to 20 GB** (hierarchical partition keys lift this limit).

### Choosing the partition key: the decision you cannot easily undo

A good partition key has **high cardinality** (many distinct values), **spreads both storage and requests evenly**, and **appears in the filter of your most frequent queries**, so that they are single-partition queries. You cannot change a container's partition key; you migrate the data to a new container.

| Workload | Candidate | Verdict |
|---|---|---|
| Multi-tenant SaaS, queries are always per tenant | `/tenantId` | Good, *unless* one tenant is 30% of the traffic. Then use a hierarchical key `/tenantId` + `/userId`. |
| Orders, read by order ID, listed per customer | `/customerId` | Good: the order list is single-partition, and a point read needs `id` + `customerId`. |
| IoT telemetry, millions of devices, queried per device and time range | `/deviceId` (hierarchical: `/deviceId`, `/yyyyMM`) | Good; the second level keeps each partition under 20 GB. |
| Anything | `/status`, `/country`, `/type` | **Bad.** Low cardinality, so a few enormous hot partitions. |
| Event log | `/createdDate` (the day) | **Bad.** All of today's writes go to one partition. |

The query that forgets the partition key is a **cross-partition query**: it fans out to every physical partition, costs RUs on each one, and gets more expensive as the container grows. It is fine for a rare admin screen and fatal on the hot path.

```csharp
using Microsoft.Azure.Cosmos;

public sealed class OrderStore(Container orders, ILogger<OrderStore> log)   // Container from a singleton CosmosClient
{
    public async Task<Order?> GetAsync(string customerId, string orderId, CancellationToken ct)
    {
        try
        {
            // Point read: id + partition key. The cheapest operation Cosmos DB has.
            ItemResponse<Order> read = await orders.ReadItemAsync<Order>(orderId, new PartitionKey(customerId), cancellationToken: ct);
            log.LogDebug("Point read cost {RU} RU", read.RequestCharge);
            return read.Resource;
        }
        catch (CosmosException e) when (e.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<List<Order>> ListOpenAsync(string customerId, CancellationToken ct)
    {
        var query = new QueryDefinition("SELECT * FROM c WHERE c.customerId = @c AND c.status = 'Open'")
            .WithParameter("@c", customerId);

        // Scoping the query to one partition key keeps it single-partition.
        using FeedIterator<Order> it = orders.GetItemQueryIterator<Order>(
            query, requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(customerId) });

        var result = new List<Order>();
        double charge = 0;
        while (it.HasMoreResults)
        {
            FeedResponse<Order> page = await it.ReadNextAsync(ct);
            charge += page.RequestCharge;
            result.AddRange(page);
        }
        log.LogDebug("Open-orders query cost {RU} RU for {Count} items", charge, result.Count);
        return result;
    }
}
```

### Consistency levels

Cosmos DB offers five, from strongest to weakest: **strong**, **bounded staleness**, **session** (the default), **consistent prefix** and **eventual**. Weaker levels cost fewer RUs for reads and give lower latency and higher availability across regions.

**Session** is the one to understand, because it is the default. It guarantees read-your-own-writes *within a session*, and the session is identified by a **session token** that the SDK tracks per `CosmosClient` instance. Two instances of your API behind a load balancer have two different clients. So a user who writes through instance A and reads through instance B may not see their own write, if B reads from a replica that has not caught up yet. The fixes are to read from the same instance, or to pass the session token along (return it to the client and send it back on the next request as `ItemRequestOptions.SessionToken`), or to accept the staleness in the UX.

### Indexing, change feed, TTL

- **Indexing policy.** By default every property of every item is indexed. This makes any query possible, and it makes every write pay for indexing properties nobody queries. For write-heavy containers with large documents, exclude paths (`"excludedPaths": [{ "path": "/payload/*" }]`). Add **composite indexes** for `ORDER BY` on two or more properties, or the query fails.
- **Change feed.** A persistent, ordered-per-partition-key record of changes that you consume with the *change feed processor*. The processor keeps its position in a *lease container* and spreads partitions across your instances. It is the natural way to build projections, search indexing and outbox relays ([Chapter 9](#chapter-9-messaging-distributed-systems)). The default *latest version* mode does **not** record deletes, so a consumer never learns an item is gone. Either soft-delete (set a flag, then let TTL remove the item later) or use *all versions and deletes* mode.
- **TTL.** Set a default time-to-live on the container, and override it per item with a `ttl` property. Expired items are removed in the background using spare RUs, with no delete job to write.

### SDK rules that prevent most incidents

- **One `CosmosClient` per account, for the life of the process.** It holds connections, the partition map and the session tokens. A client per request is the Cosmos version of the SNAT problem, and it also throws the session tokens away.
- **Keep the default direct connection mode** unless a firewall forces gateway mode.
- **Read `429`s as a signal, not as noise.** The SDK retries throttled requests for you: 9 attempts by default, waiting up to 30 seconds in total. A `CosmosException` with status 429 that reaches your code means those retries ran out. The response carries `RetryAfter`. Look for a hot partition before you buy more RU/s.
- **Use `_etag` for optimistic concurrency** (`ItemRequestOptions.IfMatchEtag`), exactly as with blobs.

## Azure SQL Database

For a .NET team, Azure SQL is SQL Server with someone else doing the patching, backups and high availability. The differences are in how you buy it, how it fails, and how you connect to it.

### Buying it

| Choice | Options | Guidance |
|---|---|---|
| Deployment | Single database · Elastic pool · Managed Instance | A pool shares compute across many small databases with uneven load (a database per tenant). Managed Instance gives near-full SQL Server compatibility (SQL Agent, cross-database queries) for lift-and-shift. |
| Purchasing model | DTU (bundled) · vCore | vCore: you choose compute and storage separately, and you can use reservations and Azure Hybrid Benefit. |
| Service tier | General Purpose · Business Critical · Hyperscale | Business Critical gives local SSD storage and a free readable secondary replica. Hyperscale scales storage to very large sizes and adds replicas quickly. |
| Compute | Provisioned · **Serverless** | Serverless scales vCores automatically and can **auto-pause** when idle, which is ideal for dev/test. The first connection after a pause resumes the database, and until it is back that connection fails with a transient error. |

### It will fail over, so retry

A managed database moves between nodes for patching, scaling and hardware failures. Each move is a few seconds of transient errors: `40613` ("database … is not currently available"), `40197`, `40501` ("the service is currently busy"), `49918` and others. Microsoft's guidance is blunt: every cloud application must handle transient connection errors with retry logic, and serverless databases need it most. With EF Core, turn on the SQL Server execution strategy:

```csharp
builder.Services.AddDbContext<ShopDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("Shop"),
        sql => sql.EnableRetryOnFailure(
            maxRetryCount: 5,
            maxRetryDelay: TimeSpan.FromSeconds(10),
            errorNumbersToAdd: null)));
```

> **Gotcha.** With a retrying execution strategy, a transaction you start yourself (`BeginTransaction`) throws, because EF Core cannot replay half a transaction. Get the strategy with `CreateExecutionStrategy()` and run the whole unit of work inside its `ExecuteAsync`, so that a retry repeats *all* of it, including the reads (Chapter 51, Case 14, shows the code). [Chapter 4](#chapter-4-data-access-databases) covers EF Core's transactions in depth.

### Connect with an identity, not a password

`Microsoft.Data.SqlClient` gets the Entra token for you:

```
Server=tcp:shop-sql.database.windows.net,1433;Database=Shop;
Authentication=Active Directory Managed Identity;User Id=<client-id-of-user-assigned-identity>;
Encrypt=True;
```

`Active Directory Default` uses a `DefaultAzureCredential`-style chain, so the same connection string also works on a laptop after `az login`. The database has to know the identity, so an Entra admin runs this once per database:

```sql
CREATE USER [shop-api-identity] FROM EXTERNAL PROVIDER;   -- the managed identity's name
ALTER ROLE db_datareader ADD MEMBER [shop-api-identity];
ALTER ROLE db_datawriter ADD MEMBER [shop-api-identity];
```

Then turn on *Microsoft Entra-only authentication* on the server, and SQL logins stop working.

**Connectivity policy.** Clients inside Azure are, by default, *redirected* to talk directly to the node that hosts the database, which gives lower latency than going through the gateway for every packet. So outbound firewalls must allow ports **11000–11999** to the region's Azure SQL addresses (the `Sql` service tag), not just 1433. "It connects from my laptop but times out from the VM" is often exactly this.

### High availability and disaster recovery

- **Zone redundancy** spreads the replicas across availability zones in one region.
- **Active geo-replication** keeps readable secondaries in other regions.
- **Failover groups** add two *listener* endpoints that move with the primary: `<group>.database.windows.net` for read-write and `<group>.secondary.database.windows.net` for read-only. **Always connect through the listener**, so that a regional failover needs no configuration change. Geo-replication is asynchronous, so a forced failover can lose recent transactions, and your RPO has to account for that.

The database tuning skills from [Chapter 37](#chapter-37-the-slow-query-lab-reading-execution-plans) (its SQL Server track) transfer directly. Azure SQL has Query Store on by default, and it adds *automatic tuning*, which can create indexes, drop indexes and force last-known-good plans for you. Review what it does. Do not let it replace understanding.

## Messaging: Service Bus, Event Hubs and Event Grid

[Chapter 9](#chapter-9-messaging-distributed-systems) teaches the patterns: outbox, idempotent consumer, saga. This section is about the three Azure brokers, which are three different tools rather than three brands of the same one:

| | **Service Bus** | **Event Hubs** | **Event Grid** |
|---|---|---|---|
| Shape | A broker with queues and topics | A partitioned, append-only log | Push-based event routing |
| A message is … | A command or business event someone must act on | One of millions of telemetry or clickstream records | A notification that something happened ("blob created") |
| Consumption | Competing consumers, peek-lock, settle each message | Each consumer group reads the log at its own position, and checkpoints | Delivered to your endpoint (webhook, Function, queue), with retries |
| Ordering | FIFO per session | Per partition | None guaranteed |
| After it is read | It is gone, once completed | It stays until retention expires; replay is possible | It is gone, once delivered |
| Pick it when | "Charge this card, exactly once in effect" | "Ingest 100k events/s and let three teams read them" | "React when a blob lands or a resource changes" |

### Service Bus

**Tiers.** *Basic* has queues only. *Standard* adds topics, sessions, transactions and duplicate detection, on shared infrastructure. *Premium* runs on dedicated capacity (messaging units), adds private endpoints and larger messages, and is the tier for production workloads that need predictable latency. Messages are limited to 256 KB on Standard. Premium allows 1 MB by default, and up to 100 MB with large-message support.

**Peek-lock, the default and the one to use.** A receiver gets a message and a **lock** on it. Until the lock expires, no other receiver sees the message. The receiver then *settles* it:

```
            receive (peek-lock)
  queue ──────────────────────────► handler ──┬── Complete  → deleted
    ▲                                         ├── Abandon   → back in the queue now, DeliveryCount + 1
    │                                         ├── Dead-letter → moved to <queue>/$deadletterqueue
    │                                         └── Defer     → set aside, retrievable by sequence number
    │   lock expires (1 min by default,
    └── max 5 min) before settlement → back in the queue, DeliveryCount + 1
                                        DeliveryCount > MaxDeliveryCount (default 10) → dead-lettered
```

The facts that matter, and that interviewers ask about:

- **The lock duration defaults to 1 minute, with a maximum of 5.** Work that takes longer must *renew* the lock. `ServiceBusProcessor` does this automatically for up to `MaxAutoLockRenewalDuration` (5 minutes by default). If the lock expires, `CompleteMessageAsync` throws `MessageLockLost`, and the message is delivered again, *after your side effects have already happened*. Chapter 51's second *Find the bug* exercise shows this, verified against the Service Bus emulator.
- **Delivery is at-least-once, always.** Locks expire, processes crash between the side effect and `Complete`, networks drop the settlement. Every consumer must be **idempotent** (Chapter 9's inbox table, or a natural key check).
- **The dead-letter queue does not drain itself.** Messages stay there until someone reads them. Put an alert on the dead-letter message count (the `DeadletteredMessages` metric), and have a tool to inspect, fix and resubmit messages. A DLQ nobody watches is a silent data-loss mechanism.
- **Sessions give ordered processing per key.** Set `SessionId = customerId`, and the queue delivers each session's messages in order to one receiver at a time, while different sessions are processed in parallel. Session state (up to one message's size) lets the receiver keep a small state machine per session.
- **Duplicate detection** discards a message whose `MessageId` was already seen within a time window (10 minutes by default, 20 seconds to 7 days). It protects against a *sender* that retries after a timeout. It does nothing about consumer-side redelivery, so it does not replace idempotent consumers.
- **Scheduled messages** (`ScheduledEnqueueTime`) and **transactions** (receive, process and send atomically *within the same namespace*) round out the features. A transaction cannot include your database. That is what the outbox pattern is for.

**The .NET client.** `ServiceBusClient` owns the AMQP connection, so create one per namespace for the life of the process. Senders, receivers and processors are cheap and share it. Know the processor defaults, because they are conservative:

| `ServiceBusProcessorOptions` | Default | Note |
|---|---|---|
| `MaxConcurrentCalls` | **1** | One message at a time. Raise it deliberately, with the downstream capacity in mind. |
| `AutoCompleteMessages` | `true` | Completes the message when your handler returns, and abandons it when the handler throws. |
| `MaxAutoLockRenewalDuration` | 5 minutes | Longer handlers need a larger value. |
| `PrefetchCount` | 0 | Prefetched messages are *locked while they wait in memory*. A large prefetch plus slow processing means expired locks and redeliveries. |
| `ReceiveMode` | `PeekLock` | `ReceiveAndDelete` is at-most-once: a crash loses the message. |

```csharp
ServiceBusProcessor processor = client.CreateProcessor("payments", new ServiceBusProcessorOptions
{
    MaxConcurrentCalls = 8,                                 // sized to what the payment provider tolerates
    MaxAutoLockRenewalDuration = TimeSpan.FromMinutes(10),
});

processor.ProcessMessageAsync += async args =>
{
    var command = args.Message.Body.ToObjectFromJson<ChargeCard>()!;
    try
    {
        await payments.ChargeOnceAsync(command, args.CancellationToken);   // idempotent on command.PaymentId
    }
    catch (CardDeclinedException e)
    {
        // A business failure: retrying cannot help, so dead-letter it with a reason a human can read.
        await args.DeadLetterMessageAsync(args.Message, "CardDeclined", e.Message, args.CancellationToken);
    }
    // Any other exception: the processor abandons the message → it is retried, up to MaxDeliveryCount.
};
processor.ProcessErrorAsync += args =>
{
    logger.LogError(args.Exception, "Service Bus error in {Source} on {Entity}", args.ErrorSource, args.EntityPath);
    return Task.CompletedTask;
};
await processor.StartProcessingAsync();
```

Separate *transient* failures (throw, and let the message be retried) from *permanent* ones (dead-letter immediately with a reason). Otherwise, a message that can never succeed burns through all 10 delivery attempts first, and each attempt may repeat a side effect.

### Event Hubs

Event Hubs is a **partitioned log**, the same model as Kafka (it even speaks the Kafka protocol).

- An event hub has **partitions**: up to 32 on Basic and Standard. Events with the same **partition key** land in the same partition, and ordering is guaranteed only within a partition. You cannot usually change the partition count later on Standard, so size it for peak parallelism.
- A **consumer group** is an independent view of the log, with its own position in each partition. Basic allows 1 consumer group, Standard 20. Give each consuming application its own group.
- **Retention** is 1 day on Basic, up to 7 on Standard, and up to 90 on Premium and Dedicated. Consumers can re-read anything still retained, which is how you replay after a bug fix.
- **Checkpointing.** `EventProcessorClient` balances partitions across your instances and stores each partition's position in a Blob container. You choose when to checkpoint: after every event is safest and slowest; every N events or T seconds is faster, and on restart the events since the last checkpoint are read again. So Event Hubs consumers must be idempotent too.
- **Capture** writes the stream to Blob Storage or Data Lake as Avro files, which gives you an archive with no consumer code.

### Event Grid

Event Grid **pushes** discrete events to subscribers. **System topics** publish Azure's own events (`Microsoft.Storage.BlobCreated`, resource-group changes, Key Vault secret near expiry, …). **Custom topics** and **domains** carry your events. Subscriptions filter by event type, subject prefix or suffix, and data fields, and can deliver in the CloudEvents 1.0 schema.

- **Retries.** If delivery fails, Event Grid retries with exponential backoff. By default it gives up after **30 attempts or 24 hours** (1,440 minutes), whichever comes first, and both are configurable. Configure a **dead-letter** destination (a blob container), or events that exhaust their retries are dropped.
- **Webhook validation.** Before Event Grid sends events to an HTTP endpoint, it proves that you own the endpoint. It sends a `SubscriptionValidationEvent`, and your endpoint must echo `validationCode` back synchronously (or, if it cannot, someone must `GET` the `validationUrl` in the event). An endpoint that returns `200` without the code never receives a real event. Subscribing an Azure Function or a Service Bus queue avoids this, and it is usually the better design anyway: the queue absorbs bursts and gives you peek-lock semantics.
- **Delivery is at-least-once, and not ordered.** Deduplicate on the event `id`.

## Configuration and Secrets: Key Vault and App Configuration

| Kind of value | Where it lives |
|---|---|
| Defaults that are the same everywhere | `appsettings.json` in the repo |
| Per-environment, non-secret values (URLs, feature switches, timeouts) | App Service / Container Apps settings, or **App Configuration** with a label per environment |
| Secrets you cannot remove (a third-party API key, a signing certificate) | **Key Vault**, referenced by the app, never copied into settings |
| Credentials for Azure services | **Nothing.** A managed identity replaces them. |

### Key Vault

- A vault holds **secrets** (strings), **keys** (RSA/EC keys you can use but never export, such as for signing or envelope encryption) and **certificates** (with auto-renewal from supported CAs).
- Use the **Azure RBAC permission model** (roles such as *Key Vault Secrets User*), not the older vault access policies. RBAC can be scoped down to a single secret, and it is audited like every other role assignment.
- **Soft delete** is always on: deleted vaults and objects are recoverable for 7–90 days (90 by default, chosen when the vault is created). **Purge protection** stops anyone, including an Owner, from purging before that period ends. The gotcha is that a soft-deleted vault or secret still owns its name, so IaC that deletes and re-creates a vault with the same name fails until the old one is purged, or recovered.
- **Key Vault is throttled** per vault, and it answers `429` when you exceed the limits. It is designed for loading secrets at start-up and on rotation, not for reading them on every request. Load secrets through the configuration system once, and reload them periodically:

```csharp
// Package: Azure.Extensions.AspNetCore.Configuration.Secrets
using Azure.Extensions.AspNetCore.Configuration.Secrets;

builder.Configuration.AddAzureKeyVault(
    new Uri(builder.Configuration["KeyVault:Uri"]!),
    credential,
    new AzureKeyVaultConfigurationOptions
    {
        ReloadInterval = TimeSpan.FromMinutes(30),   // picks up rotated secrets without a restart
    });

// A secret named "Stripe--ApiKey" becomes the configuration key "Stripe:ApiKey",
// so it binds to options classes like any other setting.
builder.Services.Configure<StripeOptions>(builder.Configuration.GetSection("Stripe"));
```

When you rotate a secret, the application must be able to *use* the new value. `IOptionsMonitor<T>` sees reloaded configuration, while `IOptions<T>` is fixed at start-up. For credentials that you cannot switch atomically, use the **two-key pattern**: the provider accepts key A and key B at the same time, you rotate B while clients use A, switch the clients to B, then rotate A.

### App Configuration and feature flags

App Configuration is a central key-value store with **labels** (for example one per environment), **Key Vault references**, and **feature flags** that `Microsoft.FeatureManagement` understands (including percentage rollouts and targeting). Its best trick is dynamic refresh driven by a **sentinel key**: the app watches one key, and when you change it, the app reloads all its configuration. That way, a batch of related edits takes effect together. Note that the configuration provider and the refresh middleware come from two different packages:

```csharp
// Packages: Microsoft.Extensions.Configuration.AzureAppConfiguration (the provider)
//           Microsoft.Azure.AppConfiguration.AspNetCore (UseAzureAppConfiguration, the refresh middleware)
builder.Configuration.AddAzureAppConfiguration(options =>
{
    options.Connect(new Uri(builder.Configuration["AppConfig:Endpoint"]!), credential)
        .Select(KeyFilter.Any, LabelFilter.Null)
        .Select(KeyFilter.Any, builder.Environment.EnvironmentName)       // environment label overrides
        .ConfigureKeyVault(kv => kv.SetCredential(credential))            // resolves Key Vault references
        .ConfigureRefresh(refresh => refresh
            .Register("App:Sentinel", refreshAll: true)                   // bump this key to reload everything
            .SetRefreshInterval(TimeSpan.FromSeconds(30)))
        .UseFeatureFlags();
});
builder.Services.AddAzureAppConfiguration();

var app = builder.Build();
app.UseAzureAppConfiguration();   // triggers the refresh check on incoming requests
```

## API Management

API Management (APIM) is a gateway in front of your APIs. It gives you one place to handle authentication, rate limiting, transformation, caching and a developer portal. Its pieces:

- **APIs** and their **operations**, imported from OpenAPI.
- **Products** group APIs, and **subscriptions** to a product issue subscription keys. These keys identify a *caller*. They are not strong authentication, so pair them with `validate-jwt` (or `validate-azure-ad-token`).
- **Policies** are XML pipelines that run in four stages: `inbound`, `backend`, `outbound` and `on-error`, at global, product, API or operation scope.

```xml
<policies>
  <inbound>
    <base />
    <validate-azure-ad-token tenant-id="{{tenant-id}}">
      <audiences><audience>api://orders-api</audience></audiences>
    </validate-azure-ad-token>
    <rate-limit-by-key calls="100" renewal-period="60"
                       counter-key="@(context.Request.Headers.GetValueOrDefault("Authorization",""))" />
    <set-backend-service backend-id="orders-api-backend" />
  </inbound>
  <backend><base /></backend>
  <outbound><base /></outbound>
  <on-error><base /></on-error>
</policies>
```

Two distinctions come up in exams and design reviews. **Rate limit vs quota**: a rate limit protects the backend from bursts (calls per short period), while a quota enforces a usage contract (calls or bandwidth per week or month). **Revisions vs versions**: a revision is a non-breaking change you can test and then make current, and a version is a breaking change that callers opt into (`/v2/`, a header, or a query string). Tiers range from *Consumption* (serverless, pay per call) through *Developer* (no SLA, for non-production use only) to Basic, Standard and Premium, and their v2 variants. Networking options and multi-region deployment differ by tier (multi-region needs Premium), so check the tier comparison before you design around them.

## Networking for Application Developers

You do not need to design hub-and-spoke topologies. You do need to know enough to keep your service reachable by the right callers only, and to debug it when it is not. Most of that comes down to five concepts:

- A **VNet** is your private network in a region, divided into **subnets**, with **network security groups (NSGs)** acting as allow/deny rules on subnets and NICs.
- A **private endpoint** gives a PaaS resource (a storage account, a vault, a SQL server, a Service Bus namespace) a **private IP inside your VNet**. With public network access disabled on the resource, that private IP is the only way in. It controls **inbound** access *to* the service.
- A **service endpoint** is the older, simpler option: traffic from a subnet reaches the service over the Azure backbone, and the service's firewall can allow that subnet. The service still has a public IP, and the rule is per subnet, not per resource.
- **App Service / Functions VNet integration** controls **outbound** traffic: it lets your app reach private endpoints and other resources *in* the VNet. It does not make your app private. For that, you add a private endpoint to the *app*, or put a Front Door or Application Gateway in front of it and restrict access to it.
- **Private DNS** is how the name finds the private IP, and it is the part that breaks.

```
  app (VNet-integrated) asks DNS for  shopdata.blob.core.windows.net
        │
        ▼
  public DNS: CNAME → shopdata.privatelink.blob.core.windows.net
        │
        ├── Private DNS zone "privatelink.blob.core.windows.net" LINKED to the app's VNet?
        │        yes → A record → 10.1.2.5  (the private endpoint)                         ✓
        │        no  → resolves to the PUBLIC IP → the storage firewall says 403 (public access off) ✗
        │
        └── the app uses custom DNS servers (on-premises, a firewall)? Then THOSE servers must forward
            privatelink.* to Azure DNS (168.63.129.16), or the lookup never reaches the private zone.
```

The first command to run when "the private endpoint doesn't work" is a DNS lookup *from where the app runs* (the Kudu console or SSH on App Service, `az containerapp exec` on Container Apps): `nslookup shopdata.blob.core.windows.net`. A public IP in the answer means the problem is DNS, not the firewall, not RBAC and not your code. The zone names follow a pattern: `privatelink.blob.core.windows.net`, `privatelink.vaultcore.azure.net`, `privatelink.database.windows.net`, `privatelink.servicebus.windows.net`, `privatelink.documents.azure.com`.

**Traffic in front of your app:**

| Service | Layer | Scope | Use it for |
|---|---|---|---|
| **Front Door** | L7 (HTTP) | Global | Multi-region web apps and APIs: anycast entry, WAF, CDN caching, failover between regions |
| **Application Gateway** | L7 | Regional | WAF and path-based routing inside one region, including to private back ends |
| **Load Balancer** | L4 (TCP/UDP) | Regional | Non-HTTP traffic, VMs |
| **Traffic Manager** | DNS | Global | DNS-based failover or geo-routing for anything, with DNS caching delays |

**Outbound**, a **NAT gateway** on the integration subnet gives your app a static egress IP (which partners can allow-list) and 64,512 SNAT ports per public IP. It is the infrastructure half of the SNAT fix described in the App Service section.

## Observability: Application Insights and KQL

[Chapter 13](#chapter-13-observability) explains the three pillars and OpenTelemetry. On Azure, they land in **Azure Monitor**: *metrics* (numeric time series, cheap, retained for 93 days, the basis for fast alerts) and *logs* (records in a **Log Analytics workspace**, queried with **KQL**). **Application Insights** is the application-performance view over a workspace: requests, dependencies, exceptions and traces, correlated by operation ID into an end-to-end transaction.

For .NET, the recommended way in is the **Azure Monitor OpenTelemetry Distro**:

```csharp
using Azure.Monitor.OpenTelemetry.AspNetCore;

builder.Services.AddOpenTelemetry().UseAzureMonitor(options =>
{
    // The connection string comes from APPLICATIONINSIGHTS_CONNECTION_STRING.
    options.Credential = credential;        // Entra-authenticated ingestion, if the resource requires it
    options.TracesPerSecond = 5.0;          // the distro's default: rate-limited sampling at 5 traces/s
});
```

**Sampling decides your bill and what you can see.** Current versions of the distro default to *rate-limited* sampling of about 5 traces per second. You can change this with `TracesPerSecond`, or switch to percentage sampling with `SamplingRatio`. A sampled-out trace is gone, so a rare failure may not be in the data at all. Keep this in mind before you conclude that "it never happened".

**KQL you will use in your first week** (Application Insights table names, with workspace-based equivalents in brackets):

```kusto
// Failed requests by operation over the last hour  (AppRequests)
requests
| where timestamp > ago(1h) and success == false
| summarize failures = count() by name, resultCode
| order by failures desc

// p50/p95/p99 latency per endpoint, in 5-minute buckets
requests
| where timestamp > ago(6h)
| summarize percentiles(duration, 50, 95, 99) by name, bin(timestamp, 5m)
| render timechart

// Which dependency is slow or failing?  (AppDependencies)
dependencies
| where timestamp > ago(1h)
| summarize calls = count(), failed = countif(success == false), p95 = percentile(duration, 95) by type, target
| order by p95 desc

// Everything that happened in one request, across services  (join on operation_Id)
union requests, dependencies, exceptions, traces
| where operation_Id == "<operation id from an error report>"
| project timestamp, itemType, name, resultCode, duration, message, cloud_RoleName
| order by timestamp asc
```

**Alerts.** Use *metric alerts* for fast, cheap signals (HTTP 5xx rate, queue length, dead-letter count, CPU). Use *log search alerts* for anything that needs a query (a specific exception type, error rate per tenant). Both notify through **action groups** (email, SMS, webhook, Logic App). Add an **availability test** that calls your health endpoint from several regions: it catches the outage your own telemetry cannot see, because when the app is down it sends nothing.

> **Pitfall.** Setting a **daily cap** to control Application Insights cost. When the cap is reached, ingestion *stops* for the rest of the day, so the day you blow the budget is probably the day of the incident, and you go blind in the middle of it. Control volume with sampling, with log-level filtering (no `Information` logs from every EF Core query), and by not logging request bodies. Keep the cap as a safety net set well above normal volume.

## Infrastructure as Code, the Azure Way

Chapter 10 compares Bicep, Terraform and Pulumi. Whichever you choose, the Azure-specific skill is to **deploy the identity and its role assignments together with the resource**, so that access is reviewed in the same pull request as the thing it grants access to:

```bicep
param location string = resourceGroup().location
param appName string

resource identity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: '${appName}-id'
  location: location
}

resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: toLower(replace('${appName}data', '-', ''))
  location: location
  sku: { name: 'Standard_ZRS' }
  kind: 'StorageV2'
  properties: {
    allowSharedKeyAccess: false          // identity only: keys and account SAS stop working
    allowBlobPublicAccess: false
    minimumTlsVersion: 'TLS1_2'
  }
}

// Storage Blob Data Contributor: a built-in role definition ID, the same in every tenant.
var blobDataContributor = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'ba92f5b4-2d11-453d-a403-e96b0029c9fe')

resource blobAccess 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: storage
  name: guid(storage.id, identity.id, blobDataContributor)   // deterministic: re-deploying is idempotent
  properties: {
    roleDefinitionId: blobDataContributor
    principalId: identity.properties.principalId
    principalType: 'ServicePrincipal'   // avoids a race with Entra replication right after the identity is created
  }
}

output identityClientId string = identity.properties.clientId
output blobEndpoint string = storage.properties.primaryEndpoints.blob
```

Run `az deployment group what-if` in the pull request, so reviewers see what *will* change: especially deletions and replacements, which a template diff hides. For the inner development loop, the **Azure Developer CLI** (`azd up`) provisions the infrastructure and deploys the code from one template. From CI, deploy with the workload-identity federation described in *Identity*, so no secret is involved.

## Cost: The Developer's Share of the Bill

FinOps belongs to everyone ([Chapter 28](#chapter-28-compliance-data-privacy-cloud-cost-finops)), but some costs are set by code, and only the developer can see them:

| Decision in code or config | Cost mechanism |
|---|---|
| Cosmos DB partition key, indexing policy, cross-partition queries | RU/s you must provision to avoid 429s |
| Logging level and payload size | Log Analytics ingestion is charged per GB |
| Polling instead of events (a timer checking a blob, a loop peeking a queue) | Transactions per operation, plus compute that never sleeps |
| Premium SKUs copied from prod into every dev and PR environment | Idle capacity billed around the clock |
| Cross-region or internet egress (chatty calls to another region, serving large files without a CDN) | Bandwidth charges |
| Blobs written to hot storage and never tiered | Storage in the most expensive tier, forever |
| Orphans: disks of deleted VMs, public IPs, old snapshots, abandoned resource groups | Pure waste; tags plus a periodic review catch them |

Put a **budget with an alert** on every subscription, **tag** every resource with an owner, and make ephemeral environments easy to delete. The cheapest resource is the one that no longer exists.

## Self-check: Exam-Style Questions

Try each one before you open the answer. The questions mix AZ-900 fundamentals with developer-level mechanism, the way a good interviewer does.

**1.** Your App Service's system-assigned identity has the *Contributor* role on the storage account, but `BlobClient.DownloadContentAsync` returns 403 `AuthorizationPermissionMismatch`. Why?

<details>
<summary>Answer</summary>

Contributor is a **control-plane** role with no `DataActions`. Reading blobs with an Entra token needs a data-plane role such as *Storage Blob Data Reader* or *Storage Blob Data Contributor*, at the account or container scope. If the role was just assigned, also allow up to 10 minutes for it to take effect.
</details>

**2.** A request flows through Front Door (99.99%), App Service (99.95%) and Azure SQL (99.99%), all required. What is the best composite SLA you can claim, and what would raise it?

<details>
<summary>Answer</summary>

Multiply: 0.9999 × 0.9995 × 0.9999 ≈ **99.93%**. Adding components in series lowers it. Deploying the App Service + SQL pair in a second region behind Front Door, so that either region alone can serve, raises it, provided that failover actually works and the data layer has a replication story (for example, failover groups).
</details>

**3.** You need a background job that runs for 45 minutes once a night. Which Functions plans can run it as a single execution, and which cannot?

<details>
<summary>Answer</summary>

The **Consumption** plan cannot: its maximum timeout is 10 minutes. **Flex Consumption**, **Premium** and **Dedicated** can: they default to 30 minutes, so you raise `functionTimeout`, which can be unbounded. Better still, split it into restartable chunks (Durable Functions fan-out, or a Container Apps job), so that a scale-in or a platform update does not lose 44 minutes of work.
</details>

**4.** Which Service Bus feature would you use to guarantee that all events for one customer are processed in order, while different customers are processed in parallel?

<details>
<summary>Answer</summary>

**Sessions**: set `SessionId` to the customer ID on the queue or subscription, which must have sessions enabled. Each session is locked to one receiver at a time and delivered in FIFO order, and different sessions go to different receivers concurrently.
</details>

**5.** Duplicate detection is enabled with a 10-minute window. Does that make your consumer safe to charge credit cards without an idempotency check?

<details>
<summary>Answer</summary>

**No.** Duplicate detection drops a second *send* with the same `MessageId` within the window. It does nothing about *redelivery* of one message to your consumer: after a lock expires, after a crash before `Complete`, or after an abandon. Consumers must be idempotent.
</details>

**6.** A container's partition key is `/status`, with three values. The container has 30,000 RU/s provisioned, and it throttles at around 10,000 RU/s of real load. Explain.

<details>
<summary>Answer</summary>

A single logical partition (one `status` value) lives on one physical partition, and a physical partition serves at most 10,000 RU/s. With most traffic on one or two status values, the load cannot spread, whatever you provision: a hot partition. The fix is a high-cardinality key that appears in the main queries, which means migrating to a new container.
</details>

**7.** What is the difference between Azure Policy and RBAC? Give one example of each.

<details>
<summary>Answer</summary>

RBAC controls **who** can perform actions ("the CI identity may deploy to this resource group"). Policy controls **what** state resources may have, whoever creates them ("storage accounts must disable public network access", "only West Europe and North Europe"). A user with full RBAC permissions can still be blocked by a `Deny` policy.
</details>

**8.** You enabled a private endpoint for Key Vault and disabled public access. The app now fails with 403 "public network access is disabled". What do you check first, and how?

<details>
<summary>Answer</summary>

**DNS.** From inside the app (Kudu/SSH), run `nslookup <vault>.vault.azure.net`. If it returns a public IP, the `privatelink.vaultcore.azure.net` private DNS zone is missing, is not linked to the VNet the app uses, or custom DNS servers do not forward to Azure DNS. Also check that the app has VNet integration at all: without it, its outbound calls never enter the VNet.
</details>

**9.** Name the three blob access tiers that are online, the one that is not, and the catch with moving data to cool the day after writing it.

<details>
<summary>Answer</summary>

Hot, cool and cold are online. Archive is offline and must be rehydrated (up to 15 hours at standard priority). The catch is the minimum retention period (cool 30 days, cold 90, archive 180): deleting, overwriting or re-tiering earlier incurs an early-deletion charge. Tiering pays off only for data that really sits still.
</details>

**10.** Why is `new DefaultAzureCredential()` inside a controller action a bug, even though it works?

<details>
<summary>Answer</summary>

Each instance has its own token cache, so every request fetches a new token: extra latency, and possible throttling by the identity endpoint. On a misconfigured host, every request also walks the whole credential chain. Create the credential and the SDK clients once, as singletons.
</details>

**11.** A webhook endpoint subscribed to Event Grid returns `200 OK` to everything, yet the subscription stays in a failed provisioning state and no events arrive. Why?

<details>
<summary>Answer</summary>

The endpoint did not complete the **validation handshake**. Event Grid sends a `SubscriptionValidationEvent`, and the endpoint must return its `validationCode` in the response body (or someone must call the `validationUrl`). Returning `200` with no code is not a validation.
</details>

**12.** Which pricing option fits a steady production database that will run for the next three years, and which fits an interruptible nightly rendering batch?

<details>
<summary>Answer</summary>

A **3-year reservation** for the database: the biggest discount, for a committed baseline. **Spot** capacity for the batch job: a deep discount in exchange for possible eviction, which an interruptible, restartable job tolerates.
</details>

**13.** Your team deletes and re-creates a Key Vault named `kv-shop-dev` in every test run, and the second run fails with "vault name already in use". Why?

<details>
<summary>Answer</summary>

**Soft delete**: the deleted vault still reserves its name for the retention period (7–90 days). Purge it after deletion (allowed only if purge protection is off, which is fine in dev), or give each run a unique name.
</details>

**14.** A developer argues that since Azure SQL is "managed", there is no need for retry logic. What do you answer?

<details>
<summary>Answer</summary>

"Managed" means that *Azure* performs failovers, patching and scaling operations, and each one produces seconds of transient errors (40613, 40197, 40501 …). Microsoft's guidance is that every cloud application must retry transient connection errors, and serverless databases even more so, because of resume after auto-pause. Enable `EnableRetryOnFailure`, and wrap explicit transactions in the execution strategy.
</details>

**15.** Which messaging service fits each case: (a) 200,000 device readings per second that three teams read independently; (b) "reserve stock for order 42"; (c) "run this function whenever a file lands in the `invoices` container"?

<details>
<summary>Answer</summary>

(a) **Event Hubs**: a partitioned log, with a consumer group per team, and replay within retention. (b) **Service Bus**: a command that needs exactly one competing consumer, with peek-lock, dead-lettering and possibly sessions. (c) **Event Grid** (the `BlobCreated` system event), delivered to the Function directly, or via a Service Bus queue for buffering and retries.
</details>

## Summary

Azure is organised as a hierarchy of tenant, management groups, subscriptions, resource groups and resources. All of it is managed through one API, ARM. RBAC decides *who*, Policy decides *what*, and locks protect resources but not data. The one distinction to carry everywhere is **control plane versus data plane**: managing a resource and using it are different permissions.

Identity is the foundation. Managed identities replace secrets. `DefaultAzureCredential` is a convenience for development, and production should name its credential. Role assignments take time to propagate, and Cosmos DB's data-plane roles are different from everyone else's. Once identity works, turn off keys.

For compute, App Service is the default for web workloads: learn the slot swap sequence, the 230-second limit and SNAT ports. Functions are for events: use the isolated worker model on Flex Consumption, remember that scale multiplies concurrency, and keep Durable orchestrators deterministic. Container Apps covers containerised systems without a cluster to run. For data, Blob Storage wants the right redundancy, tiers, user delegation SAS and ETags. Cosmos DB is a partitioning decision with a price on every operation. Azure SQL is SQL Server that fails over, so retry. Service Bus, Event Hubs and Event Grid are three different tools: commands, streams and notifications. All of them deliver at least once.

Around everything sit Key Vault and App Configuration (load once, reload deliberately), private endpoints (whose failures are almost always DNS), Application Insights (sampling and KQL), infrastructure as code that grants access in the same pull request as the resource, and the costs only a developer can see. [Chapter 51](#chapter-51-the-azure-casebook-real-incidents-real-fixes) takes all of this into production and breaks it.
