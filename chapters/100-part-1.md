# Part 1: Junior → Middle

> **What Part 1 makes you able to do.** Work as a solid middle developer inside one service, without help: write async, data-access and messaging code that survives production, explain the mechanism behind each classic trap and the fix for it, find the first cause of a slow or failing endpoint yourself, and turn vague tickets, estimates and reviews into work the team can rely on.

**Time:** the core modules take ≈ 3 h 35 min of reading in the linked chapter sections and ≈ 12 h 40 min of hands-on work; with a second read after the experiments, about 20 hours, or four weeks at five hours a week. The foundation modules add up to ≈ 2 h 25 min of reading and 4 h 40 min of hands-on work, but only for the modules whose entry check sends you back.

## How Part 1 Works

Part 1 is a path through the chapters, not a second copy of them. Each module names the mechanism that ties its topic together, links the chapter sections that teach it, and then makes you use it:

1. **Read the mechanism first,** then the linked sections, in order.
2. **Run the experiment before reading what it shows.** Every *Prove it* program is at most 30 lines and is compiled and tested in [`verify/path`](https://github.com/malyna2/dotnet-handbook/tree/main/verify/path): clone the repository, install the .NET 10 SDK, and run `dotnet run --project <Name>` from that folder. Two experiments also need Docker for SQL Server or the Service Bus emulator.
3. **Answer the three questions without notes,** then open the answers. A "why?" you can't answer is the gap an interviewer's follow-up finds.
4. **Do the check at work.** The module is done when you have found the trap, or proved it absent, in code you own.

**Modules 1–7 are the core:** async code, data access, messaging, the working habits a team expects from a middle developer, diagnosis and the C# underneath it all. Take them in order. **Modules 8–13 are foundations:** each opens with an entry check of three questions. Get all three right and move on; otherwise, work through the module.

Before an interview, reread [Part 1 · Pay Attention To](#part-1-pay-attention-to): one table of trap → why it bites → the fix.

## The Modules of Part 1

| # | Module | Kind | Reading | Hands-on |
|---|---|---|---|---|
| 1 | [Async Essentials](#part-1-module-1-async-essentials) | Core | 35 min | 1 h 35 min |
| 2 | [EF Core Essentials](#part-1-module-2-ef-core-essentials) | Core | 15 min | 1 h 10 min |
| 3 | [SQL and Indexes](#part-1-module-3-sql-and-indexes) | Core | 20 min | 4 h 10 min |
| 4 | [Messaging and Long-Running Work](#part-1-module-4-messaging-and-long-running-work) | Core | 40 min | 1 h 25 min |
| 5 | [Working Like a Middle Developer](#part-1-module-5-working-like-a-middle-developer) | Core | 40 min | 2 h 15 min |
| 6 | [Exceptions, Logging and First Diagnosis](#part-1-module-6-exceptions-logging-and-first-diagnosis) | Core | 40 min | 1 h 5 min |
| 7 | [C# Essentials](#part-1-module-7-c-essentials) | Core | 25 min | 1 h |
| 8 | [Web API Basics](#part-1-module-8-web-api-basics) | Foundation | 30 min | 50 min |
| 9 | [Testing Essentials](#part-1-module-9-testing-essentials) | Foundation | 25 min | 45 min |
| 10 | [Design Basics](#part-1-module-10-design-basics) | Foundation | 35 min | 35 min |
| 11 | [Git and Everyday Tooling](#part-1-module-11-git-and-everyday-tooling) | Foundation | 15 min | 50 min |
| 12 | [Security Essentials](#part-1-module-12-security-essentials) | Foundation | 20 min | 50 min |
| 13 | [Dates, Money and Strings](#part-1-module-13-dates-money-and-strings) | Foundation | 20 min | 50 min |

## Where Part 2 Takes Over

Part 1 stops at the boundary of one service and at the mechanism you need to get the code right. [Part 2](#part-2-middle-senior) starts where the questions become *why does it behave like this under load, across services and over time, and what should the team choose?*: thread-pool and GC internals, isolation levels and plan regressions, outbox relays and ordering across consumers, architecture and system design, observability, production and the decisions a senior is trusted with.
