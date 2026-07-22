# Chapter 12: DevOps & CI/CD

_⏱️ Estimated read time: ~32 min ·     4709 words (study pace)_

DevOps is not a job title, a tool, or a team you can buy. It is a way of working in which the people who write software and the people who run it in production share responsibility for the whole lifecycle. The practical machinery that makes this possible is automation: version control that lets many people change the same codebase safely, pipelines that build and test every change, and deployment mechanisms that push validated code to users without drama. This chapter takes you from the internals of Git all the way to canary deployments, with .NET as the running example throughout. By the end you should be able to design a pipeline, reason about a branching strategy, and explain to a junior why rebasing a shared branch is a bad idea.

## Git, Properly Understood

Most developers use Git as a sequence of memorized incantations. To operate at a senior level you need a mental model of what those commands actually do. That model is simpler than the command surface suggests, because Git is built on a tiny, elegant data structure.

### The Object Model

Git is, at its heart, a content-addressable key-value store. Everything it stores is an *object*, and every object is identified by the SHA-1 (increasingly SHA-256) hash of its contents. There are four object types, but three matter for understanding day-to-day work.

A **blob** is the raw contents of a file. Not the filename, not the permissions—just the bytes. If two files in your repo have identical contents, Git stores exactly one blob and points to it twice. The hash *is* the identity; change one byte and you get a completely different blob with a different hash.

A **tree** represents a directory. It is a list of entries, each mapping a name (like `Program.cs` or `src`) to a hash and a mode. Those hashes point either to blobs (files) or to other trees (subdirectories). A tree is thus a snapshot of a directory's structure at a moment in time.

A **commit** points to exactly one tree—the complete snapshot of your project at that instant—plus metadata: author, committer, timestamp, message, and the hashes of its *parent* commit(s). A normal commit has one parent. A merge commit has two or more. The very first commit has none.

This is the crucial insight: **a commit is not a diff. It is a full snapshot.** Git computes diffs on demand by comparing two snapshots, but it stores complete trees. Because each commit references its parent, the commits form a directed acyclic graph (DAG). Follow the parent pointers backward and you walk the entire history.

> **Key mental model:** A branch is not a container of commits. A branch is a lightweight, movable *pointer* to a single commit—literally a 40-character hash in a small file under `.git/refs/heads/`. `HEAD` is a pointer to the branch you currently have checked out. This is why creating a branch in Git is instantaneous: you are writing one file.

You can see all of this directly:

```bash
# Show the type of any object
git cat-file -t HEAD          # commit
# Show the contents of the commit object
git cat-file -p HEAD          # tree hash, parent hash, author, message
# Follow the tree hash from above
git cat-file -p <tree-hash>   # lists blobs and subtrees with their hashes
```

Understanding that branches are just pointers demystifies nearly every "scary" Git operation. Resetting a branch moves a pointer. Rebasing rewrites commits and moves a pointer. Merging creates a commit and moves a pointer. Nothing is ever truly destroyed immediately—which brings us to the reflog later.

### .gitignore

Before your first commit, decide what should *never* enter the object store. In .NET the usual suspects are build outputs, IDE state, and anything containing secrets.

```gitignore
# Build artifacts
bin/
obj/
[Dd]ebug/
[Rr]elease/
*.user

# Test and coverage output
TestResults/
coverage/
*.trx

# Local secrets and environment
appsettings.Development.local.json
.env
*.pfx

# IDE
.vs/
.idea/
```

> **Pitfall:** `.gitignore` only prevents *untracked* files from being added. If you already committed `bin/` or a secrets file, adding it to `.gitignore` does nothing—Git keeps tracking it. You must `git rm --cached <path>` to stop tracking it, then commit that removal. And if a secret was ever committed, it lives in history forever until you rewrite it; rotating the secret is almost always faster and safer than scrubbing history.

### Branching Strategies: GitFlow vs Trunk-Based

How a team uses branches shapes how fast it can ship. Two philosophies dominate.

**GitFlow** uses long-lived branches with defined roles: `main` holds released code, `develop` is the integration branch, and short-lived `feature/*`, `release/*`, and `hotfix/*` branches feed into them. It is ceremonious and works well when you ship discrete versioned releases (think a boxed product or an on-premise .NET application customers install). Its weakness is that `develop` and `feature` branches drift apart, and big-bang merges produce painful conflicts. Integration is deferred, which is exactly what continuous integration tries to avoid.

