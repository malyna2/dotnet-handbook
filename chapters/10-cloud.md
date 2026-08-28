# Chapter 10: Cloud — AWS & Azure

_⏱️ Estimated read time: ~24 min ·     4156 words (study pace)_

For most of computing history, running software meant owning hardware. You bought servers, racked them in a room with expensive cooling, hired people to replace failed disks at 3 a.m., and paid for enough capacity to survive your busiest day of the year — capacity that sat idle the other 364 days. The cloud rewired this economic model. Instead of buying a power station, you plug into the grid and pay for the kilowatt-hours you actually use. That single shift in mindset — from *owning capacity* to *renting capability* — is the thread that runs through everything in this chapter.

As a .NET developer moving toward senior level, you don't need to become a network engineer. But you do need to understand what these services *are*, *why* they exist, and *when* to reach for each one. This chapter builds that mental map for both AWS and Azure, then shows you how to provision it all as code.

## Cloud Fundamentals

### The service models: IaaS, PaaS, SaaS, Serverless

Think of the cloud as a spectrum of how much of the stack you manage versus how much the provider manages.

- **IaaS (Infrastructure as a Service)** hands you raw compute, storage, and networking. You get a virtual machine and an empty disk; the OS, runtime, patching, and your app are all yours. This is renting an empty apartment — you bring your own furniture. Examples: AWS EC2, Azure Virtual Machines.
- **PaaS (Platform as a Service)** gives you a managed runtime. You push code; the platform handles the OS, patching, load balancing, and scaling. This is a serviced apartment — furnished, cleaned, utilities included. Examples: Azure App Service, AWS Elastic Beanstalk.
- **SaaS (Software as a Service)** is finished software you just log into. You manage nothing but your data and settings. Think Microsoft 365 or Salesforce.
- **Serverless** is PaaS taken to its logical extreme: you deploy a *function* or a *container*, and the platform runs it only when there's work, scaling to zero when idle. There are still servers — you just never think about them. Examples: AWS Lambda, Azure Functions.

The trade-off across this spectrum is **control versus convenience**. IaaS gives you maximum flexibility and maximum operational burden. Serverless gives you minimal burden but forces you into the platform's execution model. Senior engineers pick the *least* powerful option that still meets the requirement — because every knob you're handed is a knob you must maintain.

### Regions and Availability Zones

A **region** is a geographic area (e.g. `us-east-1` in AWS, `westeurope` in Azure) containing multiple data centers. An **Availability Zone (AZ)** is one or more physically isolated data centers within a region, with independent power, cooling, and networking.

Why does this matter? Two reasons:

1. **Latency and compliance.** Deploy close to your users, and keep data in regions that satisfy legal requirements (GDPR data residency, for instance).
2. **Resilience.** If you spread your app across multiple AZs, a fire or power failure in one data center doesn't take you down. This is why production databases run "Multi-AZ."

> **Pitfall:** A single region is not a disaster-recovery strategy. AZs protect against a data-center failure; only *multi-region* protects against a whole-region outage. Multi-region is expensive and complex, so reserve it for systems that genuinely require it.

### The Shared Responsibility Model

This is the single most important security concept in the cloud, and misunderstanding it causes real breaches. The provider secures the cloud *itself*; you secure what you put *in* it.

- **The provider handles:** physical security, the hypervisor, the host OS, the network backbone. "Security *of* the cloud."
- **You handle:** your data, your access policies (IAM), your application code, network configuration (security groups, firewalls), OS patching on IaaS VMs, and encryption choices. "Security *in* the cloud."

> **Security note:** When an S3 bucket leaks customer data, it's almost never AWS's fault — it's a misconfigured bucket policy. The provider gave you a lock; you left the door open. The higher up the service spectrum you go (toward SaaS/serverless), the more the provider handles — but your *data* and *access control* are always yours.

### The pay-for-what-you-use mindset

Cloud billing is metered: per compute-second, per GB stored, per GB transferred, per request. This is liberating (no upfront capital) and dangerous (costs can silently balloon). Two habits will save you:

