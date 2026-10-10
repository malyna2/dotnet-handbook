# dotnet-handbook

A self-contained .NET study handbook: English Markdown chapters in `chapters/` and Ukrainian translations in `chapters/uk/`, compiled by `build_site.py` into `main.md`, `main.uk.md` and the reader bundles `site/content.js` and `site/content.uk.js`. The reader app itself is vanilla JS in `site/` (no build step, no dependencies).

## Editing chapters

- The book is one sequence in two parts (see *Book structure* below). Files are `chapters/NNN-*.md`, auto-discovered and ordered by numeric prefix; sidebar sections come from `PART_RANGES` in `build_site.py`.
- After any chapter edit, run `python3 build_site.py` and commit the regenerated `main.md`, `main.uk.md`, `site/content.js`, `site/content.uk.js` and `chapters/uk/_sources.json` together with the source change.
- `python3 verify/check_links.py` resolves every in-book link the way the reader app does; run it after any heading or link change.
- The `_⏱ Estimated read time_` line is regenerated in the outputs on every build; keep the hand-written line in the source chapter roughly in sync when a chapter grows substantially.
- Chapter cross-links inside Markdown use the chapter's slug: `[Chapter 5: HTTP and Web APIs](#chapter-5-http-and-web-apis)` (slug = lowercased title, punctuation stripped, spaces → `-`).
- **Renaming or renumbering a chapter changes its slug.** Add the old slug to `chapters/_aliases.json` (old slug → new slug) so bookmarks, shared links and old What's New entries still open it; `check_links.py` and the site both resolve through it.

## Ukrainian translations

- The handbook and reader have English and Ukrainian editions. English chapter sources stay in `chapters/`; complete Ukrainian translations live in `chapters/uk/` with identical filenames.
- **Automatically translate every new chapter or section into Ukrainian during the same task, without waiting for a separate request.** Whenever existing reader-facing prose changes, update the corresponding Ukrainian prose too, including headings, tables, exercise answers, link labels and What's New entries.
- Translate the complete text in natural Ukrainian: never substitute summaries, omit sections, or silently leave English prose in the Ukrainian edition. Keep technical names and API identifiers unchanged.
- Preserve fenced code blocks exactly, including comments and diagrams; preserve inline code, URLs and link destinations. Keep Markdown heading levels, count and order and collapsible answer blocks. The build assigns the original English heading IDs and chapter slugs to the Ukrainian edition, so existing links and reading progress work in either language.
- Run `python3 build_site.py`, `python3 verify/check_links.py`, `python3 verify/site/build_test.py` and `node --test verify/site/*.test.cjs`. The build rejects missing translations, altered examples or structure, and English source changes whose Ukrainian file has not been updated. Do not hand-edit `chapters/uk/_sources.json` to bypass this check.
- The reader opens in the saved language, else the browser's (Ukrainian for `uk*`, English otherwise). `index.html` loads only `content.js`; `content.uk.js` is added on demand. The toggle switches the prebuilt editions locally and remembers the choice. Do not add runtime translation APIs or translate content when readers switch languages.

## Release process (pushing)

**Never push without the user explicitly asking.** The user always initiates a push; a push is a "release".

When asked to push, follow this checklist:

