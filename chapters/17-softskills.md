# Chapter 17: Soft Skills & Engineering Practices

_⏱️ Estimated read time: ~29 min ·     5072 words (study pace)_

You already know how to write good C#. You can wire up dependency injection, reason about `async`/`await`, tune an EF Core query, and design a clean bounded context. That is the price of admission to being a *middle* engineer. It is not what makes you a senior one.

The uncomfortable truth is that the gap between a mid-level developer and a senior or staff engineer is only partly technical. The rest — often the larger part — is made of skills that never show up in a LeetCode problem: writing a message that gets read and acted on, estimating work you have never done, reviewing a colleague's PR so that they leave the interaction smarter and not smaller, deciding *not* to build the clever thing, and owning an outcome from a Jira ticket all the way to a 2 a.m. alert.

This chapter is the practical field guide to those skills. No platitudes — templates, scripts, checklists, and worked examples you can use on Monday.

## 17.1 From Solving Tickets to Creating Leverage

Here is the mental model that reframes everything else.

A middle engineer is measured by **throughput**: how many tickets they close, how fast, how correctly. That is real and valuable. But it scales linearly — you can only type so fast, and there are only so many hours in a week.

A senior engineer is measured by **leverage**: how much better everyone *around* them performs because of their presence. Leverage compounds. A good design doc saves ten engineers a week of rework. A sharp code review teaches a pattern that a junior then applies fifty more times without you. A well-run incident post-mortem prevents a class of outages forever.

> **The core shift: you stop being judged by the code you personally wrote, and start being judged by the good outcomes you caused — including ones where you wrote no code at all.**

Concretely, the behaviors change like this:

| Middle mindset | Senior mindset |
|---|---|
| "The ticket didn't say to handle that case." | "This will page someone at 3 a.m.; I'll handle it or flag it." |
| "It works on my machine." | "Here's how we'll know it works in production." |
| "I finished my part." | "The feature isn't done until the user is unblocked." |
| "Someone should fix this." | "I filed it, tagged the owner, and proposed a fix." |
| "That's not my code." | "I'll leave it a little better than I found it." |

None of this requires a title change or permission. You can start acting with leverage today, and the title tends to follow the behavior rather than precede it.

## 17.2 Communication: The Real Superpower

Most engineering failures I have watched up close were not failures of code. They were failures of communication — a misread requirement, an assumption nobody voiced, a decision made in a hallway that three teams never heard about.

### Written communication as async leverage

Remote and hybrid work made clear writing the single highest-leverage skill you can develop. A well-written message is read once and understood by twenty people across three time zones without a meeting. A muddy one generates a thread of forty replies and a "quick call to align."

Principles for writing that gets read:

- **Bottom line up front (BLUF).** State the conclusion, decision, or ask in the first sentence. Busy people decide whether to keep reading based on line one.
- **Make the ask explicit.** "I need a yes/no on X by Thursday" beats "let me know your thoughts."
- **Structure for scanning.** Headers, bullets, bold for the load-bearing sentence. Nobody reads a wall of text.
- **Write for the reader who knows the least** among the people who must act.

Compare:

> *Bad:* "Hey, so I was looking into the payment thing and there's kind of a lot going on with the retries and I think maybe we have an issue but not sure, can we talk?"

> *Good:* "**Decision needed by Fri:** should we cap payment retries at 3? Right now failed charges retry indefinitely, which double-charged 4 customers last week (INC-231). I recommend a hard cap of 3 with a dead-letter queue. Details below. 👇"

The second one might get resolved in a single reply. The first guarantees a meeting.

### Explaining trade-offs to non-technical stakeholders

Seniors translate. A product manager does not care about connection pooling; they care about cost, risk, and time. Your job is to map the technical reality onto *their* decision variables.

The translation pattern: **Options → consequences in their terms → your recommendation → what you need from them.**

> **Engineer-to-engineer:** "We should introduce a read replica and move the reporting queries off the primary, otherwise the N+1 in the dashboard is going to keep saturating the write DB."

