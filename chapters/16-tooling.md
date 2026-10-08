# Chapter 16: Tooling & Productivity

_⏱️ Estimated read time: ~5 min ·     1065 words (study pace)_

The tools below are the ones a modern .NET team actually reaches for. Know what each one solves, so you pick deliberately rather than by habit.

## IDEs: Visual Studio, Rider, and VS Code

**Visual Studio** (Windows) is the heavyweight. Its debugger is best-in-class, especially for tricky scenarios: mixed-mode debugging, memory dumps, IntelliTrace, and the diagnostic tooling for CPU and allocation profiling. If you work on WPF/WinForms, complex MSBuild setups, or need the deepest debugging experience, it is hard to beat. The cost is that it is heavy and Windows-only.

**JetBrains Rider** is cross-platform and, for many, the day-to-day sweet spot. It combines ReSharper's analysis engine with a fast solution model, excellent refactoring, and strong support for EF Core, Docker, databases, and unit test runners in one product. Rider tends to feel snappier than VS on large solutions and works identically on macOS, Linux, and Windows.

**VS Code + C# Dev Kit** is the lightweight, extensible option. Backed by the Roslyn language server, C# Dev Kit gives you a solution explorer, test integration, and IntelliSense that is now genuinely good for everyday work. It shines for microservices, polyglot repos (a .NET API next to a React front end), and remote/container development via Dev Containers and SSH.

> **Tip:** Match the tool to the task, not to tribal loyalty. Many seniors keep VS Code open for quick edits and scripts, and reach for Rider or Visual Studio when they need heavy refactoring or serious debugging.

## Refactoring & Linting

Code-quality tooling comes in layers:

- **Roslyn analyzers** run inside the compiler. They ship with the SDK (the `CAxxxx` rules), come from NuGet packages, and can be authored in-house. They surface issues as build warnings, so they integrate with CI for free.
- **`.editorconfig`** is the single source of truth for style. It travels with the repo, is understood by VS, Rider, and `dotnet format`, and lets you set naming conventions, `var` usage, and analyzer severities per folder.
- **ReSharper** (VS plugin) adds deeper inspections, bulk refactorings, and code cleanup profiles beyond what ships in the box.
- **StyleCop.Analyzers** enforces consistent layout and documentation conventions.
- **SonarLint / SonarQube** catches bugs, security hotspots, and code smells, and its server component tracks quality trends and "new code" gates across the team.

> **Tip:** Commit an `.editorconfig` early and raise a few key analyzer rules to `error` (e.g. `dotnet_diagnostic.CA2007.severity` in library code). Warnings get ignored; build-breaking errors get fixed.

> **Pay attention.** **What the build actually enforces.** Analyzers run inside the compiler, so a diagnostic is a build warning everywhere the build runs, IDE and CI alike. But by default (`AnalysisMode` `Default`) only a small set of `CA` rules is on as warnings; `<AnalysisMode>Recommended</AnalysisMode>` or `All` turns on more. Style rules (`IDExxxx`) don't run in `dotnet build` at all until `<EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>`. And `AnalysisLevel` defaults to `latest`: a new SDK can switch on new warnings, which `TreatWarningsAsErrors` turns into a build that broke with no code change. Pin `AnalysisLevel` (for example `10.0`) and raise it deliberately.

## Formatting in CI

Style debates waste review time. `dotnet format` reads your `.editorconfig` and rewrites code to match. Run `dotnet format --verify-no-changes` as a CI step: the build fails if someone forgot to format. That turns formatting into a machine's job, not a reviewer's.

## API Testing

Options for poking at HTTP endpoints:

- **`.http` files** live in your repo and run directly inside VS, Rider, and VS Code. Because they are versioned alongside the code, they double as executable documentation. Prefer these for team-shared, checked-in requests.
- **Postman** is the feature-rich standard: environments, scripting, collections, and mock servers, though it increasingly pushes cloud accounts.
- **Insomnia** is a lighter, cleaner alternative with good GraphQL support.
- **Bruno** stores collections as plain files in your git repo, which is a genuine advantage for versioning and avoiding vendor lock-in.

> **Tip:** For anything the team relies on, checked-in `.http` or Bruno files beat a private Postman workspace nobody else can see.