1. Review everything since the last release: `git log origin/main..HEAD`.
2. Update `chapters/999-whats-new.md` — add (or extend, if one already exists for this release) a section **at the top**, directly under the intro paragraph:
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
- **`> **Pay attention.**` callouts** spell out a mechanism that is the usual gap behind a wrong answer and behind the interviewer's follow-up "why?": an exception's path, a thread-injection rate, an index's sort order. Give each one a bold title, then the mechanism and the fix. Keep ordinary advice in *Best practice*, *Pitfall* or *Gotcha* callouts.
- **Exercise blocks.** Some chapters end with a `## Exercises` section in a fixed shape: *Find the bug* (a code sample with a real defect), *What would you do* (a situation plus how a senior engineer reasons about it), and *Go check* (something to run or measure in the reader's own codebase). Answers are hidden in `<details><summary>…</summary>` blocks. The site renderer escapes raw HTML by default and passes through **only** `<details>` and `<summary>` on their own lines (`render()` in `site/app.js`); keep the tags on their own lines, and don't add other raw HTML — it will render as literal text. `main.md` gets working collapsibles on GitHub for free.
- **Exercise verification.** Every C# *Find the bug* sample is compiled in `verify/exercises/ChNN/`, with a test that shows the defect on the buggy code and its absence on the fix (the suite stays green; a regression in either direction is visible). `verify/exercises/check_samples.py` proves the chapter prints the tested code, token for token. Non-C# samples are verified with the relevant tool where one runs; otherwise say so in the session report.
- **Headings inside code fences still count.** `headingOwner` in `site/app.js` scans raw lines, so a `## Context` inside a fenced template registers as a heading and can make a real section slug ambiguous across chapters, which silently breaks cross-chapter links to it. In templates, use `**Label**` lines instead of `#` headings.

## Book structure: Part 1 (Junior → Middle), then Part 2 (Middle → Senior)

- One book, read in order, one navigation. **Part 1** (`100-part-1.md`, Chapters 1–16 as `101`–`116`, the review page `199-part-1-pay-attention.md`): the key concepts and topics a developer needs to work as a solid middle without help, each *explained by its mechanism*, not listed. **Part 2** (`200-part-2.md`, Chapters 17–44 as `217`–`244`, the review page `299-…`): the same topics in depth plus the harder and wider ones (internals, scale, cross-service guarantees, cloud and Azure, AI, frontend in depth, incidents, career, the Trusted Advisor). Then the appendix (`900-`) and What's New (`999-`).
- **No repetition.** Every topic is taught in one place. Part 2 builds on Part 1 and links back to it instead of re-explaining; a Part 1 chapter may point forward to its Part 2 depth in one sentence.
- To place a new topic: internals, behaviour under load, cross-service guarantees, scale, cloud specifics, AI, or deciding for others → Part 2; a key concept a middle developer uses unaided → Part 1, explained at the level of "how it works and where the trap is".
- **Chapter shape.** An introduction (what the chapter makes you able to do, how its sections connect); the teaching sections; then, where the chapter has them, `## Prove it` (or `## Practice` in Part 2), `## Three questions`, `## Decide`, `## Check at work`, `## Exercises`, and `## Interview Questions`. These section names repeat across chapters, so nothing may link to them; link the chapter instead.
- **Prove it programs.** A ```` ```csharp ```` block whose line above names `verify/path/<Name>/Program.cs` is a program of at most 30 lines compiled there. `verify/path/verify.sh` checks the code, that every program is printed exactly once, and every link; `capture.sh` regenerates `reference-runs/`, the only source of the numbers quoted with them.
- `chapters/_notes/` and other `_`-prefixed files are working notes, ignored by the build.
- The book lists topics, never results. Keep anyone's scores, self-assessment answers, employer and other personal context out of every committed file: the repo is public.

## Labs (Practice Gym)

The plan and progress live in `PRACTICE_ROADMAP.md`; tick it at the end of every session and report what was verified and what was not.

- **Lab chapter shape**, in this order: *Goal & the senior signal it trains* · *Time budget* · *Setup* · *Tasks* as a Level 1–3 ladder with checkable acceptance criteria · *Break it* (where relevant) · *Evidence to keep* · *Interview hook* (the story as STAR + 3 likely follow-ups) · *Hints and answers* in `<details>`. Keep prose tight — labs are for doing.
- **The labs** are Chapters 19 (execution plans), 37 (story bank) and 40 (.NET health check). Their kits keep their original folders: `labs/37-execution-plans/`, `labs/36-evidence-portfolio/`, `labs/62-health-check/` (external links point at them, so don't rename them).
- **Starter kits** live in `labs/<NN-slug>/`: `README.md` (what to run, a *Last verified* line with date and environment), `docker-compose.yml` with pinned image tags, seed scripts, `starter/` (compiles; its tests fail for the intended reason), `solution/` (reference; tests pass), `tests/`, `verify.sh` (maintainer self-check that proves both), and `reference-runs/` (raw output behind every number quoted in the chapter). Code targets `net10.0`; package versions are pinned centrally in `labs/Directory.Packages.props`.
- **Link kits with absolute GitHub URLs** (`https://github.com/malyna2/dotnet-handbook/tree/main/labs/<NN-slug>`). Pages ships only `site/`, so a relative `labs/…` link 404s on the website.
- **The portfolio rule.** Every lab says, in its README and its chapter, that the reader's own solutions, plans, numbers and write-ups belong in *their own public portfolio repo*, not in this one — and that stories about a real employer stay private.
- **Numbers.** Plans, timings and counts in the text come only from real runs on the seeded data, published with an environment header (CPU, RAM, engine version, scale, cache state). Career examples (CV bullets, STAR skeletons) use `[placeholders]`, never invented metrics.
- **What's New for lab chapters** links the *chapter* slug: lab chapters share section headings ("Time budget", "Evidence to keep"), and a shared slug does not resolve across chapters.
- **Cloud sessions** start without a .NET SDK or a running Docker daemon: install `dotnet-sdk-10.0` from apt (the dotnet-install hosts are blocked) and start `dockerd`. `learn.microsoft.com` is blocked too; verify facts against the source repos it is built from (`dotnet/docs`, `dotnet/AspNetCore.Docs`, `dotnet/EntityFramework.Docs`, `dotnet/core` release notes on `raw.githubusercontent.com`), and leave a `TODO(verify)` for the user when no reachable official source confirms a fact.

## Cloud and Azure (Part 2, Chapters 28–31)

- Chapter 28 (cloud fundamentals, AWS and Azure), Chapter 29 (Azure in depth, `229-`), Chapter 30 (Azure casebook, `230-`), Chapter 31 (compliance, privacy and FinOps).
- **Code verification.** `verify/snippets/Azure/verify.sh` extracts every ```` ```csharp ```` block from Chapters 29 and 30 and compiles it against the SDK versions pinned in `verify/Directory.Packages.props`; a new block without an entry in `BLOCKS` in `extract.py` fails the check. *Find the bug* blocks must match the code in `verify/exercises/Ch51/` token for token; `ACCEPT_EULA=Y verify/exercises/Ch51/verify.sh` runs those against Azurite and the Service Bus emulator. Bicep blocks are built and linted when the Bicep CLI is on `PATH` (download it from the `Azure/bicep` GitHub releases; it is not in apt).
- Limits, defaults and dates quoted in these chapters were checked against the docs source repos (`MicrosoftDocs/azure-docs`, `azure-monitor-docs`, `azure-security-docs`, `sql-docs`) and the SDK source. Cosmos DB's docs repo is not public; its limits were confirmed only through search snippets of Learn pages. Certification facts come from third-party summaries (Learn is blocked) and are flagged in the text.

## The Trusted Advisor (Part 2, Chapters 38–43)

- Client-facing expertise: 38 point of view, 39 discovery, 40 health-check lab (`labs/62-health-check/`, lab rules apply), 41 recommendations/proposals/estimates, 42 advisory casebook, 43 positioning and public proof.
- *Find the bug* in these chapters is a flawed written artifact (memo, email, SOW excerpt, headline), not C#, so it needs no compiled verification.
- Cases and examples are labelled composites; numbers are `[placeholders]` or marked illustrative. Cite books by author and title; drop any specific claim that can't be verified. Contracts and SOWs come with a short "not legal advice" note.
