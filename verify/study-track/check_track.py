#!/usr/bin/env python3
"""Check STUDY_TRACK.md against what this folder compiles and the files it links to.

1. Every ```csharp block is one of the experiments, in this order, and equals <Name>/Program.cs
   (trailing whitespace ignored) and is at most 30 lines. A block that is added, removed or
   reordered fails, so the track cannot quietly print code nobody compiled.
2. Every relative link resolves: the file or folder exists, and a #fragment matches a heading
   anchor as GitHub renders it (lowercase; punctuation other than '-' and '_' dropped; each space
   becomes '-'; repeats get -1, -2 ...). Links to this repository's GitHub tree must exist locally.
"""
import os, re, sys

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.normpath(os.path.join(HERE, "..", ".."))
TRACK = os.path.join(REPO, "STUDY_TRACK.md")
EXPERIMENTS = ["AsyncVoid", "WhenAll", "Starvation", "HttpClientPerRequest", "PeekLock", "SeekVsScan", "CheckThenAct"]
MAX_LINES = 30
GITHUB_TREE = "https://github.com/malyna2/dotnet-handbook/tree/main/"


def lines(code):
    return [l.rstrip() for l in code.rstrip("\n").split("\n")]


def outside_code(text):
    out, fenced = [], False
    for line in text.split("\n"):
        if line.startswith("```"):
            fenced = not fenced
            continue
        if not fenced:
            out.append(line)
    return out


def github_anchors(path):
    seen, anchors = {}, set()
    for line in outside_code(open(path, encoding="utf-8").read()):
        m = re.match(r"^#{1,6}\s+(.*?)\s*#*\s*$", line)
        if not m:
            continue
        title = re.sub(r"\[([^\]]*)\]\([^)]*\)", r"\1", m.group(1)).strip().lower()
        base = re.sub(r"[^\w\- ]", "", title).replace(" ", "-")
        n = seen.get(base, 0)
        seen[base] = n + 1
        anchors.add(base if n == 0 else "%s-%d" % (base, n))
    return anchors


text = open(TRACK, encoding="utf-8").read()
ok = True

blocks = [m.group(1) for m in re.finditer(r"^```csharp\n(.*?)^```", text, re.S | re.M)]
if len(blocks) != len(EXPERIMENTS):
    ok = False
    print("STUDY_TRACK.md has %d csharp blocks; expected %d (%s)" % (len(blocks), len(EXPERIMENTS), ", ".join(EXPERIMENTS)))
for name, block in zip(EXPERIMENTS, blocks):
    program = lines(open(os.path.join(HERE, name, "Program.cs"), encoding="utf-8").read())
    same, short = lines(block) == program, len(program) <= MAX_LINES
    ok &= same and short
    print("  %-22s %s, %2d lines%s" % (name, "matches Program.cs" if same else "DIFFERS from Program.cs",
                                        len(program), "" if short else " (over %d)" % MAX_LINES))

links = re.findall(r"\]\(([^)\s]+)\)", "\n".join(outside_code(text)))
bad = []
for target in links:
    if target.startswith(GITHUB_TREE):
        target = target[len(GITHUB_TREE):]
    elif re.match(r"^[a-z]+:", target):
        continue
    path, _, fragment = target.partition("#")
    full = os.path.normpath(os.path.join(REPO, path)) if path else TRACK
    if not os.path.exists(full):
        bad.append(target + "  (no such file)")
    elif fragment and (not full.endswith(".md") or fragment not in github_anchors(full)):
        bad.append(target + "  (no such heading)")
for b in bad:
    print("  broken link: " + b)
ok &= not bad
print("  %d links checked, %d broken" % (len(links), len(bad)))

print("track: OK" if ok else "track: FAILED")
sys.exit(0 if ok else 1)
