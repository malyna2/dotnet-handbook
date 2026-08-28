# Chapter 12: DevOps & CI/CD

_⏱️ Estimated read time: ~50 min · 7606 words (study pace)_

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

## Bringing It Together

A senior-level command of DevOps is really a chain of small, well-understood decisions. You keep branches short-lived and integrate constantly, because you understand that a branch is just a pointer and that deferred integration is where pain accumulates. You curate history with interactive rebase before review and treat shared history as immutable, trusting the reflog to catch your mistakes. You express your build as `dotnet` commands that run identically on your laptop and in CI, centralize configuration with `Directory.Build.props` and Central Package Management, and version artifacts deterministically with SemVer and GitVersion. Your pipeline restores with caching, tests across a matrix, gates on coverage and static analysis, and promotes a single immutable artifact through environments. You deploy with a strategy that makes rollback trivial, hide incomplete work behind feature flags, and keep every secret out of source control and inside a managed store.

None of these practices is exotic. Their power is cumulative: together they turn shipping software from a nerve-wracking event into a routine, boring, reversible non-event—which, in production, is exactly what you want.
