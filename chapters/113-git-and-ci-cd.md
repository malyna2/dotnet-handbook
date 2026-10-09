# Chapter 13: Git and CI/CD

The people who write software and the people who run it share one piece of machinery: version control that lets many people change the same codebase safely, and pipelines that build and test every change the same way on every machine. This chapter makes you able to keep a branch current, clean and reviewable without losing anyone's work, get back commits that look lost, and set up the SDK, its tools, the analyzers and the formatter so that your laptop and the CI build apply the same rules, then read and write a GitHub Actions pipeline for a .NET service.

Each half rests on one mechanism. **A commit is an immutable snapshot whose ID is a hash of its content, parent IDs included; a branch is only a movable name for one commit.** Merge versus rebase, the reflog, what `.gitignore` can't undo and why long branches hurt all follow from that sentence. And **the `dotnet` CLI is a driver**: the same commands, tools and compiler-hosted analyzers run on every machine, so a pipeline is your local build, written down and run on every push.

The chapter goes from Git's model to the pipeline: Git first, then what CI and CD mean, the `dotnet` CLI and MSBuild that every pipeline calls, a complete GitHub Actions workflow, versioning, secrets and the analysis gates, and finally the everyday tools around them. Azure Pipelines, NuGet publishing, deployment strategies, feature flags and platform engineering build on this in [Chapter 26: Delivery and Platform](#chapter-26-delivery-and-platform).

## Git, Properly Understood

Git's command surface is large; the model underneath is small, and once you have it, every command becomes predictable.

### The Object Model

