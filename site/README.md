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

## Translation (English → Ukrainian)

- **Click any sentence** to translate it inline. Click again to hide it.
- **Or select any text** (a phrase, a few words) and press the floating **🇺🇦 Translate** button.
- Translations use the free **MyMemory** API and are **cached** in your browser, so re-reading is instant and doesn't use quota.

### Raising the daily limit
Anonymous MyMemory use is rate-limited (~a few thousand words/day). Click the **🇺🇦 UA** button (top right) and enter your email to raise the limit substantially. You can also switch the target language (Polish, German, Spanish, French, Russian) there.

> **Note:** translation needs an internet connection at read time (the site itself is fully offline; only the translate calls go out). If you hit the limit, you'll see a message — add your email or try again the next day.

## Regenerating the content

If you edit the chapters in `../chapters/`, rebuild the bundle:
```bash
python3 ../build_site.py
```
This regenerates `content.js` from the Markdown files.

## Files
- `index.html` — the shell
- `style.css` — all styling (responsive, theming)
- `app.js` — Markdown renderer, highlighter, navigation, search, translation
- `content.js` — the book content (generated from `../chapters/*.md`)
