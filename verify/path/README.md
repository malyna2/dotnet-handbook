# path: the *Prove it* programs of the book

Console programs, each at most 30 lines, printed in the handbook's chapters exactly as they are here. Each chapter names its program on the line above the code: `verify/path/<Name>/Program.cs`. Run one from this folder with the .NET 10 SDK:

```bash
cd verify/path
dotnet run --project AsyncVoid
```

A few experiments need SQL Server and the Service Bus emulator, which have EULAs; their folders hold a `requires-docker` marker. Start and stop the services with `ACCEPT_EULA=Y docker compose up -d` and `docker compose down` (in PowerShell, run `$env:ACCEPT_EULA="Y"` first). SQL Server is published on port 14330.

**For maintainers.**

- **`verify.sh`** checks every experiment:
  - `check_path.py`: the code in each chapter matches the `Program.cs` named above it and is at most 30 lines; every experiment folder is printed exactly once in the book;
  - `../check_links.py`: every in-book link resolves the way the reader app resolves it;
  - everything builds with warnings as errors;
  - the tests run each program as its own process and check both halves of its output.

  Run `ACCEPT_EULA=Y ./verify.sh` for everything, or `./verify.sh --no-docker` for the experiments that need only .NET.
- **Adding an experiment** needs no shared file edits: create `<Name>/<Name>.csproj` and `<Name>/Program.cs` (the test project picks every folder up by glob), add a test file under `Tests/`, print the program in its chapter under a line naming `verify/path/<Name>/Program.cs`, and add `requires-docker` or `capture.args` to the folder if it needs them.
- **`capture.sh`** regenerates `reference-runs/`, the raw output behind every number the book quotes from these programs, each file with its environment header. `collation.sql` is the SQL Server companion run it also captures: the same query under a SQL and a Windows collation.

**Last verified:** 2026-10-07, on Linux x64 only:
- hardware: 4 vCPU Intel Xeon @ 2.80 GHz, 16 GB RAM;
- software: Ubuntu 24.04.5, .NET SDK 10.0.112 (runtime 10.0.12), Docker 29.8.2;
- services: SQL Server 2022 CU27 (16.0.4295.3, `SQL_Latin1_General_CP1_CI_AS`), Service Bus emulator 2.0.1.

macOS, Windows and ARM64 were not run.
