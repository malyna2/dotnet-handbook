#!/usr/bin/env python3
"""Check the learning-path pages (Part 1 and Part 2, chapters 70-98) against this folder.

1. Every ```csharp block on a path page is a *Prove it* program. The nearest non-blank line above
   its fence names it as `verify/path/<Name>/Program.cs`; the block equals that file (trailing
   whitespace ignored); the file is at most 30 lines.
2. Every `## Read (≈ …)` heading states the reading time of the sections its list links to: the
   build's formula (prose ~200 words a minute, code ~60), summed and rounded to the nearest
   5 minutes. A section link counts its subsections; a chapter link counts the whole chapter.
   A `**Time:** reading ≈ …` line, if the page has one, must state the same figure.
3. Without arguments (every path page): every experiment folder here is printed on exactly one
   page, so no compiled program goes unread and none is printed twice.

Usage:
  python3 check_path.py                                  # every path page, plus check 3
  python3 check_path.py ../../chapters/71-p1-x.md ...    # just these pages (while writing one)
"""
import os, re, sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(HERE))
from booklib import load_book, heading_owner, links_outside_code, resolve, word_minutes, round5, fmt_minutes  # noqa: E402

PATH_PAGES = range(70, 99)
MAX_LINES = 30


def lines_of(code):
    return [l.rstrip() for l in code.rstrip("\n").split("\n")]


def parse_minutes(text):
    m = re.match(r"^\s*(?:(\d+)\s*h)?\s*(?:(\d+)\s*min)?\s*$", text)
    if not m or not (m.group(1) or m.group(2)):
        return None
    return int(m.group(1) or 0) * 60 + int(m.group(2) or 0)


book = load_book()
owner, _ = heading_owner(book)
args = [os.path.realpath(a) for a in sys.argv[1:]]
pages = [c for c in book if c.num in PATH_PAGES and (not args or os.path.realpath(c.path) in args)]
if args and len(pages) != len(args):
    print("not a path page (chapters/70-98): " + ", ".join(a for a in args if a not in {os.path.realpath(c.path) for c in pages}))
    sys.exit(1)

ok, printed = True, {}
for page in pages:
    raw = page.md.split("\n")
    problems = []

    # 1. Prove-it programs
    i = 0
    while i < len(raw):
        if raw[i].startswith("```csharp"):
            j = i + 1
            while j < len(raw) and not raw[j].startswith("```"):
                j += 1
            block = raw[i + 1:j]
            k = i - 1
            while k >= 0 and not raw[k].strip():
                k -= 1
            m = re.search(r"verify/path/(\w+)/Program\.cs", raw[k]) if k >= 0 else None
            if not m:
                problems.append("line %d: a csharp block without `verify/path/<Name>/Program.cs` on the line above it" % (i + 1))
            else:
                name = m.group(1)
                printed.setdefault(name, []).append(page.stem)
                f = os.path.join(HERE, name, "Program.cs")
                if not os.path.exists(f):
                    problems.append("line %d: %s has no %s" % (i + 1, name, os.path.relpath(f, HERE)))
                else:
                    program = lines_of(open(f, encoding="utf-8").read())
                    same, short = lines_of("\n".join(block)) == program, len(program) <= MAX_LINES
                    print("  %-34s %-24s %s, %2d lines%s" % (page.stem, name, "matches Program.cs" if same else "DIFFERS from Program.cs",
                                                          len(program), "" if short else " (over %d)" % MAX_LINES))
                    if not (same and short):
                        problems.append("line %d: %s %s" % (i + 1, name, "differs from Program.cs" if not same else "is over %d lines" % MAX_LINES))
            i = j + 1
            continue
        i += 1

    # 2. Reading time of the Read section
    for n, (li, lvl, hid) in enumerate(page.headings):
        line = page.lines[li]
        m = re.match(r"^##\s+Read\s+\(≈\s*([^)]+)\)\s*$", line)
        if not m:
            continue
        stated = parse_minutes(m.group(1))
        end = next((j for j, l2, _ in page.headings[n + 1:] if l2 <= lvl), len(page.lines))
        seen, total = set(), 0.0
        for _, target in links_outside_code(page.lines[li + 1:end]):
            if not target.startswith("#") or target in seen:
                continue
            seen.add(target)
            hit = resolve(book, page, target, owner)
            if hit is None:
                problems.append("Read list: %s does not resolve" % target)
                continue
            chapter, sec = hit
            if chapter.num in PATH_PAGES:
                problems.append("Read list: %s is a path page; Read lists link the chapters" % target)
                continue
            total += word_minutes(chapter.section(sec) if sec else chapter.lines)
        expected = round5(total)
        print("  %-34s Read: stated %s, computed %s (%.1f min over %d links)" % (
            page.stem, fmt_minutes(stated) if stated is not None else m.group(1), fmt_minutes(expected), total, len(seen)))
        if stated != expected:
            problems.append("`## Read (≈ %s)` should say ≈ %s" % (m.group(1), fmt_minutes(expected)))
        tm = re.search(r"^\*\*Time:\*\*\s*reading\s*≈\s*((?:\d+\s*h\s*)?(?:\d+\s*min)?)", page.md, re.M)
        if tm and parse_minutes(tm.group(1)) != expected:
            problems.append("`**Time:** reading ≈ %s` should say ≈ %s" % (tm.group(1).strip(), fmt_minutes(expected)))

    for p in problems:
        print("  ! %s: %s" % (page.stem, p))
    ok &= not problems

# 3. Every experiment printed on exactly one page
if not args:
    folders = sorted(d for d in os.listdir(HERE) if os.path.isfile(os.path.join(HERE, d, "Program.cs")))
    for d in folders:
        where = printed.get(d, [])
        if len(where) != 1:
            ok = False
            print("  ! %s is printed on %d pages%s" % (d, len(where), (": " + ", ".join(where)) if where else ""))
    print("  %d path pages, %d experiments" % (len(pages), len(folders)))

print("path: OK" if ok else "path: FAILED")
sys.exit(0 if ok else 1)
