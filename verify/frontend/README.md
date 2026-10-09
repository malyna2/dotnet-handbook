# verify/frontend — the frontend chapters' code, run

Maintainer tooling for [Chapter 6: Frontend Essentials](../../chapters/106-frontend-essentials.md) and [Chapter 34: Frontend and Full-Stack in Depth](../../chapters/234-frontend-in-depth.md).

Every ```` ```javascript ````, ```` ```typescript ```` and ```` ```tsx ```` block in those chapters is printed from code in this folder:

- `check_samples.py` finds each block in a file under `ch06/` or `ch34/`, token for token (`//` comments and whitespace ignored). A block that can only run in a real browser or against a running server is listed in `NOT_RUN` with the reason, and a stale entry fails the check.
- `chNN/snippets/` holds the teaching samples; `chNN/*.test.ts(x)` runs them (Vitest, jsdom for the DOM) and asserts what the chapter says they print or do.
- `ch06/find-the-bug/` holds each *Find the bug* sample and its fix. The tests show the defect on the printed code and its absence on the fix, so the suite stays green and a regression in either direction turns it red.
- `npx tsc --noEmit` type-checks everything, so compile-time claims (excess property checks, `keyof` constraints, the Angular service and component) are checked too.

Run it all with `./verify.sh` (Node 22 and npm; it runs `npm ci` from the committed `package-lock.json`). Versions are pinned exactly in `package.json`: TypeScript 7.0.2, React 19.3.0, Angular 21.2.25, RxJS 7.8.2, TanStack Query 5.103.3, Vite 8.3.3, Vitest 5.0.1, jsdom 29.1.1. `.npmrc` sets `legacy-peer-deps` (npm 10.9 fails to resolve Vitest's optional peer set otherwise) and `engine-strict`.

**Not run here:** HTML and CSS blocks (no layout engine in jsdom), C# blocks (the SPA fallback routing in Chapter 6 was checked once against ASP.NET Core 10 with a throwaway app, not kept), and the blocks listed in `NOT_RUN`. jsdom is not a browser: it shows DOM behaviour, event order and React's rendering, never timing, layout or paint.

**Last verified:** 2026-10-09. Environment: Ubuntu 24.04 container, 4 vCPU, 15 GB RAM, Node 22.22.0, npm 10.9.4.
