# Chapter 61: Discovery and Diagnosis

_⏱️ Estimated read time: ~55 min_

A client says "we want microservices." A middle engineer hears a specification and starts sketching service boundaries. A senior engineer hears a *symptom*, and asks what hurts. Six weeks later the first engineer is splitting a monolith that was never the problem, and the second has found out that three teams share a single release train, that every deploy needs a Friday change board, and that what the client wanted all along was to ship on a Tuesday without asking permission. That can be fixed with a pipeline and a module boundary. A network doesn't have to come into it.

This chapter is about the work that comes before advice: finding out what the problem actually is. [Chapter 60](#chapter-60-having-a-point-of-view) argued that clients hire an expert for a point of view. This chapter is about earning the right to give one. A point of view given before diagnosis is just an opinion, and clients can tell the difference. What the doctor does before prescribing, the consultant has to do too, for the same reason: a correct treatment for the wrong disease is still malpractice.

```
   the request            discovery                    diagnosis              advice
 "we want X"   ──►  questions, data, system   ──►  problem statement  ──►  options + a
                    reading, stakeholders          (one page, agreed)      recommendation
                          ▲                               │                (Ch 63)
                          └──── "that's not quite it" ────┘
```

The loop in the middle matters. A diagnosis that the client has not recognised as their own is not finished. The rest of this chapter is the toolkit for getting round that loop quickly and without making the client feel interrogated.

## The Request Is Not the Need

Clients arrive with solutions because solutions are what they can name. Someone who feels slow delivery, rising cloud bills and nervous releases has no word for that combination. They do have words for what they read about at a conference, so the pain arrives already dressed as a solution. Consultants call this the **presenting problem**: the thing the client brings to the first meeting, which is real but usually one step removed from what needs to change.

The mechanism is worth understanding, because it tells you where to look. A request is a *hypothesis* the client has already formed about their own situation, filtered through three things:

1. **What they can see.** A VP sees missed dates, not a test suite that takes forty minutes.
2. **What they can fund.** "Rewrite" gets a budget line and "stop doing three things badly" doesn't, so requests drift toward whatever is fundable.
3. **What is safe to say.** "Our platform team and our product team don't talk" is politically expensive. "We need an API gateway" costs nothing.

The table below lists common requests and the needs that tend to sit under them. It gives you hypotheses to test, not answers.

| The request | What it often means underneath | The question that tells them apart |
|---|---|---|
| "We want microservices" | Deploy coupling: one change needs everyone's release. Or team contention over one codebase. Or a scaling hot spot in one module. | "Walk me through the last time a small change took too long to reach production. Where did it wait?" |
| "Make it faster" | One slow report, one slow page, or one slow batch job. Or perceived slowness from a UI that blocks. | "Which screen, for whom, and when did they last complain?" |
| "Move us to the cloud" | A data-centre contract ending, a hardware refresh, an acquirer's requirement, or a cost story someone promised the board. | "What date is driving this, and what happens on that date?" |
| "Upgrade to .NET 10" | An auditor or customer questionnaire flagging an unsupported runtime ([Chapter 30](#the-eol-treadmill-legacy-is-a-verb)). Or hiring pain. Or a genuine performance need. | "Who asked for this, and what did they say?" |
| "We need better code quality" | Too many production incidents, or one bad incident with an executive watching. Or onboarding that takes months. | "What happened recently that made this urgent now?" |

> **Best practice.** Treat the request with respect and hold it loosely. "That might well be the right answer. Before I agree, let me understand what's pushing you toward it" takes the client seriously and still keeps diagnosis open. Contradicting the request in the first meeting does neither.

> **Pitfall.** Taking the request literally because it is billable. "We want microservices" can be months of work for your firm, and nobody on your side has a reason to question it until, a year later, the system is harder to change and your name is on the design. Trusted advisors are remembered for the cheaper fix they pointed out.

The opposite failure is deciding the client is wrong before you have evidence. Sometimes "we want microservices" really does mean eight autonomous teams with a genuine need to deploy independently. Diagnosis means finding out.

## Preparing for the First Conversation

Most of a first conversation's quality is decided before it starts. Preparation is cheap and visible: a question you could have answered from their website tells them you didn't do the reading.

**Before the call, gather:**

- **What they sent.** The brief, the RFP, the ticket, the email chain. Read it twice. On the second pass mark every noun that is a solution ("Kubernetes", "event-driven", "rewrite") and every sentence that states a problem. If the list of solutions is longer than the list of problems, that is your first finding.
- **What is public.** Their product, customers, recent news (funding, an acquisition, a new regulation), job postings (they list the stack), engineering blog posts.
- **Who will be in the room,** and each person's role. If you only know names, ask the intermediary (see [Working Through an Intermediary](#working-through-an-intermediary-to-the-decision-makers)).
- **What the intermediary knows.** Your account manager has often already had one conversation. Ask what was said, what was promised, and what the client is nervous about.

**Then write down three to five hypotheses.** For example: "the slowness is one report hitting an unindexed table," "the release process is the bottleneck, not the architecture," "this is a new CTO who needs an early win." Writing them down has two uses. It gives your questions direction, and it lets you notice afterwards which ones you dropped. A hypothesis you never wrote down can quietly steer every question you ask.

> **Gotcha.** Hypotheses make you ask better questions, and they also make you hear selectively. Once you believe "it's the database," every sentence sounds like evidence. Give each hypothesis a line saying *what answer would disprove it*, and listen for that answer in particular.

**Send a three-line agenda ahead:** what you want to understand, how long, and what they get afterwards (a written summary within [N] working days).

**Decide what you want to leave with:** the business driver in one sentence, the constraint that will most shape the answer, the name of the person who decides, and permission to look at the system.

## Running the First Conversation

A discovery conversation has a shape. You won't follow it rigidly, but knowing it means you notice when you have skipped a part.

```
 0 ─────── 5 ──────────────────────── 30 ──────────────────── 45 ───── 55 ── 60 min
 │ frame   │   their story             │   dig                 │ play    │ next │
 │ the     │   (open questions,        │   (the question bank, │ back    │ steps│
 │ meeting │    mostly listening)      │    follow the energy) │         │      │
```

**Frame (about five minutes).** Say why you are there, how long it will take, and what happens afterwards. "I'd like to understand the problem before anyone talks about solutions. I'll ask a lot of questions, some of them naive. At the end I'll play back what I heard so you can correct me." That last sentence tells them the meeting has a structure, and it gives them permission to correct you.

**Their story (the largest block).** Open with a question they can't answer in one word: "Tell me how this came up. What happened that made now the time?" Then mostly stay quiet. Their order of events, what they dwell on and what they skip are all data. Take notes in their words, not yours: if they say "the nightly job," don't write "the ETL pipeline."

**Dig.** Now use the [question bank](#the-discovery-question-bank). Follow the energy. When someone's tone changes, or they start saying "honestly…" or glance at a colleague, you are near the real problem. "Say more about that" is the most productive sentence in discovery.

**Play back.** Summarise what you heard in a few sentences and ask what you got wrong. This is [a technique in its own right](#playing-back-your-understanding), covered below.

**Next steps.** Agree what happens next: access to the system, a follow-up with the person who decides, the date of your written summary. Then send the summary on time.

### Listening versus pitching

The strongest pull in a first conversation, especially for someone good at their craft, is to show expertise by proposing solutions. It cuts diagnosis short in three ways. It **anchors** the client: once you've said "you probably need a read replica," the meeting is about read replicas. It **stops them talking**, because people stop explaining once they think you understand, and the important information tends to come late. And it **stakes your credibility on a guess** you may have to retract.

When you catch yourself describing a solution in the first half of the meeting, write it down as a hypothesis and ask a question instead. You can still show expertise without prescribing: **ask the question only an expert would think to ask.** "Is the report slow for every date range, or only at month-end?" demonstrates more competence than a pitch, and the answer is useful.

> **Best practice.** Ask about specific past events, not opinions or hypotheticals. "Would a faster pipeline help?" gets a polite yes. "Tell me about the last release that slipped. What happened on the day?" gets facts. Rob Fitzpatrick's *The Mom Test* makes this argument for product interviews, and it applies just as well to technical discovery: people are poor predictors of their own future behaviour and much better at describing what already happened.

## The Discovery Question Bank

These questions are grouped by area. You won't ask them all in one meeting. Use them as a checklist afterwards, to see which areas you left blank. An empty area is a risk you haven't priced yet.

Each question comes with what you are listening for. The literal answer matters less than what it tells you about how the organisation works.

### Business driver and cost of delay

| Question | What you are listening for |
|---|---|
| "Why now? What changed?" | A trigger event (incident, audit, contract, new executive, competitor). If nothing changed, urgency may be low whatever they say. |
| "What does this problem cost you per week or month?" | Whether anyone has quantified it. "We don't know" is fine. "We lost [customer] over it" is a strong driver. |
| "What happens if we do nothing for six months?" | The real cost of delay. See [the technique below](#what-happens-if-we-do-nothing). |
| "Who is feeling the pain most sharply?" | The person whose problem this really is, often not the person in the meeting. |
| "Is there a date that matters?" | A hard deadline (contract end, regulatory date, runtime end of support) versus a wish. |

### Users

| Question | What you are listening for |
|---|---|
| "Who uses this, and what are they trying to get done?" | Whether the team knows its users or only its tickets. |
| "What do users complain about? Can I see the actual complaints?" | Raw support tickets beat summaries. Summaries filter. |
| "How do users work around the problem today?" | Workarounds (spreadsheets, manual exports, "we run it overnight") show both the pain and what a minimum fix would have to replace. |

### Constraints: budget, deadline, skills, compliance

| Question | What you are listening for |
|---|---|
| "What budget range are we working within?" | Whether one exists. If they won't say, ask for a range between two numbers you name. |
| "What's the deadline, and what's behind it?" | Fixed versus negotiable. A board meeting is movable, a regulator usually isn't. |
| "Who will own and run this after we leave?" | The team's skills decide which solutions are viable. A Kubernetes design handed to a team that has never run a container is a liability. |
| "What compliance or contractual constraints apply?" | GDPR, PCI DSS, SOC 2, data residency, customer contracts that name a cloud or region ([Chapter 28](#chapter-28-compliance-data-privacy-cloud-cost-finops)). These are hard walls. |
| "What's off the table?" | Vendor mandates, "we are a Microsoft shop," a technology the CTO won't accept. Better to hear it now than in the review. |

### Current system

| Question | What you are listening for |
|---|---|
| "Can you draw the system for me, roughly?" | Which boxes they draw first and largest, and which they forget. What they forget is often where the incidents come from. |
| "How does a change get from a developer's machine to production?" | Lead time, manual steps, approvals, and who is on the critical path. |
| "What breaks most often? What woke someone up last?" | The real reliability picture, which is usually different from the architecture diagram. |
| "Which part does nobody want to touch?" | Your first hotspot candidate. Verify it with data ([Chapter 30](#finding-hotspots-churn-complexity)). |

### Failed past attempts

This area is the one most often skipped, and among the most useful. Almost every problem worth hiring a consultant for has been attacked before.

| Question | What you are listening for |
|---|---|
| "Has anyone tried to fix this before? What happened?" | The approach, why it stopped, and who was blamed. The last one tells you what is politically dangerous. |
| "Is anyone from that attempt still here?" | A potential ally, or a potential opponent who will read your proposal as criticism of theirs. |
| "What would make this attempt different?" | If the honest answer is "nothing," the conditions that killed the last attempt are still there, and those conditions are your real problem. |

> **Pitfall.** Proposing, with enthusiasm, the approach that failed last year, to a room that includes the person who led it. You find this out from their silence. Ask about past attempts early, before you have said anything they could hear as criticism.

### Decision makers and politics

| Question | What you are listening for |
|---|---|
| "Who will make the final decision on this?" | A name. "The committee" or "we'll decide together" means you need the next question. |
| "Who else needs to agree, or could stop it?" | Veto holders: security, architecture review boards, finance, a key customer. |
| "Who has an opinion about this that we haven't heard yet?" | Absent stakeholders. Their views will arrive later, less charitably. |
| "What would make this a win for you personally?" | The interests of the person in front of you. They are allowed to have them, and knowing them makes you more useful. |

### What success looks like, in measurable terms

| Question | What you are listening for |
|---|---|
| "Six months from now, how would you know this worked?" | An observable change. "It's better" isn't one. |
| "What number would move?" | p95 latency on [page], deploys per week, incidents per month, hours of manual work, cloud spend. Any of these, with a baseline. |
| "What's that number today?" | Whether a baseline exists. If not, measuring it becomes step one of the engagement. |
| "What would 'good enough' look like, as opposed to perfect?" | The stopping point. Without one, the engagement never ends, or ends with the client feeling short-changed. |

> **Best practice.** Turn every success criterion into *metric, baseline, target, date, measured by*. "Faster reports" becomes "p95 generation time for the month-end [report], from [baseline] to under [target], by [date], measured from the existing [APM tool] traces." If you can't fill in the baseline, you have found the first task. It is also the first evidence you will be able to show later ([Chapter 36](#chapter-36-the-story-bank-evidence-portfolio)).

## Diagnostic Techniques

Questions gather information. These four techniques turn it into a diagnosis. Each has a failure mode, and a senior engineer knows those as well as the techniques.

### The five whys

Ask "why?" of a problem, then of the answer, and keep going until you reach something that can be changed and would prevent the problem. The technique comes from the Toyota Production System, and Taiichi Ohno's book of that name describes it. The number five is a rule of thumb, not a quota.

```
"Month-end report times out"
  why? ─► the query scans 40M rows
    why? ─► it filters on a column with no index
      why? ─► the column was added last year for a new customer tier
        why? ─► schema changes ship without a query review
          why? ─► the DBA role was cut, and nobody owns data performance
                                              ▲
                        a cause you can act on, and it will produce the next slow report too
```

The value is in the last step. Adding the index fixes this report. Only the answer to the fifth why stops the next one. Clients often want only the index, and that is a legitimate choice. Your job is to make sure they are choosing it knowingly.

**How it fails:**

- **It follows one chain.** Real problems usually have several contributing causes. Ask "and what else?" at each level, and you get a tree rather than a line.
- **It stops at a person.** "Why? Because [developer] didn't add the index." That is the end of learning and the start of blame. When the chain reaches a person, ask what made their action reasonable given what they knew. That is the blameless post-mortem rule from [Chapter 17](#blameless-post-mortems), applied before the incident instead of after.
- **It becomes an interrogation.** Five literal "why?"s feel hostile, especially across cultures. Vary it: "What led to that?", "What was going on at the time?"
- **It invents causes.** Each answer is a hypothesis. Check the important ones against data (the query plan, the commit history, the org chart) before you build a recommendation on them.

### What happens if we do nothing?

This is the most useful single question in discovery. It **prices the problem**: if the honest answer is "not much," the engagement may not be worth doing, and saying so builds more trust than taking the money. It **exposes the real deadline**: "our biggest customer renews in March and has complained twice" is a driver, "the architecture will get worse" is not yet. And it makes the client **state the stakes in their own words**, which you can refer back to when they hesitate at the cost of the fix.

Ask it neutrally, as a real question rather than a sales tactic. If the answer is "it limps on and we're fine with that," a trusted advisor accepts it.

> **Gotcha.** "Nothing" is rarely really an option for runtime and dependency upkeep. A system nobody changes still decays ([Chapter 30](#the-eol-treadmill-legacy-is-a-verb)): the runtime reaches end of support, the base image stops getting patches, and the next customer security questionnaire flags it. When a client says "we'll leave it alone," check the end-of-support dates before you agree that doing nothing is free.

### Playing back your understanding

Play back what you heard, in your own words, as a short structured summary, and ask what you got wrong:

> "Let me check I've got this. Month-end reporting takes up to [N] hours and sometimes times out. Finance works around it by exporting to Excel on the 28th. It got worse after the [new customer tier] launch. You'd like it under [target] before the [date] audit. Nobody has looked at it since [person] left. What have I got wrong or missed?"

It works because a misunderstanding corrected in minute fifty costs nothing and one corrected in week six costs a sprint; because being accurately heard builds more trust than any display of expertise; and because corrections reveal priorities ("actually, the audit matters less than the CFO being annoyed") that a direct question wouldn't. Keep their vocabulary: "the nightly job," not "your ETL."

End with a question that invites correction ("what have I got wrong?"), not agreement ("does that sound right?"). People agree out of politeness and correct only when asked. Then repeat it in writing: the summary you send afterwards is the play-back in a form they can forward.

### Separating symptoms from causes

A symptom is what someone observes. A cause is what, if changed, would stop the symptom. Clients report symptoms, often in the vocabulary of a guessed cause ("the database is slow").

This is the same discipline as incident response, applied to a business problem. [Chapter 33's incident cheat-card](#the-incident-cheat-card) puts *symptom* and *root cause* in separate columns for a reason: at 3 a.m. the symptom is "p99 climbs and health checks flap," and the cause is a connection pool exhausted by a missing timeout. The method in [Chapter 17.6](#176-methodical-debugging-problem-solving) carries over unchanged: reproduce, read the actual evidence, form one falsifiable hypothesis, test it, and bisect.

| The client says (symptom, often with a guessed cause) | Ask for the evidence | Candidate causes to test |
|---|---|---|
| "The database is slow" | Which queries, when, and the query plan | Missing index, parameter sniffing, lock contention, an N+1 in one endpoint, an undersized tier |
| "Releases keep breaking things" | The last five failed releases and what broke | No integration tests on one boundary, config drift between environments, a shared database schema |
| "The team is too slow" | Where the last three features spent their time | Review queues, environment contention, unclear requirements, one person as a bottleneck |
| "The cloud bill is out of control" | The cost breakdown by service and tag | One oversized resource, idle non-production environments, egress, log ingestion ([Chapter 28](#chapter-28-compliance-data-privacy-cloud-cost-finops)) |
| "The system is unstable" | Incident list, timestamps, and what changed before each | A deploy pattern, a batch job, a noisy tenant, a dependency with no timeout |

> **Best practice.** Write symptoms and causes in separate columns in your notes, and don't move anything into the cause column without evidence. The discipline feels slow, and it is what stops you from confidently fixing the wrong thing.

## Reading an Existing .NET System in a Day

Talking gets you the story. The system tells you whether the story is true. On day one of access, before you form opinions, build a **first-day map**: a quick, broad survey that turns "we have some tech debt" into specific facts.

The aim is breadth: finding where to spend day two. [Chapter 62](#chapter-62-lab-the-net-health-check) turns this into a full lab with a structured health-check report. What follows is the fast version you can do in a first session.

```
   ┌──────────┐   ┌───────────┐   ┌────────────┐   ┌──────────────┐   ┌────────┐
   │   repo   │──►│ pipelines │──►│ dashboards │──►│ dependencies │──►│  EOL   │
   │ shape,   │   │ lead time,│   │ what's     │   │ packages,    │   │ runtime│
   │ churn    │   │ manual    │   │ measured,  │   │ services,    │   │ images,│
   │ hotspots │   │ steps     │   │ what isn't │   │ vulns        │   │ SDKs   │
   └──────────┘   └───────────┘   └────────────┘   └──────────────┘   └────────┘
```

**The repo.** How many solutions and projects are there, and how do they reference each other? What does `global.json` pin? Are there tests, and do they run? Where is the churn? Churn crossed with complexity gives you hotspots ([Chapter 30](#finding-hotspots-churn-complexity)),, and they usually overlap with "the part nobody wants to touch." When they don't, ask why.

```bash
# Target frameworks across the repo: one line per project
grep -rho '<TargetFrameworks\?>[^<]*' --include='*.csproj' . | sort | uniq -c

# Pinned SDK, if any
cat global.json 2>/dev/null

# Churn: files touched most in the last 12 months (Chapter 30)
git log --since="12 months ago" --name-only --pretty=format: \
  | grep '\.cs$' | sort | uniq -c | sort -rn | head -20

# Known-vulnerable and outdated packages, including transitive ones
dotnet list package --vulnerable --include-transitive
dotnet list package --outdated
```

**The pipelines.** Find the pipeline definitions and read one end to end. How long does a build take, and how long a deploy? Which steps are manual? Where are the approval gates, and who holds them? The gap between "merged" and "in production" is often the real answer to "we want microservices" ([Chapter 12](#chapter-12-devops-cicd)).

**The dashboards.** Ask to see what the team looks at. If nobody looks at anything, that is a finding. What is measured: request rates, error rates, latency percentiles, queue depths? What isn't? Is anything alerting on user-visible symptoms, or only on CPU? ([Chapter 13](#chapter-13-observability).)

**The dependencies.** Databases, queues, caches, third-party APIs, the shared SSO, the one SOAP service nobody can find the owner of. For each one, note whether calls have a timeout and whether anyone would notice if it went down.

**The EOL picture.** Runtime versions against their end-of-support dates ([Appendix B](#appendix-b-net-version-comparison-cheat-sheet) has the table), base images, the database engine version, and any .NET Framework projects. An unsupported runtime is the finding most likely to turn "we'll think about it" into "we need to do this now," because it has a date and an auditor attached ([Chapter 30](#the-eol-treadmill-legacy-is-a-verb)).

Capture it as a one-page map:

```text
**First-day map: [client/system codename]**   date: [date]   access: [what you could and couldn't see]

**Repo**
  solutions/projects: [n] / [n]      target frameworks: [list with counts]
  tests: [exist? run? pass? how long?]
  top hotspots (churn × complexity): [3 files/folders]
  knowledge concentration: [folders with one active author]

**Delivery**
  build time: [n min]   deploy time: [n min]   manual steps: [list]
  merge → production: [typical duration, and where it waits]

**Observability**
  measured: [...]   not measured: [...]   alerts on symptoms? [y/n]

**Dependencies**
  [name] - [sync/async] - [timeout? y/n] - [owner]

**Lifecycle**
  runtimes vs end of support: [...]   base images: [...]   known vulns: [count by severity]

**Matches the client's story?**
  confirms: [...]
  contradicts: [...]
  open questions for day two: [...]
```

The last section is the one that matters. The map exists to test what you were told. When the data contradicts the story ("the database is slow," but the traces show most of the request time in an outbound HTTP call), you have your most valuable finding. Present a contradiction as a question, not a gotcha: "I expected to see the time in the database, and I'm seeing it in the call to [service]. Does that match what you've seen?"

> **Pitfall.** Treating the first-day map as the diagnosis. It shows you where to look. The worst version of this is a thirty-page static-analysis dump handed to a client who asked why month-end is slow. Findings that don't connect to their problem are noise, however correct they are.

> **Gotcha.** Access is itself a finding. If it takes a week to get read access to the repo and nobody can grant access to production logs, the developers probably live with the same friction every day. Write down how long access took and what you had to ask for.

## Stakeholder Mapping

Every engagement has more stakeholders than the people who show up to meetings. A stakeholder map makes them visible so you can plan who to talk to, how often, and about what. [Chapter 17](#tailoring-the-message-to-the-audience) covers how to tailor a message to each audience. This section is about knowing who the audiences are.

The standard tool is the **power/interest grid**, usually attributed to Aubrey Mendelow. Place each stakeholder by how much power they have over the outcome and how much they care about it:

```
            high │  KEEP SATISFIED            │  MANAGE CLOSELY
                 │  CFO (funds it, busy)       │  CTO (sponsor)
                 │  CISO (can veto)            │  Head of Platform
      power      │                             │  (owns what you'll change)
                 ├─────────────────────────────┼──────────────────────────────
                 │  MONITOR                    │  KEEP INFORMED
                 │  other product teams        │  developers on the team
                 │                             │  finance users of the report
            low  │                             │  your account manager
                 └─────────────────────────────┴──────────────────────────────
                            low            interest            high
```

*Manage closely* means regular direct contact, so nothing in the diagnosis surprises them in a meeting. *Keep satisfied* means short, infrequent updates in their terms (cost, risk, dates). These are the people who can stop the work late without having followed any of it. *Keep informed* are the people who know the system and feel the pain: your best source of information, and later your best advocates. Positions move. A CISO jumps to "manage closely" the moment your recommendation sends customer data to another region, so redraw the map as the recommendation takes shape.

Alongside the grid, note each person's role in the decision. The labels vary between sources; these four are enough:

| Role | Who they are | What they need from you |
|---|---|---|
| **Economic buyer** | Controls the budget and signs off | Cost, risk, and what they get, in a page |
| **Sponsor / champion** | Wants this to happen and argues for it internally | Material they can reuse in rooms you aren't in |
| **Veto holder** | Security, architecture board, compliance, a key customer | Their concern addressed early and in writing |
| **Operator** | Will run it after you leave | A design they can actually operate, and a voice in it |

> **Best practice.** For each "manage closely" stakeholder, write one line: what they need to be true for this to count as a success *for them*. When the lines conflict (the CFO wants spend down, the head of platform wants headcount), you have found the political shape of the problem, and your recommendation has to address it.

> **Pitfall.** Leaving the stakeholder map lying around. A document that says "CISO: can veto, low interest, cautious" is harmless in your notes and damaging on a shared screen. Keep it private and describe roles, not personalities.

## Spotting the Unstated Problem

Some problems never get stated because nobody in the room can say them. Most of these are **organisational problems presented as technical ones**, and engineers are well placed to spot them because the technology is where they show up.

Conway's law, from Melvin Conway's 1968 paper "How Do Committees Invent?", says roughly that a system's design ends up mirroring the communication structure of the organisation that builds it. [Chapter 6](#when-to-split-and-conways-law) covers it as an architecture constraint. In discovery you use it the other way round: **read the system's shape to find the organisation's problems.**

Two chatty services that should be one often sit on the boundary between two teams that don't talk. A "shared" library nobody owns often marks a function that was reorganised away. A module with one active author in a year is a person the organisation depends on.

| Stated as a technical problem | Often actually | Signals in the system or the conversation |
|---|---|---|
| "We need microservices so teams can move independently" | Teams lack ownership or authority. A central change board approves everything. | Deploys need a meeting. The pipeline is fine but the calendar isn't. |
| "The codebase is too hard to work in" | Knowledge is concentrated in one or two people who are a bottleneck or have left | Churn by author shows one name on the core. "Ask [person]" comes up in every answer. |
| "We need a new architecture" | Two leaders disagree about direction, and a consultant's recommendation is expected to settle it | Different people describe different goals. Someone asks "which option would you pick?" before you have seen anything. |
| "Our velocity is too low" | Priorities change weekly, and work in progress piles up | Many open branches, many half-finished features, and roadmap churn. |
| "Quality is bad" | Nobody is allowed to say no to a deadline | Incident timing lines up with release dates. Tests are skipped "temporarily." |
| "We need to rewrite it in [new stack]" | Hiring and retention trouble, or a new leader who wants a visible programme | Job postings, recent leadership change, "nobody wants to work on this." |

**What to do when you spot one.** You weren't hired to redesign their org chart, but a technical fix for an organisational problem will fail with your name on it, so don't pretend you didn't see it:

1. **Check it privately, as a question,** with the sponsor: "Every deploy seems to wait for [board] approval, and that looks like where the time goes. Could that change, or is it fixed?"
2. **Describe it as a constraint on the technical options, not a judgement of people:** "With the current approval process, microservices will deploy no faster than the monolith does now." That is a technical statement they can take upstairs.
3. **Put it in the problem statement,** naming the process, not the people.

> **Gotcha.** Sometimes you are hired *because* of the unstated problem: to be the outside voice that says what insiders can't, or to provide cover for a decision already made. The first is a legitimate and valuable role. The second is a trap if you don't notice it. If someone asks "which option would you pick?" before you have looked at anything, ask yourself which of the two you are being hired for.

## The One-Page Problem Statement

Discovery ends with a written problem statement that the client agrees with. It is the most leveraged page in the engagement: every estimate, recommendation and success check afterwards points back to it. [Chapter 63](#chapter-63-recommendations-proposals-and-estimates) builds recommendations on top of it, and a recommendation without one has nothing to stand on.

The mechanism: a problem statement turns a conversation, which everyone remembers differently, into a document that can be corrected, signed off and cited. Where a play-back checks your understanding in the moment, the problem statement fixes it in writing.

```text
**Problem statement: [engagement/codename]**   version [n], [date], agreed by [name, role] on [date]

**Situation** (2-3 sentences, their words where possible)
  What exists today and who depends on it.

**Problem** (symptoms, observed and measured)
  - [symptom] - evidence: [trace/ticket/log/metric] - baseline: [number, date measured]
  - [symptom] - evidence: [...]

**Likely causes** (hypotheses, each with its evidence and confidence)
  - [cause] - supported by: [...] - confidence: [high/med/low] - how we would confirm: [...]

**Why now / cost of delay**
  What happens if nothing changes, and by when. [date, driver]

**Constraints**
  Budget: [range]   Deadline: [date + what drives it]   Team: [who operates it after]
  Compliance/contractual: [...]   Off the table: [...]

**Out of scope**
  [things discussed that this engagement will NOT address]

**Success looks like**
  [metric] from [baseline] to [target] by [date], measured by [source]

**Open questions / risks**
  [what we still don't know, and who can answer]

**Stakeholders**
  Decides: [name]   Must agree: [names]   Operates: [team]
```

Rules that keep it honest:

- **One page.** If it doesn't fit, you haven't finished diagnosing. You have gathered material. The one-page limit forces you to choose what matters.
- **Symptoms have evidence and causes have confidence levels.** A problem statement that states causes as facts is a recommendation in disguise.
- **No solutions.** Not even "consider Redis." The moment a solution appears, the reader stops reading the problem.
- **Out of scope is as important as scope.** It is what protects you and the client when the conversation later drifts to "while you're in there…"
- **It is agreed, not just delivered.** Walk through it live, invite corrections, and revise it. "Agreed by [name] on [date]" is a line you will point to in month three.

> **Best practice.** Keep a blank copy of this template, and one filled in for an invented or open-source system, in your own public portfolio repo. It shows a prospective client or employer how you think before they ever meet you ([Chapter 65](#chapter-65-positioning-and-public-proof)). Real client problem statements are confidential and usually covered by an NDA. Never publish one. If you want to use a real engagement as a case study, anonymise it thoroughly and get the client's written permission first.

## Pushing Back on the Brief

Sometimes diagnosis shows the brief is wrong: the requested solution won't solve the problem, will make it worse, or solves a problem nobody has. Delivering a brief you know is wrong isn't professionalism; it is complicity, and the bill arrives later. But not every disagreement is worth raising ([Chapter 17](#picking-battles-and-influencing-without-authority) covers picking battles), and with a client your relationship is newer and your authority borrowed.

| The brief… | Push back? | How hard |
|---|---|---|
| Is a slightly different solution from the one you'd pick, and both would work | No. Note your preference once, then deliver theirs well. | Mention, then let go |
| Will work but costs noticeably more than an alternative | Yes. Show the alternative with its costs. | Clear recommendation, their call |
| Solves a different problem from the one the evidence shows | Yes, before work starts | Firm, with evidence, in writing |
| Won't work given a constraint they've told you about (skills, budget, compliance) | Yes, as soon as you see it | Firm, in writing, repeated if ignored |
| Creates a security, data-loss or legal risk | Yes, always | Non-negotiable. Escalate if needed. |

**How to do it without losing the client.** Pushing back well usually strengthens the relationship. What loses clients is pushing back publicly, vaguely, or in a way that makes someone look foolish.

1. **Acknowledge what the request gets right.** "Independent deploys for the payments team is the right goal."
2. **Show the evidence, not your opinion.** "Here's what I found: the last [N] releases each waited [N] days for the change board, and the build itself takes [N] minutes."
3. **State the consequence in their terms.** "If we split services and keep the change board, you'll have more deployables waiting in the same queue. You'd pay for the split without getting the independence."
4. **Offer an alternative that serves the underlying need.** "If we make the payments module deployable on its own inside the monolith and agree a lighter approval path for it, you get most of the benefit in [timeframe]. If that proves insufficient, the module boundary is the first step toward a split anyway."
5. **Leave the decision with them, and mean it.** "It's your call. I wanted you to have this before we commit." If they choose the original brief, [disagree and commit](#disagreeing-productively-and-managing-up): deliver it well, keep a written record of the concern, and don't bring it up again unless new evidence appears.

Do it **privately first**: nobody should hear their idea challenged for the first time in front of peers. Do it **early**: pushback in discovery costs a conversation, and after signature it costs a change request. And confirm it **in writing**: a short email with the concern, the alternative and the decision protects everyone.

> **Gotcha.** On a fixed-scope contract, the brief is also the statement of work. "This is the wrong solution" may mean "this contract needs to change," and that is a commercial conversation your firm has to be part of. Raise it with your account manager and the client sponsor together, framed as protecting the client's outcome. Don't unilaterally deliver something other than what was signed.

## Working Through an Intermediary to the Decision Makers

Much outsourcing and outstaffing work runs through an intermediary: an account manager, delivery manager or PM, on your firm's side or the client's. Part of their job is protecting the relationship by controlling who talks to whom. That is reasonable, and it means information reaches you filtered, and your diagnosis reaches the decision maker filtered, if at all.

```
   client decision maker ◄──── the real problem lives here
            │  ▲
            ▼  │   (filtered both ways: softened, summarised, delayed)
   intermediary (account manager / PM)
            │  ▲
            ▼  │
          you   ◄──── the diagnosis lives here
```

**Why intermediaries filter.** Usually not obstruction: they worry that an engineer will alarm the client, promise scope, or criticise the client's team. Address the worry and the filter loosens.

**How to get access:**

1. **Make it safe for them.** Tell them in advance what you want to ask and why: "I'd like thirty minutes with [the CTO] to confirm what success looks like. Here are the four questions. I won't discuss scope or pricing." A specific, bounded request with a stated purpose is far easier to approve than "can I talk to the client?"
2. **Include them.** Invite them to the meeting. They hear what you hear, they can step in on commercial matters, and they stop being a bottleneck because they aren't being bypassed.
3. **Make them look good.** Send your discovery summary through them, or with them in copy, and credit the access they arranged. An intermediary who looks good for bringing you in will bring you in again.
4. **Explain the risk of not having access.** "If I can't confirm the success criteria with the person who'll judge them, we're estimating against a guess. That risk lands on the delivery, and on your account." You are putting your need in terms of their interest.

**What not to do:** go around them. Contacting a client executive without your account manager's knowledge can end your place on the account, however good your insight. If access still doesn't come, write the risk down ("success criteria unconfirmed with [role]; estimate assumes [X]") and send it to the intermediary, so the gap is visible and has an owner.

> **Best practice.** Ask the intermediary the stakeholder questions directly: "Who decides? What are they nervous about? What happened on this account before I joined?" They often know things the client would never say to you, and asking shows you understand the commercial context.

## Remote and Cross-Cultural Discovery

A lot of discovery now happens over video, across time zones, and in English as a second language on one or both sides. Diagnosis still works, but you lose signals and gain risks, and both need planning.

**What you lose remotely** is the hallway conversation, most body language, and the moment when someone important goes quiet. Compensate deliberately:

- **Cameras on for discovery,** where the culture allows. A frown during a play-back is information.
- **Book the one-to-ones a hallway would have given you:** fifteen minutes each with the people who held back in the group call.
- **Ask for written input before the call.** Many people, often the most knowledgeable engineers and often non-native speakers, say more in writing than on a call.
- **Share your screen while you take notes.** People correct misunderstandings they can see.

**Working in a second language.** For many readers of this book English isn't their first language, and often it isn't the client's either. That is normal in international delivery. What reduces the risk:

- **Confirm numbers, dates and names in writing, always.** "Fifteen" and "fifty" sound alike on a bad line. Type them in the chat during the call and repeat them in the summary.
- **Paraphrase instead of saying "yes, understood."** For many speakers "yes" means "I heard you," not "I agree." A paraphrase tests understanding in both directions.
- **Rehearse your key phrases:** the framing sentence, the play-back opener, and a polite way to disagree ("I see it a little differently. Can I show you why?"). Rehearsed phrases free your attention for listening.
- **Use plain words.** Idioms such as "boil the ocean" or "let's table this" travel badly. "Table" means opposite things in British and American English.
- **Let silences run.** Comfortable pause lengths differ between cultures, and the useful answer often comes after the pause.

**Culture also shapes how problems get reported.** In some business cultures criticism is direct. In others "that could be challenging" means "that will not work," and juniors won't contradict a manager in a group call. Erin Meyer's *The Culture Map* is a practical introduction. For discovery, the consequence is that a group video call is the worst place to surface problems in those settings. One-to-ones, written input and questions about past events get past the reticence, and "what's wrong with the architecture?" usually doesn't.

> **Gotcha.** If you only meet the client at the end of their working day, you meet tired people who want the call to end, and you get short, cautious answers. Take at least one early-morning slot, in their time zone, for the conversations that matter.

> **Best practice.** End every remote discovery call with a written summary within a day: what we heard, what we agreed, open questions, next steps, with names and dates. In a remote, second-language engagement that summary *is* the shared memory, so write it as carefully as a contract clause.

## Exercises

These exercises have no compiler to check them against. They exercise judgment, which is the skill this chapter is about. Put your worked answers, your own question bank and your own problem-statement template in your public portfolio repo. Anything drawn from a real client stays private.

### Find the bug: the discovery call

Here is part of a transcript from a first discovery call. The client is [Client], a logistics company. You are the contractor.

```text
CLIENT (CTO): Thanks for joining. So, we want to move our order platform to microservices.
              The board has approved a modernisation budget.
YOU:          Great, that's a good call. We did a similar migration at [previous client]
              and it went well. I'd suggest starting with an API gateway and splitting
              out the order service first, since that's usually the biggest domain.
CLIENT (CTO): Makes sense. How long would that take?
YOU:          For a first service, probably [N] months with a team of four.
              We'd use Kubernetes and a service bus for events.
CLIENT (CTO): Our team hasn't used Kubernetes, but I suppose they can learn.
YOU:          Absolutely, it's pretty standard now. Do you have any questions for me?
CLIENT (CTO): Not really. Oh, one thing: our last attempt at this stalled
              after the platform lead left, so we want to get it right this time.
YOU:          Understood. With the right architecture it'll go much more smoothly.
              I'll send over a proposal by Friday.
```

List everything wrong with this conversation. There are at least six distinct defects.

<details>
<summary>Answer</summary>

1. **Endorsed the solution before any diagnosis.** "Great, that's a good call" commits you to microservices before you know what problem they are meant to solve.
2. **Never asked why now.** A board-approved budget is a trigger, not a driver. What pain made the board approve it?
3. **Pitched from another client's context.** "[Previous client]" had a different problem. Their diagnosis doesn't transfer.
4. **Gave a design and an estimate with no information.** Gateway, Kubernetes, a service bus and "[N] months with four people", all before seeing the system or the team. That estimate will now anchor every later conversation.
5. **Dismissed a team-skills constraint.** "Our team hasn't used Kubernetes" is a major finding about who will operate the result, and "they can learn" skipped past it.
6. **Asked no questions of the client.** "Any questions for me?" turned discovery into a pitch. Nothing about users, the current system, constraints, decision makers or success criteria.
7. **Brushed past the most important sentence.** "Our last attempt stalled after the platform lead left" points to an ownership and knowledge-concentration problem that no architecture fixes. The right response: "Tell me more. What happened, and what would be different this time?"
8. **Skipped from one call to a proposal.** No play-back, no request for system access, no problem statement.

A better version of the second line: "Before I say whether that's the right call, can you tell me what's driving it? What would be different for the business if it were done?"
</details>

### Find the bug: the problem statement

A colleague has drafted this problem statement after two discovery calls:

```text
**Problem statement: order platform**

**Problem**
  The system is a legacy monolith with poor architecture and a lot of technical debt.
  The database is too slow and the code quality is bad.

**Cause**
  The original developers didn't follow SOLID principles or clean architecture.

**Solution**
  Migrate to microservices on Kubernetes with an event-driven architecture,
  starting with the order service.

**Success**
  A modern, scalable, maintainable platform.
```

What is wrong with it, and what would you ask to fix each problem?

<details>
<summary>Answer</summary>

- **The problem is opinion, not observation.** "Poor architecture," "a lot of technical debt," "code quality is bad" are judgements with no evidence. Which symptoms does the business feel? Ask for the incidents, slow pages, missed dates and complaints that led to the engagement.
- **"The database is too slow" has no evidence or baseline.** Which queries, for whom, how slow, measured how? Ask for traces or query plans and record a baseline figure with a date.
- **The cause blames people and isn't falsifiable.** "Didn't follow SOLID" blames absent developers and can't be tested. It also antagonises anyone in the room who wrote the code. Causes should be specific, evidence-backed hypotheses with confidence levels.
- **It contains a solution.** A problem statement with a solution section is a proposal, and it tells the reader you decided before you diagnosed.
- **The success criterion can't be measured.** "Modern, scalable, maintainable" can't be checked. Ask: what number would move, from what baseline, by when, and who judges?
- **Missing sections:** why now and the cost of delay, constraints (budget, deadline, team skills, compliance), out of scope, open questions, stakeholders, and any record that the client agreed to it.
- **It ignores what discovery found.** If this is the client from the previous exercise, the stalled attempt and the departed platform lead aren't mentioned, and they are the most likely reason a second attempt would fail too.
</details>

### What would you do: the account manager's rule

You are a senior .NET contractor placed with [Client] through your firm. Before your first discovery call, your account manager tells you: "The client wants a proposal for moving to Azure Kubernetes Service. Don't ask about budget, and don't question the AKS decision. The CTO chose it personally, and this is a big account for us." In the call, the head of engineering mentions that the team is four developers, nobody has run containers in production, and the main pain is "releases take all weekend."

<details>
<summary>How a senior engineer reasons about it</summary>

Two things are true at once, and they need different handling.

**The account manager's worry is legitimate.** They are protecting a relationship, and "the CTO chose it personally" is real stakeholder data: the economic buyer has a strong opinion and doesn't want it challenged in public.

**The evidence points elsewhere.** "Releases take all weekend" is a delivery-pipeline symptom. AKS doesn't fix a manual release process; it moves it somewhere more complicated. Four developers with no container experience is an operability constraint no proposal can ignore.

So:

1. **Don't challenge AKS in the group call.** Ask diagnostic questions instead: "Walk me through last weekend's release." "Who will operate the platform?"
2. **Take it to the account manager first, privately,** in terms of the account's health: "AKS alone won't fix their main pain, and this team will struggle to run it. If we propose it without addressing that, delivery goes badly and it comes back on us. How do we raise it?"
3. **Frame the proposal around the CTO's goal, not against the CTO's choice.** Automate the release pipeline first (it fixes the weekend problem visibly), containerise as part of that, and keep AKS as the target with an explicit operability plan. That keeps the CTO's decision and makes it succeed. If the evidence says AKS is wrong rather than premature, that is a private conversation with the CTO, arranged with the account manager.
4. **Get a budget range from the account manager** if you may not ask the client. You can't design without one.
5. **Write the risk down** with an owner: "Main stated pain (weekend releases) not addressed by AKS alone. Team has no container operations experience."

What you don't do is quietly deliver a proposal you believe won't work. The account manager's rule covers what you say in the call. It doesn't cover staying silent about a failure you can see coming.
</details>

### Go check

- **Rewrite a request you received recently.** Take the last ticket or brief that arrived as a solution ("add caching," "move to [X]"). Write the underlying problem you think it points to and the three questions that would confirm it. If you can, ask those questions for real.
- **Build your first-day map on a system you know.** Use your current codebase or an open-source .NET project. Run the commands above and fill in the template. Notice what you could not answer, and how long it took to find out who would know.
- **Draw a stakeholder map for your current project.** Place everyone who could stop or change it on the power/interest grid. Is there anyone in "keep satisfied" who hasn't heard from the team in a month? Keep this one private.
- **Practise the play-back.** In your next meeting where someone explains a problem, summarise it back in three sentences and ask "what did I get wrong?" Count how often the correction contains something important.
- **Write a one-page problem statement for a public system.** Pick an open-source .NET project with an active issue tracker, choose a recurring complaint, and write the problem statement from issues, discussions and code. Put it in your portfolio repo. It shows discovery skill without breaking anyone's NDA.

## Sources & Further Reading

- **Peter Block, *Flawless Consulting*.** The standard text on the consulting relationship, including how to handle a client's resistance to a diagnosis.
- **Donald C. Gause and Gerald M. Weinberg, *Are Your Lights On? How to Figure Out What the Problem Really Is*.** A short book on problem definition: the antidote to solving the request instead of the problem.
- **Gerald M. Weinberg, *The Secrets of Consulting*.** Rules of thumb for giving advice and getting it taken.
- **David H. Maister, Charles H. Green and Robert M. Galford, *The Trusted Advisor*.** How trust in advisory relationships is built and lost; the frame for Part XIII.
- **Rob Fitzpatrick, *The Mom Test*.** Asking about past behaviour, not opinions or hypotheticals.
- **Taiichi Ohno, *Toyota Production System*.** The origin of the five whys as a working method.
- **Melvin E. Conway, "How Do Committees Invent?"** (1968). The original paper behind Conway's law.
- **Matthew Skelton and Manuel Pais, *Team Topologies*.** Using Conway's law deliberately, for when the unstated problem is team structure.
- **Erin Meyer, *The Culture Map*.** How cultures differ in communicating, criticising and deciding.
- **Adam Tornhill, *Your Code as a Crime Scene*.** Churn, hotspots and knowledge maps from version control.
- **Within this book:** [Chapter 17](#chapter-17-soft-skills-engineering-practices) (communication, disagreement, post-mortems), [Chapter 30](#chapter-30-working-with-legacy-brownfield-code) (hotspots and EOL), [Chapter 33](#chapter-33-real-world-scenarios-architectural-decisions) (symptom-to-cause under pressure), and the rest of Part XIII: [Chapter 60](#chapter-60-having-a-point-of-view), [Chapter 62](#chapter-62-lab-the-net-health-check), [Chapter 63](#chapter-63-recommendations-proposals-and-estimates), [Chapter 64](#chapter-64-the-advisory-casebook), [Chapter 65](#chapter-65-positioning-and-public-proof).
