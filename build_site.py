#!/usr/bin/env python3
"""Bundle the chapter markdown files into site/content.js as window.BOOK.

Auto-discovers every numbered `chapters/NN-*.md` file: no need to register a
new chapter here — just drop a file named with a numeric prefix (e.g.
`34-my-topic.md`) into chapters/ and re-run this script.

- Ordering is by the numeric prefix (0, 1, 2, … 33, 99, 100 — numeric, so 100
  correctly sorts after 33, not after 10).
- The sidebar "Part" grouping is assigned by numeric range in PART_RANGES below.
  A file whose number falls outside every named range lands in "Additional
  Chapters" (and is reported when you run this) until you slot it into a range.
- Files starting with "_" (scratch/insert files) and any file without a numeric
  prefix are ignored.

Note: this generates the website's navigation (content.js). The manual table of
contents inside chapters/00-frontmatter.md is separate — update it by hand if you
want a new chapter listed there too.
"""
import glob, json, os, re

CH_DIR = os.path.join(os.path.dirname(__file__), "chapters")
OUT = os.path.join(os.path.dirname(__file__), "site", "content.js")

# Inclusive numeric ranges → sidebar Part label. Add or adjust ranges to
# re-group chapters. "__home__" is the special landing page (chapter 0).
PART_RANGES = [
    (0,   0,   "__home__"),
    (1,   4,   "Part I — The Language & the Platform"),
    (5,   8,   "Part II — Designing Software That Lasts"),
    (9,   12,  "Part III — Distributed Systems & the Cloud"),
    (13,  15,  "Part IV — Running Software in Production"),
    (16,  18,  "Part V — The Craft & the AI Era"),
    (19,  24,  "Part VI — Deepening the Backend"),
    (25,  30,  "Part VII — Foundations, Governance & Specializations"),
    (31,  31,  "Part VIII — Capstone"),
    (32,  33,  "Part IX — The War Room: Scenarios & Interviews"),
    (99,  10**9, "Appendices"),
]
DEFAULT_PART = "Additional Chapters"

def part_for(num):
    for lo, hi, label in PART_RANGES:
        if lo <= num <= hi:
            return label
    return DEFAULT_PART

def slugify(text):
    s = text.strip().lower()
    s = re.sub(r"[^\w\s-]", "", s)
    s = re.sub(r"[\s_]+", "-", s)
    return s.strip("-")

# --- discover chapter files -------------------------------------------------
entries = []
for path in glob.glob(os.path.join(CH_DIR, "*.md")):
    stem = os.path.basename(path)[:-3]
    if stem.startswith("_"):
        continue
    m = re.match(r"^(\d+)", stem)
    if not m:
        continue  # skip files without a numeric prefix
    entries.append((int(m.group(1)), stem, path))
entries.sort(key=lambda e: (e[0], e[1]))

# --- build the book ---------------------------------------------------------
book = []
uncategorized = []
for num, stem, path in entries:
    with open(path, encoding="utf-8") as f:
        md = f.read()
    m = re.search(r"^#\s+(.+)$", md, re.MULTILINE)
    title = m.group(1).strip() if m else stem
    nav = re.sub(r"^Chapter\s+\d+:\s*", "", title)
    nav = re.sub(r"^Appendix\s+([A-Z]):\s*", r"App. \1: ", nav)
    part = part_for(num)
    if stem == "00-frontmatter" or num == 0:
        nav = "Preface & Contents"
        title = "The Middle → Senior .NET Developer Handbook"
        part = "__home__"
    if part == DEFAULT_PART:
        uncategorized.append(stem)
    book.append({
        "id": stem,
        "slug": slugify(title),
        "title": title,
        "nav": nav,
        "part": part,
        "md": md,
    })

os.makedirs(os.path.dirname(OUT), exist_ok=True)
with open(OUT, "w", encoding="utf-8") as f:
    f.write("window.BOOK = ")
    json.dump(book, f, ensure_ascii=False)
    f.write(";\n")

kb = os.path.getsize(OUT) / 1024
print(f"Wrote {OUT} — {len(book)} chapters, {kb:.0f} KB")
if uncategorized:
    print("  ! Uncategorized (in '%s' — add a range in PART_RANGES to group): %s"
          % (DEFAULT_PART, ", ".join(uncategorized)))