**Trunk-based development** keeps everyone committing to a single branch (`main`) many times a day, using very short-lived branches (hours, not weeks) that merge back quickly. Incomplete work is hidden behind feature flags rather than long-lived branches. This is the model that high-performing teams and virtually all continuous-deployment shops use, because small frequent merges are cheap and low-risk.

> **Best practice:** For a service you deploy continuously, prefer trunk-based development with short-lived branches and feature flags. Reserve GitFlow-style release branches for software with genuine parallel-version maintenance needs. The longer a branch lives, the more expensive its eventual merge.

### Merge vs Rebase

These two commands both integrate changes from one branch into another, but they do it differently and the difference matters.

`git merge feature` into `main` creates a new *merge commit* with two parents, tying the two histories together. History is preserved exactly as it happened—including the fact that development was concurrent. The downside is a history graph full of merge commits that can be noisy.

`git rebase main` while on `feature` takes each of your feature commits, sets them aside, moves your branch pointer to the tip of `main`, and *replays your commits on top* one by one. The result is a linear history as if you had started your work from the current `main`. Note that rebasing creates *new* commits with new hashes—the originals are abandoned.

```bash
# Merge approach: integrate main's updates into your feature branch
git switch feature
git merge main            # creates a merge commit if histories diverged

# Rebase approach: replay your work on top of the latest main
git switch feature
git rebase main           # linear history, new commit hashes
```

When to use each:

- **Rebase to keep your own in-progress feature branch current** with `main`, and to clean up messy local history before sharing. Linear history is easier to read and to bisect.
- **Merge to integrate a finished feature into a shared branch**, especially with `--no-ff` so the merge commit records that a feature landed as a unit.

> **The golden rule of rebasing:** Never rebase commits that others have already pulled. Because rebase rewrites history (new hashes), anyone who based work on the old commits will have a divergent history, and the next `git pull` becomes a nightmare of duplicated commits. Rebase private history freely; treat shared history as immutable.

### Interactive Rebase

Interactive rebase is the power tool for curating history before you share it. It lets you reorder, combine (squash), edit, or drop commits.

```bash
git rebase -i HEAD~4
```

This opens an editor listing your last four commits with a command in front of each:

```
pick a1b2c3d Add order validation
squash e4f5g6h Fix typo in validation
reword h7i8j9k Add repository method
drop  k0l1m2n Debug logging I forgot to remove
```

- `pick` keeps the commit as-is.
- `squash` (or `s`) folds the commit into the previous one, letting you merge the two messages.
- `fixup` is like squash but discards the squashed commit's message entirely.
- `reword` keeps the commit but lets you rewrite its message.
- `drop` deletes the commit.

This is how you turn a working branch full of "wip", "fix", and "actually fix" commits into a handful of clean, reviewable commits. Do it *before* opening a pull request, never after review has started on shared commits.

### Cherry-Pick

`git cherry-pick <hash>` applies the changes from a single commit onto your current branch, creating a new commit with the same diff but a new parent and hash. The classic use is a hotfix: a bug is fixed on `main`, and you need that exact fix on a `release/1.4` branch without dragging along everything else.

```bash
git switch release/1.4
git cherry-pick 9f8e7d6      # apply just that one fix here
```

> **Pitfall:** Cherry-picking the same change into multiple branches duplicates the logical change under different hashes. When those branches later merge, Git may or may not recognize the duplication, occasionally producing surprising conflicts. Use cherry-pick deliberately for isolated fixes, not as a routine integration strategy.

### Resolving Conflicts

A conflict occurs when merge or rebase cannot automatically reconcile two changes to the same region of a file. Git marks the file with conflict markers:

```csharp
<<<<<<< HEAD
    var timeout = TimeSpan.FromSeconds(30);
=======
    var timeout = TimeSpan.FromSeconds(60);
>>>>>>> feature/longer-timeout
```

Everything between `<<<<<<<` and `=======` is your current branch's version; everything from `=======` to `>>>>>>>` is the incoming version. You resolve by editing the file to the correct final state, deleting the markers, then staging it.

```bash
# After editing the file to its correct final form:
git add src/HttpClientFactory.cs
git status                 # confirm no remaining "Unmerged paths"
git merge --continue       # or git rebase --continue
# Escape hatch if things went wrong:
git merge --abort          # returns to the pre-merge state
```

