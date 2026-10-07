# Part 1 · Module N: Title in Title Case

> **What this module makes you able to do.** One or two sentences: the job a middle developer does without help once the module is done. In Part 2, the decision a senior makes. Generic: never anyone's scores, self-assessment or employer.

**Time:** reading ≈ 25 min; hands-on ≈ 1 h 10 min — the experiment 15 min, the questions 15, the check at work 30, the chapter exercise 10.

<!--
This file is ignored by the build (it starts with "_"). It is the shape of every learning-path
page: Part 1 (Junior → Middle) is chapters 70–84, Part 2 (Middle → Senior) is 85–98.

Page names:   70-part-1-overview.md · 71..83-p1-<slug>.md · 84-part-1-pay-attention.md
              85-part-2-overview.md · 86..92-p2-<slug>.md · 93-part-2-pay-attention.md
Title line:   "# Part 1 · Module 3: SQL and Indexes" (the middle dot and the colon are parsed by
              build_site.py: the sidebar shows "3" and "SQL and Indexes").

Headings: use ONLY the `##` headings below, in this order, and no `#`-headings inside code
fences. They exist on every path page, so they are ambiguous across pages and nothing links to
them; any other heading text could collide with a chapter heading and silently break links to it.

  Core module (Part 1):        Covers · The mechanism to explain without notes · Read (≈ …) ·
                               Prove it · Three questions · Check at work
  Foundation module (Part 1):  Entry check · Covers · The mechanism to explain without notes ·
                               Read (≈ …) · Prove it (optional) · Check at work
  Part 2 module:               Covers · The mechanism to explain without notes · Read (≈ …) ·
                               Practice · Three questions · Decide · Check at work

Rules (checked by verify/path/check_path.py and verify/check_links.py):
- Links use the reader app's slugs: a chapter slug (#chapter-4-data-access-databases) or a
  section id (#the-n1-problem-seeing-it-and-killing-it). A section id must be unique across the
  book; for ambiguous ones (#exercises, #find-the-bug, #summary) link the chapter and name the
  section in words. Links to the repository are absolute GitHub URLs.
- `## Read (≈ N min)` must equal the build formula over the linked sections (prose 200 wpm, code
  60 wpm, a section counts its subsections, rounded to 5 min); never list a section together
  with one of its own subsections. The **Time:** line states the same reading figure.
- Every ```csharp block is a Prove-it program of at most 30 lines, identical to
  verify/path/<Name>/Program.cs, and the nearest non-blank line above the fence names that path.
  Illustrative code belongs in the chapters, not on path pages.
- Numbers in the text come from verify/path/reference-runs/ (capture.sh), with the environment
  in the file header. Raw HTML: only <details> and <summary> on their own lines.
- This HTML comment is for the template only: the site would print it as text.
-->

## Entry check

*Foundation modules only, and first: three questions to answer without notes. All three right: skip to the next module. Otherwise, work through this one.*

**1.** A short question with one right answer and a "why".

<details>
<summary>Answer</summary>

The answer, then the mechanism in a sentence or two, then where the chapter explains it: [Chapter 2: Dependency Injection](#dependency-injection).
</details>

## Covers

- what you will be able to answer or do, as a question or a trap;
- three to six bullets.

## The mechanism to explain without notes

**One bold sentence: the mechanism that ties the module together.**

One to three short paragraphs, or a lead sentence and bullets: the mechanism, then how each trap in *Covers* follows from it. Book voice: senior-engineer prose, the mechanism behind every claim, cross-references by chapter number with a link.

> **Pay attention.** **A bold title.** Optional: the mechanism behind the most common wrong answer, then the fix.

## Read (≈ 25 min)

1. [Chapter 8: The Sync-Over-Async Deadlock](#the-sync-over-async-deadlock): what to look for in it, in one clause.
2. [Chapter 20: Keep-Alive, Connection Pooling, and Socket Exhaustion](#keep-alive-connection-pooling-and-socket-exhaustion).

## Prove it

One sentence: what the program shows, and why it is worth running before reading the answer.

`verify/path/Name/Program.cs` · run it from `verify/path` with `dotnet run --project Name`:

```csharp
// Prove it: the claim, in one line.
Console.WriteLine("the program, at most 30 lines, identical to verify/path/Name/Program.cs");
```

```text
the expected output; numbers only from reference-runs/
```

What to notice:

- **A bold lead.** What the output proves, tied back to the mechanism.

## Practice

*Part 2 only, instead of Prove it:* the lab, exercise or program to do, with an absolute GitHub link for repository folders, the time budget, and what to keep as evidence in your own portfolio repo (not in this one).

## Three questions

**1.** A "why" question an interviewer asks as the follow-up.

<details>
<summary>Answer</summary>

- **Bold lead.** The mechanism, then the fix.
</details>

**2.** …

**3.** …

## Decide

*Part 2 only:* a situation with two or three defensible options. The answer, in `<details>`, weighs them the way a senior does: what each costs, what decides it here, the choice, and what would change it.

## Check at work

**Inspect.** Something to search for or run in your own codebase, with what a good and a bad result look like. **Measure.** A number to read from your own system, and where.
