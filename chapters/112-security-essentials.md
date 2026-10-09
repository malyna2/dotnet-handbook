# Chapter 12: Security Essentials

Security is not a feature you bolt on at the end of a sprint. It is a property of a system that emerges from thousands of small decisions: how you parse input, where you store a connection string, which overload of a crypto API you call, and whether you trusted a value that came from the network. This chapter makes you able to write and review an endpoint so that it checks who the caller is and what this caller may do to *this* record, treats every input as data, keeps secrets out of the repository, and doesn't carry one of the handful of configuration lines that quietly switch a defence off.

One rule runs under every section: **the server trusts only what it verifies itself, on every request.** The caller's identity comes from a credential it validates, access is decided per resource, and input is only ever data, never code. Each trap in the chapter is a place where something unverified gets trusted.

The sections build from the mindset to the machinery. First the four decision rules, then authentication and authorization as ASP.NET Core wires them, then the OWASP Top 10 as the map of what goes wrong, each with its .NET mitigation. Then the parts you will configure yourself: OAuth 2.0, OpenID Connect and JWT validation, identity providers, secrets, the cryptography you use (hashing, encryption, Data Protection), TLS from the handshake to HSTS, the browser-facing defences, and dependency scanning. Workload identity, zero trust, the depth of cryptography and the software supply chain continue in [Chapter 27: Security in Depth and the Supply Chain](#chapter-27-security-in-depth-and-the-supply-chain).

## The Security Mindset

Four principles, used as decision procedures when the "how" is unclear:

**Defense in depth.** Assume every single control will eventually fail, and layer independent controls so that one failure is not a breach. A parameterized query stops SQL injection — but you still validate input, run the database account with least privilege, and log anomalies.

**Least privilege.** Every component — a user, a service account, a process, a token — gets exactly the permissions it needs to do its job and nothing more. The web app's database login should not be `db_owner`. The background worker that reads a queue should not have write access to the whole storage account. A JWT scoped to `orders:read` should not be able to delete anything. Least privilege bounds the blast radius of a compromise.

**Secure by default.** The default configuration must be the safe configuration. A new controller action should require authorization unless you deliberately open it. HTTPS should be mandatory out of the box. If a developer forgets to configure something, the system should fail closed (deny) rather than fail open (allow). ASP.NET Core largely embraces this — for example, the framework's HTTPS redirection and HSTS templates ship enabled — but you are responsible for keeping it that way.

**Never trust input.** Every byte that crosses a trust boundary — HTTP request bodies, query strings, headers, cookies, file uploads, messages from a queue, responses from a third-party API, even data read back from your own database — is potentially hostile. Trust is earned by validation, not granted by origin.

> **Best practice:** Treat "the client already validated this" as a comment, never a guarantee. Client-side validation is a UX nicety. Server-side validation is the security control. An attacker uses `curl`, not your form.

## Authentication vs. Authorization

The mindset becomes concrete at the first question every request raises: who is calling, and may they do this?

- **Authentication (AuthN)** answers *"Who are you?"* — it establishes and verifies identity. Logging in with a password, presenting a certificate, or validating a JWT are authentication. In ASP.NET Core it produces a `ClaimsPrincipal`.
- **Authorization (AuthZ)** answers *"What are you allowed to do?"* — it decides whether an already-identified principal may perform an action. Checking a role, a scope, or resource ownership is authorization.

A `ClaimsPrincipal` carries one or more `ClaimsIdentity` objects, each a bag of **claims** — simple key/value statements like `sub=42`, `role=admin`, `email=x@y.com`. Claims are the currency of authorization; you make decisions based on what claims a user carries, not by re-querying a database on every request.

Authentication always comes first; you cannot authorize an unknown principal. In ASP.NET Core the two are distinct middleware, and *order matters*:

```csharp
app.UseAuthentication(); // figures out WHO — populates HttpContext.User
app.UseAuthorization();  // figures out WHAT — enforces [Authorize] policies
```

A `401 Unauthorized` means "I don't know who you are" (authentication failed). A `403 Forbidden` means "I know who you are, but you can't do this" (authorization failed). Despite its name, `401` is about authentication.

**Which authentication scheme.** For APIs, the dominant scheme is **JWT bearer tokens**: the client sends `Authorization: Bearer <token>`, and the server validates a signed set of claims without a lookup (configured in *Validating JWTs Correctly* below). For server-rendered apps, **cookie authentication** stores an encrypted session identifier in a cookie. For delegated identity — "log in with Google/Microsoft/your corporate IdP" — you configure `.AddOpenIdConnect(...)` and let the middleware handle the redirect dance of the flows described under *OAuth 2.0, OpenID Connect, and JWTs*. The design rule: your API should *trust tokens from a known issuer*, not manage passwords itself.

### Policy-based and role-based authorization

**Role-based** is the classic coarse check: `[Authorize(Roles = "Admin")]`. It works but roles are blunt instruments. **Policy-based** authorization is the flexible, recommended approach — you name a policy and define what satisfies it:

```csharp
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdultsOnly", policy =>
        policy.RequireAssertion(ctx =>
            ctx.User.HasClaim(c => c.Type == "age") &&
            int.Parse(ctx.User.FindFirstValue("age")!) >= 18));

    options.AddPolicy("CanDeleteProducts", policy =>
        policy.RequireClaim("permission", "products:delete"));
});

// Applied declaratively:
products.MapDelete("/{id:int}", ...).RequireAuthorization("CanDeleteProducts");
```

For complex rules, implement `IAuthorizationRequirement` plus an `AuthorizationHandler<T>` — this lets you inject services and evaluate against resources (e.g. "can edit *this specific* document because you own it"). That resource-based check is done imperatively via `IAuthorizationService.AuthorizeAsync(user, resource, policy)`, after the resource is loaded. Why it has to be imperative is the first entry in the OWASP list below.

> **Best practice:** Make authenticated access the default: set `options.FallbackPolicy` to a policy that requires an authenticated user, so every endpoint without its own policy demands a login and opening one takes an explicit `[AllowAnonymous]`. That is *secure by default* in one line.

## The OWASP Top 10, with .NET Mitigations

The OWASP Top 10 is the industry's consensus list of the most critical web application risks. Below is each category with the mitigation you apply in .NET. Learn the *category*, not just the trick — the categories are stable even as frameworks change.

> **Note:** This walk-through follows the **2021 edition** (A01–A10 below). OWASP published a revised Top 10 in 2025 — notably elevating software supply chain failures to its own category, which [Chapter 27](#chapter-27-security-in-depth-and-the-supply-chain) covers in full — but the list is deliberately stable between editions, and every .NET mitigation here carries over unchanged.

### A01: Broken Access Control

The most common serious flaw, and still A01 in the 2025 edition (which also folds SSRF into it): a user can act on data or functions they should not reach. The classic form is **Insecure Direct Object Reference (IDOR)** — `GET /api/invoices/1005` returns invoice 1005 even though it belongs to another tenant, simply because the code fetched by ID without checking ownership.

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

> **Pay attention.** **Why `[Authorize]` can't stop an IDOR.** Attributes and endpoint policies run before the action, against the principal and the route, and the record hasn't been loaded yet. They can answer "may this caller use this endpoint?", never "does invoice 1005 belong to this caller?", because ownership is a column of the row. So the check has to sit where the row is: in the query itself (`Where(i => i.Id == id && i.OwnerId == userId)`, or an EF Core global query filter on the tenant), or in a resource-based check after loading. A query that filters by owner can't forget the check on the next endpoint that loads the same entity.

### A02: Cryptographic Failures (formerly "Sensitive Data Exposure")

Sensitive data is stored or transmitted without adequate protection: passwords hashed with MD5, PII sent over HTTP, secrets in source control, weak or home-grown crypto. The mitigations are covered in depth in the Cryptography section below, but the headline rules are: enforce TLS everywhere, hash passwords with a slow adaptive algorithm, encrypt sensitive data at rest, and never invent your own cryptography.

### A03: Injection

(A05 in the 2025 edition.) Untrusted input is interpreted as code or commands — SQL, OS commands, LDAP, NoSQL queries. **SQL injection** remains the canonical example. The fix is to keep data and code strictly separated using parameterized queries, never string concatenation.

```csharp
// VULNERABLE — never do this
var sql = $"SELECT * FROM Users WHERE Email = '{email}'";
// Input:  ' OR '1'='1' --   returns every row.

// SAFE — parameterized (ADO.NET)
using var cmd = new SqlCommand(
    "SELECT * FROM Users WHERE Email = @email", connection);
cmd.Parameters.Add("@email", SqlDbType.NVarChar, 256).Value = email;
```

Entity Framework Core parameterizes automatically for LINQ, and `FromSql` (EF Core 7+; `FromSqlInterpolated` before that) safely parameterizes interpolated strings — but `FromSqlRaw` with a manually built string reintroduces the hole. Dapper is the same: `conn.QueryAsync<User>("... WHERE Email = @email", new { email })` sends a parameter; a concatenated or interpolated SQL string does not.

```csharp
// SAFE — EF Core turns the interpolation into parameters
var users = await _db.Users
    .FromSql($"SELECT * FROM Users WHERE Email = {email}")
    .ToListAsync();
```

> **Pay attention.** **Same syntax, opposite effect.** `FromSql` takes a `FormattableString`, so EF Core receives the format and the values separately and turns each hole into a `DbParameter`; the database never parses the value as SQL. `FromSqlRaw` takes a `string`, so the compiler formats the interpolation *before* EF Core sees it, and the input becomes part of the SQL text. One refactor between the two compiles cleanly and reopens the injection. Identifiers (a sort column, a table name) can't be parameters at all: map the user's choice to a fixed name from an allow-list.

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

You inherit every vulnerability in every NuGet package and transitive dependency. A CVE in a JSON parser or logging library is your CVE. The mitigation is active dependency management and scanning — covered in *Dependency Scanning* below.

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
            ValidateIssuerSigningKey = true,   // validate the signing KEY too (see below)
            ClockSkew                = TimeSpan.FromSeconds(30), // tolerate small clock drift
        };
    });
