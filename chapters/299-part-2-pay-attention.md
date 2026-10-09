# Part 2 · Pay Attention To

> **What this page is for.** The traps behind the most common wrong answers in Part 2, one row each: what the trap looks like, the mechanism that makes it bite, and the fix. The chapter in the first column explains the row in full.

| Chapter | Trap | Why it bites | The fix |
|---|---|---|---|
| [17](#chapter-17-runtime-internals-and-performance) | `SetMinThreads` as the starvation fix | Below the minimum the pool injects at once, so the graphs look healthy, but every call still blocks and the outage returns at a higher load. | Stop blocking; keep a raised minimum only as a measured stopgap. |
| [17](#chapter-17-runtime-internals-and-performance) | `SemaphoreSlim.Wait` or `Thread.Sleep` on the pool | They don't report blocking to the pool, so only the starvation detector adds threads: at most one per 500 ms. | `WaitAsync` and async I/O. |
| [17](#chapter-17-runtime-internals-and-performance) | A 100 KB buffer per request | Arrays of 85,000 bytes or more go to the large object heap, collected only with gen 2. | Stream, or rent from `ArrayPool<T>`. |
| [18](#chapter-18-data-in-depth) | A lost update under Read Committed | Both transactions read committed data and both write; Repeatable Read only turns it into serialization or deadlock errors. | An atomic conditional update, a concurrency token, or `FOR UPDATE`. |
| [18](#chapter-18-data-in-depth) | A query that got slow without a deploy | Stale statistics, or a cached plan built for a different parameter value. | Compare estimated and actual rows; an index that is right for every value; fix the statistics before the query. |
| [20](#chapter-20-distributed-systems) | Retries at every layer | 4 × 4 × 4 = 64 calls per user action; jitter spreads them but doesn't reduce them. | Retry at one layer, behind a circuit breaker and a total timeout. |
| [20](#chapter-20-distributed-systems) | Purging the inbox before dead-lettered messages are resubmitted | The duplicate looks new and the effect runs again. | Size retention to the longest way back; natural keys and downstream idempotency keys. |
| [21](#chapter-21-architecture) | Services split over a shared database | A distributed monolith: every deploy is coupled, and three services at 99.9% in a synchronous chain give about 99.7%. | Each service owns its data and publishes events; start with a modular monolith. |
| [21](#chapter-21-architecture) | "Adding a field or an enum value is safe" | Strict generated clients reject what they don't know. | Know your readers; expand and contract, with consumer telemetry. |
| [25](#chapter-25-observability-and-testing-at-scale) | Alerting on "error rate > X% for 5 minutes", or on CPU | It isn't tied to the SLO: blips page, and a slow, steady burn never does. | Page on error-budget burn rate over a long and a short window. |
| [25](#chapter-25-observability-and-testing-at-scale) | A ratio sampler set directly | Each service ignores the caller's decision, so traces have holes. | `ParentBasedSampler` everywhere; tail sampling to keep the errors. |
| [25](#chapter-25-observability-and-testing-at-scale) | A load test with a fixed number of virtual users | The closed model slows down with the server, so the latency of queued requests is never measured. | An arrival-rate (open-model) executor; check the achieved rate. |
| [29](#chapter-29-azure-in-depth-for-net-developers) | Scaling out to "fix" SNAT timeouts | Each instance adds 128 ports per destination, so the problem hides until traffic grows again. | Reuse connections; private endpoints; a NAT gateway. |
| [29](#chapter-29-azure-in-depth-for-net-developers) | Unbounded Functions consumers | Instances × `maxConcurrentCalls` overwhelms the database behind them. | Cap both and size their product against the downstream limit. |
| [29](#chapter-29-azure-in-depth-for-net-developers) | "We can always redeploy the previous build" | A changed schema, contract or setting makes the old build fail too. | Expand then contract; release behind flags; rehearse the rollback. |
| [36](#chapter-36-senior-behaviours-career-and-interviews) | The sum of the most-likely estimates | Skew, correlated overruns and forgotten tasks all push the real effort up. | PERT plus the outside view; a range and a commitment level. |
| [36](#chapter-36-senior-behaviours-career-and-interviews) | AI-written "correct" tests on legacy code | They assert the behaviour someone believes, not the behaviour the code has. | Characterization tests first; read every assertion. |
