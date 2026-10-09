// Chapter 6, "How Angular differs": signals and the RxJS search pipeline, run outside a browser.
// The DI/component snippet (snippets/angular-di.ts) is type-checked only: running it needs the
// Angular compiler and a bootstrapped app.
import { logsOf } from "../logs";

test("computed signals recompute lazily, on read", async () => {
  expect(await logsOf(() => import("./snippets/signals"))).toEqual([19, 28.5]);
});

test("debounce, distinct, and switchMap dropping a stale response", async () => {
  vi.useFakeTimers();
  const pending = new Map<string, (r: { id: number; name: string }[]) => void>();
  const requested: string[] = [];
  const rendered: string[][] = [];
  vi.stubGlobal("searchApi", (term: string) => {
    requested.push(term);
    return new Promise((resolve) => pending.set(term, resolve));
  });
  vi.stubGlobal("render", (r: { name: string }[]) => rendered.push(r.map((p) => p.name)));
  const { searchTerms } = await import("./snippets/rxjs-search");

  searchTerms.next("l"); searchTerms.next("la"); searchTerms.next("lap");
  await vi.advanceTimersByTimeAsync(300);
  expect(requested).toEqual(["lap"]);              // one request after the pause

  searchTerms.next("lap");                          // same term again: ignored
  await vi.advanceTimersByTimeAsync(300);
  searchTerms.next("laptop");
  await vi.advanceTimersByTimeAsync(300);
  expect(requested).toEqual(["lap", "laptop"]);

  pending.get("laptop")!([{ id: 2, name: "Laptop stand" }]);
  await vi.advanceTimersByTimeAsync(0);
  pending.get("lap")!([{ id: 1, name: "Lapel pin" }]);   // the slow, stale answer arrives last
  await vi.advanceTimersByTimeAsync(0);
  expect(rendered).toEqual([["Laptop stand"]]);
  vi.useRealTimers();
  vi.unstubAllGlobals();
});