> **Best practice:** Keep pull requests small. The likelihood and pain of conflicts grows with the size and age of a branch. A 40-line PR merged today rarely conflicts; a 4,000-line PR merged next month almost certainly will. Enable a merge tool (`git mergetool`) or rely on your IDE's three-way merge view for complex cases.

### The Reflog: Your Safety Net

The single most reassuring fact about Git is that it almost never truly loses committed work. Every time `HEAD` moves—commit, checkout, reset, rebase, merge—Git records the previous position in the **reflog**.

```bash
git reflog
# a1b2c3d HEAD@{0}: rebase finished
# f4e5d6c HEAD@{1}: checkout: moving to feature
# 9a8b7c6 HEAD@{2}: commit: Add order validation  <-- the state before I broke everything
```

Suppose you ran `git reset --hard` and lost commits, or a rebase went sideways. Find the hash of the good state in the reflog and recover it:

```bash
git reset --hard 9a8b7c6      # move the branch back to that commit
# or, to inspect without moving your branch:
git switch -c recovery 9a8b7c6
```

Reflog entries are local and expire (default 90 days for reachable, 30 for unreachable), but that is more than enough to rescue almost any "I destroyed my work" panic. Knowing the reflog exists changes your relationship with Git's scarier commands: they become reversible experiments rather than one-way risks.

## What CI/CD Actually Means

The acronym conflates three distinct practices. Precision here separates people who understand the pipeline from those who parrot the buzzword.

**Continuous Integration (CI)** is the discipline of merging every developer's work into a shared mainline frequently—at least daily—and verifying each merge with an automated build and test run. The goal is to catch integration problems within minutes of introducing them, while the change is small and fresh in the author's mind. CI is fundamentally a *human* practice (integrate often) supported by automation (build and test on every push).

**Continuous Delivery (CD)** extends CI: every change that passes the pipeline is automatically prepared and proven to be *deployable* to production. The build produces a release-ready artifact and may deploy automatically to staging, but the final push to production remains a deliberate, one-click human decision.

**Continuous Deployment** removes even that final button. Every change that passes all automated gates goes to production automatically, with no human in the loop. This demands very high confidence in your test suite and safe deployment techniques (feature flags, canaries, fast rollback).

> **The distinction that matters in interviews and in practice:** Continuous *Delivery* keeps a human gate before production; continuous *Deployment* does not. Both require the same rigorous automated pipeline underneath.

## CI/CD Platforms

Several platforms implement these ideas. They differ in hosting model and syntax, but the concepts transfer.

**GitHub Actions** is event-driven automation living in your GitHub repository. Workflows are YAML files in `.github/workflows/`. Its ecosystem of reusable *actions* from the Marketplace makes it fast to assemble pipelines, and it is the default choice for projects already on GitHub. We cover it in depth below.

**Azure DevOps Pipelines** is Microsoft's mature offering, deeply integrated with the .NET ecosystem, Azure deployment targets, and enterprise features like environments, approvals, and variable groups backed by Azure Key Vault. Pipelines are defined in `azure-pipelines.yml` with `stages`, `jobs`, and `steps`, or via a classic visual editor. It is common in enterprise .NET shops.

**GitLab CI/CD** is built into GitLab and configured with `.gitlab-ci.yml`. It uses `stages` and `jobs` with a runner model, and is known for a coherent single-application experience covering source, CI, registry, and deployment.

**Jenkins** is the veteran open-source automation server. It is enormously flexible via its plugin ecosystem and `Jenkinsfile` pipelines, self-hosted, and still widespread in organizations with established infrastructure. The tradeoff is operational overhead: you run, patch, and secure the server and its agents yourself.

The concepts—triggers, jobs, steps, artifacts, caching, secrets, environments—exist in all four. Learn them once and you can read any of these platforms' configuration.

## A Complete GitHub Actions Workflow for .NET

Let's build a real pipeline that restores, builds, tests, publishes, containerizes, and deploys a .NET application. We'll then dissect it.

