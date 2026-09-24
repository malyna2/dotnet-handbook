# CV bullets — problem → decision → measured result

> Work in your private story bank; only the finished bullets go on the CV.

## The shape

```
<Verb> <the problem, in the reader's terms> by <your decision>,
       <measured result> [<scope or proof>]
```

- **Verb first**, past tense, one that names what *you* did: cut, removed, replaced, introduced, designed, led, unblocked.
- **The decision** is the part that earns the interview question you *want* to be asked. Make it specific enough to be interesting.
- **The result** is a number with a baseline, or a concrete outcome. If you cannot defend the number for five minutes, use the outcome instead.
- **Scope or proof** in brackets: the scale, or where the evidence lives (a link for lab work).

## Worksheet — one row per candidate bullet

| Problem (who suffered, how) | My decision (and the rejected alternative) | Result (before → after, source) | Final bullet |
|---|---|---|---|
| | | | |

## Rewrites to learn from

| Weak | Strong |
|---|---|
| Responsible for performance of the orders service. | Cut p95 of the order-listing endpoint from [X] ms to [Y] ms by replacing a deep `OFFSET` scan with keyset pagination and a partial index; verified with `EXPLAIN (ANALYZE, BUFFERS)` before and after. |
| Worked with RabbitMQ and microservices. | Removed a dual write that silently dropped [N] orders per quarter by introducing a transactional outbox and idempotent consumers; duplicate-delivery tests now run in CI. |
| Mentored junior developers. | Onboarded [N] engineers onto the billing service through weekly pairing and a written runbook; one of them now leads its on-call rotation. |
| Improved code quality. | Introduced Conventional Comments and a 400-line PR size guideline; median review turnaround fell from [X] to [Y] days over [window]. |
| Personal project: microservices shop. | Personal lab (public repo): reproduced [N] production failure modes in a .NET 10 service instrumented with OpenTelemetry, diagnosed each from telemetry alone, and wrote blameless post-mortems — [link]. |

## Honesty checklist — every line must be "yes" before a bullet ships

- [ ] Every date matches my employment records to the month, and overlapping roles are **not** added together (calendar tenure).
- [ ] Every title is the one on my contract or offer letter. Responsibilities beyond the title are *described* in a bullet, not claimed as a title.
- [ ] Every "I" is something I did personally; everything else says "we" or "the team".
- [ ] For every number I know the baseline, the time window and how it was measured — and can say all three out loud.
- [ ] Lab and side-project work is labelled as such and is never described as production experience.
- [ ] Nothing confidential: no customer names, no internal system names, no figures my employer has not published (use relative changes or orders of magnitude instead).
- [ ] Every line an AI helped me polish, I can defend in my own words for five minutes.
- [ ] Every skill in the skills list is one I would happily be quizzed on for ten minutes.
