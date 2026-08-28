# Chapter 14: Security

_⏱️ Estimated read time: ~32 min ·     5086 words (study pace)_

Security is not a feature you bolt on at the end of a sprint. It is a property of a system that emerges from thousands of small decisions: how you parse input, where you store a connection string, which overload of a crypto API you call, and whether you trusted a value that came from the network. A senior .NET developer is expected to make those decisions correctly by reflex, and to recognize when a colleague has not.

This chapter builds that reflex. We start with the mindset, walk the OWASP Top 10 with concrete .NET mitigations, then go deep on the machinery you will actually touch: authentication and authorization, OAuth 2.0 and JWTs, identity providers, secrets, TLS, cryptography, and the web-facing defenses (input validation, output encoding, CSRF, CORS, security headers). We finish with keeping your dependencies clean.

## The Security Mindset

Before any specific technique, internalize four principles. They are not slogans; they are decision procedures you apply when the "how" is unclear.

**Defense in depth.** Assume every single control will eventually fail, and layer independent controls so that one failure is not a breach. A parameterized query stops SQL injection — but you still validate input, run the database account with least privilege, and log anomalies. If an attacker slips past one layer, the next catches them. Never let your entire security posture rest on a single line of code.

**Least privilege.** Every component — a user, a service account, a process, a token — gets exactly the permissions it needs to do its job and nothing more. The web app's database login should not be `db_owner`. The background worker that reads a queue should not have write access to the whole storage account. A JWT scoped to `orders:read` should not be able to delete anything. When a component is compromised, least privilege bounds the blast radius.

**Secure by default.** The default configuration must be the safe configuration. A new controller action should require authorization unless you deliberately open it. HTTPS should be mandatory out of the box. If a developer forgets to configure something, the system should fail closed (deny) rather than fail open (allow). ASP.NET Core largely embraces this — for example, the framework's HTTPS redirection and HSTS templates ship enabled — but you are responsible for keeping it that way.

**Never trust input.** Every byte that crosses a trust boundary — HTTP request bodies, query strings, headers, cookies, file uploads, messages from a queue, responses from a third-party API, even data read back from your own database — is potentially hostile. Trust is earned by validation, not granted by origin.

> **Best practice:** Treat "the client already validated this" as a comment, never a guarantee. Client-side validation is a UX nicety. Server-side validation is the security control. An attacker uses `curl`, not your form.

## The OWASP Top 10, with .NET Mitigations

The OWASP Top 10 is the industry's consensus list of the most critical web application risks. Below is each category with the mitigation you apply in .NET. Learn the *category*, not just the trick — the categories are stable even as frameworks change.

