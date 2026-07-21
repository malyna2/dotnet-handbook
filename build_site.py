#!/usr/bin/env python3
"""Bundle the chapter markdown files into site/content.js as window.BOOK."""
import json, os, re

CH_DIR = os.path.join(os.path.dirname(__file__), "chapters")
OUT = os.path.join(os.path.dirname(__file__), "site", "content.js")

# ordered (stem, part) — part label groups them in the sidebar
ORDER = [
    ("00-frontmatter",            "__home__"),
    ("01-csharp",                 "Part I — The Language & the Platform"),
    ("02-runtime",                "Part I — The Language & the Platform"),
    ("03-aspnetcore",             "Part I — The Language & the Platform"),
    ("04-data",                   "Part I — The Language & the Platform"),
    ("05-patterns",               "Part II — Designing Software That Lasts"),
    ("06-architecture",           "Part II — Designing Software That Lasts"),
    ("07-testing",                "Part II — Designing Software That Lasts"),
    ("08-async",                  "Part II — Designing Software That Lasts"),
    ("09-messaging",              "Part III — Distributed Systems & the Cloud"),
    ("10-cloud",                  "Part III — Distributed Systems & the Cloud"),
    ("11-containers",             "Part III — Distributed Systems & the Cloud"),
    ("12-devops",                 "Part III — Distributed Systems & the Cloud"),
    ("13-observability",          "Part IV — Running Software in Production"),
    ("14-security",               "Part IV — Running Software in Production"),
    ("15-performance",            "Part IV — Running Software in Production"),
    ("16-tooling",                "Part V — The Craft & the AI Era"),
    ("17-softskills",             "Part V — The Craft & the AI Era"),
    ("18-ai-native",              "Part V — The Craft & the AI Era"),
    ("19-networking",             "Part VI — Deepening the Backend"),
    ("20-distributed-theory",     "Part VI — Deepening the Backend"),
    ("21-background-actors",      "Part VI — Deepening the Backend"),
    ("22-data-scale",             "Part VI — Deepening the Backend"),
    ("23-serialization-schema",   "Part VI — Deepening the Backend"),
    ("24-advanced-testing",       "Part VI — Deepening the Backend"),
    ("25-realworld-essentials",   "Part VII — Foundations, Governance & Specializations"),
    ("26-dsa-systemdesign",       "Part VII — Foundations, Governance & Specializations"),
    ("27-compliance-finops",      "Part VII — Foundations, Governance & Specializations"),
    ("28-frontend-fullstack",     "Part VII — Foundations, Governance & Specializations"),
    ("29-legacy-brownfield",      "Part VII — Foundations, Governance & Specializations"),
    ("30-linux-cli",              "Part VII — Foundations, Governance & Specializations"),
    ("31-capstone",               "Part VIII — Capstone"),
    ("99-appendix-roadmap",       "Appendices"),
    ("100-appendix-b-versions",   "Appendices"),
]

def slugify(text):
    s = text.strip().lower()
    s = re.sub(r"[^\w\s-]", "", s)
    s = re.sub(r"[\s_]+", "-", s)
    return s.strip("-")

book = []
for stem, part in ORDER:
    path = os.path.join(CH_DIR, stem + ".md")
    with open(path, encoding="utf-8") as f:
        md = f.read()
    m = re.search(r"^#\s+(.+)$", md, re.MULTILINE)
    title = m.group(1).strip() if m else stem
    # short nav label
    nav = re.sub(r"^Chapter\s+\d+:\s*", "", title)
    nav = re.sub(r"^Appendix\s+([A-Z]):\s*", r"App. \1: ", nav)
    if stem == "00-frontmatter":
        nav = "Preface & Contents"
        title = "The Middle → Senior .NET Developer Handbook"
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