Git is a content-addressable store: every *object* is identified by the hash of its contents (SHA-1 by default; Git's published plan for its next major version makes SHA-256 the default for new repositories). Of the four object types, three matter day to day.

A **blob** is a file's raw bytes, without its name or permissions. Identical contents are stored once; change one byte and you get a different blob with a different hash.

A **tree** is a directory: a list of entries, each mapping a name and a mode to the hash of a blob (a file) or of another tree (a subdirectory).

A **commit** points to one tree, the complete snapshot of the project, plus metadata: author, committer, timestamps, message, and the hashes of its *parent* commits (one normally, two or more for a merge, none for the first commit).

The crucial insight: **a commit is not a diff. It is a full snapshot.** Git computes diffs on demand by comparing two snapshots. Each commit references its parent, so the commits form a directed acyclic graph (DAG), and because the parent's hash is part of the commit's content, a commit's ID covers the entire history behind it. Change anything upstream and every ID downstream changes.

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

Since branches are pointers, the "scary" operations are pointer moves. Resetting a branch moves a pointer. Rebasing writes new commits and moves a pointer. Merging creates a commit and moves a pointer. Nothing committed is destroyed immediately (see the reflog, below).

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

**GitFlow** uses long-lived branches with defined roles: `main` holds released code, `develop` is the integration branch, and short-lived `feature/*`, `release/*`, and `hotfix/*` branches feed into them. It suits discrete versioned releases, such as an application customers install. Its weakness is deferred integration: `develop` and feature branches drift apart, and big-bang merges produce painful conflicts, which is exactly what continuous integration exists to avoid.

**Trunk-based development** keeps everyone committing to a single branch (`main`) many times a day, using very short-lived branches (hours, not weeks) that merge back quickly. Incomplete work is hidden behind feature flags ([Chapter 26: Feature Flags](#feature-flags)) rather than long-lived branches. Continuous-deployment teams use it because small, frequent merges are cheap and low-risk.

> **Best practice:** For a service you deploy continuously, prefer trunk-based development with short-lived branches and feature flags. Reserve GitFlow-style release branches for software with genuine parallel-version maintenance needs. The longer a branch lives, the more expensive its eventual merge.

### Merge vs Rebase

`git merge feature` into `main` creates a new *merge commit* with two parents, tying the two histories together. History is preserved exactly as it happened, at the cost of a noisier graph.

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

> **Pay attention.** **Why a rebased branch can't be pushed normally.** `git push` only updates a remote branch when the remote's commit is an ancestor of yours (a fast-forward). After a rebase your commits are new objects, so the old remote tip is no longer in your history and the push is rejected. That rejection is the safety net: overriding it with `--force` discards whatever the remote has that you don't, including a teammate's push. On your own pull-request branch, use `git push --force-with-lease`, which overwrites only if the remote still points where your last fetch saw it. A background fetch (some IDEs run one) silently refreshes that expectation, so add `--force-if-includes` as well. On a branch others build on, don't force at all: merge instead.

### Interactive Rebase

Interactive rebase curates history before you share it: reorder, combine (squash), edit, or drop commits.

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

Git almost never loses *committed* work. Every time `HEAD` moves—commit, checkout, reset, rebase, merge—Git records the previous position in the **reflog**.

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

Reflog entries are local and expire (by default after 90 days, or 30 for commits no longer reachable from a branch), which is plenty for any "I destroyed my work" moment. The limit is the word *committed*: `git reset --hard` over uncommitted changes discards them for good, because they never became objects. Commit (or stash) before an experiment, and every Git command becomes reversible.

### Git GUIs

The command line is essential, but a good GUI makes history, staging, and conflict resolution far clearer. **Fork** and **GitKraken** give visual branch graphs and painless interactive rebases; **lazygit** is a fast terminal UI for those who live in the shell. Use whichever helps you *understand* history, not avoid learning git.

## What CI/CD Actually Means

The acronym conflates three distinct practices. Precision here separates people who understand the pipeline from those who parrot the buzzword.

**Continuous Integration (CI)** is the discipline of merging every developer's work into a shared mainline frequently—at least daily—and verifying each merge with an automated build and test run. The goal is to catch integration problems within minutes of introducing them, while the change is small and fresh in the author's mind. CI is fundamentally a *human* practice (integrate often) supported by automation (build and test on every push).

**Continuous Delivery (CD)** extends CI: every change that passes the pipeline is automatically prepared and proven to be *deployable* to production. The build produces a release-ready artifact and may deploy automatically to staging, but the final push to production remains a deliberate, one-click human decision.

**Continuous Deployment** removes even that final button. Every change that passes all automated gates goes to production automatically, with no human in the loop. This demands very high confidence in your test suite and safe deployment techniques (feature flags, canaries, fast rollback).

> **The distinction that matters in interviews and in practice:** Continuous *Delivery* keeps a human gate before production; continuous *Deployment* does not. Both require the same rigorous automated pipeline underneath.

## Build Automation with the dotnet CLI

Every pipeline in this chapter leans on the `dotnet` CLI, which is the same tool you use locally. Learn it first: consistency between local and CI builds eliminates a whole class of "works on my machine" problems.

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

### .NET Tools: Global, Local and One-Shot

The `dotnet` CLI is a driver. Built-in commands ship with the SDK: `build`, `test`, `publish`, `format`, `user-secrets`, `watch`. Everything else is a **.NET tool**, a NuGet package that contains a console app, and you choose where it lives:

- **Global** (`dotnet tool install -g dotnet-ef`): installed once per user in `~/.dotnet/tools` (`%USERPROFILE%\.dotnet\tools` on Windows), which is on `PATH`. Convenient, but every machine has whatever version someone installed.
- **Local**: `dotnet new tool-manifest` creates `.config/dotnet-tools.json`; `dotnet tool install dotnet-ef` (no `-g`) pins a version in it; a fresh clone or a CI agent runs `dotnet tool restore`. Commit the manifest: the CLI runs whatever it lists.
- **One-shot** (.NET 10 SDK): `dnx dotnet-counters monitor -p 1234` (or `dotnet tool exec`) runs a tool without installing it, and honours a nearby manifest's version.

A tool whose command starts with `dotnet-` can also be called as `dotnet <rest>`, which is why `dotnet ef` looks built in and isn't.

> **Pay attention.** **Where `dotnet-counters`, `dotnet-trace` and `dotnet-dump` come from, and why they can't see your container.** They are .NET tools from NuGet, not part of the SDK or the runtime. Each one talks to a running process through the runtime's *diagnostic port*: a named pipe `dotnet-diagnostic-{pid}` on Windows, and a Unix domain socket `dotnet-diagnostic-{pid}-…-socket` in `$TMPDIR` (or `/tmp`) on Linux and macOS. A tool on the host can't find a process in a container, because that socket lives in the container's `/tmp` and the PID belongs to the container's namespace. Run the tool inside the container (the docs publish single-file builds for images without an SDK), or share `/tmp` with a sidecar. `DOTNET_EnableDiagnostics=0` closes the port, and with it every one of these tools.

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

## Secrets in Pipelines

The cardinal rule: **secrets never enter source control.** Not in `appsettings.json`, not in a committed `.env`, not "temporarily" in a config file. Once a secret is in Git history it is compromised, because history is distributed to everyone who clones.

Where secrets *do* live:

- **Locally and in production**, user-secrets and a managed vault reached with a managed identity, as [Chapter 12: Secrets Management](#secrets-management) explains.
- **In CI**, the platform's encrypted secret store—GitHub Actions secrets, Azure DevOps variable groups (ideally backed by Azure Key Vault), GitLab CI/CD variables. These are injected as environment variables at runtime and masked in logs, as the `secrets.PRODUCTION_DEPLOY_TOKEN` in the workflow above shows.

> **Best practice:** Add automated secret scanning (GitHub secret scanning, Gitleaks, or `git-secrets` as a pre-commit hook) to your pipeline so an accidental commit of an API key is caught before it merges. And when a leak does happen, *rotate the secret immediately*—removing it from history is not enough, because clones and forks may retain it.

## Static Analysis Gates in CI

A pipeline that only runs tests checks correctness but not health. Static analysis gates enforce quality objectively, so standards do not erode under deadline pressure. They come in three layers: analyzers inside the compiler, a formatter, and a server-side quality gate.

### Analyzers and `.editorconfig`

Inside the IDE and the compiler, the rules come from:

- **Roslyn analyzers** run inside the compiler. They ship with the SDK (the `CAxxxx` rules), come from NuGet packages, and can be authored in-house. They surface issues as build warnings, so they integrate with CI for free.
- **`.editorconfig`** is the single source of truth for style. It travels with the repo, is understood by VS, Rider, and `dotnet format`, and lets you set naming conventions, `var` usage, and analyzer severities per folder.
- **ReSharper** (VS plugin) adds deeper inspections, bulk refactorings, and code cleanup profiles beyond what ships in the box.
- **StyleCop.Analyzers** enforces consistent layout and documentation conventions.
- **SonarLint / SonarQube** catches bugs, security hotspots, and code smells, and its server component tracks quality trends and "new code" gates across the team.

> **Tip:** Commit an `.editorconfig` early and raise a few key analyzer rules to `error` (e.g. `dotnet_diagnostic.CA2007.severity` in library code). Warnings get ignored; build-breaking errors get fixed.

> **Pay attention.** **What the build actually enforces.** Analyzers run inside the compiler, so a diagnostic is a build warning everywhere the build runs, IDE and CI alike. But by default (`AnalysisMode` `Default`) only a small set of `CA` rules is on as warnings; `<AnalysisMode>Recommended</AnalysisMode>` or `All` turns on more. Style rules (`IDExxxx`) don't run in `dotnet build` at all until `<EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>`. And `AnalysisLevel` defaults to `latest`: a new SDK can switch on new warnings, which `TreatWarningsAsErrors` turns into a build that broke with no code change. Pin `AnalysisLevel` (for example `10.0`) and raise it deliberately.

### Formatting in CI

Style debates waste review time. `dotnet format` reads your `.editorconfig` and rewrites code to match. Run `dotnet format --verify-no-changes` as a CI step: the build fails if someone forgot to format. That turns formatting into a machine's job, not a reviewer's.

### SonarQube and Coverage Gates

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

> **Capstone tie-in:** This chapter is exercised by ShopCore Steps 4 (CI/CD with GitHub Actions) and 8 (Deploy with Infrastructure as Code) — you'd build a workflow that tests every PR and publishes tagged images, then promote those images into a Terraform-provisioned environment. See [Chapter 44](#chapter-44-capstone-one-project-growing-up).

## The Everyday Toolbox

The tools below are the ones a modern .NET team reaches for around the build. Know what each one solves, so you pick deliberately rather than by habit.

### IDEs: Visual Studio, Rider, and VS Code

**Visual Studio** (Windows) is the heavyweight. Its debugger is best-in-class, especially for tricky scenarios: mixed-mode debugging, memory dumps, IntelliTrace, and the diagnostic tooling for CPU and allocation profiling. If you work on WPF/WinForms, complex MSBuild setups, or need the deepest debugging experience, it is hard to beat. The cost is that it is heavy and Windows-only.

**JetBrains Rider** is cross-platform and, for many, the day-to-day sweet spot. It combines ReSharper's analysis engine with a fast solution model, excellent refactoring, and strong support for EF Core, Docker, databases, and unit test runners in one product. Rider tends to feel snappier than VS on large solutions and works identically on macOS, Linux, and Windows.

**VS Code + C# Dev Kit** is the lightweight, extensible option. Backed by the Roslyn language server, C# Dev Kit gives you a solution explorer, test integration, and IntelliSense that is now genuinely good for everyday work. It shines for microservices, polyglot repos (a .NET API next to a React front end), and remote/container development via Dev Containers and SSH.

> **Tip:** Match the tool to the task, not to tribal loyalty. Many seniors keep VS Code open for quick edits and scripts, and reach for Rider or Visual Studio when they need heavy refactoring or serious debugging.

### API Testing

Options for poking at HTTP endpoints:

- **`.http` files** live in your repo and run directly inside VS, Rider, and VS Code. Because they are versioned alongside the code, they double as executable documentation. Prefer these for team-shared, checked-in requests.
- **Postman** is the feature-rich standard: environments, scripting, collections, and mock servers, though it increasingly pushes cloud accounts.
- **Insomnia** is a lighter, cleaner alternative with good GraphQL support.
- **Bruno** stores collections as plain files in your git repo, which is a genuine advantage for versioning and avoiding vendor lock-in.

> **Tip:** For anything the team relies on, checked-in `.http` or Bruno files beat a private Postman workspace nobody else can see.

### Diagramming

Diagrams-as-code beat drag-and-drop tools because they diff and version. **Mermaid** renders directly in GitHub/GitLab markdown, so sequence and flow diagrams live next to the code. **PlantUML** is more powerful for detailed UML. The **C4 model** (Context, Container, Component, Code) gives you a shared vocabulary for architecture at different zoom levels; tooling like Structurizr or C4-PlantUML renders it.

> **Tip:** A Mermaid sequence diagram in your README saves ten minutes of whiteboard explanation for every new joiner.

### Local Dev Tooling

Reliable local environments prevent "works on my machine." **Testcontainers** spins up real dependencies (Postgres, Redis, Kafka) in Docker for integration tests, then tears them down; no more shared, drifting test databases ([Chapter 8: Testcontainers for .NET](#testcontainers-for-net) shows how). **Azurite** emulates Azure Storage (Blob, Queue, Table) locally, and **LocalStack** emulates a broad range of AWS services. These let you develop and test cloud integrations offline and in CI.

## Bringing It Together

Command of Git and CI is a chain of small, well-understood decisions. You keep branches short-lived and integrate constantly, because a branch is just a pointer and deferred integration is where pain accumulates. You curate history with interactive rebase before review and treat shared history as immutable, trusting the reflog to catch your mistakes. You express your build as `dotnet` commands that run identically on your laptop and in CI, pin the tools in a manifest, centralize configuration with `Directory.Build.props` and Central Package Management, and version artifacts deterministically with SemVer and GitVersion. Your pipeline restores with caching, tests across a matrix, gates on analyzers, formatting, coverage and static analysis, keeps every secret in the platform's store, and produces one immutable artifact.

None of these practices is exotic. Their power is cumulative: together they turn shipping software from a nerve-wracking event into a routine, boring, reversible non-event. How that artifact reaches production safely (deployment strategies, feature flags, Azure Pipelines and measuring delivery) is [Chapter 26: Delivery and Platform](#chapter-26-delivery-and-platform).

## Check at work

**Inspect.** Four commands in your own repository:

- `git log --graph --oneline -40 main`: one line of small merges is trunk-based in practice; long parallel lanes are branches that lived for weeks.
- `git ls-files | grep -E '(^|/)(bin|obj)/|\.user$|\.env$'`: anything printed is a build output, personal IDE state or a secret file that the repository tracks.
- Look for `.config/dotnet-tools.json`. CI should run `dotnet tool restore`, not `dotnet tool install -g` with whatever version is newest that day.
- Check that CI runs `dotnet format --verify-no-changes` and builds with warnings as errors. If it doesn't, the analyzers are advice.

**Do.** In a throwaway clone, run `git reset --hard HEAD~3`, then get the three commits back from `git reflog`, before you ever need to do it for real.

**Measure.** For your last ten merged pull requests, note the lines changed and the hours from the first commit to the merge. Several days or several hundred lines is where reviews turn into "LGTM" and conflicts start to cost more than the change itself.
