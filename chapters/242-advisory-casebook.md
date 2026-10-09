# Chapter 42: The Advisory Casebook

[Chapter 35](#chapter-35-production-incidents) and [Chapter 30](#chapter-30-the-azure-casebook-real-incidents-real-fixes) are casebooks about systems: a database that falls over, a lock that expires, a private endpoint without DNS. This chapter is a casebook about the people who pay for those systems. The incidents here don't show up in a dashboard. A client asks for something that won't solve their problem, a CTO contradicts you in front of the team, an account manager has sold a feature that can't exist, or you broke production yourself.

The technical answer is usually the easy part. You know that a big-bang rewrite is risky ([Chapter 24](#chapter-24-working-with-legacy-brownfield-code)) and that microservices have a price ([Chapter 21](#chapter-21-architecture)). What decides whether the client *acts* on that knowledge is how you say it, when, to whom, and whether they trust you enough to hear it. That's the gap between a mid-level engineer, who is right, and an advisor, who is right *and listened to*.

The reader this chapter has in mind is a middle-to-senior .NET engineer who often works through an outsourcing or outstaffing company. That adds a third party to every conversation: the vendor, with its account manager, its contract and its commercial interests. Several cases are about that triangle, because it's where most contractors get stuck.

Every case is a **composite**. Each one blends patterns that recur across many engagements, and none describes a real client, vendor or person. Where a number would matter, it appears as a `[placeholder]` or is labelled illustrative. Each case has the same seven parts:

- **Situation**: the engagement and what just happened.
- **What the client says**, and what's really going on underneath it.
- **What a mid-level engineer does**: the tempting move, and why it backfires.
- **What an advisor does**: the reasoning, step by step.
- **Words you could use**: a short script in plain English. Adapt it, don't recite it.
- **Technical backing**: the chapter that holds the mechanism, so your advice rests on something.
- **Story angle**: how the case becomes a portfolio piece or an interview answer.

Read a case's *Situation* and *What the client says*, then stop and write down your own first move before reading on. Your first instinct is what you're training. The scripts only help if that instinct is already pointing in the right direction.

> **Pitfall.** The scripts are not magic words. A client can tell when someone is reading from a playbook, and it raises exactly the thing the trust equation below says to minimise: the sense that you're managing them. Take the *reasoning* from each case and use your own words.

## The Trust Equation as a Mechanism

David Maister, Charles Green and Robert Galford's *The Trusted Advisor* offers a model that works well as a debugging tool for client relationships:

```
                credibility + reliability + intimacy
   trust  =  ─────────────────────────────────────────
                        self-orientation
```

The authors are clear that this isn't arithmetic you compute. It's a model of which levers move trust and how they interact. Each term has a mechanism behind it:

| Term | What the client is really asking | What builds it | What destroys it |
|---|---|---|---|
| **Credibility** | "Do they know what they're talking about?" | Accurate, specific statements; saying "I don't know" when you don't; showing your evidence | One exposed bluff, which discounts everything else you said |
| **Reliability** | "Do they do what they say?" | Many small promises kept: the note sent when you said, the estimate that held | A missed commitment with no warning; silence |
| **Intimacy** | "Is it safe to tell them the real problem?" | Discretion; not punishing bad news; remembering what matters to *them* | Repeating a confidence; judging the client's past decisions |
| **Self-orientation** (the divisor) | "Whose interests are they serving right now?" | Focusing on the client's problem, even when the answer is less work for you | Selling, defending yourself, showing off, steering toward your favourite technology |

Two properties of the model explain most of the cases below.

**The divisor is multiplicative.** You can be brilliant, punctual and discreet, and one visible moment of self-interest shrinks all three at once. For a contractor this is structural: you bill by the hour or by the head, so the client *expects* your self-orientation to be high. A recommendation that means more work for your vendor (a rewrite, a migration, microservices) gets discounted before you finish the sentence. A recommendation that means *less* work for you ("you don't need this; here is the two-day version") is the most credible thing you can say, because it's expensive to fake.

**The numerator terms build at different speeds.** Credibility can jump in one meeting when you diagnose something nobody else could. Reliability builds only over time, from a history of kept promises, so it can't be rushed. Intimacy builds slowly and collapses fast. This is why a new engineer on an account should chase reliability first: small commitments, kept visibly. It's the one term that nothing else can substitute for.

Two other books give you vocabulary the cases rely on. Peter Block's *Flawless Consulting* separates the **presenting problem** (what the client asks you to fix) from the **underlying problem** (what's actually producing the pain), and treats the client's **resistance** as information about their concerns rather than an obstacle. Gerald Weinberg's *The Secrets of Consulting* states it more bluntly in his second law of consulting: no matter how it looks at first, it's always a people problem. Most of the cases below arrive looking like a technology decision and turn out to be about fear, status, budget or blame.

> **Best practice.** When a client conversation goes badly, debug it with the equation. Did you lose credibility (you were wrong or vague), reliability (you missed something), intimacy (they didn't feel safe) or raise self-orientation (you looked like you were protecting yourself or selling)? The fix differs for each, so name the term before you try to repair it.

## Outsourcing, Outstaffing and Who You Are Speaking For

Before the cases, get one structural fact right, because it changes the correct move in several of them.

```
   OUTSTAFFING                               OUTSOURCING
   ───────────                               ───────────
   Client ──directs──► You                   Client ──contract──► Vendor
     ▲                  │                      ▲                    │
     │   payroll,       │                      │  account/delivery  │ directs
     └── admin only ── Vendor                  └──── manager ◄───── You
                                              (you rarely talk commercials)
   You sit in the client's team.             The vendor owns delivery and scope.
   The client sets priorities.               The vendor sets priorities with the client.
```

In **outstaffing** you're effectively a member of the client's team. Advice goes straight to the client's leads, and the vendor mostly handles payroll. In **outsourcing** the vendor owns delivery, scope and the commercial relationship. An engineer who negotiates scope directly with the client can undercut a contract they have never read.

Most real engagements are a blend of the two, so find out early: **who may commit to scope, dates and money on this engagement, and who must hear bad news first?** Ask your delivery manager in week one. It's also a good reliability move, because it shows you intend to keep promises you're allowed to make.

> **Pitfall.** "The truth belongs to the client" and "the relationship belongs to the vendor" are both true, and several cases below turn on that tension. The resolution is almost never to hide the truth. It's about *sequence*: align privately with your own side first and quickly, then tell the client together. Hiding a material technical risk from the client to protect a sale is a line you don't cross. If your vendor asks you to, that's a conversation for your own manager, and possibly for your career.

## The Advisory Triage Card

When a client situation is getting hot, find the row and make its first move. Everything else can wait an hour.

| Situation | First move | Avoid |
|---|---|---|
| Client wants a full rewrite (A1) | Ask what the rewrite would *fix*; list the pains with their cost | Agreeing because it's interesting work; arguing that rewrites always fail |
| "We need microservices" (A2) | Find the actual constraint: deploy coupling, team scaling, scaling a hot path | A lecture on distributed systems; mocking the trend |
| Cloud bill doubled (A3) | Break the bill down by service and tag before touching anything | Cutting resources blind; blaming the platform |
| Previous vendor left a mess, "confirm it's their fault" (A4) | Describe the *state* and its *risk*, not the author | Blame, which the client will later hear as blame of *them* |
| Fixed deadline, scope doesn't fit (A5) | Make scope the variable, in writing, with a ranked list | Silently cutting quality to hit the date |
| CTO disagrees with you in front of the team (A6) | Acknowledge, ask one question, move the debate to a private follow-up | Winning the argument in public |
| "Skip testing / security to go faster" (A7) | Split what is negotiable from what isn't; price the risk | Yes to all; no to all |
| You caused an outage (A8) | Tell your lead and the client early, in facts; fix first | Waiting until you have the full story; hedging |
| "Competitor has AI, we need it" (A9) | Find the user job; propose a small evaluated spike | Building a chatbot to tick the box; a flat no |
| Account manager sold something impossible (A10) | Align privately with the AM before the client hears it | Contradicting the AM in the client meeting |
| Incident on a system you inherited (A11) | Stabilise; set a comms rhythm; report what you know and don't | Speculating about root cause; blaming the last owner |
| Asked about a technology you don't know (A12) | "I don't know yet; I'll come back by [day] with [what]" | Bluffing; a flat "no idea" with no follow-up |

## Case A1: "Let's just rewrite it" — the client wants a full rewrite

**Situation.** A logistics client runs a ten-year-old ASP.NET MVC 5 application on .NET Framework 4.x. Feature work is slow, the last three releases each broke something, and the one engineer who understood the pricing module left. The client's head of product has read about modern .NET and asks your vendor for a proposal: "a clean rewrite, in .NET 10, microservices, the works". Your vendor's sales team is enthusiastic: it's a large, long engagement.

**What the client says.** "The code is a mess. Nobody wants to touch it. Let's start again and do it properly this time."

**What's really going on.** The presenting problem is "old code". The underlying problems are usually narrower and measurable: slow lead time, fear of change because there are no tests, one or two modules that nobody understands, and a platform approaching end of support. A rewrite is a proposed *solution* dressed up as the problem. There's also an emotional component: the client is tired of being embarrassed by the system and wants a fresh start.

**What a mid-level engineer does.** One of two things. Either they say yes, because greenfield work is fun and the vendor wants the deal, or they argue on principle ("rewrites always fail, read Spolsky") and come across as someone defending the status quo. The first ignores the mechanism of why rewrites fail. The second is right but unpersuasive, because it dismisses the pain the client actually feels.

**What an advisor does.**

1. **Separates the goal from the method.** Ask what would be true in a year if the rewrite succeeded. The answers ("we ship weekly without breaking things", "we can change pricing without fear", "we're on a supported runtime") are the real requirements, and each can be met in more than one way.
2. **Explains the mechanism of rewrite risk, not the slogan.** The old system encodes years of requirements that exist nowhere else: edge cases, customer-specific rules, fixes for incidents nobody remembers. A rewrite has to rediscover all of them, usually in production. Meanwhile the old system still needs maintenance, so the team runs two systems, and the new one has to hit a moving target. Joel Spolsky's "Things You Should Never Do, Part I" tells the Netscape version of this story. Fred Brooks's *second-system effect* explains why the replacement tends to be over-designed.
3. **Makes the alternative concrete.** An incremental path: characterisation tests around the scary modules, a strangler-fig facade in front of the old app, and new or rewritten slices that move to modern .NET one route at a time ([Chapter 24](#chapter-24-working-with-legacy-brownfield-code)). Each slice delivers value and can be stopped without waste.
4. **Doesn't rule out a rewrite of a *part*.** Sometimes a module really should be rebuilt: the pricing engine, say, behind a well-tested contract. Advising against a big-bang rewrite isn't the same as advising against change.
5. **Handles the self-orientation problem openly.** The incremental plan may be a smaller engagement for your vendor. Say so to your delivery manager before you say anything to the client, and frame it commercially as well: a client who gets value every month renews, while a client stuck two years into a stalled rewrite leaves, and tells people why.

**Words you could use.**

> "I understand why a clean start is attractive. This codebase has been painful for a long time. Before we pick *how*, can we agree on *what* has to be true in a year? I've heard three things: releases that don't break, confidence to change pricing, and a supported runtime. I think we can get all three without stopping feature work, by replacing the system piece by piece, starting with the part that hurts most. If after two slices that isn't working, we'll have learned it cheaply, and a bigger rebuild is still an option."

**Technical backing.** [Chapter 24](#chapter-24-working-with-legacy-brownfield-code) (strangler fig versus the big rewrite, characterisation tests, .NET Framework to modern .NET), [Chapter 8](#chapter-8-testing) (the safety net), and [Chapter 39](#chapter-39-discovery-and-diagnosis) for turning "the code is a mess" into a list of measured pains.

**Story angle.** "I talked a client out of a [N]-month rewrite and into an incremental migration that shipped the first modernised slice in [N] weeks." The senior signal isn't that you said no. It's that you *reframed the goal* and gave the client a reversible first step. Keep the client unnamed. In a portfolio, write the anonymised decision record, not the client's code.

## Case A2: "We need microservices" — the architecture as a status symbol

**Situation.** A mid-size SaaS client runs a single ASP.NET Core application with one SQL Server database and one team of six developers. A new CTO arrives from a large tech company and announces that the platform will move to microservices on Kubernetes. You're asked to "draw the target architecture".

**What the client says.** "We need to be able to scale. Microservices are the industry standard. Netflix does it."

**What's really going on.** "Scale" can mean at least three different things, and microservices solve only some of them:

- **Load.** One endpoint is hot. That's usually solved by scaling out a stateless monolith, caching, or tuning a query, well before anything needs splitting.
- **Teams.** Many teams step on each other in one codebase and one release train. This is the case microservices actually address: they let teams deploy independently (Conway's law, in reverse).
- **Change isolation.** One area changes far more often than the rest, and every change needs a full regression pass.

With one team of six, the organisational problem microservices solve doesn't exist yet. Martin Fowler calls this the *microservice premium*: the cost in operations, distributed data, network failure and debugging that you pay before you get the benefit. There may also be a status element. The new CTO wants to make a mark, and "we modernised the architecture" is a visible one.

**What a mid-level engineer does.** Either draws the boxes as asked, splitting by entity (an Orders service, a Customers service) with a shared database, which gives the client the costs of distribution with none of the independence, or lectures the CTO on the fallacies of distributed computing in the first meeting and gets labelled "resistant to change".

**What an advisor does.**

1. **Takes the goal seriously and asks what problem it solves.** "Which pain would microservices remove first?" If the answer is "deploys are scary", that's a CI/CD and testing problem. If it's "the reporting queries slow down checkout", that's a data-access problem.
2. **Proposes a modular monolith as the first step, and frames it as the path toward microservices, not away from them.** Enforce module boundaries inside the one deployable: separate schemas per module, no cross-module table access, communication through defined interfaces or in-process events. If a module later needs to deploy or scale on its own, it already has a clean seam. This is the "monolith first" argument (Fowler), and it keeps both doors open.
3. **Names the one or two places where extraction pays now.** Often there's a real candidate: a CPU-heavy document renderer, or a webhook ingester with a different scaling profile. Extracting that one service gives the CTO a visible win and teaches the team the operational cost on a small surface.
4. **Puts the operational bill on the table.** Distributed tracing, per-service pipelines, contract versioning, eventual consistency, and on-call for more moving parts. Ask who in the six-person team will own each.

**Words you could use.**

> "I'm not against splitting the system. I want the split to buy us something. The benefit of microservices is independent deployment for independent teams, and right now there's one team. What I'd suggest is carving the monolith into strict modules this quarter, so each one *could* become a service, and extracting the document renderer now, because it has a genuinely different load. That gives us a first service in production and real data on what running services costs us, before we commit the whole platform."

**Technical backing.** [Chapter 21](#chapter-21-architecture) (monolith versus microservices versus modular monolith), [Chapter 20](#chapter-20-distributed-systems) (why the network changes everything), [Chapter 11](#chapter-11-messaging-and-background-work) (the messaging you'll need), and [Chapter 38](#chapter-38-having-a-point-of-view) on holding a position without being dogmatic.

**Story angle.** Interviewers ask "tell me about a time you pushed back on an architecture decision" constantly. The strong version shows you *found the constraint* (one team, one hot path) and gave the decision-maker a win on their terms. The weak version is "I told them microservices were wrong". Keep the story about the reasoning, not about the CTO.

## Case A3: The cloud bill doubled

**Situation.** An e-commerce client on Azure gets a monthly invoice of roughly twice the usual amount: [€X] became [€2X]. The finance director emails the CEO, the CEO forwards it to the vendor, and your delivery manager forwards it to you with "can you look at this today?"

**What the client says.** "Cloud is supposed to be cheaper. Who approved this? Can we just turn things down?"

**What's really going on.** A cloud bill is a sum of *rate × quantity* over hundreds of meters. It doubles for a small number of reasons: something scaled out and never scaled back, a new feature multiplied a per-request cost (egress, RU/s, log ingestion), a test environment was left running, a reservation expired, or traffic genuinely grew. The emotional reality matters as much: finance feels blindsided, and someone is looking for a person to blame. Until there's a cause, the vacuum fills with guesses.

**What a mid-level engineer does.** Starts cutting: scales down App Service plans, lowers Cosmos DB throughput, deletes "unused" resources. Some of those were the reason the Black Friday sale worked, and one was a disaster-recovery replica. Or they reply defensively ("the architecture was approved by your team") before anyone knows the cause.

**What an advisor does.**

1. **Acknowledges fast, and gives a time for the answer.** Within the hour: "We're on it. You'll have the cause by [time] tomorrow, and nothing will be turned off without your approval." That buys reliability while you work.
2. **Decomposes before changing anything.** Cost Management grouped by service, then by resource, then by meter, over daily granularity for the last two months. A doubling almost always shows up as one or two lines with a visible step on a specific day. Line that day up with the deployment history and the change log.
3. **Finds the driver, not just the resource.** "Log Analytics went from [X] to [Y]" isn't a cause. "The [date] release set the log level to Debug in production, and ingestion is billed per GB" is. The mechanism tells you both the fix and the guardrail.
4. **Separates waste from growth.** If orders grew too, some of the increase is the business working. Express cost per unit of business value (per order, per active tenant). That's the number finance can reason about.
5. **Proposes fixes with their risk,** and lets the client decide: "revert log level: saves [X]/month, no risk", "cap Functions scale-out: saves [Y], risk of slower peaks", "delete the DR replica: saves [Z], and here is what we lose".
6. **Adds the guardrail.** Budgets with alerts at [50/80/100]% of forecast, anomaly alerts, tags that map resources to owners, and a monthly cost review. This is what stops the next surprise, and it's usually the most valuable deliverable.

**Words you could use.**

> "Here's what happened. On [date] we released a change that increased [driver]. That accounts for about [share] of the increase. The rest is [growth / the test environment left running]. We've already reverted the first part, which brings next month back to roughly [amount]. To make sure you hear about this before the invoice next time, I'd like to set up budget alerts and a short monthly review. It takes about an hour a month."

Note what the script doesn't do: it doesn't blame the platform, the finance team, or a named colleague. The mistake is "we released a change", in the first person plural.

**Technical backing.** [Chapter 31](#chapter-31-compliance-data-privacy-cloud-cost-finops) (FinOps: rate versus usage, tagging, unit economics), [Chapter 30](#chapter-30-the-azure-casebook-real-incidents-real-fixes) (Case 9, a Cosmos DB bill that doubled after a small feature), and [Chapter 9](#chapter-9-exceptions-logging-and-first-diagnosis) (the cost of telemetry itself).

**Story angle.** A cost investigation is one of the best portfolio stories available to a mid-level engineer, because it has a clear before and after and it crosses into business language. In interviews, lead with the diagnosis method and the guardrail. Publish the method as a write-up. The client's figures stay private; relative changes ("roughly halved") are enough.

## Case A4: The previous vendor left a mess, and the client wants you to say so

**Situation.** Your vendor takes over a .NET platform from another outsourcing company that the client fired. The code has no tests, secrets in `appsettings.json` committed to Git, a hand-rolled authentication scheme, and copy-pasted data access. In the first steering meeting the client's COO asks you directly: "Be honest: how bad is it? Did they do a terrible job?"

**What the client says.** "We paid them a lot of money. I need to know whether we were cheated."

**What's really going on.** The client may be building a case for a dispute, or trying to justify the switch to their board, or simply looking for reassurance that they made the right call. Underneath all of these is something awkward: *the client chose that vendor, approved its work and paid its invoices for years.* Criticism of the previous vendor is also, indirectly, criticism of the client's own judgement and oversight. You also lack context. The shortcuts may have been forced by budget cuts, requirement churn or deadlines the client set.

**What a mid-level engineer does.** Enjoys the invitation. "Honestly, this is some of the worst code I've seen. No tests, secrets in Git, they rolled their own auth..." It feels like honesty and it builds instant rapport. Three months later, when your own team ships a bug, the client remembers that your standard for other people's work was contempt, and they apply it to you. Worse, if the dispute goes legal, your offhand words may end up quoted.

**What an advisor does.**

1. **Describes the state, not the author.** Findings, evidence and risk, in neutral language: "authentication is custom and doesn't validate token expiry, so a stolen token stays valid indefinitely" rather than "they didn't know what they were doing".
2. **Separates the urgent from the ugly.** Committed secrets and broken authentication are *risks* and get fixed now (rotate the secrets, purge them from history, replace the auth with the platform's). Copy-pasted data access is *cost*: it slows change, and it goes on the roadmap.
3. **Declines to judge intent, explicitly and kindly.** "I can tell you what the code does. I can't tell you why. I don't know what constraints they were working under."
4. **Offers the client what they actually need.** If they need a factual assessment for a dispute, offer to write one: findings, evidence, severity, and remediation effort, reviewed by your vendor. That's a professional document. If they need reassurance, give it through the plan: "here's how we get this to a safe state in [N] weeks."

**Words you could use.**

> "I'd rather not grade the previous team, because I don't know what they were asked to do or under what constraints. What I can tell you is where the platform stands. There are two things we need to fix this week, because they're security risks: the committed credentials and the login token handling. Then there are several things that make changes slow and risky, and I'd like to put those into a plan. If you need a written technical assessment for your own purposes, we can prepare one that sticks to evidence."

**Technical backing.** [Chapter 12](#chapter-12-security-essentials) (secrets, token validation), [Chapter 27](#chapter-27-security-in-depth-and-the-supply-chain) (removing secrets from history is not enough: rotate them), [Chapter 24](#chapter-24-working-with-legacy-brownfield-code) (living with a big ball of mud), and the lab in [Chapter 40](#chapter-40-lab-the-net-health-check) for running the assessment itself.

**Story angle.** "I inherited a codebase with [issues] and produced a risk-ranked assessment within [N] days" is a strong story. Tell it with no contempt for the previous team: interviewers are listening for how you'd talk about *their* code. The assessment template is portfolio material. The client's findings aren't.

> **Pitfall.** "Blameless" doesn't mean "findings-free". You still write down, precisely, that the token isn't validated. What you leave out is the adjective and the guess about motive.

## Case A5: A fixed deadline and scope that won't fit

**Situation.** A fintech client has a regulatory go-live date fixed by an external body. The feature list agreed at kickoff is, by your team's honest estimate, [1.5–2]× what fits before that date. The client's product owner treats every item as mandatory. Your delivery manager's instinct is to "find a way".

**What the client says.** "The date can't move and we need all of it. Can you add more people?"

**What's really going on.** Time, scope, cost and quality are coupled, the familiar iron triangle with quality as the hidden fourth side. When date and scope are both fixed, and cost can't buy time quickly, the only remaining variable is quality. Nobody chooses to cut it, so it gets cut *silently*: tests skipped, reviews rushed, error handling left for "later". Adding people late usually makes it worse, because onboarding and coordination eat the capacity they add. That's Brooks's law, from *The Mythical Man-Month*: adding people to a late software project makes it later. It's a heuristic, not a law of physics, but its mechanism (ramp-up plus communication overhead) is real.

**What a mid-level engineer does.** Says "we'll try", works evenings, and quietly drops tests to go faster. The date is hit with a system that fails in its first month. Or they give a flat "impossible", which is an opinion, not an option, and it hands the problem back without help.

**What an advisor does.**

1. **Confirms which constraint is truly fixed.** A regulatory date usually is. "All of it" usually isn't: the regulator requires a specific *capability*, not the product owner's full backlog. Ask for the rule text, and find the minimum that satisfies it.
2. **Makes scope the variable, visibly.** A ranked list, with a line drawn at your capacity: must-have for compliance, should-have for launch, and after launch. The client does the ranking. You supply the costs and the dependencies.
3. **Estimates in ranges and says what drives the range.** "Between [N] and [M] weeks; the width comes from the unknown partner API." Then work to shrink the biggest unknown first ([Chapter 41](#chapter-41-recommendations-proposals-and-estimates)).
4. **Treats adding people as a specific proposal, not a hope.** Say who, when, onboarding to which self-contained piece, and what it costs the existing team this week.
5. **Writes it down.** A one-page note: date, scope above the line, scope below it, assumptions, and what happens if an assumption breaks. When the partner API arrives late, you point at the assumption instead of re-arguing the whole plan.

**Words you could use.**

> "The date is fixed, so let's treat it as fixed. With the team we have, we can deliver everything above this line with the quality a regulated launch needs. The items below it are real, and we'll do them next. If something below the line is actually required by the regulation, let's swap it in, and tell me what comes out. What I'm not willing to do is promise all of it and then cut testing quietly to make it fit, because that's the version that fails in front of the regulator."

**Technical backing.** [Chapter 16](#chapter-16-working-like-a-middle-developer) (estimation and planning), [Chapter 41](#chapter-41-recommendations-proposals-and-estimates) (ranges, assumptions, the written proposal), and [Chapter 8](#chapter-8-testing) (what you give up when tests go).

**Story angle.** "We hit a fixed regulatory date by renegotiating scope, not quality" is an excellent STAR story, because the *Action* is visibly senior: you made a trade-off explicit and got a decision from the right person. Keep the ranked list format (with placeholders) as a portfolio template.

## Case A6: The CTO disagrees with you in front of the team

**Situation.** In a design review with the client's engineers, you recommend moving a slow synchronous integration behind a queue. The client's CTO interrupts: "No. Queues add complexity we don't need. We tried that at my last company and it was a nightmare." Eight people are watching to see what you do.

**What the client says.** "I've seen this fail. We're not doing it."

**What's really going on.** Two conversations are happening at once. The technical one is legitimate: queues *do* add complexity (poison messages, idempotency, observability). The social one is about status. A public contradiction puts the CTO in a position where backing down costs face in front of their own team. The more convincingly you argue now, the harder it becomes for them to agree. Their past experience is also real data, not just stubbornness. Something went wrong last time, and you don't yet know what.

**What a mid-level engineer does.** Wins the argument. They cite the numbers, the diagrams and the failure modes of the synchronous design, and may well be right on every point. The CTO loses in front of the team, and from then on every recommendation you make is quietly resisted. Or the engineer folds completely and never raises it again, and the timeout incidents continue.

**What an advisor does.**

1. **Acknowledges the concern as valid, because it is.** Queues do add failure modes. Saying so costs you nothing and lowers the temperature.
2. **Asks one genuine question, not a trap.** "What went wrong last time?" The answer often reveals a specific failure (no dead-letter handling, no idempotency) that your design already covers, or one it doesn't.
3. **Moves the decision out of the room.** "Could we take this offline? I'll write up both options, with the failure modes of each, and we can decide on Thursday." That gives the CTO a way to change their mind in private.
4. **Writes a fair options note.** Both options, honestly, including the costs of your own. Make the decision criteria explicit: timeout rate, recovery from partner outages, operational load.
5. **If they still decide against you, commits.** Record the decision and its rationale in an ADR, implement it well, and add the monitoring that would show whether the risk you raised materialises. "Disagree and commit" only works if the commit is real. Your job is to make the decision informed, not to make it yours.

**Words you could use.**

> In the room: "That's fair. A queue brings its own failure modes, and I'd like to hear what went wrong last time, because I want to make sure we're not repeating it. Rather than decide it here, can I write up both options side by side and bring them to Thursday's review?"
>
> In private, afterwards: "I didn't want to argue it out in front of the team. I've put both options in the note, including the risks of mine. It's your call, and I'll implement whichever you choose properly."

**Technical backing.** [Chapter 11](#chapter-11-messaging-and-background-work) (what a queue actually costs and buys), [Chapter 16](#chapter-16-working-like-a-middle-developer) (ADRs), [Chapter 36](#chapter-36-senior-behaviours-career-and-interviews) (judgement and influence), and [Chapter 38](#chapter-38-having-a-point-of-view) (strong opinions, and the evidence that would change them).

**Story angle.** "Tell me about a time you disagreed with a senior stakeholder" is among the most common behavioural questions. The strongest answers show you protected the other person's standing *and* the quality of the decision, and that you committed properly when overruled. An answer that ends "and in the end they admitted I was right" is weaker than it sounds.

> **Best practice.** Praise in public, disagree in private. When you must disagree in public, disagree with the *option*, never with the *person*, and give them a way to change their mind without an audience.

## Case A7: "Can we skip the tests and the security stuff for now?"

**Situation.** A startup client is two weeks from a demo to investors. The founder asks your team to "skip the tests and the security review this sprint, we'll come back to it". The product stores customer names, emails and payment references.

**What the client says.** "Speed is everything right now. We'll fix it after the round closes."

**What's really going on.** The founder is making a rational trade: certain cost now against uncertain cost later. The problem is that the two halves of the request carry very different risks, and they're bundled together. Deferring some test coverage is *technical debt*: it costs you later, internally, and you can repay it. Shipping personal data without basic security is *risk to other people*: customers whose data leaks, and possibly legal obligations under GDPR or similar laws. That isn't the founder's alone to waive. And "we'll come back to it" loses to the next deadline almost every time, because after the round there's a bigger one.

**What a mid-level engineer does.** Either agrees to everything, so the demo ships with an unauthenticated admin endpoint, or refuses everything on principle, which makes them look like they don't understand startups. Both treat the request as one decision when it's several.

**What an advisor does.**

1. **Unbundles the request.** Some tests can be deferred: broad unit coverage of UI glue, edge-case tests for features that may not survive the demo. Other things can't: authentication and authorisation on every endpoint that touches customer data, secrets out of the code, HTTPS, parameterised queries. Those are cheap when done now and very expensive after a breach.
2. **Shows the cost of the non-negotiables.** Often it's much smaller than the founder imagines: "[N] hours, not the sprint." Much of the pushback comes from picturing "security" as a months-long audit.
3. **Keeps the tests that buy speed.** A few integration tests around the demo path make the next two weeks *faster*, because they catch regressions before the demo rather than during it.
4. **Makes the deferral real.** Deferred items go on the backlog with an owner and a date. The client signs off on the risk in writing, in plain words, one line per item.
5. **Holds the line on the non-negotiables.** If the client insists on shipping personal data without authentication, escalate it to your delivery manager. That's a professional-liability question for your vendor, not a sprint-planning detail.

**Words you could use.**

> "I agree that speed matters most right now, so let's cut everything we can cut. Most of the test suite can wait until after the demo, and I'll list what we're deferring so it doesn't get lost. There are a few things I won't skip, because they protect your customers' data rather than our code: login on every endpoint, no secrets in the code, and safe database queries. Together that's about [N] hours. If an investor's technical advisor looks at the product, those are the first things they'll check."

**Technical backing.** [Chapter 12](#chapter-12-security-essentials) (the OWASP Top 10 and what's cheap to get right), [Chapter 31](#chapter-31-compliance-data-privacy-cloud-cost-finops) (personal data obligations), [Chapter 8](#chapter-8-testing) (which tests pay back fastest), and [Chapter 35](#chapter-35-production-incidents) (Scenario 8, the security measures that actually matter before a ship date).

**Story angle.** "I negotiated a faster path that kept the security floor" shows judgement under commercial pressure, which is exactly what senior interviews probe. Say what you gave up as well as what you kept. The deferral list format is a good portfolio template.

## Case A8: You caused the outage

**Situation.** You deployed a migration that added an index on a large table during business hours. It locked the table, checkout failed for [N] minutes, and the client's support line lit up. You worked out what happened within ten minutes, and the rollback is done. Now you have to tell the client.

**What the client says.** Nothing yet. In an hour they'll ask "what happened?", and the true answer is "I did".

**What's really going on.** This is the situation that tests the trust equation hardest, because your reliability has just taken a hit and you can't undo it. What you control is the other three terms. Concealment destroys intimacy the moment it's discovered, and incidents are almost always discovered. Hedging ("there was an issue with the database") raises self-orientation, because the client can tell you're protecting yourself. Fast, factual disclosure plus a real prevention plan is the only path that *adds* credibility at the moment you lost reliability. Clients often come out of a well-handled incident trusting the team more than before, because they've seen how it behaves under pressure.

**What a mid-level engineer does.** Waits until they have the full picture before saying anything, which from the client's side looks like silence during an outage. Or they write a vague note that blames "the database", which the client's own DBA will quietly contradict.

**What an advisor does.**

1. **Stops the bleeding first,** and tells their own lead and delivery manager immediately. Your vendor must not hear about this from the client.
2. **Sends a short holding note fast.** Impact, current status and when the next update comes. It doesn't need the cause yet.
3. **Owns it in the first person, once, without theatre.** "A migration I deployed locked the orders table." Don't repeat the apology five times: over-apologising makes the note about your feelings rather than the client's system, which is self-orientation again.
4. **Explains the mechanism plainly.** Creating an index takes locks on the table, and on a large, busy table that blocks writes for the duration. Then say why the process let it through: the migration ran in business hours, it wasn't reviewed for lock impact, and there was no staging table of production size.
5. **Proposes prevention in the system, not a promise to "be more careful".** Online index creation where the edition supports it, a migration review checklist, a deployment window for schema changes, and a staging dataset of realistic size. Then run a blameless post-incident review. "Blameless" applies to you too: the point is the process gap, not self-punishment.

The written follow-up can use a fixed template:

```text
**Incident summary** — [date, time window, timezone]
**Impact** — [who was affected, what they could not do, for how long]
**Status** — resolved at [time]; monitoring since
**What happened** — [one paragraph, mechanism in plain language; "I/we" not "an issue"]
**Why it got through** — [the process gap, not the person]
**What we changed already** — [done items]
**What we will change** — [item — owner — date]
**Next update / review meeting** — [date]
```

**Words you could use.**

> "Between [time] and [time], checkout failed for your customers. The cause was a database change I deployed: it locked the orders table while it ran. It's been rolled back, and checkout has been working normally since [time]. I'll send a full write-up by [time] tomorrow, covering why our process let this through and what we're changing so a schema change can't do this again. I'm sorry for the disruption to your customers."

**Technical backing.** [Chapter 7](#chapter-7-data-access) (migrations, indexes and locking), [Chapter 20](#chapter-20-distributed-systems) (blameless post-mortems and SRE practice), [Chapter 26](#chapter-26-delivery-and-platform) (deployment gates), and [Chapter 35](#chapter-35-production-incidents) for the incident-response pattern.

**Story angle.** "Tell me about a mistake you made" is guaranteed in senior interviews. This is the best possible material, *if* the story ends with a systemic fix rather than "I learned to be careful". Tell it in the first person, keep the client anonymous, and put the post-incident template (not the incident) in your portfolio.

> **Pitfall.** Don't invent a precise outage length or revenue figure to make the story vivid, in the client note or in an interview. Use what you measured, or say "about". A figure someone can check and contradict costs you more credibility than the incident did.

## Case A9: "Our competitor launched an AI feature. We need one."

**Situation.** A B2B client's main competitor has announced an "AI assistant". The client's CEO sends the vendor a link with one line: "We need this by next quarter. What will it cost?"

**What the client says.** "We can't be the only ones without AI."

**What's really going on.** The presenting problem is competitive anxiety. The underlying question, which nobody has asked yet, is what job a user would hand to an AI feature in *this* product, and whether an LLM does that job better than a simpler solution. The competitor's feature may be a demo, a press release or genuinely useful. Nobody in the room knows yet. Behind that are the real engineering questions: cost per call at your volume, latency, data privacy (customer data sent to a model provider), prompt injection, and how you'll tell whether the feature works at all.

**What a mid-level engineer does.** Wires a chat window to a model API in a week, because it's exciting and quick. It demos well and fails on real data: it hallucinates figures, costs more per user than expected, and nobody can say whether an answer is right. Or the engineer dismisses the whole thing as hype, and the client finds a vendor who says yes.

**What an advisor does.**

1. **Turns "AI" into a user job.** Interview two or three real users, or read the support tickets. Candidates in B2B .NET products are often concrete: summarising a long case history, drafting a reply from a knowledge base, pulling fields out of uploaded documents, or searching in natural language.
2. **Picks one job with a measurable outcome.** "Support agents draft replies [N]% faster" or "field extraction accuracy is at least [X]% on our documents", measured against a baseline.
3. **Proposes a time-boxed, evaluated spike.** A small evaluation set of real (anonymised) examples, the simplest workable design (often retrieval plus one model call, not an agent), the cost per call measured, and a go or no-go at the end. The evaluation set is the most valuable output. It turns "is it good?" from an opinion into a measurement.
4. **Surfaces the non-functional questions early.** Where the data goes and under which terms, what happens when the model is wrong (a human in the loop for anything consequential), and the injection risks of feeding user content to a model that can call tools.
5. **Is honest about the competitor.** "We don't know yet whether their feature works. We'll know whether ours does."

**Words you could use.**

> "I think there's a real opportunity here, and I'd rather find the right one than copy theirs. Let's pick one task your users spend real time on, maybe drafting support replies, and run a [N]-week experiment on real examples. At the end you'll know three things: whether it's good enough, what it costs per user, and what we'd need to do about your customers' data. If the answer is yes, we build it properly. If it's no, you've spent a few weeks rather than a quarter."

**Technical backing.** [Chapter 33](#chapter-33-building-ai-powered-systems) (RAG, evaluation, cost mechanics, securing AI features), [Chapter 32](#chapter-32-the-ai-native-developer-thriving-in-the-ai-era) (judgement in the AI era), and [Chapter 31](#chapter-31-compliance-data-privacy-cloud-cost-finops) (data leaving your boundary).

**Story angle.** "I turned a 'we need AI' request into an evaluated experiment with a go/no-go decision" is exactly the kind of story hiring managers are looking for right now, because it shows you can use the technology *and* resist it. A go/no-go based on an eval set is a better story than a feature that shipped because the CEO wanted one. Build a public version of the evaluation harness on open data for your portfolio.

## Case A10: The account manager promised something technically wrong

**Situation.** In a renewal call you weren't in, your vendor's account manager told the client that the platform would "support real-time sync with their ERP, with zero data loss and no changes to the ERP". The ERP exposes only a nightly batch export. The client now expects the feature in the next release, and the AM forwards you the email: "Can you confirm this is fine?"

**What the client says.** "Your team said this was straightforward."

**What's really going on.** The AM isn't lying on purpose. They translated "we've integrated with ERPs before" into a promise without knowing the constraint. There are now three parties with different interests: the client (who needs the truth), the AM (who needs to avoid looking incompetent at renewal) and you (who have to deliver whatever was promised). The technical facts are fixed: a nightly export can't give you real-time sync, and "zero data loss" needs defining before anyone can promise it. The social question is how the correction reaches the client.

**What a mid-level engineer does.** Replies to the client directly, possibly with the AM in copy: "That isn't possible, the ERP only exports nightly." It's accurate, and it's a disaster. The AM is humiliated in front of their customer, the vendor looks disorganised, and the client now wonders what *else* they were told. Or the engineer stays silent, tries to build the impossible, and the gap surfaces at the demo.

**What an advisor does.**

1. **Replies to the AM only, quickly, with facts and options.** Not "you were wrong" but "here's what the ERP allows, and here's what we can actually offer".
2. **Finds the options that meet the client's real need.** Why does the client want "real-time"? Usually so a salesperson sees current stock. Options might be: near-real-time via the ERP's change-notification API if it has one (which may need an ERP module the client must license), reading the ERP's database via change data capture (with the ERP vendor's support policy checked first), or nightly sync plus a clear "as of" timestamp in the UI. For each: latency, cost, and what it asks of the client.
3. **Agrees with the AM on who tells the client, and presents it together.** Ideally the AM re-opens it ("our technical lead has looked at your ERP in detail"), and you explain the options. The correction then reads as diligence, not as a contradiction.
4. **Sets a deadline for the alignment.** If the client expects the feature next release, the correction must reach them *this week*. "Let's align privately" can't turn into "let's hope it goes away". If the AM won't correct it, escalate to your delivery manager. Delivering on a promise that everyone knows is false isn't an option.
5. **Fixes the process afterwards.** Offer to join pre-sales calls, or to review technical claims before they go into proposals. Most AMs welcome it once they've been burned.

**Words you could use.**

> To the AM, privately: "I've looked at their ERP. It only exposes a nightly export, so real-time sync isn't possible without them licensing [module], and 'no changes to the ERP' rules that out. The good news is that we have options that meet what they probably need, which is current stock for sales. I've written three up with costs. Can we get on a call with them this week, and I'll walk through them? The sooner they hear it, the easier it is."
>
> To the client, together: "We've looked at your ERP's integration options in detail. There are three ways to get the data across, and the difference is how fresh it is and what it needs on your side. Here they are."

**Technical backing.** [Chapter 11](#chapter-11-messaging-and-background-work) and [Chapter 18](#chapter-18-data-in-depth) (sync, change data capture, what "zero data loss" means in practice), [Chapter 20](#chapter-20-distributed-systems) (delivery guarantees), and [Chapter 41](#chapter-41-recommendations-proposals-and-estimates) (writing options with costs).

**Story angle.** This is a strong "stakeholder management" story, but it involves a named colleague's mistake, so tell it with care. The AM is a well-meaning person without the technical context, and your role is the one who turned a problem into options and fixed the process. Never share the real email thread.

## Case A11: A production incident on a system you inherited last week

**Situation.** You joined an outstaffed engagement five days ago. On Friday evening the client's order-processing API starts returning 500s. The engineer who built it left the client months ago, the runbook is a two-line README, and the client's head of operations is on the call asking you what's going on.

**What the client says.** "You're the .NET expert now. What's wrong, and when will it be fixed?"

**What's really going on.** The client needs two things, and only one of them is a fix: they need to feel that someone competent is in control. In the first minutes, the second need is the more urgent, because it decides whether they panic, escalate, or start making changes in production themselves. You can't give them a root cause yet, but you can give them a process, a rhythm, and honesty about what's known.

**What a mid-level engineer does.** Starts reading code while the client waits in silence. Or they guess out loud ("it's probably the database") to seem in control, and the client repeats the guess to their CEO. When it turns out to be a certificate expiry, the guess is what everyone remembers. The other failure is to say "I've only been here five days, I didn't build this", which is true and completely unhelpful.

**What an advisor does.**

1. **Separates the roles.** Say it out loud: one person drives the fix, one person talks to stakeholders. If you're alone, say you'll update them every [30] minutes, and stop being interrupted in between.
2. **Stabilises before diagnosing.** What changed recently? Deployments, config changes, certificate dates, dependency status pages. A rollback of the most recent change is often the fastest mitigation even before you know why it helps. Check the obvious in order: health endpoints, logs for the first error, dependencies ([Chapter 35](#chapter-35-production-incidents) for the generic playbook).
3. **Reports in three buckets.** What we know, what we don't know yet, and what we're doing next. That structure keeps you honest and keeps the client calm, because it shows method.
4. **Doesn't speculate about cause, and doesn't blame the previous owner.** "The system has little documentation" is a finding for the post-incident review. It doesn't help during the incident.
5. **Turns the incident into the onboarding you didn't get.** Afterwards: a real runbook, dashboards and alerts for the failure you just found, and a short list of the next most likely failures. That's reliability made visible, and it's a gift to whoever inherits the system after you.

**Words you could use.**

> "Here's where we are. We know the order API is returning errors since about [time], and that it started [before/after] the [deployment/change]. We don't know the cause yet. What we're doing now is [rolling back / checking the payment dependency]. I'll update you at [time] even if nothing has changed. If you need something for your customers, the safest message right now is that orders are delayed and we're working on it, without an estimate."

**Technical backing.** [Chapter 9](#chapter-9-exceptions-logging-and-first-diagnosis) (logs, traces and metrics you'll wish you had), [Chapter 35](#chapter-35-production-incidents) (incident response), [Chapter 20](#chapter-20-distributed-systems) (SRE practice), and [Chapter 39](#chapter-39-discovery-and-diagnosis) (mapping an unfamiliar system quickly).

**Story angle.** "In my first week on an engagement I led the response to a production incident on a system I'd never seen" is a memorable opening. The substance is the method (roles, rhythm, the three buckets) and the runbook you left behind. Keep the client and system anonymous. The runbook template, with the specifics stripped out, is portfolio-ready.

## Case A12: "What do you think of [technology you don't know]?"

**Situation.** In a planning meeting, the client's architect asks you: "We're considering [a technology you've never used, say an event-streaming platform, a specific workflow engine or a niche database]. What's your view?" Everyone turns to you. You've read one blog post about it.

**What the client says.** "You're the expert. Should we use it?"

**What's really going on.** The client is asking for judgement, not for a product review. They may already have half-decided and want confirmation, or want a sanity check against a vendor pitch. The credibility term of the trust equation is the one at stake, and it's asymmetric: an honest "I don't know" costs a little credibility for a moment, while a bluff that's later exposed discounts *every* claim you've ever made to this client.

**What a mid-level engineer does.** Bluffs, repackaging the blog post as an opinion. Or they say "no idea", full stop, which is honest but leaves the client with nothing, and wastes the fact that you *do* know how to evaluate a technology even if you don't know this one.

**What an advisor does.**

1. **Says clearly what they know and don't.** "I haven't used it in production. I know it's in the [category] space."
2. **Offers what transfers: evaluation criteria.** Every technology in a category answers the same questions. What problem does it solve that you have today? What are its failure modes and operational burden? What does the .NET client library look like, and who maintains it? What's the licence and pricing model? Who else on the team will be able to run it at 3 a.m.? What does exit look like if it doesn't work out? Asking these in the meeting shows more expertise than a verdict would.
3. **Commits to a specific follow-up.** "By [day] I'll come back with a one-page view: how it compares with [what you already use], a small spike on the .NET client, and the questions to ask the vendor." Then deliver exactly that, on that day. The kept promise builds reliability, and it more than repays the credibility you "spent" by saying "I don't know".
4. **Brings in someone who knows, if that's faster.** A colleague at your vendor who has run it in production is often the best answer, and introducing them makes your vendor look good.

**Words you could use.**

> "I haven't used it in production, so I don't want to give you a verdict off the top of my head. What I'd want to know before recommending it is what problem we'd use it for that our current setup can't handle, what it takes to operate, and how mature the .NET client is. Give me until [Thursday] and I'll come back with a one-page comparison and a small working spike, so we're deciding on evidence."

**Technical backing.** [Chapter 13](#chapter-13-git-and-cicd) (evaluating tools), [Chapter 27](#chapter-27-security-in-depth-and-the-supply-chain) (assessing a dependency's maintenance and provenance), and [Chapter 38](#chapter-38-having-a-point-of-view) (forming a view quickly and stating its confidence).

**Story angle.** "How do you evaluate a technology you haven't used?" is a common senior interview question, and this is the answer: the criteria, the time-boxed spike, the written comparison. Publishing a few of these one-page evaluations, on technologies you've spiked on your own time, is some of the most transferable portfolio content you can create ([Chapter 43](#chapter-43-positioning-and-public-proof)).

## Quick Advisory Cases

Short situations, one move each. Use them as flashcards.

- **"Just give me a number."** The client wants a single estimate for a vague feature. Give a range, name the largest unknown, and offer to narrow the range after a [N]-day spike. A single number becomes a promise the moment it's said ([Chapter 41](#chapter-41-recommendations-proposals-and-estimates)).
- **The client's developer keeps pushing straight to main.** It isn't your team to manage. Raise it as a risk with evidence (the last two incidents traced to unreviewed pushes) and propose branch protection as a team rule, not a personal rebuke.
- **Your vendor wants you to recommend its partner product.** Recommend it only if you would anyway, and disclose the relationship if you do. Undisclosed incentives are the purest form of self-orientation, and clients find out.
- **The client asks you to stay late for the third week running.** Reliability includes being able to keep the pace you've set. Surface the workload as a capacity issue with a plan, before it shows up as mistakes.
- **A client engineer asks you, privately, whether their architecture choice was bad.** It's an intimacy moment. Answer honestly and kindly, in private, and never repeat it to their manager.

## Exercises

### Find the bug: the outage email

You deployed a configuration change that pointed the payment service at a sandbox endpoint for [N] minutes, so card payments failed. Here's the email you drafted to the client's operations director. Find everything wrong with it before opening the answer.

```text
Subject: Payments issue

Hi Maria,

Apologies for any inconvenience earlier. There was an issue with the payment
provider's configuration which caused some transactions to not go through for a
short while. It's resolved now. Honestly the config setup we inherited is pretty
fragile and error-prone, so this kind of thing was bound to happen sooner or later.

We're going to be more careful with deployments going forward. Around 5,000
customers may have been affected but it's probably fewer. Let me know if you have
any questions!

Best,
Alex
```

<details>
<summary>Answer</summary>

At least eight defects, grouped by the trust-equation term they damage:

- **Self-orientation.** "Issue with the payment provider's configuration" suggests the *provider* was at fault. It was our configuration change. The passive voice ("there was an issue") hides who acted. The swipe at the inherited setup shifts blame to the previous team, and the client will hear it as an excuse.
- **Credibility.** "Around 5,000 customers ... but it's probably fewer" is an invented, self-contradicting number. Either state a measured figure with its source or say it's being counted and when you'll have it. "A short while" instead of the actual window invites the client to find the real figure themselves.
- **Reliability.** "More careful going forward" isn't a change anyone can check. Name the concrete prevention (configuration validation at startup that fails fast on a sandbox URL in production, a deployment check) with an owner and a date.
- **Intimacy and tone.** "Apologies for any inconvenience" is the non-apology template, and "Let me know if you have any questions!" puts the work back on the client. The subject line "Payments issue" is vague for an incident note.
- **Missing entirely:** the impact window with times and timezone, what customers saw, whether any payment was *taken* incorrectly (the client's first question), and when the full write-up is coming.

A repaired version follows the template from Case A8: first person, mechanism in one sentence, measured impact (or "counting, figure by [time]"), done and planned changes with owners, and the date of the next update. Get your own lead to read it before you send it.
</details>

### Find the bug: the reply to "can we skip security?"

The founder from Case A7 asked to skip the security review before the demo. An engineer replied:

```text
Hi Tom,

Skipping security is a really bad idea and frankly irresponsible given you're
storing customer data. Industry best practice is clear on this. I can't in good
conscience agree to it. We need the full OWASP review and 80% test coverage
before any release, no exceptions.

Thanks,
Sam
```

<details>
<summary>Answer</summary>

The engineer is right about the underlying risk and wrong about almost everything else:

- **It judges the person** ("frankly irresponsible"). The founder is making a rational trade-off under pressure. Calling it irresponsible makes them defend it instead of reconsidering it.
- **It bundles negotiable and non-negotiable items.** "Full OWASP review and 80% coverage, no exceptions" treats a coverage target, which can be deferred, the same as authentication on customer data, which can't. It gives the founder nothing to say yes to.
- **It argues from authority** ("industry best practice is clear") instead of from mechanism and cost. What's the specific risk? What does fixing it cost in hours?
- **It offers no option.** A good reply keeps the security floor, defers the rest in a written list with a date, and gives the cost of the floor in hours ([N], not "the sprint").
- **A metric presented as a rule.** "80% coverage" is not a security control, and making it a release gate here is the engineer's preference, not the client's need. That's self-orientation of a different kind: optimising for the engineer's own comfort.

A better reply is the script in Case A7: agree on speed, list the deferrals, name the three or four non-negotiables with their cost, and explain *why* in terms of customer data and investor due diligence.
</details>

### What would you do

**1.** You're outstaffed into a client's team. In a one-to-one, the client's engineering manager asks you to "keep an eye on" one of their own developers and report back on their performance. You've noticed that developer struggling.

<details>
<summary>How a senior engineer reasons about it</summary>

Start with whose trust is at stake, and in which direction. The manager is asking you to take on a role (evaluating their staff) that isn't in your engagement, and that would make you, in the team's eyes, the manager's informant. Once the team suspects that, intimacy with every engineer on it is gone, and those are the people whose candour you depend on to do your job.

So decline the *role* without declining to help. Offer what is legitimately yours to give: pairing with the developer, reviewing their code with the same care as anyone's, and flagging *work* risks (a module that's behind or fragile) through the normal channels, as you would for any teammate. If you believe the developer is struggling, the kindest effective thing is often to help them directly, and to encourage them to ask their manager for support themselves.

If the manager presses, be plain: "I'm happy to help them grow, and to raise delivery risks as I would for anyone. Assessing people isn't something I can do well from my position, and it would change how the team works with me." Then tell your own delivery manager about the request, in case it's a pattern. The underlying mechanism is the trust equation's intimacy term: it's slow to build, fast to lose, and it's lost with a whole group at once.
</details>

**2.** Three months after you advised against it (Case A2), the CTO pushes ahead with splitting the monolith into eight services. Two months in, deployments are slower and incidents have increased. The CTO asks you, privately, "Was I wrong?"

<details>
<summary>How a senior engineer reasons about it</summary>

This isn't the moment for "I told you so", even implied. It's a moment of maximum intimacy: the CTO is showing vulnerability in private, and how you respond decides whether they ever do it again.

First, answer the real question, which is "what now?" rather than "who was right?". Diagnose with them. Which services are chatty with each other, sharing data or always deployed together? Those are candidates to merge back, which is a normal outcome and not a failure. Which one or two services have genuinely independent load or release cadence? Those were worth splitting. Is part of the pain simply missing platform work (tracing, per-service pipelines, contract tests) that would pay off now?

Second, give them a way forward that protects their standing with the team. Something like "consolidate into [three] services along the boundaries that turned out to be real" can be presented as learning, because it is. Offer to write the options note with them, not for them.

Third, if you're asked directly whether the original decision was wrong, be honest and generous: "The goal was right, and the cost turned out higher than we hoped for our team size. The data we have now makes the next decision easier." It's accurate, it doesn't gloat, and it points forward. Your credibility went up the moment the prediction came true. You don't need to spend it on being right.
</details>

**3.** Your vendor's delivery manager asks you to "pad" an estimate by 50% before sending it to the client, "because clients always push back".

<details>
<summary>How a senior engineer reasons about it</summary>

Separate the legitimate concern from the method. The concern is real: single-point estimates are optimistic, and clients do negotiate. The method, hidden padding, damages credibility when it's discovered (and a technical client will discover it the first time a "two-week" task visibly takes three days), and it teaches the client that your numbers are theatre, which makes the next negotiation worse.

Propose the honest version of the same protection: estimate in ranges, make the uncertainty and its drivers explicit, include contingency as a *named line* with a rationale ("integration risk with the partner API"), and state the assumptions that, if broken, move the estimate ([Chapter 41](#chapter-41-recommendations-proposals-and-estimates)). That gives the delivery manager the buffer they need and gives the client something to discuss other than your credibility.

If the delivery manager insists on hidden padding, it's their commercial decision, and they own the number they send. Make sure your own technical estimate, as you gave it, is recorded, and don't personally present a number to the client that you know is misleading.
</details>

### Go check

Answer these from your own current engagement, not from memory:

- **The authority map.** Who may commit to scope, dates and money on your engagement? Who must hear bad news first? If you're not sure, ask your delivery manager this week.
- **Your trust ledger.** For your main client contact, which trust-equation term is weakest right now? What's one small, specific action this week that would move it? (For reliability, it's usually a promise made and kept on time.)
- **The last hard conversation.** Pick one tense client exchange from the last three months. Rewrite your own message using the structure from the matching case. What would you change?
- **The incident template.** Does your team have a client-facing incident note template? If not, adapt the one from Case A8, and get your lead's agreement before you need it.
- **Promises in flight.** List every open commitment you've made to the client ("I'll look into it", "by Friday"). Which ones are late or forgotten? Close them or re-negotiate them today.
- **Story candidates.** Which of the twelve cases have you lived, even partly? Write each as a STAR skeleton in your private story bank ([Chapter 37](#chapter-37-the-story-bank-evidence-portfolio)), with the client anonymised.

Keep everything about a real client, vendor or colleague private. What goes into a public portfolio is *method*: templates, anonymised decision records, public-data spikes and evaluations. Real stories go public only when they're anonymised and you have permission ([Chapter 43](#chapter-43-positioning-and-public-proof)).

## Sources & Further Reading

- David H. Maister, Charles H. Green and Robert M. Galford, *The Trusted Advisor* (Free Press, 2000). The source of the trust equation and the distinction between expertise and advice.
- Peter Block, *Flawless Consulting: A Guide to Getting Your Expertise Used* (3rd ed., Pfeiffer, 2011). Contracting, presenting versus underlying problems, and working with resistance.
- Gerald M. Weinberg, *The Secrets of Consulting: A Guide to Giving and Getting Advice Successfully* (Dorset House, 1985). The laws of consulting, including "it's always a people problem".
- Roger Fisher and William Ury, *Getting to Yes: Negotiating Agreement Without Giving In* (1981). Interests versus positions, the basis of Cases A5, A7 and A10.
- Frederick P. Brooks Jr., *The Mythical Man-Month* (anniversary ed., Addison-Wesley, 1995). Brooks's law and the second-system effect.
- Joel Spolsky, "Things You Should Never Do, Part I" (*Joel on Software*, 2000). The classic argument against the big-bang rewrite.
- Martin Fowler, "StranglerFigApplication", "MonolithFirst" and "MicroservicePremium" (martinfowler.com). The incremental alternatives in Cases A1 and A2.
- Betsy Beyer et al. (eds.), *Site Reliability Engineering* (O'Reilly, 2016), chapter "Postmortem Culture: Learning from Failure". Blameless post-incident reviews.
- Kerry Patterson et al., *Crucial Conversations: Tools for Talking When Stakes Are High* (McGraw-Hill, 2002). Useful for Cases A6 and A8.
- Within this book: [Chapter 16](#chapter-16-working-like-a-middle-developer) (communication, ADRs, estimation), [Chapter 35](#chapter-35-production-incidents) and [Chapter 30](#chapter-30-the-azure-casebook-real-incidents-real-fixes) (the technical casebooks), and [Chapter 37](#chapter-37-the-story-bank-evidence-portfolio) (turning cases into stories).