```yaml
name: build-test-deploy

on:
  push:
    branches: [ main ]
  pull_request:
    branches: [ main ]

env:
  DOTNET_VERSION: '10.0.x'
  DOTNET_SKIP_FIRST_TIME_EXPERIENCE: true
  DOTNET_NOLOGO: true

jobs:
  build-and-test:
    runs-on: ubuntu-latest
    strategy:
      matrix:
        configuration: [ Debug, Release ]
    steps:
      - name: Check out code
        uses: actions/checkout@v4

      - name: Set up .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: ${{ env.DOTNET_VERSION }}

      - name: Cache NuGet packages
        uses: actions/cache@v4
        with:
          path: ~/.nuget/packages
          key: nuget-${{ runner.os }}-${{ hashFiles('**/packages.lock.json', '**/*.csproj') }}
          restore-keys: |
            nuget-${{ runner.os }}-

      - name: Restore
        run: dotnet restore

      - name: Build
        run: dotnet build --configuration ${{ matrix.configuration }} --no-restore

      - name: Test
        run: >
          dotnet test --configuration ${{ matrix.configuration }} --no-build
          --logger trx --results-directory ./TestResults
          --collect:"XPlat Code Coverage"

      - name: Upload test results
        if: always()
        uses: actions/upload-artifact@v4
        with:
          name: test-results-${{ matrix.configuration }}
          path: ./TestResults

  publish-and-containerize:
    needs: build-and-test
    if: github.ref == 'refs/heads/main'
    runs-on: ubuntu-latest
    permissions:
      contents: read
      packages: write
    steps:
      - uses: actions/checkout@v4

      - name: Set up .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: ${{ env.DOTNET_VERSION }}

      - name: Publish
        run: >
          dotnet publish src/OrderApi/OrderApi.csproj
          --configuration Release --output ./publish

      - name: Log in to GitHub Container Registry
        uses: docker/login-action@v3
        with:
          registry: ghcr.io
          username: ${{ github.actor }}
          password: ${{ secrets.GITHUB_TOKEN }}

      - name: Build and push image
        uses: docker/build-push-action@v6
        with:
          context: .
          push: true
          tags: ghcr.io/${{ github.repository }}:${{ github.sha }}

  deploy-production:
    needs: publish-and-containerize
    runs-on: ubuntu-latest
    environment:
      name: production
      url: https://api.example.com
    steps:
      - name: Deploy to production
        run: |
          echo "Deploying image ghcr.io/${{ github.repository }}:${{ github.sha }}"
          # e.g. az webapp deploy, kubectl set image, helm upgrade, etc.
        env:
          DEPLOY_TOKEN: ${{ secrets.PRODUCTION_DEPLOY_TOKEN }}
```

Now the anatomy.

**Triggers (`on`).** This workflow runs on pushes to `main` and on pull requests targeting `main`. PR runs give you a green check before merge; push runs to `main` proceed all the way to deployment. Restricting triggers keeps you from wasting minutes on irrelevant events.

**Jobs and dependencies (`needs`).** Three jobs run in sequence because each `needs` the previous. Jobs without a `needs` relationship run in parallel on separate runners. The `if: github.ref == 'refs/heads/main'` guard means the publish job is skipped for pull requests—you test PRs but only build and ship from `main`.

**Matrix (`strategy.matrix`).** The build-and-test job runs twice in parallel, once per `configuration`. Matrices generalize to multiple dimensions (OS, .NET version, database engine) and are how you test across combinations cheaply. Every matrix cell is an independent runner.

**Caching (`actions/cache`).** Restoring NuGet packages from the internet on every run is slow. The cache action stores `~/.nuget/packages` keyed by a hash of your project and lock files. When those files are unchanged, the key hits and packages are restored from cache in seconds. The `restore-keys` fallback lets a partial match seed the cache even when the exact key misses.

**Secrets.** `${{ secrets.PRODUCTION_DEPLOY_TOKEN }}` reads an encrypted value stored in the repository or environment settings. Secrets are never printed in logs (GitHub masks them) and never live in the YAML. `GITHUB_TOKEN` is a special automatically-provisioned secret scoped to the current run.

**Environments.** The `environment: production` block ties the deploy job to a named environment that can carry protection rules—required reviewer approvals, wait timers, and environment-scoped secrets. This is how you implement the human gate of continuous *delivery*: configure `production` to require a manual approval, and the job pauses until someone clicks approve.

**Permissions.** The `permissions` block follows least privilege—the containerize job gets `packages: write` because it pushes an image, and nothing more.

## Build Automation with the dotnet CLI

The pipeline above leans on the `dotnet` CLI, which is the same tool you use locally. Consistency between local and CI builds eliminates a whole class of "works on my machine" problems.