## The dotnet CLI and Global Tools

The `dotnet` CLI is the backbone of automation and CI, and it is a driver. Built-in commands ship with the SDK: `build`, `test`, `publish`, `format`, `user-secrets`, `watch`. Everything else is a **.NET tool**, a NuGet package that contains a console app, and you choose where it lives:

- **Global** (`dotnet tool install -g dotnet-ef`): installed once per user in `~/.dotnet/tools` (`%USERPROFILE%\.dotnet\tools` on Windows), which is on `PATH`. Convenient, but every machine has whatever version someone installed.
- **Local**: `dotnet new tool-manifest` creates `.config/dotnet-tools.json`; `dotnet tool install dotnet-ef` (no `-g`) pins a version in it; a fresh clone or a CI agent runs `dotnet tool restore`. Commit the manifest: the CLI runs whatever it lists.
- **One-shot** (.NET 10 SDK): `dnx dotnet-counters monitor -p 1234` (or `dotnet tool exec`) runs a tool without installing it, and honours a nearby manifest's version.

A tool whose command starts with `dotnet-` can also be called as `dotnet <rest>`, which is why `dotnet ef` looks built in and isn't.

> **Pay attention.** **Where `dotnet-counters`, `dotnet-trace` and `dotnet-dump` come from, and why they can't see your container.** They are .NET tools from NuGet, not part of the SDK or the runtime. Each one talks to a running process through the runtime's *diagnostic port*: a named pipe `dotnet-diagnostic-{pid}` on Windows, and a Unix domain socket `dotnet-diagnostic-{pid}-…-socket` in `$TMPDIR` (or `/tmp`) on Linux and macOS. A tool on the host can't find a process in a container, because that socket lives in the container's `/tmp` and the PID belongs to the container's namespace. Run the tool inside the container (the docs publish single-file builds for images without an SDK), or share `/tmp` with a sidecar. `DOTNET_EnableDiagnostics=0` closes the port, and with it every one of these tools.

## Git GUIs

The command line is essential, but a good GUI makes history, staging, and conflict resolution far clearer. **Fork** and **GitKraken** give visual branch graphs and painless interactive rebases; **lazygit** is a fast terminal UI for those who live in the shell. Use whichever helps you *understand* history, not avoid learning git.

## Diagramming

Diagrams-as-code beat drag-and-drop tools because they diff and version. **Mermaid** renders directly in GitHub/GitLab markdown, so sequence and flow diagrams live next to the code. **PlantUML** is more powerful for detailed UML. The **C4 model** (Context, Container, Component, Code) gives you a shared vocabulary for architecture at different zoom levels; tooling like Structurizr or C4-PlantUML renders it.

> **Tip:** A Mermaid sequence diagram in your README saves ten minutes of whiteboard explanation for every new joiner.

## Local Dev Tooling

Reliable local environments prevent "works on my machine." **Testcontainers** spins up real dependencies (Postgres, Redis, Kafka) in Docker for integration tests, then tears them down; no more shared, drifting test databases. **Azurite** emulates Azure Storage (Blob, Queue, Table) locally, and **LocalStack** emulates a broad range of AWS services. These let you develop and test cloud integrations offline and in CI.

> **Tip:** Testcontainers-based integration tests are one of the highest-leverage upgrades a team can make; they give near-production confidence without a shared environment.

## AI-Assisted Development

Tools like GitHub Copilot and Claude are now part of the workflow. Used well, they accelerate boilerplate, test scaffolding, unfamiliar-API exploration, and first-draft refactors. Used badly, they introduce subtle bugs, insecure patterns, and code you don't understand.

The senior mindset: **the AI drafts, you own.** Treat generated code exactly like a pull request from a fast but unvetted contributor. Read every line, question anything you can't explain, and never merge code you couldn't have written yourself. Give it context (the surrounding code, the constraints), and be specific in prompts. Watch for confidently wrong API calls, outdated patterns, and missing edge cases.

> **Tip:** If you can't explain why the AI's code works, you're not ready to merge it. Your name is on the commit, not the model's.

> **This chapter is about using tools to code faster. Chapter 18 goes much deeper on the *AI-native* workflow — agentic coding, parallel sub-agents, AFK flows — and Chapter 19 covers building AI *into* your products.**
