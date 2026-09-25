# verify/ — proof that the book's code samples do what the text says

Maintainer tooling, not reading material. Each folder compiles code printed in a chapter and, where the chapter claims a behavior, tests it.

| Folder | What it proves | Run |
|---|---|---|
| `exercises/Ch51/` | Chapter 51's *Find the bug* samples: the lost blob update (Azurite) and the Service Bus lock expiry (Service Bus emulator). Each test shows the defect on the buggy code and its absence on the fix, so the suite stays green and a regression in either direction turns it red. | `ACCEPT_EULA=Y exercises/Ch51/verify.sh` |
| `snippets/Azure/` | Every C# sample in Chapters 50 and 51, extracted from the chapter Markdown, compiles against the pinned Azure SDK versions; the *Find the bug* samples match the code the exercise tests run; the Bicep sample builds and lints. Compile-only: nothing here talks to Azure. | `snippets/Azure/verify.sh` (Bicep CLI on `PATH` for the Bicep check) |

Package versions are pinned centrally in `Directory.Packages.props`; emulator images are pinned in each `docker-compose.yml`.

**Last verified:** 2026-09-24. Environment: Ubuntu 24.04 container, 4 vCPU, 15 GB RAM, .NET SDK 10.0.112, Docker 29.3.1, Azurite 3.37.0, Service Bus emulator 2.0.1 with SQL Server 2022 CU27, Bicep CLI 0.47.16.

**Emulators are not Azure.** They are enough to show a lost update or an expired lock. They cannot show anything about identity, networking, quotas, throttling or pricing. The chapters say which claims rest on Microsoft's documentation instead of on a run here.
