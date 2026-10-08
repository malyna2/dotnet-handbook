# Part 2: Middle → Senior

> **What Part 2 makes you able to do.** Explain why the platform behaves the way it does under load, keep data and messages consistent across services and over time, choose an architecture and defend its trade-offs, run what you build in production, and make the decisions a team trusts a senior with: what to build, what to defer, and what to write down.

**Time:** reading ≈ 6 h 45 min of linked chapter sections, hands-on ≈ 38 h, labs included; with a second read, about 52 hours, or ten to eleven weeks at five hours a week. The labs are most of it: take them in the order of the modules, or start with the module your work needs now.

## How Part 2 Works

Part 2 assumes [Part 1](#part-1-junior-middle): it doesn't repeat what `await` does, why a queue redelivers or what an index seek is. Each module goes one level down (the internals behind Part 1's rules) and one level out (the same mechanism across services, under load and over months), then asks you to decide.

1. **Read the mechanism, then the linked sections.**
2. **Practise.** Part 2 practises on labs, exercises and your own system rather than on 30-line programs: the slow-query lab, the emulator-verified Service Bus exercises, an instrumented service, an ADR.
3. **Answer the three questions without notes.** They are the follow-ups a senior is asked: *why does it break, what would you watch, what would you choose.*
4. **Decide.** Each module ends with a situation that has two or three defensible answers. Write your choice and the condition that would change it before you open the reasoning.
5. **Do the check at work.**

Your own write-ups, decisions and evidence belong in your own public portfolio repository, not in this one, and stories about a real employer stay private.

Before an interview, reread [Part 2 · Pay Attention To](#part-2-pay-attention-to).

## The Modules of Part 2

| # | Module | Reading | Hands-on |
|---|---|---|---|
| 1 | [Runtime and Concurrency Internals](#part-2-module-1-runtime-and-concurrency-internals) | 45 min | 2 h 25 min |
| 2 | [Data in Depth](#part-2-module-2-data-in-depth) | 1 h 5 min | 7 h 40 min |
| 3 | [Distributed Consistency](#part-2-module-3-distributed-consistency) | 45 min | 3 h 50 min |
| 4 | [Architecture, API Evolution and System Design](#part-2-module-4-architecture-api-evolution-and-system-design) | 1 h | 3 h 50 min |
| 5 | [Observability and Testing at Scale](#part-2-module-5-observability-and-testing-at-scale) | 55 min | 6 h 35 min |
| 6 | [Production and the Cloud](#part-2-module-6-production-and-the-cloud) | 1 h 30 min | 5 h 35 min |
| 7 | [Senior Behaviours](#part-2-module-7-senior-behaviours) | 45 min | 8 h 20 min |

## Beyond Part 2

The chapters Part 2 does not route through are still worth reading when your work needs them: specialised testing, compliance and cost, front-end work, AI systems, and the Trusted Advisor part for client-facing work. The [Full book](#the-middle-senior-net-developer-handbook) tab lists every chapter.
