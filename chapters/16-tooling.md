# Chapter 16: Tooling & Productivity

_⏱️ Estimated read time: ~5 min ·     1065 words (study pace)_

A senior developer is not just someone who writes good code. It is someone whose *environment* multiplies their output. The tools below are the ones you will actually reach for on a modern .NET team. You don't need all of them, but you should know what each solves so you can pick deliberately rather than by habit.

## IDEs: Visual Studio, Rider, and VS Code

The three mainstream choices trade off differently.

**Visual Studio** (Windows) is the heavyweight. Its debugger is best-in-class, especially for tricky scenarios: mixed-mode debugging, memory dumps, IntelliTrace, and the diagnostic tooling for CPU and allocation profiling. If you work on WPF/WinForms, complex MSBuild setups, or need the deepest debugging experience, it is hard to beat. The cost is that it is heavy and Windows-only.

**JetBrains Rider** is cross-platform and, for many, the day-to-day sweet spot. It combines ReSharper's analysis engine with a fast solution model, excellent refactoring, and strong support for EF Core, Docker, databases, and unit test runners in one product. Rider tends to feel snappier than VS on large solutions and works identically on macOS, Linux, and Windows.

**VS Code + C# Dev Kit** is the lightweight, extensible option. Backed by the Roslyn language server, C# Dev Kit gives you a solution explorer, test integration, and IntelliSense that is now genuinely good for everyday work. It shines for microservices, polyglot repos (a .NET API next to a React front end), and remote/container development via Dev Containers and SSH.

> **Tip:** Match the tool to the task, not to tribal loyalty. Many seniors keep VS Code open for quick edits and scripts, and reach for Rider or Visual Studio when they need heavy refactoring or serious debugging.

## Refactoring & Linting

Code quality tooling in .NET now layers nicely:

- **Roslyn analyzers** run inside the compiler. They ship with the SDK (the `CAxxxx` rules), come from NuGet packages, and can be authored in-house. They surface issues as build warnings, so they integrate with CI for free.
- **`.editorconfig`** is the single source of truth for style. It travels with the repo, is understood by VS, Rider, and `dotnet format`, and lets you set naming conventions, `var` usage, and analyzer severities per folder.
- **ReSharper** (VS plugin) adds deeper inspections, bulk refactorings, and code cleanup profiles beyond what ships in the box.
- **StyleCop.Analyzers** enforces consistent layout and documentation conventions.
- **SonarLint / SonarQube** catches bugs, security hotspots, and code smells, and its server component tracks quality trends and "new code" gates across the team.

> **Tip:** Commit an `.editorconfig` early and raise a few key analyzer rules to `error` (e.g. `dotnet_diagnostic.CA2007.severity` in library code). Warnings get ignored; build-breaking errors get fixed.

## Formatting in CI

Style debates waste review time. Kill them with automation. `dotnet format` reads your `.editorconfig` and rewrites code to match. Run `dotnet format --verify-no-changes` as a CI step: the build fails if someone forgot to format. That turns formatting into a machine's job, not a reviewer's.

## API Testing

You will constantly poke at HTTP endpoints. Options:

- **`.http` files** live in your repo and run directly inside VS, Rider, and VS Code. Because they are versioned alongside the code, they double as executable documentation. Prefer these for team-shared, checked-in requests.
- **Postman** is the feature-rich standard: environments, scripting, collections, and mock servers, though it increasingly pushes cloud accounts.
- **Insomnia** is a lighter, cleaner alternative with good GraphQL support.
- **Bruno** stores collections as plain files in your git repo, which is a genuine advantage for versioning and avoiding vendor lock-in.

> **Tip:** For anything the team relies on, checked-in `.http` or Bruno files beat a private Postman workspace nobody else can see.

## The dotnet CLI and Global Tools

The `dotnet` CLI is the backbone of automation and CI. Beyond `build`, `test`, and `publish`, learn `dotnet user-secrets`, `dotnet ef`, and `dotnet watch` for a fast inner loop. Global tools (`dotnet tool install -g`) give you reusable utilities; a `dotnet-tools.json` manifest with `dotnet tool restore` pins tool versions per repo so everyone runs the same ones.

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

> **This chapter is about using tools to code faster. Chapter 18 goes much deeper on the *AI-native* workflow — agentic coding, parallel sub-agents, AFK flows — and on building AI *into* your products.**
