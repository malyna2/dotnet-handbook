#!/usr/bin/env python3
"""Build the handbook outputs from chapters/*.md.

Generates:
  * site/content.js  — window.BOOK, the reader website's content bundle
  * site/content.uk.js — the Ukrainian book, with the same routes and heading IDs
  * main.md / main.uk.md — the whole book in English / Ukrainian

Auto-discovers every numbered `chapters/NN-*.md` file: to add a chapter, just
drop a file named with a numeric prefix (e.g. `117-my-topic.md`, starting with an
`# Chapter 17: ...` heading) into chapters/, add its complete Ukrainian translation
with the same filename in chapters/uk/, and re-run this script. You do NOT
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
import glob, hashlib, json, os, re
from collections import Counter

ROOT = os.path.dirname(__file__)
CH_DIR = os.path.join(ROOT, "chapters")
OUT_JS = os.path.join(ROOT, "site", "content.js")
OUT_MD = os.path.join(ROOT, "main.md")
UK_DIR = os.path.join(CH_DIR, "uk")
OUT_UK_JS = os.path.join(ROOT, "site", "content.uk.js")
OUT_UK_MD = os.path.join(ROOT, "main.uk.md")
TRANSLATION_STATE = os.path.join(UK_DIR, "_sources.json")

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
UK_PARTS = {
    "__home__": "__home__",
    "Part 1 — Junior → Middle": "Частина 1 — Junior → Middle",
    "Part 2 — Middle → Senior": "Частина 2 — Middle → Senior",
    "Part 2 · Runtime and Data": "Частина 2 · середовище виконання й дані",
    "Part 2 · Distributed Systems and Architecture": "Частина 2 · розподілені системи й архітектура",
    "Part 2 · Running It in Production": "Частина 2 · робота в продакшені",
    "Part 2 · Cloud and Azure": "Частина 2 · хмара й Azure",
    "Part 2 · AI": "Частина 2 · штучний інтелект",
    "Part 2 · Frontend and Full-Stack": "Частина 2 · фронтенд і фулстек",
    "Part 2 · Incidents, Seniority and Career": "Частина 2 · інциденти, рівень Senior і кар’єра",
    "Part 2 · Beyond Senior: The Trusted Advisor": "Частина 2 · вище за Senior: довірений радник",
    "Part 2 · Capstone and Review": "Частина 2 · підсумковий проєкт і повторення",
    "Appendix": "Додаток",
    "What's New": "Що нового",
    DEFAULT_PART: "Додаткові розділи",
}

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

def with_readtime(md, is_home, language="en"):
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
    if language == "uk":
        label = label.replace(" h", " год").replace(" min", " хв")
        rt = "_⏱️ Орієнтовний час читання: ~%s · %d слів (навчальний темп)_" % (label, total)
    else:
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

def headings(md):
    """Rendered headings, including blockquotes but excluding fenced examples.

    Use the English renderer's ASCII slug algorithm so translated headings keep
    the exact same destinations as existing links and outline entries.
    """
    result, used, fenced = [], {}, False
    for line in md.splitlines():
        inner = re.sub(r"^(>\s?)+", "", line)
        if inner.startswith("```"):
            fenced = not fenced
            continue
        if fenced:
            continue
        m = re.match(r"^(#{1,6})\s+(.*)$", inner)
        if not m:
            continue
        text = m.group(2).strip()
        base = re.sub(r"[^A-Za-z0-9_\s-]", "", text.lower())
        base = re.sub(r"[\s_]+", "-", base).strip("-") or "section"
        used[base] = used.get(base, 0) + 1
        anchor = base if used[base] == 1 else "%s-%d" % (base, used[base])
        result.append((len(m.group(1)), text, anchor))
    return result


FENCED_BLOCK = re.compile(r"^(?:>\s?)*```[^\n]*\n[\s\S]*?^(?:>\s?)*```[^\n]*$", re.MULTILINE)


def code_blocks(md):
    return FENCED_BLOCK.findall(md)


def prose(md):
    """Exclude examples and generated reading-time text from preservation checks."""
    md = FENCED_BLOCK.sub("", md)
    return "\n".join(line for line in md.splitlines() if not line.startswith("_⏱"))


def validate_translation(stem, source, translated, previous):
    errors = []
    source_headings, translated_headings = headings(source), headings(translated)
    if [h[0] for h in source_headings] != [h[0] for h in translated_headings]:
        errors.append("heading levels/count/order differ")
    if source_headings and translated_headings:
        number = re.match(r"^Chapter\s+(\d+)\b", source_headings[0][1])
        translated_number = re.match(r"^(?:Розділ|Chapter)\s+(\d+)\b", translated_headings[0][1])
        if number and (not translated_number or number[1] != translated_number[1]):
            errors.append("chapter number differs")
    if code_blocks(source) != code_blocks(translated):
        errors.append("fenced code examples differ")
    src, uk = prose(source), prose(translated)
    for label, pattern in [
        ("inline code", r"`[^`\n]+`"),
        ("link destinations", r"\[[^\]]+\]\(([^)\s]+)(?:\s+\"[^\"]*\")?\)"),
        ("details tags", r"</?details(?:\s[^>]*)?>"),
        ("summary tags", r"</?summary(?:\s[^>]*)?>"),
    ]:
        expected, actual = Counter(re.findall(pattern, src)), Counter(re.findall(pattern, uk))
        # Additional inline formatting is harmless; original technical tokens
        # must still be present verbatim at least as often as in the source.
        changed = bool(expected - actual) if label == "inline code" else expected != actual
        if changed:
            errors.append(label + " differ")
    if not re.search(r"[А-Яа-яІіЇїЄєҐґ]", uk):
        errors.append("no Ukrainian prose found")
    # A translation may be prepared by replacing prose in a source copy. Reject
    # unchanged English teaching sentences so a partial copy cannot ship merely
    # because its code, links and heading counts already match.
    translated_lines = {line.strip() for line in uk.splitlines()}
    in_sources = False
    for line in src.splitlines():
        if re.match(r"^#{1,6}\s+(?:Sources|References)(?:\s|$)", line):
            in_sources = True
        elif re.match(r"^#{1,6}\s+", line):
            in_sources = False
        if in_sources:
            continue  # Published titles and author names are proper names.
        text = re.sub(r"`[^`\n]+`", "", line)
        text = re.sub(r"\]\([^)]+\)", "]", text)
        if len(re.findall(r"\b[A-Za-z]{2,}\b", text)) >= 10 and line.strip() in translated_lines:
            errors.append("untranslated English prose: " + line.strip()[:100])
            break
    state = {
        "source": hashlib.sha256(source.encode("utf-8")).hexdigest(),
        "translation": hashlib.sha256(translated.encode("utf-8")).hexdigest(),
    }
    old = previous.get(stem)
    if old and old["source"] != state["source"] and old["translation"] == state["translation"]:
        errors.append("English source changed without updating its Ukrainian translation")
    if errors:
        raise ValueError("%s: %s" % (stem, "; ".join(errors)))
    return state


def load_json(path, default):
    if not os.path.exists(path):
        return default
    with open(path, encoding="utf-8") as f:
        return json.load(f)


def build():
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
    uk_book, uk_md_parts, translation_state = [], [], {}
    previous_state = load_json(TRANSLATION_STATE, {})
    missing = [stem for _, stem, _ in entries if not os.path.exists(os.path.join(UK_DIR, stem + ".md"))]
    if missing:
        raise SystemExit("Missing Ukrainian translations in chapters/uk/: " + ", ".join(missing))
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

        with open(os.path.join(UK_DIR, stem + ".md"), encoding="utf-8") as f:
            uk_raw = f.read()
        translation_state[stem] = validate_translation(stem, raw, uk_raw, previous_state)
        uk_md = with_readtime(uk_raw, is_home or "whats-new" in stem, "uk")
        uk_title = re.search(r"^#\s+(.+)$", uk_md, re.MULTILINE).group(1).strip()
        uk_nav = re.sub(r"^Розділ\s+\d+:\s*", "", uk_title)
        uk_num = num_label
        if is_home:
            uk_nav = "Передмова й зміст"
        elif "pay-attention" in stem:
            uk_nav = "На що звернути увагу"
        elif uk_title.startswith("Додаток:"):
            uk_nav, uk_num = uk_title[len("Додаток:"):].strip(), "Дод."
        chapter = dict(book[-1], title=uk_title, nav=uk_nav, num=uk_num,
                       part=UK_PARTS[part], md=uk_md,
                       anchors=[h[2] for h in headings(raw)])
        uk_book.append(chapter)
        uk_md_parts.append(uk_md)

    os.makedirs(os.path.dirname(OUT_JS), exist_ok=True)
    with open(OUT_JS, "w", encoding="utf-8") as f:
        f.write("window.BOOK = ")
        json.dump(book, f, ensure_ascii=False)
        f.write(";\nwindow.ALIASES = ")
        json.dump(load_json(ALIASES_FILE, {}), f, ensure_ascii=False)
        f.write(";\n")

    with open(OUT_MD, "w", encoding="utf-8") as f:
        f.write("\n\n---\n\n".join(md_parts) + "\n\n---\n\n")

    with open(OUT_UK_JS, "w", encoding="utf-8") as f:
        f.write("window.BOOK_UK = ")
        json.dump(uk_book, f, ensure_ascii=False)
        f.write(";\n")
    with open(OUT_UK_MD, "w", encoding="utf-8") as f:
        f.write("\n\n---\n\n".join(uk_md_parts) + "\n\n---\n\n")
    with open(TRANSLATION_STATE, "w", encoding="utf-8") as f:
        json.dump(translation_state, f, ensure_ascii=False, indent=2, sort_keys=True)
        f.write("\n")

    print("Wrote %s — %d chapters, %d KB" % (OUT_JS, len(book), os.path.getsize(OUT_JS) / 1024))
    print("Wrote %s — %d words" % (OUT_MD, sum(len(p.split()) for p in md_parts)))
    print("Wrote %s — %d Ukrainian chapters" % (OUT_UK_JS, len(uk_book)))
    print("Wrote %s — %d words" % (OUT_UK_MD, sum(len(p.split()) for p in uk_md_parts)))
    if uncategorized:
        print("  ! Uncategorized (in '%s' — add a range in PART_RANGES): %s"
              % (DEFAULT_PART, ", ".join(uncategorized)))


if __name__ == "__main__":
    build()
