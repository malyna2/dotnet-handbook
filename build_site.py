#!/usr/bin/env python3
"""Build the handbook outputs from chapters/*.md.

Generates:
  * site/content.js  — window.BOOK, the reader website's content bundle
  * main.md          — the whole book as one Markdown file

Auto-discovers every numbered `chapters/NN-*.md` file: to add a chapter, just
drop a file named with a numeric prefix (e.g. `117-my-topic.md`, starting with an
`# Chapter 17: ...` heading) into chapters/ and re-run this script. You do NOT
need to register it here, and you do NOT need to write a read-time line — it is
computed and injected automatically (see below).

- Ordering is by numeric prefix (0, 1, … 33, 99, 100 — numeric, so 100 sorts
  after 33, not after 10).
- Sidebar "Part" grouping is assigned by numeric range in PART_RANGES. A file
  outside every named range lands in "Additional Chapters" and is reported.
- The estimated read-time line under each chapter heading is (re)generated on
  every build from the chapter's own word count, so it is always accurate.
  Any hand-written read-time line in a source file is ignored/replaced.
- Files starting with "_" and files without a numeric prefix are ignored.
- The book is one sequence: Part 1 (files 100–199) then Part 2 (200–299), then the appendix (900)
  and What's New (999). Chapters are titled "# Chapter N: …"; the part pages "# Part 1: …" and
  the review pages "# Part N · Pay Attention To". Old chapter slugs resolve through
  chapters/_aliases.json, which is written into content.js as window.ALIASES.

Note: the prose table of contents inside chapters/00-frontmatter.md is separate
and hand-maintained; update it by hand if you want a new chapter listed there.
"""
import glob, json, os, re

ROOT = os.path.dirname(__file__)
CH_DIR = os.path.join(ROOT, "chapters")
OUT_JS = os.path.join(ROOT, "site", "content.js")
OUT_MD = os.path.join(ROOT, "main.md")

# Inclusive numeric ranges of file numbers → sidebar section label. The book is one sequence:
# Part 1 (files 100–199: the part page, Chapters 1–16 as 101–116, its review page 199), then
# Part 2 (200–299: the part page, Chapters 17–44 as 217–244, its review page 299).
PART_RANGES = [
    (0,   0,   "__home__"),
    (100, 199, "Part 1 — Junior → Middle"),
    (200, 200, "Part 2 — Middle → Senior"),
    (217, 219, "Part 2 · Runtime and Data"),
    (220, 224, "Part 2 · Distributed Systems and Architecture"),
    (225, 227, "Part 2 · Running It in Production"),
    (228, 231, "Part 2 · Cloud and Azure"),
    (232, 233, "Part 2 · AI"),
    (234, 234, "Part 2 · Frontend and Full-Stack"),
    (235, 237, "Part 2 · Incidents, Seniority and Career"),
    (238, 243, "Part 2 · Beyond Senior: The Trusted Advisor"),
    (244, 299, "Part 2 · Capstone and Review"),
    (900, 900, "Appendix"),
    (999, 10**9, "What's New"),
]
# Old chapter and section addresses -> where they live now, so old links and bookmarks still open.
ALIASES_FILE = os.path.join(CH_DIR, "_aliases.json")
DEFAULT_PART = "Additional Chapters"

def part_for(num):
    for lo, hi, label in PART_RANGES:
        if lo <= num <= hi:
            return label
    return DEFAULT_PART

def slugify(text):
    s = re.sub(r"[^\w\s-]", "", text.strip().lower())
    return re.sub(r"[\s_]+", "-", s).strip("-")

def word_stats(md):
    """Total words and words-inside-code-fences (code reads slower)."""
    in_code = total = code = 0
    for ln in md.split("\n"):
        if ln.startswith("```"):
            in_code = not in_code
            continue
        w = len(ln.split())
        total += w
        if in_code:
            code += w
    return total, total - code, code  # total, prose, code