```

> **Pitfall — the `alg: none` and algorithm-confusion attacks:** Historically, libraries that trusted the token's own `alg` header could be tricked into accepting an unsigned token (`alg: none`) or into verifying an RS256 token using the public key as an HMAC secret. `Microsoft.IdentityModel` rejects unsigned tokens by default (`RequireSignedTokens` is `true`), but `ValidAlgorithms` is `null` by default, which accepts any algorithm the key supports. Pin it (`ValidAlgorithms = [SecurityAlgorithms.RsaSha256]`), and never write validation that reads the algorithm from the untrusted header and trusts it.

> **Pay attention.** **Which setting actually checks the signature.** `ValidateIssuer`, `ValidateAudience` and `ValidateLifetime` already default to `true`, and the signature is verified whenever `RequireSignedTokens` is `true` (the default). `ValidateIssuerSigningKey` is something else, and defaults to `false`: it validates the *key* that signed the token (for example, a certificate carried in the token), not the signature. The real holes are the lines added to make a `401` go away: `ValidateAudience = false` (now any token from that issuer, minted for any API, works on yours), `RequireSignedTokens = false`, or a custom `SignatureValidator` that returns the token unchecked. Read the `IDX` error in the log and fix the configuration instead.

> **Best practice:** Keep `ClockSkew` small (seconds, not the 5-minute default) and keep access-token lifetimes short (minutes). Use refresh tokens for longevity. A stolen short-lived token expires before it's very useful.

## Identity Providers

You almost never want to build authentication from scratch. Choose an identity provider (IdP) and let it handle the hard, high-stakes parts. The main options in the .NET world:

- **ASP.NET Core Identity** — a library, not a server. It manages users, password hashing, roles, lockout, and MFA *inside your own application and database*. Ideal when you own the users and don't need to be an OAuth server for other apps. It's a membership system, not a full token-issuing IdP (though it pairs with one).
- **Duende IdentityServer** — a mature, standards-compliant OpenID Connect and OAuth 2.0 framework you host yourself in .NET. The successor to the open-source IdentityServer4; it is commercially licensed (free for small companies and non-production). Choose it when you need to *be* the authorization server — issuing tokens to multiple clients and APIs you control.
- **Microsoft Entra ID** (formerly Azure Active Directory) — Microsoft's cloud IdP, the default for enterprise and Microsoft 365 organizations. Deep integration with `Microsoft.Identity.Web`.
- **Auth0** — a developer-friendly, hosted IdP (now part of Okta). Fast to integrate, generous features, subscription-priced.
- **Keycloak** — a powerful open-source, self-hosted IdP (Java-based) supporting OIDC and SAML. Popular when you want full control without licensing costs and don't mind operating it.

The decision axis: **buy vs. host, and standalone app vs. multi-app SSO.** If you just need login for one app and own the users, ASP.NET Core Identity is the least machinery. If you need single sign-on across many apps or federated enterprise login, use a real IdP (Entra ID, Auth0, Keycloak, or Duende).

### Passkeys (WebAuthn / FIDO2)

Passkeys are public-key credentials standardized by WebAuthn/FIDO2, and they remove the weakest link in password authentication: the shared secret. The browser or OS holds a private key; the server stores only the corresponding public key, so a database breach yields nothing reusable — there is no password to crack, and nothing to stuff into other sites. Authentication is a signed challenge, and the signature is bound to the site's *origin*, which is what makes passkeys phishing-resistant: a credential registered for `example.com` simply will not sign a challenge from a look-alike domain, no matter how convincing the page. ASP.NET Core Identity gained first-class passkey support in .NET 10, so this is now a framework feature rather than a third-party integration.

> **Best practice:** For new systems, treat passkeys as the *primary* factor and passwords as the fallback, not the other way around. Every login that happens via passkey is one that cannot be phished, stuffed, or brute-forced.

## Secrets Management

A secret is any value that grants access: connection strings, API keys, client secrets, signing keys, encryption keys. The cardinal rule: **secrets never live in source code or in `appsettings.json` committed to git.** Once a secret is in git history, treat it as compromised and rotate it — deleting the line does not remove it from history.

**In development**, use the .NET **Secret Manager** (`user-secrets`), which stores values in a JSON file in your user profile (`~/.microsoft/usersecrets/<id>/secrets.json`, or under `%APPDATA%\Microsoft\UserSecrets` on Windows), *outside* the project tree, keyed by a `UserSecretsId`. It keeps secrets out of git; it doesn't encrypt them, so it is for development only:

```bash
dotnet user-secrets init
dotnet user-secrets set "ConnectionStrings:Db" "Server=...;Password=..."
```

These are picked up automatically by the configuration system in Development, so `builder.Configuration["ConnectionStrings:Db"]` just works — with nothing to accidentally commit.

**In production**, use a managed secret store: **Azure Key Vault**, **AWS Secrets Manager**, **HashiCorp Vault**, or Kubernetes secrets. These provide access control, audit logging, and rotation. The application authenticates to the vault using a *managed identity* (no secret needed to fetch secrets — the platform vouches for the workload). `DefaultAzureCredential` is the development convenience; production should name its credential, as [Chapter 29](#defaultazurecredential-what-the-chain-really-is) explains:

```csharp
// Azure Key Vault via managed identity — no secret in code at all
builder.Configuration.AddAzureKeyVault(
    new Uri("https://myapp-kv.vault.azure.net/"),
    new DefaultAzureCredential());
