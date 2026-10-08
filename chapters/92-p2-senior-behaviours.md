# Part 2 · Module 7: Senior Behaviours

> **What this module makes you able to do.** Make decisions other people act on and keep them sound over time: review the design before the lines, write decisions down so they can be revisited instead of relitigated, turn tech debt into a cost the business can prioritise, estimate a project from the outside view as well as the inside, change legacy code without breaking it, grow the people around you, and keep the bar where it is when an AI writes the first draft.

**Time:** reading ≈ 45 min; hands-on ≈ 8 h 20 min — the story-bank lab's first level 4 h 30, the ADR 1 h, the review 1 h, the estimate 45, the questions 20, the decision 15, the check at work 30.

## Covers

- reviewing a design, not its lines: which questions come before the diff, and why reversibility decides how much rigour a decision needs;
- writing decisions down (ADRs, design docs, runbooks), and making tech debt visible as cost and risk rather than as taste;
- estimating a project: three-point estimates and PERT, the reference class, and why a bottom-up plan comes in low;
- changing legacy code safely: characterization tests, seams, the strangler fig, and hotspots from churn and complexity;
- mentoring, influence without authority, ownership, on-call and follow-through;
- a story bank and evidence portfolio, and working with AI tools without lowering the verification bar.

## The mechanism to explain without notes

**A senior's output is decisions that other people act on, so the job is to make each decision visible, checked against evidence and as reversible as it can be — the design before the lines, the outside view before the plan, the current behaviour before the change — and to keep the trust that lets you influence decisions you don't own.**

The cost of being wrong grows with how much has been built on a decision and how many people act on it, so every senior behaviour moves a check earlier or makes a decision cheaper to revisit:

- **Review the design before the code exists.** A design doc or a five-minute conversation costs little to change; a 40-file pull request has already spent the budget, and line comments can only polish it. How hard to look depends on reversibility: a two-way door gets a quick decision, a one-way door (a schema, a public contract, a vendor) gets alternatives on paper (Chapter 63, *Reversibility*).
- **Write the decision down.** An ADR keeps the context and the rejected alternatives, so eighteen months later the team can supersede the decision on purpose instead of arguing it again from memory.
- **Price the debt.** "The code is ugly" is taste and gets ignored; "changes here take longer and put billing at risk, and this is what fixing it buys" is a trade-off the business can schedule (Chapter 17, *Making tech debt visible to the business*). Hotspots — high churn times high complexity — say where the price is real.
- **Estimate from two views.** The inside view sums the tasks you can see; the outside view asks how long similar work actually took. Each corrects the other's blind spot.
- **Pin behaviour before changing it.** A characterization test asserts what legacy code *does*, not what it should do, so a refactor that changes anything by accident fails loudly.
- **Hold the bar on generated code.** An assistant makes producing code cheap and leaves verifying it exactly as expensive: never merge what you haven't read, keep diffs small enough to review, and review the tests as hard as the code (Chapter 18).

Influence runs on the same mechanism over a longer time: trust compounds from estimates that were honest, reviews that were fair and commitments that landed or were renegotiated early. Part 1's module on working habits covers the single review comment, the estimate as a range and the vague ticket; this module is about the decisions above them.

> **Pay attention.** **Why a bottom-up plan comes in low, three times over.** First, task durations are skewed: a task can overrun by far more than it can underrun, so the sum of most-likely values is below the expected total — PERT's (O + 4M + P) / 6 puts the skew back. Second, summing standard deviations as a root of squares assumes independent tasks; tasks that share a cause (the same unfamiliar codebase, the same thin tests) overrun together, so the real spread is wider than the formula says. Third, the largest error is usually a missing row — environments, access, data, deployment, stabilisation — which no per-task arithmetic can fix. The outside view catches all three: compare the total with how long similar work really took (Chapter 63).

## Read (≈ 45 min)

