# Chapter 36: The Story Bank & Evidence Portfolio

_⏱️ Estimated read time: ~50 min · 7428 words (study pace)_

Part XI is the practice gym. The thirty-five chapters before it explain how things work; the chapters in this Part make you *do* them and leave something behind that you can show. That second half is the one people skip, and it is the one that decides interviews and promotions. The gap between a middle and a senior engineer is rarely knowledge — plenty of middle engineers could pass a written exam on this book. The gap is **proof**: incidents you handled, decisions you defended, systems you measured, things you wrote that other people acted on.

This chapter comes first because it builds the place where all of that proof goes. You will set up a private **story bank** and a weekly **brag doc**, turn your real work into STAR stories that survive follow-up questions, rewrite your CV as claims backed by evidence, start a public **portfolio repo**, and rehearse with an AI interviewer that asks follow-ups and scores you honestly. Every lab after this one ends with an *Evidence to keep* list and an *Interview hook*; both feed the bank you build here.

```
daily work ──────────────┐
                         ▼
labs in this Part ──► brag doc ──► story bank ──► mock interview ──► rewrite
        │            (private)     (private)            ▲               │
        │                                               └───────────────┘
        ▼
    artifacts ──► public portfolio repo ◄── evidence index ◄── CV bullets
```

> **The portfolio rule.** Nothing you produce in Part XI goes into the handbook's repository. Your plans, test runs, post-mortems, reviews and designs go into **your own public portfolio repo**. Stories about a real employer stay **private**. Every lab repeats this, because it is the difference between doing an exercise and building evidence.

