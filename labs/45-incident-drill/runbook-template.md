# Runbook: latency high, CPU low — [service name]

**Owner:** [team] · **Last reviewed:** [date] · **Alert that links here:** [alert name and threshold]

**Severity guide**
- SEV2 if p95 > 1 s for 5 min on a customer-facing endpoint; SEV1 if errors > [x] % or checkout is affected.

**1. Declare and communicate (first 5 minutes)**
- [ ] Open the incident channel `[#inc-yyyymmdd-name]`; name one incident lead.
- [ ] Post the first status update to [support channel / status page]: what is affected, what is not, next update in 30 min.

**2. Mitigate before diagnosing**
- [ ] Was there a deployment in the last [2 h]? → roll back: `[command to redeploy the previous artifact]`
- [ ] Is a feature flag new or changed? → turn it off: `[flag tool / command]`
- [ ] Still bad after rollback? → scale out: `[command]` and continue to step 3.

**3. Diagnose (latency high, CPU low = waiting, not working)**
- [ ] Thread pool: `dotnet-counters monitor -p [pid] --counters System.Runtime` → `dotnet.thread_pool.queue.length` above 0 and the thread count climbing = starvation.
- [ ] Stacks: `dotnet-dump collect -p [pid]`, then `dotnet-dump analyze [file]` → `parallelstacks` → look for many threads in `Task.Wait` / `.Result` / `Monitor.Enter`.
- [ ] Dependencies: [query for slow dependency calls in your APM, e.g. KQL on `dependencies` by target, p95 duration]
- [ ] Connection pools: [counter or log line that shows pool exhaustion]

**4. Recover and verify**
- [ ] p95 back under [threshold] for 15 min on the dashboard `[link]`.
- [ ] Final status update; close the incident; schedule the post-mortem within 48 h.
