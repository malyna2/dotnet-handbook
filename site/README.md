# .NET Handbook — offline reader website

A self-contained, responsive website for the handbook. No build step, no server, no external dependencies.

## How to open

**Just double-click `index.html`** — it opens in your default browser and works from the local file.

> If your browser is strict about local files, run a tiny local server instead:
> ```bash
> cd site
> python3 -m http.server 8000
> # then open http://localhost:8000
> ```

## Features

- **Responsive** layout — sidebar collapses to a ☰ menu on phones/tablets.
- **Navigation** — full chapter list grouped by Part, plus a per-page section outline (right rail on desktop) and Prev/Next pager.
- **Search** — type in the top bar to search titles and full text.
- **Code blocks** — syntax highlighting for C#/bash/YAML, with a Copy button.
- **Light / dark / auto theme** — toggle with 🌓 (top right).
- **Reading progress bar** and scroll-spy outline.

## English / Ukrainian editions

- The **EN / УКР** switch changes the entire site: chapter prose, navigation, search, buttons, reading-time labels and release notes.
- Ukrainian is the default. The reader remembers your choice in `localStorage` under `site_lang`.
- Both editions are included locally; switching works offline and makes no translation API requests.
- Chapter URLs, section anchors, reading progress and release read marks are shared between languages. Your open chapter and reading position are preserved when switching.

### Updating translations
English source chapters live in `../chapters/`; complete Ukrainian translations use the same filenames in `../chapters/uk/`. Add and update both editions together, preserving heading structure, code samples and link destinations. Translation work uses GPT-6 Luna subagents, or the latest Haiku in Claude Code, as described in `../CLAUDE.md`.

The build rejects missing translations, altered examples or heading structure, and source changes without a translation update. `../chapters/uk/_sources.json` is generated freshness metadata; do not edit it by hand.

## Regenerating the content

If you edit the chapters in `../chapters/`, rebuild the bundle:
```bash
python3 ../build_site.py
```
This regenerates `content.js`, `content.uk.js`, `../main.md` and `../main.uk.md`. From the repository root, also run `python3 verify/check_links.py`, `python3 verify/site/build_test.py` and `node --test verify/site/*.test.cjs`.

## Files
- `index.html` — the shell
- `style.css` — all styling (responsive, theming)
- `app.js` — Markdown renderer, highlighter, navigation, search, language switching
- `content.js` — the book content (generated from `../chapters/*.md`)
- `content.uk.js` — Ukrainian content with the same routes and heading IDs
