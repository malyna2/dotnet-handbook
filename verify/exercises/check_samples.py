#!/usr/bin/env python3
"""Check that each chapter's Find-the-bug sample is the code its exercise tests compile and run.

For every entry below, the first ```csharp block after the named heading must appear in the
listed file token for token: // comments and whitespace are ignored, nothing else is. A chapter
edit that changes the sample without changing the tested code (or the other way round) fails.
Chapter 51 is checked by snippets/Azure/extract.py instead.
Usage: python3 check_samples.py [ChNN ...]   (no argument: every entry)
"""
import os, re, sys

HERE = os.path.dirname(os.path.abspath(__file__))
CHAPTERS = os.path.join(HERE, "..", "..", "chapters")

# (chapter file, heading the sample sits under, tested file relative to this folder)
SAMPLES = [
    ("107-data-access.md", "### Find the bug", "Ch04/SummaryEndpoint.cs"),
    ("104-async-essentials.md", "### Find the bug", "Ch08/ReportsController.cs"),
]


def normalise(code):
    return re.sub(r"\s+", "", re.sub(r"//[^\n]*", "", code))


def sample(chapter, heading):
    text = open(os.path.join(CHAPTERS, chapter), encoding="utf-8").read()
    at = text.find("\n" + heading + "\n")
    if at < 0:
        sys.exit("%s has no heading %r" % (chapter, heading))
    block = re.compile(r"^```csharp\n(.*?)^```", re.S | re.M).search(text, at)
    if not block:
        sys.exit("%s has no csharp block after %r" % (chapter, heading))
    return block.group(1)


wanted = set(sys.argv[1:])
ok = True
for chapter, heading, rel in SAMPLES:
    if wanted and rel.split("/")[0] not in wanted:
        continue
    tested = open(os.path.join(HERE, rel), encoding="utf-8").read()
    same = normalise(sample(chapter, heading)) in normalise(tested)
    ok &= same
    print("  %s %s: %s %s" % (chapter, heading.lstrip("# "), "matches" if same else "DOES NOT MATCH", rel))
sys.exit(0 if ok else 1)
