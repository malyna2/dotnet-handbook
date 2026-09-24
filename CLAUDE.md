# dotnet-handbook

A self-contained .NET study handbook: Markdown chapters in `chapters/`, compiled by `build_site.py` into `main.md` (single-file book) and `site/content.js` (the reader web app's content bundle). The reader app itself is vanilla JS in `site/` (no build step, no dependencies).

## Editing chapters

- Chapters are `chapters/NN-*.md`, auto-discovered by numeric prefix; sidebar grouping comes from `PART_RANGES` in `build_site.py`.
- After any chapter edit, run `python3 build_site.py` and commit the regenerated `main.md` and `site/content.js` together with the source change.
- The `_⏱ Estimated read time_` line is regenerated in the outputs on every build; keep the hand-written line in the source chapter roughly in sync when a chapter grows substantially.
- Chapter cross-links inside Markdown use the chapter's slug: `[Chapter 3: ...](#chapter-3-aspnet-core-web-apis)` (slug = lowercased title, punctuation stripped, spaces → `-`).

## Release process (pushing)

**Never push without the user explicitly asking.** The user always initiates a push; a push is a "release".

When asked to push, follow this checklist:

1. Review everything since the last release: `git log origin/main..HEAD`.
2. Update `chapters/101-whats-new.md` — add (or extend, if one already exists for this release) a section **at the top**, directly under the intro paragraph:
   - Heading format matters — the site parses it: `## Release — <Month D, YYYY>`.
   - First a `**🔧 Site & functionality**` bullet list for reader-app/build changes: plain static text, no links (these get no read-tracking).
   - Then a `**📖 Content updates**` bullet list: **one bullet per meaningful commit in the push** — every content commit gets its own line so the release mirrors the whole group of commits, not a merged summary. Format: `[<Chapter N: topic>](#<chapter-slug>) — <one short sentence>`. Several bullets may link to the same chapter (read-tracking keys are per-link-position, so they tick off independently).
   - Descriptions must be a single compact sentence, not a paragraph.
   - Skip only trivial commits (typo fixes, header syncs) — the changelog is for readers, not a git log mirror.
3. Rebuild (`python3 build_site.py`), commit the What's New update, then push.

How the feature works (for debugging): the site shows the **topmost** `## Release` section in a popup once per user. localStorage `wn_seen` stores `"<heading>|<content hash>"` — so *amending* a release re-triggers the popup for everyone. `wn_read` stores clicked chapter-link keys `"<release heading>|<slug>|<position>"` — so reordering bullets in a published release resets ✓ marks (append instead when amending). Logic lives at the end of `site/app.js` (`wnLatest`/`wnDecorate`/`wnShow`).

## Conventions

- Book voice: senior-engineer prose, `> **Best practice.**` / `> **Pitfall.**` / `> **Gotcha.**` callouts, ASCII diagrams, decision tables for "which tool when" questions, cross-references between chapters by chapter number.
- Prefer explaining the *mechanism* behind a claim over adding more claims.
- **Exercise blocks.** Some chapters end with a `## Exercises` section in a fixed shape: *Find the bug* (a code sample with a real defect), *What would you do* (a situation plus how a senior engineer reasons about it), and *Go check* (something to run or measure in the reader's own codebase). Answers are hidden in `<details><summary>…</summary>` blocks. The site renderer escapes raw HTML by default and passes through **only** `<details>` and `<summary>` on their own lines (`render()` in `site/app.js`); keep the tags on their own lines, and don't add other raw HTML — it will render as literal text. `main.md` gets working collapsibles on GitHub for free.
- **Exercise verification.** Every C# *Find the bug* sample is compiled in `verify/exercises/ChNN/`, with a test that shows the defect on the buggy code and its absence on the fix (the suite stays green; a regression in either direction is visible). Non-C# samples are verified with the relevant tool where one runs; otherwise say so in the session report.
- **Headings inside code fences still count.** `headingOwner` in `site/app.js` scans raw lines, so a `## Context` inside a fenced template registers as a heading and can make a real section slug ambiguous across chapters, which silently breaks cross-chapter links to it. In templates, use `**Label**` lines instead of `#` headings.

## Practice Gym (Part XI, chapters 36–49)

The plan and progress live in `PRACTICE_ROADMAP.md`; tick it at the end of every session and report what was verified and what was not.

- **Lab chapter shape**, in this order: *Goal & the senior signal it trains* · *Time budget* · *Setup* · *Tasks* as a Level 1–3 ladder with checkable acceptance criteria · *Break it* (where relevant) · *Evidence to keep* · *Interview hook* (the story as STAR + 3 likely follow-ups) · *Hints and answers* in `<details>`. Keep prose tight — labs are for doing.
- **Starter kits** live in `labs/<NN-slug>/` (same `NN` as the chapter): `README.md` (what to run, a *Last verified* line with date and environment), `docker-compose.yml` with pinned image tags, seed scripts, `starter/` (compiles; its tests fail for the intended reason), `solution/` (reference; tests pass), `tests/`, `verify.sh` (maintainer self-check that proves both), and `reference-runs/` (raw output behind every number quoted in the chapter). Code targets `net10.0`; package versions are pinned centrally in `labs/Directory.Packages.props`.
- **Link kits with absolute GitHub URLs** (`https://github.com/malyna2/dotnet-handbook/tree/main/labs/<NN-slug>`). Pages ships only `site/`, so a relative `labs/…` link 404s on the website.
- **The portfolio rule.** Every lab says, in its README and its chapter, that the reader's own solutions, plans, numbers and write-ups belong in *their own public portfolio repo*, not in this one — and that stories about a real employer stay private.
- **Numbers.** Plans, timings and counts in the text come only from real runs on the seeded data, published with an environment header (CPU, RAM, engine version, scale, cache state). Career examples (CV bullets, STAR skeletons) use `[placeholders]`, never invented metrics.
- **What's New for lab chapters** links the *chapter* slug: lab chapters share section headings ("Time budget", "Evidence to keep"), and a shared slug does not resolve across chapters.
- **Cloud sessions** start without a .NET SDK or a running Docker daemon: install `dotnet-sdk-10.0` from apt (the dotnet-install hosts are blocked) and start `dockerd`. `learn.microsoft.com` is blocked too; verify facts against the source repos it is built from (`dotnet/docs`, `dotnet/AspNetCore.Docs`, `dotnet/EntityFramework.Docs`, `dotnet/core` release notes on `raw.githubusercontent.com`), and leave a `TODO(verify)` for the user when no reachable official source confirms a fact.

## Cloud in Depth (Part XII, chapters 50+)

- Chapter 50 (Azure in depth) and Chapter 51 (Azure casebook). Chapter numbers 38–49 stay free for Practice Gym labs.
- **Code verification.** `verify/snippets/Azure/verify.sh` extracts every ```` ```csharp ```` block from these chapters and compiles it against the SDK versions pinned in `verify/Directory.Packages.props`; a new block without an entry in `BLOCKS` in `extract.py` fails the check. *Find the bug* blocks must match the code in `verify/exercises/Ch51/` token for token; `ACCEPT_EULA=Y verify/exercises/Ch51/verify.sh` runs those against Azurite and the Service Bus emulator. Bicep blocks are built and linted when the Bicep CLI is on `PATH` (download it from the `Azure/bicep` GitHub releases; it is not in apt).
- Limits, defaults and dates quoted in these chapters were checked against the docs source repos (`MicrosoftDocs/azure-docs`, `azure-monitor-docs`, `azure-security-docs`, `sql-docs`) and the SDK source. Cosmos DB's docs repo is not public; its limits were confirmed only through search snippets of Learn pages. Certification facts come from third-party summaries (Learn is blocked) and are flagged in the text.