def with_readtime(md, is_home):
    """Strip any existing read-time line and inject a freshly computed one
    right after the first H1. Home page gets none."""
    lines = [ln for ln in md.split("\n") if not ln.startswith("_⏱")]
    if is_home:
        return "\n".join(lines)
    total, prose, code = word_stats("\n".join(lines))
    mins = (prose * 10 // 200 + code * 10 // 60 + 5) // 10  # ~200 wpm prose, ~60 wpm code
    mins = max(5, int(round(mins / 5.0)) * 5)              # round to the nearest 5 minutes
    if mins >= 60:
        h, m = divmod(mins, 60)
        label = ("%d h" % h) + (" %d min" % m if m else "")
    else:
        label = "%d min" % mins
    rt = "_⏱️ Estimated read time: ~%s · %d words (study pace)_" % (label, total)
    out, injected = [], False
    for ln in lines:
        out.append(ln)
        if not injected and re.match(r"^#\s+", ln):
            # normalize spacing: exactly one blank line, then the read-time line
            rest = lines[len(out):]
            while rest and rest[0].strip() == "":
                rest.pop(0)
            return "\n".join(out + ["", rt, ""] + rest)
    return "\n".join([rt, ""] + lines)  # no H1 found (shouldn't happen)

# --- discover chapter files -------------------------------------------------
entries = []
for path in glob.glob(os.path.join(CH_DIR, "*.md")):
    stem = os.path.basename(path)[:-3]
    if stem.startswith("_"):
        continue
    m = re.match(r"^(\d+)", stem)
    if not m:
        continue
    entries.append((int(m.group(1)), stem, path))
entries.sort(key=lambda e: (e[0], e[1]))

# --- build ------------------------------------------------------------------
book, md_parts, uncategorized = [], [], []
for num, stem, path in entries:
    with open(path, encoding="utf-8") as f:
        raw = f.read()
    is_home = (stem == "00-frontmatter" or num == 0)
    # The What's New changelog gets no read-time line — it's not study material.
    md = with_readtime(raw, is_home or "whats-new" in stem)

    m = re.search(r"^#\s+(.+)$", md, re.MULTILINE)
    title = m.group(1).strip() if m else stem
    nav = re.sub(r"^Chapter\s+\d+:\s*", "", title)
    nav = re.sub(r"^Appendix\s+([A-Z]):\s*", r"App. \1: ", nav)
    part = part_for(num)
    group = "book"
    m_ch = re.match(r"^Chapter\s+(\d+)\b", title)
    num_label = m_ch.group(1) if m_ch else ""
    if re.match(r"^Part\s+\d+:", title):
        nav = title
    elif re.search(r"Pay Attention To$", title):
        nav = "Pay attention to"
    elif title.startswith("Appendix:"):
        nav, num_label = title[len("Appendix:"):].strip(), "App"
    if is_home:
        nav = "Preface & Contents"
        title = "The Middle → Senior .NET Developer Handbook"
        part, group = "__home__", "home"
    if part == DEFAULT_PART:
        uncategorized.append(stem)

    book.append({"id": stem, "slug": slugify(title), "title": title, "nav": nav,
                 "part": part, "group": group, "num": num_label, "md": md})
    md_parts.append(md)

os.makedirs(os.path.dirname(OUT_JS), exist_ok=True)
with open(OUT_JS, "w", encoding="utf-8") as f:
    f.write("window.BOOK = ")
    json.dump(book, f, ensure_ascii=False)
    f.write(";\nwindow.ALIASES = ")
    json.dump(json.load(open(ALIASES_FILE, encoding="utf-8")) if os.path.exists(ALIASES_FILE) else {}, f, ensure_ascii=False)
    f.write(";\n")

with open(OUT_MD, "w", encoding="utf-8") as f:
    f.write("\n\n---\n\n".join(md_parts) + "\n\n---\n\n")

print("Wrote %s — %d chapters, %d KB" % (OUT_JS, len(book), os.path.getsize(OUT_JS) / 1024))
print("Wrote %s — %d words" % (OUT_MD, sum(len(p.split()) for p in md_parts)))
if uncategorized:
    print("  ! Uncategorized (in '%s' — add a range in PART_RANGES): %s"
          % (DEFAULT_PART, ", ".join(uncategorized)))
