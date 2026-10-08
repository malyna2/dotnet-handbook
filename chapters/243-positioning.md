# Chapter 43: Positioning and Public Proof

@@TODO: write this chapter's introduction (what it makes the reader able to do, how its sections connect), then remove every @@ line.@@

@@SRC: introduction of old Chapter 65: Positioning and Public Proof@@

The first five chapters of this Part are about being an advisor *inside* an engagement: having a point of view ([Chapter 60](#chapter-60-having-a-point-of-view)), diagnosing before prescribing ([Chapter 61](#chapter-61-discovery-and-diagnosis)), running an assessment ([Chapter 62](#chapter-62-lab-the-net-health-check)), turning it into a proposal ([Chapter 63](#chapter-63-recommendations-proposals-and-estimates)), and handling the situations that go wrong ([Chapter 64](#chapter-64-the-advisory-casebook)). This chapter is about what happens *before* the engagement: why a client, or the agency that places you, would think of you as an expert before you have said a word in the meeting.

That reputation is built from two things. **Positioning** is a short, specific claim about who you help and with what. **Public proof** is evidence of that claim that a stranger can check without asking you. Together they change the buyer's question from "which of these five CVs is cheapest?" to "is this the person who writes about exactly our problem?"

[Chapter 36](#chapter-36-the-story-bank-evidence-portfolio) built the private side: a story bank, a brag doc, evidence-backed CV bullets and a public portfolio repo. That chapter is about getting *hired*. This one reuses the same material to get *chosen*: by a client comparing specialists, by an agency's sales team deciding whom to put forward, and by people in a community who mention your name when you are not in the room.

```
            Chapter 36 (hired)                     Chapter 65 (chosen)
  brag doc ──► story bank ──► CV bullets    positioning statement
       │                         │                  │
       ▼                         ▼                  ▼
  lab artifacts ──► portfolio repo ──────► public proof ladder
                                           answers → articles → OSS
                                           → talks → case studies
                                           → public assessment
                                                    │
                          profile surfaces ◄────────┤
                          (LinkedIn, CV, GitHub,    │
                           agency one-pager)        ▼
                                           inbound questions, intros,
                                           requests for you by name
                                                    │
                                                    ▼
                                           paid assessment → retainer
```

> **The portfolio rule, extended.** Your positioning statement, your profiles, your articles and your talk abstracts live in **your own** public blog or portfolio repo, never in this handbook's repository. Stories about real clients stay private unless the client has given permission in writing, and the section on case studies below explains what permission has to cover.

@@SRC: old Chapter 34: Interview Questions & How to Answer Them@@
## When the Client Interviews You

*Revise: Part XIII — Chapters 60–65*

Contractors and outstaffed engineers are often interviewed a second time, by the **end client**. That conversation tests judgment more than trivia. The client wants to know whether they can trust you with their system and their budget.

**"Here's our situation — what would you do?"**
Don't prescribe on the first sentence. Ask two or three diagnostic questions (the goal behind it, constraints, what's been tried), play back what you heard, then give a recommendation with its conditions: "If X, I'd start with A; if Y, B — and here is what I'd check in week one." That order is itself the signal. [Chapter 61](#chapter-61-discovery-and-diagnosis) has the question bank.

**"What's your opinion on [technology or approach]?"**
Commit to a position, say what it depends on and what would change your mind. "It depends", with nothing named, reads as not knowing. [Chapter 60](#chapter-60-having-a-point-of-view) shows how to build these positions in advance.

**"How long will it take?"**
Give a range, the assumptions behind it, and how you would narrow it (a short, paid discovery or spike). A confident single number to someone who hasn't seen the code is the red flag, not the range. See [Chapter 63](#chapter-63-recommendations-proposals-and-estimates).

**"Tell me about a time you disagreed with a client."**
STAR, with the emphasis on how you made the risk visible, let the client decide, and wrote the decision down, not on being right.

> **Follow-up:** *What do you not know?* Name one real gap and how you'd close it on their project. A candidate with no gaps is less credible than one who knows where the edge is.

---

@@SRC: old Chapter 65: Positioning and Public Proof@@
## Why generalists compete on price

Start with the buyer, because positioning is a property of the buyer's mind and not of your CV.

A client, or an agency account manager, who needs a .NET engineer faces two costs before any work starts. **Search cost** is the time it takes to find candidates, read profiles and interview. **Perceived risk** is the chance that the person picked will be slow, wrong or out of their depth, and that the failure will be blamed on whoever picked them. The buyer cannot measure your skill directly (if they could, they would not need you), so they reduce both costs with proxies.

When every candidate says the same thing — "Senior .NET developer, 8 years, C#, ASP.NET Core, Azure, microservices" — the proxies all read the same, and the only visible difference left is price. Buyers are not cheap; price is simply the only number on the table they can compare. David C. Baker puts the mechanism in one line in *The Business of Expertise*: your positioning is the degree to which a client can find a suitable **substitute** for you. If ten people could replace you, you are priced like ten people.

A specialist changes both costs at once:

- **Search cost falls** because the buyer's problem matches your label. Someone with a .NET Framework 4.8 estate that has to move to .NET 10 who finds "I help teams move .NET Framework systems to modern .NET without a rewrite" does not need to read ten CVs to decide whether to talk to you.
- **Perceived risk falls** because a specialist has, by definition, seen the problem before. The buyer is not paying for your hours; they are paying for the mistakes you will not make.

David Maister's classic model of professional work in *Managing the Professional Service Firm* makes the same point from the supply side. He sorts projects into three types, and each one is bought differently:

| Maister's type | The client's problem | What they buy | How it is priced | .NET example |
|---|---|---|---|---|
| **Procedure** | Well understood; they could do it themselves | Efficiency, capacity | Rate cards, competitive bids | "We need two more mid-level devs on the team for six months" |
| **Grey hair** | Seen before, but not by them | Experience: "you have done this before" | Premium over procedure; less comparison shopping | "We are migrating 40 WCF services and we have never done it" |
| **Brains** | New, complex, high stakes | Judgment and creativity | Value; almost no comparison shopping | "Our trading platform stalls under load and three vendors could not say why" |

Most outstaffing work is sold as procedure work: a seniority band, a rate, a start date. That is a legitimate business. The aim here is to move *some* of your work up to grey hair, and positioning is how the buyer can tell you belong there.

> **Gotcha.** Adding more technologies to your profile makes the substitution problem *worse*, not better. Every keyword widens the set of people you are compared with, and the wider the set, the more the comparison is about price. The keyword wall hurts in an interview ([Chapter 36](#from-artifact-to-cv-bullet)); on a profile it hurts more, because nobody asks a follow-up.

### The trust equation

Maister, Green and Galford's *The Trusted Advisor* (from which this Part takes its name) gives a model for why proof matters. They write trustworthiness as:

```
                credibility + reliability + intimacy
  trust  =  ─────────────────────────────────────────
                        self-orientation
```

Before the first meeting, a stranger can judge only **credibility** (do you know what you are talking about?) and a little **reliability** (do you do what you say, repeatedly?). A body of writing is evidence of the first; a steady cadence is visible evidence of the second. **Self-orientation**, the denominator, is the trap: a profile entirely about you, or a post that exists to sell, raises it and cancels everything on top. That is why everything in this chapter *teaches* something rather than only announcing.

@@SRC: old Chapter 65: Positioning and Public Proof@@
## Choosing a niche

A niche has two axes, and they are built differently:

| | **Vertical** (an industry) | **Horizontal** (a problem) |
|---|---|---|
| **Example** | Payments and fintech; healthcare; logistics; insurance; e-commerce | .NET Framework → modern .NET modernization; Azure cost; performance under load; observability; supply chain security |
| **You are the expert in…** | How that business works: its regulators, its vocabulary, its failure modes | One class of technical problem, wherever it occurs |
| **Who finds you** | People in that industry, via industry events and word of mouth | People with that problem, via search and technical communities |
| **Proof looks like** | "I know what PCI DSS scope does to your architecture" | "Here is the method, the before-and-after numbers and the tool" |
| **Strength** | Clients talk to each other; one good reference travels | Every .NET shop is a possible buyer; content ranks and gets shared |
| **Risk** | The industry has a bad year and so do you | Easier for others to copy; you need a method, not only knowledge |
| **Fits people who…** | Have already spent years in one industry | Keep getting pulled onto the same kind of problem |

Morgan's *The Positioning Manual for Technical Firms* and Baker both describe this split. Most engineers already *have* a niche nobody wrote down. Open the [Chapter 36](#chapter-36-the-story-bank-evidence-portfolio) story bank and ask:

1. **Where is the evidence already?** Which kind of problem appears in three or more of your stories? That is a horizontal candidate. Which industry have you spent more than half of your career in? That is a vertical candidate.
2. **Who feels the pain and can pay?** A niche needs buyers who *know* they have the problem and have a budget line for it. "Azure bills that grew faster than traffic" has a budget owner (the CFO asks the CTO). "Code that could be more elegant" does not.
3. **Can you reach them?** Name three places where these buyers read, meet or ask questions. If you cannot, the niche may be real but it is not reachable for you yet.

The handbook's own chapters map onto horizontal niches that .NET clients buy regularly:

| Horizontal niche | The pain the buyer can name | Handbook depth to draw on |
|---|---|---|
| .NET modernization | "We are stuck on .NET Framework / WCF / Web Forms and can't hire for it" | [Chapter 30](#chapter-30-working-with-legacy-brownfield-code) |
| Cloud cost | "Our Azure bill doubled and nobody can explain it" | [Chapter 28](#chapter-28-compliance-data-privacy-cloud-cost-finops), [Chapter 50](#chapter-50-azure-in-depth-for-net-developers) |
| Performance | "It is slow at month-end and we have thrown hardware at it" | [Chapter 15](#chapter-15-performance-optimization), [Chapter 37](#chapter-37-the-slow-query-lab-reading-execution-plans) |
| Reliability and observability | "We find out about outages from customers" | [Chapter 13](#chapter-13-observability), [Chapter 21](#chapter-21-distributed-systems-theory-reliability-engineering) |
| Security and supply chain | "The auditor / the enterprise customer sent us a questionnaire" | [Chapter 14](#chapter-14-security), [Chapter 35](#chapter-35-software-supply-chain-security) |
| AI features in .NET products | "The board wants AI in the product and we don't know where to start" | [Chapter 19](#chapter-19-building-ai-powered-systems) |

> **Best practice.** Combine one horizontal with a *light* vertical when you can: "performance for .NET e-commerce back ends" is more memorable than either half, and it gives you a vocabulary (checkout, basket abandonment, Black Friday) that a pure technologist lacks. Baker describes this narrowing over time as normal: tighten the positioning as the market answers, rather than all at once on day one.

### Testing a niche cheaply

You do not need to rename yourself to test a niche; you need a few weeks and a way to hear the market answer.

| Test | What you do | Signal that it is working | Signal that it is not |
|---|---|---|---|
| **Write** | Two or three posts on the niche's core problem, each taking a position | Someone you don't know replies with *their* version of the problem | Only friends react; comments are generic ("great post") |
| **Talk** | Ten short conversations with people who have the problem: ex-colleagues, community members, your agency's account managers | People describe the pain in the same words, unprompted; someone asks "could you look at ours?" | People agree politely but can't name a time it cost them |
| **Offer** | One small, specific offer: a two-hour review, a written note, a talk at their team's lunch | Someone says yes, or asks what it would cost | Interest evaporates when a date or a price appears |

Keep a log: the test replaces your guess with what buyers actually say.

### Not over-narrowing too early

The opposite failure is real too: four years of experience and a self-declared title of "the EF Core query performance specialist for Nordic insurers" is a niche nobody can find and you cannot yet fill. Three rules:

- **Position narrowly, stay capable broadly.** Positioning is the *front door* — what you lead with and what people remember — not a list of what you refuse. Your agency will still place you on general .NET work; the positioning decides which of those engagements you turn into proof.
- **Evidence-backed today, or within a quarter.** If you can't write three honest posts on it from what you know now plus one lab, it is an aspiration. Label it so and learn toward it.
- **Change it on evidence, not boredom.** Revisit the statement every six months with the leading-indicator log (see *Measuring whether it works*). A sideways move ("performance" → "performance and cost") keeps your proof; a jump to an unrelated niche resets it.

> **Pitfall.** Choosing a niche because it is fashionable. A niche is only an asset if you can out-explain most of the people competing for the same attention. If you have never shipped an AI feature to production, "AI for .NET" is where you learn, not where you position — at least until the labs and a real engagement give you evidence.

@@SRC: old Chapter 65: Positioning and Public Proof@@
## The positioning statement

Everything else in this chapter is derived from one sentence. Write it first, privately, and let the profiles follow from it.

```
**Positioning statement**
I help [who: a specific kind of team or company]
with [problem: a pain they can name in their own words]
so that [outcome: in money, time or risk — the buyer's units].

**Proof line** (one clause, checkable)
[I have done it N times / here is the method / here is the public write-up.]

**Not for** (private; keeps you honest)
[The work you will accept but not lead with.]
```

The three slots each do a job for the buyer. **Who** lets a reader recognize themselves in two seconds. **Problem** matches the words they would type into a search box or say to a colleague. **Outcome** is what they are actually paying for: nobody buys "a migration", they buy "being able to hire for the stack again" or "not paying for two runtimes".

Worked examples, horizontal and vertical:

- *Modernization, horizontal.* I help **product companies running .NET Framework 4.x** with **moving to modern .NET without stopping feature work** so that **they can hire for their stack again and stop paying for Windows-only hosting**. Proof: *[I have led N incremental migrations using the strangler pattern; the method is written up at [link].]*
- *Cost, horizontal.* I help **SaaS teams on Azure whose cloud bill grows faster than their customer count** with **finding and fixing the few design decisions that drive most of the spend** so that **cost per customer goes down without a replatform**. Proof: *[public walkthrough of a cost assessment on a sample system, [link]].*
- *Performance, light vertical.* I help **e-commerce teams on ASP.NET Core** with **the slow pages and database stalls that show up at peak traffic** so that **the next sales peak is a normal day for on-call**. Proof: *[the slow-query lab write-up and [N] anonymized case notes].*
- *Vertical first.* I help **[logistics / insurance / fintech] companies with .NET back ends** with **[the integration and data problems specific to the industry]** so that **[regulatory deadline met / partner onboarding in days, not months]**. Proof: *[years in the industry, the domain terms you use without explanation].*

Now the tests. A statement that fails any of them goes back for another draft.

| Test | Question | Fails when… |
|---|---|---|
| **Substitution** | Could a thousand other .NET developers say this sentence truthfully? | "I help companies build scalable, high-quality software" |
| **Recognition** | Would someone in the *who* slot say "that's us" in two seconds? | *Who* is "businesses", "startups and enterprises", "clients" |
| **Buyer's words** | Is the problem phrased as the buyer feels it, not as you would fix it? | "…with CQRS and event sourcing" — that's a solution, not a problem |
| **Outcome units** | Is the outcome in money, time or risk? | "…so that code is clean", "…so that best practices are followed" |
| **Defensible** | Can you talk for ten minutes on the proof line with evidence? | The proof line is a wish, or an invented number |
| **Refusable** | Is there a plausible client this statement turns away? | Nobody would ever self-select out — so it says nothing |

> **Best practice.** Write five versions, not one: two horizontal, two vertical, one combined. Read them to someone in your agency's sales team or an ex-colleague and ask only "which one would you forward to a client, and to which client?" The answer to the second half tells you more than the answer to the first.

@@SRC: old Chapter 65: Positioning and Public Proof@@
## Profile surfaces

The statement is written once and expressed on each surface where buyers look. Each has a different reader and a different job, so the same sentence cannot be pasted everywhere.

| Surface | Who reads it | How long they read | Its job |
|---|---|---|---|
| LinkedIn headline | Recruiters, agency staff, clients' engineering managers, search | A glance, next to your name | Put you in the right mental bucket |
| LinkedIn About | People who clicked because the headline matched | Under a minute; most stop before "see more" | Confirm the bucket, give one proof, give one next step |
| CV summary | Client-side technical interviewer, agency sales | A skim before the CV bullets | Frame how the bullets below should be read |
| GitHub profile README | Engineers checking whether you're real | A minute, often right before an interview | Point to the three best pieces of proof |
| Agency / outstaff profile | Agency sales, then the client | Skimmed next to five other profiles | Make the account manager's pitch for them |

### LinkedIn headline

The headline appears wherever your name does: it is your positioning in the space of a subtitle. The common failure is a title plus a technology list.

```
**Weak**    Senior .NET Developer | C# | ASP.NET Core | Azure | Microservices | Open to work
**Better**  .NET engineer helping SaaS teams cut Azure cost without a replatform
**Better**  Moving .NET Framework systems to modern .NET, incrementally | writes about migrations
```

It needs the *who* or the *problem* (ideally both) and one hint of proof or activity. The technologies can live in the experience section, where search still finds them.

### LinkedIn About

The About section is read by people who already think you might be relevant. Make it a short argument, not an autobiography:

```
**Line 1–2 (visible before "see more")**
The problem, in the buyer's words, and who has it.

**Paragraph 2 — the point of view**
What you believe about that problem that others often get wrong. (Chapter 60.)

**Paragraph 3 — proof**
Two or three checkable items: an article, a talk, an open-source contribution,
an anonymized result with [placeholders] until you have a real, shareable number.

**Paragraph 4 — how to work with you**
Through [agency], or directly for [short assessments]; how to reach you.
```

> **Gotcha.** If you work through an agency, check your contract before writing "hire me directly" anywhere. Many outstaffing contracts contain non-solicitation or non-compete clauses covering the agency's clients. A profile that invites the agency's own clients to bypass it can breach the contract and will certainly damage the relationship. "Available through [agency] for [type of work]; I also run short independent assessments for teams outside current engagements" is honest and safe, if it matches what your contract allows.

### CV summary

Three or four lines at the top that tell the reader how to read the bullets below ([Chapter 36](#from-artifact-to-cv-bullet) covers the bullets). It is the positioning statement adjusted for a hiring reader:

```
.NET engineer (calendar years: [N]) specializing in [problem] for [kind of team].
Most recent: [one-line outcome from the strongest story, in relative terms if under NDA].
Writes about [topic] at [blog link]; [talk / OSS contribution] at [link].
```

### GitHub profile README

GitHub shows the `README.md` of a public repository named exactly like your username at the top of your profile. The reader is an engineer who wants evidence, not adjectives, so keep it short:

```
**One line**: the positioning statement, lightly edited.
**Start here**: three links — the best article, the best repo or lab write-up,
  the best talk or OSS contribution — each with a one-line "what it shows".
**Writing**: the last five posts, titles only.
**Contact / availability**: one line, consistent with your agency contract.
```

It is the front door; the [Chapter 36](#chapter-36-the-story-bank-evidence-portfolio) evidence index is where the depth lives. Pin only repositories you would happily have a client read today.

### The agency or outstaff profile

Often the *only* thing a client sees before deciding to interview you, and written for the salesperson rather than the client — see *Working through intermediaries*.

> **Best practice.** Make the surfaces **consistent, not identical**. A client who looks at your LinkedIn, then your GitHub, then the agency profile should see one person with one clear focus, told three times at three depths. If LinkedIn says "Azure cost" and the agency profile says "full-stack developer (Angular/React/.NET)", the reader trusts neither.

@@SRC: old Chapter 65: Positioning and Public Proof@@
## The evidence ladder

Positioning is a claim; proof makes it believable. Public proof comes in rungs of rising cost and persuasive power. You keep a presence on several rungs at once, and higher rungs are built from lower ones.

```
                                                             persuasive power
  6. Public assessment      a full health-check report on         ▲
                            an open-source or sample system       │
  5. Case studies           anonymized, permissioned,             │
                            problem → method → result             │
  4. Talks                  meetup → conference                   │
  3. Open-source            fixes, docs, issues with repros       │
                            in libraries your niche uses          │
  2. Articles               a position, defended, with evidence   │
  1. Answers & comments     Stack Overflow, GitHub issues,        │
                            community Q&A, thoughtful replies     │
  ──────────────────────────────────────────────────────────── cost ►
```

| Rung | Cost per item | What it proves | Who sees it | Notes |
|---|---|---|---|---|
| **1. Answers and comments** | Minutes | You know the details; you help | The person asking, plus search | The cheapest way to learn what people actually struggle with. Link to your longer writing only when it truly answers the question. |
| **2. Articles** | Hours | You can reason in public and defend a position | Your network, search | The core rung. Everything above reuses articles. |
| **3. Open-source contributions** | Hours to days | Your work survives review by maintainers who owe you nothing | Engineers | A good bug report with a minimal repro counts. So does documentation. |
| **4. Talks** | Days per talk, reusable | You can explain under pressure and handle questions | The room, then the recording | A meetup talk is a rehearsed article. |
| **5. Case studies** | Days, plus permission | You have done this for a real client, with a result | Buyers | The strongest evidence of grey hair — and the hardest to publish. |
| **6. Public assessment** | Days to a week | You have a method, not only knowledge | Buyers and engineers | A [Chapter 62](#chapter-62-lab-the-net-health-check) health-check report run on an open-source .NET application or your own sample system, published in full. |

The last rung is underrated. A case study needs a client's permission; a **public assessment** does not, because you choose the subject. Run the Chapter 62 health check on a well-known open-source .NET application or a realistic sample system, and publish the full report: findings, evidence, prioritized recommendations, the first two weeks. A buyer sees exactly what they would receive, which removes most of the perceived risk of a first paid assessment. Treat it as a demonstration of method, not a shaming: tell the maintainers first and offer the findings as issues.

> **Pitfall.** Staying on rung 1 forever. Answers are scattered across other people's platforms and nobody reads them as one body of work. Every few weeks, turn your best answer into an article on your own site.

@@SRC: old Chapter 65: Positioning and Public Proof@@
## Writing articles that take a position

Most technical writing by working engineers is a tutorial or a recap. Both are useful and neither positions you, because the reader learns about the tool, not about your judgment. The post that builds an advisor's reputation **states a position and defends it** — the written form of [Chapter 60](#chapter-60-having-a-point-of-view).

### The structure

```
**Title**: the claim, not the topic.
  "Stop using the repository pattern over EF Core for reads"  — not  "Thoughts on repositories"

**The claim (first paragraph)**
One sentence the reader could disagree with. Who it applies to.

**Why it matters (context)**
The cost of getting it wrong, in money, time or risk. One concrete situation.

**The mechanism**
Why it is true — what the runtime, the database or the team actually does.
This is where you show depth; it is the part AI-generated posts do not have.

**The evidence**
A measurement, a before/after, a minimal repro, a lab result with its environment header.

**The strongest counter-argument**
State the opposing view in the form its best advocate would use. Then answer it.

**When I'd be wrong**
The conditions under which your advice flips. This is the senior signal.

**What to do on Monday**
Two or three concrete steps a reader can take in their own codebase.
```

The last three blocks separate a position from an opinion. The strongest counter-argument shows you have thought about it; "when I'd be wrong" shows your advice depends on the reader's context, which is exactly what they want from an advisor, and it turns a reader whose situation differs into a respectful one rather than an angry commenter.

> **Best practice.** Write the "when I'd be wrong" section *first*. If you can't name the conditions under which your position fails, you don't understand it well enough to publish it yet.

### Turning chapters and labs into posts

This handbook and your lab work are raw material, but a summary of a chapter adds nothing and positions nobody. Use them as a source of *questions* and *evidence*:

| Raw material | Turn it into | Example title shape |
|---|---|---|
| A lab result with numbers (e.g. [Chapter 37](#chapter-37-the-slow-query-lab-reading-execution-plans)) | An evidence post: the surprise, the plan, before/after, environment header | "The index that made the query slower: what the plan said" |
| A *Gotcha* or *Pitfall* callout you have actually hit | A post-mortem-style post in your own words, with your own repro | "The `async void` that took our worker down — and the analyzer rule that now stops it" |
| A decision table ("which tool when") | A position post: pick one side for one kind of team, defend it | "For most line-of-business apps, start with a modular monolith" |
| A *Find the bug* exercise | A teaching post built from your *own* bug of the same class | "Three ways `HttpClient` has bitten us, and the one fix" |
| Your Chapter 62 health-check report | A method post plus the public report | "How I run a two-week .NET health check, with a full example" |
| A question you answered well in a community | A longer, linkable version on your own site | Use the asker's words in the title |

The test for any derived post: *what is in it that is not in the chapter?* Your measurement, mistake, context or decision are answers; "a shorter wording" is not.

> **Gotcha.** The Part XI environment-header rule applies to your blog. A number without CPU, RAM, .NET version, data size and cache state invites a public correction from the first expert who reads it, in your own niche. Publish the raw run output in your portfolio repo next to the post.

### Avoiding AI-generated sameness

Anyone can now produce a fluent 1,200-word article on "5 tips for EF Core performance" in a minute. Readers have learned to recognize the result: a generic title, an intro that restates the title, balanced bullet lists that never commit, and a conclusion that says "it depends on your use case". It is not that AI was used; it is that the post contains nothing that only you could have written, so it carries no evidence about you. Worse, it signals the opposite of what you want: it suggests that you have nothing specific to say.

What a model cannot supply is what makes a post worth reading, and it is also what positions you:

- **Your data.** A number from a run you did, with its environment.
- **Your mistake.** The thing you got wrong first and how you found out.
- **Your decision.** The choice you made, the option you rejected, and why.
- **Your context.** The constraint that made the textbook answer wrong for you.
- **Your voice.** The sentence you would say at a whiteboard to a colleague, not the one a style guide would produce.

A workable division of labour: you write the claim, the mechanism, the evidence and "when I'd be wrong" in your own rough words; an assistant helps with structure, cutting and English. The [Chapter 36](#honesty-rules) rule applies: if a sentence came from the model and you can't defend it for five minutes, delete it.

> **Pitfall.** Letting the model choose the topic. "Ten blog ideas for a .NET developer" returns the same ten ideas to everyone. Topics come from your brag doc, your story bank and the questions clients asked you this month.

### A cadence you can sustain

Reliability is in the trust equation. Five posts in January and none since reads as a hobby that ended; a post every two weeks for a year reads as a practice. Choose a cadence you can keep in a bad month:

| Cadence | Realistic for | Weekly time | Risk |
|---|---|---|---|
| One short post a month | Anyone with a full-time engagement | ~1 h | Slow to build a body of work, but durable |
| One post every two weeks | The default for this chapter | 2–3 h | Needs a backlog of ideas; keep one in the brag doc |
| One post a week | People with slack between engagements | 4–6 h | Tends to collapse after a few months; quality drops first |

Habits that keep it going: a *Post candidate?* line in the weekly brag doc; draft in one sitting and edit in another; publish at "good", because a correction in the comments is a conversation; and own the canonical copy on your own site, cross-posting elsewhere with a link back.

@@SRC: old Chapter 65: Positioning and Public Proof@@
## Case studies without breaking NDAs

A case study shows grey hair directly, and it is the one piece of proof that can end a contract if done carelessly. Treat it as a small legal document.

### What permission has to cover

Your contract, and usually the agency's master agreement with the client, decide what you may say. Before you write, find out:

1. **Who owns the relationship?** If you work through an agency, the client is the agency's client. Ask the agency first; they may have a process, and they may want the case study for themselves, which is fine as long as your name is on it.
2. **What does the NDA cover?** Usually the client's identity, business information, code and data. Sometimes the fact of the engagement itself.
3. **What exactly are you asking to publish?** Send the actual draft, not a description. "Can I write about the project?" gets a vague yes that won't help you if someone objects later.
4. **Get the answer in writing.** An email reply that quotes the approved text is enough.

### Anonymization

When you do not have permission to name the client, anonymize so that someone in the client's industry *could not recognize them*. Removing the name is not enough when the combination of details identifies them.

| Detail | Safe version | Why |
|---|---|---|
| Client name | "A European logistics company" | The obvious one |
| Size | "Several hundred employees", "a few million orders a year" | Exact headcount plus industry plus country is often unique |
| Internal numbers | Relative changes: "p95 from seconds to tens of milliseconds", "cost per tenant roughly halved" | Absolute figures can reveal revenue or volume |
| System names | "The order service", "the nightly billing job" | Internal codenames are searchable |
| Timeline | "Over a quarter", "last year" | Exact dates plus a public incident can identify them |
| Technology details | Keep the mechanism, drop the unusual combination | "The only .NET shop in [city] on [rare database]" identifies them |
| People | Roles only: "the CTO", "the platform team" | Never name individuals without their own consent |

What never goes into a public case study, with or without anonymization: source code from the client, screenshots of their systems or dashboards, security findings that are not yet fixed, customer data of any kind, and anything the client described to you as confidential. When in doubt, the answer is no.

> **Gotcha.** Anonymized stories still leak through *you*. If your LinkedIn says you spent 2025 at [named client] through [agency], then "a European logistics company, 2025" is not anonymous. Either keep the timeline vague or ask for permission anyway.

### The case study template

```
**Situation**: the kind of company and the problem, anonymized. One paragraph.
**Why it was hard**: the constraint that ruled out the obvious fix.
**Diagnosis**: what you looked at and what you found (Chapter 61's method).
**Decision**: the options, the one chosen, and why the others were rejected.
**Result**: relative numbers, with how they were measured.
**What I'd do differently**: one honest line.
**Permission**: "Published with the client's permission" / "Anonymized; details changed".
```

This is the [Chapter 36](#chapter-36-the-story-bank-evidence-portfolio) STAR shape rewritten for a buyer, who cares most about the *Diagnosis* and *Decision*, because that is what they would pay for.

Without permission you still have the public assessment, a lab that reproduces the same class of problem, or a "pattern" article about a problem you have seen at several clients, without describing any one of them.

@@SRC: old Chapter 65: Positioning and Public Proof@@
## Speaking

Talks put you in front of people who chose your topic, and their questions tell you what they really struggle with. Raise the stakes slowly:

```
internal demo / brown-bag  ──►  local meetup (online or in person)  ──►  regional or
at the client or agency         15–30 min, friendly audience,          community conference
                                organizers often short of speakers     ──►  larger conference
     low stakes, fast feedback                                               (CFP, committee)
```

- **Start where it's easy.** A lunch talk for your agency's engineers or the client team is a real talk; so is a local .NET user group. Ask the organizer directly.
- **Reuse your best article** — the one with the most specific replies.
- **Record it.** A recording, even a screen capture of an online meetup, turns a one-hour event into a permanent rung-4 item in your evidence index.
- **Rehearse aloud three times.** If English is your second language, structure carries more than vocabulary (see the hints in [Chapter 36](#chapter-36-the-story-bank-evidence-portfolio)).

### The CFP abstract

Conferences choose talks from abstracts submitted to a call for papers (CFP), often through a platform such as Sessionize. The abstract is usually also what attendees read in the schedule, so write it for them, and use the private notes field for the committee.

```
**Title** (short; the claim or the question)
"Your .NET Framework migration doesn't need a rewrite"

**Abstract** (for attendees, ~3 short paragraphs)
The problem, in the audience's words, and why the common approach fails.
What you will show: the method, the evidence, a live demo or real numbers.
What they leave with: two or three concrete takeaways they can use on Monday.

**Level and audience**
Intermediate; developers and tech leads maintaining .NET Framework systems.

**Notes for the committee** (private)
Why you: the experience behind the talk, where you've given it before (meetup, recording link),
related articles, and anything that makes it new compared with talks on this topic from last year.

**Bio** (third person, 2–3 sentences)
[Name] helps [who] with [problem]. [One proof line.] Writes at [link].
```

> **Pitfall.** Titles that are puns or clever references and abstracts that only describe the topic ("In this talk we will explore microservices"). A committee can't tell what the talk argues, and neither can an attendee choosing between rooms. The same claim-not-topic rule as for articles applies.

@@SRC: old Chapter 65: Positioning and Public Proof@@
## Building a small network

You don't need a large audience. You need a few dozen people in your niche who know what you are good at. For a specialist that network is the main source of work that never passes through a price comparison: a referral arrives with the referrer's credibility lent to you.

### Give first

The mechanism is ordinary reciprocity with a long time lag. You help in public without asking for anything. Most of it comes back as nothing; some comes back a year later as someone who remembers you understood their problem.

Adam Grant's *Give and Take* adds the nuance: givers are over-represented at *both* the bottom and the top of success measures. The selfless ones burn out or get exploited; the successful ones give generously while protecting their own time and goals. So give in ways that also build your proof, and limit the rest.

| Give | Cost | Also builds |
|---|---|---|
| A thorough answer to a community question in your niche | 20–30 min | Rung 1; material for an article |
| A minimal repro on an open-source issue | 1 h | Rung 3; relationship with maintainers |
| A careful review of someone's draft post or talk | 30 min | A relationship with another writer |
| Sharing someone else's good work, with a sentence on *why* | 5 min | Signals taste; the author notices |
| An introduction between two people who should talk | 10 min | You become the connector |
| Speaking at or co-organizing a local meetup | Hours a month | Visibility with everyone in the room |

What does not work: generic networking messages, "let me know if I can help", and the "give" that is a pitch in disguise. They raise self-orientation.

> **Best practice.** Keep a short private list of the people in your niche you have helped or learned from, with a note of what they are working on. Once a month, look at it and do one specific useful thing for one of them. Small, specific, repeated beats large and occasional.

### Where Blair Enns draws the line

Giving first in public is not the same as working for free for a prospect. Blair Enns's *The Win Without Pitching Manifesto* argues that firms should win work without giving away their thinking in speculative pitches. There is no contradiction between the two ideas: public writing gives away *general* thinking one-to-many, and builds reputation. A free custom diagnosis of one prospect's system is the paid product given away one-to-one, and it teaches the prospect that your judgment is free. Answer questions generously in public; when a prospect asks you to "just take a quick look at our system", that is the moment to offer a small paid assessment ([Chapter 63](#chapter-63-recommendations-proposals-and-estimates)).

@@SRC: old Chapter 65: Positioning and Public Proof@@
## Working through intermediaries

If you work through an outsourcing or outstaffing company, **your first buyer is the agency's sales team**. An account manager with a client request and a bench of ten .NET engineers puts forward the ones they can describe in a sentence; everyone else is described by seniority and rate, which is procedure work.

The mechanism: salespeople reduce their own risk too. A proposed specialist whom the client rejects costs them credibility, so they propose people whose fit they can explain with confidence. Make yourself *easy to sell as an expert*:

- **Tell them your positioning, in their words.** Meet the account managers and presales people and give them the one-sentence version. Ask what kinds of client requests they see, which is also a cheap niche test.
- **Give them something to forward.** A one-page expert profile (below) and two or three links they can paste into an email.
- **Be good in presales calls.** When they put you in a call with a prospect, apply [Chapter 61](#chapter-61-discovery-and-diagnosis): ask diagnostic questions, reflect the problem back, and give one specific insight. Salespeople remember who helps them close.
- **Tell them when the proof grows.** A new article, a talk, a client win you are allowed to mention: a two-line message to the account manager keeps you top of mind.
- **Stay loyal to the agreement.** Everything you build here makes you more valuable *to the agency* as well. Never use their clients to go around them; see the gotcha on contracts above.

### The one-page expert profile

Not a CV: a CV lists history, the expert profile argues for one kind of engagement. One page, forwardable, written so an account manager could read it aloud to a client.

```
**[Name] — [positioning in one line]**
e.g. ".NET modernization: moving .NET Framework systems to modern .NET without a rewrite"

**The problem I solve**
Two sentences in the client's words: the situation, and what it costs them.

**How I work**
A 3–4 step method, one line each (e.g. assess → plan the seams → migrate incrementally → hand over).
Mention the first deliverable: "a written assessment in [N] days".

**Evidence**
- [Relative result from an anonymized engagement, with how it was measured]
- [Public assessment / article / talk — with a short link]
- [Open-source contribution or certification relevant to the niche]

**Good fit when**
Three bullets describing the client situation in which you are the right choice.

**Not the right fit when**
One or two bullets. (This raises trust: it shows judgment, and it saves the salesperson a bad placement.)

**Availability and engagement types**
Through [agency]: full-time placement, a fixed-scope assessment, or part-time advisory.
```

The *not the right fit* block feels risky and is the most valuable part of the page. It lowers self-orientation in the trust equation, and it tells the salesperson that when you *do* say "I'm a fit", they can believe it.

> **Best practice.** Ask the agency to put the fixed-scope assessment on their price list as a product with your name on it. A salesperson can sell "a two-week .NET modernization assessment by [name]" far more easily than "[name] is also good at modernization".

@@SRC: old Chapter 65: Positioning and Public Proof@@
## From hourly contractor to advisor

The move from selling hours to selling judgment is gradual, and each step creates the trust the next one needs. Nobody pays a retainer to someone whose judgment they have not tested yet.

```
  1. hourly/placement ──► 2. advice inside ──► 3. first paid ──► 4. follow-on ──► 5. retainer
     (procedure work)       the engagement       assessment         delivery or      (access to
                            (free, visible,      (fixed scope,      implementation   judgment,
                             written)            fixed price)       oversight        monthly)
```

1. **Hourly or placement work.** Where most outstaffed engineers start. Do the work well; nothing below works without reliability.
2. **Advice inside the engagement.** Write the short decision memo, the ADR, the risk note the client did not ask for but needed ([Chapter 60](#chapter-60-having-a-point-of-view)). This is part of your paid time, and it is where the client first experiences you as an advisor. Keep copies (privately) in the brag doc.
3. **The first paid assessment.** A fixed-scope, fixed-price piece of work with a written report: the [Chapter 62](#chapter-62-lab-the-net-health-check) health check, a cost review, a migration plan. It is small enough for a client to say yes to without a long approval, and it turns your judgment into a deliverable they can hand to their boss. Pricing and the proposal are covered in [Chapter 63](#chapter-63-recommendations-proposals-and-estimates). Your first one can be sold through the agency, to a new client of theirs, or independently to a client outside any restricted relationship.
4. **Follow-on work.** The assessment ends with recommendations; some clients want you to lead or oversee them. This is where the assessment pays for itself.
5. **A retainer.** A monthly fee for access to your judgment: architecture reviews, a standing call, reviewing the team's decisions, being on hand before big changes. It only works after the client has seen your judgment pay off at least once. Chapter 63 compares retainers with time-and-materials and fixed-price models and explains how to scope them so they don't turn into unpaid hours.

Alan Weiss's *Value-Based Fees* and Jonathan Stark's *Hourly Billing Is Nuts* make the commercial case against hours. Move gradually anyway: for most outstaffed engineers the realistic first step is *one* assessment a quarter alongside a normal placement.

> **Pitfall.** Offering a free assessment "to get a foot in the door". It sets the price of your judgment at zero in the client's mind, and a free report is read less carefully than a paid one. If you want to lower the risk for the client, make the first assessment small and cheap, not free, and publish a public assessment (rung 6) so they can see exactly what they would get.

@@SRC: old Chapter 65: Positioning and Public Proof@@
## Measuring whether it works

Positioning work pays off slowly, and the numbers the platforms show you are mostly the wrong ones. Likes, impressions and follower counts measure how much *attention* a post got, not whether the *right* people now think of you for the *right* problem. They are vanity metrics for this purpose: they move easily and they don't predict work.

Track **leading indicators** instead: events that come before work and that you can count honestly each week.

| Leading indicator | Why it predicts work | Vanity metric it replaces |
|---|---|---|
| Unprompted questions from strangers about your niche problem | Someone recognized you as a source for exactly this | Impressions |
| Specific replies ("we have this too — how did you handle X?") | The post reached someone with the pain | Likes |
| Introductions and referrals ("you should talk to [name]") | Your network is working when you are not there | Connection count |
| Requests for you *by name* from agency sales or clients | The intermediary can sell you as a specialist | Profile views |
| Invitations: to speak, to review, to guest-write | Peers recognize the expertise | Follower count |
| Conversations that reach "what would that cost?" | Buyer intent | Newsletter subscribers |
| Paid assessments sold; follow-on work from them | The outcome itself (a lagging indicator) | — |

Log them in the same place and at the same time as the brag doc: ten minutes every Friday, one line per event, with a date and where it came from (which post, which talk, which intro). After three months you will see which activities produce the indicators, and you can do more of those and drop the rest.

> **Gotcha.** Everything here is small numbers. A handful of real inbound questions a quarter can be a strong signal for a specialist, and no number here is a benchmark. Compare yourself only with your own previous quarter, and look at the *source* column more than the totals.

@@SRC: old Chapter 65: Positioning and Public Proof@@
## The 90-day plan

The plan below assumes a full-time engagement and a realistic 3–4 hours a week for this work. It is split into three phases of about a month each. Each phase ends with something public.

| Weeks | Focus | Weekly time | Done when |
|---|---|---|---|
| **1–2** | Mine the story bank for niche candidates; draft five positioning statements; run the tests; pick one to test | 3 h | One statement passes every test in the table; the others are saved |
| **3–4** | Start the cheap niche test: 5 of the 10 conversations; first position post drafted and published | 4 h | Post live on your own site; conversation log started |
| **5** | Rewrite the LinkedIn headline and About, the CV summary, and the GitHub profile README from the statement | 3 h | All three surfaces consistent; a friend can repeat your focus back to you in one sentence |
| **6** | Remaining 5 conversations; community answers (rung 1), one a week from now on | 3 h | 10 conversations logged; patterns noted |
| **7–8** | Second post, built from a lab result or a story (with environment header); one open-source contribution in a library your niche uses | 4 h | Post live; PR, issue with repro, or docs fix submitted |
| **9** | Write the one-page expert profile; meet agency sales, give them the profile | 3 h | The account manager has the PDF and can say your positioning back |
| **10–11** | Third post; submit a talk to a local meetup (or give an internal one); start the public assessment on a sample or open-source system | 4 h | Meetup contacted or talk scheduled; assessment scoped |
| **12** | Publish the public assessment or a smaller version of it | 4 h | Report live, linked from the GitHub profile and the expert profile |
| **13** | Review: read the leading-indicator log; revise the positioning statement on evidence | 2 h | A written one-page review in your private notes: keep, adjust, or change the niche |

Plus, every week: ten minutes of the Friday log, and one act of "give first".

> **Best practice.** Protect the time as a recurring calendar block, the same slot each week. Positioning work is important and never urgent, which means it is the first thing a busy engagement squeezes out unless it has a fixed place.

@@SRC: old Chapter 65: Positioning and Public Proof@@
## Evidence to keep

Private notes: the five positioning drafts with their test results, the niche-test log (it names people), the leading-indicator log and the day-90 review, and any client's written permission. Public, in your own blog and portfolio repo: the published statement, articles with the raw run output behind every number, talk abstracts and recordings, permissioned case studies, the public assessment report, and a generic version of the one-page expert profile. None of it goes into the handbook's repository.

@@SRC: old Chapter 65: Positioning and Public Proof@@
## Exercises

### Find the bug

**1. A LinkedIn headline.**

```
Passionate Senior Full-Stack .NET Developer | C# | ASP.NET Core | Blazor | Angular | React |
Azure | AWS | Docker | Kubernetes | Microservices | Clean Code Enthusiast | Open to Opportunities
```

<details>
<summary>What's wrong, and a fix</summary>

It fails the substitution test completely: thousands of developers could use this line truthfully, so the only thing left to compare is price. There is no *who* and no *problem*; "passionate" and "enthusiast" are adjectives that nobody can check; the technology list invites the interviewer to ask about the weakest item (Chapter 36's keyword-wall pitfall); and "Open to Opportunities" makes you sound available rather than chosen. Both clouds and three front-end frameworks point in different directions, which makes it hard for an agency to sell you as anything specific.

A fix, from a statement that passes the tests: *".NET engineer helping SaaS teams cut Azure cost without a replatform | writes about cloud cost for .NET"*. It has a who, a problem and a proof hint; and the technologies still appear in the experience section, where search finds them.
</details>

**2. A positioning statement.**

```
I help businesses of all sizes build scalable, high-quality software solutions
using modern .NET technologies and best practices, so that they can succeed
in today's fast-moving digital world.
```

<details>
<summary>What's wrong, and a fix</summary>

Run it through the tests. **Recognition:** "businesses of all sizes" means no reader recognizes themselves. **Buyer's words:** nobody searches for "scalable, high-quality software solutions"; they search for the pain ("our app is slow at month-end"). **Solution, not problem:** "using modern .NET technologies" describes your tools, not their problem. **Outcome units:** "succeed in today's fast-moving digital world" is not money, time or risk. **Refusable:** no client would ever self-select out, so the statement filters nothing. **Proof:** none.

A fix (horizontal, performance): *"I help e-commerce teams on ASP.NET Core with the slow pages and database stalls that appear at peak traffic, so that the next sales peak is a normal day for on-call. Proof: [the slow-query lab write-up; N anonymized case notes]."*
</details>

**3. An article introduction.**

```
**Title**: Thoughts on Caching in .NET

In today's world, performance is more important than ever. Caching is a powerful
technique that can greatly improve the performance of your applications. In this
article, we will explore the different types of caching available in .NET, including
in-memory caching, distributed caching and output caching, and discuss their pros and
cons. By the end, you'll have a better understanding of which caching strategy is
right for your use case, because as always, it depends!
```

<details>
<summary>What's wrong, and a fix</summary>

The title is a topic, not a claim. The first two sentences are true of every performance article ever written and tell the reader nothing; they are also a recognizable AI-sameness opener. It promises a survey ("explore the different types"), which is the documentation's job, and it pre-announces that it will not commit ("it depends"). There is no position to disagree with, no mechanism, no evidence, and no hint of the author's own experience. A reader cannot learn anything about the author's judgment, so the post positions nobody.

A fix states a claim that a reader could argue with, names who it applies to, and promises evidence: *Title: "Add a cache last: most .NET caching bugs are invalidation bugs." — "For line-of-business APIs under [a few hundred] requests per second, a cache is usually the wrong first fix for a slow endpoint. In [the lab / a client system], the query behind the slow endpoint went from [X] to [Y] ms with one index, and the cache we had planned would have added a stale-data bug we found in testing. Here is the plan output, the fix, and the three conditions under which I would add the cache anyway."* (Placeholders stay placeholders until you have real, environment-labelled numbers.)
</details>

### What would you do

**4.** You have been outstaffed to the same client for two years through an agency. The client's CTO, impressed with your work, says in a one-to-one: "We have a sister company with a mess of a .NET Framework system. Could you take a look for them directly? Just a quick review, off the books." You have wanted to start doing assessments.

<details>
<summary>How a senior engineer reasons about it</summary>

Three problems are tangled in the request, and each has a clear answer.

- **The contract.** A sister company is very likely covered by the agency's non-solicitation clause, and "off the books" makes it worse. Before saying anything more than "thank you, let me find out how we can do this properly", read your contract, then talk to the agency. The agency usually *wants* this: it is new revenue and a new client. Propose that it is sold through them, as a fixed-scope assessment with your name on it.
- **The "quick look".** A free, informal review is the Win Without Pitching trap: it prices your judgment at zero and gets read casually. Offer a small, paid, fixed-scope assessment with a written report instead ([Chapter 62](#chapter-62-lab-the-net-health-check) for the method, [Chapter 63](#chapter-63-recommendations-proposals-and-estimates) for scoping and pricing).
- **The opportunity.** This is exactly the step from 2 to 3 in the contractor-to-advisor path, arriving through trust you have already built. Handled properly, it also becomes a case study, if you ask for permission as part of the engagement rather than afterwards.

What you say to the CTO: "I'd be glad to. Let me set it up properly through [agency] as a short, fixed-scope assessment, so there's a written report your sister company can act on. I'll come back to you this week with the scope."
</details>

**5.** Six weeks into your 90-day plan you have published two posts. One, a general piece on clean architecture, got a lot of likes and comments from your network. The other, on a specific WCF-to-gRPC migration problem, got few likes but three detailed replies from people you do not know, one of whom asked whether you do assessments. Your niche test was "modernization". A friend says to write more like the first post.

<details>
<summary>How a senior engineer reasons about it</summary>

The friend is reading vanity metrics. The first post's likes came mostly from people who already know you, and nothing in it tied you to a problem that someone would pay to have solved. The second post produced three leading indicators in one go: specific replies, strangers, and a question with buyer intent. That is the niche test saying *yes*.

So: answer the assessment question properly (a short call using [Chapter 61](#chapter-61-discovery-and-diagnosis)'s discovery questions, then a small scoped offer); write down in the indicator log which post produced it; and make the next two posts in the same vein, perhaps one on the migration decision (rewrite vs strangler) and one lab-backed piece with numbers. Keep writing general posts occasionally if you enjoy them, but don't let them take the fortnightly slot. The day-90 review will judge the niche on the indicator log, not on the like counts.
</details>

### Go check

**6.** Open your own LinkedIn profile, CV and GitHub profile side by side, as a stranger would. Write down, for each, the one sentence you think a reader would repeat about you after thirty seconds. Then show them to someone who does not know your work well (an agency salesperson is ideal) and ask them for their sentence.

<details>
<summary>What to look for</summary>

If the three sentences differ from each other, the surfaces are inconsistent; fix the weakest one first, usually the agency profile or the CV summary, which get written once and forgotten. If *their* sentence is a job title plus a technology ("a senior .NET guy who does Azure"), your positioning hasn't reached the page yet: the surfaces describe you, but they don't make a claim. If their sentence is close to your positioning statement, keep it — and keep the evidence for it next to it on the page.
</details>

**7.** Take your last three months of the brag doc from [Chapter 36](#the-weekly-brag-doc). Count the entries that could become a public post under this chapter's rules: something with your own data, mistake, decision or context, which you can publish without breaking an NDA. Then check your published writing (if any) for the last three months and apply the "what is in this post that isn't in the documentation?" test to each post.

<details>
<summary>What to look for</summary>

Most engineers find that the brag doc has more post candidates than they expected — typically the "Got wrong / learned" and "Decided" lines, not the "Shipped" ones — and that their published posts, if any, are surveys or tutorials that fail the test. The fix is a topic backlog: add a *Post candidate?* line to the weekly brag doc template, and start the next post from the strongest candidate on it. If you found no candidates you can publish at all, because everything is under NDA, the next posts should come from labs and a public assessment instead, which need nobody's permission.
</details>

@@SRC: old Chapter 65: Positioning and Public Proof@@
## Sources & Further Reading

- **David C. Baker, *The Business of Expertise: How Entrepreneurial Experts Convert Insight to Impact + Wealth*** (2017) — positioning as the degree to which a client can find a substitute for you; vertical and horizontal positioning; narrowing over time.
- **Philip Morgan, *The Positioning Manual for Technical Firms*** (2015) — the generalist-to-specialist move for developers and technical consultancies, with vertical and horizontal specialization. Morgan's site, philipmorgan.net, has his later guides on specialization and point of view.
- **David H. Maister, *Managing the Professional Service Firm*** (1993) — the brains / grey hair / procedure classification of professional work, and how each is sold and priced.
- **David H. Maister, Charles H. Green and Robert M. Galford, *The Trusted Advisor*** (2000) — the trust equation (credibility + reliability + intimacy, over self-orientation) and the advisor stance this Part is named after.
- **Blair Enns, *The Win Without Pitching Manifesto*** (2010) — winning work without giving away your thinking in speculative pitches; the line between public generosity and free consulting.
- **Adam Grant, *Give and Take*** (2013) — givers, matchers and takers; why givers are over-represented at both the top and the bottom, and what separates the two groups.
- **Alan Weiss, *Value-Based Fees*** and **Jonathan Stark, *Hourly Billing Is Nuts*** — the commercial case for moving from hours to value; read alongside [Chapter 63](#chapter-63-recommendations-proposals-and-estimates).
- **Austin Kleon, *Show Your Work!*** (2014) — a short book on sharing work in progress as a habit, useful for sustaining a writing cadence.
- **Patrick McKenzie, "Don't Call Yourself A Programmer, And Other Career Advice"** (kalzumeus.com, 2011) — on describing yourself by the business value you create rather than by the technology you use.
- **Chapter 36: The Story Bank & Evidence Portfolio** — the private [story bank](#chapter-36-the-story-bank-evidence-portfolio), [CV bullets](#from-artifact-to-cv-bullet) and [honesty rules](#honesty-rules) this chapter builds on.
- **Chapter 17: Soft Skills & Engineering Practices** — [from colleague to advisor](#from-colleague-to-advisor) and [written communication as async leverage](#written-communication-as-async-leverage).
