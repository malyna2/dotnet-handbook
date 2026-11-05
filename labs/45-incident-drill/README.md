# Lab kit — Chapter 45, Lab 7: Incident Drill

A 90-minute rehearsal of a production incident, run on your own machine with the faulty release from Lab 3 (`labs/45-runtime-diagnostics`). One person (or a timer) injects the fault; you run the incident from the first alert to the post-mortem, and you write the two documents real teams keep: a runbook and a blameless post-mortem.

> **The portfolio rule.** Your filled-in runbook, timeline and post-mortem go in **your own public portfolio repo**. A post-mortem of a real incident at your employer stays private.

**Last verified:** 2026-11-05. The fault (the starter build of `labs/45-runtime-diagnostics`) was reproduced with that kit's `verify.sh` on Ubuntu 24.04, 4 vCPU, .NET SDK 10.0.114. The documents in this folder are templates; nothing in them needs a run.

## The scenario

You own the *Quotes* service. At 14:00 a release went out. At 14:07 the latency alert fires: p95 above 1 s for 5 minutes (normal: about 150 ms). CPU on the instances is at 15 %. The database dashboard is green.

## Run it

```bash
cd ../45-runtime-diagnostics
dotnet build src/DiagLab.Api -c Release -p:LabVariant=starter     # the "bad release"
dotnet src/DiagLab.Api/bin/Release/net10.0/DiagLab.Api.dll --urls http://localhost:5080 &
dotnet run --project src/DiagLab.Load -c Release -- "http://localhost:5080/quote?sku=abc" 2000 100   # production traffic
```

Start a stopwatch when the load starts. Work through `runbook-template.md` as if the service were real: write the timeline as you go (`timeline-template.md`), decide when to "roll back" (stop the app, build and start `-p:LabVariant=solution`), and confirm recovery with the same load. Then write `postmortem-template.md` within 48 hours, as a team would.

## Acceptance

- **Level 1:** the timeline has a timestamp for detection, the first status update, mitigation and recovery, and you mitigated (rolled back) *before* you had the root cause.
- **Level 2:** the runbook names the exact command, counter or query for every diagnostic step, and someone who has never seen the service could follow it.
- **Level 3:** the post-mortem has no person's name in the causes, at least one action item that removes the *class* of failure (an analyzer rule against `.Result`, a load test in CI), and an owner and a date for each item.

## Files

| File | What it is |
|---|---|
| `runbook-template.md` | The runbook for "latency high, CPU low" — fill in the blanks for your service. |
| `timeline-template.md` | The incident log you keep while it happens. |
| `postmortem-template.md` | The blameless post-mortem. |
