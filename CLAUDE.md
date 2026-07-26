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
   - Then a `**📖 Content updates**` bullet list: one bullet per changed chapter, formatted as `[<full chapter title>](#<chapter-slug>) — <short description of what changed>`. These links get the popup + per-user read-tracking (✓ marks) automatically.
   - Skip trivial commits (typo fixes, header syncs) — the changelog is for readers, not a git log mirror.
3. Rebuild (`python3 build_site.py`), commit the What's New update, then push.

How the feature works (for debugging): the site shows the **topmost** `## Release` section in a popup once per user (localStorage `wn_seen` stores the release heading; `wn_read` stores clicked chapter-link keys `"<release heading>|<slug>"`). Logic lives at the end of `site/app.js` (`wnLatest`/`wnDecorate`/`wnShow`).

## Conventions

- Book voice: senior-engineer prose, `> **Best practice.**` / `> **Pitfall.**` / `> **Gotcha.**` callouts, ASCII diagrams, decision tables for "which tool when" questions, cross-references between chapters by chapter number.
- Prefer explaining the *mechanism* behind a claim over adding more claims.
