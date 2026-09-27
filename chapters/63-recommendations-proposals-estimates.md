# Chapter 63: Recommendations, Proposals and Estimates

_⏱️ Estimated read time: ~50 min_

Clients don't hire a senior contractor for answers they could find themselves. They hire one to turn partial information into a decision they can act on and defend to the people above them. [Chapter 61](#chapter-61-discovery-and-diagnosis) covered finding out what is wrong. This chapter covers what comes after: saying what to do about it, writing it so a busy executive can act, putting a shape and a price on the work, and keeping that shape intact once delivery starts.

These are four skills, but they form one pipeline, and a weak link breaks the rest:

```
 diagnosis ──► options memo ──► decision ──► ADR / decision log
 (Ch 61, 62)   (what to do)     (theirs)     (what was chosen, and why)
                                   │
                                   ▼
                  proposal ──► estimate ──► SOW ──► delivery ──► change requests
                  (the shape)  (the range)  (the     (the work)  (keeping the shape
                                            contract)            honest as it moves)
```

A good recommendation with a vague SOW turns into a dispute. A careful SOW built on a point estimate nobody believed turns into a death march. A perfect estimate for the wrong option wastes the client's money efficiently.

[Chapter 17](#chapter-17-soft-skills-engineering-practices) covered the basics: bottom line up front, trade-offs for a product manager, ADRs, and estimation within a team (17.4). The audience here is a client who pays by the day, can end the engagement, and may have been burned by the last vendor. The habits are the same, but the stakes and the incentives are different.

## 63.1 Why "It Depends" Is Not Advice

Most architecture questions really do depend on circumstances. The mistake is stopping at "it depends", because that hands the uncertainty back to the person who paid you to deal with it.

Uncertainty has a cost whether or not anyone names it. If you don't recommend, the client still decides. They decide with less information than you had, or they decide by default, which usually means doing nothing while the problem compounds. Declining to commit doesn't avoid the risk. It moves the risk onto the person least able to judge it.

There are three honest ways to recommend under uncertainty. There is also a dishonest way that can pass for a fourth:

| Posture | What you say | When it is right |
|---|---|---|
| **Recommend** | "Do B. Here's why, and here's what would change my mind." | More research probably wouldn't change the answer. |
| **Recommend a step** | "Run a two-week spike on X. Its result picks between A and B." | The options differ a lot and one cheap experiment would settle it. |
| **Recommend a reversible start** | "Start with A. It keeps B open, and we'll know more in a month." | You can't settle it cheaply, but one option doesn't close off the others. |
| *Hedge* | "Both have merits; it's really a business decision." | Never, as a conclusion. Every decision is a business decision, and your job is to make this one easier. |

> **Pitfall.** A hedge can look like humility ("I'd hate to push you either way"). The client hears that you don't know. They will go to someone who sounds as if they do, and that person may be worse at the job than you.

Recommending doesn't mean claiming more confidence than you have. State your confidence and the reason for it: "Fairly confident. The three riskiest modules have no System.Web dependencies." [Chapter 60](#chapter-60-having-a-point-of-view) covers where a point of view comes from. This chapter covers how to deliver it.

## 63.2 The Options Memo

The options memo is the advisor's core document: two to four pages in a fixed shape. The shape matters because each part answers a question the decision-maker will ask. Any part you leave out comes up in the meeting, where you'll answer it less well.

1. **The decision.** What is being decided, by whom, by when.
2. **The recommendation.** One sentence, at the top.
3. **Context.** Only what is needed to judge the options: the forcing function, the constraints, and the diagnosis.
4. **Options: two or three real ones, plus "do nothing".** Each described the same way: cost (a range), time, risk, reversibility, and what it lets you do next.
5. **Comparison.** One table, with the same criteria for every option.
6. **Why the recommendation wins.** The two or three reasons that decide it, not a list of every advantage.
7. **What would change the recommendation.** The specific facts that would flip it.
8. **What we need to learn next.** Open questions, how to answer them, who owns each, and by when.
9. **The ask.** What you need from the reader, and by when.

Three rules keep the memo honest:

- **"Do nothing" is always an option, and it's never free.** Its cost is the problem growing: incidents continue, the hiring pool shrinks, a contract auto-renews. Stating that cost is often the most persuasive part of the memo, because the client may never have seen it written down. Sometimes doing nothing is the right answer. An advisor who says so and loses the work earns more trust than one who always finds something to sell.
- **Every option must be one a reasonable person would pick.** One real option next to two strawmen is a sales pitch, and experienced readers notice. If you can't make the strongest case for an option, you don't understand it well enough to reject it.
- **Same criteria, same depth.** If the favoured option gets a cost breakdown and the others get "expensive", the comparison is rigged, even if you didn't mean it to be.

```markdown
**Options memo: [decision in one line]**
Author: [name] · Date: [date] · Decision owner: [name, role] · Decide by: [date]

**Recommendation**
[One sentence: do X.] [One sentence: the main reason.]

**Context**
[Forcing function: why now.] [Constraints: budget, dates, team, compliance.]
[What the diagnosis found: 3–5 bullets, with links to the evidence.]

**Options**
Option 0 — Do nothing: [what that means concretely]
Option A — [name]: [2–3 sentences]
Option B — [name]: [2–3 sentences]

**Comparison**
| | Do nothing | A | B |
| Cost (range) | | | |
| Time to first value | | | |
| Main risks | | | |
| Reversibility | | | |
| Leaves us able to | | | |

**Why [recommended option]**
[2–3 decisive reasons.]

**What would change this recommendation**
- If [fact], then [other option] wins, because [mechanism].

**What we need to learn next**
| Question | How we find out | Owner | By |

**Ask**
[Decision / approval / access needed, from whom, by when.]
```

> **Best practice.** Write "What would change this recommendation" *before* "Why". Doing it first makes you find the assumptions your recommendation depends on, and those are exactly what a sharp reader will test. It also shows that you reached the recommendation from evidence and will change it if the evidence changes.

## 63.3 A Worked Example: The .NET Framework 4.8 Monolith

All names, figures and dates in this example are **illustrative**. They show the reasoning. They are not benchmarks.

**The situation.** A mid-sized distributor's order platform is a .NET Framework 4.8 monolith: ASP.NET MVC 5 customer portal, a Web Forms back office, WCF services used by the warehouse system, EF6, and SQL Server. It runs on two Windows Server VMs in a co-location facility whose contract renews, for several years, in nine months. It has about 400k lines of C#, few tests, and three in-house developers. The CTO asks for "a plan to get us to the cloud and off old .NET".

**The diagnosis** (discovery, as in [Chapter 61](#chapter-61-discovery-and-diagnosis)):

- .NET Framework 4.8.x is still serviced, but it gets no new features, and its support follows the Windows versions it runs on. Check Microsoft's current lifecycle page before you quote a date. The runtime itself isn't urgent. The ecosystem around it is: libraries are dropping Framework targets, and fewer engineers want to work on Web Forms.
- The Web Forms back office is the most tangled part. The MVC portal is fairly clean. Two WCF services use `netTcpBinding`.
- Uploads are written to a local disk path, and a nightly job runs as a Windows Scheduled Task.
- **The business's real problems are release risk (manual deploys, weekends only) and the co-location renewal.** The framework version is not one of them.

The last point decides the recommendation. The CTO asked about the cloud and old .NET. What the business needs is to leave the co-location facility before the renewal and to be able to ship without fear. A plan that answers the question as asked, and misses the real need, gets accepted and later regretted.

**The options.**

- **Option 0: do nothing.** Renew the contract and keep the VMs. There's no migration spend, but the renewal commits the client for years, which makes "do nothing" the *least* reversible option on the table. Release risk stays as it is, and every change gets a little more expensive each year.
- **Option A: move to Azure App Service (Windows) as-is.** App Service runs .NET Framework 4.8 apps on Windows plans. The work is mostly environmental: uploads move to Blob Storage, the scheduled task becomes a WebJob or Function, the `netTcpBinding` services switch to HTTP bindings or move to a VM, the database moves to Azure SQL or SQL Managed Instance, and CI/CD with deployment slots is added. Anything that assumes a full Windows server (COM components, MSMQ, Windows services, local file shares) needs a new home, and finding all of those is the main risk. This option exits the co-location contract and fixes weekend deploys. It does nothing about the framework. [Chapter 50](#chapter-50-azure-in-depth-for-net-developers) covers App Service slots, managed identity and configuration.
- **Option B: strangler fig to .NET 10.** A .NET 10 facade (YARP) sits in front of the monolith, and capabilities move behind it one at a time: portal first, WCF surfaces next, back office last or replaced. The System.Web adapters (`Microsoft.AspNetCore.SystemWebAdapters`) let the old and new apps share session state and authentication while both run. [Chapter 30](#chapter-30-working-with-legacy-brownfield-code) covers the mechanics. Each slice ships on its own, and you can stop at any slice and still have a working system. .NET 10 is an LTS release supported until November 2028.
- **Option C: rewrite on .NET 10.** This is the cleanest target on paper. In practice it means maintaining two systems during the build, rediscovering edge cases as production incidents, delivering no value until cutover, and then a large cutover that's hard to reverse. Chapter 30 explains why big-bang rewrites fail. Flyvbjerg and Gardner's *How Big Things Get Done* shows the same pattern in large projects across many industries.

**Comparison.** Effort is in person-months. The figures are illustrative, from before any discovery narrowed them.

| | 0: Do nothing | A: Move to App Service | B: Strangler to .NET 10 | C: Rewrite |
|---|---|---|---|---|
| Effort | ~0 + renewal | 3–6 | 12–30, spread out | 25–60+, up front |
| First value | — | 2–4 months | 3–5 months | At cutover, 12+ months |
| Main risks | Lock-in; release risk unchanged | Hidden server dependencies; still on Framework | Old/new coupling; slow if under-staffed | Rediscovering scope; running two systems; cutover |
| Reversibility | **Low** (multi-year contract) | High (VMs stay as fallback) | High per slice | Low once committed |
| Leaves us able to | Nothing new | Start B from Azure, with CI/CD in place | Stop at any slice | Only finish |

**Recommendation: A, then B.** Move the monolith to App Service within four months. That exits the co-location contract and fixes deploys. Then strangle it to .NET 10 one slice at a time, starting with the portal, with a review after each slice. This is one recommendation: a sequence with decision points, not a hedge between two options. It wins for three reasons:

1. **It meets the deadline with the cheapest reversible move.** None of the other options gets out of the co-location contract within nine months without taking on the most risk.
2. **A makes B cheaper.** B needs CI/CD, slots, Blob Storage and managed identity anyway. Building them against the running monolith means the strangler starts on proven ground.
3. **Value arrives early and keeps arriving**, which keeps the budget alive through the long part.

**What would change it:**

- *Discovery finds server dependencies App Service can't host.* The first move goes to Azure VMs instead. The sequence stays the same.
- *A SaaS replacement is planned within two years.* Do A only. Spending on B would be modernizing something about to be retired.
- *The co-location contract can go month-to-month.* The deadline disappears, so go straight to B and skip the intermediate hosting move.
- *The back office turns out to be thin CRUD.* Replacing it becomes a small, contained rewrite inside B, not a rewrite of the whole system.

**What we need to learn next** (a two-week paid discovery, 63.11):

| Question | How | Owner | By |
|---|---|---|---|
| What assumes a full Windows server? | Dependency inventory, Upgrade Assistant analysis, a trial deploy to App Service | Advisor + senior dev | Week 1 |
| Can the contract go month-to-month? | Ask the provider | Client ops lead | Week 1 |
| How coupled is auth between the portal and the back office? | Read the code; spike with the System.Web adapters | Advisor | Week 2 |
| Is a SaaS replacement on the roadmap? | Ask the COO | CTO | Week 1 |

Notice what the memo doesn't do. It doesn't pretend the ranges are precise. It doesn't recommend the most interesting technical work. And it doesn't bury the renewal, which is the fact that makes "do nothing" a one-way door.

## 63.4 Writing for Executives

The CTO will read the whole memo. Above the CTO is someone (a CFO, a CEO, a board) who will read one page, and maybe only its first paragraph. A large share of your influence depends on that page.

### Bottom line up front, and why it works

Executives read to decide *whether to get involved*, not to understand everything. The opening tells them whether this is a decision they must make, a risk they must know about, or something to delegate. If the answer is in paragraph four, they have delegated it before reaching it, often to the person least able to decide. A strong first sentence gives the recommendation, the cost, and what happens if they don't act:

> *We recommend moving the order platform to Azure over the next four months (€[X]–[Y]) and modernizing it in stages after that. Renewing the co-location contract instead commits us to [N] more years on a platform that already limits how safely we can release.*

### The pyramid

Barbara Minto's *The Pyramid Principle* gives BLUF a structure you can repeat. A governing thought goes at the top. Beneath it are a few key points that together support it fully, and beneath each of those is its evidence.

```
                 ┌──────────────────────────────────────────┐
                 │ Move to Azure now, modernize in stages    │
                 └──────────────────────────────────────────┘
                     │                 │                  │
      ┌──────────────┴───┐  ┌──────────┴────────┐  ┌──────┴──────────────┐
      │ Avoids multi-year │  │ Cuts release risk │  │ Keeps every later    │
      │ lock-in           │  │ within a quarter  │  │ option open          │
      └──────────────────┘  └───────────────────┘  └──────────────────────┘
        renewal terms         weekend deploys,       each step can stop
                              incident history       with a working system
```

- **Vertical logic.** Each level answers the question raised by the level above it. "Move now" raises "why?", and the three points answer it. "Avoids lock-in" raises "how much?", and the evidence answers that. A box that answers nothing doesn't belong in the pyramid.
- **Horizontal logic: MECE.** The key points should be *mutually exclusive* (they don't overlap) and *collectively exhaustive* (nothing that matters is missing). Overlapping points read as padding. A missing one will be raised by the reader, and you'll be on the defensive.

For the introduction, Minto uses **SCQA**: *Situation*, *Complication*, *Question*, *Answer*. "Our order platform runs on two co-located servers (S). The contract renews in nine months, for [N] years (C). Should we renew or move? (Q) Move, in two stages (A)."

### The one-page executive summary

```markdown
**[Decision] — executive summary**                      [date] · [author]

**Recommendation.** [What, cost range, timeframe — one or two sentences.]
**Why now.** [The forcing function, in business terms.]
**What it buys us.**
- [Business outcome, with a range or before/after]
- [Business outcome]
**What it costs.** [Range, spread over time; internal time needed.]
**Main risks and how we contain them.**
- [Risk in business terms] → [containment]
**Alternatives considered.** [One line each, and why not.]
**Decision needed.** [What, from whom, by when; what happens if no decision.]

Detail: [link to the options memo]
```

> **Best practice.** Read the one-pager aloud as the CFO and cross out every term they'd have to ask about. "App Service", "strangler" and "YARP" belong in the memo. If you can't state the recommendation without jargon, you haven't translated it yet.

### Translating technical risk into business terms

Executives think in three currencies: **money**, **time**, and **risk to the business** (revenue, customers, compliance, reputation). A technical risk that isn't converted into one of them gets treated as zero, because the reader can't weigh it against everything else competing for the budget.

| What you'd tell an engineer | What the executive needs to hear |
|---|---|
| "No automated tests on order intake." | "Any release can break order-taking, and we find out from customers. That happened [N] times last quarter, at about [€ per hour of lost orders × hours]." |
| "We're on .NET Framework 4.8." | "Each year fewer engineers and libraries support this platform, so hiring gets slower and changes get more expensive. It's a slow cost, not a sudden one." |
| "Deploys are manual and weekend-only." | "We can release safely about once a month, which limits how fast we can respond. Each release also costs [N] staff weekends." |
| "Two WCF services use a legacy binding." | Usually nothing. It's an implementation detail inside one option's cost. |

The translation rests on **expected cost**: likelihood × impact, stated as a range. "About one release in [N] causes an order-intake outage of [hours], at roughly €[X] per hour" is something a CFO can weigh against a migration budget. "The code is fragile" is not.

- **Use the client's own numbers.** Revenue per hour, incident history and time-to-hire exist somewhere in their organization. A number they recognize is worth more than ten you estimated.
- **Give ranges, not false precision.** "€40–90k" is honest. "€63,450" suggests precision you don't have, and the first time one such figure turns out wrong, they stop trusting the rest.
- **Don't inflate the downside.** Scaring executives into approving work works once. After that, they discount everything you say.

> **Gotcha.** For many CFOs the real constraint is *when* the cash goes out, not the total. A plan that costs a bit more but spreads the spend across four quarters, with value from the first, can beat a cheaper plan that needs everything up front. Ask how the client budgets (capex versus opex, annual cycles, approval thresholds) before you design the phases. [Chapter 28](#chapter-28-compliance-data-privacy-cloud-cost-finops) covers the cloud-cost side.

## 63.5 Reversibility: Matching Rigor to the Door

Not every decision deserves a memo. In his 2015 letter to Amazon shareholders, Jeff Bezos separated **Type 1** decisions, which are one-way doors (irreversible or nearly so) and deserve slow, careful thought, from **Type 2** decisions, which are two-way doors and should be made quickly by small groups. He warned about getting it wrong in both directions. Treating two-way doors like one-way doors makes an organization slow. Treating one-way doors casually is how it gets trapped.

For an advisor, that becomes a rule for how much analysis to spend on a decision:

| Door | Examples | Rigor | Record |
|---|---|---|---|
| **Two-way, cheap** | A logging library; a strangler's first slice; a feature flag | Decide in the meeting; set a review date | A line in the decision log |
| **Two-way, expensive** | Hosting move; ORM for new code; team structure | Short written comparison; name the rollback path | One-page ADR |
| **One-way** | Multi-year contracts; a rewrite; destructive data-model changes; public API contracts | Full options memo, discovery first, explicit sign-off | Memo + ADR + executive summary |

Reversibility can be designed, and designing it is often the most useful thing an advisor does:

- **Slicing.** A rewrite is one one-way door. A strangler is a series of two-way doors, so each commitment stays small even if the total work is similar.
- **Keeping the fallback running.** During a hosting move, keep the old VMs warm and the DNS switch ready for [N] weeks.
- **Expand/contract data changes** ([Chapter 30](#chapter-30-working-with-legacy-brownfield-code)). Add the new structure, write to both, and remove the old one only once the new one has been proven.
- **Shorter commitments.** Month-to-month at a premium is often worth paying for, because you're buying an option.

> **Pitfall.** "Do nothing" looks like the ultimate two-way door, but it often isn't. Contracts auto-renew, support windows close, key people leave, and data grows until migrating it is no longer simple. Before treating inaction as the safe default, ask which doors will close on their own.

The opposite mistake is just as common: putting a small decision through a heavy process because a memo looks diligent. If the client can undo it next sprint in a day, recommend it in one sentence, log it, and move on.

## 63.6 Presenting a Recommendation and Handling Pushback

Most decisions are made in meetings, and meetings go the way they were prepared.

**Pre-wire it.** Before the group meeting, go through the recommendation one-on-one with the key people: the CTO, the finance contact, and whoever's team will do the work. In private, people raise their real objections. In a group, they defend positions. Each objection you hear beforehand is one you can answer in writing, and nobody hears your recommendation for the first time in a room where disagreeing might embarrass them.

**Know who signs.** In outsourcing, the person you work with every day is often not the one who approves the budget. Find out who does and what they care about before you write the one-pager.

**In a 30-minute decision meeting:** state the decision and your recommendation (2 minutes). Explain why now (3 minutes). Walk through the comparison table and the deciding reasons (10 minutes). Cover the risks and what would change your recommendation, before anyone asks (5 minutes). Leave the rest for discussion. Close by stating what was decided, who owns what, and when you'll send the write-up. If nothing was decided, state what's needed to decide.

Pushback falls into a few recurring types:

| What you hear | What it often is | Response |
|---|---|---|
| "Too expensive." | A budget limit, or doubt about the value | Ask which. For budget, show the cheapest option that still meets the deadline and what it gives up. For value, go back to the cost of doing nothing. |
| "Why not just rewrite it properly?" | A preferred solution, often from someone senior | Put it in the same comparison table, against the same criteria. If it loses, it loses in the open. |
| "The last vendor said three months." | Anchoring | Ask what that estimate included and what happened to it. Don't match a number just because it exists. |
| "Can't it be faster?" | Schedule pressure | Name the real levers: less scope, more people (with the ramp-up cost from Brooks's *The Mythical Man-Month*), or more risk. Ask which one they want. |
| "We need certainty first." | Fear of a one-way door | Offer a staged commitment: fund discovery now and decide the rest at the gate. |
| "Let us think about it." | An unspoken objection, or the decision-maker isn't in the room | "What would need to be true for this to be an easy yes?" Follow up in writing within a day. |

The rule behind the table: **update on evidence, not on pressure.** When pushback brings new information (a constraint, a cost you got wrong, a strategy nobody mentioned), change your recommendation and say so plainly: "That changes my view. Given the SaaS plans, I'd do A only." That isn't losing the argument. It's what your "What would change this" section promised. When pushback is only repetition or seniority, restate your reasoning once, politely, and ask what would change their mind.

> **Best practice.** The client is entitled to decide against you, and they often have reasons you can't see. You've done your job if they decide *knowing* the trade-off, whatever they choose.

**If you're overruled,** disagree once, clearly and in writing, then commit (Chapter 17's *disagree and commit*). Put your effort into making the chosen path succeed, including making it more reversible: slice it, keep the old system able to take traffic, and agree on review checkpoints. The one exception is an ethical or legal line, such as unsafe practices or misleading customers or regulators. That belongs with your firm's leadership. It isn't a design disagreement.

## 63.7 After the Decision: Writing It Down

A decision that isn't written down gets made again six months later, by someone who wasn't there and has less information. For a contractor there's a second reason: the engagement ends, and the client's memory of *why* shouldn't leave with you.

Use the ADR format from [Chapter 17](#chapter-17-soft-skills-engineering-practices) (17.5), and add four things for client work:

- **Who decided, and where.** "Approved by [CTO] at the steering meeting, [date]." Not to assign blame: when circumstances change, the first question is who can reopen the decision.
- **A link to the options memo.** The ADR records the choice, and the memo records the reasoning.
- **Revisit triggers.** Copy in "What would change this recommendation". It defines when reopening the decision is legitimate rather than disruptive.
- **Dissent, if any.** "The back-office team preferred a full rewrite; see [link]." Recording dissent respects it, and it lets people who disagreed raise the question again later without looking like sore losers.

For two-way-door decisions, a one-line **decision log** (date, decision, owner, link) is enough.

> **Gotcha.** Store decision records where the *client* owns them: their repo or wiki, not your firm's tracker. When the contract ends, neither of you can reach the tracker.

## 63.8 Proposals That Survive Contact

A proposal turns an agreed direction into work someone can buy, and it often becomes the basis of the SOW. Write every sentence as if it will be quoted back to you in a dispute. *This section and the next give engineering advice about what proposals and SOWs contain. They are not legal advice. Contract law varies by jurisdiction, and your firm's legal and commercial people own the final wording.*

| Section | What it answers | What goes wrong without it |
|---|---|---|
| **Problem** | What the client is fixing, in their terms | You solve a different problem well |
| **Approach** | How, and why this way (link the memo) | Indistinguishable from a cheaper bid |
| **Phases** | The work in stages, each ending at a gate | One large commitment with no place to stop |
| **Deliverables** | What they receive, each with acceptance criteria | "Done" means whatever they need it to mean |
| **Assumptions** | What must be true for the plan and price to hold | You carry risks you never agreed to |
| **Client dependencies** | What the client provides, and by when | Your schedule quietly depends on theirs |
| **Exclusions** | What is *not* included, by name | Everything adjacent is assumed to be included |
| **Risks** | What could go wrong, who carries it, and how it's contained | Surprises become disputes |
| **Commercial model** | How you're paid, and how change is handled | Every change is negotiated from scratch |

**An assumption is a risk you've handed to the client, but only if it can be checked.** "Assumes a test environment with production-like data by week 2" means a week-6 environment is a documented reason for a change request, not an argument. "Assumes reasonable cooperation" transfers nothing. **Exclusions** work the same way: list what a reasonable reader might assume is included ("Excludes: the Web Forms back office; performance testing beyond section 5; 24/7 support after go-live"). Each one is a conversation held now, cheaply, instead of later, expensively.

```markdown
**Proposal: Order platform — Phase 1, move to Azure**
[Client] · [Your firm] · Version [n] · [date]

**Problem**
Two co-located servers; contract renews [date]; manual weekend releases.
[Link: options memo, ADR-001]

**Approach**
Move the existing application to App Service with minimal code change, add
automated deployment, keep the current servers as fallback for [N] weeks.

**Phases and gates**
P1a Discovery (2 weeks, fixed fee) → gate: go/no-go, confirmed P1b estimate
P1b Migration (est. [range] weeks) → gate: production cutover sign-off
P1c Stabilization ([N] weeks)      → gate: fallback servers decommissioned

**Deliverables and acceptance**
D1 Discovery report — accepted after walkthrough; comments within 5 business days.
D2 Azure environments as IaC — accepted when [named checks] pass and test is
   recreated from code.
D3 Pipeline (build, test, deploy to slot, swap) — accepted after [N] production
   releases through it.
D4 Cutover — accepted when [named business smoke tests] pass and [N] days
   without Sev-1.

**Assumptions** · **Client dependencies** (what / who / by) · **Exclusions**
**Risks** (risk / likelihood / impact / carried by / containment)
**Commercial model and change control** (63.9, 63.12)
```

> **Best practice.** End every phase at a gate where the price of the next phase is confirmed. That makes the proposal easier to buy (the first commitment is small), more honest (each phase is priced using what the last one taught), and safer (either side can stop cleanly).

## 63.9 Commercial Models and Who Carries the Risk

Every commercial model answers one question: **when the work turns out bigger or smaller than expected, who pays?** How both sides behave follows from the answer.

| Model | How it works | Client carries | Vendor carries | Behaviour it encourages |
|---|---|---|---|---|
| **Time and materials (T&M)** | Pay for time worked at agreed rates | Effort risk | Utilization; reputation | Flexibility, but nobody is accountable for the total |
| **T&M with a cap** | T&M up to a not-to-exceed amount | Effort up to the cap | Overrun beyond it (per terms) | Shared discipline |
| **Fixed price** | One price for defined scope | Scope fit: they get what was written, not necessarily what they need | Effort risk, priced in as a premium | The vendor defends scope, and change requests become the battleground |
| **Retainer** | Monthly fee for defined capacity or availability | Paying for unused capacity | Demand beyond the retained capacity | An ongoing relationship; suits advisory and support work |
| **Value-based** | Price tied to the value delivered, not the effort | Paying more when the value is high | Effort, *and* whether the value is realized | Focus on outcomes; needs measurable value and trust |

**Fixing the price doesn't remove the uncertainty. It moves it to the vendor, who charges for carrying it.** A well-run vendor estimates the work and adds a margin that grows with the uncertainty. A client who demands a fixed price before any discovery is buying insurance at its most expensive. The fair thing to say is: "We can fix a price now, but it will carry a large risk margin. Fund two weeks of discovery first and the build price will be lower, because we'll be pricing less uncertainty."

Each model also shapes behaviour. Under fixed price, every ambiguity is money: the vendor wants the narrow reading and the client wants the broad one. That's why fixed price needs the tightest SOW (63.10) and the most formal change control (63.12). Under T&M, the risk runs the other way: nobody on the vendor side owns the total. Good T&M relationships make up for it with forecasts, burn-down reporting, and an advisor who treats the client's budget as finite.

**Value-based pricing** (Alan Weiss's *Value-Based Fees*; Blair Enns's *Pricing Creativity*) suits advisory work with a clear, measurable outcome and a buyer who can judge it. It rarely suits outstaffed delivery, where you're capacity inside someone else's plan.

**If you work through an outsourcing or outstaffing firm,** the account manager and the client negotiate the model, and you're usually billed T&M within it. You still have influence:

- **Your estimates become the price.** Under fixed price, your range and assumptions set how much risk your firm takes on. Give them honestly and in writing.
- **You're often the first to spot out-of-scope work.** How you raise it (63.12) decides whether it becomes a paid change or unpaid work nobody sees.
- **Commercial conversations go through your firm.** When a client asks you for a price, a discount, or "a small extra, no need to mention it", the answer is friendly and always the same: "Good idea. Let me bring [account manager] in so we do it properly." Negotiating directly can commit your firm to things it hasn't agreed.

> **Pitfall.** Quietly absorbing out-of-scope work feels generous. It teaches the client that scope is negotiable, costs your firm margin, and leaves no record to justify the next change request. If you want to do the favour, make it visible: "Happy to include this. I'll log it as a goodwill change so we both know it's outside the SOW."

## 63.10 Scope Traps in Statements of Work

Some SOW phrases look harmless but carry large amounts of unpriced work. (As before: engineering advice about content, not legal advice.)

| Phrase | What the client can reasonably read into it | Rewrite |
|---|---|---|
| "Accepted upon client satisfaction." / No acceptance section | Done is whenever they say so | Acceptance criteria for each deliverable; an [N]-business-day acceptance window; severity definitions, where only [named severities] block acceptance; deemed accepted if no written defects arrive in the window |
| "...including integrations with existing systems." | Every system, in every direction | A named list: system, direction, protocol, data, who provides the sandbox and documentation. Anything else goes through change requests |
| "Migrate existing data." | All of it, with full history, cleaned, reconciled, no downtime | Named entities, volumes and history depth; who owns cleansing; reconciliation method and tolerance; number of rehearsals; cutover window |
| "Feature parity with the current system." | Every behaviour, including the undocumented bugs someone relies on | A named feature list from discovery; anything off the list is excluded or priced as a change |
| "Secure, fast, scalable, highly available." | Whatever the worst day needs | Numbers: p95 latency at [N] RPS on named operations; an availability target and how it's measured; the security standard, who runs the pen test, and whether fixing its findings is in scope |
| "Support during go-live." | Around the clock, indefinitely | Duration, hours, response times by severity, and the support channel |
| "Client will provide access as needed." | Your schedule depends on theirs, invisibly | A dependency table with dates, and what happens when a date slips |

**Data migration is where estimates fail most quietly.** Moving the data is the small part. The large parts are finding out what the data really contains (as opposed to what the schema says), deciding what to do with records that don't fit the new model, reconciling counts and totals until the business trusts the result, rehearsing until the cutover fits its window, and handling changes made during the migration. Each of those needs a business owner, and "migrate the data" assigns none.

**Hidden non-functional requirements** are the other trap: the client expects them without writing them down, because to them they're obvious. Ask before signing:

- **Performance:** what load, on which operations, and what's the current baseline? "No slower than today" is a real requirement.
- **Availability and recovery:** what does an hour of downtime cost? A day? What are the RPO and RTO?
- **Security and compliance:** which standards apply? Who pen-tests, and who fixes what they find? Where may personal data live ([Chapter 28](#chapter-28-compliance-data-privacy-cloud-cost-finops))?
- **Operability:** who runs the system after handover, and what monitoring and runbooks do they need?
- **Accessibility, localization, browsers and devices, audit logging and retention.**

Each answer goes into the SOW as a measurable criterion or into the exclusions. The dangerous NFR is the one that ends up in neither.

> **Best practice.** Read the SOW once as the client's most demanding stakeholder would read it on the project's worst day. Any sentence that could mean more work than you estimated is a trap. Fix it while it's still about wording, not money.

## 63.11 Estimating for Clients

Section 17.4 covered thin slices, spikes, ranges and buffers. With a client, the estimate often becomes a *price*, the listener may not be technical, and your competitors have every reason to be optimistic.

### Estimate, target, commitment

Steve McConnell's *Software Estimation: Demystifying the Black Art* separates three things that often get confused. An **estimate** is a prediction with an honest range. A **target** is what the business wants. A **commitment** is a promise to deliver defined scope by a date. Much of what goes wrong happens when a target is presented as an estimate, or an estimate turns into a commitment without anyone deciding it should. Name which one you're giving: "The estimate is 4–7 months. Your target is 5. We can commit to 5 if we drop [scope] or accept [risk]. Which do you prefer?"

### Ranges, confidence and calibration

A range without a confidence level is only half an estimate. **P50** (equally likely to come in over or under) is a reasonable internal planning number, but a dangerous external commitment, because you'll miss it half the time. **P80 or P90** is what you commit to. The gap between P50 and P90 is the price of certainty, and showing both makes that price visible.

People are overconfident when they give ranges: their "90% sure" intervals contain the true answer much less often than 90% of the time. Douglas Hubbard's *How to Measure Anything* covers calibration training. The practical fix is to keep a log of your ranges and the actual outcomes, and widen your ranges until they contain the actual as often as you claim.

### The cone of uncertainty

McConnell, building on Barry Boehm's data, calls it the **cone of uncertainty**: the range of plausible outcomes is widest at the start, and it narrows as decisions are made. In McConnell's version, an estimate at the initial-concept stage can be off by a factor of about four in either direction.

```
 multiplier
   4x │╲
   2x │  ╲____
   1x ├────────────────────────────────── (actual)
 0.5x │  ╱‾‾‾‾
0.25x │╱
      └──┬────────┬──────────┬─────────►
       initial  approved   requirements,   decisions made
       concept  concept    then design
```

Two properties of the cone matter most with clients:

- **It narrows only as decisions get made.** Time passing doesn't narrow it. If requirements are still open at month three, the uncertainty is where it was on day one. The cone shows the *best* case.
- **It tells you when a fixed price is sensible.** A fixed price given at the wide end of the cone is either heavily padded or wrong. That is the case for paid discovery (below).

### Reference-class estimates: the outside view

Bottom-up estimates come in low for a structural reason: work you can't see yet isn't in the sum. Daniel Kahneman (*Thinking, Fast and Slow*) calls this the **inside view**. The **outside view** starts by asking how long projects *like this one* actually took. That is **reference-class forecasting**, the core of Flyvbjerg and Gardner's *How Big Things Get Done*.

For a contractor, the reference class is usually your firm's own history: "our last four Framework-to-App-Service moves took [range]". Choose a class that's honestly similar. Get the *distribution* of outcomes, not just the average. Start from that distribution and adjust carefully for real differences, because most people over-adjust ("we're special") and end up back at the inside view. If your bottom-up number is well below the reference class, the likeliest explanation is missing work, not an unusual project.

> **Best practice.** Build your own reference class starting now. For every engagement, record the initial estimate, the final effort, and the main reason for the gap. After a few years, that private log will beat any technique. Keep the client details private (63.13).

### Three-point estimates and PERT

For bottom-up estimates, give each task an **optimistic (O)**, **most likely (M)** and **pessimistic (P)** value. PERT (the Program Evaluation and Review Technique, from the 1950s) combines them:

```
 Expected   E = (O + 4M + P) / 6
 Std. dev.  σ ≈ (P − O) / 6
 Total      E_total = ΣE        σ_total = √(Σσ²)   (assumes independent tasks)
```

Here is Phase 1b from the worked example, in person-days (**illustrative**):

| Task | O | M | P | E | σ |
|---|---|---|---|---|---|
| Facade, IaC, pipeline | 8 | 12 | 20 | 12.7 | 2.0 |
| Auth and session bridging | 5 | 10 | 25 | 11.7 | 3.3 |
| Uploads and scheduled job | 3 | 6 | 15 | 7.0 | 2.0 |
| Characterization tests, critical paths | 10 | 15 | 25 | 15.8 | 2.5 |
| First slice cutover (portal) | 15 | 25 | 50 | 27.5 | 5.8 |
| **Total** | 41 | 68 | 135 | **74.7** | **7.7** |

What the numbers show:

- **E (≈75) is higher than the sum of the most-likely values (68).** Tasks can overrun by far more than they can underrun, so adding up the most-likely values hides that skew.
- **The sum of the pessimistic values (135) isn't a useful ceiling,** because it assumes everything goes wrong at once. If the tasks are independent, P90 ≈ E + 1.28σ ≈ 85 days.
- **The independence assumption is the weak point.** The root-sum-of-squares formula assumes overruns partly cancel out. These tasks share causes, though: the same unfamiliar codebase, the same thin tests, the same client team. Correlated overruns don't cancel, so the true spread is wider than 7.7. Treat the PERT spread as a floor and cross-check it against the reference class.
- **The biggest error is usually a missing row, not a wrong one.** McConnell names omitted activities as a major source of underestimation. Check the list for environments, access and onboarding, data, deployment, documentation and handover, stabilization, and project management.

> **Gotcha.** If the client sees "E = 74.7", they'll remember "75 days" as a promise. Present "65–90 days; we'd commit to 85" and keep the decimals in your spreadsheet.

### Paid discovery before a fixed price

The cone and the risk premium point to the same practice: **don't fix a price for work you don't understand yet. Sell the understanding first.** A short, fixed-fee discovery moves the estimate down the cone before anyone commits to the build. Its deliverables should be worth having even if the client then hires someone else: an evidence-based dependency and risk inventory, an options memo, an estimated backlog with a reference-class check, a fixed or capped price for the next phase, and answers to the "learn next" questions.

The pitch is honest: "You can buy a large, uncertain commitment now, with a risk premium, or a small, certain one that makes the large one cheaper and safer. Either way, you own the report."

> **Pitfall.** A discovery that always recommends what the client first asked for, at the size they hoped, looks like a sales step. Its credibility comes from sometimes delivering unwelcome findings: a smaller project, a different option, or "not yet".

### Saying "I don't know yet" credibly

"I don't know" damages credibility only when nothing comes after it. Presented as the start of a plan, it shows competence:

1. **What I know.** "The portal is clean. I've read the controllers, and only the auth module touches System.Web."
2. **What I don't know, and why it matters.** "How much session state the back office shares with the portal. That could swing the estimate by a month."
3. **What it depends on.** "Whether the back office writes objects to session that the portal then reads."
4. **How and when I'll find out.** "Two days of reading and a spike with the adapters. I'll have an answer Thursday."
5. **What I can commit to now.** "3–6 months for the phase today, narrower on Thursday."

The estimate is vague, but nothing else in that answer is. It compares well with a confident single number that falls apart in week three.

## 63.12 Change Requests Without the Fight

Scope will change, and it should, because the client learns during delivery. Change control isn't there to prevent change. It's there to make change *visible and priced*, so the client decides it knowingly and doesn't find out through a late date or a surprise invoice.

The SOW sets a **baseline**, and each request is classified against it:

| Class | Example | Handling |
|---|---|---|
| **Clarification** | "By export we meant CSV." The SOW didn't specify | Absorb it; record the interpretation |
| **Defect** | The export drops a column that the acceptance criteria require | Fix at your cost |
| **Change** | "Can the export also push to the BI tool?" | Impact assessment → client decision → SOW amendment |
| **Grey area** | Ambiguous wording, and you wrote it | Discuss it; lean generous |

The impact assessment is short and gives the client choices:

```markdown
**CR-007: Push order export to the BI tool**
Raised by [name], [date] · Assessed by [name], [date]

**Impact**
Effort 6–10 days (BI API auth, mapping, retries, monitoring).
Schedule: portal cutover moves ~2 weeks if added to this phase.
Risk: needs BI sandbox access, not yet provided.

**Options**
1. Add to this phase: +[cost range], cutover moves to [date].
2. Swap for [equal-sized backlog item]: no cost or date change.
3. Defer to Phase 2.

**Recommendation**
Option 3: the BI tool goes live in [quarter]; building now integrates
against a system that isn't live yet.

**Decision**
[option] · [name] · [date]
```

There's no "out of scope" and no "not in the contract" in it. The message is *yes, and here's what it costs*, and the decision stays with the client, who is the right person to make it. Three habits keep this from turning adversarial:

- **Raise it early and lightly.** "Quick flag: this sounds like a change. I'll send a short impact note tomorrow." Raised in the moment, a change request is routine. Saved up until week ten, it feels like an ambush.
- **Offer swaps.** Under fixed price, trading for an item of equal size keeps the budget and the date and lets the scope follow what the client has learned.
- **Agree a small-change allowance.** A pool of [N] days, tracked visibly and reported weekly, takes the paperwork out of trivial requests without hiding them.

> **Best practice.** When an ambiguity comes from your own drafting, resolve it in the client's favour and say so. It costs a few days and shows you argue in good faith, which makes it easier to hold firm on the next request that really is a change.

> **Pitfall.** Unlogged "small" changes. None is big enough to discuss, but together they explain why the project is six weeks late with nothing written down. If a change takes more than an hour, log it, even if you then absorb it.

## 63.13 Your Portfolio and the NDA Line

Options memos, one-pagers, proposals and estimates show senior judgment directly, and they're usually covered by confidentiality agreements. The Part XI rule applies:

- **Practice work goes in your own public portfolio repo, not this handbook's repo.** Write a memo, an executive one-pager and a phased proposal with a three-point estimate for a *fictional* or *public* scenario: the monolith in 63.3, or an open-source project's modernization.
- **Real client documents stay private.** That includes their structure, prices and estimates, and any "anonymized" version in which the client can still be identified from its industry, stack and numbers. When in doubt, leave it out.
- **Stories about real engagements** go in your private story bank ([Chapter 36](#chapter-36-the-story-bank-evidence-portfolio)), with metrics as [placeholders] until they're verified and you're allowed to use them.

[Chapter 64](#chapter-64-the-advisory-casebook) walks through advisory situations end to end. [Chapter 65](#chapter-65-positioning-and-public-proof) turns artifacts like these into public proof.

## Exercises

No compiler checks these. The *Find the bug* samples are documents, and their defects are in the reasoning.

### Find the bug — the options memo

A contractor sent this to a client's CTO and CFO. The CFO replied: "What happens if we don't do any of these?" Find everything wrong with it, not just what the CFO spotted.

```markdown
**Options memo: Modernizing the billing service**

**Background**
The billing service is a .NET Framework 4.7.2 WCF app with EF6 and a
shared SQL Server. It has 12% test coverage, uses synchronous I/O
throughout, and the DI container is an unmaintained fork of Unity.

**Option A — Rewrite as .NET 10 microservices on AKS**
Event-sourced, CQRS, with Dapr sidecars. Cost: €412,350. Duration: 9 months.
Modern, scalable, cloud-native, future-proof.

**Option B — Upgrade in place**
Probably quite hard given the state of the code. Cost: unclear.

**Option C — Leave it**
Not recommended.

**Recommendation**
Both A and B have merits and the right choice depends on business
priorities. We'd be happy to discuss further. Option A is the more
modern approach.
```

<details>
<summary>The defects</summary>

1. **No bottom line up front and no real recommendation.** It hedges, then hints at a preference ("more modern"). Fix: open with one sentence that says what to do and why.
2. **"Do nothing" is dismissed, not analysed.** The CFO asked exactly the question the memo avoids. Fix: give C the same treatment as the other options, including its cost and any deadlines.
3. **B is a strawman.** "Probably quite hard. Cost: unclear" isn't a choice anyone could make. Fix: estimate it (Upgrade Assistant analysis, a CoreWCF or HTTP path for the WCF surface), or say what would make it estimable.
4. **False precision.** "€412,350" for a rewrite before any discovery. Fix: a range with a confidence level and assumptions.
5. **A is a technology wish list.** Nothing in the background points to event sourcing, CQRS, Dapr or AKS. No scaling or consistency problem is mentioned. Fix: start from the business problem.
6. **No forcing function and no business translation.** Test coverage, sync I/O and the DI container are engineering observations. The memo never says what they cost the business or why the decision is needed now.
7. **Unequal criteria, and no reversibility.** There's no comparison table and no risks, and a billing rewrite is a one-way door that goes unmentioned.
8. **Missing sections:** what would change the recommendation, what needs to be learned next, and the ask.
9. **Jargon for a CFO.** The executive version has to be in money, time and risk.
</details>

### Find the bug — the SOW excerpt

Your firm is about to sign this fixed-price SOW, and you've been asked to "sanity-check the technical bits". List the scope traps and propose better wording. (This is an engineering review, not a legal one.)

```markdown
**3. Scope of work**
3.1 Vendor will migrate the client's order management system to a
    modern cloud-native platform on Azure.
3.2 Vendor will migrate all existing data to the new platform.
3.3 The new platform will integrate with the client's ERP and other
    systems as required.
3.4 The platform will be secure, performant and highly available.
3.5 Vendor will provide support during go-live.

**4. Acceptance**
4.1 The deliverables will be accepted when the client confirms that the
    solution meets its needs.

**5. Timeline and fees**
5.1 Fixed fee: €[X]. Delivery within 16 weeks of signature.
5.2 Client will provide access to systems and staff as needed.
```

<details>
<summary>The traps and rewrites</summary>

- **3.1 "modern cloud-native platform":** the target is undefined (rehost? re-platform? rewrite?). Name the target architecture and link the document that defines it.
- **3.2 "all existing data":** no entities, volumes, history depth, cleansing owner, reconciliation method or cutover window. Specify each, plus [N] rehearsal runs.
- **3.3 "ERP and other systems as required":** unlimited integration scope. Use a named list (system, direction, protocol, data, sandbox provider), and route anything else through change requests.
- **3.4 "secure, performant and highly available":** three hidden NFRs with no numbers. Add p95 targets on named operations and loads, an availability target with its measurement, and the security standard, including who pen-tests and whether remediation is in scope.
- **3.5 "support during go-live":** no limit on duration or hours. Specify [N] weeks, business hours, response times by severity, and the channel.
- **4.1 acceptance "when the client confirms that the solution meets its needs":** undefined acceptance, which under a fixed fee means unlimited rework. Add per-deliverable criteria, an acceptance window, severity definitions, and deemed acceptance.
- **5.1 a fixed fee and a date with no discovery:** a fixed price at the wide end of the cone. Split it into fixed-fee discovery, then a fixed or capped build price confirmed at the gate.
- **5.2 "access ... as needed":** a hidden schedule dependency. Use a dependency table with dates, and say that slipped dependencies move the schedule and may trigger a change request.
- **Missing entirely:** assumptions, exclusions, risks and change control, which are the sections that protect both sides under a fixed fee.

Send this to your account manager as engineering risks with suggested wording. The contract drafting belongs to the people who own it.
</details>

### What would you do — the fixed price in the room

You're an outstaffed senior engineer, three weeks into a T&M engagement. In a steering meeting, with the CEO present, the CTO says: "You know the codebase best now. Give us a fixed price for the whole Framework-to-.NET 10 migration. We need it for next week's board." Your account manager isn't there.

<details>
<summary>How a senior advisor reasons about it</summary>

There are two problems here: a commercial one and an estimation one.

**Commercial:** a price commits your firm, not you, and anything you say in that room will be remembered as a quote. Be friendly and open about it: "I'll give you an honest range for the board, and I'll bring [account manager] in so the commercial side is done properly. You'll have both by [day]."

**Estimation:** three weeks in, you're still near the wide end of the cone. What the board needs is a decision it can make responsibly, and a fixed price isn't that. Offer three things: a range with its confidence level and assumptions, grounded in the reference class; a staged commitment (fund a fixed-fee discovery now, get a fixed or capped phase-one price at its gate); and the expected cost of doing nothing, so the board isn't weighing a big number against zero.

Don't give whatever number makes the meeting end smoothly. It will go onto the board slide, then into the SOW, and then into your team's overtime. Send the written version to the CTO and your account manager the same day. That document is what the board will see, so make sure you wrote it.
</details>

### What would you do — overruled

You recommended the A-then-B path from 63.3. The CEO chose a full rewrite: "We're not paying twice. Do it properly once." The CTO privately agrees with you but didn't speak up.

<details>
<summary>How a senior advisor reasons about it</summary>

First, check whether the decision contained information you didn't have. "Not paying twice" might reflect a real constraint, such as a funding round, a capital budget that can't be split, or an acquisition. If it does, update your view openly. If it rests on a misunderstanding, correct it once, briefly, in writing: "For the record: the pipeline, IaC, storage and identity work in Phase A is reused by any later path, including a rewrite." Then stop. The decision is the CEO's.

**Record it:** write an ADR with the decision, who made it, the alternatives, your dissent, and the revisit triggers. This protects everyone, including the CEO.

**Commit, and reduce the risk of the chosen path.** A rewrite doesn't have to be a big bang. Propose delivering it in slices behind a facade, keeping the old system able to take traffic until each slice has been proven, with review checkpoints. That fits "do it properly once" and turns one irreversible decision into several reversible ones. You're helping the decision succeed, not reopening it.

Avoid lobbying the CTO to reverse it, and avoid quietly steering the rewrite back towards a strangler. Either would damage trust more than being overruled did.
</details>

### Go check

- Rewrite your last significant technical recommendation as an options memo using the 63.2 template, including "do nothing" and "what would change this". Where was the original weakest? Put a fictionalized version in your portfolio repo.
- If you're allowed to, read your current engagement's SOW as its most demanding stakeholder would. List every phrase that could cover more work than planned, and check which of them have already caused friction.
- Compare your last three estimates with the actual outcomes. Were the actuals inside your ranges? Start a private estimation log today: estimate, range, confidence, actual, and the reason for any gap.
- Write the executive translation of one technical risk in your system: likelihood, impact in money or time, as a range, using the client's own numbers. Show it to someone non-technical and ask what they'd decide.
- Classify your project's open decisions as one-way or two-way doors. Is your analysis time going to the right ones?

## Sources & Further Reading

- **Barbara Minto, *The Pyramid Principle: Logic in Writing and Thinking*** — the pyramid structure, vertical and horizontal logic, MECE, and SCQA.
- **Steve McConnell, *Software Estimation: Demystifying the Black Art*** — estimates versus targets versus commitments, the cone of uncertainty, and omitted activities.
- **Barry Boehm, *Software Engineering Economics*** — the early empirical work behind the cone of uncertainty.
- **Bent Flyvbjerg and Dan Gardner, *How Big Things Get Done*** — reference-class forecasting, and why big projects overrun.
- **Daniel Kahneman, *Thinking, Fast and Slow*** — the inside and outside views, and the planning fallacy.
- **Douglas W. Hubbard, *How to Measure Anything*** — calibrated estimates and confidence intervals.
- **Frederick P. Brooks Jr., *The Mythical Man-Month*** — the limits of adding people to compress a schedule.
- **Alan Weiss, *Value-Based Fees*** and **Blair Enns, *Pricing Creativity*** — pricing advisory work by value.
- **David H. Maister, Charles H. Green and Robert M. Galford, *The Trusted Advisor*** — the relationship side of advisory work that gives this Part its name.
- **Tom DeMarco and Timothy Lister, *Waltzing with Bears*** — managing risk explicitly in software commitments.
- **Donald G. Reinertsen, *The Principles of Product Development Flow*** — cost of delay.
- **Jeff Bezos, 2015 Letter to Amazon Shareholders** — Type 1 and Type 2 decisions.
- **Michael Nygard, "Documenting Architecture Decisions"** (2011) — the original ADR format.
- **Martin Fowler, "StranglerFigApplication"** (martinfowler.com) — the pattern behind Option B.
- **dotnet/systemweb-adapters** (GitHub) — adapters for incremental ASP.NET to ASP.NET Core migration.
- **dotnet/core release notes** (GitHub, `releases-index.json`) — .NET 10's LTS status and its November 2028 end of support.
