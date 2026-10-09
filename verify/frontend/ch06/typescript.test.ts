// Chapter 6, "TypeScript for C# Developers" and "Talking to the API from the Browser".
// The compile-time claims are checked by `tsc --noEmit` (see verify.sh); the runtime ones here.
import { logsOf } from "../logs";
import { loadOrder } from "./snippets/load-order";

afterEach(() => vi.unstubAllGlobals());

test("structural typing: any value with the right shape fits", async () => {
  expect(await logsOf(() => import("./snippets/structural"))).toEqual([5, 10]);
});

test("discriminated unions narrow by their tag", async () => {
  expect(await logsOf(() => import("./snippets/unions"))).toEqual([
    "Order 7: 30", "Failed with 503",
  ]);
});

test("generic constraints tie the value type to the key", async () => {
  expect(await logsOf(() => import("./snippets/generics"))).toEqual([2]);
});

test("types are erased: `as` checks nothing, a type guard checks at run time", async () => {
  expect(await logsOf(() => import("./snippets/erasure"))).toEqual(["string", "19.901", false]);
});

test("compile-time claims in the generics snippet", () => {
  const users = [{ id: 1, email: "a@example.com" }];
  function firstBy<T, K extends keyof T>(items: T[], key: K, value: T[K]): T | undefined {
    return items.find((item) => item[key] === value);
  }
  // @ts-expect-error "emial" is not a key of the user type
  firstBy(users, "emial", "x");
  // @ts-expect-error id is a number
  firstBy(users, "id", "2");
  const p: { x: number; y: number } = { x: 1, y: 2 };
  // @ts-expect-error an object literal with an unknown property is rejected (excess property check)
  const q: { x: number; y: number } = { x: 1, y: 2, label: "A" };
  expect([p, q].length).toBe(2);
});

test("fetch resolves on a 404; loadOrder turns it into an error", async () => {
  vi.stubGlobal("fetch", async () => new Response(JSON.stringify({ title: "Not Found" }), { status: 404 }));
  const raw = await fetch("/api/orders/1");
  expect(raw.ok).toBe(false);                 // no exception from fetch itself
  await expect(loadOrder(1)).rejects.toThrow("Order 1 failed: 404");
});

test("loadOrder sends cookies and returns the parsed order", async () => {
  let init: RequestInit | undefined;
  vi.stubGlobal("fetch", async (_u: string, i: RequestInit) => {
    init = i;
    return new Response(JSON.stringify({ id: 1, total: 9 }), { status: 200 });
  });
  expect(await loadOrder(1)).toEqual({ id: 1, total: 9 });
  expect(init!.credentials).toBe("include");
});