The kit for this chapter — templates, interviewer prompts and a git-mining script — is at [labs/36-evidence-portfolio](https://github.com/malyna2/dotnet-handbook/tree/main/labs/36-evidence-portfolio).

## Goal and the senior signal it trains

**Goal:** turn what you have done into specific, measured claims you can defend under questioning.

**The senior signal: detail density under follow-up.** An interviewer cannot check your employment history, so they check something they *can* observe: how deep your knowledge goes before it runs out. Someone who did the work can answer the third "why?" — why that index and not another, what the plan actually said, what would break at ten times the load. Someone retelling a colleague's work, or a blog post, runs out after the first. Follow-up questions work as a lie detector for second-hand experience, and they also catch honest people who simply never wrote down the details of what they did.

The second mechanism is the **scorecard**. In most structured interview loops the interviewer writes evidence against named competencies — ownership, technical depth, collaboration — and the hiring decision is made from those notes, often by people who were not in the room. An answer that is pleasant but produces no quotable evidence does not score badly; it scores *nothing*, and nothing fails you. Everything in this chapter is about making your answers produce evidence someone can write down.

## Time budget

| Activity | Time |
|---|---|
| Setup: two repos, templates, mining script | 1 h |
| Mining a year of work for candidates | 1–2 h |
| Drafting five STAR worksheets | 2 h |
| First round of mock interviews | 1 h |
| **Ongoing:** brag doc | 15 min every Friday |
| **Ongoing:** one mock interview, one rewrite | 30 min a week |

## Setup

You need two places, and the split matters.

| | Private story bank | Public portfolio repo |
|---|---|---|
| **Holds** | STAR worksheets, coverage matrix, weekly brag doc, mock transcripts and scores | Lab artifacts, your evidence index, anything you would put a link to on your CV |
| **Why this visibility** | It names employers, colleagues, customers and internal numbers | An interviewer should be able to read it without asking you |
| **Where** | A private repo, or a folder in your notes app | A public GitHub repo, e.g. `my-dotnet-portfolio` |

1. Create both. The kit's README has a copy-paste quick start.
2. Copy the templates: `star-worksheet.md` and `coverage-matrix.md` into the story bank; `portfolio-README.md` into the portfolio repo as its `README.md`.
3. Run the mining script over the repos you have worked in for the last year (next section).

> **Gotcha.** Before you paste a story into any AI assistant, strip customer names, internal system names, hostnames and unpublished figures, and check your employer's policy on external AI tools. "The payments service at a mid-size retailer" works exactly as well as the real name, and it is the only version you are allowed to share.

## Mining everyday work for stories

Memory is a biased index. It keeps what was recent and what was dramatic, and it drops the quiet month in which you made a migration safe, taught someone the codebase, or talked a team out of a rewrite. Those are often the better stories. The artifact trail does not have this bias, so mine the trail rather than your memory.

| Source | Query | What it finds |
|---|---|---|
| Your commits | `git log --author="<you>" --since="12 months ago" --no-merges` | Areas you own, bursts of activity, the week something changed |
| Merged pull requests | GitHub search: `is:pr is:merged author:<login> merged:>=<a year ago, YYYY-MM-DD>` | What you shipped, and the descriptions you wrote at the time |
| Reviews you gave | GitHub search: `is:pr reviewed-by:<login>` | Teaching, catches, disagreements |
| Reverts and hotfixes | `git log -i --grep=revert --grep=hotfix --grep=rollback` | Incidents and bad decisions |
| Incident tracker | Incidents where you were responder or incident commander | Your incident story |
| Ticket tracker | Jira: `assignee was currentUser() AND resolved >= -52w` | The long tail of shipped work |
| Calendar | Design reviews, interviews you ran, recurring 1:1s with a junior | Mentoring and influence |
| Chat | Search for "thanks" or "thank you" together with your name | Impact other people noticed |
| Past feedback | Performance reviews, peer feedback | The words other people use about you |

The kit's `scripts/mine-git.sh` automates the git part. It reads only local history and prints a Markdown report of candidates: commits per month, the directories you touched most, commits whose message mentions an incident or a fix, your largest changes, commits made at weekends or late at night, and your commits that someone later reverted.

```bash
./scripts/mine-git.sh --since "12 months ago" ~/src/billing ~/src/orders \
    > ~/story-bank/candidates.md
```

Read the report asking one question per line: **did a number move, and do I know by how much?** Then look for the **story triggers**, which are the moments that produce stories worth telling:

- a number moved — latency, error rate, cost, lead time, incident count;
- something surprised you — the cause was not where everyone looked;
- you disagreed with someone, or changed your mind;
- something broke on your watch;
- someone got better because of you;
- you said no, or "not yet", and it held;
- you deleted something — a service, a dependency, a process.

> **Best practice.** Find the number *now*, while the dashboard still has the data and the ticket still has the thread. A story without its number is weak; a number you cannot source is a liability. The brag doc (below) exists so that you never have to reconstruct a metric from memory again.

## The story bank

A story bank is a small set of prepared stories — five is enough to start — each worked out in enough detail to survive three levels of follow-up, and mapped to the questions it answers. You are not memorising scripts. You are preparing *material* that you can speak about freely because you know it thoroughly.

### How a STAR answer is scored

[Chapter 34](#chapter-34-interview-questions-how-to-answer-them) introduces STAR (Situation, Task, Action, Result). In a senior loop the proportions are what matter, because they show where your attention goes:

```
0:00       0:20   0:30                                 2:00       2:25  2:30
 ├─Situation─┼─Task─┼───────────── Action ───────────────┼─ Result ──┼Refl.┤
     ~15%       ~5%                 ~60%                      ~15%     ~5%
```

- **Situation and Task are context, not content.** If the listener needs a minute to understand your system, simplify it. They need just enough to follow the Action.
- **Action is where the evidence is**: the decisions you made, the alternatives you rejected and why, who you had to convince and how. Say "I" for what you did and "we" for what the team did, precisely. Chapter 34's warning about the spotlight applies here.
- **Result is a number with a baseline**, or a concrete outcome if you cannot defend a number.
- **Reflection is what makes it senior.** It shows you learned something specific, and ideally that the lesson changed what you did later.

Then the follow-ups start, and they climb a predictable ladder. Your story needs material on all three levels for its main decision:

```
Level 1  WHAT     "I added a partial index on the open orders, newest first."
Level 2  WHY      "The plan showed a sequential scan discarding almost every
                   row it read, then sorting everything to return twenty.
                   The index removed both steps."
Level 3  WHAT IF  "At ten times the table it still holds, because the index
                   contains only open orders. If 'open' ever became most of
                   the table, it would lose its edge over a plain index on
                   created_at, and I would drop the filter."
```

If you run out at level 2, the story is thin. If you run out at level 1, it was probably not your decision.

### The worksheet

One file per story, in the private bank. Write notes, not prose: you will *speak* this story, and memorised prose sounds memorised. The full template is `templates/star-worksheet.md` in the kit; its core is:

```markdown
**Story name:** <3–5 words>
**Questions it answers:** <at most three>
**When / where:** <month year · team · your title at the time, as held>

**Situation** (≤ 20 s): system, scale, what was at stake — for a stranger
**Task** (1 sentence): what *you* were responsible for — not the team's goal
**Action** (~60%):
- Decision → alternatives considered → why this one
- Who I had to convince, and how
- The moment it could have gone wrong
- What I did personally vs. what the team did
**Result:** before → after · window · source of the number · honest caveat
**Reflection:** what I'd do differently · what I do now because of this
**Depth check:** one line each for WHAT / WHY / WHAT IF
**Evidence I could show:** <sanitised link — or "none: confidential">
```

### Worksheets for the questions you will be asked

These are Chapter 34's behavioral questions and its follow-up about deciding without complete information, plus two that come up in almost every senior loop: an incident you owned, and influencing without authority. For each, the notes say what the interviewer is actually probing, which story to pick, what to go and find, and what sinks the answer.

#### "Tell me about a hard bug you solved"

- **Probes:** method under uncertainty — reproduce, hypothesise, test one thing at a time, bisect (Chapter 17) — and whether you fixed the cause or only the symptom.
- **Pick a story where** the bug was intermittent, distributed or production-only, and you found the root cause rather than a workaround.
- **Numbers to go find:** how often it happened (one request in N, once a week), how long it went unexplained, time from your start to root cause, recurrences since the fix.
- **Red flags:** "I added logging and saw it" with no hypothesis; the fix was a restart; the cause was "another team's bug" and the story ends there.
- **Follow-ups to prepare:** "What did you rule out first, and why?" · "How did you know it was fixed?" · "What stops it coming back?"

#### "Describe a disagreement with a colleague"

- **Probes:** ego, listening, whether you settle disagreements with data, and whether you can disagree and commit.
- **Pick a story where** both positions had merit — ideally one where you changed your mind, or committed fully to the option you argued against.
- **Numbers to go find:** the spike, benchmark or data point that settled it.
- **Red flags:** the colleague is the villain; you won by rank or by escalating; the disagreement was about style.
- **Follow-ups to prepare:** "What would they say about this?" · "What would have changed your mind?" · "How do you two work together now?"

#### "Tell me about a bad technical decision you made"

- **Probes:** self-awareness and accountability — whether you can own a real mistake with a real cost, and explain why it looked right at the time.
- **Pick a story where** the decision was yours, the cost was real, and the context made it reasonable when you made it.
- **Numbers to go find:** what it cost (engineer-weeks, incidents, money, a delayed launch) and how long it took to notice and correct.
- **Red flags:** a humble-brag ("I cared too much about quality"); blaming the requirements; a mistake with no cost.
- **Follow-ups to prepare:** "How did you notice?" · "What did it cost?" · "What do you do differently now — and when did you last do it?"

#### "How do you mentor junior developers?"

- **Probes:** leverage — whether you make other people better, or just answer their questions (Chapter 17, *teach by asking*).
- **Pick a story about** one person (anonymised), with a visible before and after in what they could do.
- **Numbers to go find:** time to their first independent release, the first incident they handled alone, the scope they now own.
- **Red flags:** "I review their code and answer questions"; no outcome for the mentee; mentoring as a list of activities.
- **Follow-ups to prepare:** "What did you do when they were stuck?" · "What did you deliberately let them get wrong?" · "How did you know it was working?"

#### "How do you handle technical debt?"

- **Probes:** business framing and prioritisation — whether you can make debt visible in the business's terms and get it paid down without a rewrite.
- **Pick a story where** you quantified a piece of debt, got time to fix it (or deliberately decided to leave it), and measured the effect.
- **Numbers to go find:** the cost of the debt before (hours per change, incidents per quarter, lead time) and after.
- **Red flags:** "we rewrote it"; arguments from code purity; "management wouldn't let us".
- **Follow-ups to prepare:** "How did you convince the product manager?" · "Which debt did you decide to leave, and why?" · "How do you stop it coming back?"

#### "How do you push back on scope or an unrealistic deadline?"

- **Probes:** negotiation and estimation honesty — turning "no" into options (Chapter 17, *estimation*).
- **Pick a story where** you put options on the table — cut scope, phase the delivery, move the date, accept a named risk — and someone else chose.
- **Numbers to go find:** original versus agreed scope and date, what shipped when, how accurate your estimate turned out to be.
- **Red flags:** you just said no; you silently worked weekends to hit it; the date slipped and you "told them so".
- **Follow-ups to prepare:** "What did you give up?" · "What if they had rejected every option?" · "How good was your estimate in the end?"

#### "Tell me about a decision you made without complete information"

- **Probes:** risk bounding — whether you can act under uncertainty without pretending it isn't there.
- **Pick a story where** you named the uncertainty out loud, chose something reversible, and set a checkpoint to find out whether you were right.
- **Numbers to go find:** what waiting would have cost, when the checkpoint was, how large the damage could have been if you were wrong.
- **Red flags:** "I gathered all the information first" — the question says you couldn't; gut feel with no checkpoint.
- **Follow-ups to prepare:** "What would have made you reverse it?" · "What did you tell stakeholders about the uncertainty?" · "Were you right?"

#### "Tell me about an incident you owned"

- **Probes:** stabilise first, diagnose second; communication under pressure; a blameless follow-up ([Chapter 17, *blameless post-mortems*](#blameless-post-mortems); [Chapter 33's incident cheat-card](#the-incident-cheat-card)).
- **Pick a story where** you were a responder or the incident commander, and the order of your actions shows mitigation before investigation.
- **Numbers to go find:** time to detect, time to mitigate, impact (users, orders, minutes), recurrences after the action items shipped.
- **Red flags:** you debugged while the site stayed down; blame; action items that were never done.
- **Follow-ups to prepare:** "What was the first thing you did, and why that?" · "Who did you tell, what, and when?" · "Which action item actually shipped?"

#### "Tell me about a time you influenced a decision without authority"

- **Probes:** trust, data, bringing people along early (Chapter 17, *judgment and influence*).
- **Pick a story where** people who did not report to you adopted a change — another team, a shared standard, a platform decision.
- **Numbers to go find:** how many teams or services adopted it, and what it changed once they had.
- **Red flags:** your manager forced it through; "they eventually agreed", with no account of how.
- **Follow-ups to prepare:** "Who resisted, and why?" · "What did you change in your proposal because of them?" · "What if they had said no?"

### The coverage matrix

Five stories should cover nine questions, and the matrix is how you check that they do. Mark `●` where a story is your first choice and `○` where it is a backup.

| Question | Deadlock | Pricing rewrite | New joiner | Retry storm | Outbox |
|---|---|---|---|---|---|
| A hard bug you solved | ● | | | ○ | |
| A disagreement with a colleague | | ● | | | ○ |
| A bad technical decision you made | | ○ | | ● | |
| How you mentor | | | ● | | |
| How you handle tech debt | | ● | | | ○ |
| Pushing back on scope or a deadline | | ○ | | | ● |
| A decision without complete information | ○ | | | | ● |
| An incident you owned | | | | ● | |
| Influencing without authority | | | ○ | | ● |

The rules that make the matrix useful:

- **Every row has a first choice**, and ideally a backup — interviewers often ask two questions that would both get your best story.
- **No story carries more than three first choices.** The same story three times in one loop tells the panel you only have one.
- **At least one story is a failure you caused.** "A bad decision you made" cannot be answered with a success in disguise.
- **At least one story is about another person** — someone you taught, convinced or disagreed with.

## The weekly brag doc

The brag document is Julia Evans's idea: a running list of what you did, written down while you still remember it, so that when a review, a promotion case or an interview comes round you are not reconstructing a year from memory. The version here adds one discipline: **the number goes in the same week**, together with where it came from.

```markdown
**Week:** 2026-W39 (Sep 21 – Sep 27)

**Shipped** — what reached users or teammates (link the PR or ticket)
**Decided** — a choice you made or influenced, and the alternative you rejected
**Measured** — a number that moved: before → after, and where it came from
**Unblocked / taught** — someone who got further because of you
**Got wrong / learned** — one line; it becomes a story's Reflection later
**Story candidate?** — yes/no, and which question it would answer
```

Rules:

- **Fifteen minutes, every Friday**, before you close the laptop. Most weeks you will write three lines. That is fine; the week you skip is the one with the number.
- **Link, don't describe.** A PR link keeps the detail that a sentence loses.
- **Private by default.** It names people and internal systems.
- **"Got wrong" is not optional.** The bad-decision story is the one people have least material for.

> **Best practice.** On the last Friday of the month, turn the month into three outcome bullets and send them to your manager. This is managing up ([Chapter 17](#disagreeing-productively-and-managing-up)): the person who argues for your promotion in a room you are not in can only use what they know. A manager with twelve monthly summaries argues from evidence; one without argues from memory.

## From artifact to CV bullet

A CV is read in seconds, and a CV bullet has two jobs: survive the skim, and invite the interview question you *want* to be asked. The shape that does both is:

```
<Verb> <the problem, in the reader's terms> by <your decision>,
       <measured result> [<scope or proof>]
```

The order is deliberate. A skimming reader picks up the verb and the result. The *decision* in the middle is bait: it is specific enough that an interviewer asks about it, and you have already prepared that story. A bullet that says *what* you worked on ("worked on performance") gives them nothing to ask; a bullet that says *how* you decided lets you steer the conversation to your strongest material.

| Weak | Strong |
|---|---|
| Responsible for performance of the orders service. | Cut p95 of the order-listing endpoint from [X] ms to [Y] ms by replacing a deep `OFFSET` scan with keyset pagination and a partial index; verified with `EXPLAIN (ANALYZE, BUFFERS)` before and after. |
| Worked with RabbitMQ and microservices. | Removed a dual write that silently dropped [N] orders per quarter by introducing a transactional outbox and idempotent consumers; duplicate-delivery tests now run in CI. |
| Mentored junior developers. | Onboarded [N] engineers onto the billing service through weekly pairing and a written runbook; one of them now leads its on-call rotation. |
| Personal project: microservices shop. | Personal lab (public repo): reproduced [N] production failure modes in a .NET 10 service instrumented with OpenTelemetry, diagnosed each from telemetry alone, and wrote blameless post-mortems — [link]. |

Lab work belongs on a CV — under **Projects** or **Practice**, labelled as a personal lab, with a link. A public repo containing before-and-after plans and a written post-mortem is stronger evidence than most job-history bullets, because the interviewer can read it before the interview.

> **Pitfall.** The keyword wall — "C#, .NET, EF Core, Docker, Kubernetes, Kafka, RabbitMQ, Azure, AWS, Redis, gRPC…" — is not neutral. Every item is a question you may be asked, and a senior interviewer will pick the one you know least. List what you would happily be quizzed on for ten minutes, and nothing else.

## Honesty rules

Exaggeration is not only unethical, it is also bad strategy, and the mechanism is simple: an interviewer who catches one inflated claim discounts every other claim you made, including the true ones. Employment checks typically confirm dates and job titles; follow-up questions check everything else.

| Rule | The honest version | Why it matters |
|---|---|---|
| **Calendar tenure** | Years of experience = the calendar span you actually worked in the stack. Overlapping jobs count once. | Employment checks compare dates, and "5 years" that turns out to be 3 is the easiest exaggeration to catch. |
| **Titles as held** | Use the title on your contract. If your responsibilities exceeded it, *describe* them in a bullet ("led a 4-person team's migration"), don't claim the title. | Titles get checked; responsibilities get discussed, and those you can defend. |
| **I vs. we** | "I" for what you did personally, "we" for the team. Be precise, not modest and not grandiose. | Too much "we" scores zero on ownership; claiming the team's work collapses at the first follow-up. |
| **Lab ≠ production** | "In a personal lab I reproduced thread-pool starvation and…" — never "In production I…" for something you only did in a lab. | Said honestly, lab experience is a strong answer: "I haven't had this in production; I've reproduced it deliberately, and this is what I learned." |
| **Numbers you can re-derive** | Only numbers for which you know the baseline, the window and how they were measured. Otherwise: "roughly halved", "from seconds to milliseconds". | The follow-up is always "how did you measure that?" |
| **Confidentiality** | Relative changes and orders of magnitude instead of internal figures; no customer names. | A candidate who leaks a previous employer's numbers will leak yours; interviewers notice. |
| **AI-polished text** | Use AI to tighten wording, then check that you can defend every line in your own words for five minutes. | Interviewers pick a line of your CV and dig. Words you did not choose are where you run out. |

Calendar tenure is where most honest people get it wrong by accident, so here it is worked through:

```
Job A       2021-03 ████████████████████████████████████ 2024-03
Contract B                             2023-06 ████████████ 2024-06
Span        2021-03 ─────────────────────────────────────── 2024-06

Sum of durations:  36 + 12            = 48 months = "4 years"  ✗
Calendar span:     2021-03 → 2024-06  = 39 months = "3 years"  ✓
```

The overlap is real work, but it did not happen in extra years. "Three years" with two overlapping engagements is true; "four years" is not.

## The mock-interview protocol

An AI assistant makes a good sparring partner for interview practice: it is available at 11 p.m., it asks follow-ups without getting bored, and it costs you nothing socially to fail in front of it. It has two limits you must design around. It **cannot tell whether your story is true** — it grades delivery, not honesty, and honesty is on you. And it **tends to be generous**, so its scores are only useful after you have checked that it is strict.

### The protocol

1. **One question per session.** Choose it from the coverage matrix, weakest row first.
2. **Timebox:** two to three minutes for the main answer. Set a timer.
3. **Speak, don't type**, if your assistant has a voice mode. Typed answers can be edited; a real answer can't. If you must type, type straight through without going back.
4. Paste the **behavioral interviewer** prompt (below), answer, take the follow-ups, then type `END` for the scores.
5. **Log it** in the story's worksheet: date, question, the five scores.
6. **Rewrite the worksheet, not the transcript**, aimed at your lowest score. Then re-take within a week.
7. Once a story scores 3 or more everywhere, put it through the **bar raiser** prompt: hostile follow-ups, to test composure.

### The rubric

All the prompts in the kit score against the same five dimensions, so your scores are comparable across sessions:

| Dimension | 1 | 2 | 3 | 4 |
|---|---|---|---|---|
| **Structure** | No discernible S/T/A/R, or the question went unanswered | STAR present, but Situation takes over half | Clear STAR, Action the largest part, 2–3 min | Result signposted early, tight, ends on reflection |
| **Ownership** | "We" throughout; your part can't be found | Your part named, but thin | Your decisions and actions explicit | Your decisions explicit, *and* how you brought others along |
| **Specificity** | No numbers or concrete detail | Numbers without baseline or method | Before → after, with how it was measured | Plus time window and an honest caveat |
| **Trade-offs** | One option, presented as obvious | Alternatives named, not weighed | Alternatives weighed against named criteria | Plus what evidence would have changed the decision |
| **Reflection** | None, or a humble-brag | A generic lesson | A specific lesson from this story | Plus evidence it changed what you did later |

### Calibrate the judge first

Before you trust the scorer with a real answer, give it one you *know* is bad. This is the same discipline [Chapter 25 applies to evaluating AI features](#testing-nondeterministic-systems-evals-for-ai-features): a judge you have not tested against a known answer is not measuring anything. Paste the **debrief scorer** prompt with this canary answer:

```text
Question: Tell me about a hard bug you solved.
Answer: We had some performance issues in our system, so the team looked into
it and we
made a lot of improvements, and in the end everything was much faster and the
client was
happy. I learned that performance is really important.
```

It should score 1 on ownership, specificity and trade-offs, and no more than 2 anywhere. If it gives a 3 on anything, the scorer is flattering you. Tighten the prompt (remind it that a 3 must be earned and needs quoted evidence), or try a different assistant, and then run the canary again. Do this once per assistant, and again whenever the assistant's model changes.

> **Pitfall.** AI scorers reward length. A four-minute answer full of detail often outscores a tight two-minute answer that a human interviewer would prefer. The *Structure* row and the timebox are your counterweight: if your score went up and your answer got longer, check the stopwatch before you celebrate.

### The prompts

Two are reproduced here in full. The kit's `prompts/` folder has these plus four more: a **story drill-down** for finding holes in a written worksheet, a **CV claim deep dive** that digs into one bullet until your knowledge runs out, the **bar raiser**, and a **system design interviewer** used again later in this Part.

**Behavioral interviewer** — replace `{{QUESTION}}`, or leave it and let it choose:

```text
You are a senior engineering interviewer running the behavioral part of an
interview loop for a Senior .NET Backend Engineer at a product company. I am
the candidate.

How you run the interview:
- Ask ONE question at a time, then stop and wait for my answer. Never answer
  for me, never hint at what a good answer would contain, never coach me
  during the interview.
- Open with this question: "{{QUESTION}}". If it still reads {{QUESTION}},
  pick one yourself from: a hard bug I solved; a disagreement with a
  colleague; a bad technical decision I made; how I mentor junior developers;
  how I handle technical debt; pushing back on scope or a deadline; a decision
  made without complete information; an incident I owned; influencing without
  authority.
- After each answer, ask 2 to 4 follow-up questions, one at a time, aimed at
  what STAR answers usually hide:
  * what I personally did, as opposed to the team;
  * which alternatives I rejected, and why;
  * how the result was measured: the baseline, the time window, the source of
    the number;
  * what the other person in any disagreement would say about it;
  * what I would do differently now.
- If I am vague ("we improved performance a lot"), ask for the specific
  number, how it was measured, and over what period.
- If I say "we" for a decision, ask who made it.
- Stay neutral during the interview: short acknowledgements only ("OK.", "Go
  on."). No praise, no reassurance.
- Keep your own turns short, like a real interviewer.

When I type END, stop interviewing and score my answers with this rubric. Give
each dimension a score from 1 to 4, quote my own words as the evidence for
each score, and be strict: a 3 must be earned, a 4 is rare.

Structure: 1 = no discernible situation/task/action/result, or the question
was not answered. 2 = STAR present but the situation took over half the
answer. 3 = clear STAR, action is the largest part, 2-3 minutes long. 4 =
result signposted early, tight, ends on reflection.
Ownership: 1 = "we" throughout, my contribution cannot be identified. 2 = my
part named but thin. 3 = my decisions and actions explicit. 4 = my decisions
explicit AND how I brought other people along.
Specificity: 1 = no numbers or concrete details. 2 = numbers without a
baseline or a method. 3 = before and after, with how it was measured. 4 =
before, after, time window, method, and an honest caveat.
Trade-offs: 1 = one option presented as obvious. 2 = alternatives named but
not weighed. 3 = alternatives weighed against named criteria. 4 = plus what
evidence would have changed my decision.
Reflection: 1 = none, or a humble-brag. 2 = a generic lesson. 3 = a specific
lesson tied to this story. 4 = a specific lesson plus evidence that it changed
what I did later.

After the scores, list the three changes that would most raise my lowest
scores, each as a concrete edit to the story, not general advice. Do not
rewrite the answer for me.
```

**Debrief scorer** — for a transcript, and for the canary:

```text
You are scoring a candidate's answer from a behavioral interview for a Senior
.NET Backend Engineer. Be strict and literal. Score only what is in the
transcript, never what the candidate probably meant. A 3 must be earned; a 4
is rare.

For each dimension give a score from 1 to 4 and quote the candidate's exact
words as evidence. If there is no evidence for a higher score, give the lower
one.

Structure: 1 = no discernible situation/task/action/result, or the question
was not answered. 2 = STAR present but the situation took over half the
answer. 3 = clear STAR, action is the largest part. 4 = result signposted
early, tight, ends on reflection.
Ownership: 1 = "we" throughout, the candidate's own contribution cannot be
identified. 2 = own part named but thin. 3 = own decisions and actions
explicit. 4 = own decisions explicit AND how they brought other people along.
Specificity: 1 = no numbers or concrete details. 2 = numbers without a
baseline or a method. 3 = before and after, with how it was measured. 4 =
before, after, time window, method, and an honest caveat.
Trade-offs: 1 = one option presented as obvious. 2 = alternatives named but
not weighed. 3 = alternatives weighed against named criteria. 4 = plus what
evidence would have changed the decision.
Reflection: 1 = none, or a humble-brag. 2 = a generic lesson. 3 = a specific
lesson tied to this story. 4 = a specific lesson plus evidence it changed
later behaviour.

Then give: the total out of 20; the single weakest moment in the answer,
quoted; and three concrete edits, each naming the sentence to change. Do not
rewrite the whole answer.

The question: <<<[paste the question]>>>
The transcript: <<<[paste the answer and any follow-ups]>>>
```

## Tasks

### Level 1 — Build the bank

- The private story bank and the public portfolio repo both exist.
- `mine-git.sh` (or the manual queries) has been run over every repo you worked in during the last year, and the output is saved in the story bank.
- Five STAR worksheets are drafted. Each has at least one number whose source you can name, and a depth-check line for WHAT, WHY and WHAT IF.
- The coverage matrix has a first choice in every one of the nine rows; no story carries more than three; at least one story is a failure you caused.
- The brag doc has entries for the last four weeks, reconstructed from commits, PRs and tickets.

### Level 2 — Pressure-test it

- You have calibrated the scorer: the canary answer scores no higher than 2 anywhere.
- Every story has been through at least one mock interview, and the scores are logged in its worksheet.
- Every story scores 3 or more on all five dimensions — or its worksheet states which dimension you cannot raise, and why (for example, confidential numbers).
- Every main answer fits in three minutes when spoken, timed with a stopwatch.
- Your two strongest stories have survived the bar raiser prompt.

### Level 3 — Publish the evidence

- The portfolio repo's README has an evidence index: competency → one-line falsifiable claim → link → type (lab, personal project, open source).
- At least three CV bullets are in problem → decision → measured result form, each either linked to an artifact or backed by a story in the bank.
- Every CV line passes the honesty checklist in `templates/cv-bullets.md`.
- Every CV bullet has been through the CV claim deep dive, and any bullet whose answers went vague has been reworded.
- The brag doc has four consecutive weekly entries written *in the week*, and one monthly summary has gone to your manager.

## Break it

Stress-test the stories before an interviewer does:

- **The adjective test.** Delete every adjective and adverb from a story — "complex", "critical", "significantly", "huge". What remains has to be impressive on its own. If it isn't, the story is running on tone.
- **The five whys, applied to yourself.** Ask "why?" about your main decision five times. If you run out before the third, that is the gap an interviewer will find.
- **The stranger test.** Tell the Situation to someone outside your company in twenty seconds. If they ask what anything means, simplify it.
- **The "we" count.** Transcribe one mock answer and highlight every "we". For each one, ask: was this a decision, and whose was it?
- **The bar raiser.** Hostile follow-ups, for stories that already score well. Watch for three failure modes: getting defensive, getting vaguer, getting longer.

## Evidence to keep

| Artifact | Where | Visibility |
|---|---|---|
| STAR worksheets with the mock-interview log | Story bank | Private |
| Coverage matrix | Story bank | Private |
| Weekly brag doc and monthly summaries | Story bank | Private (summaries go to your manager) |
| Mock transcripts and scores over time | Story bank | Private |
| Evidence index (`README.md`) | Portfolio repo | Public |
| CV with evidence-linked bullets | CV, portfolio repo | Public |
| Artifacts from the labs in this Part | Portfolio repo | Public |

## Interview hook

Two things come out of this chapter for the interview itself.

**"Tell me about yourself" in ninety seconds, built from the bank.** Present → proof → direction. What you do now, at what scale (two sentences). Two stories compressed to one line each, with their numbers — the ones you most want to be asked about. Why this role is the next step (one sentence). The two proof lines are bait, exactly like the decision in a CV bullet: they invite the interviewer to spend the next ten minutes on your best material.

**"What have you done to grow in the last year?"** — as STAR:

- **S:** [Mid-level role; feedback that your impact was hard to see beyond your own tickets.]
- **T:** Make your impact visible and checkable, and close the gaps that feedback pointed to.
- **A:** Kept a weekly brag doc with numbers sourced in the week; mined a year of PRs and incidents into five prepared stories; worked through practice labs on [query plans / messaging / incidents] and published the results; rehearsed with scored mock interviews, and rewrote each story against its weakest score.
- **R:** [Concrete outcome: a promotion case written from the brag doc, an offer, a scope you now own] — and a portfolio repo the interviewer can open right now.

Three likely follow-ups:

1. "What's one thing from your brag doc that surprised you?" — have a real answer; it shows the habit is genuine.
2. "Which of those labs was hardest, and what did you get wrong first?" — the Reflection from that lab's own interview hook.
3. "How do you decide what's worth measuring?" — whatever changes a decision: latency on the path users feel, cost, lead time, incident count; not activity counts.

## Hints and answers

<details>
<summary>"I don't have any interesting stories — my work is mostly CRUD."</summary>

Interesting stories are rarely about interesting technology. They are about a decision under a constraint. CRUD systems still have slow queries, migrations that had to run with no downtime, a field that meant two different things to two teams, a deadline that came in half as long as the estimate, and a new joiner who needed onboarding. Run the mining script and look for the story triggers — a number that moved, a disagreement, something deleted. If a year of work produces no triggers at all, the finding is itself useful: it tells you what the labs in this Part need to give you.
</details>

<details>
<summary>"My best story is an incident that was my fault. Won't that hurt me?"</summary>

It is usually your best story, and it answers two questions at once: an incident you owned, and a bad technical decision. What interviewers probe is not whether you have ever caused an outage — every experienced engineer has — but what you did in the first ten minutes, whether you told people quickly, and whether the action items made the failure impossible rather than asking people to be more careful. Tell it blamelessly about the *system*, including the part where the system let you ship it. Avoid the two failure modes: minimising it ("it was only a few minutes") and self-flagellation (a Reflection that is all apology and no mechanism).
</details>

<details>
<summary>"I can't share numbers — everything is under NDA."</summary>

Use relative changes and orders of magnitude: "p95 from seconds to tens of milliseconds", "roughly a third fewer incidents over two quarters", "a job that ran for hours now runs in minutes". Name the *method* precisely even when the value is vague, because the method is what shows you really measured it: "compared p95 from the APM for the same weekday traffic, a week before and a week after the deploy". Say once, calmly, that you are keeping the exact figures confidential. That is a professional answer, and interviewers read it as one.
</details>

<details>
<summary>"Everything we did was a team effort. I can't separate my part."</summary>

You can; you just haven't written it down yet. Go back to the artifacts: which PRs did you author, which design doc did you write or comment on, which meeting did you call, which option did you argue for? Your part is usually a decision, an investigation, or getting people to agree — not the whole outcome. "The team shipped the migration; I designed the dual-write phase and wrote the reconciliation job that proved it was safe to cut over" is precise, generous to the team, and gives the interviewer an Action to score.
</details>

<details>
<summary>"The AI interviewer gives me 3s and 4s on everything."</summary>

Then you are not measuring anything yet. Run the canary answer through the debrief scorer. If it scores above 2 anywhere, the judge is miscalibrated: add "be strict; quote evidence for every score above 2" to the prompt, or try a different assistant, and re-run the canary until it fails as it should. Check your timing as well — if your answers run past three minutes, the scorer may be rewarding length. And have at least one mock with a human; a friend reading the follow-up ladder from the worksheet catches things no model will.
</details>

<details>
<summary>"English is my second language. My stories sound flat when I say them aloud."</summary>

Structure carries more of the weight than vocabulary. Open with a one-line headline ("This is about a deadlock that only happened on Fridays"), use signposts ("There were two options…", "The turning point was…", "What I'd do differently is…"), and keep sentences short. Record yourself, transcribe it, and compare it with the worksheet: the missing parts are usually the *because* clauses, not the words. The English lab later in this Part has a phrase bank and speaking drills built for exactly this.
</details>

## Further reading

- **Julia Evans, "Get your work recognized: write a brag document"** (jvns.ca, 2019) — the original brag-document argument and template.
- **Chapter 17: Soft Skills & Engineering Practices** — [career growth toward senior and staff](#1712-career-growth-toward-senior-and-staff), [written communication](#written-communication-as-async-leverage) and [blameless post-mortems](#blameless-post-mortems), the skills the stories in this chapter show off.
- **Chapter 34: Interview Questions & How to Answer Them** — the [behavioral question bank](#behavioral-seniority) the worksheets are built around.
