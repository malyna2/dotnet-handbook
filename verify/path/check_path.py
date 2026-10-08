#!/usr/bin/env python3
"""Check the *Prove it* programs printed in the book against this folder.

1. A ```csharp block whose nearest non-blank line above names `verify/path/<Name>/Program.cs` is a
   *Prove it* program: it must equal that file (trailing whitespace ignored), and the file must be
   at most 30 lines.
2. Without arguments (the whole book): every experiment folder here is printed exactly once, so no
   compiled program goes unread and none is printed twice.

Usage:
  python3 check_path.py                                   # the whole book, plus check 2
  python3 check_path.py ../../chapters/104-async-essentials.md ...   # just these chapters
"""
import os, re, sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(HERE))
from booklib import load_book, heading_owner, links_outside_code, resolve, word_minutes, round5, fmt_minutes  # noqa: E402

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
pages = [c for c in book if not args or os.path.realpath(c.path) in args]
if args and len(pages) != len(args):
    print("not a chapter: " + ", ".join(a for a in args if a not in {os.path.realpath(c.path) for c in pages}))
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
            if m:
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

    for p in problems:
        print("  ! %s: %s" % (page.stem, p))
    ok &= not problems

# 2. Every experiment printed exactly once
if not args:
    folders = sorted(d for d in os.listdir(HERE) if os.path.isfile(os.path.join(HERE, d, "Program.cs")))
    for d in folders:
        where = printed.get(d, [])
        if len(where) != 1:
            ok = False
            print("  ! %s is printed on %d pages%s" % (d, len(where), (": " + ", ".join(where)) if where else ""))
    print("  %d chapters, %d experiments" % (len(pages), len(folders)))

print("path: OK" if ok else "path: FAILED")
sys.exit(0 if ok else 1)
