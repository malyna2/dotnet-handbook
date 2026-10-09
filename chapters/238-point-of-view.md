# Chapter 38: Having a Point of View

Chapters 38–43 are about a shift that no certification measures: going from being the person a client hands tickets to, to being the person a client *asks*. For a .NET engineer placed through an outsourcing or outstaffing firm, that shift decides a lot. It decides whether you are renewed or rotated, whether you are invited to the architecture meeting or told its outcome, and whether the client's CTO asks for you by name when the next contract is signed. The vendor sells your hours. What the client comes to value, or fails to find, is your judgment.

This chapter is about the first ingredient of that judgment: **having a point of view**. That means positions you hold on purpose, can defend with a mechanism, and would drop for a stated reason. The next chapters build on it. [Chapter 39](#chapter-39-discovery-and-diagnosis) turns a point of view into a diagnosis of a specific client's system, [Chapter 40](#chapter-40-lab-the-net-health-check) practises that diagnosis on a real codebase, [Chapter 41](#chapter-41-recommendations-proposals-and-estimates) turns it into written recommendations and estimates, [Chapter 42](#chapter-42-the-advisory-casebook) walks through worked engagements, and [Chapter 43](#chapter-43-positioning-and-public-proof) makes all of it visible.

What this chapter does **not** repeat is how to move a decision: stakeholder communication, disagreeing productively and ADRs are covered in [Chapter 16](#chapter-16-working-like-a-middle-developer), and influence without authority in [Chapter 36](#chapter-36-senior-behaviours-career-and-interviews). Those chapters are about how to move a decision. This chapter is about having something worth moving.

```
                  what the client sees
                  ────────────────────
  "a pair of hands"  ──►  "someone who knows .NET"  ──►  "someone whose opinion we want"
        tickets              answers questions              is asked before deciding
           │                        │                                │
      throughput               knowledge                 point of view + diagnosis
                                                         + proof + visibility
```

## Why Clients Pay for Judgment, Not Knowledge

Start with an uncomfortable observation. Almost everything you know about .NET, the client can get somewhere else, and most of it for free. The documentation is public. The GitHub issues are public. A model will explain the difference between `IQueryable` and `IEnumerable` faster than you can open your mouth. A contractor who competes on *knowing things* competes with the documentation, with every other contractor on the vendor's bench, and with a chat window. That is a race to the lowest rate.

What the client cannot get for free is **knowledge applied to their situation, with someone accountable for it**. Three properties make judgment worth paying for:

1. **It is contextual.** "EF Core is slower than Dapper" is knowledge. "On *your* order-history endpoint, EF's change tracking and materialisation cost you about what we measured, and on the other 140 endpoints it costs nothing that matters, so move this one query and leave the rest alone" is judgment. The second sentence needs your system, your numbers and a decision about what matters.
2. **It closes options.** A client with a problem already has too many options. Every blog post offers another one. Judgment is valuable because it *removes* options: it says which three of the twelve are real candidates and which one to try first. A list of options hands the hard part back to the client.
3. **It carries risk.** Someone who commits to a position can be wrong, and the client knows it. That is exactly why a committed position means something: you are putting your credibility behind it. An advisor who never commits can never be shown to be wrong, so their advice carries no information.

David Maister, Charles Green and Robert Galford, in *The Trusted Advisor*, put trust into a well-known formula: trust = (credibility + reliability + intimacy) / self-orientation. The denominator is the part engineers tend to forget. A contractor whose recommendations always happen to need more contractor hours, or whose opinions follow whatever the client's CTO said last week, has high self-orientation. Clients discount it heavily, even when they can't say why. A point of view you hold *regardless* of whom it pleases is the most direct way to push that denominator down.

> **Pitfall.** Treating "the client is always right" as a professional stance. The client is always right about *what they want*. Their business goals, budget, risk appetite and deadlines are theirs to set. They are not automatically right about *how to get it*, and they did not hire a senior engineer so that someone would type up their own guesses. Deferring on the "how" feels polite. What it tells the client is that there is nobody home.

### The outstaffing trap

An outstaffed engineer sits inside the client's team and follows the client's process. Nothing about that setup asks you for an opinion. Tickets arrive already specified, the architecture is "already decided", and the vendor's account manager would prefer that you didn't cause friction. The result is a stable equilibrium in which you are billed as a senior and used as a middle.

Nobody breaks that equilibrium for you. You break it by offering a *small*, well-reasoned, correct opinion at the right moment. "Before we build the export on top of that query, I'd check its plan; I think it scans the whole table" is enough. Then you repeat that. Each correct call raises the credibility term. Each call you make without spin lowers self-orientation. After enough of them the client starts asking before deciding. There is no shortcut that skips the repetitions.

## The Five Layers of Client-Perceived Expertise

A client does not see "expertise" directly. They infer it from things they can observe. It helps to separate those observable things into five layers, because each one fails in its own way and needs its own fix.

```
        ┌──────────────────────────┐
        │        VISIBILITY        │  they know you exist and what you stand for     (Ch 65)
        ├──────────────────────────┤
        │          PROOF           │  evidence you've been right before              (Ch 36, 65)
        ├──────────────────────────┤
        │        DIAGNOSIS         │  you can apply it to THEIR system               (Ch 61, 62)
        ├──────────────────────────┤
        │      POINT OF VIEW       │  you have positions, not just facts             (this chapter)
        ├──────────────────────────┤
        │        KNOWLEDGE         │  you know how the platform works                (Ch 1–35, 50–51)
        └──────────────────────────┘
          each layer rests on the ones below it; a gap anywhere reads as a gap in all of them
```

| Layer | What the client observes | How it fails | What it sounds like when it's missing |
|---|---|---|---|
| **Knowledge** | You answer technical questions correctly and quickly | Shallow knowledge falls apart after the second "why?" | "I'd have to look that up" on a core topic, repeatedly |
| **Point of view** | You recommend; you don't just list | Neutral trade-off lists, or opinions held for the wrong reasons | "There are pros and cons to both approaches" |
| **Diagnosis** | You find *their* real problem, which is often not the one they reported | Pattern-matching a generic solution onto a specific system | "You should use microservices" before reading the code |
| **Proof** | Past results, write-ups, numbers from real runs | Claims with no artefacts, or artefacts nobody can check | "Trust me, I've done this before" |
| **Visibility** | People who haven't worked with you already know what you think | Good work only the immediate team ever sees | Being introduced as "one of the vendor's developers" |

Two properties of this stack matter in practice.

**The layers are inferred from the top down but built from the bottom up.** A prospective client meets your visibility first: a talk, a post, a colleague's recommendation. Then they check it against your proof, then against how you diagnose their system in the first week. If they find a gap, it spreads to every layer. One confident, wrong diagnosis in week one will make them discount a strong portfolio. So you build from the bottom and are judged from the top.

**Point of view is the hinge.** Knowledge without a point of view is a reference book. Diagnosis without one has nothing to diagnose *against*, and visibility without one is just aggregating news.

## Trade-off Lists vs. Committed Positions

Every engineer has written the neutral trade-off list. The client asks "should we use Azure Service Bus or RabbitMQ?", and the reply is a balanced table: managed vs self-hosted, sessions vs quorum queues, pricing tiers vs ops cost, feature for feature. It is accurate and fair, and it leaves the client exactly where they started, now holding a table.

The list is not wrong. It is **incomplete**. It does the knowledge half of the work and skips the judgment half. The missing step is the one where you weigh the rows *for this client* and say which way the balance tips.

| | Neutral trade-off list | Committed position |
|---|---|---|
| Answers | "What are the options?" | "What should we do?" |
| Work done for the client | Research | Research **and** the decision |
| Risk carried by the advisor | None; it can't be wrong | Real; it can be wrong |
| Information content | Low; the client could have searched for it | High; it encodes your weighting |
| Failure mode | Paralysis, or the client picks for the wrong reason | Overconfidence, if held without a mechanism |
| When it's right | Early exploration; the client explicitly wants to decide | Almost every time a client asks "what would you do?" |

The committed position does not throw the trade-offs away. It **ranks** them. Here is the same Service Bus vs RabbitMQ question, answered both ways:

```
NEUTRAL
  Both are mature brokers. Service Bus is fully managed and supports sessions,
  scheduled messages and dead-lettering. RabbitMQ is open source, very flexible,
  and can run anywhere. Service Bus costs money per operation; RabbitMQ costs
  operational effort. It depends on your requirements.

COMMITTED
  Service Bus. You're Azure-only, the team is four developers with no one on call
  for infrastructure, and your ordering requirement is per-customer, which is
  exactly what Service Bus sessions give you. RabbitMQ would be my pick if you
  needed to run on-prem or across clouds, or had a platform team to operate it;
  you have neither. I'd change my mind if the throughput estimate from the
  capacity exercise lands well above what the Standard tier handles, because
  then we're comparing Premium pricing against running RabbitMQ ourselves,
  and that's a different conversation.
```

The committed answer is only a little longer. It names the decision, the two or three facts about *this client* that decide it, the case where the other option wins, and the condition that would reverse it. That is the shape the next section formalises.

> **Best practice.** When a client asks an either/or question, the first sentence of your answer is the recommendation. Reasoning comes second and trade-offs third. Engineers habitually build up to the conclusion, and a busy reader stops before reaching it. [Chapter 16](#chapter-16-working-like-a-middle-developer) makes the same point about written communication in general: put the ask first.

> **Gotcha.** "I'll lay out the options and let you decide" can be the right answer, when the decision truly turns on something only the client can weigh, such as their risk appetite, a political constraint, or a budget line you can't see. Then say *that*: "This comes down to how much you value X over Y; that's your call. If X matters more, A; if Y, B." That is still a position: a position on *what the decision turns on*. What you don't do is hand over a table and walk away.

## The Anatomy of a Defensible Opinion

"Defensible" doesn't mean "correct". It means the opinion survives a skeptical, intelligent person pushing on it. That happens not because you win the argument, but because every push lands on something you have already thought through. A defensible opinion has five parts.

```
  ┌─────────────┐   ┌──────────────┐   ┌────────────┐   ┌─────────────┐   ┌──────────────────┐
  │   CLAIM     │──►│  MECHANISM   │──►│  CONTEXT   │──►│ COUNTER-CASE│──►│ I'D CHANGE MY    │
  │ what to do  │   │ why it works │   │ when it    │   │ where the   │   │ MIND IF…         │
  │             │   │ (causal)     │   │ holds      │   │ opposite    │   │ (observable)     │
  │             │   │              │   │            │   │ wins        │   │                  │
  └─────────────┘   └──────────────┘   └────────────┘   └─────────────┘   └──────────────────┘
     answers           answers            answers          answers            answers
    "so what?"         "why?"          "always?"        "what about…?"     "are you sure?"
```

Each part answers one predictable challenge. When a part is missing, that challenge turns into an argument.

**1. The claim.** One sentence, specific enough to act on. "Use the outbox pattern" is a claim. "Think carefully about consistency" is not, because nobody could disagree with it. That is the test: *could a competent engineer reasonably hold the opposite view?* If not, you have a platitude, not an opinion.

**2. The mechanism.** The causal story of *why* the claim is true, told in terms of how the system actually behaves. It is the most important part and the one most often missing. "Because it's best practice" is not a mechanism. "Because the database commit and the broker publish are two separate operations, and a crash between them loses or duplicates the event; putting the event in the same transaction as the data makes the pair atomic" is a mechanism. It lets the listener check your reasoning instead of trusting your authority. It also lets you work out the context and counter-case yourself, because once you know *why* something works you know when it stops working.

**3. The context.** The conditions under which the mechanism applies. Every engineering claim has them, and experts state them without being asked. "For systems where the message and the data change live in the same relational database" is context. Leave it out and the claim sounds universal, and universal claims are easy to refute with a single counterexample.

**4. The counter-case.** The situation where the opposite choice wins, stated honestly and before anyone raises it. Naming it first does two things. It shows you have looked at the alternative seriously. And it takes away the skeptic's best move, because the counterexample they were about to produce is already on the table, together with why it doesn't apply here.

**5. "I'd change my mind if…"** An observable condition that would reverse the claim. Not "if someone gives me a good argument"; that is unfalsifiable. It has to be something you could measure or see: "if the relay's lag p99 exceeds the business's freshness requirement under realistic load" or "if we find the broker and the database need to stay consistent across different storage engines." This clause is what separates an opinion held on evidence from one held out of identity. If you can't write it, you don't hold the opinion. The opinion holds you.

Here is the template. Use it for every entry in the canon that follows.

```
**Position**
One sentence. Specific enough that a competent engineer could disagree.

**Mechanism**
Why it's true, in terms of how the system behaves. No appeals to authority.

**Holds when**
The conditions the mechanism depends on.

**Counter-case**
Where the opposite choice wins, and why that isn't this situation.

**I'd change my mind if**
Observable, measurable conditions. Not "a better argument".

**Evidence / where the mechanism lives**
Links: book chapter, your own measurement, an ADR, a post-mortem.

**Held since / last revised**
Dates. Positions have a history.
```

> **Best practice.** Say the mechanism out loud even to clients who "don't need the details". A non-technical stakeholder can't check an EF Core query plan, but they can tell whether you explained *why* or simply asserted *what*. The mechanism is how they learn that your confidence rests on something. You pitch it in their vocabulary ("two separate saves that can fail independently"), not yours, but you don't leave it out.

## Strong Opinions, Loosely Held, and How It Slides into Dogma

The futurist Paul Saffo popularised the maxim "strong opinions, weakly held" in a 2008 essay on forecasting. His method was to force yourself to a tentative conclusion early, then set about proving it wrong, using each failed version to steer the search for better information. It is often quoted as "strong opinions, loosely held", and in software culture it has drifted a long way from what he meant.

The original is a **process**. You form a view quickly so that you have something concrete to attack, then you attack it. The strength is there to make the view testable. The looseness is how you run the test.

The drifted version is a **personality**: be loud and confident, and if someone pushes back hard enough, fold gracefully. That keeps the confident delivery and drops the self-attack. Nothing in it says what would make you let go, so in practice the opinion is held exactly as loosely as the social pressure in the room allows. Critics have pointed out that this rewards confident people for being confident, not for being right. Cedric Chin's essay "Strong Opinions, Weakly Held Doesn't Work That Well" at Commoncog is a good read on this.

There are two ways it goes wrong, and they point in opposite directions:

```
   DOGMA                      CALIBRATED POSITION                    WEATHERVANE
   ─────                      ───────────────────                    ───────────
   strong, never updated      strong, updated on evidence            weak, updated on pressure
   "always use X"             "X, because M; unless C"               "X… or Y, whatever you prefer"
   the mind-changer           the mind-changer is written            the mind-changer is
   doesn't exist              down in advance                        whoever spoke last

   client learns:             client learns:                         client learns:
   your answer is fixed       your answer carries information        your answer carries none
```

**How positions harden into dogma.** It rarely happens all at once. It builds up through a few steps you can recognise:

1. **The mechanism falls out.** You say "modular monolith first" so often that you stop saying *why*. Once the mechanism is gone, you can't tell when it stops applying.
2. **The context falls out.** "For a team of this size" quietly becomes "always".
3. **The position becomes identity.** You're known as "the monolith person". Now changing your mind costs you something socially, so you stop looking for reasons to.
4. **Counterevidence gets explained away.** When a client's microservices work fine, it's "because they have an unusually good platform team", and you never ask whether that is common.

The fix is structural, not a matter of willpower. Write the "I'd change my mind if" clause **before** you are in an argument, date it, and review it on a schedule (see [the calibration section](#calibration-keeping-score-on-yourself) below). A written clause is hard to quietly move. In the middle of a disagreement you can't redraw the line, because you drew it last spring.

> **Pitfall.** Confusing *confidence of delivery* with *strength of opinion*. You can state a position calmly, with an explicit probability, and still hold it strongly: "I'm fairly sure, call it 80%, that the lock contention is in the outbox relay, not the API." Clients don't need you to sound certain. They need you to be *accurate about how certain you are*. That is the whole calibration section in one sentence.

> **Gotcha.** Philip Tetlock's research on expert political judgment (the "foxes and hedgehogs" distinction he borrowed from Isaiah Berlin) found that experts organised around one big idea tended to forecast worse than experts who drew on many small models and updated often. Engineers with a signature position — "everything should be event-driven", "ORMs are always a mistake" — are hedgehogs. The trap is that hedgehogs make better *content*: a single strong thesis is more quotable. [Chapter 43](#chapter-43-positioning-and-public-proof) deals with how to be visible without turning into a hedgehog.

## Building Your Opinion Canon

An **opinion canon** is a written set of 15–20 positions on recurring decisions in your field. Each one is in the five-part format above, dated, and linked to the evidence behind it. It is the reference you consult before a client asks, so that when they do, you give a considered answer instead of an improvised one.

Why write it down rather than "just know" your opinions?

- **Writing exposes missing mechanisms.** Many positions that feel solid in your head turn out to be "because everyone says so" once you try to write the mechanism paragraph. Better to find that at your desk than in front of a client's architect.
- **It makes you consistent.** A client who hears one position in week one and a contradictory one in week six, both stated confidently, will stop trusting both. The canon keeps you consistent with yourself, and when you do change, the change is deliberate and explained.
- **It is proof as well as preparation.** A public canon is evidence of judgment that a CV cannot provide (see [Chapter 37](#chapter-37-the-story-bank-evidence-portfolio) and [Chapter 43](#chapter-43-positioning-and-public-proof)).
- **It gives you a baseline for [diagnosis](#chapter-39-discovery-and-diagnosis).** A health check measures a system against a view of what healthy looks like. Your canon *is* that view, written down.

> **The portfolio rule.** Your canon and your radar go in **your own public portfolio repo**, not in this handbook's repository. Stories that back them up must be anonymised. A position may rest on "a payments client in 2025", but not on a named company, and not on anything covered by an NDA without permission. When in doubt, the evidence you cite is your own lab run (Chapters 19, 37 and 40), a public benchmark, or a book chapter. Real-client stories stay in your private story bank.

What follows are eight worked positions. Each is grounded in a chapter of this book that holds the full mechanism. They are *examples of the format*, and reasonable positions in their own right, but a canon you copy is worth nothing. Rewrite each one in your own words, check its mechanism against your own experience, and change it where you disagree. **Where you disagree with this book is where your canon gets interesting.**

### Canon #1: Modular monolith first; services earn their way out

**Position.** A new system, or a new team's system, starts as a modular monolith: one deployable unit, strict internal module boundaries, ideally a schema per module. A module is extracted into a service only when there is a specific, named reason.

**Mechanism.** Microservices trade in-process calls and local transactions for network calls and eventual consistency. That buys independent deployment and independent scaling. The cost is paid immediately and on every request: latency, partial failure, distributed tracing, contract versioning, data duplication. The benefit only shows up once you have teams that need to deploy independently, or components with very different scaling profiles. A modular monolith keeps the boundaries, which are the expensive part to get right, without paying the network tax, and a clean boundary is what makes a later extraction cheap. Get the boundaries wrong inside a monolith and you move some code. Get them wrong across services and you get a distributed monolith.

**Holds when.** One team or a few; boundaries not yet proven by real change patterns; no module with scaling needs orders of magnitude apart from the rest.

**Counter-case.** Several teams that already block each other's releases; a component with very different runtime needs (a GPU-bound inference worker, a burst-scaling ingestion endpoint); a regulatory requirement to isolate a component's data and deployment.

**I'd change my mind if.** Deploy-coordination cost becomes measurable: releases held up waiting on other modules, merge queues backing up across module boundaries. Or a module's resource profile shows up in the metrics as the thing driving the whole app's scaling.

**Mechanism lives in.** [Chapter 21: Architecture](#chapter-21-architecture) (monolith vs microservices vs modular monolith); [Chapter 44](#chapter-44-capstone-one-project-growing-up) (the capstone's "split into microservices" step, and what it costs); [Chapter 20](#chapter-20-distributed-systems) (the fallacies of distributed computing you sign up for).

### Canon #2: EF Core by default; Dapper on measured hot paths

**Position.** Data access defaults to EF Core. A specific query moves to Dapper, or to hand-written SQL through EF, when a measurement shows EF's overhead matters on that path. Not before, and not across the whole codebase.

**Mechanism.** EF Core's cost is expression translation, change tracking and materialisation. Its benefit is migrations, LINQ composition, the unit of work, and a domain model that changes safely. On most endpoints, the database round-trip and the query plan dominate latency, and EF's overhead is small in comparison. On a small number of hot, read-heavy paths the overhead becomes a real share. You can only find those paths by measuring. Replacing EF wholesale pays the Dapper cost (hand-owned SQL, no change tracking, no migrations) everywhere in order to collect the benefit in a few places. `AsNoTracking`, projections and compiled queries also close much of the gap without leaving EF.

**Holds when.** A typical line-of-business app with a relational store; a team that isn't made up of SQL specialists; a write side with real domain rules.

**Counter-case.** Reporting and analytics services that are mostly complex read SQL; a team of strong SQL developers who will own every query anyway; tight latency budgets across the board, not just on a few paths.

**I'd change my mind if.** Profiling shows EF overhead (not the query plan, not N+1) as a large part of latency on *many* endpoints, or the SQL EF generates keeps defeating tuning on the queries that matter most.

**Mechanism lives in.** [Chapter 7: Data Access](#chapter-7-data-access) (EF Core internals, Dapper, mixing both in one transaction); [Chapter 17: Runtime Internals and Performance](#chapter-17-runtime-internals-and-performance) (how to measure before changing); [Chapter 19](#chapter-19-the-slow-query-lab-reading-execution-plans) (reading the plan, which is usually where the time actually goes).

### Canon #3: The outbox over distributed transactions

**Position.** When a service must change its database *and* publish an event, it writes the event to an outbox table in the same local transaction, and a relay publishes it. Not a distributed transaction (2PC) across database and broker, and not "save then publish and hope".

**Mechanism.** The database commit and the broker publish are two separate operations with separate failure modes. A crash between them either loses the event or publishes one for data that was never saved. 2PC can make them atomic, but it needs both resources to take part in a coordinator protocol. Many cloud brokers don't, and where it is supported it holds locks across the network and makes the coordinator a failure point. The outbox turns a cross-resource problem into a single-resource one: one local ACID transaction covers both the data and the *intent* to publish. The price is at-least-once delivery, so consumers must be idempotent, plus a relay you have to run and monitor, plus some publish latency.

**Holds when.** The data and the outbox can live in the same transactional store; consumers can be made idempotent; a publish delay of seconds is acceptable to the business.

**Counter-case.** Very high-volume streams where change data capture (CDC) from the transaction log is cheaper than polling an outbox table; workflows that really are long-running across services, where a saga with compensations is the right shape and the outbox is only one piece of it.

**I'd change my mind if.** Relay lag under realistic load exceeds the business's freshness requirement and tuning doesn't fix it (then look at CDC), or the outbox table's write amplification shows up as a bottleneck in the database's own metrics.

**Mechanism lives in.** [Chapter 11: Messaging and Background Work](#chapter-11-messaging-and-background-work) (the outbox pattern and idempotent consumers); [Chapter 18](#chapter-18-data-in-depth) (outbox vs CDC); [Chapter 20](#chapter-20-distributed-systems) (idempotency as the antidote to "did that happen?"). [Chapter 16](#chapter-16-working-like-a-middle-developer) has a worked ADR for exactly this decision.

### Canon #4: Long-lived systems stay on LTS

**Position.** Production systems with a multi-year maintenance horizon target the current LTS release of .NET. An STS release is chosen only for a specific feature you need now, with an upgrade already planned. Upgrades are scheduled well before end of support, never in the month it runs out.

**Mechanism.** LTS and STS builds get the same quality of fixes while they are supported; the only difference is how long support lasts. LTS releases get three years, STS releases a shorter window (extended to 24 months starting with .NET 9). Every major upgrade costs something: breaking changes, re-testing, dependency bumps. LTS lets you pay that cost less often, on a calendar you can plan. Running past end of support means no security patches, which is both a vulnerability and an audit finding. The calendar can also surprise you: .NET 8 (LTS) and .NET 9 (STS) both reach end of support on November 10, 2026, while .NET 10 (LTS) is supported until November 14, 2028. A team that moved to .NET 9 "to be ahead" gained no extra runway.

**Holds when.** The product outlives a single release cycle; the team has limited capacity for platform work; compliance requires supported runtimes.

**Counter-case.** Short-lived services, internal tools, or teams with strong automated upgrade pipelines for whom yearly upgrades are routine; a specific STS feature that removes real pain now (a performance win on a measured bottleneck, a platform capability you need).

**I'd change my mind if.** The team's measured upgrade cost falls to routine, for example a major version bump handled in days, with tests catching the breaks. Then the argument for LTS weakens, and staying current gets you performance improvements sooner.

**Mechanism lives in.** [Chapter 3: How .NET Runs Your Code](#chapter-3-how-net-runs-your-code) (release cadence, LTS vs STS); [the appendix](#appendix-net-version-comparison-cheat-sheet) (dates and support windows); [Chapter 24](#chapter-24-working-with-legacy-brownfield-code) (the end-of-life treadmill).

### Canon #5: Managed identity over secrets

**Position.** Workloads authenticate to cloud resources (databases, Key Vault, storage, Service Bus) through a managed identity or workload identity. A stored secret is the exception, documented and on a rotation schedule. Moving a secret into Key Vault is not the goal. The goal is not having the secret at all.

**Mechanism.** A secret is a bearer credential. Whoever holds it *is* the service, from anywhere, until it is rotated. Every copy (config file, CI variable, a developer's laptop, a log line) is another way to leak it, and rotation is a coordinated change that teams tend to put off. A managed identity replaces "something the app knows" with "something the platform vouches for". The platform issues short-lived tokens to the workload, there is nothing long-lived to copy, and access is granted and revoked through RBAC on the identity. Key Vault on its own still needs the app to authenticate *to the vault*, and with managed identity that last secret disappears too.

**Holds when.** The workload runs on a platform that issues identities (App Service, Functions, AKS with workload identity, VMs, Container Apps) and the target resource accepts Entra ID authentication.

**Counter-case.** Third-party APIs that accept only API keys (store the key in Key Vault, fetch it using the managed identity); on-premises or other-cloud workloads, where workload identity federation is the equivalent move; local development, where developer credentials take the managed identity's place.

**I'd change my mind if.** A target service in the client's stack doesn't support Entra authentication. That case is a documented exception, not a reversal. Or if the RBAC model becomes so sprawling that nobody can say which identity has access to what. That is a governance problem, but a real one.

**Mechanism lives in.** [Chapter 12: Security Essentials](#chapter-12-security-essentials) (secrets management; zero trust and workload identity); [Chapter 29: Azure in Depth for .NET Developers](#chapter-29-azure-in-depth-for-net-developers) (Entra ID, managed identity and RBAC, mechanically); [Chapter 30](#chapter-30-the-azure-casebook-real-incidents-real-fixes) (what goes wrong when the app uses a different principal than you think).

### Canon #6: Observability before microservices

**Position.** A team doesn't split a system into services until it can answer "why is this request slow?" and "what happened to this order?" in the system it *already* has: structured logs, metrics, and distributed traces with correlation IDs, all working. Observability comes first. It is a prerequisite, not a follow-up task.

**Mechanism.** In a monolith, a stack trace and a profiler reach most problems, because the whole request happens in one process. Split it, and a single user action becomes a chain of network hops across processes, each with its own logs and its own clock. Without trace context carried across those hops, diagnosis turns into manual log archaeology across services, and outages last longer because nobody can *find* the problem, not because it is hard to *fix*. Distribution multiplies the number of failure points and cuts your ability to see them. Only observability built beforehand restores that ability. A team that can't debug its monolith from telemetry will do worse with twelve services.

**Holds when.** Any move towards more processes: services, background workers, queues, serverless functions.

**Counter-case.** Honestly, I don't know a strong one. The nearest is a very small, well-understood integration (one worker, one queue) where log correlation by message ID is enough. Even there, OpenTelemetry costs little to add.

**I'd change my mind if.** Hard to see it happening. This is one of the positions where the "change my mind" clause is weak, and you should say so: a position you can't imagine being wrong about deserves *more* scrutiny, not less. Review it anyway.

**Mechanism lives in.** [Chapter 9: Exceptions, Logging and First Diagnosis](#chapter-9-exceptions-logging-and-first-diagnosis) (the three signals, correlation across services, and the 3 a.m. walk); [Chapter 20](#chapter-20-distributed-systems) (SLOs and error budgets as what the telemetry is *for*).

### Canon #7: Test against the engine you run in production

**Position.** Anything that touches SQL (queries, migrations, constraints, concurrency behaviour) is tested against the same database engine as production, in a container. The EF Core in-memory provider is not used as a stand-in for a relational database.

**Mechanism.** A test is only worth as much as its fidelity to the behaviour it claims to check. The in-memory provider isn't relational: it doesn't enforce constraints the way the real engine does, doesn't support transactions or raw SQL, and translates queries differently. So a test can pass against it and the same code can fail in production, which is a green suite giving false confidence. Testcontainers makes the real engine about as cheap to run as a fake, so the fidelity argument now costs little.

**Holds when.** There is a relational store and the code does anything beyond trivial CRUD against it.

**Counter-case.** Pure domain logic with no persistence, which should be tested without any database at all; teams whose CI truly can't run containers (then invest in making it able to, and meanwhile accept the gap knowingly).

**I'd change my mind if.** Container start-up and image pulls make the suite slow enough that people stop running it locally. The fix is usually reusing containers and caching images, but if it doesn't work, a faster lower-fidelity tier plus a smaller real-engine tier is a reasonable compromise.

**Mechanism lives in.** [Chapter 8: Testing](#chapter-8-testing) (the in-memory provider trap, Testcontainers); [Chapter 25](#chapter-25-observability-and-testing-at-scale) for the specialised tiers.

### Canon #8: Strangle; don't rewrite

**Position.** Modernising a legacy .NET Framework system (or any large running system) happens incrementally behind a routing façade, one slice at a time, with the old system still serving the rest. A big-bang rewrite is not on the table unless the case against incremental migration is specific and written down.

**Mechanism.** A running system encodes years of edge cases that nobody has written down, and a rewrite rediscovers them one production incident at a time. The business doesn't stop while you rewrite, so the target keeps moving and you maintain two systems. Value only arrives at cutover, which makes the project easy to cancel, and cutover is all-or-nothing. The strangler fig reverses each of these: value arrives slice by slice, each slice is verified against the live system's actual behaviour, and each step can be rolled back.

**Holds when.** The system is in use, its behaviour is only partly specified, and the business can't freeze feature work.

**Counter-case.** A small system whose behaviour is fully covered by characterisation tests; a platform so hostile to change (unsupported runtime, no seams at all, no way to route traffic) that the façade itself would cost more than a careful rewrite of a *small* scope.

**I'd change my mind if.** The cost of keeping the old and new systems running side by side (duplicate data sync, a routing layer, two deploy pipelines) grows beyond the pace of migration for more than a couple of quarters. That means the strangling has stalled and needs a different plan.

**Mechanism lives in.** [Chapter 24: Working with Legacy & Brownfield Code](#chapter-24-working-with-legacy-brownfield-code) (strangler fig vs big rewrite, .NET Framework to modern .NET).

### Filling out the rest of the canon

Eight positions are a start; twelve more make a working canon. Some prompts, plus others you'll recognise from your own client work (coverage targets, dependency pinning, schema registries):

- Where resilience lives: in every HTTP client through the standard handlers, or in a mesh? ([Chapter 20](#chapter-20-distributed-systems))
- Clean Architecture's layers: when they pay for themselves and when they are ceremony. ([Chapter 10](#chapter-10-design-basics), [Chapter 21](#chapter-21-architecture))
- Background work: `BackgroundService`, Hangfire/Quartz, or a queue plus workers? ([Chapter 11](#chapter-11-messaging-and-background-work))
- Multi-tenancy: shared schema with a tenant column, schema per tenant, or database per tenant? ([Chapter 18](#chapter-18-data-in-depth))
- Kubernetes for a team of five: yes, no, or "use the managed container platform one step down"? ([Chapter 14](#chapter-14-containers-and-linux))
- How much AI assistance in the codebase, and under which review rules? ([Chapter 32](#chapter-32-the-ai-native-developer-thriving-in-the-ai-era))

> **Best practice.** Include at least two positions where you disagree with the mainstream, or with this book. A canon that agrees with every conference talk is a summary, not a point of view. The disagreements are what a client remembers, and what shows that you think instead of repeat, as long as each one has a mechanism behind it.

> **Pitfall.** A canon made entirely of "should" statements about *other people's* code. A good canon also includes positions about *process*: when to write an ADR, when to spike, how you estimate, when you refuse to give a number. Clients experience your process every week and your architecture opinions only occasionally.

## A Personal Tech Radar

The canon holds *positions* on decisions. A **tech radar** holds *stances* on specific technologies, techniques, tools and platforms, and it tracks how those stances move over time. The format was made popular by Thoughtworks' Technology Radar: items ("blips") placed on four rings, in four quadrants. Thoughtworks also publishes a "Build Your Own Radar" tool, and many companies keep internal radars in the same format.

The four rings, as Thoughtworks defines them in substance:

| Ring | Meaning | What it commits you to | .NET-flavoured example (yours will differ) |
|---|---|---|---|
| **Adopt** | Proven and mature; you'd seriously consider it the default | Recommending it without much caveat | OpenTelemetry for traces and metrics; Testcontainers for data-access tests; .NET 10 for new long-lived services |
| **Trial** | Ready to use, not as fully proven; use on a project that can absorb risk | Having used it for real, or being willing to on a bounded project | Aspire for local multi-service orchestration |
| **Assess** | Worth exploring to understand how it would affect you | Having looked closely enough to say what it's *for* | Native AOT for a latency-sensitive API; a new data-access library you haven't shipped |
| **Hold** | Getting attention, but proceed with caution, or stop using it for new work | Being ready to explain *why* in one breath | EF Core in-memory provider as a relational test double; starting a greenfield system as microservices; new code on .NET 8 or 9 this late in their support window |

Thoughtworks' quadrants are *Techniques*, *Tools*, *Platforms*, and *Languages & Frameworks*. For a personal radar they work fine as they are. Use your own if they fit your practice better, for example *Architecture*, *Data*, *Delivery* and *Cloud*.

The example column is illustrative. It shows the *shape* of an entry, not a recommendation to copy. Your radar is only worth something if it reflects **your** hands-on experience. An item in Adopt that you have never shipped is borrowed opinion.

The canon changes rarely; the radar's most useful property is **movement**. An item that went from Assess to Trial to Adopt over two years, with a dated note at each step, shows that you evaluate technology in stages instead of jumping on trends. An item that moved from Adopt to Hold, with the reason, shows you are willing to update, and that is worth more to a client than any number of blips that never moved.

### Keeping the radar honest

- **Publish editions, not a live document.** Date each edition (quarterly or twice a year) and keep old ones. The diff between editions is the point.
- **Every blip gets a rationale** of two to four sentences: what you've done with it, where it fits, the one thing to watch out for. An entry with no rationale is just a logo.
- **Mark hands-on vs read-about.** A simple marker, such as "(used in production)", "(lab only)" or "(reading)", keeps you honest and tells the reader how much weight to give it.
- **Hold is not an insult.** Put things in Hold with care and a specific reason. "Hold: overhyped" says nothing. "Hold for new work: this replaces a problem you don't have with a platform you'd have to operate" says something.
- **Keep it small.** A personal radar with 80 blips is a list of everything you've heard of. Twenty to forty items you can actually speak to is plenty.

A minimal edition in Markdown, for your portfolio repo:

```
**Radar — edition [YYYY-MM]**

**Adopt**
- OpenTelemetry (.NET) — Techniques/Tools — used in production at [anonymised client type].
  Traces + metrics via OTLP; logs still via [provider]. Watch: cardinality of custom attributes.
  Moved from Trial in edition [YYYY-MM] after [evidence].

**Trial**
- [item] — [quadrant] — (lab only). What it's for, where it fits, what I'm watching.

**Assess**
- [item] — [quadrant] — (reading). What question I'm trying to answer about it.

**Hold**
- [item] — [quadrant] — the specific reason, and what I recommend instead.

**Moved since last edition**
- [item]: [old ring] → [new ring], because [evidence].
```

## Saying "It Depends" Like an Expert

"It depends" is the most mocked answer in software, and also the most honest one. It is mocked because it usually ends the answer. It is honest because almost every engineering answer really does depend on something. The expert's version keeps the honesty and removes the evasion: **name what it depends on, say which way each value points, and say how to find out which value applies.**

```
  NOVICE                 "It depends."
                                │
  INTERMEDIATE           "It depends on your requirements."        ← names a category, not a variable
                                │
  EXPERT                 "It depends on two things: A and B.
                          If A is ___, do X, because ___.
                          If A is ___, do Y.
                          B only matters if ___.
                          We can find out A by ___ this week.
                          My bet, from what I've seen so far, is X."
```

That last line matters. An expert "it depends" usually ends with a *provisional* call: "I'd bet X, and here is what would tell us otherwise." You have conditioned the answer on the variables. You have not avoided giving one.

Here it is applied to a question clients really do ask: *"Should we add a Redis cache in front of the product catalogue?"*

| It depends on… | If… | Then… | Because (mechanism) |
|---|---|---|---|
| **Read/write ratio** of the data | Reads vastly outnumber writes | Caching is a candidate | Each hit saves a round-trip; each write costs an invalidation |
| | Writes are frequent | Probably not | Invalidation traffic and staleness cancel out the gains |
| **Where the latency actually is** | The DB query is the slow part and can't be tuned further | Cache helps | You skip the slow part |
| | The query is slow because of a missing index or an N+1 | Fix the query first | A cache hides a defect that will come back on the next cache miss |
| **Staleness tolerance** | Seconds of staleness are fine | Simple TTL cache | No invalidation logic needed |
| | Must be fresh (prices, stock) | Cache with explicit invalidation, or don't cache | Stale price = wrong charge |
| **Operational capacity** | Team can run and monitor Redis | Distributed cache is viable | It's another stateful dependency with its own failure modes |
| | It can't | In-memory `HybridCache`/`IMemoryCache` per instance first | Gets most of the win with no new infrastructure |

And the answer that goes with the table: *"It depends mostly on where the latency is. My bet is the catalogue query itself; I saw an N+1 in the product-list endpoint last week. Give me a day to profile it. If the query is the problem, we fix it and probably don't need a cache. If the query is already tight and reads dominate, an in-process cache with a short TTL gets most of the win before we add Redis."*

This is a small version of what [Chapter 39](#chapter-39-discovery-and-diagnosis) does at the scale of a whole system: turning "it depends" into a short list of variables and a cheap way to measure each one.

> **Best practice.** Keep it to *two or three* variables. If you list seven, you are reciting the whole problem space, which is the neutral trade-off list again with extra steps. Part of the expertise is knowing which two variables actually decide the answer in most cases.

> **Gotcha.** Sometimes the honest variable is political, not technical: "It depends on whether the platform team will support Redis in production." Say so. It's still a real variable, and naming it early saves a month of technical work on an option that was never going to be allowed.

## What You Know, What You Believe, and What You'd Need to Test

Advisors lose credibility less often by being wrong than by being wrong **at the wrong level of confidence**, presenting a guess as a fact. The discipline that prevents it is simple to describe and hard to keep up: every claim you make to a client sits in one of three buckets, and you say which.

| Bucket | What it means | How you phrase it | Example |
|---|---|---|---|
| **Know** | Verified: measured on this system, confirmed in source or official documentation, reproduced | Plain statement, with the source | "The order-list endpoint issues [N] queries per request. Here's the SQL log from staging." |
| **Believe** | Reasoned from mechanism and experience, not yet verified *here* | "I think… because…", ideally with a probability | "I think most of the p95 is those queries, because the endpoint does little else. Call it 80%." |
| **Need to test** | Plausible, but you don't have the information to lean either way | "I don't know yet. Here's how we'd find out, and what it costs" | "Whether batching fixes it depends on how the ORM translates the include. A one-day spike tells us." |

The mechanism behind why this works: a client can't audit most of what you say, so they audit the *few* things they can check and generalise from those. If one confident "know" claim turns out to be a "believe", everything else you said gets marked down, including the things you really did know. If you flagged it as a belief with 80% and it turned out wrong, you were right about your uncertainty, and your credibility holds. Labelling protects your credibility where it is most exposed.

> **Pitfall.** "Know" claims borrowed from memory of an older version. .NET changes a lot from release to release: a default changes, an API is obsoleted, the performance characteristics of something you "know" shift completely. "EF Core can't do X" might have been true two majors ago. Unless you checked it against the current version, a remembered platform fact is a *belief*. Treat it as one.

> **Best practice.** In written recommendations, make the buckets visible. A findings table with a column for evidence ("measured", "inferred", "to verify") costs one column and tells the reader exactly how much weight each row can take. [Chapter 41](#chapter-41-recommendations-proposals-and-estimates) builds this into the proposal format.

## Calibration: Keeping Score on Yourself

**Calibration** is how well your stated confidence matches your actual hit rate. If you're calibrated, the things you call 80% likely happen about 80% of the time. That is different from being *right*. An advisor who says "50/50" about everything is never embarrassed and never useful. An advisor who says "certain" about everything is useful until the first miss, and after that the client can't tell which of their "certain"s to believe.

Philip Tetlock and Dan Gardner's *Superforecasting* is the accessible account of the research here. It describes the Good Judgment Project's forecasting tournaments and the habits of the forecasters who did best: breaking questions down, starting from base rates, updating often and in small steps, and above all *keeping score*. The scoring rule it uses is the **Brier score**. For a single yes/no prediction it is the squared difference between the probability you gave and the outcome (1 if it happened, 0 if not), averaged over all your predictions. Lower is better: 0 is perfect, and always saying 50% scores 0.25. The mechanism matters more than the formula. The squared penalty punishes confident misses far more than hedged ones, while hedging everything to 50% caps how good your score can get. The only way to score well is to be confident when you should be and uncertain when you should be.

You don't need a tournament. You need a **prediction log**: a private file where you write down engineering predictions as you make them, with a probability and a date to check.

```
**Prediction log — [YYYY]**

| # | Date       | Prediction                                                    | P    | Resolve by  | Outcome | Notes / what I learned |
|---|------------|---------------------------------------------------------------|------|-------------|---------|------------------------|
| 1 | [YYYY-MM-DD] | The .NET 10 upgrade of [service] lands within the 2-sprint estimate | 0.70 | [date] |        |                        |
| 2 | [YYYY-MM-DD] | Lock contention, not CPU, explains the p99 spikes on [endpoint]       | 0.80 | [date] |        |                        |
| 3 | [YYYY-MM-DD] | Moving [query] to Dapper cuts its p95 by at least half                | 0.40 | [date] |        |                        |
| 4 | [YYYY-MM-DD] | The client picks option B in the architecture review                  | 0.60 | [date] |        |                        |
```

The rules that make it work:

- **Predictions must resolve.** "The migration will go well" can't be scored. "The migration finishes by [date] with no rollback" can.
- **Write the probability *before* you know the outcome.** Hindsight will otherwise recalibrate you for free, and wrongly.
- **Include the ones you'd rather not.** Estimates, diagnoses, "the client will accept this", "this library will still be maintained in a year". These are exactly the calls clients are paying for.
- **Review every quarter.** Bucket the predictions by stated probability (say 50–60%, 60–70%, and so on up to 90–100%) and compare each bucket's hit rate with its stated confidence. You need a few dozen predictions before the buckets mean much, so read early results as a direction, not a measurement.

What the review usually shows is a **pattern**, not a verdict. Engineers are often overconfident in particular categories: estimates, "this is definitely the root cause", "the vendor will fix that bug soon". They are often underconfident in others. The pattern tells you which of your "80%"s to quietly treat as "60%" when advising, until your record says otherwise.

> **Gotcha.** Logging only the predictions you're sure about makes your calibration look perfect and tells you nothing. And an advisor who stops committing in front of clients to protect a score has missed the point of keeping one.

> **The portfolio rule, again.** The prediction log is **private**: it names clients, colleagues and internal dates. What can go public, in anonymised form, is the *method* and the *aggregate*: "I keep a prediction log; last year my estimates were systematically optimistic in [category], and here is what I changed". That is strong evidence of judgment in an interview ([Chapter 37](#chapter-37-the-story-bank-evidence-portfolio)).

## Updating in Public Without Losing Credibility

Sooner or later you'll have to reverse a position in front of the people who heard you state it. Many engineers dread this more than being wrong in private, and so they defend the position past the point where the evidence supports it. That is the most expensive way to handle it. Clients don't remember that you were wrong. They remember **how you behaved** once the evidence turned.

Here is the mechanism. A reversal is evidence *about your process*. Done well, it proves the thing clients most need to believe about an advisor: that your positions follow the evidence, so your *current* position is worth something. Done badly, whether hidden, blamed on someone else, or dragged out, it proves the opposite, and it makes the client wonder what else you are defending for the sake of your ego.

A reversal done well has four parts, and they mirror the anatomy of the opinion itself:

```
**What I said**
The original position, stated fairly — not a weakened version of it.

**What changed**
The specific new evidence. Ideally, it's the "I'd change my mind if…" condition firing.

**What I now recommend**
The new position, with its mechanism.

**What doesn't change**
The parts of the earlier advice that still stand, and the cost of the switch.
```

An example, in the voice you'd use in the team channel:

> *Two weeks ago I recommended we keep the catalogue reads on EF Core and tune them. I said I'd change my mind if profiling showed EF overhead, not the query plan, dominating the endpoint. The profile from Tuesday's load test shows that: the plan is fine and most of the time is materialisation of the wide projection. So I now recommend moving that one query to Dapper; I've put a PR up. Everything else stays on EF, and the tuning we already did still applies to the other endpoints.*

Notice what it does *not* contain: no apology spiral, no hedge about how the first call was "sort of right", and no hint that someone else's data misled you. It is short. It points back to the clause you wrote in advance, which turns the reversal from "I was wrong" into "the process worked". This is the payoff for writing the clause down: **a pre-registered mind-changer turns a climb-down into a demonstration.**

The distinction to protect is between **updating on evidence** and **caving to pressure**. Both change your stated position. Only one should.

| | Updating on evidence | Caving to pressure |
|---|---|---|
| Trigger | New data, a mechanism you missed, the pre-written condition firing | A senior person disagreeing, a deadline, fatigue |
| What you can say | "This changed my mind: [evidence]" | "OK, let's do it your way" (with no new reason) |
| Effect on credibility | Goes up: your positions track reality | Goes down: your positions track the org chart |
| What to do instead of caving | — | "I still think X, for reason M. It's your call, and I'll help make Y work. Can we write down what we'd watch for?" |

That last cell is the professional move when the client decides against your advice: *disagree and commit*, with the disagreement **written down** where it can be seen, for example in an ADR's alternatives section ([Chapter 16](#chapter-16-working-like-a-middle-developer)). You are not trying to win later. You are making sure that if the risk you named materialises, the team recognises it early, because someone already described what it would look like.

> **Pitfall.** Revising the canon quietly. If a published position changes, the change is a *new dated entry*, with the old version kept and marked superseded, just as an ADR is never edited to reverse it but superseded by a new one. A canon with no revision history in it looks like it has never been tested.

> **Gotcha.** The contractor's version of this has a commercial twist. If your earlier advice created billable work for your firm and the new evidence says that work isn't needed, say so. It will cost your employer hours this month. It is also the most powerful trust signal you can send to the client, because it is visibly against your own interest. That is the self-orientation denominator from the start of the chapter, in action. Tell your account manager first, so they don't hear about it from the client.

## Exercises

These exercises don't have a compiler, which is why they're here. Each one practises a move from this chapter on a realistic artefact or situation.

### Find the bug: the canon entry

A colleague shares the first entry of their opinion canon and asks for review:

```
**Position**
Microservices are the modern way to build scalable .NET systems and should be
the default for any serious project.

**Mechanism**
Industry leaders like Netflix and Amazon use microservices, and it is widely
considered best practice for cloud-native development.

**Holds when**
Always, for production systems.

**Counter-case**
Some people argue monoliths are simpler, but this is an outdated view.

**I'd change my mind if**
Someone showed me a better architecture.
```

<details>
<summary>What's wrong with it</summary>

Every one of the five parts fails, each in its own way:

- **Position.** It is not specific enough to act on ("serious project" is undefined), and it states a belief about the industry instead of a recommendation for a situation. A competent engineer *could* disagree, which is good, but the claim is too vague to test.
- **Mechanism.** An appeal to authority ("Netflix does it") and to consensus ("widely considered"). It says nothing about *why* services produce scalability: independent scaling of components with different load, independent deployment for independent teams. Without that, the author can't tell that the mechanism needs conditions (many teams, divergent scaling needs) that most "serious projects" don't have.
- **Holds when.** "Always" isn't a context. A universal claim can be knocked down by a single counterexample, and there are plenty.
- **Counter-case.** It is a strawman ("simpler"), dismissed by labelling it ("outdated") instead of engaging with it. The real counter-case is the network tax plus the distributed-data costs paid on every request by a team that doesn't need independent deployment. It isn't mentioned.
- **I'd change my mind if.** It can't be falsified. "A better architecture" can always be rejected as not better. An observable condition would look like "…if we find deploy coordination isn't a bottleneck and the modules share a scaling profile".

The underlying bug: **the position came first and the justification was filled in afterwards**. Start again from the mechanism and see what position it supports; for most teams, that is Canon #1.
</details>

### Find the bug: the reply to the client

The client's CTO asks in a shared channel: *"We're starting the new booking service next sprint. Postgres or Cosmos DB? We need to decide by Friday."* A contractor replies:

```
Great question! Both are excellent choices and it really depends on your use case.
Postgres is a mature relational database with strong consistency, rich SQL, and a
great EF Core provider. Cosmos DB is a globally distributed NoSQL database with
elastic scale and multiple consistency levels, though it can get expensive. There
are lots of factors to consider such as scalability, cost, team skills, data model,
consistency requirements and future growth. Happy to discuss further on a call!
```

<details>
<summary>What's wrong with it</summary>

Everything in it is true, and none of it helps. The specific defects:

- **No recommendation.** The CTO has a Friday deadline and asked an either/or question. The reply hands back a neutral trade-off list and schedules a meeting. The work of deciding has been passed back to the person who asked.
- **"It depends" with six unnamed variables.** "Scalability, cost, team skills, data model, consistency requirements and future growth" covers the whole problem space. An expert picks the two that decide it here and says which way they point.
- **No knowledge/belief separation.** "Can get expensive" is neither a measurement nor a stated belief with a reason. It's a vague warning.
- **No provisional call, no way to find out.** Nothing says what the contractor would bet on, or what would settle the question before Friday.
- **Self-orientation leak.** "Happy to discuss further on a call!" turns a written question with a deadline into a meeting. It isn't malicious, but it reads as protecting yourself instead of helping.

A stronger reply, recommendation first:

> *Postgres, unless one of two things is true. A booking service is relational at its core (slots, reservations, a hard no-double-booking constraint), and Postgres enforces that constraint in one transaction; in Cosmos you'd be building it yourself with partition design and optimistic concurrency. The team already runs EF Core against Postgres, so there's no new operational skill to learn. The two things that would change my answer: (1) if bookings must be written in multiple regions with low latency (not just read), Cosmos's multi-region writes become the deciding factor; (2) if the access pattern is mostly single-document lookups by a natural key at very high volume. From the spec I think (1) isn't a requirement, maybe 80% sure. Can someone confirm the multi-region-write question by Wednesday? If it's "no", I'd go ahead with Postgres.*

It names the two variables, gives the mechanism, makes a provisional call with a stated confidence, and turns the remaining uncertainty into one question with a deadline.
</details>

### What would you do: the decision that's already made

You're three weeks into an outstaffing engagement. In a planning meeting, the client's head of engineering announces that the team will split the monolith into eight microservices over the next two quarters. The slide deck is ready and the vendor's account manager is visibly pleased: it means more headcount. From what you've seen, the team has no distributed tracing, one person who understands the deployment pipeline, and module boundaries that change every sprint. Your canon says observability before microservices, and modular monolith first. Someone turns to you: "You've done this before. Anything we should watch out for?"

<details>
<summary>How a senior engineer reasons about it</summary>

Three forces are pulling here. There is the evidence, which says the plan is premature. There is social pressure, since the decision was announced, not proposed. And there is commercial interest, since your employer benefits from the plan. That third force is exactly why your answer matters: if you nod along, you are showing high self-orientation in front of the client's leadership.

What doesn't work: a public "this is a mistake" three weeks in. You don't have the diagnosis yet ([Chapter 39](#chapter-39-discovery-and-diagnosis)). You'd be applying a canon position before checking whether its context holds. And a head of engineering who is contradicted in their own planning meeting has every reason to dig in.

What does work: answer the question you were actually asked ("anything to watch out for?") with *conditions*, not a verdict, and make the conditions checkable:

- "Two things I'd want in place before the first extraction: request tracing across service boundaries, so we can debug the first incident, and a clear owner for the pipeline, because eight services means eight deploy paths. Both are cheap now and expensive afterwards."
- "And I'd pick the first service by how stable its boundary is. If a module's interface has changed in each of the last few sprints, extracting it turns every one of those changes into a cross-service contract change."

Then, in a smaller conversation that week, give your actual position with its mechanism, framed around the client's goal (they want independent delivery and scale), and propose a cheap test: "Let's extract one service first, with tracing in place, and measure what it costs us. If it goes smoothly, the plan stands and we've de-risked it. If not, we've learned that for the price of one service, not eight."

Log it as a prediction ("the first extraction will take longer than planned, 70%"). Tell your account manager what you told the client and why, before they hear it elsewhere. If the client goes ahead anyway, make sure the risks you named are written into the ADR's consequences, and then help make it work. That is disagree-and-commit, with the disagreement on record.
</details>

### Go check

- **Write five canon entries** in the five-part template, in your own portfolio repo. Start with the positions you state most often at work. For each one, time how long it takes to write the "I'd change my mind if" clause. Where you can't write one, you've found a position that is holding you.
- **Find where you disagree with this book.** Go through the eight canon entries above and find the one you'd argue with hardest. Write your version, mechanism first. If you can't produce a mechanism for your disagreement, that tells you something too.
- **Draft your first radar edition**: 15–25 blips across four quadrants, each marked as used in production, lab only, or reading. Count how many Adopt items you have actually shipped. Move the rest.
- **Start a prediction log today** with three predictions about current work: an estimate, a diagnosis and a decision outcome. Put a calendar reminder at the resolve date. Review your calibration after the first quarter.
- **Audit your last five written technical answers** (PR comments, Slack replies, emails to a client). For each one: did the first sentence contain a recommendation? Did you name what "it depends" on? Did you mark what you knew versus believed? Count, don't judge, then pick one habit to change.
- **Check for the drifted maxim in yourself.** Think of the last technical position you gave up in a meeting. Was it new evidence, or the seniority of the person disagreeing? Write down which, privately and honestly.

## Sources & Further Reading

- David H. Maister, Charles H. Green and Robert M. Galford, *The Trusted Advisor* — the trust equation (credibility, reliability, intimacy over self-orientation) and the advisor's role compared with the expert's.
- Gerald M. Weinberg, *The Secrets of Consulting: A Guide to Giving and Getting Advice Successfully* — among much else, the "Rule of Three": if you can't think of three things that might go wrong with your plans, there's something wrong with your thinking. A good check on any canon entry's counter-case.
- Gregor Hohpe, *The Software Architect Elevator* — moving between the engine room and the boardroom, and why architects sell options and decisions instead of diagrams.
- Philip E. Tetlock and Dan Gardner, *Superforecasting: The Art and Science of Prediction* — calibration, the Brier score, and the habits of well-calibrated forecasters.
- Philip E. Tetlock, *Expert Political Judgment: How Good Is It? How Can We Know?* — the foxes-and-hedgehogs finding referred to above.
- Paul Saffo, "Strong Opinions, Weakly Held" (saffo.com, July 2008) — the original, process-oriented statement of the maxim.
- Cedric Chin, "'Strong Opinions, Weakly Held' Doesn't Work That Well" (commoncog.com) — the critique of how the maxim is used in practice.
- Thoughtworks, *Technology Radar* and "Build Your Own Technology Radar" (thoughtworks.com/radar) — the Adopt/Trial/Assess/Hold rings and the quadrant format.
- .NET support dates in Canon #4: `dotnet/core` `releases.md` (github.com/dotnet/core), checked September 2026. See also [the appendix](#appendix-net-version-comparison-cheat-sheet).
- Within this book: [Chapter 16](#chapter-16-working-like-a-middle-developer) for communication and ADRs, [Chapter 36](#chapter-36-senior-behaviours-career-and-interviews) for influence; [Chapter 37](#chapter-37-the-story-bank-evidence-portfolio) for turning positions and predictions into interview evidence; [Chapters 39](#chapter-39-discovery-and-diagnosis)–[43](#chapter-43-positioning-and-public-proof) for the rest of the advisory practice.
