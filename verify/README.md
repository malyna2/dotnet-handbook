# verify/ — proof that the book's code samples do what the text says

Maintainer tooling, not reading material. Each folder compiles code printed in the book and, where the text claims a behavior, tests it. `check_links.py` resolves every in-book link the way the reader app does (`python3 verify/check_links.py`); `booklib.py` is the model of the book it and `path/check_path.py` share.

| Folder | What it proves | Run |
|---|---|---|
| `exercises/Ch04/` | Chapter 7's (Data Access) *Find the bug*: the summary endpoint whose lazy loading turns one request into 1,202 statements. The tests count statements on the printed code and on the fix, over HTTP on SQLite. | `exercises/Ch04/verify.sh` |
| `exercises/Ch08/` | Chapter 4's (Async Essentials) *Find the bug*: the controller that blocks on async work. Under 5 × vCPU concurrent requests the printed code starves the thread pool (seconds instead of under one, an unrelated request kept waiting, dozens of threads), and it can't be cancelled; the fix shows neither. `check_samples.py` proves the chapter prints the tested code, token for token. `capture.sh` regenerates `reference-run.txt`, the numbers the chapter quotes. | `exercises/Ch08/verify.sh` |
| `exercises/Ch51/` | Chapter 30's (the Azure casebook's) *Find the bug* samples: the lost blob update (Azurite) and the Service Bus lock expiry (Service Bus emulator). Each test shows the defect on the buggy code and its absence on the fix, so the suite stays green and a regression in either direction turns it red. | `ACCEPT_EULA=Y exercises/Ch51/verify.sh` |
| `frontend/` | The JavaScript, TypeScript and React samples in Chapters 6 and 34: each runnable sample is printed from code here and tested with Vitest (jsdom), the *Find the bug* samples on the printed code and the fix; `tsc` type-checks the rest. `check_samples.py` proves the chapters print the tested code. Samples that need a real browser or server are listed in `NOT_RUN` with the reason. | `frontend/verify.sh` (Node 22, `npm ci`) |
| `measurements/ExceptionCost/` | The exception-cost figures in Chapter 9: one throw/catch by the number of frames it crosses, with and without reading the stack trace, against returning a failure value. `capture.sh` regenerates `reference-run.txt` with its environment header. Timing only: there is nothing to assert. | `measurements/ExceptionCost/capture.sh` |
| `path/` | The *Prove it* programs at the end of the Part 1 chapters (and two in Part 2). Each program is printed in its chapter exactly as compiled here, at most 30 lines, and a test runs it as its own process and checks both halves of its output. The check also confirms each page's reading time and resolves every in-book link. | `ACCEPT_EULA=Y path/verify.sh` (or `path/verify.sh --no-docker`) |
| `snippets/Azure/` | Every C# sample in Chapters 29 and 30 (Azure in depth and the Azure casebook), extracted from the chapter Markdown, compiles against the pinned Azure SDK versions; the *Find the bug* samples match the code the exercise tests run; the Bicep sample builds and lints. Compile-only: nothing here talks to Azure. | `snippets/Azure/verify.sh` (Bicep CLI on `PATH` for the Bicep check) |

Package versions are pinned centrally in `Directory.Packages.props`; emulator images are pinned in each `docker-compose.yml`.

**Last verified:** 2026-09-24. Environment: Ubuntu 24.04 container, 4 vCPU, 15 GB RAM, .NET SDK 10.0.112, Docker 29.3.1, Azurite 3.37.0, Service Bus emulator 2.0.1 with SQL Server 2022 CU27, Bicep CLI 0.47.16.

`snippets/Azure/` (with Bicep CLI 0.47.16), `exercises/Ch04`, `Ch08` and `Ch51`, and `measurements/ExceptionCost` were last run on 2026-10-08 in the same container class, .NET SDK 10.0.112.

`path/` was last verified on 2026-10-07: same container class, .NET SDK 10.0.112, Docker 29.8.2, SQL Server 2022 CU27 and Service Bus emulator 2.0.1 (details in its README).

**Emulators are not Azure.** They are enough to show a lost update or an expired lock. They cannot show anything about identity, networking, quotas, throttling or pricing. The chapters say which claims rest on Microsoft's documentation instead of on a run here.