- **Tag everything** with owner, environment, and cost-center so you can attribute spend.
- **Watch egress.** Data flowing *out* of the cloud (to the internet, or between regions) is where surprise bills come from. Data *in* is usually free; data *out* is not.

> **Cost note:** The most expensive resource is the one you forgot to turn off. Idle dev VMs, unattached disks, and orphaned load balancers bill 24/7. Set budgets and alerts on day one.

## AWS Core Services

AWS is the largest cloud and reads like an alphabet soup. Here are the services that matter, grouped by what problem they solve.

### Compute

- **EC2 (Elastic Compute Cloud)** — virtual machines. The IaaS foundation. You choose an instance type (CPU/memory), an AMI (machine image), and you're responsible for the OS upward. Use it when you need full control or must run software that doesn't fit a managed model.
- **Lambda** — serverless functions. Upload code, and it runs in response to events (an HTTP call, a file upload, a queue message), billed per millisecond of execution. You never manage a server. Ideal for glue logic, event processing, and spiky workloads.
- **ECS/Fargate** — container orchestration. ECS runs Docker containers; **Fargate** is the serverless mode where you don't manage the underlying EC2 hosts at all — you just specify CPU/memory per container. This is the sweet spot for containerized .NET apps that need more than a function but don't warrant Kubernetes.
- **EKS (Elastic Kubernetes Service)** — managed Kubernetes. Reach for this only when you genuinely need Kubernetes' ecosystem and portability; it carries real operational complexity.

### Storage and databases

