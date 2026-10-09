"""The handbook as the reader app sees it, for the checks in verify/.

Mirrors build_site.py (chapter discovery, slugs, the read-time formula) and site/app.js (heading
ids, headingOwner, link resolution) closely enough to answer two questions without a browser:
does a link resolve, and how long do the sections a page links to take to read.
"""
import glob, os, re

REPO = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
CHAPTERS = os.path.join(REPO, "chapters")


def py_slug(text):
    """build_site.slugify: a chapter's slug, from its title."""
    s = re.sub(r"[^\w\s-]", "", text.strip().lower())
    return re.sub(r"[\s_]+", "-", s).strip("-")


def js_slug(text):
    """app.js headingSlug: a heading's id (JavaScript's \\w is ASCII-only)."""
    s = re.sub(r"[^A-Za-z0-9_\s-]", "", text.lower())
    s = re.sub(r"[\s_]+", "-", s)
    return re.sub(r"^-+|-+$", "", s) or "section"


def home_title():
    m = re.search(r'title = "([^"]+)"', open(os.path.join(REPO, "build_site.py"), encoding="utf-8").read())
    return m.group(1)


class Chapter:
    def __init__(self, num, stem, path):
        self.num, self.stem, self.path = num, stem, path
        self.md = open(path, encoding="utf-8").read()
        t = re.search(r"^#\s+(.+)$", self.md, re.M)
        title = t.group(1).strip() if t else stem
        self.title = home_title() if num == 0 else title
        self.slug = py_slug(self.title)
        self.lines = [l for l in self.md.split("\n") if not l.startswith("_⏱")]
        self.headings = []                      # (line index, level, id), as render() assigns ids
        used, fenced = {}, False
        for i, line in enumerate(self.lines):
            if line.startswith("```"):
                fenced = not fenced
                continue
            if fenced:
                continue
            inner = re.sub(r"^(>\s?)+", "", line)
            h = re.match(r"^(#{1,6})\s+(.*)$", inner)
            if h:
                base = js_slug(h.group(2).strip())
                used[base] = used.get(base, 0) + 1
                self.headings.append((i, len(h.group(1)), base if used[base] == 1 else "%s-%d" % (base, used[base])))
        self.ids = {h[2] for h in self.headings}

    def section(self, hid):
        """The lines of the section whose heading has id `hid`, subsections included."""
        for n, (i, lvl, x) in enumerate(self.headings):
            if x == hid:
                end = next((j for j, l2, _ in self.headings[n + 1:] if l2 <= lvl), len(self.lines))
                return self.lines[i:end]
        return None


def load_book():
    book = []
    for path in glob.glob(os.path.join(CHAPTERS, "*.md")):
        stem = os.path.basename(path)[:-3]
        m = re.match(r"^(\d+)", stem)
        if stem.startswith("_") or not m:
            continue
        book.append(Chapter(int(m.group(1)), stem, path))
    book.sort(key=lambda c: (c.num, c.stem))
    return book


def heading_owner(book):
    """app.js headingOwner: id -> the one chapter slug that has it. Raw lines, fences included;
    ids found in more than one chapter are dropped (returned separately)."""
    owner, dupe = {}, set()
    for c in book:
        used = {}
        for line in c.md.split("\n"):
            if re.match(r"^#{1,6}[ \t]+.*$", line):
                base = js_slug(re.sub(r"^#{1,6}[ \t]+", "", line).strip())
                used[base] = used.get(base, 0) + 1
                i = base if used[base] == 1 else "%s-%d" % (base, used[base])
                if i in owner and owner[i] != c.slug:
                    dupe.add(i)
                owner.setdefault(i, c.slug)
    for d in dupe:
        owner.pop(d, None)
    return owner, dupe


def word_minutes(lines):
    """The build's read-time formula, unrounded: prose at ~200 words a minute, code at ~60."""
    fenced, prose, code = False, 0, 0
    for l in lines:
        if l.startswith("```"):
            fenced = not fenced
            continue
        if fenced:
            code += len(l.split())
        else:
            prose += len(l.split())
    return prose / 200.0 + code / 60.0


def round5(minutes):
    return max(5, int(round(minutes / 5.0)) * 5)


def fmt_minutes(m):
    h, mm = divmod(m, 60)
    if not h:
        return "%d min" % mm
    return "%d h" % h + (" %d min" % mm if mm else "")


def links_outside_code(lines):
    """(line index, target) for every Markdown link outside fences and inline code."""
    out, fenced = [], False
    for n, line in enumerate(lines):
        if line.startswith("```"):
            fenced = not fenced
            continue
        if fenced:
            continue
        text = re.sub(r"`[^`]+`", "", line)
        for target in re.findall(r"\[[^\]]+\]\(([^)\s]+)(?:\s+\"[^\"]*\")?\)", text):
            out.append((n, target))
    return out


def aliases():
    """chapters/_aliases.json: an old chapter address -> the chapter that holds it now."""
    import json
    p = os.path.join(CHAPTERS, "_aliases.json")
    return json.load(open(p, encoding="utf-8")) if os.path.exists(p) else {}


_ALIASES = None


def resolve(book, chapter, target, owner, from_whats_new=False):
    """Where app.js sends a click on `#target` from `chapter`: (chapter, heading id or None), or
    None if the click goes nowhere. An old chapter address resolves through its alias."""
    global _ALIASES
    if _ALIASES is None:
        _ALIASES = aliases()
    by_slug = {c.slug: c for c in book}
    t = target[1:] if target.startswith("#") else target
    if t in by_slug:
        return by_slug[t], None
    if t in _ALIASES and _ALIASES[t] in by_slug:
        return by_slug[_ALIASES[t]], None
    if not from_whats_new and t in chapter.ids:
        return chapter, t
    o = owner.get(t)
    if o is not None and t in by_slug[o].ids:
        return by_slug[o], t
    return None