> **Note:** This walk-through follows the **2021 edition** (A01–A10 below). OWASP published a revised Top 10 in 2025 — notably elevating software supply chain failures to its own category, which [Chapter 35](#chapter-35-software-supply-chain-security) covers in full — but the list is deliberately stable between editions, and every .NET mitigation here carries over unchanged.

### A01: Broken Access Control

The most common serious flaw: a user can act on data or functions they should not reach. The classic form is **Insecure Direct Object Reference (IDOR)** — `GET /api/invoices/1005` returns invoice 1005 even though it belongs to another tenant, simply because the code fetched by ID without checking ownership.

The mitigation is to enforce authorization on *every* request at the resource level, server-side. Do not rely on the UI hiding a button.

```csharp
[HttpGet("api/invoices/{id:int}")]
[Authorize]
public async Task<IActionResult> GetInvoice(int id)
{
    var invoice = await _db.Invoices.FindAsync(id);
    if (invoice is null) return NotFound();

    // Resource-level check: does this invoice belong to the caller?
    var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
    if (invoice.OwnerId != userId)
        return NotFound(); // 404, not 403 — don't confirm the resource exists

    return Ok(invoice);
}
```

> **Pitfall:** Returning `403 Forbidden` for a resource the user doesn't own leaks its existence. Prefer `404` for cross-tenant access so attackers can't enumerate valid IDs.

For anything beyond trivial checks, use ASP.NET Core's resource-based authorization (`IAuthorizationService.AuthorizeAsync`) so the ownership logic lives in a reusable handler rather than being copy-pasted into every action.

### A02: Cryptographic Failures (formerly "Sensitive Data Exposure")

Sensitive data is stored or transmitted without adequate protection: passwords hashed with MD5, PII sent over HTTP, secrets in source control, weak or home-grown crypto. The mitigations are covered in depth in the Cryptography section below, but the headline rules are: enforce TLS everywhere, hash passwords with a slow adaptive algorithm, encrypt sensitive data at rest, and never invent your own cryptography.

### A03: Injection

Untrusted input is interpreted as code or commands — SQL, OS commands, LDAP, NoSQL queries. **SQL injection** remains the canonical example. The fix is to keep data and code strictly separated using parameterized queries, never string concatenation.

```csharp
// VULNERABLE — never do this
var sql = $"SELECT * FROM Users WHERE Email = '{email}'";
// Input:  ' OR '1'='1' --   returns every row.

// SAFE — parameterized (ADO.NET)
using var cmd = new SqlCommand(
    "SELECT * FROM Users WHERE Email = @email", connection);
cmd.Parameters.Add("@email", SqlDbType.NVarChar, 256).Value = email;
```

Entity Framework Core parameterizes automatically for LINQ, and `FromSqlInterpolated` safely parameterizes interpolated strings — but `FromSqlRaw` with a manually built string reintroduces the hole.

```csharp
// SAFE — EF Core turns the interpolation into parameters
var users = await _db.Users
    .FromSqlInterpolated($"SELECT * FROM Users WHERE Email = {email}")
    .ToListAsync();
```

For OS commands, never pass user input to a shell; use `ProcessStartInfo` with an argument list rather than a single command string.

### A04: Insecure Design

A category about missing or ineffective security controls at the *design* level — flaws no amount of clean coding can fix because the architecture itself is wrong (e.g., no rate limiting on a password-reset endpoint, or trusting a price sent by the client). The mitigation is threat modeling: before building, ask "how would I abuse this?" Design in rate limits, business-logic validation, and secure defaults from the start.

For rate limiting, you no longer need a third-party package: since .NET 7, ASP.NET Core ships rate-limiting middleware with fixed-window, sliding-window, token-bucket, and concurrency policies, applied globally or per-endpoint.

```csharp
builder.Services.AddRateLimiter(o => o.AddFixedWindowLimiter("login", w =>
{
    w.PermitLimit = 5;
    w.Window = TimeSpan.FromMinutes(1);
}));
app.UseRateLimiter();
// then: app.MapPost("/login", ...).RequireRateLimiting("login");
```

### A05: Security Misconfiguration

Default credentials, verbose error pages exposing stack traces, unnecessary features enabled, missing security headers, permissive CORS. In .NET, the common offenders are leaving the developer exception page on in production and disabling HTTPS redirection.

```csharp
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();   // detailed errors — DEV ONLY
}
else
{
    app.UseExceptionHandler("/error"); // generic message in prod
    app.UseHsts();
}
app.UseHttpsRedirection();
```

> **Pitfall:** A stack trace in a production 500 response is a gift to an attacker — it reveals framework versions, file paths, and internal type names. Ensure `ASPNETCORE_ENVIRONMENT` is `Production` on your servers.

### A06: Vulnerable and Outdated Components

You inherit every vulnerability in every NuGet package and transitive dependency. A CVE in a JSON parser or logging library is your CVE. The mitigation is active dependency management and scanning — covered in the final section.

### A07: Identification and Authentication Failures

Weak passwords allowed, no protection against credential stuffing or brute force, session IDs in URLs, sessions that never expire, missing MFA. Prefer a battle-tested identity system (ASP.NET Core Identity or an external IdP) over rolling your own login. Enforce account lockout, support MFA, and use short-lived tokens with refresh.

### A08: Software and Data Integrity Failures

Code or data from untrusted sources is used without integrity checks — including **insecure deserialization**, where an attacker crafts a serialized payload that executes code or corrupts state on deserialization. In .NET this historically meant `BinaryFormatter`, which is so dangerous it is now obsolete and removed from modern runtimes.

> **Best practice:** Never use `BinaryFormatter`, `NetDataContractSerializer`, `SoapFormatter`, or `LosFormatter`. For data interchange use `System.Text.Json`, and never deserialize type information from untrusted input (avoid `TypeNameHandling.All` in Newtonsoft.Json).

This category also covers unsigned software updates and untrusted CI/CD pipelines — verify integrity of what you deploy.

### A09: Security Logging and Monitoring Failures

If a breach happens and no one notices for months, your logging failed. You need to log security-relevant events (failed logins, access-control denials, input-validation failures) with enough context to investigate — but *without* logging secrets, passwords, tokens, or full PII.

```csharp
// Log the event and the actor, never the credential
_logger.LogWarning("Failed login for user {UserId} from {IP}",
    userId, HttpContext.Connection.RemoteIpAddress);
```

> **Pitfall:** Logging request bodies or headers wholesale will eventually capture an `Authorization: Bearer ...` token or a password field. Redact deliberately.

### A10: Server-Side Request Forgery (SSRF)

Your server fetches a URL supplied by the user, and an attacker points it at internal resources — `http://169.254.169.254/` (cloud metadata endpoints), internal admin panels, or `localhost`. Mitigate by validating and allow-listing destinations, resolving and checking the target IP is not private/loopback/link-local, and disabling redirects on outbound requests that use user-controlled URLs.

## Authentication vs. Authorization

These two words are constantly confused, so pin them down precisely:

- **Authentication (AuthN)** answers *"Who are you?"* — it establishes and verifies identity. Logging in with a password, presenting a certificate, or validating a JWT are authentication.
- **Authorization (AuthZ)** answers *"What are you allowed to do?"* — it decides whether an already-identified principal may perform an action. Checking a role, a scope, or resource ownership is authorization.

Authentication always comes first; you cannot authorize an unknown principal. In ASP.NET Core the two are distinct middleware, and *order matters*:

```csharp
app.UseAuthentication(); // figures out WHO — populates HttpContext.User
app.UseAuthorization();  // figures out WHAT — enforces [Authorize] policies
```

A `401 Unauthorized` means "I don't know who you are" (authentication failed). A `403 Forbidden` means "I know who you are, but you can't do this" (authorization failed). Despite its name, `401` is about authentication.

## OAuth 2.0, OpenID Connect, and JWTs

Modern applications rarely handle passwords directly. Instead they delegate to an identity provider using **OAuth 2.0** (an authorization framework) and **OpenID Connect** (an authentication layer on top of OAuth). Understanding the roles and flows is essential.

The actors: the **resource owner** (the user), the **client** (your app), the **authorization server** (the IdP that issues tokens), and the **resource server** (your API that accepts tokens).

### Authorization Code Flow with PKCE

This is the correct flow for interactive apps — server-rendered web apps, SPAs, and mobile apps. The client never sees the user's password; the IdP handles login and returns an authorization *code*, which the client exchanges for tokens.

**PKCE** (Proof Key for Code Exchange, pronounced "pixy") hardens this flow against code-interception attacks. The client generates a random secret (the *code verifier*), hashes it (the *code challenge*), and sends the challenge when starting the flow. When exchanging the code for tokens, it presents the original verifier. An attacker who steals the authorization code cannot use it without the verifier.

The sequence:
1. Client generates `code_verifier` (random), computes `code_challenge = BASE64URL(SHA256(code_verifier))`.
2. Client redirects the user to the authorization server with the challenge.
3. User authenticates and consents; the server redirects back with a one-time `code`.
4. Client POSTs the `code` plus the `code_verifier` to the token endpoint.
5. Server verifies the hash matches and returns an ID token, access token, and (optionally) refresh token.

> **Best practice:** Always use Authorization Code + PKCE for user-facing apps. The older **Implicit flow** (tokens returned directly in the URL fragment) is deprecated and insecure. PKCE is now recommended even for confidential clients, not just public ones.

### Client Credentials Flow

This is machine-to-machine: a service authenticating *as itself*, with no user involved (a nightly batch job calling an API). The client sends its own ID and secret directly to the token endpoint and receives an access token.

```
POST /connect/token
grant_type=client_credentials
&client_id=report-service
&client_secret=<secret>
&scope=reports:read
```

There is no user, no ID token, and no refresh token — when the access token expires, the service simply requests another. Store that client secret in a secrets manager, never in code.

### OpenID Connect

OAuth 2.0 was designed for authorization (delegated access), not authentication — using an access token to prove identity is subtly wrong. **OpenID Connect (OIDC)** fixes this by adding an **ID token** (always a JWT) that asserts *who* the user is, plus a standardized `/userinfo` endpoint and discovery document (`/.well-known/openid-configuration`). When you "Sign in with Google," you are using OIDC.

The distinction: the **access token** is for calling APIs (authorization); the **ID token** is for your client to learn who logged in (authentication). Do not send ID tokens to APIs, and do not use access tokens to establish user identity in your client.

### What a JWT Is

A **JSON Web Token** is a compact, URL-safe, digitally signed token in three Base64URL-encoded parts separated by dots: `header.payload.signature`.

- **Header** — the signing algorithm and token type, e.g. `{"alg":"RS256","typ":"JWT"}`.
- **Payload** — the *claims*: standard ones like `iss` (issuer), `aud` (audience), `exp` (expiry), `sub` (subject), plus custom claims like roles or scopes.
- **Signature** — the header and payload signed with the issuer's key, so any tampering is detectable.

> **Pitfall:** A JWT is *signed*, not *encrypted*. Anyone can Base64-decode the payload and read every claim. Never put secrets — passwords, credit-card numbers, API keys — in a JWT payload. Signing guarantees integrity, not confidentiality.

### Validating JWTs Correctly

This is where developers most often introduce vulnerabilities. Validating a JWT is *not* just "does it parse." You must verify:

1. **Signature** — using the issuer's public key (for RS256) or shared secret (HS256), proving the token was issued by whom it claims and not altered.
2. **Issuer (`iss`)** — it came from the authorization server you trust.
3. **Audience (`aud`)** — this token was minted *for your API*, not for some other service.
4. **Expiry (`exp`)** and **not-before (`nbf`)** — the token is currently valid in time.

In ASP.NET Core, the JWT bearer middleware does all of this when configured correctly. The key point: **turn every validation on explicitly and never disable signature validation.**

```csharp
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // Authority = the trusted issuer; middleware fetches its signing
        // keys from /.well-known/openid-configuration automatically.
        options.Authority = "https://login.example.com";
        options.Audience  = "orders-api";

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer           = true,
            ValidIssuer              = "https://login.example.com",
            ValidateAudience         = true,
            ValidAudience            = "orders-api",
            ValidateLifetime         = true,   // enforce exp / nbf
            ValidateIssuerSigningKey = true,   // enforce the signature
            ClockSkew                = TimeSpan.FromSeconds(30), // tolerate small clock drift
        };
    });
```

> **Pitfall — the `alg: none` and algorithm-confusion attacks:** Historically, libraries that trusted the token's own `alg` header could be tricked into accepting an unsigned token (`alg: none`) or into verifying an RS256 token using the public key as an HMAC secret. Modern `Microsoft.IdentityModel` libraries reject `none` and require you to specify valid algorithms. Never write validation that reads the algorithm from the untrusted header and trusts it.

> **Best practice:** Keep `ClockSkew` small (seconds, not the 5-minute default) and keep access-token lifetimes short (minutes). Use refresh tokens for longevity. A stolen short-lived token expires before it's very useful.

## Identity Providers

You almost never want to build authentication from scratch. Choose an identity provider (IdP) and let it handle the hard, high-stakes parts. The main options in the .NET world:

- **ASP.NET Core Identity** — a library, not a server. It manages users, password hashing, roles, lockout, and MFA *inside your own application and database*. Ideal when you own the users and don't need to be an OAuth server for other apps. It's a membership system, not a full token-issuing IdP (though it pairs with one).
- **Duende IdentityServer** — a mature, standards-compliant OpenID Connect and OAuth 2.0 framework you host yourself in .NET. The successor to the open-source IdentityServer4; it is commercially licensed (free for small companies and non-production). Choose it when you need to *be* the authorization server — issuing tokens to multiple clients and APIs you control.
- **Microsoft Entra ID** (formerly Azure Active Directory) — Microsoft's cloud IdP, the default for enterprise and Microsoft 365 organizations. Deep integration with `Microsoft.Identity.Web`.
- **Auth0** — a developer-friendly, hosted IdP (now part of Okta). Fast to integrate, generous features, subscription-priced.
- **Keycloak** — a powerful open-source, self-hosted IdP (Java-based) supporting OIDC and SAML. Popular when you want full control without licensing costs and don't mind operating it.

The decision axis: **buy vs. host, and standalone app vs. multi-app SSO.** If you just need login for one app and own the users, ASP.NET Core Identity is the least machinery. If you need single sign-on across many apps or federated enterprise login, use a real IdP (Entra ID, Auth0, Keycloak, or Duende).

### Password Hashing in ASP.NET Core Identity

Identity's `PasswordHasher<T>` uses PBKDF2 with a per-user salt and many iterations by default — a sensible baseline. If you build your own login (generally discouraged), you must replicate this.

### Passkeys (WebAuthn / FIDO2)

Passkeys are public-key credentials standardized by WebAuthn/FIDO2, and they remove the weakest link in password authentication: the shared secret. The browser or OS holds a private key; the server stores only the corresponding public key, so a database breach yields nothing reusable — there is no password to crack, and nothing to stuff into other sites. Authentication is a signed challenge, and the signature is bound to the site's *origin*, which is what makes passkeys phishing-resistant: a credential registered for `example.com` simply will not sign a challenge from a look-alike domain, no matter how convincing the page. ASP.NET Core Identity gained first-class passkey support in .NET 10, so this is now a framework feature rather than a third-party integration.

> **Best practice:** For new systems, treat passkeys as the *primary* factor and passwords as the fallback, not the other way around. Every login that happens via passkey is one that cannot be phished, stuffed, or brute-forced.

## Secrets Management

A secret is any value that grants access: connection strings, API keys, client secrets, signing keys, encryption keys. The cardinal rule: **secrets never live in source code or in `appsettings.json` committed to git.** Once a secret is in git history, treat it as compromised and rotate it — deleting the line does not remove it from history.

**In development**, use the .NET **Secret Manager** (`user-secrets`), which stores values in a JSON file *outside* your project tree, keyed by a `UserSecretsId`:

```bash
dotnet user-secrets init
dotnet user-secrets set "ConnectionStrings:Db" "Server=...;Password=..."
```

These are picked up automatically by the configuration system in Development, so `builder.Configuration["ConnectionStrings:Db"]` just works — with nothing to accidentally commit.

**In production**, use a managed secret store: **Azure Key Vault**, **AWS Secrets Manager**, **HashiCorp Vault**, or Kubernetes secrets. These provide access control, audit logging, and rotation. The application authenticates to the vault using a *managed identity* (no secret needed to fetch secrets — the platform vouches for the workload):

```csharp
// Azure Key Vault via managed identity — no secret in code at all
builder.Configuration.AddAzureKeyVault(
    new Uri("https://myapp-kv.vault.azure.net/"),
    new DefaultAzureCredential());
```

> **Best practice — rotation.** Secrets should be rotated regularly and immediately upon suspected compromise. Design for rotation from day one: fetch secrets at runtime (or cache briefly) rather than baking them into a build, and support two valid keys during a rollover window so nothing breaks mid-rotation.

## HTTPS, TLS, HSTS, and Certificates

**TLS** (Transport Layer Security, the protocol behind HTTPS) provides three guarantees for data in transit: *confidentiality* (eavesdroppers see ciphertext), *integrity* (tampering is detected), and *authentication* (the certificate proves you're talking to the real server). It is non-negotiable for any application handling credentials or personal data.

A **certificate** binds a public key to a domain name and is signed by a Certificate Authority (CA) the client trusts. TLS uses asymmetric crypto for the handshake (to authenticate the server and agree on keys) then switches to fast symmetric encryption for the session.

In ASP.NET Core, redirect HTTP to HTTPS and enable **HSTS**:

```csharp
app.UseHttpsRedirection();
app.UseHsts(); // production only
```

**HSTS** (HTTP Strict Transport Security) sends a response header telling the browser: "for the next *N* seconds, only ever contact this domain over HTTPS, and refuse to proceed if the certificate is invalid." This defeats SSL-stripping attacks where an attacker downgrades the first request to HTTP.

> **Pitfall:** HSTS is sticky and cached by the browser. Don't enable it (especially with `includeSubDomains` and `preload`) until you're certain *every* subdomain can serve valid HTTPS — otherwise you can lock users out of an HTTP-only subdomain. This is why the default template excludes HSTS in Development.

Use modern TLS (1.2 minimum, prefer 1.3), automate certificate issuance and renewal (Let's Encrypt / ACME, or your cloud's managed certificates), and never disable certificate validation in HTTP clients to "make it work":

> **Pitfall:** Setting `ServerCertificateCustomValidationCallback` to always return `true` disables TLS authentication entirely, silently exposing you to man-in-the-middle attacks. If you see this in a code review, block the PR.

## Cryptography for Developers

You will rarely implement a cipher, but you must choose and use cryptographic primitives correctly. Two foundational distinctions:

**Hashing vs. encryption.** *Hashing* is a one-way function — you cannot recover the input from the hash. Use it to *verify* something (passwords, integrity) without storing the original. *Encryption* is reversible with a key — use it to protect data you need to read back later.

**Symmetric vs. asymmetric encryption.** *Symmetric* (AES) uses one shared key for both encrypt and decrypt — fast, ideal for bulk data. *Asymmetric* (RSA, ECDSA) uses a public/private key pair — the public key encrypts (or verifies signatures) and the private key decrypts (or signs). Asymmetric is slow, so in practice systems use it to exchange a symmetric key, then encrypt the bulk data symmetrically (exactly what TLS does).

### Password Hashing

Passwords require a *special* kind of hashing. General-purpose hashes (SHA-256) are designed to be *fast*, which is exactly wrong for passwords — it lets an attacker who steals your database try billions of guesses per second on a GPU.

> **Pitfall:** Never store passwords with MD5, SHA-1, or a plain SHA-256. MD5 and SHA-1 are broken; plain fast hashes are trivially brute-forced even when "salted." This is a resume-generating incident waiting to happen.

Use a **slow, adaptive, salted** password-hashing algorithm designed for the purpose: **Argon2** (the modern winner), **bcrypt**, or **PBKDF2** (what ASP.NET Core Identity uses, and the only one in the BCL). Two properties matter:

- **Salt** — a unique random value per password, stored alongside the hash. It ensures two users with the same password get different hashes and defeats precomputed *rainbow tables*.
- **Work factor** — a tunable cost (iterations / memory) you raise as hardware gets faster, keeping each guess expensive.

Here is correct PBKDF2 usage with the BCL, generating a per-password salt and a high iteration count:

```csharp
using System.Security.Cryptography;

public static class Passwords
{
    private const int SaltSize = 16;       // 128-bit salt
    private const int KeySize  = 32;       // 256-bit derived key
    private const int Iterations = 600_000; // tune upward over time
    private static readonly HashAlgorithmName Algo = HashAlgorithmName.SHA256;

    public static string Hash(string password)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
        byte[] hash = Rfc2898DeriveBytes.Pbkdf2(
            password, salt, Iterations, Algo, KeySize);
        // store algorithm params with the hash so you can rehash later
        return $"{Iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
    }

    public static bool Verify(string password, string stored)
    {
        var parts = stored.Split('.', 3);
        int iterations = int.Parse(parts[0]);
        byte[] salt = Convert.FromBase64String(parts[1]);
        byte[] expected = Convert.FromBase64String(parts[2]);

        byte[] actual = Rfc2898DeriveBytes.Pbkdf2(
            password, salt, iterations, Algo, expected.Length);

        // constant-time comparison defeats timing attacks
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
```

Note the two subtleties a senior developer catches: storing the parameters *with* the hash (so you can raise the iteration count later and re-hash on next login), and using `FixedTimeEquals` rather than `==` to avoid leaking information through comparison timing. In practice, prefer `PasswordHasher<T>` from ASP.NET Core Identity, or a vetted library like `BCrypt.Net`, over hand-rolling even this.

### Encryption at Rest and in Transit

*In transit* is TLS, covered above. *At rest* means encrypting stored data — database Transparent Data Encryption, encrypted disks, or field-level encryption for especially sensitive columns. The hard part of encryption at rest is **key management**: the encryption key must live somewhere safer than the data it protects, which is what Key Vault / KMS and hardware security modules are for.

### ASP.NET Core Data Protection (`IDataProtector`)

For the common in-app need — "encrypt this small piece of data so only my app can read it back" (a token in a cookie, a password-reset link, a temporary identifier) — ASP.NET Core provides the **Data Protection** API. It handles key generation, storage, rotation, and algorithm selection for you, so you never touch raw AES.

```csharp
public class TokenService
{
    private readonly IDataProtector _protector;

    public TokenService(IDataProtectionProvider provider)
    {
        // The "purpose string" isolates this protector — data protected
        // for one purpose cannot be unprotected under another.
        _protector = provider.CreateProtector("ResetTokens.v1");
    }

    public string Protect(string value)   => _protector.Protect(value);
    public string Unprotect(string token) => _protector.Unprotect(token);
}
```

`Protect` returns an authenticated, encrypted string; `Unprotect` reverses it and throws if the data was tampered with or was protected for a different purpose. Use `ITimeLimitedDataProtector` when you want the token to expire automatically.

> **Pitfall:** By default, data protection keys are stored on the local filesystem. In a load-balanced or containerized deployment, each instance generates its *own* keys, so a cookie encrypted by one server can't be decrypted by another — users get random logouts and errors. Configure a *shared* key ring (Azure Blob Storage, Redis, a shared volume) and protect it at rest. Do this before you scale out.

### Crypto Agility and the Post-Quantum Migration

Everything above assumes the algorithms hold. For most of your career they have, which has let us bake algorithm choices into code, config files, database columns, and certificate chains without thinking twice. That assumption now has an expiry date, and the interesting engineering problem is less "which algorithm" than "how quickly could we change ours?"

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

## Web-Facing Defenses

Beyond the fundamentals, the browser threat model demands specific defenses.

### Input Validation and Output Encoding

These are the two halves of handling untrusted data, and they operate at different boundaries:

- **Input validation** happens when data *enters* — check type, length, format, and range against an allow-list ("accept only what matches this pattern") rather than a deny-list ("block these bad characters"). Deny-lists are always incomplete.
- **Output encoding** happens when data *leaves* into another context — HTML, a URL, JavaScript, SQL. You encode the data for *that specific context* so it's treated as data, not code.

```csharp
public record CreateUser
{
    [Required, EmailAddress, MaxLength(256)]
    public string Email { get; init; } = "";

    [Required, StringLength(100, MinimumLength = 12)]
    public string Password { get; init; } = "";

    [Range(0, 120)]
    public int Age { get; init; }
}
```

With `[ApiController]`, model validation runs automatically and returns a `400` with details before your action executes.

### Cross-Site Scripting (XSS)

XSS is injection into the *browser*: an attacker gets their JavaScript to run in another user's session, stealing cookies or acting as them. The defense is context-aware output encoding. **Razor encodes HTML output by default** — `@Model.UserComment` is safe. The danger is deliberately bypassing it.

> **Pitfall:** `@Html.Raw(userInput)` and building HTML by string concatenation disable encoding and reopen XSS. Only use `Html.Raw` on content you fully control or have sanitized with a library like `HtmlSanitizer`.

For APIs feeding SPAs, the encoding responsibility shifts to the front-end framework (React/Angular escape by default) — but the same rule holds: never `dangerouslySetInnerHTML` untrusted data. A strong **Content-Security-Policy** header (below) is the crucial second layer that limits damage even if an XSS slips through.

### Anti-Forgery / CSRF

**Cross-Site Request Forgery** tricks a logged-in user's browser into making an unwanted state-changing request to your site, riding on their existing cookie. The classic defense is the **anti-forgery token** (synchronizer token pattern): the server embeds a secret token in the form that a cross-origin attacker cannot read or reproduce.

In ASP.NET Core MVC/Razor Pages this is largely automatic — the tag helpers inject the token and `[AutoValidateAntiforgeryToken]` validates it on unsafe verbs:

```csharp
builder.Services.AddControllersWithViews(options =>
    options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute()));
```

> **Best practice:** CSRF specifically targets *cookie-based* auth. Token-based APIs where the client sends `Authorization: Bearer ...` from JavaScript are not vulnerable in the same way, because the browser doesn't attach that header automatically cross-site. Additionally set cookies to `SameSite=Lax` (or `Strict`) as defense in depth.

### CORS Done Right

The browser's **Same-Origin Policy** blocks JavaScript on one origin from reading responses from another. **CORS** (Cross-Origin Resource Sharing) is how a server *opts in* to allowing specific other origins. It is a relaxation of security, so configure it as tightly as possible.

```csharp
builder.Services.AddCors(options =>
    options.AddPolicy("spa", policy => policy
        .WithOrigins("https://app.example.com") // explicit, never "*"
        .WithMethods("GET", "POST")
        .WithHeaders("Authorization", "Content-Type")
        .AllowCredentials()));
```

> **Pitfall:** `AllowAnyOrigin()` combined with `AllowCredentials()` is invalid and dangerous — the spec forbids it precisely because it would let *any* site make credentialed requests to your API. Never reflect the `Origin` header back blindly, and never wildcard origins on an authenticated API.

CORS is enforced by the *browser*, not the server — it is not an authorization mechanism. It stops a malicious site's JavaScript from reading your API in a victim's browser; it does nothing against `curl` or a server-side attacker.

### Security Headers

A handful of response headers harden the browser's behavior. The most important:

- **`Content-Security-Policy` (CSP)** — the strongest anti-XSS control. It declares which sources of scripts, styles, and other resources the browser may load, so injected inline scripts simply don't run. Building a strict CSP (ideally nonce-based) takes effort but pays off enormously.
- **`X-Content-Type-Options: nosniff`** — stops the browser from MIME-sniffing a response into a different content type (e.g., interpreting an uploaded "image" as JavaScript).
- **`Strict-Transport-Security`** — HSTS, discussed above.
- **`X-Frame-Options: DENY`** (or CSP `frame-ancestors`) — prevents clickjacking by disallowing your site from being framed.
- **`Referrer-Policy`** — limits how much URL information leaks to other sites.

```csharp
app.Use(async (context, next) =>
{
    var h = context.Response.Headers;
    h["X-Content-Type-Options"] = "nosniff";
    h["X-Frame-Options"] = "DENY";
    h["Referrer-Policy"] = "strict-origin-when-cross-origin";
    h["Content-Security-Policy"] =
        "default-src 'self'; script-src 'self'; object-src 'none'; frame-ancestors 'none'";
    await next();
});
```

## Dependency Scanning

Your code is a small fraction of what you ship; the rest is dependencies. Managing their vulnerabilities is a first-class security task, not an afterthought.

The .NET SDK has this built in. `dotnet list package --vulnerable` queries the GitHub Advisory Database for known CVEs in your direct dependencies; add `--include-transitive` to catch the (often more numerous) indirect ones:

```bash
dotnet list package --vulnerable --include-transitive
```

Wire this into CI so a build *fails* when a vulnerable package appears, rather than relying on someone to run it manually. Complement it with:

- **Dependabot** (built into GitHub) — automatically opens pull requests to bump vulnerable or outdated dependencies, and alerts on new advisories affecting your repo.
- **Snyk**, **GitHub Advanced Security**, or **OWASP Dependency-Check** — deeper SCA (Software Composition Analysis) tooling that scans dependencies (and sometimes container images and IaC) across ecosystems.

> **Best practice:** Also enable NuGet package **source mapping** and consider **signed packages** to defend against dependency-confusion and typosquatting attacks, where an attacker publishes a malicious package with a name similar to (or matching an internal) package you depend on.

Scanning tells you about *known* vulnerabilities in packages you already trust. It says nothing about a package that was deliberately backdoored last night, about your build system being modified after the source was clean, or about proving to a customer what went into the binary you shipped them. That wider problem — the packages you consume, the build that assembles them, and the artifacts you publish — is the subject of [Chapter 35: Software Supply Chain Security](#chapter-35-software-supply-chain-security).

> **Capstone tie-in:** This chapter is exercised by ShopCore Step 5 (Caching, Auth, and Observability) — you'd add JWT authentication and role-based authorization so only authenticated users check out and only admins mutate the catalog. See Chapter 32.

## Summary

Security is a discipline of layered, deliberate decisions. Adopt the mindset — defense in depth, least privilege, secure by default, never trust input — and it informs every line you write. Know the OWASP Top 10 as *categories* of failure and the .NET mitigation for each. Distinguish authentication (who you are) from authorization (what you may do), and implement both with the framework's tools rather than reinventing them. Delegate identity to OAuth 2.0 / OIDC with the Authorization Code + PKCE flow, validate JWTs on issuer, audience, expiry, and signature — every time. Keep secrets out of source and in a managed vault, enforce TLS with HSTS, hash passwords with a slow salted algorithm, reach for `IDataProtector` instead of raw crypto, keep your algorithm choices agile — you will have to change them, and the certificate-lifetime clock is already running — and defend the browser boundary with validation, encoding, anti-forgery tokens, tight CORS, and a strong CSP. Finally, scan your dependencies continuously — because the vulnerability you didn't write is still yours to fix.
