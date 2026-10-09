# Chapter 16: Working Like a Middle Developer

The previous fifteen chapters are about code. This one is about the work around it, which decides how much of that code ends up useful: a message that gets read and acted on, a pull request reviewed so the author leaves smarter rather than smaller, an estimate for work you have never done, a bug found by method rather than luck, a change made safely in code you don't fully understand, and an outcome owned from the ticket to the alert. The chapter makes you able to take a vague ticket, a request for an estimate or a pull request, and send back something a teammate can act on without a meeting.

One habit ties it together: **turn every guess into a claim someone else can check, with the condition or assumption it rests on, in writing, before anyone builds on it.** People act on a claim they can verify in a minute and ignore or argue with one they can't; and a misunderstanding costs more with every step built on it, from one reply before the code exists to an incident after release. A problem statement makes your reading of a ticket checkable, a range names the assumption that drives an estimate, a review comment states the condition, mechanism, cost and fix, and a request for help shows what you already ruled out.

The sections follow a piece of work through a team: from throughput to leverage, communication, code review, estimation, technical writing, debugging, safe change and tech debt, agile ceremonies, and ownership. Then the practice: four written tasks, three questions, a check at work and two judgment exercises. Mentoring, influence and the path to senior continue in [Chapter 36: Senior Behaviours, Career and Interviews](#chapter-36-senior-behaviours-career-and-interviews).

## From Solving Tickets to Creating Leverage

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

None of this needs a title or permission; the title tends to follow the behavior.

## Communication: The Real Superpower

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

### Asking Questions That Unblock You

A question unblocks you when the person answering doesn't have to ask you three questions back first. Send the state of your thinking, not only the gap in it.

**In writing**, when you are stuck past your timebox ([Methodical Debugging](#methodical-debugging-problem-solving)) or a ticket is vague:

- **The goal, not only your attempted fix.** "How do I make the CI step wait 30 seconds?" gets you a sleep; "the integration tests start before the database is ready" gets you a health check. Asking about your attempted solution instead of the problem is the *XY problem*: helpers solve the wrong thing well.
- **What you tried, and what each attempt ruled out.** The helper skips your first half hour and often spots the wrong assumption at a glance.
- **The exact error, pasted, and one specific ask:** a yes or no, a name, a pointer.
- **For a vague ticket,** a short problem statement (symptom and evidence, target, constraints, out of scope; [Chapter 39](#the-one-page-problem-statement) has the full template) with your questions at the end, before any code.

**In meetings:**

- **Prepare one question from the agenda.** The one you think of an hour later costs another meeting.
- **Ask early.** Once the room has converged on a plan, a question sounds like an objection, and people defend what they have just said in public.
- **State the assumption you are testing.** "I'm assuming the export only needs the current month. Is that right?" gets a yes or a correction; "how does the export work?" gets a tour.
- **Confirm in writing afterwards:** "To confirm: current month only, nightly, [name] owns it." Everyone leaves a meeting remembering it differently, and a written line gets corrected while that is still cheap.

### Disagreeing productively and managing up

To disagree without turning it into a fight, argue about the problem, not the person, and lead with curiosity:

> "Help me understand the reasoning — I'm worried that approach couples us to the vendor's API. What am I missing?"

That framing invites information rather than triggering defense. And when the decision goes against you after a fair hearing, you **disagree and commit**: you voiced the objection clearly, it was heard, the call was made, and now you support it fully. Re-litigating a settled decision is how you lose trust.

**Managing up** means making your manager's job easier: no surprises, bring problems *with* a proposed solution, and tell them what you need rather than expecting them to guess. "I'm blocked on the security review and it'll slip the release two days unless we escalate — can you ping their lead?" is worth more than silent heroics followed by a missed date.

## Code Review Mastery

Code review is where craft, teaching, and team culture intersect every single day. Done well it spreads knowledge and raises the floor. Done badly it becomes a gauntlet of ego and bikeshedding.

### Finding What to Say in a Review

A diff that compiles and passes its tests can still be wrong under a condition its tests never create. Reading line by line for style rarely finds that. Asking the same five questions of every changed line usually does, because each question supplies a condition the tests left out:

| Ask of each change | The condition | Typical defect it exposes |
|---|---|---|
| What if it runs **twice**? | A retry, a redelivery, a double-clicked button | A handler that isn't idempotent; a `POST` with no idempotency key |
| What if it runs **concurrently**? | Two requests, two instances, `MaxConcurrentCalls` above 1 | Check-then-act; a `DbContext` shared across threads; mutable static state |
| What if it runs **slowly**? | A slow dependency, a held lock, peak load | `.Result` on a request path; no timeout; a `CancellationToken` not passed on |
| What if it **fails halfway**? | A crash between two writes, an exception mid-loop | Save, then publish, with no outbox; `async void`; `catch { }` |
| What if it meets **100× the data**? | Production volume instead of seed data | N+1; an unbounded `ToListAsync()`; a filter applied after materialising |

Each "yes, that breaks" is the condition of a comment; the mechanism, the cost and the fix follow from it (next section). Two moves cover what the questions miss: compare the change with the file that already does the closest thing, since divergence from it is where new defects hide, and use [Chapter 32's rubric](#judging-ai-generated-code-a-reviewers-rubric) for the full order of reading a diff. If all five questions come back clean, approve and say which risks you checked ("retries and concurrency look safe: the claim is atomic"). The author learns what was verified, and the next reviewer knows what wasn't.

### Giving feedback: kind, specific, actionable

A comment gets acted on when the author can check it without asking you anything. That takes four parts: the **condition** under which the code misbehaves, the **mechanism** that makes it misbehave, the **cost** when it does, and the **fix**. Kind is the tone; specific and actionable are those four parts.

> *Bad:* "This is wrong."

> *Bad:* "Why would you do it this way??"

> *Good:* "blocking: if `SendAsync` throws, this `async void` method has no `Task` to carry the exception, so the `try/catch` around the call never sees it. ASP.NET Core has no `SynchronizationContext`, so the runtime rethrows it on a thread-pool thread, nothing catches it there, and the process terminates with every in-flight request. Can we make it `async Task` and await it, as `NotificationService` does?"

The good version gives the condition (`SendAsync` throws), the mechanism (no `Task`, so the exception is rethrown on the captured `SynchronizationContext` or, when there is none, on a thread-pool thread; [Chapter 4](#the-compiler-generated-state-machine) traces the path), the cost (an unhandled exception ends the process) and a fix with a local example to copy. The author can verify it in a minute, which makes acting on it cheaper than arguing with it.

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

These labels are a shortened form of [Conventional Comments](https://conventionalcomments.org/), which writes the intent and the severity separately: `issue (blocking):`, `suggestion (non-blocking):`, `nitpick:`. Either form works once the team agrees on one.

The split between **nits and blockers** keeps reviews moving. When everything carries equal weight, a whitespace comment stalls a PR as long as a security hole; when style points are labelled `blocking:`, the author learns your `blocking:` is negotiable and argues the next real one. Label honestly, and let people merge over your nits.

### The author's responsibilities

Review quality is a two-way street. As the author:

- **Keep PRs small.** A 200-line PR gets a real review; a 2,000-line PR gets a "LGTM 👍" that catches nothing. Slice work so PRs stay reviewable.
- **Write a description that answers *why*.** What problem, what approach, what you considered and rejected, how to test it. Link the ticket.
- **Review your own diff first.** Many review comments are things the author would have caught by reading the diff once.
- **Leave breadcrumbs** on tricky lines: a comment on the PR saying "did it this way because X" pre-empts the question.

### Receiving feedback without ego

Your code is not you. A comment on your PR is a gift of someone's attention. Practical habits:

- Assume good intent; the terse comment is usually haste, not contempt.
- Say "good catch" and mean it. Fix it, or explain your reasoning and open a discussion.
- If a reviewer misunderstood, that's often a signal the *code* is unclear — consider a comment or rename rather than just replying in the thread.
- Don't argue every nit. Take most, push back on the few that matter, move on.

> **Review is teaching in both directions. Every PR is a chance to make the other person — author or reviewer — a slightly better engineer. Optimize for that, not for winning.**

## Estimation & Planning

Estimates are hard because you are predicting the unknown — and software work is disproportionately made of unknowns. The **planning fallacy** (we systematically underestimate our own tasks even when we know similar tasks ran long) is not a personal flaw you can will away; it is a cognitive bias to design around.

Techniques that actually help:

- **Slice into thin vertical slices.** Break work down until each piece is something you can *picture doing*. A task you can't estimate is a task you don't understand yet — that's the signal, not the failure. Prefer slices that each deliver a sliver of end-to-end value over horizontal layers ("do all the DB work") that deliver nothing until the last one lands.
- **Spike the unknowns.** When uncertainty dominates, don't estimate — timebox a **spike**: "8 hours to prototype the third-party integration, then we'll estimate the real work." You're buying information.
- **Estimate ranges, not points.** "3 to 5 days" is more honest than "4 days," and it communicates uncertainty. Widen the range when you're less sure.
- **Buffer for the invisible work:** code review, testing, meetings, the CI flake, the environment that's down. The coding is often the smallest slice.
- **Communicate estimates as forecasts, not promises.** "Based on what I know now, I expect this in the first half of next week. The biggest risk is the payment vendor's sandbox — if that's flaky, add two days." You've given a number *and* the assumptions it rests on.

> **Pay attention.** **Why estimates run long, and why padding doesn't fix it.** You estimate from the inside: you list the steps you can picture and add them up. The steps that blow estimates are the ones you can't picture yet (the sandbox that's down, the migration nobody mentioned), so they are missing from the sum, and the error runs one way: work rarely finishes much faster than its known steps allow, but it can run several times longer. Padding is a guess about that error that nobody can check. Anchor on the *outside view* instead, how long similar work actually took according to your tracker, and give a range whose top depends on one named assumption. When that assumption breaks, the estimate is void: re-estimate that day, before more plans are built on the old date. Daniel Kahneman's *Thinking, Fast and Slow* describes both views.

Avoid the **sunk-cost trap**: "we've already spent three weeks on this approach" is not a reason to spend a fourth. Past effort is gone regardless; decide based on the cost and value *from here*. A senior says out loud, "I know we've invested a lot, but continuing is the more expensive path now."

## Technical Writing & Documentation

Code says *what* the system does. Documentation captures *why* — the context that is otherwise lost the moment it leaves your head.

### Architecture Decision Records (ADRs)

An ADR is a short, immutable document recording one significant decision, its context, and its consequences. They are cheap to write and priceless eighteen months later when someone asks "why on earth did we use Kafka here?"

A template:

```markdown
**ADR-014: Use Outbox Pattern for Order Event Publishing**

- Status: Accepted
- Date: 2026-07-21
- Deciders: Payments team
- Supersedes: —

**Context**
We publish an "OrderPlaced" event to the message bus after saving an
order. Currently we save to the DB and publish in the same method,
without a shared transaction. If the publish fails after the DB commit,
downstream services never learn about the order — we've seen 3 such
drops this quarter (INC-198, INC-201, INC-217).

**Decision**
Adopt the Transactional Outbox pattern: within the same DB transaction
that saves the order, insert an event row into an `Outbox` table. A
background dispatcher polls the table and publishes to the bus, marking
rows as sent. This makes DB write and event intent atomic.

**Consequences**
Positive:
- Event publishing is now at-least-once and crash-safe.
- The DB transaction remains the single source of truth.

Negative / trade-offs:
- Added latency (poll interval, ~1s) before events are published.
- New moving part (dispatcher) to run and monitor.
- Consumers must be idempotent (at-least-once => possible duplicates).

**Alternatives considered**
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

## Methodical Debugging & Problem Solving

Junior engineers debug by changing things and hoping. Seniors debug like scientists: with hypotheses and evidence.

The loop:

1. **Reproduce it first.** A bug you can't reproduce, you can't verify you fixed. A reliable repro comes before anything else.
2. **Read the actual error.** The full message, the full stack trace, the inner exception. The answer is astonishingly often right there in text people skimmed past.
3. **Form a hypothesis.** "I think the null comes from the cache returning a stale entry." A specific, falsifiable statement.
4. **Test the one hypothesis.** Change one thing. If you change five things and it works, you've learned nothing and may have added two new bugs.
5. **Binary-search the problem space.** Bug appeared somewhere in 200 commits? `git bisect`. Somewhere in a pipeline of ten stages? Log at stage five; you've halved the search. Halving beats linear scanning every time.

**Rubber-ducking** works because explaining the problem out loud forces you to make your assumptions explicit, and the wrong one usually reveals itself mid-sentence. Explain it to a colleague, a literal duck, or a comment box — the medium doesn't matter, the articulation does.

> **The 30-minute rule: struggle productively on your own for about 30 minutes, then ask for help.** Less, and you skip the search that teaches you the system. More, and you spend hours on what a colleague could unstick in two minutes. Set the limit before you start: once you're stuck, every next attempt looks like the one that will work, so a decision made then always says "one more try." When you ask, send what you tried and what it ruled out ([Asking Questions That Unblock You](#asking-questions-that-unblock-you)); a good question is a sign of seniority, not weakness.

### Blameless post-mortems

When something breaks in production, the goal is to fix the *system*, not to find a person to blame. Blame makes people hide information, which makes the next outage worse. A blameless post-mortem assumes everyone acted reasonably given what they knew, and asks how the system let a reasonable action cause an outage.

A lightweight structure: **what happened** (timeline), **impact** (who/how much), **root cause** (the *why*, dug several levels deep — the deploy wasn't the root cause, the *lack of a canary* was), and **action items** (concrete, owned, dated). Focus every action on making the failure impossible or detectable, not on "be more careful."

## Safe Change, Refactoring & Tech Debt

Changing code you don't fully understand is where careers are made or dented. The senior approach is disciplined.

- **Refactor under a green test suite.** Refactoring means changing structure *without* changing behavior — and the only way to know behavior didn't change is tests. No coverage on the code you're about to refactor? Write **characterization tests** first: tests that pin down what the code *currently* does (even if it's wrong), so you'll notice if you change it.
- **Separate refactoring commits from behavior changes.** A PR that both moves code around *and* changes what it does is nearly impossible to review. "Refactor: extract `PriceCalculator`" and "Fix: apply loyalty discount before tax" should be two commits, ideally two PRs.
- **The boy-scout rule:** leave the code a little cleaner than you found it. Fix the confusing name, add the missing test, delete the dead branch — small, in-scope improvements, not a surprise rewrite bolted onto a bugfix.

### Making tech debt visible to the business

Tech debt is invisible to non-engineers until it manifests as slow delivery or an outage. Your job is to make it visible *before* that, in their language:

> "The reporting module has no tests and three of us are afraid to touch it. Right now every change there takes 3× as long and risks breaking billing. If we spend one sprint adding tests and splitting it up, feature work in that area gets roughly twice as fast afterward. I'd like to schedule that for next sprint."

Note the framing: not "the code is ugly" (aesthetic, ignorable) but "this costs us velocity and risks revenue, here's the payoff of fixing it" (a business trade-off they can prioritize). Track debt as visible tickets, not private grumbling. Deliberate, communicated debt ("we'll ship the quick version now and fix it in Q1, tracked as TECH-88") is a legitimate tool. *Undocumented* debt is the dangerous kind.

## Agile in Practice (Not Cargo-Cult)

Most teams "do Agile." Fewer do it well. The difference is whether the ceremonies serve the work or the work serves the ceremonies.

**Scrum vs Kanban, briefly.** Scrum organizes work into fixed sprints with commitments and roles; it suits teams that benefit from a planning rhythm and relatively stable priorities. Kanban is a continuous flow with **WIP (work-in-progress) limits** and no fixed sprints; it suits interrupt-driven work like support or platform teams. Neither is holy. Pick for your context.

Ceremonies done well vs cargo-cult:

- **Standup.** *Well:* a 10-minute sync to surface blockers and coordinate — "I'm stuck on X, can someone pair after?" *Cargo-cult:* everyone recites yesterday's tasks to the manager as a status report while others zone out. If your standup is a status report, kill it and use a written update.
- **Retro.** *Well:* the team honestly inspects how it works and commits to one or two concrete changes, then *actually does them* next sprint. *Cargo-cult:* the same complaints every fortnight, no action, so people stop bothering.
- **Refinement.** *Well:* the team clarifies upcoming stories, slices big ones, surfaces unknowns *before* the sprint. *Cargo-cult:* stories go into the sprint vague and half the sprint is spent figuring out what they meant.

**WIP limits** are the most underused idea in the whole toolkit: starting five things finishes zero. Limiting how much is in flight *forces* the team to finish and ship before starting more, which counterintuitively increases throughput. Prefer finishing to starting.

## Ownership & Professionalism

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

Ownership extends to your own skills: the tools shift yearly, so keep a lightweight learning habit rather than sporadic cramming. [Chapter 36: Keep Learning](#keep-learning) turns that into a practice.

## Prove it

This chapter's hands-on is written. Do each task before you open its answer; the answer is one good version, not the only one.

**1. Review before you run.** [Chapter 11](#chapter-11-messaging-and-background-work)'s *Prove it* prints the `CheckThenAct` experiment, [`verify/path/CheckThenAct/Program.cs`](https://github.com/malyna2/dotnet-handbook/blob/main/verify/path/CheckThenAct/Program.cs). Treat its first handler, `CheckThenAct`, as a pull request and write one labelled review comment. Then run it from `verify/path` with `dotnet run --project CheckThenAct` and check your prediction against the output.

<details>
<summary>A comment that lands</summary>

```text
blocking: when the broker delivers a message twice and the copies run at the
same time (MaxConcurrentCalls above 1, or a redelivery after lock expiry that
overlaps a slow first attempt), both copies pass ContainsKey before either
records the id, so the card is charged twice. Claim the id before the effect,
in one atomic step: TryAdd here; in the database, an insert under a unique key
in the same transaction as the charge.
```

- **Condition:** two concurrent copies of one message. **Mechanism:** each `ConcurrentDictionary` call is thread-safe, but the check and the record are separate calls with the whole charge between them, so both copies fit in the gap. **Cost:** a double charge. **Fix:** claim first, atomically. The run agrees: the check-then-act handler charges the card 2 times, the claim-first handler once.
- **Why `blocking:`.** Money moves twice. Any lower label on a data-integrity defect tells the author it is optional.
</details>

**2. Rewrite three weak comments.** Each is a common kind of comment on a common kind of line. Rewrite it as condition → mechanism → cost → fix, with the right label.

```text
a) On:      var client = new HttpClient();          // in a method called per request
   Comment: Don't create HttpClient like this.

b) On:      var user = _users.GetAsync(id).Result;  // in a controller action
   Comment: Use async.

c) On:      var data = await _db.Invoices.Where(i => i.Status == Status.Pending).ToListAsync(ct);
   Comment: blocking: bad name.
```

<details>
<summary>Model rewrites</summary>

```text
a) blocking: a new HttpClient per call is a new handler, so a new connection
   pool and a new TCP connection on every request. Each closed connection
   holds its local port in TIME_WAIT, so under load the service runs out of
   ports and outgoing calls fail. Inject a typed client from
   IHttpClientFactory, as PaymentsClient does.

b) blocking: .Result holds this request's thread-pool thread until the query
   returns. Under load every in-flight request holds one, the pool adds
   threads slowly, and requests queue while the CPU stays low. Make the
   action async and await GetAsync.

c) nit: "data" doesn't say what it holds; pendingInvoices would.
```

- **(a) and (b) were right but unactionable.** The author learned *what* you disliked, not *why*, so arguing or ignoring was cheaper than checking. The mechanism is what lets them verify you in a minute: [Chapter 5: Keep-Alive, Connection Pooling, and Socket Exhaustion](#keep-alive-connection-pooling-and-socket-exhaustion) for (a), [Chapter 4: The Sync-Over-Async Deadlock](#the-sync-over-async-deadlock) for (b). A local example to copy (`PaymentsClient`) makes the fix cheaper than a reply.
- **(c) was the opposite failure:** a style point labelled `blocking:`. Each over-labelled nit teaches the author that your `blocking:` is negotiable, and the next real one gets argued too.
</details>

**3. Write the problem statement for a vague ticket.** The ticket says: "Add caching to the product page." Write what you would send back before writing any code: at most a page, no solution in it, your questions at the end.

<details>
<summary>A model answer</summary>

```text
Problem statement: product page performance (ticket [ID])

Problem       The product page is reported slow. Not yet known: which
              endpoint, for which users, and its p95 latency today.
Evidence      [trace or APM link]; baseline [N] ms p95 on [date]
Success       p95 under [N] ms at [peak load], read from [dashboard]
Constraints   How stale may price and stock be: seconds, minutes, never?
              Who must see a price change, and how fast?
Out of scope  Other pages; the search API.

Questions
1. What prompted the ticket: a complaint, an alert, a load test?
2. Is the goal user-facing latency or database load? They have different fixes.
3. Is there a date, and what drives it?
```

- **Why not open the Redis docs first.** The ticket is solution-shaped: someone's guess at a fix for a problem nobody wrote down. If the page is slow because of a missing index or an N+1, a cache hides the cause and adds a staleness rule nobody agreed to.
- **What it buys.** The cheapest moment to find out you read a ticket differently from its author is before the code exists, when it costs one reply. Each question can be answered in a line.
</details>

**4. Turn a point estimate into a range.** In the stand-up your lead asks: "The CSV export of orders: two days?" The happy path is a day of work. You don't know whether the export covers the current month or the whole order history, which is millions of rows.

<details>
<summary>A model answer</summary>

```text
Two days if it's the current month: one query, streamed to the response.
If it must cover the full history, it becomes a background job with a
download link: five to eight days. I'll confirm which with [product owner]
by tomorrow noon and update the ticket.
```

And the message on the day the assumption breaks:

```text
CSV export: the full history is needed after all (confirmed with [product
owner]). My two days assumed the current month, so that estimate no longer
holds. New range: five to eight days, most of it the background job. If the
date matters more than the scope, the current-month export can ship in two
days and the history follow.
```

- **A range shows what a point hides:** which assumption drives its top, so everyone knows what to watch.
- **Re-estimate the day the assumption breaks.** Your number is already in someone's plan; each day you absorb the slip silently, more work is scheduled against a date that is gone. Offering a smaller scope leaves the decision with whoever owns the date.
</details>

## Three questions

**1.** A PR wraps a Service Bus handler's body in `catch (Exception) { }` "so poison messages stop retrying". What do you comment, with which label, and why?

<details>
<summary>Answer</summary>

- **`blocking:`, because it turns every failure into a success.** With `AutoCompleteMessages` at its default, `true`, a handler that returns normally completes the message. A transient timeout becomes a lost payment: no retry, no dead-letter entry, no alert. It stops poison messages by deleting good ones too.
- **The fix to ask for.** Catch only the permanent failures (deserialisation, validation, "the order no longer exists") and dead-letter them with a reason a human can act on. Let transient exceptions propagate, so the message is retried up to `MaxDeliveryCount`. Alert on the dead-letter count.
- **The question that finds it:** what if it fails halfway? [Chapter 29: Service Bus](#service-bus) has the settings; [Chapter 30, Case 6](#case-6-40000-messages-in-the-dead-letter-queue-and-nobody-knew) is what this looks like in production.
</details>

**2.** Your estimate said "three to five days, assuming the vendor API supports bulk updates." On day two you learn it doesn't. Why is "I'll work late and still make it" the wrong answer, and what do you send?

<details>
<summary>Answer</summary>

- **The estimate is void, not tight.** It held only while the bulk API existed. Working late hides that until the date slips, by which time others have planned more on top of it.
- **Send it the same day:** what you found, with the link; that the estimate assumed the opposite; the new range and what drives it (one call per item, rate limits, retries); and an option that keeps the date, such as a smaller first scope.
- **Why the assumption was worth writing down on day zero:** it turns this message from a broken promise into an update everyone saw coming.
</details>

**3.** You've been stuck for 40 minutes on an integration test that passes locally and fails in CI. Why is "the test fails in CI, any idea?" a weak question, and what do you send instead?

<details>
<summary>Answer</summary>

- **It hands over the whole search.** Before the helper can think, they must ask what you already know: which test, which error, what you tried. Each round trip in chat costs both of you minutes and focus.
- **Send the state of your search:** the goal, the exact error (pasted), what you tried and what each attempt ruled out, your hypothesis, and one specific ask. For example: "`OrderExportTests` fails only in CI with `Connection refused` on the database port. Reruns fail every time; the connection string matches. I think the tests start before the database container is ready. Does our pipeline wait for its health check?"
- **Name the goal, not only your attempted fix.** "How do I add a sleep to the CI step?" gets you a sleep; "the tests need the database ready first" gets you a health check. Asking about your fix instead of your problem is the *XY problem*.
- **Why a timebox.** Shorter, and you skip the search that teaches you the system. Longer, and you spend an hour on what a colleague who knows the system unblocks in minutes.
</details>

## Check at work

**Inspect.** Open the last ten review comments you left (most PR tools filter comments by author). Count how many state a condition and a cost, how many carry a label, and how many `blocking:` comments were about style. Good: every `blocking:` names what goes wrong and when; nits are labelled. Bad: "this could be a problem", a bare "why?", unlabelled style notes.

**Do.** In the next two pull requests you review, ask the five questions of every changed line and leave at least one comment in the full form, with a label. Afterwards ask each author: "Was that comment clear enough to act on without asking me anything?"

**Measure.** Take your last five estimates and their actual durations from the tracker. If most ran long by a similar ratio, that ratio is your outside view: apply it before you say the next number, and name the assumption behind the top of the range.


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
