# study-track: the *Prove it* experiments of `STUDY_TRACK.md`

Seven console programs, each at most 30 lines, printed in [`STUDY_TRACK.md`](../../STUDY_TRACK.md) exactly as they are here. Run them from this folder with the .NET 10 SDK.

| Experiment | Track topic | What you see | Needs |
|---|---|---|---|
| `dotnet run --project AsyncVoid` | 1a | The caller's `catch` misses an `async void` exception, and the process dies on a thread-pool thread | — |
| `dotnet run --project WhenAll` | 1b | `await Task.WhenAll` throws one exception; the task holds both | — |
| `dotnet run --project Starvation -- await`, then `-- block` | 1c | The same 200 one-second requests: about 1 s on a handful of threads, against about 11 s and nearly 70 | — |
| `dotnet run --project HttpClientPerRequest` | 1d | 0 against 500 sockets left in `TIME_WAIT` | — |
| `dotnet run --project PeekLock` | 2 | A handler that outlives its lock gets the same message again, and `Complete` throws `MessageLockLost` | Docker |
| `dotnet run --project SeekVsScan` | 3 | Which predicates seek an index and which scan it, with logical reads | Docker |
| `dotnet run --project CheckThenAct` | 4 | A check-then-act dedup charges twice; claim-first charges once | — |

The two Docker experiments need SQL Server and the Service Bus emulator, which have EULAs. Start and stop them with `ACCEPT_EULA=Y docker compose up -d` and `docker compose down`. In PowerShell, run `$env:ACCEPT_EULA="Y"` first. SQL Server is published on port 14330.

**For maintainers.**

- **`verify.sh`** checks every experiment:
  - the code in `STUDY_TRACK.md` matches the `Program.cs` here and is at most 30 lines;
  - every link in `STUDY_TRACK.md` resolves (GitHub heading anchors included);
  - everything builds with warnings as errors;
  - the tests run each program as its own process and check both halves of its output.

  Run `ACCEPT_EULA=Y ./verify.sh` for everything, or `./verify.sh --no-docker` for the five experiments that need only .NET.
- **`capture.sh`** regenerates `reference-runs/`, the raw output behind every number the track quotes, each file with its environment header. `collation.sql` is the SQL Server companion run it also captures: the same query under a SQL and a Windows collation.

**Last verified:** 2026-10-07, on Linux x64 only:
- hardware: 4 vCPU Intel Xeon @ 2.80 GHz, 16 GB RAM;
- software: Ubuntu 24.04.5, .NET SDK 10.0.112 (runtime 10.0.12), Docker 29.8.2;
- services: SQL Server 2022 CU27 (16.0.4295.3, `SQL_Latin1_General_CP1_CI_AS`), Service Bus emulator 2.0.1.

macOS, Windows and ARM64 were not run.
