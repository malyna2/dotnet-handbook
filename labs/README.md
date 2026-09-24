# The Practice Gym — lab kits

Starter kits for **Part XI — The Practice Gym** of *The Middle → Senior .NET Developer Handbook*. Each folder belongs to the chapter with the same number; read the chapter first, then work here.

| Kit | Chapter | What you produce |
|---|---|---|
| [`36-evidence-portfolio/`](36-evidence-portfolio/) | Chapter 36: The Story Bank & Evidence Portfolio | A private story bank, a weekly brag doc, a public portfolio repo, and scored mock interviews |

More kits land here as their chapters are written.

## The portfolio rule

**Your solutions do not belong in this repository.** Your query plans, test runs, post-mortems, review comments, design docs and rewritten prose go in **your own public portfolio repo**, where an interviewer can read them. That is the whole point of doing the labs: leaving evidence behind.

Keep one distinction: **lab artifacts are public; stories about a real employer are private.** Anything that names a customer, a colleague, an internal system or a number your employer has not published stays in a private repo or a private notes folder.

## Prerequisites

The code labs need:

- **Docker** with Compose v2 (`docker compose version`).
- **.NET 10 SDK** (`dotnet --version` → `10.0.x`). .NET 10 is the current LTS; .NET 8 and 9 leave support on 10 November 2026.
- About 8 GB of free RAM and 20 GB of free disk for the database labs.

The writing and interview kits need nothing but a text editor and, for the mock interviews, any capable AI chat assistant.

## Layout of a code kit

```
<NN-slug>/
  README.md          what to run, and when it was last verified
  docker-compose.yml pinned image tags
  starter/           what you work on — compiles; its tests fail until you fix it
  solution/          a reference solution — don't open it until you are done
  tests/             the acceptance tests you must turn green
  verify.sh          the maintainers' self-check (starter fails as intended, solution passes)
  reference-runs/    raw output behind every number quoted in the chapter
```