```bash
dotnet restore                       # download NuGet dependencies
dotnet build -c Release --no-restore # compile; skip a redundant restore
dotnet test  -c Release --no-build   # run tests against the built output
dotnet publish -c Release -o ./out   # produce a self-contained, deployable app
dotnet pack  -c Release -o ./nupkgs  # produce a NuGet package (.nupkg)
```

The `--no-restore` and `--no-build` flags matter in CI: each stage is explicit, so you avoid the CLI silently re-running earlier steps and wasting time. `dotnet publish` gathers the app, its dependencies, and runtime config into an output folder ready to copy to a server or into a container. `dotnet pack` is for producing libraries you distribute via NuGet.

### MSBuild, Directory.Build.props, and Central Package Management

Under the CLI sits **MSBuild**, the engine that reads your `.csproj` files (which are MSBuild XML) and executes the build. You rarely invoke it directly, but understanding that `dotnet build` *is* MSBuild explains where build configuration lives.

Setting the same properties in every `.csproj` is tedious and error-prone. **`Directory.Build.props`** solves this: place one at your repository root and MSBuild automatically imports it into every project beneath it.

```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>
</Project>
```

Now every project inherits nullable reference types, the latest language version, and—importantly for CI hygiene—warnings treated as errors, so a sloppy warning fails the build rather than rotting silently.

**Central Package Management (CPM)** does the same for NuGet versions. Instead of pinning versions in every project, you declare them once in `Directory.Packages.props`:

```xml
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
  </PropertyGroup>
  <ItemGroup>
    <PackageVersion Include="Serilog.AspNetCore" Version="8.0.1" />
    <PackageVersion Include="FluentValidation" Version="11.9.0" />
  </ItemGroup>
</Project>
```

Individual projects then reference packages *without* a version:

```xml
<PackageReference Include="Serilog.AspNetCore" />
```

> **Best practice:** Adopt `Directory.Build.props` and Central Package Management early. They prevent version drift, where different projects in the same solution pull incompatible versions of the same library—one of the more maddening debugging experiences in a large .NET codebase.

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

## Semantic Versioning and GitVersion

A version number is a contract. **Semantic Versioning (SemVer)** formalizes it as `MAJOR.MINOR.PATCH`:

- **MAJOR** increments on a breaking change—existing consumers must change their code.
- **MINOR** increments when you add functionality in a backward-compatible way.
- **PATCH** increments for backward-compatible bug fixes.

Pre-release versions append a suffix like `2.4.0-beta.1`, which sorts *before* the final `2.4.0`. Honoring SemVer lets consumers express dependency ranges safely—`[2.0,3.0)` means "any 2.x, but never auto-upgrade across the breaking 3.0 boundary".

Deciding and stamping the version by hand is tedious and easy to get wrong. **GitVersion** computes the version automatically from your Git history—tags, branch names, and commit counts—so that the same commit always yields the same version, and the version increments consistently.

```bash
dotnet tool install --global GitVersion.Tool
dotnet-gitversion /showvariable SemVer   # e.g. 2.4.0-feature-orders.5
```

In CI you capture that value and feed it into `dotnet pack -p:Version=$VERSION`, so your artifacts are versioned deterministically from source control rather than from someone remembering to bump a number.

## Deployment Strategies

Getting a validated artifact built is only half the job; *how* you replace the running version determines whether users notice.

**Rolling deployment** replaces instances gradually. In a fleet of ten servers you update two at a time, letting the new version take traffic while the rest still run the old version, until all are updated. It needs no extra capacity but means both versions serve traffic simultaneously for a while—your app and database must tolerate that.

**Blue-green deployment** runs two complete environments: *blue* (current, live) and *green* (new). You deploy to green, test it in isolation, then flip the router so all traffic goes to green in one atomic switch. Rollback is instant—flip back to blue. The cost is running double the infrastructure during the transition.

**Canary deployment** releases the new version to a small slice of traffic first—say 5%—while watching error rates, latency, and business metrics. If the canary stays healthy you progressively increase the share to 25%, 50%, 100%. If it misbehaves you route everyone back to the stable version, having exposed only a fraction of users to the problem. Canaries pair naturally with feature flags and good observability.

> **Best practice:** Whatever strategy you choose, make **rollback cheaper and faster than fixing forward**. The measure of a mature deployment process is not that failures never happen, but that a bad release can be reverted in seconds without a heroic effort.

