# Post-mortem: [title] — [date]

**Status:** draft / reviewed · **Severity:** SEV[n] · **Duration:** [detection → recovery] · **Authors:** [roles]

This review is blameless: it asks how the system let the failure through, not who made it.

**Summary.** [Two or three sentences a manager can read: what users saw, for how long, and what fixed it.]

**Impact.** [Requests affected, error rate, latency, customers, money — measured, with the query that measured it.]

**Timeline.** [Link to the incident log, plus the five key times: start, detection, mitigation, recovery, root cause found.]

**Root cause.** [The mechanism, e.g.: an endpoint blocked on `.Result`, so each request held a thread-pool thread for the full downstream call; under load the pool queued work faster than it injected threads.]

**Contributing factors.** [Why it reached production: no load test, no analyzer rule, the code review checklist did not cover it, the alert fired 7 minutes after impact began.]

**What went well.** [Rollback took [n] minutes; the alert was on a symptom users feel.]

**What went badly.** [Detection took [n] minutes; the runbook lacked the counter name.]

**Action items.**

| Action | Removes the class of failure? | Owner | Due |
|---|---|---|---|
| [Enable analyzer rule VSTHRD002 (Microsoft.VisualStudio.Threading.Analyzers) against synchronous waits in CI] | yes | [role] | [date] |
| [Add a load test at 2× peak to the release pipeline] | yes | [role] | [date] |
| [Add the thread-pool queue length to the service dashboard and runbook] | detection | [role] | [date] |
