# Part 1 · Module 5: Working Like a Middle Developer

> **What this module makes you able to do.** Take a vague ticket, a request for an estimate or a pull request, and send back something a teammate can act on without a meeting: a problem statement with your questions, a range with the assumption that drives it, a review comment with its condition and its fix, a request for help that shows what you tried.

**Time:** reading ≈ 40 min; hands-on ≈ 2 h 15 min — the four written tasks 1 h, the questions 15, the check at work 1 h.

## Covers

- turning a vague, solution-shaped ticket into a one-page problem statement with your questions, before writing code;
- giving an estimate as a range with the assumption that drives it, and re-estimating the day that assumption breaks;
- writing a review comment that gets acted on: condition → mechanism → cost → fix, labelled `blocking:`, `suggestion:`, `nit:` or `question:`;
- finding something worth saying in a review: what if it runs twice, concurrently, slowly, fails halfway, or meets 100× the data?
- digging on your own inside a timebox, then asking with what you tried;
- asking a question in a meeting that changes the outcome: prepared, early, with the assumption stated, confirmed in writing.

## The mechanism to explain without notes

**Turn every guess into a claim someone else can check, with the condition or assumption it rests on, in writing, before anyone builds on it.**

Two facts make this the job. People act on a claim they can check in a minute, and ignore or argue with one they can't: "this could be a problem" leaves the author to rebuild your reasoning, while "two concurrent deliveries both pass the check, so the card is charged twice" can be verified on the spot. And a misunderstanding costs more with every step built on it: a misread ticket costs one reply before the code exists, a rewrite after review, an incident after release.

Every habit in this module applies both facts:

- **A vague ticket** names a solution ("add caching") and hides the problem. A short problem statement (the symptom with its evidence, the target, the constraints, what is out of scope) plus your questions makes your reading of the ticket checkable while that costs one reply.
- **An estimate** is a forecast that holds only while its assumptions do. A point hides which one drives it; a range with its driving assumption tells everyone what to watch. When the assumption breaks, the estimate is void: say so that day, with the new range.
- **A review comment** that lands is a prediction: under this condition, this line does this, which costs that, and here is the fix. The label says whether it blocks the merge. Five questions (twice, concurrently, slowly, halfway, at 100× the data) are how you find the conditions.
- **Being stuck** is a guess about where the problem is that nobody else can see. After a timebox of about 30 minutes, a question with the goal, the exact error, what you tried and what it ruled out lets the helper check your search instead of restarting it.
- **A meeting question** works the same way: prepared from the agenda, asked before the room converges, phrased as the assumption you are testing ("I'm assuming the export runs nightly. Is that right?"), and confirmed in writing afterwards, because everyone leaves a meeting remembering it differently.

> **Pay attention.** **A missed estimate has two causes, and padding fixes neither.** Either the work was harder than you pictured, or you estimated a different scope from the one the asker meant. In the first, the steps you couldn't picture were never in the sum, so the error runs one way: long. The fix is a range anchored on how long similar work actually took, with the assumption behind its top named. In the second, the estimate was right for the wrong ticket; the fix is the problem statement and its questions before any number.

## Read (≈ 40 min)

1. [Chapter 17: 17.1 From Solving Tickets to Creating Leverage](#171-from-solving-tickets-to-creating-leverage): the table. Each right-hand answer removes a surprise for someone else, and that habit is what lets a middle developer work without supervision.
2. [Chapter 61: The Request Is Not the Need](#the-request-is-not-the-need) and [The One-Page Problem Statement](#the-one-page-problem-statement): written for consultants, but a ticket is a request too. For a ticket, keep *Problem*, *Constraints*, *Out of scope*, *Success looks like* and *Open questions*.
3. [Chapter 17: 17.4 Estimation & Planning](#174-estimation-planning), then [What would you do — the estimate](#what-would-you-do-the-estimate): ranges, spikes, and the assumption written next to the number.
4. [Chapter 17: 17.3 Code Review Mastery](#173-code-review-mastery), then [What would you do — the review](#what-would-you-do-the-review): the five questions in *Finding What to Say in a Review*, the comment formula, the labels, and how much to say to someone new.
5. [Chapter 18: Judging AI-generated code: a reviewer's rubric](#judging-ai-generated-code-a-reviewers-rubric): read it as the checklist of *what to look for*; the defects are the same in human-written code. Keep its *Signal → check* table and its order for reading a diff.
6. [Chapter 17: 17.6 Methodical Debugging & Problem Solving](#176-methodical-debugging-problem-solving): the hypothesis loop and the 30-minute rule.
7. [Chapter 17: Written communication as async leverage](#written-communication-as-async-leverage), [Running meetings that don't waste an hour × N people](#running-meetings-that-dont-waste-an-hour-n-people), [Asking Questions That Unblock You](#asking-questions-that-unblock-you) and [Disagreeing productively and managing up](#disagreeing-productively-and-managing-up): the ask in the first line, a question that carries what you tried and the assumption you are testing, decisions written down, disagree and commit.

## Prove it

This module's hands-on is written. Do each task before you open its answer; the answer is one good version, not the only one.

**1. Review before you run.** Part 1, Module 4 prints the `CheckThenAct` experiment, [`verify/path/CheckThenAct/Program.cs`](https://github.com/malyna2/dotnet-handbook/blob/main/verify/path/CheckThenAct/Program.cs). Treat its first handler, `CheckThenAct`, as a pull request and write one labelled review comment. Then run it from `verify/path` with `dotnet run --project CheckThenAct` and check your prediction against the output.

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

- **(a) and (b) were right but unactionable.** The author learned *what* you disliked, not *why*, so arguing or ignoring was cheaper than checking. The mechanism is what lets them verify you in a minute: [Chapter 20: Keep-Alive, Connection Pooling, and Socket Exhaustion](#keep-alive-connection-pooling-and-socket-exhaustion) for (a), [Chapter 8: The Sync-Over-Async Deadlock](#the-sync-over-async-deadlock) for (b). A local example to copy (`PaymentsClient`) makes the fix cheaper than a reply.
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
- **The question that finds it:** what if it fails halfway? [Chapter 50: Service Bus](#service-bus) has the settings; [Chapter 51, Case 6](#case-6-40000-messages-in-the-dead-letter-queue-and-nobody-knew) is what this looks like in production.
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