**Artifact management** underpins all of this. Build an artifact *once*—a container image, a NuGet package, a published zip—store it in a registry or artifact feed, and promote that *same* immutable artifact through environments (dev → staging → production). Tag it by commit SHA or SemVer so you know exactly what is running. Rebuilding per environment reintroduces the risk that staging and production differ.

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

## Secrets in Pipelines

The cardinal rule: **secrets never enter source control.** Not in `appsettings.json`, not in a committed `.env`, not "temporarily" in a config file. Once a secret is in Git history it is compromised, because history is distributed to everyone who clones.

Where secrets *do* live:

- **Locally**, use .NET User Secrets (`dotnet user-secrets set`) which stores values outside the repo tree, or environment variables.
- **In CI**, use the platform's encrypted secret store—GitHub Actions secrets, Azure DevOps variable groups (ideally backed by Azure Key Vault), GitLab CI/CD variables. These are injected as environment variables at runtime and masked in logs.
- **In production**, use a managed secret store—Azure Key Vault, AWS Secrets Manager, HashiCorp Vault—accessed via a managed identity so no credential is stored anywhere at all.

> **Best practice:** Add automated secret scanning (GitHub secret scanning, Gitleaks, or `git-secrets` as a pre-commit hook) to your pipeline so an accidental commit of an API key is caught before it merges. And when a leak does happen, *rotate the secret immediately*—removing it from history is not enough, because clones and forks may retain it.

## Static Analysis Gates in CI

A pipeline that only runs tests checks correctness but not health. Static analysis gates enforce quality objectively, so standards do not erode under deadline pressure.

**SonarQube** (or SonarCloud) performs deep static analysis—bugs, code smells, security hotspots, duplication, and complexity—and enforces a **quality gate**: a set of pass/fail conditions such as "no new critical issues" and "coverage on new code ≥ 80%". A failing gate fails the pipeline, blocking the merge. Because it evaluates *new* code specifically, you can improve a legacy codebase incrementally without being buried by its existing debt.

**Coverage thresholds** ensure tests actually exercise the code. Collect coverage during `dotnet test` (via Coverlet, the `XPlat Code Coverage` collector in the workflow above), then fail the build if coverage drops below a threshold:

```bash
dotnet test --collect:"XPlat Code Coverage" \
  -- DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.Threshold=80
```

A typical quality-gate stage in the pipeline runs the Sonar scanner around the build and test steps, uploads results, and waits for the gate verdict:

```yaml
      - name: SonarQube scan
        run: |
          dotnet sonarscanner begin /k:"order-api" \
            /d:sonar.host.url="${{ secrets.SONAR_HOST }}" \
            /d:sonar.token="${{ secrets.SONAR_TOKEN }}" \
            /d:sonar.cs.opencover.reportsPaths="**/coverage.opencover.xml"
          dotnet build -c Release
          dotnet test -c Release --collect:"XPlat Code Coverage"
          dotnet sonarscanner end /d:sonar.token="${{ secrets.SONAR_TOKEN }}"
```

> **Best practice:** Set quality gates on *new* code rather than demanding a huge legacy codebase suddenly hit 90% coverage. A ratcheting gate—"don't make it worse"—is achievable and steadily improves the codebase, whereas an unrealistic absolute gate just gets disabled the first time it blocks a hotfix.

> **Capstone tie-in:** This chapter is exercised by ShopCore Steps 4 (CI/CD with GitHub Actions) and 8 (Deploy with Infrastructure as Code) — you'd build a workflow that tests every PR and publishes tagged images, then promote those images into a Terraform-provisioned environment. See Chapter 32.

## Bringing It Together

A senior-level command of DevOps is really a chain of small, well-understood decisions. You keep branches short-lived and integrate constantly, because you understand that a branch is just a pointer and that deferred integration is where pain accumulates. You curate history with interactive rebase before review and treat shared history as immutable, trusting the reflog to catch your mistakes. You express your build as `dotnet` commands that run identically on your laptop and in CI, centralize configuration with `Directory.Build.props` and Central Package Management, and version artifacts deterministically with SemVer and GitVersion. Your pipeline restores with caching, tests across a matrix, gates on coverage and static analysis, and promotes a single immutable artifact through environments. You deploy with a strategy that makes rollback trivial, hide incomplete work behind feature flags, and keep every secret out of source control and inside a managed store.

None of these practices is exotic. Their power is cumulative: together they turn shipping software from a nerve-wracking event into a routine, boring, reversible non-event—which, in production, is exactly what you want.
