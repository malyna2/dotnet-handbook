# Chapter 44: The Forward Deployed Engineer

Chapters 38–43 describe an advisor who works *around* the code: a point of view, a diagnosis, a recommendation, a reputation. This chapter is about a role where the advisor also owns the result in production. A **forward deployed engineer** (FDE) is embedded with a customer, builds the thing that makes the vendor's product work on *that* customer's data and processes, and is judged on whether the customer's outcome happens, not on whether a ticket closed.

After this chapter you can say what the role is and how it differs from the neighbouring ones, run an engagement from first scope to handover, work inside an environment you do not control, close the gap between an AI demo and a system the customer trusts, send what you learned back to the product team in a form they act on, and recognise the four ways the role goes wrong. The last section maps your .NET background onto it honestly, including what you would have to add.

The sections follow the order of an engagement. [What the role is](#what-the-role-is-by-its-mechanism) and [how it differs](#fde-versus-the-neighbouring-roles) set the frame. [The engagement loop](#the-engagement-loop) is the spine, and each step of it leans on an earlier chapter instead of repeating it. The next three sections are the parts that are new: [other people's environments](#building-in-someone-elses-environment), [the demo-to-production gap for AI work](#the-demo-to-production-gap-for-ai-work) and [the feedback loop to product](#the-feedback-loop-to-product). Then come [the failure modes](#failure-modes-of-the-role) and [the route there from .NET](#getting-there-from-net).

> **A note on scope.** The title is used differently by different companies, and the market for it moves fast. This chapter describes patterns that recur, not the practice of any one employer. Where a claim depends on the company, it says so. Cases are labelled composites; numbers are `[placeholders]` or marked illustrative.

## What the role is, by its mechanism

Start from the gap the role exists to close. A vendor builds a **general** product: one data model, one set of integrations, one interface for many customers. A customer has **specific** things: its own data in its own shapes, its own approval chain, its own way of doing the work the product is supposed to improve. Between the two sits an integration and adaptation job that is too customer-specific for the product team to do from headquarters and too technical for a classic account team.

```
        general product                      specific customer
   one schema, one API, one UI         its data, its systems, its rules
              │                                      │
              └──────────────┐        ┌──────────────┘
                             ▼        ▼
                    ┌────────────────────────┐
                    │   the gap: mapping,    │   ← the FDE works here,
                    │ integration, workflow, │     on site or embedded,
                    │ trust, adoption        │     for the length of the job
                    └────────────────────────┘
                             │
                             ▼
              an outcome the customer can point at
```

The mechanism that defines the role is **ownership of the outcome across the whole gap**. An engineer who owns only a ticket can be right about the code and still watch the customer fail to adopt the result. The FDE owns the thing the customer will judge: the report the operations team now trusts, the workflow that now takes minutes instead of days. That ownership pulls the work well outside the editor: understanding the customer's process, negotiating access, sitting with the people who will use the result, deciding what to cut.

The model is widely attributed to Palantir, which put engineers inside customer organisations to make its platform work against their data. It has since been taken up in various forms by AI labs and enterprise AI vendors. Both groups sell something general (a platform, a model) to organisations whose value depends on connecting it to their own data and processes. Treat the exact origin story and each company's current definition as something to check against that company's own description of the job rather than against this book.

**Why demand grew with AI.** Two things are true of AI products at once. The capability is general: a model can read, summarise, classify and draft across domains. And the *reliability* is specific: whether it is good enough for this customer's contracts, tickets or clinical notes depends on their data, their definition of "correct" and their tolerance for error, none of which the vendor can know in advance. Closing that gap is mostly engineering plus judgment plus trust, on site, which is the FDE's job description. The technology changes quickly; the shape of the gap does not.

> **Best practice.** Describe the role by its outcome in your own words ("I make a general product work on a specific customer's data and workflow, and I'm accountable for it working") and not by a company's title. Titles vary (forward deployed, applied AI, solutions or customer engineer with a build mandate); the question that separates them is who owns production code and the outcome, which the next section sets out.

## FDE versus the neighbouring roles

Four roles share a customer and some vocabulary. They differ in what each one owns and what is left behind afterwards.

| | Forward deployed engineer | Solutions / sales engineer | Consultant | Product engineer |
|---|---|---|---|---|
| **Main job** | Make the product work on this customer's data and workflow; ship it | Show that the product *can* fit; support the sale | Advise, assess, recommend; often implement as a delivery partner | Build the general product for all customers |
| **Owns production code** | Yes, for the customer-specific layer (often alongside the customer's team) | Usually not; demos, proofs of concept, sample code | Sometimes; often the client's team owns the code | Yes, for the product itself |
| **Owns the outcome** | Yes: adoption and the customer's result | The sale and the technical fit | The quality of the advice or the deliverable; seldom the long-run result | The product's quality for the whole user base |
| **Typical reporting line** | Engineering or a dedicated deployment organisation of the vendor (varies) | Sales or pre-sales | The consulting firm | Product engineering |
| **Reusable afterwards** | Should be: connectors, evals, patterns pushed into the product | Demo assets, answers to common objections | Method, templates, the report | The product itself |
| **Typical success measure** | Customer outcome in production; expansion | Closed deals | Client satisfaction, repeat work | Product usage, reliability |

Two lines in the table carry the argument. **Owns production code** separates the FDE from the solutions engineer: a demo that runs on the presenter's laptop is a different object from a service on-call people will be paged for. **Reusable afterwards** separates the FDE from a consultant: a good consultant leaves a report, while a good FDE leaves something in the product that makes the next customer's engagement shorter. If nothing returns to the product, the role has quietly become delivery work billed to one customer, a problem [the failure modes](#failure-modes-of-the-role) return to.

For a reader of this book the closest relatives are the contractor placed at a client through an agency ([Chapter 42](#chapter-42-the-advisory-casebook)) and the advisor of [Chapter 38](#chapter-38-having-a-point-of-view) to [Chapter 41](#chapter-41-recommendations-proposals-and-estimates). The differences are the reporting line (you work for the vendor whose product you are deploying, so there are two customers: the one in the room and the product) and the accountability for the *result*, not for a document.

## The engagement loop

An engagement is a loop with five steps and a feedback edge. It is deliberately short: the working assumption is that the first usable result arrives in days or weeks, not quarters, because that is what earns the access and trust for the rest.

```
   ┌──────────────┐   ┌─────────────────┐   ┌──────────────┐
   │ 1. SCOPE one │──►│ 2. PROTOTYPE on │──►│ 3. HARDEN    │
   │  use case    │   │  real data,     │   │ security,    │
   │  + success   │   │  within days    │   │ evals, ops   │
   │  measure     │   │                 │   │              │
   └──────────────┘   └─────────────────┘   └──────┬───────┘
          ▲                                        │
          │           ┌────────────────┐   ┌───────▼───────┐
          └───────────│ 5. GENERALISE  │◄──│ 4. HAND OVER  │
        next use case │ push into the  │   │ runbook, own- │
                      │ product        │   │ ers, training │
                      └────────────────┘   └───────────────┘
```

### 1. Scope one use case

Pick **one** use case with a named business owner, a measurable result and a first version that can exist soon. "Use the platform to improve operations" is not a use case. "Triage the inbound supplier emails so the team starts each day with a ranked list" is. The method is the one in [Chapter 39: Discovery and Diagnosis](#chapter-39-discovery-and-diagnosis): find the actual problem under the stated request, with the people who live with it, before you write code. The output is a one-page problem statement with a success measure and an explicit out-of-scope list.

What is new in this role is that the scope is also a **negotiation about where the product ends and custom work begins**. If you scope a feature the product should have as a one-off, you will build it again for the next customer ([the feedback loop](#the-feedback-loop-to-product) is how that is prevented). Write the scope so that a reader can tell which parts are configuration of the product, which are customer-specific glue, and which are gaps.

### 2. Prototype on real data, within days

A prototype on invented data proves only that the code runs. A prototype on a **sample of the customer's real data** shows the things that decide the project: the format nobody documented, the field that is empty a third of the time, the system of record that disagrees with the spreadsheet everyone actually uses. This is why the role is embedded: you need access to the data and to the person who can explain it.

Time-box it and say what it is. A prototype is a question to the customer ("is this the right thing?"), and its value is the reaction, not the code. Agree up front that it will be thrown away or rebuilt, because the single most common way an engagement goes wrong starts with a prototype that is quietly promoted ([failure modes](#failure-modes-of-the-role)). The estimate and expectation-setting from [Chapter 41](#chapter-41-recommendations-proposals-and-estimates) apply: give a range, name the assumptions, and say what would change it.

### 3. Harden

Hardening is what turns the reaction into something the customer can depend on, and it is where the earlier chapters of the book come in all at once.

- **Reliability and operations:** timeouts, retries and idempotency on every call out ([Chapter 20](#chapter-20-distributed-systems)), logs, metrics and traces someone can use at 02:00 ([Chapter 25](#chapter-25-observability-and-testing-at-scale)), and a deployment that is repeatable ([Chapter 26](#chapter-26-delivery-and-platform)).
- **Security and data handling:** least privilege, secrets management and a threat model for the data you now touch ([Chapter 27](#chapter-27-security-in-depth-and-the-supply-chain)), and for personal or regulated data, the rules of [Chapter 31](#chapter-31-compliance-data-privacy-cloud-cost-finops).
- **Quality for AI components:** an evaluation set and a rollout plan, which is the subject of [its own section below](#the-demo-to-production-gap-for-ai-work).
- **Legacy edges:** whatever the system integrates with is probably older than your product ([Chapter 24](#chapter-24-working-with-legacy-brownfield-code)).

### 4. Hand over

A deployment that only its builder can run is a dependency on a person, not a result. Handover has concrete outputs: a **runbook** (what it does, what its failure looks like, who to call), named **owners** on the customer's side for the service and for the data, a trained group of users, and a defined way to report problems. Practise it once before you leave: ask the customer's engineer to deploy a change and handle a seeded failure while you watch. The incident habits of [Chapter 35](#chapter-35-production-incidents) are the model for the runbook.

### 5. Generalise

The last step is the one that decides whether the role scales. Ask of everything you built: **would the next customer need this?** What is a pattern (a connector for a common system, an evaluation harness, a deployment recipe) goes back to the product or to a shared library. What is specific stays in the customer's layer. The mechanism is described in [the feedback loop](#the-feedback-loop-to-product). Then the loop starts again with the next use case at the same customer, which is usually how an engagement grows.

> **Pitfall.** Skipping step 3 because the prototype impressed everyone. The enthusiasm that follows a good demo is the moment at which nobody wants to hear that the evaluation, the access review and the runbook are still to do. Say it early, as part of the scope, so that hardening is a planned stage and not an objection.

## Building in someone else's environment

At a vendor you control the repository, the pipeline, the cloud account and the deploy button. At a customer you control none of them, and most of the real work in the first weeks is getting to the point where you can do the job at all. The constraints are predictable, and a short checklist at kickoff avoids most of the lost weeks.

- **Network and identity.** The service may have to run inside the customer's network, behind their proxy, with no inbound access from outside, authenticating users against *their* identity provider. Ask for the network diagram and the identity integration (SSO, group-to-role mapping) in the first week, and treat the answers as design inputs. The Azure side of this (private endpoints, managed identity, Entra ID) is in [Chapter 29](#chapter-29-azure-in-depth-for-net-developers); the cloud basics are in [Chapter 28](#chapter-28-cloud-fundamentals-aws-azure).
- **Restricted data.** You may be allowed to see only a masked or synthetic extract, or only inside the customer's environment, with nothing copied out. Design the workflow so that real data never has to leave: bring the code to the data. For personal or regulated data, find out the lawful basis and the retention rules before the first query ([Chapter 31](#chapter-31-compliance-data-privacy-cloud-cost-finops)). When data may not be sent to an external model provider, that is an architecture constraint to surface at scope time, not a surprise at integration.
- **Security review.** Expect a questionnaire, an architecture review and possibly a penetration test before anything touches production. Write the answers as you go (data flows, where data is stored, who can access it, how secrets are handled), reuse them across the questionnaire and the design document, and start the review early because it is on the critical path and you do not control its calendar.
- **Procurement and approvals.** A new cloud resource, a new SaaS dependency or a new firewall rule may take weeks. Keep a list of every request with its owner and date, and design the first version so that it needs as few new approvals as possible.
- **No admin rights.** You may not be able to install a tool, open a port or read a log. Find the person who can, and learn what they need in a request in order to say yes quickly: a clear purpose, the exact resource, and a rollback. Where possible, work in a sandbox that you do control and promote through the customer's own pipeline.
- **Legacy integration.** The system of record may be a mainframe export, a SOAP service, a shared folder of CSV files, or a database that nobody dares touch. The tools of [Chapter 24](#chapter-24-working-with-legacy-brownfield-code) (characterization tests, an anti-corruption layer, strangling instead of replacing) apply directly; add a **reconciliation check** that compares your output to the system of record on a schedule, because the first sign your integration is wrong should come from your monitoring and not from the customer's finance team.

> **Best practice.** Keep an *environment log* from the first day: what you were given, what you asked for and when, what blocked you, and what you assumed. It is the evidence for a conversation about timelines ("the access request has been open eleven working days; here is what depends on it"), and it becomes part of the handover.

The thread through all of this is **respect for the customer's controls**. Controls that feel like friction (review boards, change windows, data restrictions) exist because someone was once hurt without them. An FDE who routes around them wins a week and loses the trust that the next ten weeks depend on. The senior move is to understand what each control protects, meet that need in the cheapest way available, and ask for an exception only with the reasoning written down.

## The demo-to-production gap for AI work

This is the part of the role that has grown most in recent years, and the part where the older chapters help least, because the failure mode is new. A traditional integration either works or fails visibly. An AI component works *most of the time*, fails in ways that look plausible, and gets worse in ways nobody sees unless they measure. The demo shows the best ten cases. Production contains all of them.

The chapters on [building AI-powered systems](#chapter-33-building-ai-powered-systems) and [the AI-native developer](#chapter-32-the-ai-native-developer-thriving-in-the-ai-era) cover the mechanics of prompts, retrieval, tools, agents and evaluation. This section covers the *customer-facing* half: how to decide, together with the business owner, whether it is good enough, and how to let it into the workflow without betting the process on it.

### Build the eval set from the customer's own cases

An evaluation set is a collection of real inputs with a known good outcome, run every time anything changes ([Chapter 33's evaluation section](#chapter-33-building-ai-powered-systems) has the techniques: reference-based checks, a model as judge, human review). For an FDE the new questions are *whose cases* and *who says what is correct*.

1. **Use the customer's real cases, not invented or public ones.** A model that does well on a public benchmark can still fail on the customer's abbreviations, mixed languages and scanned forms. Ask the business owner for a sample drawn from the actual flow, including the ugly parts.
2. **Include the hard and rare cases deliberately.** A random sample is dominated by easy examples. Add the cases the experts say are tricky, the ones that caused an incident in the past, and the ones where two experts would disagree. Keep them labelled so you can report on the hard slice separately.
3. **Let the customer's experts label the expected result**, and record where they disagree with one another. Disagreement is data: it tells you the task is under-specified, and no model score can be higher than the agreement between the people who wrote the answers.
4. **Keep a held-out part you do not tune against.** If you adjust prompts until the whole set passes, the set measures your tuning and not the system.
5. **Grow it from production.** Every error found after launch becomes a new case, so the set tracks the real distribution as it drifts.

### Agree "good enough" with the business owner

"It works" is not a requirement. Before the build is far along, agree in writing, with the person who owns the business result:

- **The metric and its definition** in the customer's units: the share of invoices processed without a human touch, time to first response, cost per case. Not "accuracy" without saying accuracy of *what*.
- **The threshold**, and what it depends on. An assistant that drafts text a person edits can tolerate much more error than a system that posts a payment.
- **The cost of each kind of error.** A false positive and a false negative rarely cost the same, and the owner usually knows which one is worse. That decides which way to bias the system and what to measure separately.
- **What happens on a miss:** who sees the output, who can override it, and how a wrong result is corrected.

> **Pay attention.** **Why "92% accurate" is not a requirement, and what a number needs before it can be agreed.**
>
> An accuracy figure is the average over a particular set of cases, so it moves with how that set was chosen: a sample full of easy cases reports a high number for a system that fails the cases the customer cares about. It also hides the *kind* of error. At 92%, the remaining 8% may be harmless typos or the one wrong refund that reaches the customer. And it says nothing about the rate at which the system is *sure and wrong*, which is what causes the incidents. When a scoping note promises an accuracy with no eval set behind it, the promise cannot be tested, so the first wrong answer in production becomes an argument about what was promised.
>
> Fix: define the target on an eval set built from the customer's own cases, state it per slice (easy, hard, high-cost) and per error type, have the business owner sign the threshold and the cost of each error, and attach the set to the agreement so it can be re-run. Report the result with its uncertainty: the number from a few dozen cases is a rough indication, not a measurement.

### Roll out with a human in the loop, then widen

Even with a good score, do not switch the process over in one step. A staged rollout makes the risk visible and reversible:

```
   shadow mode        suggest + human decides      act + human audits       act alone
 (runs, output is  ──► (person sees AI output  ──► (AI acts; sample is  ──► (only for the
  compared, not        and accepts or edits)         reviewed afterwards)     low-risk slice)
  used)
        │                      │                           │
   measure agreement   measure acceptance and     measure error rate in
   with humans         edit rate, collect         the audited sample,
                       corrections                alert on drift
```

Each stage gives a measurement that decides whether to move to the next: agreement with humans in shadow mode, acceptance and edit rate while suggesting, error rate in the audited sample when acting. The person in the loop is also the source of the labelled corrections that grow the eval set. Decide in advance what stops the rollout (an error rate above the agreed threshold, a drift alert) and who may stop it, and build the switch that turns the AI path off and falls back to the manual process, because the day you need it is not the day to write it. Security of the AI feature itself (prompt injection, the data it can reach, what its tools may do) is covered in [Chapter 33](#chapter-33-building-ai-powered-systems) and [Chapter 27](#chapter-27-security-in-depth-and-the-supply-chain); the customer's security review will ask about it.

## The feedback loop to product

The reason a vendor pays for engineers at customers is not only to make those customers successful. It is to learn what the product is missing. That learning only happens if the FDE sends it back in a form a product team can act on, and the first decision is what *kind* of thing the thing you built is.

| | One-off for this customer | Pattern to push into the product |
|---|---|---|
| **Signal** | Depends on this customer's quirks (their format, their rule, their ERP) | Appeared at a second customer, or is plainly general (a common system, a common failure) |
| **Where it lives** | A customer-specific layer you own and document | The product or a shared library, owned by the product team |
| **Cost of keeping it** | Maintained per customer | Maintained once, tested once |
| **Risk if you get it wrong** | Wasted time on a feature nobody else wants | A product shaped by one loud customer |

A reasonable rule: build it as customer-specific the first time, **keep it separable** so that it can be lifted out, and promote it when a second customer needs it or when the first one will clearly have to maintain it forever. Building a general capability for a single customer's guess is as much a mistake as writing the same glue code five times. When you cannot tell, ask the product team early, because a conversation costs less than a rewrite.

### Writing a field report a product team acts on

A product team receives requests from many places and has to rank them. A field report that says "the customer wants better search" gets filed. One that gives the evidence, the cost of the gap and a proposal gets scheduled. The shape is that of the problem statement in [Chapter 39](#the-one-page-problem-statement) and the recommendation in [Chapter 41](#chapter-41-recommendations-proposals-and-estimates): symptom, evidence, options, a stated preference with its conditions. A short template:

```
**Field report**: <one line: the gap, in the product's terms>

**Customer and context**: <composite or anonymised; their size, their domain,
what they were trying to achieve>

**What happened**: <the observed failure or workaround, with the evidence:
logs, counts, the eval result, a timeline>

**Impact**: <what it cost, in the customer's units and in engagement time;
how many users or cases; what it blocks>

**How often**: <this customer's count, and other customers who hit it, with
the evidence for each>

**Workaround today**: <what you built, where it lives, how fragile it is,
how long it took, who maintains it>

**Options**: <A: product change, B: shared library or connector, C: stay
customer-specific; with the cost of each>

**Recommendation**: <the option you would choose, and what would change your mind>

**What I need**: <a decision, a design review or an owner, and by when>
```

Two habits make these reports effective. **Report the mechanism, not the complaint**: "the connector retries on a 429 without honouring `Retry-After`, so a burst of 200 documents produced [N] throttled calls" lets an engineer reproduce it, while "it's slow" does not. And **close the loop**: when the product ships the change, remove your workaround and tell the customer, otherwise the custom code stays forever and the next engagement inherits it.

> **Gotcha.** A customer's feature request is evidence of a *problem*, not a specification. Pushing the customer's proposed solution into the product copies one organisation's workaround into everyone's interface. Send the problem, the evidence and the range of acceptable solutions, and let the product team design the capability.

## Failure modes of the role

The role has four characteristic ways of going wrong. Each has a mechanism and a countermeasure that you can put in place on day one.

**1. Becoming free staff augmentation.** The engagement drifts from "make the product work on their data" to "do whatever engineering the customer's team did not have time for". The mechanism is easy to follow: you are capable, you are there, requests arrive in chat, and each one is small. The product never changes, the customer's own team never learns the system, and the vendor is, in effect, paying for a contractor without the revenue to match. *Countermeasure:* every piece of work maps to the agreed use case and success measure; anything else goes through the same scoping step ([Chapter 41's scope traps](#chapter-41-recommendations-proposals-and-estimates) apply), and you ask of each request whether it moves the outcome or merely moves a queue.

**2. The prototype that never ships.** The demo is impressive, nobody owns the next step, and months later the "pilot" is still running on your laptop or a notebook, serving a few enthusiasts. The mechanism is that the prototype was never designed to cross the line into production: no evaluation, no security review, no named customer owner, no budget for operating it. *Countermeasure:* agree the production criteria at scope time ([the loop's hardening step](#3-harden)), name the customer's owner for production before the prototype starts, and set a date by which it is either promoted, rebuilt or stopped. A pilot with no exit date is a decision that has not been taken.

**3. Dependence on one person.** The whole engagement lives in your head: the data quirks, the access arrangements, the deployment steps, the political map. If you are ill or are moved to another customer, it stops. This is a failure for the customer *and* for you, because you cannot be moved on to the next job while it stays true. *Countermeasure:* the environment log and runbook from [handover](#4-hand-over), a second person (the customer's or the vendor's) who has performed each critical operation, and a habit of writing down decisions as you make them, in the style of an ADR ([Chapter 16](#chapter-16-working-like-a-middle-developer)).

**4. Unbounded scope.** Each success earns the next request, and the use case grows until it is a programme with no measure of done. The mechanism is that an embedded engineer is the cheapest source of "while you're here" work. *Countermeasure:* a written scope with an out-of-scope list, a change process even if it is one email, and a regular review where the business owner decides what to continue, stop or hand to someone else. Scope reviews are also where you learn whether the original success measure still matters.

What these have in common is that the *absence of a decision* is what causes them: nobody decided where the engagement ends, who owns production, who else can run it, what is out of scope. The countermeasure is the same in each case: make the decision explicit, in writing, early.

## Getting there from .NET

If you are a .NET engineer reading this because the role looks attractive, the honest summary is that you already have a large part of it, and the missing parts are learnable and specific.

**What transfers.**

- **Enterprise integration.** Most customers' systems of record are exactly the estates a .NET engineer knows: SQL Server, Active Directory and Entra ID, SOAP and REST services, file drops, message queues, scheduled jobs. That is a large share of the integration work, and it is hard for someone who has only built greenfield products.
- **Azure and the enterprise cloud.** Identity, networking, private connectivity, Key Vault, monitoring ([Chapter 28](#chapter-28-cloud-fundamentals-aws-azure) to [Chapter 31](#chapter-31-compliance-data-privacy-cloud-cost-finops)). Many customers run on Azure, and the people on the other side of the table are often the identity and network teams you will work with.
- **Data.** Query design, plans, consistency, migrations ([Chapter 18](#chapter-18-data-in-depth)). Understanding someone's data well enough to build on it is the first week of every engagement.
- **Client communication.** The advisory habits of Chapters 38–43: diagnosing before prescribing, writing short decision documents, handling disagreement, scoping honestly. For many engineers these are the part that is hardest to find elsewhere, and this book has already given you a method for each.
- **Production sense.** Having been paged ([Chapter 35](#chapter-35-production-incidents)) is a real qualification for a role in which you own the result.

**What to add.**

- **Reading fluency in Python and TypeScript.** AI tooling, data work and many vendors' SDKs and examples are in Python, and a lot of customer-facing UI work is in TypeScript. You do not have to be an expert, but you must be able to read an unfamiliar codebase in either, run it, change it and review it. A realistic goal is to be productive in one of them within weeks, and to be able to follow the other.
- **Evaluation practice.** Building an eval set, labelling with experts, measuring per slice and tracking change over time, as in [the section above](#the-demo-to-production-gap-for-ai-work) and in [Chapter 33](#chapter-33-building-ai-powered-systems). This is the skill that most separates "built a demo" from "can be trusted with production" and is easy to show: you can publish an evaluation of a public dataset and your method.
- **Fast prototyping.** Going from a vague request to a working thing on real data in days: choosing what to leave out, using AI tools to move faster ([Chapter 32](#chapter-32-the-ai-native-developer-thriving-in-the-ai-era)), knowing when to stop polishing. This is a habit that you build by doing it repeatedly with a timer running.
- **Working with ambiguity and people.** Setting your own agenda in a customer's building, asking for what you need and saying no politely. If you have never done it, a contractor engagement with a clear scope is practice.

**What interview loops commonly include.** Loops vary widely by company and change often, so treat this as a list of patterns, not a description of any named employer. Candidates commonly report some mix of: a coding exercise (often practical and often in Python), a technical design or "build a small integration" session, a *decomposition* or case discussion in which you are given a vague customer problem and asked how you would scope and run it, a behavioural round about working with difficult stakeholders and ambiguity, and a conversation about past customer-facing work. The case discussion is where the habits of [Chapter 39](#chapter-39-discovery-and-diagnosis) and [Chapter 41](#chapter-41-recommendations-proposals-and-estimates) show: asking diagnostic questions before proposing a solution is the visible signal. The stories for the behavioural round come from your [story bank](#chapter-37-the-story-bank-evidence-portfolio), and the public proof that gets you the call is in [Chapter 43](#chapter-43-positioning-and-public-proof). Always read the actual job description and any candidate guide the company publishes, because the role and the loop differ from one employer to the next.

> **Best practice.** Build proof the role would ask of you: a small public project that takes a messy real-world dataset, solves a defined use case with an AI component, ships an eval set and a short write-up of the decisions and the failures. It shows prototyping speed, evaluation practice and communication in one artifact, and nobody's NDA is involved.

## Decide

A manufacturing customer asks your vendor to deploy an assistant that answers maintenance technicians' questions from [N] thousand PDF manuals and past work orders. The pilot sponsor says the demo was great and wants it "live for all plants next month". You have had access to a sample of the data for a week. Three proposals:

1. Build the full solution for all plants, since the demo worked and the sponsor is ready.
2. Run a staged pilot at one plant: build an eval set from the technicians' own past questions, agree the success measure and the cost of a wrong answer with the maintenance lead, run in shadow mode and then suggest-only, and decide on wider rollout from the measurements.
3. Decline until the vendor's product has a built-in manuals feature, so no custom work is needed.

<details>
<summary>How a senior engineer weighs it</summary>

**What each costs.**

- *Full build for all plants:* fastest to a headline, and every risk lands at once: unmeasured quality on the hard questions, the security and data-access review for every site, and no agreed definition of "good". A confident wrong answer about a safety procedure is a very different incident from a wrong answer about a holiday policy.
- *A staged pilot:* slower to the headline and the only option that produces evidence. The eval set and the agreed measure are reusable for every plant, and shadow mode costs little because nobody acts on the output yet.
- *Waiting for the product:* avoids custom work and also avoids the learning. The vendor's product team learns what manuals customers need only because someone builds it somewhere first.

**What decides it:** the cost of a wrong answer (safety-related maintenance) and the absence of any measurement. Without an eval set from real technician questions, "great demo" is an anecdote.

**The choice.** Option 2, with the wider rollout gated on the measured acceptance and error rates for the high-risk question types, a human-checked path for safety-critical answers, and a field report to the product team on whatever you had to build that is likely to recur.

**What would change it.** If the data may not leave the plant network, scope the architecture (on-premises or private deployment) before anything else. If the safety team requires that no generated answer is used without a cited source, the design must show the source passage and refuse to answer without one. Both are constraints to surface in week one.
</details>

## Exercises

### Find the bug

**1. A scoping note.**

```
Subject: Scope for the contract-review assistant (Northwind Logistics)

Scope: build an assistant that reads supplier contracts and flags risky clauses.

Success: the assistant will be 95% accurate at flagging risky clauses.
Timeline: live for the whole legal team in 3 weeks.
Data: we will test with a handful of sample contracts you send us.
Out of scope: nothing; we will support whatever the legal team needs.

After launch the assistant runs unattended and legal will receive its flags
directly.
```

<details>
<summary>What's wrong, and a fix</summary>

Nearly every sentence is a future dispute.

- **"95% accurate" with no eval set.** There is no set of contracts, no definition of "risky", no statement of who decided what counts as a correct flag, and no split between a missed risky clause and a spurious flag, which cost very different amounts. The number cannot be checked, so the first miss becomes an argument about the promise.
- **"A handful of sample contracts"** is not a representative evaluation sample and is not a held-out set; real contracts have scanned pages, amendments and several languages. The data plan also ignores access and confidentiality: these are legal documents under the customer's controls.
- **"Live for the whole legal team in 3 weeks"** skips the hardening step (security review, access, logging), a measured rollout and a named owner for production.
- **"Out of scope: nothing"** is the unbounded-scope failure mode in one line.
- **"Runs unattended"** removes the human in the loop for a task where the cost of a missed clause is high, with no stage in which the output is checked first.

A fix keeps the same project and makes it testable: *Use case:* flag [N] named clause types in supplier contracts for the legal team's first-pass review. *Eval:* a set of [N] contracts from the customer's own archive, labelled by two lawyers (disagreements recorded), with a held-out part; the owner agrees the threshold separately for missed clauses and false flags. *Rollout:* shadow mode, then suggestions that a lawyer accepts or rejects, with the acceptance rate reported weekly; no unattended use. *Timeline:* a prototype on real contracts in [N] days; a production date set only after the security review and the measured results. *Out of scope:* listed clause types not covered, other document types, and any legal advice.
</details>

### What would you do

**2.** Four months into an engagement, the customer's operations director messages you directly most days with small requests: a report tweak, a one-off data export, a script for another team. Each takes under an hour, and she thanks you warmly. Your agreed use case, a demand-forecasting workflow, has not moved in three weeks. Your manager asks how the engagement is going.

<details>
<summary>How a senior engineer reasons about it</summary>

This is the first failure mode, staff augmentation, with a pleasant face. The requests are small, the sponsor is happy, and the engagement is drifting: the success measure is not moving and the vendor is paying for work that has no path back to the product.

- **Look at the facts before reacting.** Tally the last month by type: how many hours went to the agreed use case, how many to requests, and what the requests have in common. If several are really the same missing capability (a reporting need, an export), that is a *product signal* worth a field report. If they are unrelated chores, they are a different engagement.
- **Do not simply refuse.** The director is a sponsor and the relationship is an asset. Go to her with the numbers: "Here is where my time went; the forecasting workflow, which we agreed is the measure of this engagement, has had [X] hours. I'd like to keep helping with the small things, and I want to do it without losing the forecast. Can we agree a weekly slot for requests and a rule for what goes into it?"
- **Route the recurring requests properly.** Hand the exports and report tweaks to her team with a short how-to, or turn the repeated ones into a small supported feature (and report the gap to product).
- **Tell your manager the real state** and propose the plan: the forecasting milestone and its date, the request slot, and what you have asked the customer to take over.

The aim is not fewer favours. It is that the engagement is again steered by a written outcome, not by whoever messaged last.
</details>

**3.** Your team built an invoice-extraction pipeline for one customer, with a connector for their ERP and a layout parser for their invoices. A second customer, in a different industry, has asked for something very similar. Your manager suggests copying the first customer's repository, renaming it and deploying. The product team has a roadmap item for a generic extraction connector, but it is not scheduled.

<details>
<summary>How a senior engineer reasons about it</summary>

A second customer is exactly the signal that the first build contains a pattern. But copying the repository turns one customer's quirks into the second's starting point and doubles the maintenance, without anything returning to the product.

- **Separate the parts.** Split what you built into the customer-specific (that ERP's field mapping, those invoice layouts) and the general (the extraction harness, the evaluation harness, the retry and idempotency handling, the deployment recipe). The general parts are what the second customer actually needs.
- **Reuse the general parts as a library or template** with the customer-specific parts as configuration. That gets customer two moving quickly *and* creates something the product team can adopt.
- **Send a field report** with the evidence from two customers: the repeated need, the time the first build took, the part that is general, and a proposal (a shared connector, a layout-parsing component). Two customers moves a roadmap item from "maybe" to "scheduled".
- **Do not promise a date for the product work.** Tell the second customer what exists now (a proven pattern with customer-specific configuration) and what is not guaranteed (the product feature).

What you say to your manager: "I can start customer two next week from the reusable parts rather than a copy, and I'll send product a field report with both customers' evidence so the generic connector has a chance of being scheduled."
</details>

### Go check

**4.** Take a project you were part of (a real one, kept private) in which you integrated something with a customer's or another team's system. List every control that stood between you and a working deployment (network, identity, data access, security review, approvals) and note for each one how long it took and when you first asked for it.

<details>
<summary>What to look for</summary>

Most people find that the controls that cost the most time were asked for last. The pattern is the argument for an environment log and a kickoff checklist: the earliest requests are the ones to make in the first week, because they are the ones whose calendar you do not control. If you find a control you worked around instead of satisfying, ask whether it protected something real, and whether you could have met the same need in a way the owner would have accepted.
</details>

**5.** Pick an AI feature you use or have built (a summariser, a classifier, a code assistant). Write ten real inputs you would put in its eval set, including at least three hard ones. For each, write the expected result and what a *wrong but plausible* answer would look like. Then ask someone else who knows the domain to label the same ten without seeing yours, and compare.

<details>
<summary>What to look for</summary>

Two things usually surprise people. First, the **disagreement rate**: if the two labellings differ on more than one or two of the ten, the task is under-specified, and no score on this set could be trusted until the definition of "correct" is written down. Second, the *wrong but plausible* answers tend to be more dangerous than the clearly wrong ones, which is why the eval set needs error types and not just a pass rate. Keep the ten cases in your own portfolio repository; they are the start of an eval set and of a public write-up.
</details>

## Interview Questions

**What does a forward deployed engineer do differently from a solutions engineer?**

<details>
<summary>Answer</summary>

A solutions engineer shows that the product can fit and supports the sale; a forward deployed engineer builds the customer-specific layer, puts it into production and is accountable for the customer's outcome. The difference shows in who owns production code and who is paged. The FDE also sends what recurs back into the product, so the next engagement is shorter. See [the comparison table](#fde-versus-the-neighbouring-roles).
</details>

**A customer wants an AI feature "95% accurate". How do you respond?**

<details>
<summary>Answer</summary>

Ask what the 95% is measured on. An accuracy figure is the average over a set of cases, and it moves with how that set is chosen and hides the kind of error. Build an eval set from the customer's own cases (with the hard ones included), have their experts label it, agree a threshold per error type with the business owner who knows what each error costs, and roll out in stages with a human in the loop. See [the demo-to-production gap](#the-demo-to-production-gap-for-ai-work).
</details>

**How do you decide whether to build something once for a customer or push it into the product?**

<details>
<summary>Answer</summary>

Build it as a separable customer-specific layer the first time, and promote it when a second customer needs it or the first will plainly have to maintain it forever. Send the product team a field report with the problem, the evidence, the workaround's cost and options, and not only the customer's proposed solution. See [the feedback loop to product](#the-feedback-loop-to-product).
</details>

**Your prototype impressed the customer's executives. What do you do next?**

<details>
<summary>Answer</summary>

Treat the enthusiasm as the moment to set the production criteria, not to skip them: agree a named owner on the customer's side, the security and data review, an eval set and a rollout plan, and a date by which the pilot is promoted, rebuilt or stopped. A prototype that is quietly promoted is the usual way an engagement fails. See [the loop's hardening step](#3-harden) and [the failure modes](#failure-modes-of-the-role).
</details>

**You cannot get admin rights or network access you need. What do you do?**

<details>
<summary>Answer</summary>

Find out what the control protects, then meet that need in the cheapest way and ask the owner with a clear purpose, the exact resource and a rollback. Start requests in the first week because their calendar is not yours, keep an environment log so you can show what is blocked and what depends on it, and design the first version to need as few approvals as possible. See [building in someone else's environment](#building-in-someone-elses-environment).
</details>

**Why does a staged rollout matter for an AI feature that scored well in testing?**

<details>
<summary>Answer</summary>

The test set is a sample, and production drifts from it; the cost of an error depends on how the output is used. Shadow mode, then suggestions a person accepts, then audited action, each stage gives a measurement that gates the next, and a switch back to the manual path limits the damage of a miss. The corrections also grow the eval set.
</details>

## Sources & Further Reading

- **David H. Maister, Charles H. Green and Robert M. Galford, *The Trusted Advisor*** (2000): the trust equation behind the stance this role depends on; the customer-facing half of the job is the same trust problem with higher stakes.
- **Peter Block, *Flawless Consulting*** : contracting and scoping with a client, and the difference between doing the work for someone and being accountable alongside them.
- **Michael Feathers, *Working Effectively with Legacy Code*** (2004): characterization tests and seams, the techniques for the legacy integration that most customer environments require.
- **Company job descriptions and engineering blogs.** The role is defined by each employer; read the posting for the job you want and the employer's own account of its deployment model, because they differ and change. This chapter deliberately cites no company's hiring process or statistics.
- **In this book:** [Chapter 33: Building AI-Powered Systems](#chapter-33-building-ai-powered-systems) (prompts, retrieval, agents, evaluation, security) and [Chapter 32: The AI-Native Developer](#chapter-32-the-ai-native-developer-thriving-in-the-ai-era) (working faster with AI tools); [Chapter 39: Discovery and Diagnosis](#chapter-39-discovery-and-diagnosis), [Chapter 41: Recommendations, Proposals and Estimates](#chapter-41-recommendations-proposals-and-estimates) and [Chapter 42: The Advisory Casebook](#chapter-42-the-advisory-casebook) (the engagement habits); [Chapter 43: Positioning and Public Proof](#chapter-43-positioning-and-public-proof) and [Chapter 37: The Story Bank & Evidence Portfolio](#chapter-37-the-story-bank-evidence-portfolio) (the proof and the stories); [Chapter 24: Working with Legacy & Brownfield Code](#chapter-24-working-with-legacy-brownfield-code), [Chapter 27: Security in Depth and the Supply Chain](#chapter-27-security-in-depth-and-the-supply-chain), [Chapters 28–31](#chapter-29-azure-in-depth-for-net-developers) (cloud, Azure, compliance) and [Chapter 35: Production Incidents](#chapter-35-production-incidents); [Chapter 36: Senior Behaviours, Career and Interviews](#chapter-36-senior-behaviours-career-and-interviews) (the wider career map).