- **S3 (Simple Storage Service)** — object storage. Effectively infinite, cheap, durable storage for files, backups, static assets, and data lakes. Not a filesystem — it's key/value blobs accessed over HTTP. For static assets, front it with an edge CDN (**CloudFront** on AWS, **Azure Front Door** on Azure) to cache content close to users and cut both latency and egress.
- **RDS (Relational Database Service)** — managed relational databases (SQL Server, PostgreSQL, MySQL, and Amazon's Aurora). AWS handles backups, patching, and Multi-AZ failover. Use this instead of running a database on EC2 yourself.
- **DynamoDB** — managed NoSQL key/value and document store. Single-digit-millisecond latency at any scale, serverless billing. Great for high-throughput, simple-access-pattern workloads (session stores, shopping carts) — but you must design around its access patterns up front, because ad-hoc queries are painful.

### Messaging and events

- **SQS (Simple Queue Service)** — a managed message queue. One producer writes, one consumer reads and deletes. The backbone of decoupled, resilient systems: if the consumer is down, messages wait safely.
- **SNS (Simple Notification Service)** — pub/sub fan-out. One message published to a topic is delivered to many subscribers. Often paired with SQS (SNS fans out to multiple queues).
- **EventBridge** — an event bus for event-driven architectures. Richer than SNS: it routes events based on content-matching rules and integrates with dozens of SaaS sources. Use it as the central nervous system of an event-driven app.

### Networking, identity, and observability

- **VPC (Virtual Private Cloud)** — your isolated private network in AWS. You define subnets (public and private), route tables, and security groups (virtual firewalls). Databases live in private subnets, unreachable from the internet; only your app tier can talk to them.
- **IAM (Identity and Access Management)** — covered in depth below.
- **CloudWatch** — metrics, logs, alarms, and dashboards. Your app writes logs here; you set alarms on metrics (CPU, error rate) that trigger actions.
- **Secrets Manager / Parameter Store** — Secrets Manager stores and rotates secrets (DB passwords, API keys) with automatic rotation. Parameter Store (part of Systems Manager) is a cheaper, simpler config/secret store. Use these instead of putting secrets in environment variables or, worse, source control.

### IAM: roles and policies, deeply

IAM is where most AWS security lives, so it's worth understanding precisely. Four concepts:

- **Users** — long-lived identities for humans, with passwords or access keys.
- **Groups** — collections of users for shared permissions.
- **Policies** — JSON documents that *grant or deny* permissions. They're attached to users, groups, or roles.
- **Roles** — the crucial one. A role is an identity that can be *assumed temporarily* by a service, application, or user. It has no permanent credentials; instead it hands out short-lived, auto-rotating tokens.

Here's a policy that allows read-only access to one S3 bucket:

```json
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Effect": "Allow",
      "Action": ["s3:GetObject", "s3:ListBucket"],
      "Resource": [
        "arn:aws:s3:::my-app-uploads",
        "arn:aws:s3:::my-app-uploads/*"
      ]
    }
  ]
}
```

The mental model: every request is *denied by default*. A policy must explicitly `Allow` it, and any explicit `Deny` always wins. Policies combine additively across all sources.

> **Best practice — least privilege:** Grant only the exact actions on the exact resources needed. Start with nothing and add permissions as errors reveal what's actually required. Never attach `Administrator` "just to make it work."

The reason **roles** are the key to secure AWS is that they eliminate long-lived credentials. When your EC2 instance or Lambda function needs to read S3, you don't put an access key in the code — you attach a role. AWS injects temporary credentials that rotate automatically. This is the single most important habit for cloud security: **applications should never hold static keys.**

### AWS SDK for .NET example

The AWS SDK for .NET (the `AWSSDK.*` NuGet packages) follows a consistent client pattern. Notice there are *no credentials in the code* — when this runs on AWS with a role attached, the SDK finds the temporary credentials automatically via the default credential chain.

```csharp
using Amazon.S3;
using Amazon.S3.Model;

// No keys passed in: the SDK resolves credentials from the
// environment, a shared profile, or (in production) the IAM role
// attached to the EC2/ECS/Lambda compute running this code.
var s3 = new AmazonS3Client();

var request = new PutObjectRequest
{
    BucketName = "my-app-uploads",
    Key = $"invoices/{Guid.NewGuid()}.pdf",
    ContentType = "application/pdf",
    FilePath = "/tmp/invoice.pdf"
};

PutObjectResponse response = await s3.PutObjectAsync(request);
Console.WriteLine($"Uploaded, ETag: {response.ETag}");
```

### Infrastructure with CloudFormation and CDK

**CloudFormation** is AWS's native Infrastructure-as-Code service: you declare resources in YAML/JSON templates, and AWS creates and tracks them as a "stack." **AWS CDK (Cloud Development Kit)** lets you write that infrastructure in real languages — including C# — which then *synthesizes* to CloudFormation. For a .NET shop, CDK is attractive because you get types, loops, and IDE support instead of hand-writing YAML.

## Azure Core Services

Azure is Microsoft's cloud and the natural home for .NET. The tooling, documentation, and identity model are built with .NET developers in mind. Here's the equivalent map.

### Compute

- **App Service** — managed hosting for web apps and APIs (PaaS). Push a .NET app and it runs, scales, and gets patched for you, with built-in deployment slots for blue/green releases. The fastest path to production for a standard ASP.NET Core app.
- **Azure Functions** — serverless functions, the Lambda equivalent, with first-class .NET support (including the *isolated worker* model that runs on current .NET versions).
- **AKS (Azure Kubernetes Service)** — managed Kubernetes, the EKS equivalent.
- **Container Apps** — serverless containers built on Kubernetes and KEDA, without exposing Kubernetes itself. This is the Fargate-equivalent sweet spot: run containerized .NET services with scale-to-zero and event-driven autoscaling, minus the orchestration overhead.

### Storage and databases

- **Azure SQL Database** — managed SQL Server in the cloud. For a .NET/SQL Server team this is often the single most comfortable migration: your existing T-SQL and EF Core code just work, while Azure handles patching, backups, and high availability.
- **Cosmos DB** — globally distributed, multi-model NoSQL database. The DynamoDB equivalent, but with turnkey multi-region writes and multiple APIs (including a MongoDB-compatible one). Reach for it when you need global low latency and elastic scale.
- **Blob Storage** — object storage, the S3 equivalent. Files, backups, static content, data lakes.

### Messaging and events

- **Service Bus** — enterprise message broker with queues and topics (pub/sub), sessions, transactions, and dead-lettering. The rich, ordered, reliable messaging backbone — think SQS + SNS with more enterprise features.
- **Event Hubs** — high-throughput event *streaming* (millions of events/sec), the ingestion pipe for telemetry and big-data streams. Roughly the Kafka/Kinesis equivalent.
- **Event Grid** — lightweight event *routing* for reactive, event-driven architectures, the EventBridge equivalent.

The distinction matters: **Service Bus** is for commands and reliable business messages; **Event Hubs** is for high-volume streaming; **Event Grid** is for discrete reactive notifications.

### Identity, secrets, and observability

- **Microsoft Entra ID** (formerly Azure Active Directory) — the identity backbone for both users and applications.
- **Key Vault** — managed store for secrets, keys, and certificates, the Secrets Manager equivalent.
- **Application Insights** — deep application performance monitoring (APM) with distributed tracing, live metrics, and rich .NET integration. Part of Azure Monitor.

### Entra ID and Managed Identity, deeply

This is Azure's answer to "applications should never hold static keys," and it's arguably even cleaner than AWS roles.

A **Managed Identity** is an identity in Entra ID that Azure automatically creates and manages for your resource (an App Service, a Function, a VM). Azure handles the credentials entirely — they're never exposed to you or your code, and they rotate automatically. There are two kinds:

- **System-assigned** — tied to the lifecycle of one resource; deleted when the resource is deleted.
- **User-assigned** — a standalone identity you can share across multiple resources.

The workflow is: enable a managed identity on your App Service, grant that identity access to a resource (say, Key Vault or Azure SQL) via role assignment, and then your code authenticates *as that identity* with zero credentials.

The magic in .NET is the `DefaultAzureCredential` class from the `Azure.Identity` package. It tries a chain of credential sources: locally it uses your Visual Studio / Azure CLI login; in production it uses the managed identity. The *same code* works in both environments.

```csharp
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;

// DefaultAzureCredential picks the right credential automatically:
// your dev login locally, the Managed Identity in Azure.
// No secrets, no connection strings with passwords.
var credential = new DefaultAzureCredential();

var client = new SecretClient(
    vaultUri: new Uri("https://my-team-vault.vault.azure.net/"),
    credential: credential);

KeyVaultSecret secret = await client.GetSecretAsync("StripeApiKey");
string apiKey = secret.Value;
```

You can even connect to **Azure SQL** with a managed identity and no password in the connection string at all:

```
Server=tcp:my-server.database.windows.net;
Database=AppDb;
Authentication=Active Directory Default;
Encrypt=True;
```

> **Best practice:** Managed Identity plus Key Vault plus `DefaultAzureCredential` is the gold-standard pattern for Azure .NET apps. There is no reason to ship a password in a connection string or an `appsettings.json` in 2026.

> **Security note:** The whole point is that there is *no secret to leak*. A leaked connection-string password is a breach; a managed identity has nothing to steal because the credential lives only inside Azure's control plane and rotates constantly.

### ARM and Bicep

**ARM (Azure Resource Manager) templates** are Azure's native JSON IaC format — powerful but verbose and painful to read. **Bicep** is a modern domain-specific language that compiles down to ARM. It's dramatically cleaner, and it's the recommended choice for Azure-native IaC. We'll see an example below.

## Mapping AWS and Azure Services

Because the two clouds solve the same problems, learning one accelerates learning the other. This table maps the equivalents (they're close, not identical):

| Capability | AWS | Azure |
|---|---|---|
| Virtual machines (IaaS) | EC2 | Virtual Machines |
| Managed web/app hosting (PaaS) | Elastic Beanstalk | App Service |
| Serverless functions | Lambda | Azure Functions |
| Serverless containers | Fargate (ECS) | Container Apps |
| Managed Kubernetes | EKS | AKS |
| Object storage | S3 | Blob Storage |
| Managed relational DB | RDS / Aurora | Azure SQL / DB for PostgreSQL |
| Managed NoSQL | DynamoDB | Cosmos DB |
| Message queue | SQS | Service Bus (queues) |
| Pub/sub | SNS | Service Bus (topics) |
| Event routing bus | EventBridge | Event Grid |
| Event streaming | Kinesis | Event Hubs |
| Identity & access | IAM | Entra ID + RBAC |
| Credential-free app identity | IAM Roles | Managed Identity |
| Secrets store | Secrets Manager | Key Vault |
| Monitoring & logs | CloudWatch | Azure Monitor / App Insights |
| Native IaC | CloudFormation | ARM / Bicep |
| Private network | VPC | Virtual Network (VNet) |

> **Best practice:** Don't get religious about clouds. Pick based on your team's existing skills, your identity provider, and where your data already lives. For a .NET/SQL Server shop, Azure's native integration is a genuine productivity multiplier; for a team already deep in AWS, staying put is usually right.

## Infrastructure as Code

Clicking around a web portal to create resources is fine for learning and fatal for production. It's not repeatable, not reviewable, and not documented. **Infrastructure as Code (IaC)** means defining your infrastructure in text files you commit to git, review in pull requests, and apply automatically. Your environments become reproducible and your infrastructure gets a version history.

### Declarative vs imperative, and state

There are two philosophies:

- **Declarative** — you describe the *desired end state* ("I want one App Service and one SQL database"), and the tool figures out how to get there. CloudFormation, Bicep, ARM, and Terraform are declarative.
- **Imperative** — you write *steps* ("create this, then create that"). CDK and Pulumi feel imperative because you write code with loops and conditionals, but they ultimately *synthesize* a declarative definition under the hood.

The concept that makes declarative IaC work is **state**. The tool keeps a record of what it has already created. When you re-run it, it *diffs* your desired configuration against the recorded state and applies only the difference — creating, updating, or deleting resources to converge. This is why you can safely run the same template repeatedly (idempotency).

> **Pitfall:** State is precious and sometimes sensitive. Terraform stores it in a `terraform.tfstate` file that can contain secrets and must never be committed to git in plaintext. In a team, store it in a shared, locked backend (an S3 bucket with DynamoDB locking, or an Azure Storage account) so two engineers can't corrupt it with simultaneous applies.

### Terraform

**Terraform** (by HashiCorp) is the dominant *cloud-agnostic* IaC tool. It uses its own language, HCL, and has providers for AWS, Azure, and virtually everything else — which is its main appeal: one tool and one workflow across multiple clouds. Here's a minimal Azure resource group and storage account:

```hcl
provider "azurerm" {
  features {}
}

resource "azurerm_resource_group" "app" {
  name     = "rg-myapp-prod"
  location = "westeurope"
}

resource "azurerm_storage_account" "uploads" {
  name                     = "myappuploadsprod"
  resource_group_name      = azurerm_resource_group.app.name
  location                 = azurerm_resource_group.app.location
  account_tier             = "Standard"
  account_replication_type = "LRS"

  tags = {
    environment = "production"
    owner       = "platform-team"
  }
}
```

The workflow is `terraform plan` (preview the diff) then `terraform apply` (execute it). Always read the plan before applying — it tells you exactly what will change, and crucially, what will be *destroyed*.

> **A note on licensing:** In August 2023, HashiCorp relicensed Terraform from the open-source MPL to the source-available Business Source License (BUSL). Nothing changes for typical internal use, but the community forked the last MPL version as **OpenTofu**, now under the Linux Foundation and drop-in compatible at the fork point. Anyone picking an IaC tool today should know both names.

### Pulumi (in C#)

**Pulumi** does what Terraform does but lets you use real programming languages — including **C#**. For a .NET team this is compelling: you get types, `for` loops, unit tests, and NuGet packages instead of a bespoke DSL. The same Azure example:

```csharp
using Pulumi;
using Pulumi.AzureNative.Resources;
using Pulumi.AzureNative.Storage;
using Pulumi.AzureNative.Storage.Inputs;

return await Deployment.RunAsync(() =>
{
    var rg = new ResourceGroup("app", new ResourceGroupArgs
    {
        ResourceGroupName = "rg-myapp-prod",
        Location = "westeurope"
    });

    var storage = new StorageAccount("uploads", new StorageAccountArgs
    {
        ResourceGroupName = rg.Name,
        AccountName = "myappuploadsprod",
        Kind = Kind.StorageV2,
        Sku = new SkuArgs { Name = SkuName.Standard_LRS }
    });

    return new Dictionary<string, object?>
    {
        ["primaryEndpoint"] = storage.PrimaryEndpoints.Apply(e => e.Web)
    };
});
```

### Bicep

For an Azure-only shop, **Bicep** is the native, cleanest option. The equivalent storage account:

```bicep
param location string = 'westeurope'

resource storage 'Microsoft.Storage/storageAccounts@2023-01-01' = {
  name: 'myappuploadsprod'
  location: location
  sku: {
    name: 'Standard_LRS'
  }
  kind: 'StorageV2'
  tags: {
    environment: 'production'
    owner: 'platform-team'
  }
}
```

**Choosing between them:** Use **Bicep** if you're all-in on Azure and want the tightest native integration. Use **Terraform** if you're multi-cloud or your organization has standardized on it. Use **Pulumi** or **CDK** if your team strongly prefers writing infrastructure in a familiar programming language with full testing support. All are valid; the worst choice is *no* IaC.

## Serverless Trade-offs and Cold Starts

Serverless (Lambda, Azure Functions, Container Apps scaling to zero) is genuinely transformative for the right workloads, but it's not free of trade-offs.

**The wins:** you pay nothing when idle, scaling is automatic and effectively instant, and there are no servers to patch. For spiky, event-driven, or low-traffic workloads, the economics are unbeatable.

**The costs:**

- **Cold starts.** When a function hasn't run recently, the platform must spin up a fresh execution environment — allocate a container, load your runtime, initialize your code — before handling the request. For .NET this historically meant a noticeable delay (hundreds of milliseconds to a second-plus) on the first request. It's improved dramatically (AOT compilation, ReadyToRun, and platform features like **AWS Lambda SnapStart for .NET** — which snapshots an initialized runtime and restores it — and **Azure Functions Flex Consumption** (GA December 2024), whose "always ready" instances keep a warm pool while still scaling to zero), but it's real. A "warm" function responds in single-digit milliseconds; a cold one makes a user wait.
- **Execution limits.** Functions have maximum durations and memory ceilings. Long-running or heavyweight jobs don't fit.
- **Statelessness.** Each invocation is independent; you can't cache in memory reliably across calls. State must live externally (a database, a cache).
- **Local development and debugging** are more awkward than a normal app, and **vendor lock-in** is higher because your code binds to the platform's event model.

> **Best practice:** Mitigate cold starts by keeping deployment packages small, minimizing initialization work in the constructor, using provisioned concurrency / premium plans for latency-sensitive user-facing paths, and considering Native AOT for .NET functions. For background and async processing where a few hundred milliseconds don't matter, cold starts are a non-issue — don't over-engineer.

The senior judgment call: **serverless for event-driven and bursty work; managed containers or App Service for steady, latency-sensitive, always-on traffic.** A busy API serving thousands of requests per second is often cheaper and faster on an always-warm container than on per-invocation functions.

## Cost Awareness and Security: The Habits That Matter

Everything in this chapter comes back to two disciplines that separate a mid-level engineer from a senior one in the cloud.

**On cost:**

- **Right-size, then reserve.** Start small and scale up based on real metrics — don't guess large. For predictable steady workloads, commit to reserved capacity (Reserved Instances / Savings Plans) for large discounts over on-demand pricing.
- **Kill zombies.** Automate the shutdown of dev/test resources outside business hours and clean up orphaned disks, snapshots, and IPs.
- **Set budget alerts** at the account level from day one. Nobody should learn about a runaway bill from the invoice.
- **Mind egress and cross-region traffic** — the quiet budget killers.

**On security:**

- **Least privilege, always.** Grant the minimum permissions, scoped to specific resources. Audit and prune regularly.
- **Never hardcode credentials.** No access keys in code, config files, or environment variables committed to git. Use **IAM roles** on AWS and **Managed Identity** on Azure so applications carry no static secrets at all.
- **Store secrets in a vault** (Secrets Manager / Key Vault) with rotation, and reference them at runtime — never inline.
- **Encrypt in transit and at rest** (usually a checkbox or default now — make sure it's on).
- **Isolate the network.** Databases in private subnets/VNets, never exposed to the public internet; access controlled by security groups.
- **Respect the shared responsibility model.** The provider secures the platform; the config, the data, and the access policy are always yours.

## Lock-In, and the Honest Economics of Leaving

Every architecture discussion involving a managed service eventually reaches someone saying "but that locks us in," at which point the conversation usually stops. It shouldn't, because "locked in" is not a binary state and the objection is frequently used to justify building something worse.

Here is the reframe that makes the discussion productive: **lock-in is not a yes/no property, it is a switching cost — and switching cost is worth paying for capability you get now.** You are already locked into your programming language, your database engine, your identity provider, and your ORM. Nobody proposes writing SQL that runs identically on six engines. The question is never "are we locked in," it is "what would leaving cost, and is the thing we get worth that price?"

### The gradient

Switching cost is not evenly distributed across the services you use. It concentrates, and knowing where lets you make deliberate trades:

| Layer | Example | Cost to leave | Why |
|---|---|---|---|
| **Compute** | Containers on ECS / Container Apps / GKE | Low | Your image runs anywhere. Mostly you rewrite deployment config. |
| **Managed open-source** | RDS/Azure Database for PostgreSQL, managed Redis, managed Kafka | Low–moderate | The engine is portable; you're leaving the *operations*, not the data model. |
| **Proprietary data stores** | DynamoDB, Cosmos DB | High | The data model itself is shaped by the store's partitioning and query semantics. Leaving means redesigning access patterns, not just migrating rows. |
| **Proprietary glue** | Step Functions, EventBridge rules, Logic Apps, IAM policy | High | This is business logic expressed in a vendor's configuration language. It has no export format and is rarely documented anywhere else. |
| **Managed AI/ML platform** | Provider-specific model APIs, vector services | Moderate–high | Model behaviour differs; prompts, evals, and tuning don't transfer cleanly. |

Two things fall out of that table.

**The expensive lock-in is rarely the thing people worry about.** Teams argue about the database and then encode six months of workflow logic into a state machine defined in a proprietary JSON dialect that exists only in one cloud's console. The compute layer — the thing everyone tries hardest to keep portable — is the cheapest to move.

**Data gravity is the real anchor.** Not the format: the *volume*, and the egress bill attached to it. Moving a hundred terabytes out of a cloud costs real money at published egress rates, takes real time, and has to happen while the system keeps running. This is why egress pricing exists and is priced the way it is. (EU regulation has begun to push on this — the Data Act's provisions on switching cloud providers are phasing in, and the major providers have already made free-egress-on-exit offers — but do not plan an architecture around a discount that requires you to be leaving.)

### The abstraction layer that costs more than the lock-in

The instinctive engineering response is to write a portability layer: wrap the cloud SDK behind your own interfaces so you can swap providers later.

Occasionally this is right — usually when you genuinely run on two clouds today, or when a contract requires it. Far more often it is a large, permanent tax paid against a migration that never happens:

- You get the **lowest common denominator** of every provider's features, so you lose the capability that justified using a managed service at all.
- The abstraction is **wrong until it's tested**, and it isn't tested, because you only have one provider. The day you migrate you discover your interface leaked assumptions about the original — retry semantics, consistency, ordering, error codes.
- It is **code your team maintains forever** instead of code a vendor maintains.

> **Best practice.** Prefer *portable seams* over portability layers. Keep provider-specific code behind the boundaries your architecture already has — a repository, a message publisher, an ACL at a bounded-context edge (Chapters 6 and 30) — and let it be genuinely provider-specific inside. That gives you a known, contained blast radius for a future migration without paying an ongoing abstraction tax. Where the abstraction already exists and is free — `IDistributedCache`, `ILogger`, OpenTelemetry, S3-compatible APIs, a Postgres wire protocol — take it. Where you'd have to build it, usually don't.

### Repatriation: when leaving actually pays

"Cloud repatriation" — moving workloads back to owned or colocated hardware — went from heresy to a recurring headline, largely on the back of a few well-publicized write-ups reporting seven-figure annual savings. Before you cite them in a design review, understand which properties made those cases work, because they are specific:

- **Steady, predictable load.** The cloud's core value is elasticity, and elasticity is worth nothing to a workload that runs at a flat 70% around the clock. You are paying an on-demand premium for an option you never exercise.
- **High egress or high storage volume**, where the marginal cloud price is far above the marginal hardware price.
- **An existing operations capability.** Someone has to rack, patch, monitor, secure, and be on call for hardware. If that team doesn't exist, "savings" is a compute-cost comparison that omits the salaries.
- **Scale enough to amortize it.** The fixed cost of running your own infrastructure is substantial; below some size it dominates.

And what you give up is real: capacity you can't get in an hour, DR that isn't a config change, managed service SLAs, and the ability for a small team to run a large system. Most published success stories are companies with large steady workloads and existing infrastructure teams. Most teams reading this book are not that.

> **Gotcha.** The most common repatriation-shaped saving does not require leaving the cloud at all. Before anyone builds a business case for a datacenter, run the Chapter 28 checklist: right-sizing, reserved capacity or savings plans for the steady baseline, spot for the tolerant parts, deleting zombie resources, and fixing the top three egress paths. Teams routinely find 30–50% this way, in a fortnight, with no migration risk. Do that first; if the number still justifies leaving, you now have a much better-informed case.

### A decision rule you can use in a design review

- **Use the managed service** when it does something meaningfully hard (a database's durability and failover, a broker's delivery guarantees, a CDN's footprint), and its switching cost is proportionate.
- **Be deliberate about proprietary glue.** Logic that lives in a vendor's configuration language is the most expensive kind to move and the easiest to accumulate accidentally. If a workflow is central to your business, consider keeping it in code you own.
- **Write down what leaving would cost** for the two or three services you depend on most. Not a plan — an estimate, one paragraph each, in an ADR (Chapter 17). This converts a recurring argument into a known number, and the number is usually smaller than the loudest person in the room thinks.
- **Revisit when the shape changes.** The right answer at 10 engineers and spiky traffic is different at 200 engineers and a flat baseline. Lock-in decisions should be reviewed when the business changes, not defended forever.

## Summary

The cloud replaces owned capacity with metered capability, along a spectrum from IaaS (you manage almost everything) to serverless (you manage almost nothing). Regions and Availability Zones give you locality and resilience; the shared responsibility model draws the line between the provider's job and yours; and metered billing rewards vigilance.

AWS and Azure solve the same set of problems — compute, storage, databases, messaging, identity, observability — with different names, and the mapping table lets you translate fluently between them. For a .NET developer, Azure's native integration (App Service, Azure SQL, Entra ID, Managed Identity, Application Insights) offers a remarkably smooth path, while AWS's breadth and maturity make it the industry's default.

Two habits underpin real seniority in the cloud: define everything as code (Terraform, Bicep, Pulumi, or CDK — with careful attention to state), and design for credential-free identity and least privilege from the very first line. Master those, and you're no longer just deploying to the cloud — you're engineering on it.
