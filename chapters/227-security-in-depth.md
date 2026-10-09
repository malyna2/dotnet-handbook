# Chapter 27: Security in Depth and the Supply Chain

[Chapter 12: Security Essentials](#chapter-12-security-essentials) covered what every middle developer must get right inside one application: authentication and authorization, the OWASP Top 10, storing secrets, TLS, the cryptography you call, and scanning dependencies for known CVEs. This chapter is the depth above that, where the decisions span services, pipelines and years. It makes you able to remove long-lived credentials instead of guarding them, to plan a cryptographic migration before it is forced on you, and to defend the chain of code and builds that ends in the artifact you ship.

Three threads, in that order. **Zero trust and workload identity** replace "this request came from inside the network" and "this service holds a secret" with identities the platform attests and credentials that expire in minutes — in production and in CI. **Crypto agility** takes the algorithms themselves off the list of things you assume, because the post-quantum migration has already started. And the **software supply chain** applies the same thinking to the packages you consume, the build that assembles them, and the artifacts you publish, ending with what to do on the day an advisory lands and where to start.

## Zero Trust and Workload Identity

[Chapter 12: Secrets Management](#secrets-management) is about storing credentials safely. This section is about the better move: **not having them.**

### What zero trust actually claims

Strip away the marketing and zero trust is one architectural assertion: **network position confers no trust.** Being inside the VPC, behind the firewall, or on the corporate LAN tells you nothing about whether a request is legitimate. Every request — including service-to-service requests that never leave your cluster — is authenticated and authorized on its own merits.

The model it replaces is the *castle and moat*: a hard perimeter with a soft interior, where anything that got inside was assumed friendly. That model failed for reasons that are now obvious. Attackers get inside — through a phished laptop, a compromised dependency, an SSRF bug, a misconfigured bucket — and once inside, a flat trusted network hands them everything. Most large breaches of the last decade are lateral-movement stories, not perimeter-breach stories.

Three practical consequences for a backend engineer:

- **Authenticate every hop.** `OrderService` calling `PaymentService` must prove who it is, every call, even over a private subnet.
- **Authorize every call.** Identity is not permission. `OrderService` may call `PaymentService.Charge` and nothing else.
- **Assume breach.** Design so that a compromised service is a contained incident rather than a full one — short-lived credentials, narrow scopes, audited access.

> **Pitfall.** "Zero trust" is also a product category, and vendors will sell you a gateway and call it done. The architecture is not a product. A team that buys the gateway but still runs services with a shared static API key and a flat network has bought a logo.

### The problem: how does a service prove *what it is*?

Human authentication is well understood — you know a password, hold a device, present a passkey. Service authentication is harder, and the traditional answer is embarrassing when written down: we give the service a long-lived secret and hope nobody else reads it.

That secret has to be provisioned somehow, which creates the *secret zero* problem — the credential the service uses to fetch its other credentials from the vault. Wherever that lives (an environment variable, a file, a Kubernetes Secret, a build pipeline variable), it is a static string that grants access, never expires on its own, and is copied into every replica, every log that accidentally dumps the environment, and every core file.

**Workload identity** replaces it with attestation. Instead of the workload *knowing* a secret, the platform *vouches* for the workload: the infrastructure it runs on already knows it scheduled this container, from this image, in this namespace, under this service account, and can sign a statement to that effect. The workload presents that short-lived, verifiable statement instead of a password.

```
  static secret                          workload identity
  ─────────────                          ─────────────────
  service holds SECRET_KEY               platform attests: "this is
  forever, everywhere                    payments-api in ns=prod"
        │                                        │
        ▼                                        ▼
  leak = compromise until                 identity document, valid
  someone notices and rotates             for minutes, bound to the
  (median: never)                         workload, not copyable
```

### SPIFFE and SPIRE

**SPIFFE** (Secure Production Identity Framework For Everyone) is the vendor-neutral standard for this. Two concepts:

- A **SPIFFE ID** is a URI naming a workload: `spiffe://prod.contoso.com/ns/payments/sa/payments-api`. It is the *name*, deliberately hierarchical and readable.
- An **SVID** (SPIFFE Verifiable Identity Document) is the credential proving it — usually a short-lived X.509 certificate with the SPIFFE ID in the SAN, sometimes a JWT.

**SPIRE** is the reference implementation. Its interesting part is the *attestation chain*, which is how it avoids simply moving the secret-zero problem:

1. **Node attestation.** A SPIRE agent on each node proves the node's identity to the server using something the platform already vouches for — an AWS instance identity document, a Kubernetes node token, a TPM. No pre-shared secret.
2. **Workload attestation.** When a process asks the local agent for its identity, the agent inspects the *calling process* — its PID, and from there its cgroup, container, image, Kubernetes service account — and matches it against registration entries. The workload proves nothing; the platform observes it.
3. **Issuance and rotation.** The agent hands back an SVID valid for minutes to an hour, and rotates it automatically. Nothing is stored, nothing needs rotating by a human, and a stolen SVID expires before it is useful.

The workload receives its credential over a local Unix socket (the SPIFFE Workload API). It never handles a long-lived key.

### mTLS between services

With every workload holding a short-lived certificate, mutual TLS becomes practical: both sides present certificates, both verify, and the connection carries a cryptographic identity your code can authorize against rather than an IP address it must guess about.

```csharp
// Authorize on the peer's verified identity, not on where the packet came from.
app.Use(async (ctx, next) =>
{
    var cert = await ctx.Connection.GetClientCertificateAsync();
    var spiffeId = cert?.Extensions
        .OfType<X509SubjectAlternativeNameExtension>()
        .SelectMany(e => e.EnumerateUriNames())
        .FirstOrDefault(u => u.StartsWith("spiffe://"));

    if (spiffeId is null || !PolicyAllows(spiffeId, ctx.Request.Path))
    {
        ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
        return;
    }
    await next();
});
```

In practice you rarely write that yourself. A **service mesh** (Istio, Linkerd) puts a sidecar or node proxy in the path that terminates mTLS, rotates certificates, and enforces policy — so mTLS becomes a platform property rather than something each team implements. That is a genuine benefit and a real cost: the mesh hides the identity plumbing, which is fine until you are debugging a 403 that your application code never saw. Know what the mesh is doing on your behalf before you rely on it. [Chapter 26](#service-mesh-awareness) covers the operational side.

> **Gotcha.** mTLS authenticates the *service*, not the *user*. A request arriving from `orders-api` over mTLS still carries an end user whose permissions must be checked separately. Conflating the two is how you build a system where any authenticated service can read any customer's data — the confused deputy, wearing a certificate.

### OIDC federation: killing the last static credential in CI

The most valuable place to apply this is your build pipeline, because that is where the highest-value long-lived credentials traditionally live. [Chapter 26](#azure-pipelines-in-practice) mentions workload identity federation for Azure DevOps service connections; here is the mechanism, because the security of the whole arrangement rests on one configuration detail.

1. Your CI platform runs a job and mints a **short-lived, signed JWT** describing it: issuer (`https://token.actions.githubusercontent.com`), and claims including `repository`, `ref`, `workflow`, `environment`, and a composite `sub`.
2. The job presents that token to your cloud's STS.
3. The cloud validates the signature against the CI platform's published JWKS, then checks the token's claims against a **trust policy** you configured.
4. If it matches, the cloud returns credentials valid for minutes, scoped to a role you defined.

No secret is stored anywhere. Nothing needs rotating. A leaked build log contains, at worst, an expired token.

```yaml
# GitHub Actions — request an OIDC token, exchange it, hold nothing.
permissions:
  id-token: write
  contents: read

steps:
  - uses: aws-actions/configure-aws-credentials@v4
    with:
      role-to-assume: arn:aws:iam::111122223333:role/deploy-billing
      aws-region: eu-west-1
```

```json
// The trust policy — this condition IS the security boundary.
{
  "Effect": "Allow",
  "Principal": { "Federated": "arn:aws:iam::111122223333:oidc-provider/token.actions.githubusercontent.com" },
  "Action": "sts:AssumeRoleWithWebIdentity",
  "Condition": {
    "StringEquals": {
      "token.actions.githubusercontent.com:aud": "sts.amazonaws.com",
      "token.actions.githubusercontent.com:sub": "repo:contoso/billing:environment:production"
    }
  }
}
```

> **Pitfall — the wildcard that gives away production.** Two mistakes recur, and both are catastrophic. Using `StringLike` with `repo:contoso/*` lets *any* repository in your organization — including a new one a contractor creates, or a public one anyone can fork and open a PR against — assume your production deployment role. Omitting the `sub` condition entirely lets *any GitHub repository on the internet* assume it. Pin `sub` to a specific repository *and* a specific ref or environment, use `StringEquals`, and review these policies the way you would review a firewall rule facing the internet — because that is what they are.

The same pattern works for Azure (federated credentials on an app registration), GCP (Workload Identity Federation), and HashiCorp Vault (the JWT auth method). And it is not only for CI: **managed identities** on Azure, **IRSA / EKS Pod Identity** on AWS, and GKE Workload Identity apply the same idea to running workloads — the platform attests the pod, the cloud issues short-lived credentials, and your `DefaultAzureCredential` or AWS SDK picks them up with no configuration in your code.

### Where it degrades

Zero trust is a direction, not a binary state, and honest engineering means naming the parts that won't get there:

- **Legacy services** that cannot present or validate certificates. Front them with a proxy that terminates mTLS and speaks plain HTTP over a tightly restricted path — and be explicit that the trusted segment is now the last hop.
- **Third-party SaaS and appliances** that only accept an API key. Scope the key as tightly as the vendor allows, rotate it on a schedule you actually keep, and monitor its use.
- **Databases**, which mostly still want a username and password. Use IAM/Entra authentication where the engine supports it (RDS IAM auth, Azure SQL with Entra tokens) — that gets you short-lived credentials for the highest-value secret you own.
- **Break-glass access**, which must exist and must not be behind the same automation everything else is. Make it manual, heavily audited, and alarming when used.

The goal is not a perfect score. It is that the number of long-lived, broadly-scoped credentials in your organization trends toward zero, and that you can name every remaining one.

## Crypto Agility and the Post-Quantum Migration

The cryptography of [Chapter 12](#cryptography-for-developers), and the workload identity above, assume the algorithms hold. For most of your career they have, which has let us bake algorithm choices into code, config files, database columns, and certificate chains without thinking twice. That assumption now has an expiry date, and the interesting engineering problem is less "which algorithm" than "how quickly could we change ours?"

**Why this is a today problem, not a 2035 problem.** A sufficiently large quantum computer running Shor's algorithm breaks the mathematics that RSA and elliptic-curve cryptography rest on. No such machine exists, and credible estimates of when one might are all over the map. That would be someone else's problem except for one detail: **an adversary can record your encrypted traffic today and decrypt it later.** This is called *harvest now, decrypt later*, and it is not speculative — bulk capture of encrypted traffic is a known activity of well-resourced intelligence services.

So the question is not "when will quantum computers arrive." It is: **how long does this data need to stay confidential?** Session cookies, cache entries and short-lived tokens genuinely don't care. Medical records, legal case files, source code, diplomatic traffic, long-lived credentials, and anything with a statutory retention period of decades do. If the answer is "fifteen years," the migration deadline was some time ago.

Symmetric cryptography is much less affected. Grover's algorithm gives at best a quadratic speed-up against a symmetric cipher, which is handled by doubling the key size — AES-256 remains fine. Hashing is similar. The damage is concentrated in **asymmetric** primitives: key exchange (RSA, ECDH) and signatures (RSA, ECDSA, EdDSA).

**The standards.** NIST completed its selection process and published the first post-quantum standards in August 2024:

| Standard | Algorithm | Replaces | Used for |
|---|---|---|---|
| FIPS 203 | **ML-KEM** (formerly Kyber) | ECDH, RSA key transport | Key encapsulation — establishing a shared secret |
| FIPS 204 | **ML-DSA** (formerly Dilithium) | ECDSA, RSA signatures | General-purpose digital signatures |
| FIPS 205 | **SLH-DSA** (formerly SPHINCS+) | — | Hash-based signatures; conservative fallback, larger and slower |

Note the split. **Key exchange is the urgent half** — that is what harvest-now-decrypt-later attacks — while signatures mostly protect against *future* forgery and can migrate on a longer timeline (a signature verified today cannot be retroactively forged by a machine built in 2040).

**Hybrid, not replacement.** In TLS the deployed approach is a *hybrid* key exchange: perform both a classical ECDH and an ML-KEM encapsulation, and derive the session key from both. The connection is secure unless *both* are broken, which hedges against the real possibility that the new algorithms have implementation or analysis flaws we haven't found yet — they are, after all, much younger than the ones they replace. Hybrid key exchange is already the default in mainstream browsers and is widely supported by major CDNs and cloud load balancers, which means a significant share of the web's traffic is already post-quantum protected at the transport layer without any application changing.

**What this means for a .NET service, concretely.** For most of you, the honest answer is *less than the vendor pitch suggests*, because the TLS termination that matters is happening in your load balancer, CDN, or ingress controller — not in your code. Your practical work is:

1. **Inventory where cryptography lives.** This is the actual project, and it takes longer than any code change. TLS termination points; certificate issuance; JWT and token signing; data-protection key rings; field-level encryption in the database; signed URLs; client certificates; SSH and code-signing keys; anything with `RSA` or `ECDsa` in the source; and every third-party library or device you cannot upgrade. Most organizations discover they cannot answer "what algorithms are we using and where" at all, which is the finding.
2. **Turn on hybrid key exchange where the switch already exists** — your CDN and load balancer. This is usually a configuration flag and it protects the traffic most exposed to bulk capture.
3. **Fix the long-retention data first.** Anything you encrypt and store for years is where the harvest-now risk actually bites.
4. **Build agility into new code.** .NET 10 ships `MLKem`, `MLDsa` and `SlhDsa` types in `System.Security.Cryptography` (backed by the platform's native crypto — so availability depends on the underlying OpenSSL or Windows CNG version, and you should check `MLKem.IsSupported` rather than assume). Their real value right now is that you can build and test agility before you need it.

**Crypto agility is the deliverable.** The migration you should be planning for is not "to ML-KEM." It is "to whatever comes next, on demand" — because this will happen again. Agility is an architectural property with concrete implications:

- **Version your ciphertext.** Every encrypted blob should carry a small envelope identifying the algorithm and key that produced it, so a reader can decrypt old data with the old algorithm while new writes use the new one. `IDataProtector` already does this for you, which is one more reason to prefer it over hand-rolled AES.
- **Never hardcode an algorithm identifier** in a place you can't change without a deployment — and especially not in a database column, a wire format, or a public API contract.
- **Keep an interface between your code and the primitive**, so swapping the implementation is one class rather than a search-and-replace across the solution.
- **Rehearse rotation.** A key you have never rotated is a key you cannot rotate. If your incident plan says "rotate the signing key," do it once, deliberately, on a Tuesday, and find out what breaks.

> **Best practice.** Treat the inventory as the deliverable for this year and the algorithm swap as next year's. A team that knows exactly where its crypto lives can migrate in weeks whenever it needs to; a team that doesn't will need months no matter which algorithm is in fashion.

**The related deadline that will bite sooner.** Independently of quantum anything, the CA/Browser Forum has agreed a schedule that shortens the maximum lifetime of public TLS certificates in stages — from today's 398 days down to 47 days by March 2029, with domain validation reuse shrinking alongside it. Whatever you think about post-quantum timelines, **this one is dated and certain**, and it makes manual certificate handling untenable. If any certificate in your estate is renewed by a human following a runbook, that is now a scheduled outage. Automate issuance and renewal (ACME via Let's Encrypt, your cloud's certificate manager, or `cert-manager` in Kubernetes), monitor expiry as a first-class alert, and make sure the automation covers the awkward ones — internal services, client certificates, mutual TLS between services, and the load balancer nobody remembers configuring.

## The Software Supply Chain

Open your solution and count the projects. Now run `dotnet list package --include-transitive` and count the packages. For a typical ASP.NET Core service the second number is somewhere between fifty and four hundred. Almost none of it was written by anyone you can name, reviewed by anyone on your team, or built on a machine you control. It arrives as a compressed archive from a public server, and your build system unpacks it and links it into the thing you ship to customers.

That is the software supply chain, and for most of the last two decades our profession treated it as somebody else's problem. It isn't any more. Attackers worked out that compromising one popular package buys them access to thousands of downstream organizations, which is a far better return than attacking those organizations one at a time. The 2025 revision of the OWASP Top 10 promoted supply chain failures to their own category. The EU's Cyber Resilience Act turns parts of what follows into a legal obligation for anyone selling software into Europe.

The rest of this chapter is about the three places that trust can be violated — **what you consume**, **where you build**, and **what you publish** — and the specific controls that close each gap in a .NET shop. As always, the point is the mechanism. "Use a lockfile" is advice; understanding *which attack a lockfile actually stops, and which it doesn't*, is knowledge you can apply when the next attack looks slightly different.

## The Shape of the Problem

Start with the arithmetic, because it explains why intuition fails here.

You add one package. That package depends on four others; each of those depends on a handful more. Three levels down you are trusting perhaps two hundred distinct pieces of code and — this is the part that matters — perhaps a hundred distinct *human beings*, any one of whom can be phished, bribed, coerced, or simply have their laptop stolen. Your security posture is not the average of those hundred people's practices. It's the **minimum**.

```
        your service
             │
   ┌─────────┼─────────┐
   A         B         C          ← the 12 packages you chose
   │       ┌─┴─┐       │
   D       E   F       G          ← the 40 you didn't
   │           │      ┌┴┐
   H           I      J K         ← the 150 you've never heard of
                             ← any single maintainer here can reach production
```

Three distinct surfaces sit on that picture, and they need different controls:

| Surface | What the attacker wants | Representative attack | Primary control |
|---|---|---|---|
| **Packages you consume** | Get malicious code into your dependency graph | Typosquatting, dependency confusion, maintainer account takeover | Source mapping, lockfiles, signature verification |
| **The build that assembles them** | Modify the artifact *after* the source is clean | SolarWinds, compromised CI action | Pinned, least-privilege, isolated CI |
| **Artifacts you publish** | Use *you* as the delivery vehicle to your customers | Your package or image, backdoored | Signing, provenance attestation, SBOM |

Note the middle row. A great deal of energy goes into reviewing dependencies, which addresses only the first row. SolarWinds' source code was fine; the malware was injected during compilation. If your threat model stops at "are my packages safe," you have covered a third of the problem.

> **The core reframe.** Supply chain security is not about auditing other people's code — you cannot read two hundred packages, and neither can anyone else. It is about *limiting what an untrusted dependency can reach*, *knowing exactly what you shipped*, and *being able to answer questions quickly when something turns out to be bad*.

## Attacks on What You Consume

### Typosquatting, and its newer cousin

The simplest attack: publish `Newtonsofl.Json` and wait for a typo. Registries have gotten better at detecting near-miss names, so this alone is no longer very productive — but the mechanism has been given fresh life by AI coding assistants.

An LLM asked for a package to do X will occasionally invent one that doesn't exist, confidently and *repeatably* — the same plausible-sounding name across many sessions. Attackers query models for hallucinated package names, register the popular ones, and wait. The industry has taken to calling this **slopsquatting**. It converts a model's hallucination into a real, installable, malicious package, and it defeats the developer's usual sanity check, because the name looks right — it was generated by the same statistical process that makes real package names look the way they do.

> **Pitfall.** "The assistant suggested it and it installed fine" is not evidence a package exists legitimately. Before adding any package you have not used before, look at it on nuget.org: download count, repository link, publication history, whether the source repo actually corresponds. This takes fifteen seconds and is the single highest-value habit in this chapter.

### Dependency confusion, and why pinning does not fix it

This one is worth understanding precisely, because the obvious fix is the wrong one.

Suppose your company has an internal package, `Contoso.Billing.Client`, published to a private Azure Artifacts feed. Your `nuget.config` lists two sources: the private feed and nuget.org. You ask for version `2.1.0`.

NuGet does **not** ask the private feed first and stop when it succeeds. Given multiple sources, restore queries them and — for a floating or unsatisfied version — takes the best match it finds anywhere. So an attacker who learns the name `Contoso.Billing.Client` (from a leaked `packages.config`, a stray build log, a public fork, a job advert listing your internal library names) publishes `Contoso.Billing.Client 99.0.0` to the *public* nuget.org. Your next restore finds a higher version on a source it also trusts, and pulls the attacker's package into your build.

Now consider the reflexes people reach for:

- **"Pin the exact version."** Helps here, but it is brittle and it does not survive the first developer who bumps a version. It also does nothing for a brand-new internal package name.
- **"Remove nuget.org and mirror everything internally."** Works, and some regulated shops do it, but it is a large operational commitment.
- **"Reorder the sources."** Does nothing. Source order is not precedence.

The actual fix is **package source mapping**: telling NuGet, per package-ID pattern, which source is *allowed* to serve it. Any source not mapped is never consulted for that ID, so the public registry is structurally incapable of answering for `Contoso.*`.

```xml
<!-- nuget.config -->
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
    <add key="contoso" value="https://pkgs.dev.azure.com/contoso/_packaging/internal/nuget/v3/index.json" />
  </packageSources>

  <packageSourceMapping>
    <!-- Everything by default comes from nuget.org... -->
    <packageSource key="nuget.org">
      <package pattern="*" />
    </packageSource>
    <!-- ...except our own namespace, which ONLY the private feed may serve. -->
    <packageSource key="contoso">
      <package pattern="Contoso.*" />
    </packageSource>
  </packageSourceMapping>
</configuration>
```

The longest matching pattern wins, so `Contoso.*` beats `*`. A public package claiming to be `Contoso.Billing.Client` is now unreachable, at any version, forever. That is what a structural fix looks like: it removes the attacker's move rather than racing them on version numbers.

> **Best practice.** Reserve your company's ID prefix on nuget.org as well. Prefix reservation means nobody else can publish under it publicly, which protects developers on machines that don't have your `nuget.config`.

### The maintainer is a single point of failure

The `event-stream` incident in 2018 remains the cleanest illustration: a burned-out maintainer of a widely used package accepted help from a friendly stranger, handed over publish rights, and the new maintainer shipped a release that stole cryptocurrency wallets. No exploit, no vulnerability — just a legitimate publish by an account with legitimate rights.

Since then the same pattern has scaled up. Phishing campaigns against registry maintainers now run continuously, and self-propagating worms have appeared in the npm ecosystem: malicious code that, on execution in a developer or CI environment, harvests registry tokens and cloud credentials from the machine and uses any token it finds to publish infected versions of *that* maintainer's packages, spreading outward without human involvement. In September 2025 one such worm compromised hundreds of npm packages in a single run.

**Where .NET sits in this, precisely.** NuGet does not have npm's `postinstall` hook. Restoring a package does not, by itself, execute code from it — which genuinely removes the most common infection vector. But "no install scripts" is not "no execution," and the difference gets people hurt:

- A package can ship `build/*.props` and `build/*.targets`, which MSBuild **imports into your build**. Anything MSBuild can do — run a task, invoke a tool, execute an `Exec` — that file can do, on every build, on every developer machine and CI runner.
- **Analyzers and source generators** ship as packages and run *inside the compiler process* every time you build, including in your IDE as you type.
- **`dotnet tool` packages** execute when invoked, and `dotnet tool restore` from a manifest fetches them.
- Templates, MSBuild SDKs, and custom task assemblies are all code that runs at build time.

So a compromised NuGet package does not need you to call its API. It needs you to *build*. Treat "we restored it but never referenced the type" as no protection at all.

## Pinning What You Actually Build

### Lockfiles

By default, `dotnet restore` resolves your dependency graph fresh each time. `PackageReference` versions are *minimums*, not exact pins, and transitive versions are decided by the resolver. Two restores a week apart can produce different closures.

A lockfile freezes the entire resolved graph — every transitive package, exact version, and content hash:

```xml
<PropertyGroup>
  <RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>
</PropertyGroup>
```

That produces `packages.lock.json` next to the project. **Commit it.** Then, in CI:

```bash
dotnet restore --locked-mode
```

`--locked-mode` fails the build if the lockfile and the project files disagree. This matters more than it sounds: it converts "a dependency silently changed" from an invisible event into a red build that a human must look at. It also makes your builds repeatable, which is a prerequisite for investigating anything later.

What a lockfile buys you:
- Restores are reproducible; the graph cannot drift under you.
- A change in the transitive closure becomes a reviewable diff in a pull request.
- The recorded content hashes detect a package whose contents changed under a fixed version.

What it does **not** buy you: protection from a malicious version you deliberately upgrade to. A lockfile makes changes *visible*, not *safe*. That distinction is the whole game — most supply chain controls are detection and blast-radius controls wearing prevention's clothing.

> **Gotcha.** `<FloatingVersion>`-style references (`Version="8.*"`) and the `latest` habit undo this. A floating version is a standing invitation to whoever compromises that package next. Floating is defensible for internal packages inside one repo's release train; it is not defensible for third-party dependencies.

### Central package management

For a solution with more than a handful of projects, `Directory.Packages.props` ([Chapter 13](#msbuild-directorybuildprops-and-central-package-management)) gives you one place where every version lives — and one property that matters for security:

```xml
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
    <CentralPackageTransitivePinningEnabled>true</CentralPackageTransitivePinningEnabled>
  </PropertyGroup>
  <ItemGroup>
    <PackageVersion Include="Serilog.AspNetCore" Version="9.0.0" />
    <PackageVersion Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="9.0.4" />
  </ItemGroup>
</Project>
```

Security value, beyond tidiness: version bumps concentrate in one file that you can require review on (a `CODEOWNERS` entry), and transitive pinning lets you force a patched version of an indirect dependency without waiting for the direct dependency to update.

### Verifying signatures

NuGet packages can carry an author signature (the publisher's certificate) and a repository signature (nuget.org counter-signs everything it serves). You can require them:

```xml
<configuration>
  <config>
    <add key="signatureValidationMode" value="require" />
  </config>
  <trustedSigners>
    <repository name="nuget.org" serviceIndex="https://api.nuget.org/v3/index.json">
      <certificate fingerprint="..." hashAlgorithm="SHA256" allowUntrustedRoot="false" />
      <owners>contoso;dotnetfoundation</owners>
    </repository>
  </trustedSigners>
</configuration>
```

The `<owners>` element is the interesting part: it constrains not just "signed by nuget.org" but "published by an account I named." That turns an account-takeover of some *other* publisher into a restore failure rather than a silent substitution.

You can also check an individual artifact directly:

```bash
dotnet nuget verify ./artifacts/Contoso.Billing.Client.2.1.0.nupkg --all
```

> **Be honest about what signing proves.** A signature proves the package came from the account that holds the key and has not been altered in transit. It proves nothing about the code's intent. Every self-propagating registry worm publishes perfectly, validly signed packages — it stole the credentials that make signing legitimate. Signing raises the cost of impersonation; it does not detect malice.

### Update policy: neither frozen nor firehose

Two failure modes, both common. Freeze everything and you accumulate known CVEs until an upgrade becomes a quarter-long project. Auto-merge everything and you have automated the delivery of whatever a compromised maintainer publishes next.

A workable middle:

- **Security patches**: fast lane. Automated PR, auto-merge on green for patch-level bumps of packages you already trust.
- **Everything else**: batched weekly or monthly, reviewed as a group, with the lockfile diff as the review artifact.
- **A cooldown window.** Configure your bot to ignore releases younger than a few days (Renovate calls this `minimumReleaseAge`). Most malicious releases are detected and yanked within hours; a 3–7 day delay costs you almost nothing and steps around the majority of these incidents entirely. This is the highest-leverage single setting in your dependency automation.
- **`dotnet list package --vulnerable --include-transitive`** in CI, failing on high severity ([Chapter 12](#dependency-scanning)), plus GitHub's dependency review on pull requests.

## The Build Is Part of the Attack Surface

Here is the row people skip. Your CI runner checks out your source, has credentials to your registries and clouds, and produces the artifact that goes to production. It is, in effect, a production machine with a shell exposed to anyone who can influence a workflow file.

### Pin your actions by commit SHA, not by tag

A Git tag is a mutable pointer. `uses: some-org/some-action@v4` means "whatever commit `v4` points at *at the moment your job runs*." If an attacker gains write access to that repository, they repoint the tag and every downstream workflow executes their code on the next run — with your secrets in the environment.

This is not hypothetical. In March 2025 a very widely used action was compromised exactly this way: its tags were repointed to a malicious commit that dumped the runner's memory — including secrets — into the publicly readable build log. Tens of thousands of repositories were affected within hours.

```yaml
# Mutable. The code that runs here can change without you doing anything.
- uses: actions/checkout@v4

# Immutable. This is a specific tree of code, forever.
- uses: actions/checkout@11bd71901bbe5b1630ceea73d27597364c9af683 # v4.2.2
```

Keep the human-readable tag in a trailing comment so the pin stays reviewable, and let Dependabot or Renovate update SHAs for you — both understand this format.

### Least privilege for the workflow token

Default token permissions are frequently far broader than a build needs. Set them down explicitly, at the top of the workflow, and grant back only what a specific job requires:

```yaml
permissions:
  contents: read          # the floor for everything

jobs:
  publish:
    permissions:
      contents: read
      packages: write     # only this job may push a package
      id-token: write     # only this job may mint an OIDC token
```

> **Pitfall — `pull_request_target`.** This trigger runs the workflow definition from the *base* branch but with a token that has write access, and people combine it with a checkout of the *fork's* head. That combination executes an untrusted contributor's code with your write-scoped token. If you need to run something against a fork's code, use `pull_request` (no secrets, read-only) and split any privileged step into a separate, tightly scoped workflow.

### Stop putting long-lived cloud credentials in CI

A static `AWS_SECRET_ACCESS_KEY` in repository secrets is a credential that never expires, is copied into every job that references it, and appears in the memory of every step that runs. OIDC federation replaces it: the runner requests a short-lived signed token asserting *which repository, branch, and workflow* is running, and your cloud exchanges it for credentials valid for minutes.

This is the trust-chain machinery of [OIDC federation](#oidc-federation-killing-the-last-static-credential-in-ci) earlier in this chapter — including the failure mode where a too-loose `sub` claim condition on the cloud side lets *any* repository in your org, or in some misconfigurations any repository at all, assume the role. Get that condition right; it is the whole security boundary.

### Deterministic builds

Two builds of the same commit should produce the same bytes. When they do, anyone can independently rebuild your source and compare — which is the only way to detect a build system that is quietly modifying output.

```xml
<PropertyGroup>
  <Deterministic>true</Deterministic>                              <!-- default in modern SDKs -->
  <ContinuousIntegrationBuild Condition="'$(CI)' == 'true'">true</ContinuousIntegrationBuild>
  <PublishRepositoryUrl>true</PublishRepositoryUrl>
  <EmbedUntrackedSources>true</EmbedUntrackedSources>
  <IncludeSymbols>true</IncludeSymbols>
  <SymbolPackageFormat>snupkg</SymbolPackageFormat>
</PropertyGroup>
```

`ContinuousIntegrationBuild` normalizes embedded file paths (otherwise your agent's directory layout leaks into the binary and defeats byte-comparison). Source Link embeds the commit, so any consumer of your package can go from a stack frame to the exact line of source that produced it — useful for debugging, and useful for *proving* what a published artifact was built from.

### The build environment itself

- **Ephemeral runners.** A fresh container per job means malware cannot persist to poison the next build. Self-hosted, long-lived runners on public repositories are the worst combination available.
- **Isolate the privileged steps.** The job that runs tests (executing arbitrary contributor code) should not be the job that holds the signing key.
- **Egress control on the runner** is the control that turns a successful compromise into a failed exfiltration. If the build only needs nuget.org and your registry, an allowlist means the credential-stealer has nowhere to send what it stole.

## Knowing What You Shipped: SBOMs

A **Software Bill of Materials** is a machine-readable inventory of everything in a build: components, versions, and ideally hashes and licences. Two formats matter — **CycloneDX** (OWASP; security-oriented) and **SPDX** (Linux Foundation; originally licence-oriented, now an ISO standard). Either is fine; consistency matters more than the choice.

```bash
dotnet tool install --global CycloneDX
dotnet CycloneDX ./src/Contoso.Billing.sln -o ./artifacts --json
```

Generate it **in the build that produces the artifact**, from the resolved graph, and publish it as a build artifact alongside the binary. An SBOM generated later by scanning a repository is a guess; one generated during the build is a record.

The honest value proposition:

**What an SBOM genuinely gives you** is speed of answer. When the next `Log4Shell`-scale disclosure lands at 9 a.m., the question every executive asks is "are we affected, and where?" Without SBOMs, that is a multi-day archaeology project across every service. With a stored SBOM per release, it is a query — including for services nobody has deployed in eight months, which are exactly the ones nobody remembers.

**What it does not give you** is safety. An SBOM is an inventory, not an analysis. It will not tell you whether the vulnerable code path is reachable in your usage, and it does not detect a package that is malicious but not yet publicly known to be. Teams that generate SBOMs, upload them to a bucket and never query them have bought a compliance checkbox, not a capability.

> **Best practice.** Feed SBOMs into something that continuously re-evaluates them against new advisories — OWASP Dependency-Track is the common open-source choice. The value is in the *standing query*, not the document.

## Proving How You Built It: Provenance

An SBOM says what is inside. **Provenance** says where the artifact came from: which source commit, which build system, which workflow, at what time. It is a signed statement produced by the build platform itself, so it cannot be forged by someone who merely has your package.

**SLSA** (Supply-chain Levels for Software Artifacts) is the framework that gives this a ladder to climb. In SLSA v1.0's Build track:

- **Build L1** — provenance exists and describes how the artifact was built.
- **Build L2** — provenance is signed by a hosted build platform, so it is authenticated.
- **Build L3** — the build runs in an isolated environment with non-falsifiable provenance; a build cannot forge the provenance of another build.

In GitHub Actions, L2-style provenance is roughly one step:

```yaml
permissions:
  id-token: write
  attestations: write
  contents: read

steps:
  - uses: actions/attest-build-provenance@v2
    with:
      subject-path: 'artifacts/*.nupkg'
```

For container images, the equivalent world is **Sigstore**: `cosign` signs the image, and keyless signing binds the signature to a workflow identity via OIDC rather than a key you have to store and rotate.

```bash
cosign sign --yes ghcr.io/contoso/billing@sha256:abcd...

cosign verify ghcr.io/contoso/billing@sha256:abcd... \
  --certificate-identity-regexp '^https://github.com/contoso/billing/.github/workflows/release.yml@refs/tags/v.*' \
  --certificate-oidc-issuer https://token.actions.githubusercontent.com
```

Read that verify command carefully, because it is the point of the whole exercise. It does not ask "is this signed?" — anyone can sign anything. It asks "was this image produced by *that workflow*, in *that repository*, on a *release tag*?" An attacker who pushes an image built anywhere else fails the check.

And a signature nobody verifies is decoration. The verification has to live somewhere that can say no: a Kubernetes admission controller (Kyverno, Sigstore's policy-controller, or Connaisseur) that refuses to admit an unsigned or unattested image. Until that gate exists, you have added a signing step to your pipeline and changed nothing about what can run.

> **Deploy-time is the right gate.** Build-time checks catch mistakes; admission control catches attacks. The attacker's whole objective is to introduce an artifact that never went through your build.

## When It Happens: The Response Playbook

Assume, one Tuesday, an advisory lands: a package in your graph shipped a malicious version for eleven hours two days ago. Prepared teams work this in an hour; unprepared teams work it for a week. The difference is entirely in what you set up beforehand.

1. **Was the bad version ever resolved?** Grep your committed lockfiles across repositories — including release branches and tags, not just `main`. This is a one-command answer *because* the lockfiles are committed. Without them, you are re-resolving historical graphs and guessing.
2. **Did it ever reach a build?** Check restore logs and the SBOMs attached to each release. An SBOM per artifact answers "which deployed versions contain it" directly.
3. **Assume secret exposure if it ran.** If a malicious package executed on a runner or a developer machine, treat every credential that machine could see as compromised: registry tokens, cloud credentials, signing keys, SSH keys, `~/.nuget/plugins`, environment variables. Rotate them. Do not reason your way to "it probably didn't get that one" — that reasoning is exactly what the worms rely on.
4. **Pin forward, not backward.** Move to a known-good version and pin it; "roll back to the previous version" is wrong if the previous version was also compromised (frequently true — attackers backport).
5. **Rebuild and republish everything downstream.** If you publish packages or images that embedded the bad dependency, your consumers are now in step 1 of their own version of this list. Tell them, with versions and times.
6. **Write down what made it slow.** The gaps you hit at 3 p.m. on Tuesday are your backlog for the next quarter, and they will be specific: "we couldn't tell which release contained it," "nobody knew who owned that service," "the runner had a token that never expires."

> **Gotcha.** Yanking a package from a registry does not remove it from your caches. Build agents, `~/.nuget/packages`, Docker layer caches, and internal mirrors will keep serving it happily. Purge the caches explicitly, or your "fixed" build will restore the malicious version from disk.

## The Regulatory Floor

For a long time everything above was optional diligence. That is changing, and it is worth knowing the shape of the obligations even if compliance isn't your job — they determine what your customers will start demanding of you in procurement questionnaires.

- **EU Cyber Resilience Act (CRA).** In force since December 2024. It covers "products with digital elements" sold in the EU — which includes most commercial software, not just devices. Obligations phase in: reporting duties for actively exploited vulnerabilities from **September 2026**, and the main body of requirements from **December 2027**. Substantively it mandates security-by-design, an SBOM maintained by the manufacturer, coordinated vulnerability disclosure, and security updates through a defined support period. That last one has teeth: shipping and abandoning becomes a compliance problem, not just bad manners.
- **US Executive Order 14028 and NIST SSDF (SP 800-218).** If you sell to the US federal government, self-attestation against the Secure Software Development Framework is already part of procurement. SSDF is also a genuinely decent checklist independent of whether it applies to you.
- **PCI DSS v4.0** added explicit inventory requirements for third-party components and scripts.

The common thread is that *knowing and proving what you shipped* is becoming a legal requirement, not just an engineering good idea. Teams that already generate SBOMs and provenance will find compliance mostly a documentation exercise. Teams that don't will find it a re-platforming project.

## Where to Start

You cannot do all of this next sprint. Ordered by value per hour of effort, for a typical team:

| # | Control | Effort | Stops |
|---|---|---|---|
| 1 | Package source mapping in `nuget.config` | ~1 hour | Dependency confusion, permanently |
| 2 | Pin GitHub Actions by SHA + `permissions: contents: read` | ~2 hours | Compromised action running with your secrets |
| 3 | Lockfiles + `--locked-mode` in CI | ~half a day | Silent graph drift; makes incident response a query |
| 4 | Dependency-update cooldown window | ~15 minutes | Most malicious releases, which are yanked within hours |
| 5 | `--vulnerable --include-transitive` gate in CI | ~1 hour | Shipping known-vulnerable dependencies |
| 6 | OIDC instead of long-lived cloud secrets in CI | ~1 day | The standing prize for compromising your build |
| 7 | SBOM per release, stored and queryable | ~2 days | Multi-day archaeology on the next Log4Shell |
| 8 | Signing + provenance + admission control | ~1 week | Artifacts that never came from your build |

Items 1, 2 and 4 together take an afternoon and remove the three most commonly exploited paths. Do those first, then argue about the rest.

## Summary

Two habits run under the whole chapter. First, prefer *not having* a secret to guarding one: zero trust means network position confers no trust, workload identity lets the platform vouch for a service instead of the service holding a password, and OIDC federation removes the last static credential from CI — provided the trust policy pins the subject exactly. Second, assume your current choices will have to change: inventory where cryptography lives, version your ciphertext, turn on hybrid key exchange where the switch exists, and automate certificates before shrinking lifetimes make manual renewal an outage.

Your dependency graph is a list of people who can reach production, and it is longer than you think. Three surfaces need defending, not one: what you consume, the build that assembles it, and what you publish onward. Package source mapping structurally eliminates dependency confusion; lockfiles make graph changes visible and incident response a query rather than an excavation; signature verification with named owners raises the cost of impersonation. In CI, pin actions by SHA because tags are mutable pointers into someone else's repository, cut token permissions to the floor, replace long-lived cloud credentials with short-lived OIDC, and remember that a build runner is a production machine holding your secrets. On the way out, an SBOM tells you what you shipped and provenance proves where it came from — but only if something verifies them at deploy time, and only if you actually query them when the advisory lands.

None of this makes an untrusted dependency trustworthy. That is not the goal. The goal is that when — not if — one of those hundred maintainers has a bad day, you find out fast, you know exactly what you shipped, and the blast radius stops well short of production.

## Sources & Further Reading

- **Microsoft Learn — "Package Source Mapping"** and **"Central Package Management"**, NuGet documentation. https://learn.microsoft.com/nuget/consume-packages/package-source-mapping
- **Microsoft Learn — "Locking dependencies" (`packages.lock.json`, `--locked-mode`)** and **"Signed packages / trusted signers."** https://learn.microsoft.com/nuget/consume-packages/
- **OWASP Top 10 (2025)** — the software supply chain failures category, and **OWASP Dependency-Track** for continuous SBOM analysis. https://owasp.org/
- **SLSA v1.0 specification** — Build track levels L1–L3 and the provenance format. https://slsa.dev/
- **Sigstore documentation** — `cosign`, keyless signing, and identity-based verification. https://docs.sigstore.dev/
- **CycloneDX** (https://cyclonedx.org/) and **SPDX** (https://spdx.dev/) specifications; the `CycloneDX` .NET global tool.
- **GitHub Docs — "Security hardening for GitHub Actions"** (SHA pinning, `permissions`, `pull_request_target` hazards) and **"Artifact attestations."** https://docs.github.com/actions/security-guides/
- **NIST SP 800-218**, *Secure Software Development Framework (SSDF)*. https://csrc.nist.gov/publications/detail/sp/800-218/final
- **European Commission — Cyber Resilience Act**, obligations and application timeline. https://digital-strategy.ec.europa.eu/en/policies/cyber-resilience-act
- **CISA / NSA — "Securing the Software Supply Chain"** guidance series for developers and suppliers. https://www.cisa.gov/
- Post-incident write-ups worth reading in full: the **xz-utils backdoor (CVE-2024-3094)** for multi-year social engineering into a build system, **SolarWinds** for build-time injection with clean source, and the **`tj-actions/changed-files` compromise (CVE-2025-30066)** for mutable tags in CI.

- **SPIFFE / SPIRE documentation** — SPIFFE IDs, SVIDs, and node and workload attestation. https://spiffe.io/
- **NIST FIPS 203, 204 and 205** — ML-KEM, ML-DSA and SLH-DSA, the first post-quantum standards. https://csrc.nist.gov/