```

> **Best practice — rotation.** Secrets should be rotated regularly and immediately upon suspected compromise. Design for rotation from day one: fetch secrets at runtime (or cache briefly) rather than baking them into a build, and support two valid keys during a rollover window so nothing breaks mid-rotation.

## Cryptography for Developers

You will rarely implement a cipher, but you must choose and use cryptographic primitives correctly. Two foundational distinctions:

**Hashing vs. encryption.** *Hashing* is a one-way function — you cannot recover the input from the hash. Use it to *verify* something (passwords, integrity) without storing the original. *Encryption* is reversible with a key — use it to protect data you need to read back later.

**Symmetric vs. asymmetric encryption.** *Symmetric* (AES) uses one shared key for both encrypt and decrypt — fast, ideal for bulk data. *Asymmetric* (RSA, ECDSA) uses a public/private key pair — the public key encrypts (or verifies signatures) and the private key decrypts (or signs). Asymmetric is slow, so in practice systems use it to exchange a symmetric key, then encrypt the bulk data symmetrically (exactly what TLS does).

### Password Hashing

General-purpose hashes (SHA-256) are designed to be *fast*, which is exactly wrong for passwords: an attacker who steals your database can try billions of guesses per second on a GPU.

> **Pitfall:** Never store passwords with MD5, SHA-1, or a plain SHA-256. MD5 and SHA-1 are broken; plain fast hashes are trivially brute-forced even when "salted."

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

Two subtleties: storing the parameters *with* the hash (so you can raise the iteration count later and re-hash on next login), and using `FixedTimeEquals` rather than `==` to avoid leaking information through comparison timing. In practice, prefer `PasswordHasher<T>` from ASP.NET Core Identity, or a vetted library like `BCrypt.Net`, over hand-rolling even this.

Identity's `PasswordHasher<T>` uses PBKDF2 with HMAC-SHA512, a 128-bit per-user salt and 100,000 iterations by default (the format is versioned inside the stored hash). OWASP's Password Storage Cheat Sheet currently asks for 220,000 iterations with HMAC-SHA512, so raise `PasswordHasherOptions.IterationCount`: verification returns `SuccessRehashNeeded` for any hash with fewer iterations or an older algorithm, and Identity's sign-in rehashes it, so existing users upgrade on their next login.

### Encryption at Rest and in Transit

*In transit* is TLS, covered in the next section. *At rest* means encrypting stored data — database Transparent Data Encryption, encrypted disks, or field-level encryption for especially sensitive columns. The hard part of encryption at rest is **key management**: the encryption key must live somewhere safer than the data it protects, which is what Key Vault / KMS and hardware security modules are for.

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

## HTTPS, TLS, HSTS, and Certificates

**HTTPS is just HTTP inside a TLS tunnel.** **TLS (Transport Layer Security)**, the successor to SSL, provides three guarantees for data in transit: *confidentiality* (eavesdroppers see ciphertext), *integrity* (tampering is detected), and *authentication* (the certificate proves you're talking to the real server).

A **certificate** binds a public key to a domain name and is signed by a Certificate Authority (CA) the client trusts. The handshake puts the two kinds of encryption from the previous section to work: asymmetric crypto to authenticate the server and agree on keys, then fast symmetric encryption for the session.

### The TLS Handshake, Step by Step

Here is a **TLS 1.3** handshake, the modern default, which is faster than its predecessors (one round-trip):

1. **ClientHello** — The client sends supported TLS versions, a list of cipher suites, a random nonce, and — a TLS 1.3 optimization — its **key share** (an ephemeral public key guess) up front.
2. **ServerHello** — The server picks a cipher suite, sends its own key share and random nonce. At this point both sides can derive the shared symmetric key via **Diffie-Hellman** — crucially, without ever sending the secret over the wire.
3. **Certificate** — The server sends its **X.509 certificate**, which binds its domain name to a public key and is signed by a **Certificate Authority (CA)** the client trusts. The client verifies the signature chain up to a trusted root in its store, checks the domain matches, and checks expiry/revocation.
4. **Finished** — Both sides confirm they derived the same keys. From here, all application data is encrypted with fast **symmetric** encryption (e.g., AES-GCM).

The elegant trick: **asymmetric** cryptography (slow) is used only to authenticate and to agree on a shared secret; then **symmetric** cryptography (fast) does the bulk encryption. You get the security of public-key crypto with the speed of symmetric ciphers.

**TLS 1.3** also supports **0-RTT resumption**, where a returning client can send data in its very first packet — great for latency, but 0-RTT data is vulnerable to replay, so never use it for non-idempotent requests.

Step 3 is the only thing that tells the client it isn't talking to an attacker in the middle. That is why disabling certificate validation is never a fix:

> **Pitfall:** Setting `ServerCertificateCustomValidationCallback` to always return `true` disables TLS authentication entirely, silently exposing you to man-in-the-middle attacks. If you see this in a code review, block the PR. If you have a self-signed cert in dev, trust it properly in the machine store instead.

### HTTPS Redirection and HSTS in ASP.NET Core

In ASP.NET Core, redirect HTTP to HTTPS and enable **HSTS**:

```csharp
app.UseHttpsRedirection();
app.UseHsts(); // production only
```

**HSTS** (HTTP Strict Transport Security) sends a response header telling the browser: "for the next *N* seconds, only ever contact this domain over HTTPS, and refuse to proceed if the certificate is invalid." This defeats SSL-stripping attacks where an attacker downgrades the first request to HTTP. ASP.NET Core's `UseHsts` sends a 30-day `max-age` by default and skips `localhost`.

> **Gotcha.** HSTS and redirection are browser mechanisms. An API client follows a redirect *after* its first request has already crossed the network in clear text, `Authorization` header included, and it ignores HSTS. For APIs, don't listen on HTTP at all, or reject plain HTTP with `400` rather than redirecting.

> **Pitfall:** HSTS is sticky and cached by the browser. Don't enable it (especially with `includeSubDomains` and `preload`) until you're certain *every* subdomain can serve valid HTTPS — otherwise you can lock users out of an HTTP-only subdomain. This is why the default template excludes HSTS in Development.

Use modern TLS (1.2 minimum, prefer 1.3), and automate certificate issuance and renewal (Let's Encrypt / ACME, or your cloud's managed certificates).

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

With `[ApiController]`, model validation runs automatically and returns a `400` with details before your action executes; [Chapter 5: Model Binding & Validation](#model-binding-validation) covers the pipeline and FluentValidation.

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

The browser's **Same-Origin Policy** blocks JavaScript on one origin from reading responses from another, and **CORS** (Cross-Origin Resource Sharing) is how a server *opts in* to allowing specific other origins; [Chapter 6: Cookies, Sessions, and the Same-Origin Policy](#cookies-sessions-and-the-same-origin-policy) explains both from the browser's side, preflight included. Here the point is the server's: CORS is a relaxation of security, so configure it as tightly as possible.

```csharp
builder.Services.AddCors(options =>
    options.AddPolicy("spa", policy => policy
        .WithOrigins("https://app.example.com") // explicit, never "*"
        .WithMethods("GET", "POST")
        .WithHeaders("Authorization", "Content-Type")
        .AllowCredentials()));
