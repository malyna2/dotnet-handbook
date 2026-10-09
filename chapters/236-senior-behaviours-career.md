# Chapter 36: Senior Behaviours, Career and Interviews

A senior's output is decisions that other people act on. The cost of a wrong decision grows with how much is built on it and how many people follow it, so every senior behaviour either moves a check earlier or makes a decision cheaper to revisit: the design reviewed before the lines exist, the decision written down so it can be superseded rather than relitigated, the estimate checked against how long similar work really took, the legacy behaviour pinned before it is changed. Influence runs on the same mechanism over a longer time: trust compounds from estimates that were honest, reviews that were fair and commitments that landed or were renegotiated early.

This chapter makes you able to work that way and to show it. It starts with the behaviours that multiply your impact through other people — mentoring, judgment and influence without authority — and how to steer your own growth toward senior and staff. Then it turns those behaviours into interview answers: how to approach any question, and the behavioural questions every senior loop asks. It closes with how to keep learning once the book ends. The practice at the end draws on the tools taught elsewhere: ADRs and the single review comment from [Chapter 16: Working Like a Middle Developer](#chapter-16-working-like-a-middle-developer), characterization tests, seams and hotspots from [Chapter 24](#chapter-24-working-with-legacy-brownfield-code), reference-class and PERT estimates from [Chapter 41](#chapter-41-recommendations-proposals-and-estimates), the reviewer's rubric for generated code from [Chapter 32](#chapter-32-the-ai-native-developer-thriving-in-the-ai-era), and the story bank you build in [Chapter 37](#chapter-37-the-story-bank-evidence-portfolio).

## Mentoring, Pairing & Growing Others

The fastest way to increase your leverage is to make the people around you better. This is also the clearest signal of readiness for senior and staff roles.

### Teach by asking, not telling

When a junior brings you a problem, resist the urge to hand them the answer — it solves today's problem but teaches nothing. Ask questions that lead them to it:

> "What does the stack trace point to?" · "What have you tried?" · "What do you expect this line to do versus what it does?" · "Where could we add a log to find out?"

They arrive at the solution themselves, which means they can do it alone next time. You've created leverage instead of a dependency.

### Pairing: driver and navigator

In pair programming, the **driver** types and focuses on the immediate line of code; the **navigator** thinks a step ahead — the design, the edge case, the next test. Swap roles regularly. Pairing is expensive (two people, one task) so spend it where it pays: onboarding, genuinely hard problems, high-risk changes, and spreading knowledge of a scary subsystem. It is not for routine work.

### Mentoring vs sponsoring, and psychological safety

**Mentoring** is giving advice and guidance — it helps someone in the room. **Sponsorship** is spending your own credibility on someone's behalf when they're *not* in the room: recommending them for the high-visibility project, saying their name in the promotion discussion. Sponsorship is rarer and more powerful; as you gain standing, sponsor people, especially those with less structural advantage than you.

Both require **psychological safety** — a team where people can admit "I don't understand this" or "I broke prod" without fear. You build it in small moments: admit your own mistakes openly, respond to "dumb" questions with genuine answers, never punish honesty. A senior who says "I have no idea how this works, let's find out together" gives everyone else permission to be human, and that unlocks the whole team.

## Judgment & Influence

Seniority is largely **judgment** — knowing which of the many technically-correct options is the *right* one here, and getting people to go along with it without a title forcing them to.

### Knowing when NOT to add complexity

The most expensive code is the code you didn't need. The YAGNI principle itself is in [Chapter 10: Design Basics](#chapter-10-design-basics); what changes at senior level is that you apply it to *other people's* proposals. Mid-level engineers are often seduced by the flexible, abstract, future-proof design; seniors have been burned by unused abstractions often enough to prefer the simplest thing that solves the *actual* problem, and to add complexity only when a *second real* need proves it's warranted. When someone proposes a speculative abstraction, the senior question is: "What concrete requirement, that exists today, needs this?"

### Picking battles and influencing without authority

You cannot fight every fight; you'll exhaust your credibility and be tuned out. Decide whether a given issue is a hill worth dying on. A security hole or data-loss bug: yes, plant the flag. Tabs vs spaces when there's a linter: absolutely not — defer and move on. Save your capital for what matters.

Driving decisions **without authority** is the staff-engineer skill. You can't order anyone; you influence through:

- **Trust,** earned by being right, being honest about uncertainty, and following through on commitments over time.
- **Data,** so it's not your opinion vs theirs but a shared look at evidence.
- **Bringing people along early** — socialize an idea in one-on-ones before the big meeting, so by the time you present, the key people already nod. Decisions are usually made in the hallway, not the meeting.
- **Framing in others' interests:** show how your proposal helps *their* goals, not just that it's technically superior.

> **Reputation is your real currency, and it compounds. Be the person whose estimates are honest, whose reviews are fair, whose commitments land, and who says "I was wrong" when they were. That reputation, built over years, is what lets you move a decision with a single Slack message.**

### From colleague to advisor

Everything above assumes you sit inside the team. As a contractor, consultant or outstaffed engineer you sit next to it, and the client measures you differently. An employer pays for output; a client increasingly pays for **judgment** — the reduced risk of a decision they cannot evaluate themselves. Three shifts follow:

- **From answering to framing.** "How do we build X?" becomes "Should we build X, and what does it cost us not to?" You diagnose before you prescribe.
- **From neutral to committed.** Listing trade-offs is where the job starts. The client wants a recommendation with the conditions under which it would change.
- **From tasks to outcomes.** Report in the client's units: money, time and risk, not tickets closed.

Your influence comes from the same sources as inside a team (trust, data and bringing people along early), with one extra hazard: **self-orientation**. The moment advice sounds like it serves your next contract, it stops being advice. Chapters 38–43, starting with [Chapter 38: Having a Point of View](#chapter-38-having-a-point-of-view), turn this stance into skills you can practise.

## Career Growth: Toward Senior and Staff

Finally, steer your own growth deliberately instead of hoping it happens.

**Seek feedback actively** rather than waiting for the annual review. Ask specific questions: "What's one thing I could do that would have the most impact on the team?" or "What would you need to see for me to be considered senior?" Vague questions ("any feedback?") get vague answers; specific ones get gold.

**Find sponsors, not just mentors.** A mentor advises you; a sponsor advocates for you in rooms you're not in. Do visible, high-value work, make sure the right people know you did it (without bragging — let the results and a clear write-up speak), and build relationships with people who have influence over your trajectory.

**Understand the staff-engineer archetypes.** Senior-and-beyond is not one path; the well-known shapes (drawn from Will Larson's *Staff Engineer*) are:

- **Tech Lead:** guides the execution of a team — the "how" and "who" of delivery, closest to a team's day-to-day.
- **Architect:** owns the technical direction and quality of a critical area across teams.
- **Solver:** the person dropped onto the gnarliest, most ambiguous problem to untangle it.
- **Right Hand:** operates as an extension of an engineering leader, carrying organizational leverage.

You don't have to pick forever, but knowing which one energizes you tells you which skills to lean into. A Solver invests in debugging and systems depth; a Tech Lead in communication and planning; an Architect in design and cross-team influence.

> **The through-line: senior engineering is the multiplication of impact through other people and good judgment, not the maximization of your personal code output. Every skill here — clear writing, kind reviews, honest estimates, blameless post-mortems, deliberate mentoring, sound judgment, real ownership — is a lever. Master the levers, and your impact stops being bounded by your own two hands.**

Start with one lever — pick the weakest one — and practice it deliberately this week.

## Interviews: Turning Behaviour into Answers

An interview compresses everything above into an hour, and it scores what it can hear. The technical question banks live with their topics — each chapter ends with its own *Interview Questions* — so this section is about the approach that applies to all of them and the behavioural questions that probe seniority directly.

### How to Approach Any Interview

**Five habits that apply to every question:**

1. **Clarify before you answer.** A ten-second "Do you mean X or Y?" beats two minutes solving the wrong problem. Interviewers score you on scoping, not mind-reading.
2. **Think out loud.** Silence reads as "stuck." Narrate your reasoning even when you're confident — it lets the interviewer follow, hint, and give partial credit.
3. **Structure the answer.** Lead with the one-sentence conclusion, then support it. "Use `ValueTask` when the result is usually synchronous — here's why…" is stronger than building to a mystery reveal.
4. **Admit unknowns cleanly.** "I haven't used that, but I'd expect it works like X because…" shows honesty plus reasoning. Bluffing is the fastest way to fail a senior loop.
5. **For behavioral questions, use STAR** — Situation, Task, Action, Result. Keep Situation short, spend your words on *your* Action, and always land a measurable Result.

**How do you handle a question you don't know the answer to?**
State what you do know, reason from first principles toward a plausible answer, and be explicit about the boundary: "I know GC has generations; I'm less sure of the exact LOH threshold, but I'd reason it's large because compaction is expensive." That earns more than silence or a confident wrong guess.

**A candidate says "it depends" — is that a good answer?**
Only if you then say *what* it depends on and pick a default. "It depends on read/write ratio: read-heavy, I'd cache; write-heavy, I'd skip the cache to avoid invalidation pain." Naming the trade-off axis is the senior signal.

**How do you show seniority beyond just knowing facts?**
Talk about trade-offs, failure modes, operability, and cost — not just the happy path. Juniors describe how a thing works; seniors describe when *not* to use it and what breaks it at 3 a.m.

### Behavioral / Seniority

Answer these with **STAR** and keep the spotlight on *your* actions and a concrete result. Have three or four real stories prepared that you can flex to different questions.

> **Practice it.** [Chapter 37: The Story Bank & Evidence Portfolio](#chapter-37-the-story-bank-evidence-portfolio) has a worksheet for each question below, a way to mine your own work for the stories, and a scored mock-interview protocol to rehearse them.

**Tell me about a hard bug you solved.**
Pick a genuinely tricky one — intermittent, distributed, or a heisenbug. Emphasize *method*: how you reproduced it, formed and tested hypotheses, used tooling (logs, profiler, dump), found root cause, and prevented recurrence (a test, a monitor). Result: the metric that improved. The story sells your debugging process, not luck.

**Describe a disagreement with a colleague.**
Show you can disagree on the technical merits and stay collaborative. Structure: the two positions and their trade-offs, how you sought data or a spike to decide, and how you committed to the outcome even if it wasn't your pick. Interviewers probe for ego and for "disagree and commit."

**Tell me about a bad technical decision you made.**
Own a real one, no humble-brags. Explain the context and why it seemed right, what went wrong, how you caught and corrected it, and the lesson you carry forward. This probes self-awareness and growth — the willingness to be wrong is a seniority signal.

**How do you mentor junior developers?**
Concrete examples: pairing, code review framed as teaching not gatekeeping, giving stretch tasks with a safety net, explaining the *why* behind feedback, and growing autonomy over time. Result: someone who leveled up. Shows you scale your impact through others, not just your own commits.

**How do you handle technical debt?**
Make it visible and quantify its cost (slower delivery, more bugs), then negotiate — pay it down opportunistically alongside feature work, reserve capacity each sprint, and fix high-interest debt first. Frame it to stakeholders in terms of risk and velocity, not purity. Shows pragmatism and business awareness.

**How do you push back on scope or an unrealistic deadline?**
Bring data, not complaints: present the estimate, the trade-offs, and options (cut scope, phase delivery, add risk-acceptance, or move the date). Let stakeholders choose with full information. The senior move is turning "no" into "here are the trade-offs — which do you want?"

> **Follow-up:** *Tell me about a time you had to make a decision without complete information.* Show how you bounded the risk: made a reversible choice, shipped small to learn, set a checkpoint to re-evaluate, and communicated the uncertainty rather than pretending certainty.

Practice out loud, time yourself, and remember: interviewers hire for *reasoning you can hear*, not just answers you happen to know.

## How to Keep Learning

[Chapter 16](#chapter-16-working-like-a-middle-developer) set up the habit: a few high-signal sources, release notes, learning by building and teaching. At senior level the question changes from *how to keep up* to *where to go deep*, and the sources get closer to the metal.

### Where to Learn From

A book ends; the field does not. The half-life of a specific framework detail is short, but the habit of continuous, deliberate learning is what keeps a career compounding. Build a routine from these sources.

**Read code, not just articles.** The fastest way to level up is to read software written by people better than you. Clone Microsoft's [eShop](https://github.com/dotnet/eShop) reference application and trace how it wires up services, messaging, and .NET Aspire. When you hit a behavior you cannot explain, step into [dotnet/runtime](https://github.com/dotnet/runtime) itself — the source is public, and reading how `List<T>`, `Task`, or the GC is implemented demystifies things you have used for years.

**Follow people who teach in public.** A handful of .NET voices consistently explain the *why* behind the code:
- **Andrew Lock** — deep, careful blog posts on ASP.NET Core internals.
- **Steve Gordon** — performance, HttpClient, and runtime deep dives.
- **Nick Chapsas** — pragmatic videos on modern C# and benchmarking.
- **Milan Jovanović** — architecture, DDD, and modular monoliths.
- **Jimmy Bogard** — the mind behind MediatR and AutoMapper, and a rich source on DDD and messaging.
- **David Fowler** — a .NET architect whose threads on distributed systems and async are essential.

**Practice deliberately.** Use [Microsoft Learn](https://learn.microsoft.com) for structured, up-to-date modules when you adopt a new technology. Use [Exercism](https://exercism.org)'s C# track to sharpen fundamentals with mentored feedback. Contribute to open source — even a documentation fix or a small bug on a library you use teaches you how real projects are governed.

The goal is not to consume everything. It is to build a steady, sustainable habit: read a little real code every week, follow a few people whose judgment you trust, and always have one small learning project on the side.

### A Short Shelf of Great Books

Videos and blogs keep you current; books give you depth that lasts. These have earned permanent spots on many senior engineers' shelves. Read them slowly.

- **C# in Depth** — Jon Skeet
- **Dependency Injection Principles, Practices, and Patterns** — Mark Seemann
- **Clean Architecture** — Robert C. Martin
- **Designing Data-Intensive Applications** — Martin Kleppmann
- **Patterns of Enterprise Application Architecture** — Martin Fowler
- **Implementing Domain-Driven Design** — Vaughn Vernon
- **The Pragmatic Programmer** — Andrew Hunt and David Thomas

You do not need to read them all at once, and you should not. Pick the one that matches what your work needs now — Skeet while you deepen the language, Kleppmann when you split a system into services, Vernon when the domain modeling gets hard.

### Depth Versus Breadth, and Why You Will Return

This book gave you breadth: a map of the whole territory a senior .NET engineer is expected to traverse. Breadth is what lets you hold a conversation about anything in it and know where to dig. But breadth alone is shallow. The engineers people trust are the ones who, on top of that broad map, have gone deep in a few areas — deep enough to debug the hard cases, to make the non-obvious trade-off, to teach it to others.

So the aim is a T-shape: a wide base of competence, with a few tall spikes of genuine mastery. You cannot go deep everywhere, and trying to will only leave you exhausted and mediocre. Choose your spikes deliberately — maybe performance and the runtime, maybe distributed systems and messaging — and let the rest stay at working competence, refreshed as needed.

Come back to this book. In six months, reread the *Pay attention to* pages at the end of each part and ask honestly where you now stand. You will find that chapters which once felt abstract have become obvious, and that new chapters have quietly become relevant because your work changed. A book like this is not a certificate you earn once; it is a compass you consult repeatedly, and each time it points a little further than before.

You have the map and the habits; [Chapter 44: Capstone](#chapter-44-capstone-one-project-growing-up) gives you the project. The only thing left is to open your editor and start ShopCore. Build the thing. That is how senior engineers are made — not by finishing books, but by shipping systems and reflecting on what they cost. Go build.

## Sources & Further Reading

- **Microsoft Learn** — the authoritative reference for C#, .NET runtime/GC, ASP.NET Core, and EF Core behavior (learn.microsoft.com). Cross-check any runtime specifics here against the current docs.
- **.NET diagnostics tooling docs** — `dotnet-counters`, `dotnet-trace`, `dotnet-dump`, `dotnet-gcdump` guides on Microsoft Learn for the performance methodology in [Chapter 9](#chapter-9-exceptions-logging-and-first-diagnosis).
- **"Staff Engineer"** by Will Larson — the archetypes in *Career Growth* and the work of staff-level influence.
- **"Cracking the Coding Interview"** by Gayle Laakmann McDowell — interview strategy, behavioral framing, and algorithmic warm-ups.
- **"System Design Interview"** (Volumes 1 & 2) by Alex Xu — the structured approach behind [Chapter 23: System Design](#chapter-23-system-design).
- **"Designing Data-Intensive Applications"** by Martin Kleppmann — deep background for the distributed-systems, consistency, and CAP material.
- **OWASP Top 10** (owasp.org) — the canonical web security risk list.

## Practice

**1. The story-bank lab, Level 1 (≈ 4 h 30 min).** [Chapter 37](#chapter-37-the-story-bank-evidence-portfolio) is a lab with a kit in [`labs/36-evidence-portfolio`](https://github.com/malyna2/dotnet-handbook/tree/main/labs/36-evidence-portfolio): set up the two repos, mine a year of your own work for candidates, and draft five STAR worksheets. Levels 2 and 3 (mock interviews, a published evidence index) follow at the pace of its time budget. Your stories, numbers and worksheets go in your private story bank and **your own public portfolio repo**, never in this one; stories about a real employer stay private.

**2. Write an ADR (1 h).** Pick a decision your team made recently without a record — or your answer to an earlier chapter's *Decide*. Use the template from [Chapter 16](#chapter-16-working-like-a-middle-developer): context, decision, consequences (including the negative ones), alternatives considered, and one line on what would make you supersede it. Then ask someone who wasn't there whether they could reconstruct *why* from the ADR alone.

**3. Review a real pull request for its design first (1 h).** Before reading any line, write down: the problem it solves, whether that problem needed solving now, what it makes hard to change later, and whether a one-way door is hidden in it (a schema, a public contract, a new dependency). Then review the lines with Chapter 32's [reviewer's rubric](#judging-ai-generated-code-a-reviewers-rubric) — written for generated code, and just as good for human code. Done when every comment names its condition, its cost and its label, and the design questions came first.

**4. Estimate one piece of work twice (45 min).** For your next multi-week task, make a three-point estimate per task with PERT ([Chapter 41](#chapter-41-recommendations-proposals-and-estimates)), then an outside-view estimate from the actual duration of the last few similar pieces of work. Write down where they disagree and why, and the range and commitment level you would give. Keep the actual result next to it when the work is done: that is the start of your own reference class.

The Practice Gym's planned code-review gym (M5 in [`PRACTICE_ROADMAP.md`](https://github.com/malyna2/dotnet-handbook/blob/main/PRACTICE_ROADMAP.md)) will become this chapter's lab: ten seeded pull requests, including one clean one, scored for both detection and severity calibration.

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

- **Skew.** A task can overrun by far more than it can underrun, so each task's expected value (O + 4M + P) / 6 is above its most-likely value, and so is their sum. In Chapter 41's worked example, the sum of most-likely values is 68 person-days and the expected total about 75.
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
- **A** costs months of no visible output and the classic rewrite risk: the old module's undocumented behaviour has to be rediscovered, and the business keeps asking for features meanwhile, so the rewrite chases a moving target ([Chapter 24](#chapter-24-working-with-legacy-brownfield-code), *Why Big-Bang Rewrites Usually Fail*).
- **B** costs a routing seam, characterization tests, and a period of running two implementations side by side. Every step ships, and every step can stop.
- **C** costs nothing now and adds to the hotspot's interest: the next feature is slower again, and a "revisit later" without an owner and a trigger is never revisited.

**What decides it here: churn.** A hotspot that changes every sprint repays every hour of clean-up quickly, because the next change uses it. The export is new code that can live outside the old module from day one.

**The choice: B.** Present it in the business's units: what each change in the module costs now, the incident risk, and what the first increment buys. Write an ADR for the seam and the migration path.

**What would change it.** If the module will be frozen after this feature (being replaced, or rarely touched), C with a written record is honest and cheap. If the platform under it is reaching end of support, the migration plan has a deadline, and that deadline sets the pace of B.
</details>

## Check at work

**Inspect.** List the last three significant technical decisions in your team. For each: is there an ADR or design doc, and could a new joiner learn *why* from it? Then rank your repository's files by churn and compare the top of the list with what the team complains about:

```bash
# Files ranked by number of commits touching them (last 12 months), from Chapter 24
git log --since="12 months ago" --name-only --pretty=format: \
  | grep '\.cs$' | sort | uniq -c | sort -rn | head -30
```

**Measure.** Your own estimates against actuals for the last five pieces of work you estimated: how often the actual fell inside your range, and in which direction you missed. That list is your first reference class, and the start of your calibration.
