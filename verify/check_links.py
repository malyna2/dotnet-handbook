#!/usr/bin/env python3
"""Check every in-book link the way the reader app (site/app.js) resolves it.

For a link `[text](#target)` in a chapter, app.js tries, in order:
  1. a chapter whose slug is `target` (build_site.slugify of the chapter title);
  2. an element with that id on the current page (the chapter's own rendered headings);
  3. headingOwner[target]: the one chapter whose heading has that id. headingOwner scans raw
     lines, code fences included, and drops ids that occur in more than one chapter.
What's New links skip step 2. A link that resolves through headingOwner must also land: the
owning chapter must render a heading with that id. Relative non-# links 404 on the site, which
ships only site/, so links to the repository must be absolute GitHub URLs.

Usage: python3 verify/check_links.py      (exit code 1 if any link is broken)
"""
import os, re, sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from booklib import load_book, heading_owner, links_outside_code, resolve  # noqa: E402

book = load_book()
owner, dupe = heading_owner(book)
broken, total = [], 0
for c in book:
    for n, target in links_outside_code(c.md.split("\n")):
        if re.match(r"^[a-z]+:", target):
            continue
        total += 1
        if not target.startswith("#"):
            broken.append("%s:%d  %s  (relative link: 404 on the site)" % (c.stem, n + 1, target))
        elif resolve(book, c, target, owner, from_whats_new="whats-new" in c.stem) is None:
            t = target[1:]
            why = "ambiguous across chapters" if t in dupe else "no such chapter or heading"
            broken.append("%s:%d  %s  (%s)" % (c.stem, n + 1, target, why))
for b in broken:
    print("  broken: " + b)
print("site links: %d checked, %d broken" % (total, len(broken)))
sys.exit(1 if broken else 0)