> **Same thing, to a PM:** "The dashboards are slowing down the checkout database, which is why some users saw errors on Black Friday. I see two paths. **Option A:** a two-day fix that solves 80% of it, ships this sprint. **Option B:** a proper solution that also prepares us for next year's traffic — about a week, needs one more engineer. Given the holiday deadline, I recommend A now and we schedule B for Q1. I need you to confirm the deadline and whether the extra engineer is available."

Notice: no jargon, quantified impact, framed as a choice, with a clear recommendation and a specific ask. That is the senior move.

### Tailoring the message to the audience

The same information gets packaged differently depending on who is receiving it:

- **To your manager:** status, risks, and what you need unblocked. Lead with the risk.
- **To a peer:** technical detail, trade-offs, room to disagree.
- **To an executive:** business impact and one number. They have thirty seconds.
- **To a junior:** context and the "why," so they can generalize next time.

### Running meetings that don't waste an hour × N people

A meeting is one of the most expensive things a company does — multiply the duration by the salaries in the room. Respect that cost.

A meeting checklist:

- **Agenda in the invite,** or decline it. No agenda, no meeting.
- **Name the meeting's job:** decision, brainstorm, or broadcast. Different meetings, different rules.
- **The decider is in the room.** A decision meeting without the decider is theater.
- **Timebox each item.**
- **End with:** decisions made, action items with owners and dates, and where they're written down.
- **If it could have been a doc, make it a doc.**

### Disagreeing productively and managing up

To disagree without turning it into a fight, argue about the problem, not the person, and lead with curiosity:

> "Help me understand the reasoning — I'm worried that approach couples us to the vendor's API. What am I missing?"

That framing invites information rather than triggering defense. And when the decision goes against you after a fair hearing, you **disagree and commit**: you voiced the objection clearly, it was heard, the call was made, and now you support it fully. Re-litigating a settled decision is how you lose trust.

**Managing up** means making your manager's job easier: no surprises, bring problems *with* a proposed solution, and tell them what you need rather than expecting them to guess. "I'm blocked on the security review and it'll slip the release two days unless we escalate — can you ping their lead?" is worth more than silent heroics followed by a missed date.

## 17.3 Code Review Mastery

Code review is where craft, teaching, and team culture intersect every single day. Done well it spreads knowledge and raises the floor. Done badly it becomes a gauntlet of ego and bikeshedding.

### Giving feedback: kind, specific, actionable

Every review comment should be at least two of those three, and ideally all three. The gold standard: explain the *why*, offer a concrete alternative, and keep the tone collaborative.

> *Bad:* "This is wrong."

> *Bad:* "Why would you do it this way??"

> *Good:* "This `async void` will swallow exceptions — if `SendAsync` throws, we'll never see it and the message is silently lost. Can we make it `async Task` and let the caller await it? See how `NotificationService` does it."

The good version names the concrete risk, explains the consequence, proposes a fix, and points at a local example. The author knows exactly what to do and *why*.

### Conventional comments: label your intent

A tiny convention removes enormous ambiguity — prefix each comment with its type so the author knows what is blocking versus optional:

