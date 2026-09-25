# Chapter 51: The Azure Casebook — Real Incidents, Real Fixes

_⏱️ Estimated read time: ~55 min · 9463 words (study pace)_

[Chapter 50](#chapter-50-azure-in-depth-for-net-developers) explains how Azure works. This chapter is about what happens when it meets production. Each case is a situation that .NET teams on Azure run into again and again. They are composites of common incidents, not one company's post-mortem. For each one you get the same six parts:

- **Situation**: what the team was doing, and what went wrong.
- **What you see**: the symptoms, as they reach you.
- **What is going on**: the mechanism, usually a detail from Chapter 50.
- **How to confirm it**: the command, query or experiment that turns a hunch into a diagnosis.
- **Fix**: what to change now, and what to change properly.
- **Prevent it** and **Interview angle**: the habit that stops it happening again, and how to tell the story.

Read a case's *Situation* and *What you see*, then stop and write down your own diagnosis before you read on. That habit is the one that pays off at 3 a.m. and in system-design interviews alike. [Chapter 33](#chapter-33-real-world-scenarios-architectural-decisions) does the same for cloud-agnostic scenarios. This chapter is specific to Azure.

## The Azure Triage Card

When something breaks, the first suspect is rarely your business logic. Start from the symptom:

| Symptom | First suspect | First check |
|---|---|---|
| `403` from a data service (Blob, Key Vault, Service Bus, Cosmos) | Data-plane role missing, or not propagated yet · wrong identity · network rule | Error code (`AuthorizationPermissionMismatch` = RBAC); who the token belongs to (`oid`); `az role assignment list --assignee <principal-id> --all` |
| `403` *after* enabling a private endpoint | DNS resolves to the public IP | `nslookup <name>` from **inside** the app |
| Intermittent timeouts under load, CPU fine | SNAT port exhaustion · connection pool exhaustion · thread-pool starvation ([Chapter 8](#chapter-8-asynchronous-concurrent-programming)) | App Service *Diagnose and solve problems* → *SNAT Port Exhaustion*; dependency failures in App Insights |
| 500s for a minute after every deployment | Cold start after a slot swap · settings that moved with the swap | Warm-up configuration; which settings are slot settings |
| Messages processed twice | Lock expiry · crash between side effect and settlement · missing idempotency | `DeliveryCount` on the message; `MessageLockLost` in the logs |
| Queue growing, consumers "fine" | Poison messages cycling · dead-letter queue filling · consumers throttled downstream | `ActiveMessages`, `DeadletteredMessages` metrics |
| Cosmos `429`s below the provisioned RU/s | Hot partition | *Normalized RU Consumption* split by `PartitionKeyRangeId` |
| `40613` / `40501` / `10928` from Azure SQL | Failover (transient) · throttling · worker limit (too much concurrency) | Error number; the database's resource metrics; how many callers are connected |
| No telemetry during the incident | Daily cap reached · sampling · the app is down and silent | The Application Insights cap setting; an availability test |

## Case 1 — "It's Contributor on the whole subscription and still gets 403"

**Situation.** A team moves an API from connection strings with account keys to managed identity. In the portal, they give the App Service's system-assigned identity the *Contributor* role on the subscription, "so we never have to think about it again". The deployment succeeds, and every call to Blob Storage fails.

**What you see.**

```
Azure.RequestFailedException: This request is not authorized to perform this operation using this permission.
Status: 403 (This request is not authorized to perform this operation using this permission.)
ErrorCode: AuthorizationPermissionMismatch
```

**What is going on.** Three separate mistakes produce the same 403, and this team made the first one:

1. **Control plane versus data plane.** *Contributor* allows managing the storage account (its `Actions`) and has no `DataActions`. Reading a blob with an Entra token needs a data role, such as *Storage Blob Data Reader* or *Storage Blob Data Contributor*.
2. **Propagation.** Role assignments can take up to 10 minutes to take effect, and a token issued earlier keeps its old contents until it expires.
3. **The wrong principal.** The role was assigned to the app *registration* instead of the managed identity, or to the production slot's identity while the staging slot (which has its own system-assigned identity) makes the call.

A fourth variant catches Cosmos DB users: its data-plane RBAC uses Cosmos DB's own role assignments, so no Azure RBAC role in the IAM blade grants access to items.

Also look at what the team *meant* to do: Contributor on the whole subscription is an enormous grant for an app. If the app is compromised, the attacker can delete every resource in the subscription, and list every storage key in it.

**How to confirm it.**

```bash
# Which principal is the app really using? (object ID of the managed identity)
az webapp identity show -g shop-rg -n shop-api --query principalId -o tsv

# What can that principal do, at every scope?
az role assignment list --assignee <principal-id> --all -o table
```

If the list shows only `Contributor`, you have your answer. If it shows the right data role, check *when* it was created, and look for a deny assignment.

**Fix.** Remove the subscription-wide Contributor role. Assign *Storage Blob Data Contributor* to the identity, scoped to the one container it needs. Deploy that assignment in the same Bicep module as the storage account (Chapter 50, *Infrastructure as Code*). Use a **user-assigned** identity, so the assignment exists before the app starts, and nothing waits for propagation during a deployment.

**Prevent it.** Role assignments live in IaC and go through code review, with a scope that is a resource or a container. Nobody clicks them into the portal. Add an Azure Policy (or at least a periodic query) that flags Owner and Contributor assignments to service principals at subscription scope.

**Interview angle.** "Explain the difference between control plane and data plane access" is a common senior question. Tell this incident as the example, and finish with least privilege: *we replaced a subscription-wide Contributor with a container-scoped data role, defined in the same template as the container*.

## Case 2 — "Works on my machine, fails in Azure": the credential chain picked the wrong identity

**Situation.** A background worker runs fine locally and in the test environment. In production it fails with 401s from Key Vault. Nothing in the code changed between environments. Another variant: the same code starts to fail when someone adds a second user-assigned identity to the app.

**What you see.**

```
Azure.Identity.AuthenticationFailedException: ClientSecretCredential authentication failed:
AADSTS7000222: The provided client secret keys for app '3f2a…' are expired.
```

or, in the second variant:

```
ManagedIdentityCredential authentication unavailable. ... Multiple user assigned identities exist,
please specify the clientId / resourceId of the identity in the token request
```

**What is going on.** `DefaultAzureCredential` walks a chain, and the **first** credential that is *available* wins. `EnvironmentCredential` comes first. Someone once added `AZURE_CLIENT_ID`, `AZURE_TENANT_ID` and `AZURE_CLIENT_SECRET` to the production app settings for a quick test. From that day, the app ran as that old service principal, not as its managed identity, until the secret expired. The error names `ClientSecretCredential`, which is the clue: a managed identity never uses a client secret.

In the second variant, `ManagedIdentityCredential` cannot choose between two user-assigned identities, because nobody told it which one to use.

**How to confirm it.** Turn on Azure SDK logging, and the chain reports each attempt. With `AddAzureClients`, SDK events already go to `ILogger`. Raise the `Azure.Identity` category to `Information` or `Debug` for one run:

```json
{ "Logging": { "LogLevel": { "Azure.Identity": "Debug" } } }
```

Then list the app settings that start with `AZURE_`, and decode the `oid` claim of a token the app received, to confirm who it is.

**Fix.** Delete the stray `AZURE_*` settings. Make production explicit: construct `ManagedIdentityCredential` with the user-assigned identity's client ID (Chapter 50, *Identity*, shows the code). Or keep `DefaultAzureCredential`, but set `AZURE_TOKEN_CREDENTIALS` to `ManagedIdentityCredential` (or to `prod`), and set `AZURE_CLIENT_ID` to the identity's client ID. Keep `DefaultAzureCredential` for development only.

**Prevent it.** Treat app settings as code: generate them from IaC, and alert on drift. A policy rule, or a line in the deployment pipeline, that rejects `AZURE_CLIENT_SECRET` in production settings costs nothing.

**Interview angle.** This case shows that you understand *why* convenience APIs have production caveats, which is a senior trait. Name the chain, name the precedence, and explain how you made the behaviour deterministic.

## Case 3 — Intermittent timeouts under load, with every dashboard green

**Situation.** An order API on App Service (3 × P1v3) calls a payment provider and a shipping provider over HTTPS. During a sales campaign, a few percent of requests fail with timeouts. CPU is at 35%, memory is flat, the providers say their side is healthy, and scaling out to 6 instances *helps a bit*.

**What you see.**

```
System.Net.Http.HttpRequestException: A connection attempt failed because the connected party did not
properly respond after a period of time ... (payments.example.com:443)
 ---> System.Net.Sockets.SocketException (10060)
```

In Application Insights, failed dependency calls cluster by instance, and appear only above a certain request rate.

**What is going on.** **SNAT port exhaustion.** Outbound connections from App Service to the internet go through source NAT, and each instance starts with a preallocated 128 SNAT ports. A code review finds this, called on every request:

```csharp
public async Task<PaymentResult> ChargeAsync(Charge charge, CancellationToken ct)
{
    using var http = new HttpClient { BaseAddress = new Uri(_options.BaseUrl) };   // new connection pool per call
    var response = await http.PostAsJsonAsync("/v1/charges", charge, ct);
    response.EnsureSuccessStatusCode();
    return (await response.Content.ReadFromJsonAsync<PaymentResult>(ct))!;
}
```

Every call opens a new TCP connection. Disposing the client closes the connection, but the port stays reserved for a while after the close, and at a high request rate new connections arrive faster than ports are freed. Once the pool is empty, new connections wait for a port and then time out. Scaling out "helps a bit" because each instance brings its own ports, which is also a clue in itself.

**How to confirm it.** In the portal: App Service → *Diagnose and solve problems* → the **SNAT Port Exhaustion** detector shows allocated and failed SNAT connections per instance. In code: search for `new HttpClient(`, `new BlobServiceClient(`, `new CosmosClient(`, `new ServiceBusClient(` and `new SqlConnection(` outside of start-up code.

**Fix.**

1. **Reuse connections.** Use a typed client from `IHttpClientFactory`, which pools handlers, and register SDK clients as singletons:

```csharp
builder.Services.AddHttpClient<PaymentClient>(c => c.BaseAddress = new Uri(builder.Configuration["Payments:BaseUrl"]!))
    .AddStandardResilienceHandler();   // Microsoft.Extensions.Http.Resilience: timeouts, retries, circuit breaker
```

2. **Take Azure traffic off SNAT.** Calls to Azure services through private endpoints (or service endpoints) stay inside the VNet and use no SNAT ports.
3. **Add a NAT gateway** to the VNet integration subnet: 64,512 ports per public IP, and a stable egress IP the payment provider can put on its allow list.

**Prevent it.** Add an analyzer rule, or a review checklist item, against `new HttpClient()` in request paths. Load-test with realistic *outbound* traffic, not just inbound requests: SNAT problems never appear in tests that mock the providers.

**Interview angle.** This is a good story because the diagnosis runs against intuition: the resource that ran out was not CPU or memory but ports, and the evidence was the per-instance clustering. Mention the [Chapter 20](#chapter-20-networking-web-fundamentals) background (TCP connection lifecycle) if they probe.

## Case 4 — A minute of 500s after every deployment

**Situation.** The team deploys to a staging slot and swaps into production, as recommended. After every swap, production returns 500s and very slow responses for 30–90 seconds. Once, after a swap, production wrote orders into the *staging* database for twenty minutes.

**What you see.** A spike of 5xx and latency in the minute after each swap. For the database incident: orders missing from production reports, found later in the staging database.

**What is going on.** There are two separate problems, and both come from the order of the swap steps (Chapter 50, *Deployment slots*):

1. **No real warm-up.** By default, the swap warms each instance up with a request to `/`, and "any HTTP response" counts as warm. This API's `/` returns 404 instantly, so the swap sees it as warm, even though the first real request still pays for JIT compilation, the EF Core model build, the first database connections and empty caches.
2. **A setting that was not sticky.** The connection string was an ordinary app setting on both slots, not a *deployment slot setting*. Non-sticky settings **move with the code** during a swap, so the staging slot's value arrived in production.

**How to confirm it.** Correlate the 5xx spikes with the swap timestamps (the Activity Log records swaps). For the settings, compare each slot's configuration and check which settings have the slot-setting flag: `az webapp config appsettings list --slot staging` shows `"slotSetting": true|false` for each.

**Fix.**

- Mark every environment-specific value (connection strings, endpoints, feature switches that differ by environment) as a **deployment slot setting**.
- Add a warm-up endpoint that exercises the app's real dependencies, and point the swap at it:

```
WEBSITE_SWAP_WARMUP_PING_PATH     = /health/warmup
WEBSITE_SWAP_WARMUP_PING_STATUSES = 200
```

  With the status list, a warm-up that fails (for example, returns 503 because the database is unreachable) now **stops the swap**, instead of promoting a broken build.
- Make the `/health/warmup` endpoint do the expensive first-time work: open a database connection, touch the EF Core model, fill critical caches.
- Use **swap with preview** for risky releases: the staging slot restarts with production's settings, and you test it before the traffic moves.

**Prevent it.** A deployment checklist entry: *every new setting — sticky or not?* Better still, generate slot settings from IaC, where "sticky" is part of the definition. And alert on the 5xx rate in the ten minutes after a swap, so that a bad swap is visible within minutes, not in tomorrow's report.

**Interview angle.** "How do you achieve zero-downtime deployments on App Service?" Slots, the swap order, warm-up, sticky settings, and the fact that database migrations must be backward-compatible with the version still serving traffic ([Chapter 12](#chapter-12-devops-cicd), expand/contract).

## Case 5 — Customers charged twice: the batch that outlived its locks

**Situation.** A payment worker reads `ChargeCard` commands from a Service Bus queue. To "reduce round trips", a developer changed it to receive 50 messages at a time and process them one by one. Each message calls the payment provider, which takes about 2 seconds. Within a day, support reports customers charged twice.

**What you see.** In the logs, `ServiceBusException` with `Reason = MessageLockLost` on `CompleteMessageAsync`, and messages with `DeliveryCount` of 2 or 3 in the handler's logs. Duplicate charges share one `MessageId`.

**What is going on.** Every message received in a batch gets its lock **at receive time**. The queue's lock duration is the default 1 minute. Processing 50 messages at 2 seconds each takes 100 seconds, so everything after roughly the 30th message is processed *after its lock has expired*. By then Service Bus has made the message visible again, and another receiver (or the same one, on its next loop) takes it. The charge runs twice, and the original `Complete` fails with `MessageLockLost`. Prefetching causes the same problem: prefetched messages are also locked while they wait in memory.

**How to confirm it.** This chapter's second *Find the bug* exercise reproduces it against the Service Bus emulator, scaled down to a 5-second lock and a 1-second handler. In the reference run, 12 messages produced 14 handler calls: two `MessageLockLost` failures, two orders handled twice. In production, compare `DeliveryCount > 1` in the handler logs with the lock duration and the batch size.

**Fix.** Two layers, and you need both:

1. **Stop outliving locks.** Use `ServiceBusProcessor`, which renews each message's lock while the handler runs (`MaxAutoLockRenewalDuration`, 5 minutes by default) and receives only as many messages as it has handlers for. Raise `MaxConcurrentCalls` for throughput, instead of batching. If you must batch, keep `batch size × processing time` well under the lock duration, or renew the locks yourself.
2. **Make the charge idempotent**, because lock expiry is only one of several ways a message gets redelivered. Pass `PaymentId` as the provider's idempotency key (most payment APIs support one), and record processed IDs in the same transaction as the business state ([Chapter 9](#chapter-9-messaging-distributed-systems), idempotent consumer).

**Prevent it.** Treat "at-least-once" as a design input, never as an edge case. A test with a short lock duration, like the exercise, catches the batching mistake in CI.

**Interview angle.** "How do you guarantee exactly-once processing?" The honest answer: *you don't; you get at-least-once delivery and make processing idempotent*. This case is the concrete story that proves you mean it.

## Case 6 — 40,000 messages in the dead-letter queue, and nobody knew

**Situation.** Finance notices that invoices for some orders were never sent. The invoice worker looks healthy: it is running, the queue is short, and there are no errors in the last hour.

**What you see.** In the portal, the `invoices` queue shows a small active count and **40,000 dead-lettered messages**, accumulated over three weeks. The `DeadLetterReason` on the oldest is `MaxDeliveryCountExceeded`.

**What is going on.** Three weeks ago, a deployment changed the invoice schema, and orders with a discount code failed deserialisation. Each of those messages was abandoned 10 times (the default `MaxDeliveryCount`) and then dead-lettered, which is exactly what Service Bus is designed to do. The design did its part. The operations around it did not: nobody alerted on the dead-letter count, and nobody had a way to replay the messages.

Two smaller mistakes made it worse. The handler treated a *permanent* failure (a message that can never deserialise) as transient, so each bad message took 10 attempts before it was moved aside. And the worker logged each failure as a warning, which nobody reads.

**How to confirm it.** The metric `DeadletteredMessages` per entity shows when it started to grow. Correlate that with the deployment history. Peek a few dead-lettered messages and read `DeadLetterReason`, `DeadLetterErrorDescription` and the application properties.

**Fix.**

1. Fix the schema bug, deploy, and **resubmit** the dead-lettered messages. Every team that uses Service Bus needs a tool like this:

```csharp
public static async Task<int> ResubmitDeadLettersAsync(ServiceBusClient client, string queue, int max, CancellationToken ct)
{
    await using ServiceBusReceiver dlq = client.CreateReceiver(queue, new ServiceBusReceiverOptions { SubQueue = SubQueue.DeadLetter });
    await using ServiceBusSender sender = client.CreateSender(queue);

    int moved = 0;
    while (moved < max)
    {
        IReadOnlyList<ServiceBusReceivedMessage> batch =
            await dlq.ReceiveMessagesAsync(maxMessages: 20, maxWaitTime: TimeSpan.FromSeconds(5), ct);
        if (batch.Count == 0) break;

        foreach (ServiceBusReceivedMessage dead in batch)
        {
            // The copy constructor keeps body, MessageId, SessionId and application properties.
            var retry = new ServiceBusMessage(dead);
            retry.ApplicationProperties["resubmittedFromDlq"] = DateTimeOffset.UtcNow.ToString("O");

            await sender.SendMessageAsync(retry, ct);       // send first …
            await dlq.CompleteMessageAsync(dead, ct);       // … then remove: a crash in between duplicates, never loses
            moved++;
        }
    }
    return moved;
}
```

   Because the consumer is idempotent (Case 5), resubmitting a message that was in fact processed is harmless.

2. **Classify failures in the handler.** Deserialisation errors, validation errors and "the entity no longer exists" are permanent: dead-letter them *immediately*, with a reason and a description a human can act on. Timeouts, 429s and 503s from dependencies are transient: throw, and let the message be retried.

**Prevent it.** Put an alert on `DeadletteredMessages > 0` for every business queue, routed to the owning team. Make the dead-letter queue part of the runbook, with the resubmit tool linked. Review "what does this consumer do with a message it can never process?" in design reviews.

**Interview angle.** This case shows ownership beyond your code: *the platform behaved correctly, and we were missing the operational loop around it*. Interviewers value engineers who close that loop.

## Case 7 — Functions scaled out and took the database down

**Situation.** A nightly import drops 200,000 messages onto a Service Bus queue. A function on the Flex Consumption plan processes them, and each message performs two SQL writes. The import used to take an hour on a VM. Now, a few minutes in, the whole shop, including the customer-facing API, fails with SQL errors.

**What you see.**

```
Microsoft.Data.SqlClient.SqlException (0x80131904): Resource ID : 1. The request limit for the database is 200 and has been reached.
```

That is error **10928**: the database's worker limit. The Azure SQL CPU chart is at 100%. The function app's instance count went from 1 to dozens within minutes.

**What is going on.** Serverless scale does exactly what it promises. The queue was deep, so the platform added instances. Each instance ran the Service Bus trigger's default concurrency (16 per core). Dozens of instances × 16 or 32 concurrent executions = over a thousand concurrent SQL sessions against a database sized for the web shop's normal load. The database hit its worker limit, and it could not tell the import's sessions apart from the checkout's.

**How to confirm it.** Plot the function app's instance count against the database's worker and session metrics. They rise together. The error number 10928 is specific: it is not "the database is slow", it is "too many concurrent requests".

**Fix.** Bound the total concurrency to what the database can take, and make each unit of work cheaper:

- Cap the per-instance concurrency in `host.json` (`maxConcurrentCalls`), and cap the **maximum instance count** of the function app. The product of the two is the most concurrent SQL work the import can ever generate. Size it from the database's limits, leaving headroom for the shop.
- Batch the writes. Receive messages in batches (the trigger can bind to `ServiceBusReceivedMessage[]`) and write each batch in one round trip, with a table-valued parameter or `SqlBulkCopy`.
- Isolate the workloads. Run imports against a separate database, an elastic pool with per-database limits, or at least a time window, so that a batch job can never starve checkout.

**Prevent it.** For every event-driven consumer, write down its *maximum* concurrency and check it against every downstream system: the database, partner APIs, rate limits. This is *queue-based load levelling*: the queue absorbs the burst, and the consumers drain it at a rate the downstream system can sustain, which only works if something caps the drain rate.

**Interview angle.** "What are the risks of serverless?" Most candidates say cold starts. This case gives the more senior answer: *elastic compute in front of inelastic dependencies*, and how you cap it.

## Case 8 — Cosmos DB throttles at a third of its provisioned throughput

**Situation.** A multi-tenant SaaS stores all documents in one Cosmos DB container with partition key `/tenantId`, and 30,000 RU/s provisioned. A large customer (about 40% of all traffic) onboards. From then on, that customer sees 429s during business hours, while the account-level metrics show only about 11,000 RU/s consumed in total.

**What you see.** `CosmosException` with status 429 (TooManyRequests) reaching the application: the SDK's 9 automatic retries were not enough. Latency rises for *that* tenant only.

**What is going on.** A **hot logical partition**. All of the big tenant's documents share one partition key value, so they live in one logical partition, on one physical partition. A physical partition serves at most 10,000 RU/s, no matter how much the container has in total. Throughput is divided across physical partitions, so this one gets a fraction of the 30,000, and the tenant's traffic needs more than that fraction. The other physical partitions sit idle. The 20 GB limit per logical partition is the next problem waiting in line.

**How to confirm it.** In the Cosmos DB account's *Insights* (or Metrics), look at **Normalized RU Consumption** split by `PartitionKeyRangeId`. One range sits at 100% while the others are low. The throttled requests' diagnostics show the same partition key range.

**Fix.** Spread the tenant across partitions, without losing single-partition queries for the common case:

- **Hierarchical partition keys** (`/tenantId`, then `/userId` or `/documentId`): queries that filter on `tenantId` target only that tenant's partitions, and one tenant can spread across many physical partitions and exceed 20 GB.
- Or a **synthetic key** such as `tenantId-bucket`, where the bucket is a hash of the document ID modulo N. This spreads the writes, but queries for a tenant must fan out to N buckets.

Either way it is a **migration**: create a new container with the new key, copy the data with the change feed (a processor that reads the old container and writes to the new one), switch reads, then switch writes. Plan it as a project.

**Prevent it.** Design partition keys with the *largest* tenant in mind, not the average. Put an alert on normalized RU consumption per partition range, not just on the total. Load-test with realistic skew; uniform synthetic data never shows a hot partition. [Chapter 23](#chapter-23-data-at-scale-multi-tenancy) covers multi-tenant data design in depth.

**Interview angle.** Cosmos DB partitioning questions are common in Azure interviews. The strong answer names the per-partition limits, explains why buying more RU/s did not help, and describes the migration honestly.

## Case 9 — The Cosmos DB bill doubled after a small feature

**Situation.** A team adds an "orders by status" admin screen and a nightly export. The next invoice shows the Cosmos DB cost doubled, and autoscale sits at its maximum most of the day.

**What you see.** High RU consumption with no rise in user traffic. The export's log (the team had logged `RequestCharge`, luckily) shows a query that costs thousands of RUs per page.

**What is going on.** Two things, working together:

1. **Cross-partition queries on a frequent path.** `SELECT * FROM c WHERE c.status = 'Open' ORDER BY c.createdAt` has no partition key filter, so it fans out to every physical partition. The admin screen refreshes it every 30 seconds for every open browser tab.
2. **Default indexing on a write-heavy container.** Every property of every order is indexed, including a large `lineItems` array that no query touches, so every write pays for index updates on each element.

**How to confirm it.** Log `RequestCharge` per query type (Chapter 50's `OrderStore` sample shows how). Look at the Cosmos DB metrics for total request units by operation type. Check the container's indexing policy.

**Fix.**

- Serve the admin view from a **projection**: a small container or a SQL table that the change feed keeps up to date, partitioned by status and date bucket. Better still, ask whether the screen needs to be live at all.
- Run the export from an **analytical copy** of the data (such as Microsoft Fabric mirroring for Cosmos DB), or from the change feed, instead of querying the transactional container.
- **Exclude** `/lineItems/*` and other unqueried paths from indexing, and add a **composite index** for the `status` + `createdAt` sort that remains.

**Prevent it.** Make `RequestCharge` visible: log it in development, and add a test that asserts an upper bound on the RU cost of the hottest queries, the way Chapter 37's lab asserts *work* instead of time. Review new queries for a partition key filter as routinely as you review SQL queries for an index.

**Interview angle.** Cost is an engineering metric. "I found the three queries that were 80% of our RU spend, and cut the bill by [X]%" is a strong CV bullet, provided you keep the numbers from your own system.

## Case 10 — The private endpoint that made things worse

**Situation.** A security review asks the team to take the storage account and the Key Vault off the public internet. They add private endpoints for both, turn off public network access, and deploy. The API, on App Service, fails immediately.

**What you see.**

```
Azure.RequestFailedException: This request is not authorized to perform this operation.
Status: 403 ... ErrorCode: AuthorizationFailure
```

and from Key Vault, a 403 saying that public network access is disabled and the request did not come from a trusted service or a private link. The same code works from a VM in the VNet.

**What is going on.** The app still resolves the *public* address. Three pieces are needed, and one is missing:

1. **VNet integration on the app.** Without it, the app's outbound calls never enter the VNet at all.
2. **Private DNS zones** (`privatelink.blob.core.windows.net`, `privatelink.vaultcore.azure.net`), with A records for the endpoints, **linked to the VNet** that the app is integrated with.
3. **DNS resolution through Azure DNS.** If the VNet uses custom DNS servers (a domain controller, a firewall), those servers must forward `privatelink.*` queries to Azure DNS at `168.63.129.16`.

The VM works because it sits in a VNet where all three are in place. The app's integration VNet was never linked to the zones.

**How to confirm it.** From the Kudu console (or SSH) *of the app*:

```
nslookup shopdata.blob.core.windows.net
# broken:  ... Address: 20.60.x.x        (public IP)
# working: ... Aliases: shopdata.privatelink.blob.core.windows.net
#              Address: 10.1.2.5         (the private endpoint)
```

A public IP in the answer settles it: the problem is DNS, and nothing in RBAC or in your code will fix it.

**Fix.** Link the private DNS zones to the integration VNet (or fix the forwarding on the custom DNS servers). Make sure the app has VNet integration, with outbound traffic routed through it. Deploy the endpoints, the zones, the zone links and the DNS zone groups together, in one IaC module, so that nobody can create one without the others.

**Prevent it.** A standard "private PaaS resource" module that always creates the endpoint, the zone group and the link. A post-deployment smoke test that resolves every dependency's name *from inside the app* and fails the pipeline if it gets a public IP.

**Interview angle.** Networking questions separate the developers who have shipped to locked-down environments from those who have not. The phrase that shows you have: *a private endpoint is only as good as the DNS that points to it*.

## Case 11 — Large uploads fail at almost exactly four minutes

**Situation.** Users upload site-survey videos (1–4 GB) through an ASP.NET Core API on App Service, which streams them to Blob Storage. Small files work. Large files fail for users on slow connections, and the failures all happen just under four minutes into the upload.

**What you see.** The browser reports a network error or a 500. The server logs show the upload still running after the client has given up, and sometimes it completes, leaving blobs nobody knows about.

**What is going on.** **The 230-second limit.** App Service's front end ends any request that has not produced a response within 230 seconds (3 minutes 50 seconds). It does not matter that the server is still busy, or that bytes are still flowing. On top of that, every upload holds a request thread, memory buffers and an outbound connection for its whole duration, so ten concurrent large uploads hurt everyone else.

**How to confirm it.** The failure time is the fingerprint: always about 230 seconds after the request started. In Application Insights, the failed requests have a duration of about 230,000 ms.

**Fix.** Take the API out of the data path:

```
  browser ── 1. POST /uploads (metadata) ──────────────► API: validate, authorise, create a record
          ◄─ 2. { uploadUrl: <user delegation SAS> } ──  (create+write on ONE blob, 15 minutes)
          ── 3. PUT blocks directly ───────────────────► Blob Storage (browser SDK uploads in blocks, with retries)
                                                          │
                              4. BlobCreated event ◄──────┘  Event Grid → Service Bus queue → worker:
                                                              scan, transcode, mark the record complete
```

Chapter 50's `UploadUrlIssuer` shows step 2. The browser uploads in blocks, so it can resume after a failure, and upload time is limited only by the SAS expiry, which should be long enough for a slow connection but no longer. The API's requests now take milliseconds. Route the `BlobCreated` event through a Service Bus queue rather than straight to the worker, so that bursts are buffered and processing gets peek-lock and dead-lettering.

**Prevent it.** A design rule: *no request does work proportional to user-controlled size or duration*. Anything that can exceed a few seconds becomes "accept, then process asynchronously" ([Chapter 22](#chapter-22-background-processing-scheduling-the-actor-model)).

**Interview angle.** A classic system-design follow-up ("how would you handle large file uploads?"). Name the limit, the SAS scoping (one blob, create and write only, short expiry), and the event-driven completion.

## Case 12 — A deployment broke every running workflow

**Situation.** A refund-approval workflow runs on Durable Functions: an orchestrator waits up to three days for a manager's approval. The team adds a fraud-check activity at the start of the orchestrator and deploys on Tuesday. From then on, every refund *started before Tuesday* fails when the manager approves it. New refunds work.

**What you see.** Orchestration instances in the `Failed` state, with a non-determinism error that says the orchestrator's history does not match the actions the code now schedules. Only instances created before the deployment are affected.

**What is going on.** **Replay** (Chapter 50, *Durable Functions*). When an instance wakes up, because the approval event arrived, the framework re-runs the orchestrator from the beginning and compares every action the code schedules with the recorded history. The old history says "first action: `NotifyApprover`". The new code's first action is `CheckFraud`. The histories do not match, so the framework cannot continue safely, and the instance fails.

The same mechanism explains the other classic Durable bug. Code that uses `DateTime.UtcNow` or `Guid.NewGuid()` in an orchestrator gets a *different* value on each replay. Timers then fire at the wrong time, and IDs change between replays.

**How to confirm it.** The failures correlate with the instance creation time relative to the deployment. The error message names a mismatch between history and code.

**Fix, for the instances already broken.** Deploy a fixed version that restores the old shape for old instances, then use the *rewind* or *restart* management operations where they apply, or recreate the affected requests from your own records. This is manual work, and it is why prevention matters here.

**Prevent it.** Treat an orchestrator's code as a **versioned contract** with every instance that is in flight:

- For breaking changes, **deploy side by side**: add `ApproveRefundV2` as a new orchestrator and start new instances on it, while old instances finish on the unchanged V1, which you delete once they have drained. Or deploy the new version to a separate task hub, or a separate app, and let the old one drain.
- Keep orchestrators thin and deterministic: `context.CurrentUtcDateTime`, `context.NewGuid()`, durable timers, and all I/O in activities. Activities can change freely, as long as their inputs and outputs stay compatible.
- Add a test that replays a recorded history of each orchestrator against the new code, and run it before every deployment.

**Interview angle.** "What are the trade-offs of Durable Functions?" This case is the mature answer: *workflow code becomes a long-lived contract with in-flight instances*. The same is true of every workflow engine: Temporal, Logic Apps, sagas implemented with MassTransit.

## Case 13 — Secret rotation took production down

**Situation.** Security policy requires rotating the partner API key every 90 days. The first rotation was done by hand: generate a new key at the partner, update the Key Vault secret, deactivate the old key. The API failed with 401s from the partner for up to a day, and different instances failed at different times. At the same time, a new service that reads Key Vault *on every request* started failing with `429`s during peak hours.

**What you see.** 401s from the partner on some instances and not others. `RequestFailedException: Status: 429 (Too Many Requests)` from Key Vault on the new service.

**What is going on.** Two different misuses of Key Vault:

1. **Stale copies.** Every consumer caches the secret in a different place. App Service Key Vault references refresh every 24 hours, or immediately on a restart or configuration change. The configuration provider reloads on its `ReloadInterval`. `IOptions<T>` holds the value read at start-up until the process restarts. So when the old key was deactivated, many instances still held it, for anything from minutes to a day.
2. **Key Vault is not a per-request cache.** Vaults are throttled, and reading a secret on every request turns traffic peaks into 429s, plus a network round trip on every call.

**How to confirm it.** For the 401s: note which instances fail, and when each last restarted or reloaded its configuration. For the 429s: the Key Vault metrics show *Service API Hit* rising with request traffic.

**Fix.**

- **Rotate with overlap.** The partner accepts two keys at once. Add the new key, update Key Vault, wait until every consumer has provably picked it up (force it: restart the app, or bump the App Configuration sentinel), and only then deactivate the old key. Most providers that require rotation support two active keys for exactly this reason.
- **Load secrets once, reload deliberately.** Use the configuration provider with a `ReloadInterval`, and `IOptionsMonitor<T>` in the code that uses the key, so a reload takes effect without a restart (Chapter 50, *Key Vault*).
- For the new service: read the secret at start-up through configuration, not per request.
- Where the credential is for an Azure service, **remove it altogether**: a managed identity has nothing to rotate.

**Prevent it.** Automate rotation with Key Vault's near-expiry events (Event Grid) and a function that performs the overlap sequence. Write the sequence down as a runbook before the first rotation, not during it.

**Interview angle.** Secrets management questions often end in "how do you rotate without downtime?" The answer is the overlap window plus consumers that reload, and even better, no secret at all.

## Case 14 — "The database is not currently available", every few days

**Situation.** An API on Azure SQL (General Purpose, serverless in the test environment) logs short bursts of errors: a few seconds each, a few times a week in production, and on the first request of every morning in test. The team's first instinct is to open a support ticket.

**What you see.**

```
Microsoft.Data.SqlClient.SqlException (0x80131904): Database 'Shop' on server 'shop-sql' is not currently available.
Please retry the connection later. ... Error Number:40613
```

After someone enables EF Core's retries, one endpoint starts failing *every time*, with:

```
InvalidOperationException: The configured execution strategy 'SqlServerRetryingExecutionStrategy' does not support
user-initiated transactions. Use the execution strategy returned by 'DbContext.Database.CreateExecutionStrategy()'
to execute all the operations in the transaction as a retriable unit.
```

**What is going on.** Error 40613 is the platform telling you it is *moving* the database: a failover for patching, a scaling operation, a hardware replacement. In production that happens routinely. In the serverless test database, the database auto-pauses overnight, and the first connection in the morning must wait while it resumes. Both are transient, and both are expected. The support ticket would be answered with "please implement retry logic".

The second error is EF Core protecting you. A retrying strategy cannot replay half of a transaction you started yourself, so it refuses to run one.

**How to confirm it.** The error number, and its correlation with the maintenance events in the Azure SQL *Resource health* view, or with the auto-pause events in test.

**Fix.** Enable `EnableRetryOnFailure` (Chapter 50, *Azure SQL*), and wrap explicit transactions in the execution strategy, so the *whole unit* is retried:

```csharp
public async Task TransferStockAsync(int fromWarehouse, int toWarehouse, int productId, int quantity, CancellationToken ct)
{
    IExecutionStrategy strategy = _db.Database.CreateExecutionStrategy();

    await strategy.ExecuteAsync(async () =>
    {
        // Everything in here is retried as one unit, including the reads, which see fresh data on each attempt.
        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        var from = await _db.Stock.SingleAsync(s => s.WarehouseId == fromWarehouse && s.ProductId == productId, ct);
        var to = await _db.Stock.SingleAsync(s => s.WarehouseId == toWarehouse && s.ProductId == productId, ct);
        from.Quantity -= quantity;
        to.Quantity += quantity;

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    });
}
```

One subtlety: if the connection drops *during* `CommitAsync`, the client cannot know whether the commit happened. A retry could then apply the transfer twice. For operations where that matters, make the write idempotent (a unique operation ID), or use the `ExecuteInTransactionAsync` overload with a `verifySucceeded` callback that checks whether the commit landed.

**Prevent it.** Retry policies for every dependency are part of the service template, not something added after the first incident. In the test environment, serverless auto-pause is a feature: it finds missing retry logic before production does.

**Interview angle.** "How do you handle transient faults?" Retries with backoff, idempotency, and the subtle point: *a retry can't know whether a commit whose acknowledgement was lost actually happened*. That point is what separates a senior answer.

## Case 15 — Blind in the middle of the incident

**Situation.** Checkout starts failing at 14:10. The on-call engineer opens Application Insights: no requests, no exceptions, no traces since 13:40. The dashboard suggests the service is idle. Customer complaints suggest otherwise.

**What you see.** Telemetry stops abruptly at 13:40, while the platform metrics (App Service HTTP 5xx, CPU) show activity continuing. The Application Insights resource shows a warning that the daily cap was reached.

**What is going on.** Months earlier, someone set a **daily cap** to control cost. Today, a retry storm against a failing dependency multiplied the logging volume: every failed call logged an exception with its full payload, at `Information` level, from every instance. The volume hit the cap at 13:40, and ingestion **stopped** for the rest of the day. The cost control worked exactly as designed, at the worst possible moment.

**How to confirm it.** The cap and its reset time are in the Application Insights (or Log Analytics workspace) usage settings. The volume that caused it shows up in the workspace:

```kusto
// Which tables and apps produce the ingested volume? (_BilledSize is in bytes)
union withsource = TableName App*
| where TimeGenerated > ago(1d)
| summarize GB = round(sum(_BilledSize) / 1e9, 2) by TableName, AppRoleName
| order by GB desc
```

**Fix.** Now: raise the cap temporarily, and use the platform metrics (which are not affected by the cap), the log stream and Kudu for the rest of the incident. Afterwards:

- Control volume at the **source**: sensible log levels (`Warning` for framework categories such as `Microsoft.EntityFrameworkCore`), no request or response bodies, and deduplicated exceptions in retry loops (log the final failure, not every attempt).
- Use **sampling** (Chapter 50, *Observability*), keeping in mind that it can hide rare events.
- Keep the daily cap, if at all, as an emergency brake set far above normal volume, with an alert at a lower threshold, so that a human decides before ingestion stops.

**Prevent it.** An **availability test** and **metric alerts** do not depend on application telemetry, so they still work when ingestion stops or when the app is too broken to send anything. Every critical service needs at least one signal that does not depend on its own logs.

**Interview angle.** Observability questions reward engineers who understand the *economics* of telemetry: what it costs, what sampling hides, and which signals survive a failure.

## Case 16 — The region went down: a design review after the fact

**Situation.** A regional outage takes the shop's primary region down for four hours. The architecture diagram said "multi-region". In practice: the web tier was deployed in two regions, but the Azure SQL database had no geo-replica, the storage account was LRS, and the Key Vault, App Configuration and Service Bus namespace existed only in the primary region. The secondary region's app instances started, and could reach none of their dependencies.

**What you see.** Front Door reported the secondary region healthy, because its health probe only checked that the process answered, and routed traffic there. Every request then failed on its first dependency.

**What is going on.** Availability is a property of the **whole request path**, not of the compute tier (Chapter 50, *Regions, availability zones and SLAs*). Every stateful dependency needs its own answer to "where does it live when the region is gone?", and the health probe must test the dependencies that matter, or failover routes traffic into a broken region.

**The review, dependency by dependency:**

| Dependency | What it had | What it needed for the stated goal | Trade-off to accept |
|---|---|---|---|
| Azure SQL | Single region | A **failover group**, with the app connecting through the listener | Asynchronous replication: a forced failover can lose recent transactions (RPO > 0) |
| Blob Storage | LRS | **GZRS** or **RA-GZRS**, and a rehearsed customer-managed failover | Cost; after failover the account is LRS until geo-redundancy is reconfigured |
| Cosmos DB (catalogue) | Single region | Add a second region, with service-managed failover | RU/s billed in each region |
| Service Bus | Standard, one region | Premium with geo-replication or geo-disaster recovery, or a design that tolerates losing in-flight messages | Premium cost; what happens to messages in flight must be decided explicitly |
| Key Vault, App Configuration | Primary only | A replica or a second instance per region, deployed from the same IaC | More to keep in sync |
| Front Door health probe | `/` | A `/health/ready` endpoint that checks critical dependencies | Probe traffic; a dependency blip can fail over a region, so tune the thresholds |

**The conversation that matters.** Before buying any of this, agree on the **RTO** (how long you can be down) and **RPO** (how much data you can lose) with the business, per capability. "Checkout down for 4 hours once every few years" might be acceptable, and much cheaper than active-active. Then **rehearse** the failover. A failover that has never been tested is a hypothesis, and this incident was the test.

**Interview angle.** "How would you make this system multi-region?" The strongest answers start with RTO and RPO, go through the state, not the compute, and end with how the failover is tested. [Chapter 21](#chapter-21-distributed-systems-theory-reliability-engineering) has the theory; this table is the practice.

## Quick Cases

**The CI secret that leaked.** A client secret for the deployment service principal, stored in the CI system, turned up in a build log after someone added verbose logging to a script. The secret had *Owner* on the production subscription. *Fix:* revoke it now, then replace it with **workload identity federation** (Chapter 50, *Identity*): a federated credential that trusts only the `production` environment of this repository, and a role scoped to the resource groups the pipeline deploys. *Prevent:* there is no secret left to leak. Scope deployment identities per environment, and require environment approvals for production ([Chapter 35](#chapter-35-software-supply-chain-security)).

**Event Grid events that never arrive.** A new webhook endpoint subscribed to `BlobCreated` gets nothing. The subscription shows a failed provisioning state. *Cause:* the endpoint returned `200` to the validation event without echoing `validationCode`. *Fix:* handle the `SubscriptionValidationEvent` (or subscribe a Function or a Service Bus queue instead, which needs no handshake), and configure a dead-letter container so that events that exhaust their retries (30 attempts or 24 hours by default) are kept rather than dropped.

**The swap that kept failing its warm-up.** Every swap is aborted: the staging slot's warm-up endpoint returns 503, and its logs show 403s from Key Vault. Production works fine. *Cause:* managed identity configuration is **specific to each slot**. The staging slot has its own system-assigned identity, the role was granted only to the production slot's identity, and identities do not move in a swap. So the staging build can never reach Key Vault, and the warm-up correctly stops the swap. *Fix:* attach one **user-assigned** identity to both slots and grant the role once, so access does not depend on which slot the code runs in.

**The timer that ran twice.** A timer-triggered function that sends a daily report sent it twice after a scale-out. *Cause:* not the timer. The Functions timer trigger uses a blob lease so that only one instance runs each schedule, but two *separate* function apps (a staging app, deployed with the same configuration) shared nothing and both ran it. *Fix:* disable timers in non-production environments by configuration (`AzureWebJobs.<FunctionName>.Disabled`), and make the report idempotent per day.

## Exercises

### Find the bug

**1.** Two instances of an inventory service run this code to reserve stock. Stock lives in one JSON blob. Tests pass, and the code has run for months. Then the warehouse counts more stock than the system shows sold.

```csharp
public static async Task ReserveAsync(BlobClient blob, string sku, int quantity, CancellationToken ct)
{
    BlobDownloadResult current = await blob.DownloadContentAsync(ct);
    Inventory inventory = current.Content.ToObjectFromJson<Inventory>()!;

    if (inventory.Stock[sku] < quantity)
        throw new InvalidOperationException($"Not enough {sku} in stock.");

    inventory.Stock[sku] -= quantity;

    await blob.UploadAsync(BinaryData.FromObjectAsJson(inventory), overwrite: true, ct);
}
```

<details>
<summary>Answer</summary>

**A lost update.** Instance A reads `{ "sku-1": 10 }`. Instance B reads the same, reserves 3 and writes 7. Instance A reserves 2 and writes 8, overwriting B's write. No error is raised anywhere: `overwrite: true` means "write whatever is there". The stock now reads 8 when it should read 5.

The fix is **optimistic concurrency with the ETag**: write only if the blob is unchanged since you read it, and on `412 Precondition Failed`, re-read and re-apply:

```csharp
for (var attempt = 1; ; attempt++)
{
    BlobDownloadResult current = await blob.DownloadContentAsync(ct);
    Inventory inventory = current.Content.ToObjectFromJson<Inventory>()!;
    if (inventory.Stock[sku] < quantity)
        throw new InvalidOperationException($"Not enough {sku} in stock.");
    inventory.Stock[sku] -= quantity;

    try
    {
        await blob.UploadAsync(BinaryData.FromObjectAsJson(inventory),
            new BlobUploadOptions { Conditions = new BlobRequestConditions { IfMatch = current.Details.ETag } }, ct);
        return;
    }
    catch (RequestFailedException e) when (e.Status == 412 && attempt < 5)
    {
        // someone else wrote first: loop, re-read, re-apply
    }
}
```

This is verified against Azurite in `verify/exercises/Ch51`: with another writer forced in between the read and the write, the original code ends at 8 and the fixed code at 5, after exactly one `412`. The bigger design question is whether one blob should hold shared, frequently-updated state at all. Under real contention the retries pile up. A database row with a conditional update, or partitioning the state (one blob per SKU), scales better.
</details>

**2.** A payment worker drains a Service Bus queue. The queue uses the default 1-minute lock duration, and each payment call takes about 2 seconds. Customers report being charged twice.

```csharp
public async Task DrainOnceAsync(CancellationToken ct)
{
    IReadOnlyList<ServiceBusReceivedMessage> batch =
        await receiver.ReceiveMessagesAsync(maxMessages: 50, maxWaitTime: TimeSpan.FromSeconds(5), ct);

    foreach (ServiceBusReceivedMessage message in batch)
    {
        await handler.HandleAsync(message, ct);          // calls a partner API, ~2 s
        await receiver.CompleteMessageAsync(message, ct);
    }
}
```

<details>
<summary>Answer</summary>

**The batch outlives its locks.** All 50 messages are locked when they are *received*. Processing them one after another takes about 100 seconds, so from roughly the 30th message on, each is processed after its lock has expired. Service Bus has already made it visible again, so it is delivered again, handled again (a second charge), and the original `CompleteMessageAsync` throws `ServiceBusException` with `Reason == MessageLockLost`. That exception also abandons the rest of the batch.

The fix has two layers:

1. Use a `ServiceBusProcessor`. It renews each lock while the handler runs (`MaxAutoLockRenewalDuration`, 5 minutes by default) and receives only as many messages as it has concurrent handlers (`MaxConcurrentCalls`). Get throughput from concurrency, not from big batches.
2. Make the handler idempotent (a payment ID as the provider's idempotency key, plus a processed-messages record), because lock expiry is only one of the ways a message is redelivered.

This is verified against the Service Bus emulator in `verify/exercises/Ch51`, scaled down to a 5-second lock and a 1-second handler. In the reference run, 12 messages produced 14 handler calls and 2 `MessageLockLost` failures with this code, and exactly 12 calls with the processor.
</details>

### What would you do

**1.** A product manager asks for "a quick fix" after the third identity-related 403 this month: "Can't we just give the app Owner on the subscription so this stops happening?" The team lead half-agrees, because the release is on Friday.

<details>
<summary>How a senior engineer reasons about it</summary>

Start from what the 403s actually were. If they were data-plane roles missing (Case 1), Owner would not even fix them: it has no data actions either. If they were propagation delays after fresh deployments, Owner would not fix those, because the delay applies to every new role assignment. So "give it Owner" is both dangerous and probably ineffective, and the second argument usually lands better than the first.

Then offer something that fixes the *cause* by Friday: a user-assigned identity, created once, with container- and vault-scoped data roles defined in the same Bicep module as the resources. That removes both the missing-role and the propagation failures. It is maybe half a day of work, and it can be reviewed like any other change.

Finally, make the risk concrete for the non-engineers in the room without drama: with Owner, a single vulnerability in the app (an SSRF, a leaked token) would let an attacker delete every resource and read every key in the subscription. Frame it as *blast radius*, which product people understand, and write the decision down (an ADR, [Chapter 17](#chapter-17-soft-skills-engineering-practices)) if they still choose the shortcut.
</details>

**2.** You are designing a new "document processing" service: PDFs arrive via upload (a few thousand a day, in bursts after business hours), each takes 20 seconds to 6 minutes to process with a native library that needs a specific Linux package, and results go to Azure SQL. A colleague proposes Functions on the Consumption plan, "because it's serverless and cheap".

<details>
<summary>How a senior engineer reasons about it</summary>

Check the constraints against the hosting plans (Chapter 50, *Compute*), one at a time:

- **Duration.** Up to 6 minutes fits under the legacy Consumption plan's 10-minute maximum, but with little margin. The plan is also documented as legacy, and its Linux version retires in 2028. That alone rules it out for a new service.
- **Native dependency.** A specific Linux package means a **custom container**, and Flex Consumption does not support containers (it deploys code packages only). The realistic options are **Container Apps** (a job, or an app with a Service Bus scale rule), or Functions on the Premium plan or on Container Apps hosting, with a custom image.
- **Bursts after hours.** Scale to zero between bursts fits Container Apps well. KEDA scales on queue length.
- **Azure SQL downstream.** Whatever scales out must be capped (Case 7). Set the maximum replicas × per-replica concurrency from the database's limits.

A reasonable proposal: upload via SAS directly to Blob Storage (Case 11) → `BlobCreated` → Service Bus queue → a **Container Apps job** (or app) with a Service Bus scale rule, `minReplicas: 0`, and a capped maximum. Idempotent processing keyed by blob name, with results written in one transaction. Then say what you would measure before committing: the processing time distribution on real documents, and the cost at the expected volume against a small always-on alternative.

The review comment for the colleague: "serverless" is a property of the operating model, not a product, and here the dependency and the downstream database decide more than the price does.
</details>

### Go check

Answer these from your own system, not from memory:

- **Identity.** List every role assignment held by your apps' identities: `az role assignment list --assignee <id> --all`. How many are Contributor or Owner? How many are scoped to a subscription or a resource group instead of a resource? Is any `AZURE_CLIENT_SECRET` set in any environment's settings?
- **Keys.** Which of your storage accounts, Service Bus namespaces and Cosmos DB accounts still allow shared-key or local authentication? Who or what still uses the keys?
- **Connections.** Search for `new HttpClient(`, `new CosmosClient(`, `new ServiceBusClient(`, `new BlobServiceClient(` and `new DefaultAzureCredential(` outside start-up code.
- **Messaging.** For each queue and subscription: the lock duration, `MaxDeliveryCount`, the current dead-letter count, and whether anything alerts on it. Is every consumer idempotent, and how do you know?
- **Deployments.** Which app settings are slot settings? What does your warm-up path check? What does your Front Door or Traffic Manager health probe check?
- **Data.** For each Cosmos DB container: the partition key, the largest logical partition, and the three most RU-expensive queries. For Azure SQL: is `EnableRetryOnFailure` on, and do explicit transactions go through the execution strategy?
- **Telemetry.** Is a daily cap set? What is the sampling configuration? Is there an availability test on every public entry point?
- **Recovery.** For each stateful dependency: where does it live when the region is gone, and when was that last tested?

Each "I don't know" is a candidate for a small, measurable improvement, and for a story in your evidence portfolio ([Chapter 36](#chapter-36-the-story-bank-evidence-portfolio)). Keep stories about a real employer private, and anonymise the numbers you share.

## Summary

The Azure incidents that cost the most time are rarely exotic. They come from a handful of mechanisms: **control plane versus data plane** permissions and their propagation delay; a **credential chain** that picks an unexpected identity; **SNAT ports** and connections that are not reused; the **slot swap sequence** and settings that move with it; **peek-lock** semantics, lock expiry and dead-letter queues nobody watches; **elastic compute in front of inelastic databases**; **hot partitions** in Cosmos DB; **private endpoints without DNS**; the **230-second** request limit; **replay** in Durable Functions; **secret rotation** without overlap; **transient SQL faults**; **telemetry caps**; and **multi-region diagrams** that stop at the compute tier.

Each case follows the same discipline: read the symptom, name the mechanism, confirm it with a specific check, fix the cause rather than the symptom, and add the guardrail that keeps it fixed. That discipline, more than any service name, is what an interviewer is listening for, and what your team relies on at 3 a.m.