1. [Chapter 17: 17.5 Technical Writing & Documentation](#175-technical-writing-documentation): the ADR template, design docs and runbooks.
2. [Chapter 63: 6.3.5 Reversibility: Matching Rigor to the Door](#635-reversibility-matching-rigor-to-the-door) and [6.3.7 After the Decision: Writing It Down](#637-after-the-decision-writing-it-down): how much rigour a decision deserves, and the record it leaves.
3. [Chapter 17: 17.7 Safe Change, Refactoring & Tech Debt](#177-safe-change-refactoring-tech-debt) and [Chapter 30: Making Technical Debt Visible and Deliberate](#making-technical-debt-visible-and-deliberate): debt in the business's units.
4. [Chapter 30: Characterization Tests](#characterization-tests-pinning-down-behavior), [Seams](#seams-places-to-change-behavior-without-editing), [The Strangler Fig Pattern](#the-strangler-fig-pattern) and [Finding Hotspots: Churn × Complexity](#finding-hotspots-churn-complexity).
5. [Chapter 63: Reference-Class Estimates: the Outside View](#reference-class-estimates-the-outside-view) and [Three-Point Estimates and PERT](#three-point-estimates-and-pert): the worked example and what its numbers show.
6. [Chapter 17: 17.8 Mentoring, Pairing & Growing Others](#178-mentoring-pairing-growing-others), [17.10 Judgment & Influence](#1710-judgment-influence), [17.11 Ownership & Professionalism](#1711-ownership-professionalism) and [17.12 Career Growth: Toward Senior and Staff](#1712-career-growth-toward-senior-and-staff).
7. [Chapter 36: Mining Everyday Work for Stories](#mining-everyday-work-for-stories), [How a STAR Answer Is Scored](#how-a-star-answer-is-scored), [The Weekly Brag Doc](#the-weekly-brag-doc) and [Honesty Rules](#honesty-rules).
8. [Chapter 18: Verification and Trust Discipline](#verification-and-trust-discipline) and [Anti-Patterns and Failure Modes](#anti-patterns-and-failure-modes): the bar for generated code.

## Practice

**1. Chapter 36's story-bank lab, Level 1 (≈ 4 h 30 min).** [Chapter 36](#chapter-36-the-story-bank-evidence-portfolio) is a Practice Gym lab with a kit in [`labs/36-evidence-portfolio`](https://github.com/malyna2/dotnet-handbook/tree/main/labs/36-evidence-portfolio): set up the two repos, mine a year of your own work for candidates, and draft five STAR worksheets. Levels 2 and 3 (mock interviews, a published evidence index) follow at the pace of its time budget. Your stories, numbers and worksheets go in your private story bank and **your own public portfolio repo**, never in this one; stories about a real employer stay private.

**2. Write an ADR (1 h).** Pick a decision your team made recently without a record — or your answer to an earlier module's *Decide*. Use Chapter 17's template: context, decision, consequences (including the negative ones), alternatives considered, and one line on what would make you supersede it. Then ask someone who wasn't there whether they could reconstruct *why* from the ADR alone.

**3. Review a real pull request for its design first (1 h).** Before reading any line, write down: the problem it solves, whether that problem needed solving now, what it makes hard to change later, and whether a one-way door is hidden in it (a schema, a public contract, a new dependency). Then review the lines with Chapter 18's [reviewer's rubric](#judging-ai-generated-code-a-reviewers-rubric) — written for generated code, and just as good for human code. Done when every comment names its condition, its cost and its label, and the design questions came first.

**4. Estimate one piece of work twice (45 min).** For your next multi-week task, make a three-point estimate per task with PERT, then an outside-view estimate from the actual duration of the last few similar pieces of work. Write down where they disagree and why, and the range and commitment level you would give. Keep the actual result next to it when the work is done: that is the start of your own reference class.

The Practice Gym's planned code-review gym (M5 in [`PRACTICE_ROADMAP.md`](https://github.com/malyna2/dotnet-handbook/blob/main/PRACTICE_ROADMAP.md)) will become this module's lab: ten seeded pull requests, including one clean one, scored for both detection and severity calibration.

Later, if you need it: Chapter 63's [6.3.2 The Options Memo](#632-the-options-memo) and [The Cone of Uncertainty](#the-cone-of-uncertainty); Chapter 30's [Sprout Method and Sprout Class](#sprout-method-and-sprout-class) and [The EOL Treadmill](#the-eol-treadmill-legacy-is-a-verb); Chapter 18's [Measuring Whether Any of This Is Working](#measuring-whether-any-of-this-is-working); and the rest of Chapter 36, from [The Mock Interview Protocol](#the-mock-interview-protocol).

## Three questions

**1.** A pull request adds a notifications module with a generic plugin framework: 40 files, green tests, clean code. Every line comment you could write is a nit. Why is "approve with nits" the wrong outcome, and what should have happened instead?

<details>
<summary>Answer</summary>

- **The decision is in the design, not the lines.** The question is whether a plugin framework was needed for the one or two channels that exist (YAGNI), and what it costs every future change. Line review can't ask that; it can only polish the answer.
- **The review came too late.** By the time 40 files exist, rejecting the design throws away a week, so reviewers approve. The cheap moment was a short design doc or a conversation before the code.
- **Reversibility sets the bar.** An internal abstraction is a two-way door: a request-changes for the simpler shape, or an agreed follow-up, is enough. If it adds a one-way door — a public contract, a schema, a vendor — block it until the alternatives are written down.
- **What to do now.** Leave one design-level comment: the problem, the simpler alternative, the cost of each. Ask for a short ADR if the framework stays, and agree on a design check before code for work of this size.
</details>

**2.** A bottom-up plan sums its most-likely task estimates to 40 days. Why is the expected effort higher, why isn't the sum of the pessimistic estimates a useful ceiling, and what do you tell the person who asked?

<details>
<summary>Answer</summary>

- **Skew.** A task can overrun by far more than it can underrun, so each task's expected value (O + 4M + P) / 6 is above its most-likely value, and so is their sum. In Chapter 63's worked example, the sum of most-likely values is 68 person-days and the expected total about 75.
- **The pessimistic sum assumes everything goes wrong at once.** A percentile comes from the spread: with independent tasks, P90 ≈ E + 1.28σ. But tasks that share a cause overrun together, so the real spread is wider than the formula, and missing rows add effort no task carries.
- **The outside view corrects both.** Check the total against how long similar work actually took.
- **What to say.** A range and a commitment level with its assumptions — "[low]–[high] days; we'd commit to [P80 value] if [assumption] holds; we'll narrow it after [decision]" — not a single number, which will be remembered as a promise.
</details>

**3.** You must change the pricing rules in a large class that has no tests and that the business calls critical. An AI assistant offers to write unit tests for it first. Why are those tests not the safety net you need, and what is the safe sequence?

<details>
<summary>Answer</summary>

- **Tests written from an understanding assert the understanding.** An assistant (or a person) writing "correct" tests encodes what it *believes* the code should do. Where the belief is wrong, the tests either fail on today's behaviour or pass while pinning the wrong rule.
- **A characterization test asserts what the code does now.** Call it, let the failure tell you the real value, and encode that value, right or wrong. Now any accidental change during the refactor fails a test.
- **The sequence.** Characterize the behaviour around the change. Find or make a seam. Refactor under the green suite, in commits separate from behaviour changes. Then change the rule, with a normal test for the new behaviour, sprouting new code beside the old where the class resists. An assistant can speed up each step, as long as you read every assertion it writes.
- **Where to stop.** Refactor only the part the change touches. If the class is also a hotspot (high churn, high complexity), the clean-up has a business case of its own.
</details>

## Decide

Your team owns a reporting module that is your repository's top hotspot: it changes every sprint, it is the most complex code you have, and it caused two incidents this year. Product wants a new export feature in it next quarter. Which do you propose?

- **A.** Stop and rewrite the module first, then build the export on the new code.
- **B.** Strangler fig: build the export as a new component behind a routing seam, with characterization tests around the old module, and move pieces across as features touch them.
- **C.** Build the export in the old module, and record the debt in the backlog with its cost, to revisit later.

<details>
<summary>Answer</summary>

**The cost of each.**
- **A** costs months of no visible output and the classic rewrite risk: the old module's undocumented behaviour has to be rediscovered, and the business keeps asking for features meanwhile, so the rewrite chases a moving target (Chapter 30, *Why Big-Bang Rewrites Usually Fail*).
- **B** costs a routing seam, characterization tests, and a period of running two implementations side by side. Every step ships, and every step can stop.
- **C** costs nothing now and adds to the hotspot's interest: the next feature is slower again, and a "revisit later" without an owner and a trigger is never revisited.

**What decides it here: churn.** A hotspot that changes every sprint repays every hour of clean-up quickly, because the next change uses it. The export is new code that can live outside the old module from day one.

**The choice: B.** Present it in the business's units: what each change in the module costs now, the incident risk, and what the first increment buys. Write an ADR for the seam and the migration path.

**What would change it.** If the module will be frozen after this feature (being replaced, or rarely touched), C with a written record is honest and cheap. If the platform under it is reaching end of support, the migration plan has a deadline, and that deadline sets the pace of B.
</details>

## Check at work

**Inspect.** List the last three significant technical decisions in your team. For each: is there an ADR or design doc, and could a new joiner learn *why* from it? Then rank your repository's files by churn and compare the top of the list with what the team complains about:

```bash
# Files ranked by number of commits touching them (last 12 months), from Chapter 30
git log --since="12 months ago" --name-only --pretty=format: \
  | grep '\.cs$' | sort | uniq -c | sort -rn | head -30
```

**Measure.** Your own estimates against actuals for the last five pieces of work you estimated: how often the actual fell inside your range, and in which direction you missed. That list is your first reference class, and the start of your calibration.
