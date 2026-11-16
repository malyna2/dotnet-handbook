# Lab kit — Chapter 46, Labs 3 and 4: Thread Pool Starvation and a Memory Leak

One small ASP.NET Core service with two production bugs: `GET /quote` blocks on `.Result` (Lab 3), and `POST /watchers` subscribes a per-request object to a static event and never unsubscribes (Lab 4). You find each one with the tools you would use in production, `dotnet-counters` and `dotnet-gcdump`, before you fix it.

> **The portfolio rule.** Your fix, your counter output and your heap comparison go in **your own public portfolio repo**, not here.

**Last verified:** 2026-11-05 — Ubuntu 24.04 container, 4 vCPU, 15 GB RAM, .NET SDK 10.0.114, dotnet-counters and dotnet-gcdump 10.0.745401 (local tools, pinned in `.config/dotnet-tools.json`). `verify.sh`: the starter fails both tests, the solution passes both; the tool output for each variant is in [`reference-runs/`](reference-runs/).

## Run it

```bash
dotnet tool restore
dotnet build src/DiagLab.Api -c Release                       # starter by default; -p:LabVariant=solution for the reference
dotnet src/DiagLab.Api/bin/Release/net10.0/DiagLab.Api.dll --urls http://localhost:5080 &
PID=$!

# Lab 3: watch the pool while you load it
dotnet tool run dotnet-counters monitor -p $PID --counters System.Runtime &
dotnet run --project src/DiagLab.Load -c Release -- "http://localhost:5080/quote?sku=abc" 400 100

# Lab 4: grow the heap, snapshot it, read the report
dotnet run --project src/DiagLab.Load -c Release -- http://localhost:5080/watchers 500 20 POST
dotnet tool run dotnet-gcdump collect -p $PID -o before.gcdump
dotnet tool run dotnet-gcdump report before.gcdump | head -20
```

`curl localhost:5080/stats` shows the subscriber count, the pool's thread count and queue, and the heap after a full collection.

`starter/Endpoints.cs` is the file you change. `dotnet test --project tests/DiagLab.Tests` checks it: 200 concurrent quotes must finish in under 3 seconds, and no watcher may stay subscribed after its request.

## Reference numbers (4 vCPU, Release, from `reference-runs/tools-*.txt`)

| | starter | solution |
|---|---|---|
| 400 quotes, 100 concurrent: p50 / p95 | 2335 ms / 3978 ms | 104 ms / 154 ms |
| `dotnet.thread_pool.queue.length`, max during the load | 89 | 0 |
| pool threads afterwards (`/stats`) | 58 | 5 |
| GC heap after 500 `POST /watchers` (`dotnet-gcdump`) | 34,473,840 bytes | 1,442,726 bytes |
| `System.Byte[]` objects over 10 KB in the snapshot | 501 | 1 |
