#!/usr/bin/env python3
"""Check that every JavaScript/TypeScript sample in the frontend chapters is code that runs here.

Each ```javascript / ```typescript / ```tsx (or js/ts/jsx) block in the chapters below must appear,
token for token, inside one file of that chapter's folder in verify/frontend/ (// comments and
whitespace are ignored, nothing else is). A block that cannot run outside a browser or a real
server is listed in NOT_RUN with the reason, matched by its first line; a NOT_RUN entry that no
longer matches a block fails the check, so the list can't go stale.
Usage: python3 check_samples.py [ch06 ...]   (no argument: every chapter)
"""
import os, re, sys

HERE = os.path.dirname(os.path.abspath(__file__))
CHAPTERS = os.path.join(HERE, "..", "..", "chapters")
LANGS = {"javascript", "js", "typescript", "ts", "tsx", "jsx"}

# chapter file -> folder (relative to this one) that holds its tested code
FOLDERS = {
    "106-frontend-essentials.md": "ch06",
}

# (chapter file, first line of the block) -> why it is not run
NOT_RUN = {
    ("106-frontend-essentials.md", "for (const row of rows) {"):
        "layout thrashing: a cost that exists only in a real browser; jsdom has no layout engine",
}


def normalise(code):
    return re.sub(r"\s+", "", re.sub(r"//[^\n]*", "", code))


def blocks(path):
    text = open(path, encoding="utf-8").read()
    for m in re.finditer(r"^```(\w+)\n(.*?)^```", text, re.S | re.M):
        if m.group(1) in LANGS:
            yield text.count("\n", 0, m.start()) + 1, m.group(2)


def sources(folder):
    out = {}
    for root, dirs, files in os.walk(os.path.join(HERE, folder)):
        for f in files:
            if f.endswith((".js", ".ts", ".tsx", ".jsx", ".mjs")):
                p = os.path.join(root, f)
                out[os.path.relpath(p, HERE)] = normalise(open(p, encoding="utf-8").read())
    return out


wanted = set(sys.argv[1:])
ok, used = True, set()
for chapter, folder in FOLDERS.items():
    if wanted and folder not in wanted:
        used.update(k for k in NOT_RUN if k[0] == chapter)
        continue
    files = sources(folder)
    for line, code in blocks(os.path.join(CHAPTERS, chapter)):
        first = code.strip().split("\n")[0].strip()
        if (chapter, first) in NOT_RUN:
            used.add((chapter, first))
            print("  %s:%d not run (%s)" % (chapter, line, NOT_RUN[(chapter, first)]))
            continue
        n = normalise(code)
        hit = next((rel for rel, src in sorted(files.items()) if n in src), None)
        ok &= hit is not None
        print("  %s:%d %s" % (chapter, line, ("runs as " + hit) if hit else "NOT FOUND in " + folder + "/: " + first))
for key in NOT_RUN:
    if key not in used:
        ok = False
        print("  stale NOT_RUN entry: %s %r" % key)
sys.exit(0 if ok else 1)