- **`blocking:`** must be addressed before merge (bug, security, data loss).
- **`suggestion:`** I'd prefer this, but your call.
- **`nit:`** trivial/style, non-blocking, feel free to ignore.
- **`question:`** I genuinely don't understand; not necessarily a problem.
- **`praise:`** this is genuinely nice — call it out. (Yes, praise in reviews. It's free and it works.)

```
nit: extra blank line here.

question: is `userId` guaranteed non-null at this point? If it can be
null we'll NRE on line 42.

blocking: this SQL is built with string concatenation — that's an
injection vector. Use a parameterized query.

praise: nice use of a discriminated result type here, much clearer
than the old bool-and-out-param.
```

The distinction between **nitpicks and blockers** is what keeps reviews moving. If everything is presented with equal weight, a whitespace comment stalls a PR as long as a security hole. Be explicit, and let people merge over your nits.

### The author's responsibilities

Review quality is a two-way street. As the author:

- **Keep PRs small.** A 200-line PR gets a real review; a 2,000-line PR gets a "LGTM 👍" that catches nothing. Slice work so PRs stay reviewable.
- **Write a description that answers *why*.** What problem, what approach, what you considered and rejected, how to test it. Link the ticket.
- **Review your own diff first.** Half your reviewers' comments were things you'd have caught by reading it yourself.
- **Leave breadcrumbs** on tricky lines: a comment on the PR saying "did it this way because X" pre-empts the question.

### Receiving feedback without ego

Your code is not you. A comment on your PR is a gift of someone's attention. Practical habits:

- Assume good intent; the terse comment is usually haste, not contempt.
- Say "good catch" and mean it. Fix it, or explain your reasoning and open a discussion.
- If a reviewer misunderstood, that's often a signal the *code* is unclear — consider a comment or rename rather than just replying in the thread.
- Don't argue every nit. Take most, push back on the few that matter, move on.

> **Review is teaching in both directions. Every PR is a chance to make the other person — author or reviewer — a slightly better engineer. Optimize for that, not for winning.**

## 17.4 Estimation & Planning

Estimates are hard because you are predicting the unknown — and software work is disproportionately made of unknowns. The **planning fallacy** (we systematically underestimate our own tasks even when we know similar tasks ran long) is not a personal flaw you can will away; it is a cognitive bias to design around.

Techniques that actually help:

- **Slice into thin vertical slices.** Break work down until each piece is something you can *picture doing*. A task you can't estimate is a task you don't understand yet — that's the signal, not the failure. Prefer slices that each deliver a sliver of end-to-end value over horizontal layers ("do all the DB work") that deliver nothing until the last one lands.
- **Spike the unknowns.** When uncertainty dominates, don't estimate — timebox a **spike**: "8 hours to prototype the third-party integration, then we'll estimate the real work." You're buying information.
- **Estimate ranges, not points.** "3 to 5 days" is more honest than "4 days," and it communicates uncertainty. Widen the range when you're less sure.
- **Buffer for the invisible work:** code review, testing, meetings, the CI flake, the environment that's down. The coding is often the smallest slice.
- **Communicate estimates as forecasts, not promises.** "Based on what I know now, I expect this in the first half of next week. The biggest risk is the payment vendor's sandbox — if that's flaky, add two days." You've given a number *and* the assumptions it rests on.

Avoid the **sunk-cost trap**: "we've already spent three weeks on this approach" is not a reason to spend a fourth. Past effort is gone regardless; decide based on the cost and value *from here*. A senior says out loud, "I know we've invested a lot, but continuing is the more expensive path now."

## 17.5 Technical Writing & Documentation

Code says *what* the system does. Documentation captures *why* — the context that is otherwise lost the moment it leaves your head.

### Architecture Decision Records (ADRs)

An ADR is a short, immutable document recording one significant decision, its context, and its consequences. They are cheap to write and priceless eighteen months later when someone asks "why on earth did we use Kafka here?"

A template:

```markdown
# ADR-014: Use Outbox Pattern for Order Event Publishing

- Status: Accepted
- Date: 2026-07-21
- Deciders: Payments team
- Supersedes: —

## Context
We publish an "OrderPlaced" event to the message bus after saving an
order. Currently we save to the DB and publish in the same method,
without a shared transaction. If the publish fails after the DB commit,
downstream services never learn about the order — we've seen 3 such
drops this quarter (INC-198, INC-201, INC-217).

## Decision
Adopt the Transactional Outbox pattern: within the same DB transaction
that saves the order, insert an event row into an `Outbox` table. A
background dispatcher polls the table and publishes to the bus, marking
rows as sent. This makes DB write and event intent atomic.

## Consequences
Positive:
- Event publishing is now at-least-once and crash-safe.
- The DB transaction remains the single source of truth.

Negative / trade-offs:
- Added latency (poll interval, ~1s) before events are published.
- New moving part (dispatcher) to run and monitor.
- Consumers must be idempotent (at-least-once => possible duplicates).

## Alternatives considered
- 2-phase commit across DB and broker: rejected, operationally heavy,
  poor support in our stack.
- Publish-then-save: rejected, inverts the source-of-truth problem.
```

Keep ADRs in the repo (`/docs/adr/`) so they version with the code. Never edit an accepted ADR to reverse it — write a new one that supersedes it. The history *is* the value.

### READMEs, design docs, and runbooks

- **README:** what this is, how to run it locally, how to test it, where to get help. Optimize for the new joiner staring at a fresh clone. If the setup steps are stale, the README is worse than nothing.
- **Design doc / RFC:** written *before* building something significant. States the problem, goals and non-goals, proposed design, alternatives, and open questions. Its real purpose is to make thinking reviewable *before* code is written, when changing direction is cheap. Run a **design review** by circulating it async first, collecting comments in the doc, then meeting only to resolve the genuine disagreements — not to hear it read aloud.
- **Runbook:** the operational manual for a service. "Alert X fires → check dashboard Y → if queue depth > 1000, scale the workers with this command → if that doesn't help, escalate to Z." The runbook is what lets a tired on-call engineer act correctly at 3 a.m. without paging you.

### Comment the *why*, not the *what*

```csharp
// Bad: restates the code
// increment retry count by one
retryCount++;

// Good: explains the non-obvious reason
// Vendor rate-limits us to 3 attempts per minute; a 4th attempt gets
// the whole IP banned for an hour, so we cap hard here. See ADR-014.
if (retryCount >= 3) return Result.Fail("retry cap reached");
```

Good code is self-documenting about *what*. Comments earn their keep by capturing the *why* — the constraint, the gotcha, the link to the decision — that the code itself cannot express.

## 17.6 Methodical Debugging & Problem Solving

Junior engineers debug by changing things and hoping. Seniors debug like scientists: with hypotheses and evidence.

The loop:

1. **Reproduce it first.** A bug you can reproduce on demand is 80% solved. A bug you can't reproduce, you can't verify you fixed. Invest in a reliable repro before anything else.
2. **Read the actual error.** The full message, the full stack trace, the inner exception. The answer is astonishingly often right there in text people skimmed past.
3. **Form a hypothesis.** "I think the null comes from the cache returning a stale entry." A specific, falsifiable statement.
4. **Test the one hypothesis.** Change one thing. If you change five things and it works, you've learned nothing and may have added two new bugs.
5. **Binary-search the problem space.** Bug appeared somewhere in 200 commits? `git bisect`. Somewhere in a pipeline of ten stages? Log at stage five; you've halved the search. Halving beats linear scanning every time.

**Rubber-ducking** works because explaining the problem out loud forces you to make your assumptions explicit, and the wrong one usually reveals itself mid-sentence. Explain it to a colleague, a literal duck, or a comment box — the medium doesn't matter, the articulation does.

> **The 30-minute rule: struggle productively on your own for about 30 minutes, then ask for help.** Less, and you rob yourself of the learning that comes from wrestling with it. More, and you're just burning the team's time on something a colleague could unstick in two minutes. When you ask, show what you tried and what you expected — a good question is itself a sign of seniority, not weakness.

### Blameless post-mortems

When something breaks in production, the goal is to fix the *system*, not to find a person to blame. Blame makes people hide information, which makes the next outage worse. A blameless post-mortem assumes everyone acted reasonably given what they knew, and asks how the system let a reasonable action cause an outage.

A lightweight structure: **what happened** (timeline), **impact** (who/how much), **root cause** (the *why*, dug several levels deep — the deploy wasn't the root cause, the *lack of a canary* was), and **action items** (concrete, owned, dated). Focus every action on making the failure impossible or detectable, not on "be more careful."

## 17.7 Safe Change, Refactoring & Tech Debt

Changing code you don't fully understand is where careers are made or dented. The senior approach is disciplined.

- **Refactor under a green test suite.** Refactoring means changing structure *without* changing behavior — and the only way to know behavior didn't change is tests. No coverage on the code you're about to refactor? Write **characterization tests** first: tests that pin down what the code *currently* does (even if it's wrong), so you'll notice if you change it.
- **Separate refactoring commits from behavior changes.** A PR that both moves code around *and* changes what it does is nearly impossible to review. "Refactor: extract `PriceCalculator`" and "Fix: apply loyalty discount before tax" should be two commits, ideally two PRs.
- **The boy-scout rule:** leave the code a little cleaner than you found it. Fix the confusing name, add the missing test, delete the dead branch — small, in-scope improvements, not a surprise rewrite bolted onto a bugfix.

### Making tech debt visible to the business

Tech debt is invisible to non-engineers until it manifests as slow delivery or an outage. Your job is to make it visible *before* that, in their language:

> "The reporting module has no tests and three of us are afraid to touch it. Right now every change there takes 3× as long and risks breaking billing. If we spend one sprint adding tests and splitting it up, feature work in that area gets roughly twice as fast afterward. I'd like to schedule that for next sprint."

Note the framing: not "the code is ugly" (aesthetic, ignorable) but "this costs us velocity and risks revenue, here's the payoff of fixing it" (a business trade-off they can prioritize). Track debt as visible tickets, not private grumbling. Deliberate, communicated debt ("we'll ship the quick version now and fix it in Q1, tracked as TECH-88") is a legitimate tool. *Undocumented* debt is the dangerous kind.

## 17.8 Mentoring, Pairing & Growing Others

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

## 17.9 Agile in Practice (Not Cargo-Cult)

Most teams "do Agile." Fewer do it well. The difference is whether the ceremonies serve the work or the work serves the ceremonies.

**Scrum vs Kanban, briefly.** Scrum organizes work into fixed sprints with commitments and roles; it suits teams that benefit from a planning rhythm and relatively stable priorities. Kanban is a continuous flow with **WIP (work-in-progress) limits** and no fixed sprints; it suits interrupt-driven work like support or platform teams. Neither is holy. Pick for your context.

Ceremonies done well vs cargo-cult:

- **Standup.** *Well:* a 10-minute sync to surface blockers and coordinate — "I'm stuck on X, can someone pair after?" *Cargo-cult:* everyone recites yesterday's tasks to the manager as a status report while others zone out. If your standup is a status report, kill it and use a written update.
- **Retro.** *Well:* the team honestly inspects how it works and commits to one or two concrete changes, then *actually does them* next sprint. *Cargo-cult:* the same complaints every fortnight, no action, so people stop bothering.
- **Refinement.** *Well:* the team clarifies upcoming stories, slices big ones, surfaces unknowns *before* the sprint. *Cargo-cult:* stories go into the sprint vague and half the sprint is spent figuring out what they meant.

**WIP limits** are the most underused idea in the whole toolkit: starting five things finishes zero. Limiting how much is in flight *forces* the team to finish and ship before starting more, which counterintuitively increases throughput. Prefer finishing to starting.

## 17.10 Judgment & Influence

Seniority is largely **judgment** — knowing which of the many technically-correct options is the *right* one here, and getting people to go along with it without a title forcing them to.

### Knowing when NOT to add complexity (YAGNI)

The most expensive code is the code you didn't need. **YAGNI — You Aren't Gonna Need It** — means: don't build the generic plugin framework for the one case you have today. Mid-level engineers are often seduced by the flexible, abstract, future-proof design. Seniors have been burned by unused abstractions enough to prefer the simplest thing that solves the *actual* problem, and to add complexity only when a *second real* need proves it's warranted. When someone proposes a speculative abstraction, the senior question is: "What concrete requirement, that exists today, needs this?"

### Picking battles and influencing without authority

You cannot fight every fight; you'll exhaust your credibility and be tuned out. Decide whether a given issue is a hill worth dying on. A security hole or data-loss bug: yes, plant the flag. Tabs vs spaces when there's a linter: absolutely not — defer and move on. Save your capital for what matters.

Driving decisions **without authority** is the staff-engineer skill. You can't order anyone; you influence through:

- **Trust,** earned by being right, being honest about uncertainty, and following through on commitments over time.
- **Data,** so it's not your opinion vs theirs but a shared look at evidence.
- **Bringing people along early** — socialize an idea in one-on-ones before the big meeting, so by the time you present, the key people already nod. Decisions are usually made in the hallway, not the meeting.
- **Framing in others' interests:** show how your proposal helps *their* goals, not just that it's technically superior.

> **Reputation is your real currency, and it compounds. Be the person whose estimates are honest, whose reviews are fair, whose commitments land, and who says "I was wrong" when they were. That reputation, built over years, is what lets you move a decision with a single Slack message.**

## 17.11 Ownership & Professionalism

The single word that most separates senior from mid-level is **ownership**.

### End-to-end ownership

A senior doesn't consider a feature "done" when the code merges. Done means: it's deployed, it works in production, it's monitored, and the user's problem is actually solved. Ownership spans the whole arc — ticket → design → code → review → deploy → verify in prod → watch the dashboards → follow up on the edge case that showed up a week later. When something in your area breaks, you don't ask "is this my job?" — you make sure it gets fixed and stays fixed.

### Reliability, on-call, and follow-through

- **On-call basics:** know your service's runbooks, alerts, and dashboards *before* you're paged. When an alert fires, stabilize first (stop the bleeding), diagnose second, and write up what happened after. Never silence an alert without fixing what it warned about.
- **Reliability mindset:** ask "how will this fail, and how will we know?" *before* it ships. Add the log, the metric, the alert as part of the feature, not after the incident.
- **Follow-through** is the quiet superpower: do what you said, by when you said, or renegotiate *proactively* the moment you know you can't. The engineer whose word is reliable becomes the one everyone routes the important work to.

### Managing your energy and avoiding burnout

Seniority is a marathon; you can't sprint for years. Sustainable practices are professional, not indulgent:

- Protect **focus time** — batch shallow work, guard blocks of deep work, turn off notifications.
- Sustainable pace beats hero crunches. The all-nighter that ships Friday costs you the whole next week in bugs and fatigue. Consistency wins.
- Notice the burnout signs — cynicism, exhaustion, dread — early, and act (rest, rescope, talk to your manager) before they become a crisis. You can't create leverage while running on empty.

### Continuous learning

The .NET ecosystem moves; the fundamentals move slowly but the tools shift yearly. Build a lightweight habit rather than sporadic cramming: follow a few high-signal sources, read the release notes for each major .NET version, keep a running list of things to learn, and — most effective — learn by *building* small things and by *teaching* what you just learned. Teaching forces the gaps into the open.

## 17.12 Career Growth: Toward Senior and Staff

Finally, steer your own growth deliberately instead of hoping it happens.

**Seek feedback actively** rather than waiting for the annual review. Ask specific questions: "What's one thing I could do that would have the most impact on the team?" or "What would you need to see for me to be considered senior?" Vague questions ("any feedback?") get vague answers; specific ones get gold.

**Find sponsors, not just mentors.** A mentor advises you; a sponsor advocates for you in rooms you're not in. Do visible, high-value work, make sure the right people know you did it (without bragging — let the results and a clear write-up speak), and build relationships with people who have influence over your trajectory.

**Understand the staff-engineer archetypes.** Senior-and-beyond is not one path; the well-known shapes (drawn from Will Larson's *Staff Engineer*) are:

- **Tech Lead:** guides the execution of a team — the "how" and "who" of delivery, closest to a team's day-to-day.
- **Architect:** owns the technical direction and quality of a critical area across teams.
- **Solver:** the person dropped onto the gnarliest, most ambiguous problem to untangle it.
- **Right Hand:** operates as an extension of an engineering leader, carrying organizational leverage.

You don't have to pick forever, but knowing which one energizes you tells you which skills to lean into. A Solver invests in debugging and systems depth; a Tech Lead in communication and planning; an Architect in design and cross-team influence.

> **The through-line of this entire chapter: senior engineering is the multiplication of impact through other people and good judgment, not the maximization of your personal code output. Every skill here — clear writing, kind reviews, honest estimates, blameless post-mortems, deliberate mentoring, sound judgment, real ownership — is a lever. Master the levers, and your impact stops being bounded by your own two hands.**

You already have the technical foundation. The path from middle to senior runs straight through this chapter. Start with one lever — pick the weakest one — and practice it deliberately this week.

## Exercises

The drills in the technical chapters have answers you can check against a compiler. These do not, which is the point — the skills in this chapter are judgment, and judgment is practised by reasoning through situations before you are in them.

### What would you do — the estimate

Your product manager asks how long a feature will take. You genuinely do not know: it depends on whether a third-party API supports bulk operations, which the documentation does not say. They need a number for a roadmap slide by end of day. Saying "I don't know" has not gone well before.

<details>
<summary>How a senior engineer reasons about it</summary>

The trap is treating this as a choice between a number you don't believe and a refusal. It is neither.

What the PM actually needs is not a number — it is the ability to plan. Those are different, and the second is something you can give honestly:

- **Name the uncertainty and its size.** "If the API supports bulk operations, about a week. If it doesn't, we need a queue and retry handling, which is closer to three. I can find out which by tomorrow afternoon."
- **Offer to buy the information.** A half-day spike converts an unbounded range into a real estimate. Almost every PM will take that trade, because a roadmap built on a fabricated number is their problem, not yours, and they know it.
- **If the slide truly cannot wait**, give the range with the assumption attached in writing — "3 weeks, assuming no bulk API; I'll confirm Thursday" — and follow up when you know. The written assumption is what protects both of you later.

What not to do: give the optimistic number because it is the one that ends the conversation. That is the estimate that becomes a commitment in someone else's spreadsheet, and the cost is paid in six weeks by you.

The underlying principle: your job in estimation is to transfer your uncertainty accurately, not to eliminate it for the listener's comfort.
</details>

### What would you do — the review

You are reviewing a PR from an engineer who joined three weeks ago. The feature works and the tests pass. The code also uses a pattern your team abandoned two years ago for good reasons, has three functions that each do two things, and names a variable `data`. It is Friday afternoon and they are clearly proud of it.

<details>
<summary>How a senior engineer reasons about it</summary>

Two separate questions are hiding here, and conflating them is what makes code review go badly.

**What must change before merge?** Only what is genuinely load-bearing: the abandoned pattern, if it will cause real problems or spread. Naming and function decomposition are worth mentioning but are not merge blockers on a working feature from someone still learning the codebase.

**What are you actually teaching?** A new joiner is calibrating on this review — not just on what you said, but on how much of it there is. Twenty comments reads as "you did badly," regardless of what each one says. Three comments with reasoning attached reads as "here is how we think here."

So: pick the one structural thing, explain *why* the team moved away from that pattern (the reason, not the rule — they cannot infer institutional history), and offer to pair on it rather than leaving them to guess at what you want. Mark the nits explicitly as nits, or leave them for a follow-up. Say what was good, specifically, because you have information they don't: which parts were hard.

And the meta-point about Friday afternoon: if the change is not urgent, a review that lands as a conversation on Monday is often better than one that lands as a wall of text at 5pm. Delivery timing is part of the message.
</details>

### Go check

- Find a decision your team made in the last six months that is not written down anywhere. Write the ADR for it — context, decision, consequences, alternatives — and share it. Notice how much you had to reconstruct, and how much of the reasoning nobody remembers.
- Read the last three PRs you reviewed. Count how many of your comments explained *why* versus stated *what*. Count how many were nits with no label.
- Look at your last estimate that was wrong. Was it wrong because the work was harder than you thought, or because you estimated a different scope than the one you were handed? These have different fixes.
- Ask one person you work with what they wish you did differently. Then say nothing except "thank you" and think about it for a week.
