# <Your Name> — .NET backend evidence portfolio

<One sentence: what you build, at what scale, and the kind of problems you want to work on next.>

Everything here is **practice or personal work, labelled as such**. Stories from my employers are not published; I am happy to talk about them in an interview.

## Evidence index

| Competency | Claim (one line, falsifiable) | Evidence | Type |
|---|---|---|---|
| Query performance | I can read an execution plan, fix a slow query, and prove the fix with numbers. | [`execution-plans/results.md`](execution-plans/results.md) | Lab |
| Reliable messaging | I can make a message pipeline produce exactly-once *effects* under duplicates, reordering and crashes. | [`idempotent-messaging/`](idempotent-messaging/) | Lab |
| Incident response | I can detect, mitigate and root-cause a production fault from telemetry alone, and write it up blamelessly. | [`incidents/`](incidents/) | Lab |
| Code review | I find the defects that matter, rank them by severity, and say so kindly. | [`code-review/`](code-review/) | Lab |
| Technical writing | I write decisions, designs and status updates that get read and acted on. | [`writing/`](writing/) | Lab |
| System design | I can design a system under time pressure and defend its trade-offs. | [`system-design/`](system-design/) | Lab |

Keep the claims falsifiable: "I can do X, and here is the proof" — never "passionate about X".

## How to read this repo

- Each folder has its own README: what the problem was, what I decided, and what the numbers were.
- Numbers come from the runs committed next to them. Hardware and versions are listed with each result.
- <Optional: one line on what you would do differently if you did it again.>