```

> **Pitfall:** `AllowAnyOrigin()` combined with `AllowCredentials()` is invalid and dangerous — the spec forbids it precisely because it would let *any* site make credentialed requests to your API, and ASP.NET Core's policy builder throws `InvalidOperationException` for it. The workaround people then reach for, reflecting the request's `Origin` header back, recreates the same hole: never do it, and never wildcard origins on an authenticated API.

CORS is enforced by the *browser*, not the server — it is not an authorization mechanism. It stops a malicious site's JavaScript from reading your API in a victim's browser; it does nothing against `curl` or a server-side attacker. And it hides *responses*, not requests: a "simple" cross-origin request (a `GET`, or a `POST` with a form or plain-text body) is sent without a preflight, your server executes it, and only then does the browser withhold the response. State-changing endpoints that use cookies still need anti-forgery protection.

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

Scanning tells you about *known* vulnerabilities in packages you already trust. It says nothing about a package that was deliberately backdoored last night, about your build system being modified after the source was clean, or about proving to a customer what went into the binary you shipped them. That wider problem — the packages you consume, the build that assembles them, and the artifacts you publish — is the subject of [Chapter 27: Security in Depth and the Supply Chain](#chapter-27-security-in-depth-and-the-supply-chain).

> **Capstone tie-in:** This chapter is exercised by ShopCore Step 5 (Caching, Auth, and Observability) — you'd add JWT authentication and role-based authorization so only authenticated users check out and only admins mutate the catalog. See [Chapter 44](#chapter-44-capstone-one-project-growing-up).

## Summary

Security is a discipline of layered, deliberate decisions. Adopt the mindset — defense in depth, least privilege, secure by default, never trust input — and it informs every line you write. Distinguish authentication (who you are) from authorization (what you may do), and check access per resource, where the record is. Know the OWASP Top 10 as *categories* of failure and the .NET mitigation for each. Delegate identity to OAuth 2.0 / OIDC with the Authorization Code + PKCE flow, and validate JWTs on signature, algorithm, issuer, audience and lifetime — every time, without switching a check off to make a `401` go away. Keep secrets out of source and in a managed vault reached with a managed identity; hash passwords with a slow salted algorithm you didn't write; reach for `IDataProtector` instead of raw crypto; enforce TLS and never disable certificate validation; and defend the browser boundary with validation, encoding, anti-forgery tokens, tight CORS, and a strong CSP. Finally, scan your dependencies continuously — because the vulnerability you didn't write is still yours to fix.

[Chapter 27: Security in Depth and the Supply Chain](#chapter-27-security-in-depth-and-the-supply-chain) takes the next step: replacing stored secrets with workload identity and short-lived tokens, zero trust between services, algorithm agility, and the software supply chain.

## Interview Questions

**AuthN vs AuthZ?**
**Authentication** verifies *who you are* (login, token validation). **Authorization** verifies *what you're allowed to do* (roles, policies, resource ownership). AuthN comes first; a valid identity still needs an authorization check per action. Conflating them ("logged in = allowed") is a classic vulnerability.

**Red flag:** "If the user is authenticated, they can access the endpoint" — that's broken access control, OWASP's #1 risk.

**Name a few OWASP Top 10 risks.**
Broken access control, injection (SQL/command), cryptographic failures (weak/no encryption of secrets), insecure design, security misconfiguration, vulnerable/outdated components, identification/authentication failures, and SSRF. The theme: validate input, enforce access control server-side, encrypt secrets, and patch dependencies.

**How do you prevent SQL injection?**
Use parameterized queries / prepared statements (or an ORM that parameterizes) so user input is always data, never concatenated into SQL. Never build queries by string concatenation. Add least-privilege DB accounts and input validation as defense in depth. EF Core and Dapper parameterize by default — the risk is raw string SQL.

**Red flag:** "Sanitize the input by escaping quotes" — escaping-by-hand is a blocklist that always misses cases; parameterization makes input structurally data.

**How do you store passwords?**
Never plaintext or plain hash. Use a slow, salted, adaptive password hash — bcrypt, scrypt, Argon2, or PBKDF2 with a high work factor and a per-user salt. The salt defeats rainbow tables; the slowness defeats brute force. Increase the work factor over time. Never encrypt passwords (reversible) when you should hash them.

**Red flag:** "Encrypt them with AES" or "hash with MD5/SHA-256" — encryption is reversible, and fast hashes are exactly what brute-forcers want.

**How do you validate a JWT — what must you check?**
Verify the signature against the trusted key, then validate the claims: issuer, audience, expiry (`exp`) and not-before (`nbf`), and the signing algorithm (reject `none` and don't let the token pick the algorithm). Only then trust its claims. Skipping audience/expiry checks or trusting the header's alg are the common JWT vulnerabilities.

**Where do secrets go — not appsettings, then where?**
Out of source control and out of plain config: use a secrets manager / vault (Azure Key Vault, AWS Secrets Manager, HashiCorp Vault), environment variables injected at deploy, or user-secrets in local dev. Rotate them, scope access least-privilege, and never log them. A leaked connection string in Git is a breach.

## Check at work

**Inspect.** Search your service:

- every action with an `{id}` in its route: does the query filter by the caller's owner or tenant, or does it load by ID alone?
- `FromSqlRaw(`, `ExecuteSqlRaw(`, `SqlQueryRaw(`, and SQL built with `+` or `$"`: each hit needs a reason, and any identifier in it needs an allow-list;
- `ValidateAudience = false`, `ValidateIssuer = false`, `RequireSignedTokens = false`, a custom `SignatureValidator`, or `ServerCertificateCustomValidationCallback` that returns `true`: each one disables a check;
- secrets in `appsettings*.json` and in history: `git log -p -S "Password=" -- '*.json'`;
- `AllowAnyOrigin()`, or a policy that echoes the request's `Origin` back.

**Do.** Review one endpoint with three questions: who is the caller, what proves they may touch *this* record, and which inputs reach a parser (SQL, a shell, a URL fetch)? Write the answers into the pull request.

**Measure.** Count the endpoints that allow anonymous access, from your route table or OpenAPI document. Then set a fallback policy that requires an authenticated user, so that opening an endpoint takes an explicit `[AllowAnonymous]`.
