// Chapter 6, "JavaScript the Way Interviews Test It": each snippet prints what its comments say.
import { logsOf } from "../logs";

test("coercion and equality", async () => {
  expect(await logsOf(() => import("./snippets/coercion.js"))).toEqual([
    "12", 12, true, true, false, true, false, false, "object",
  ]);
});

test("|| versus ?? and optional chaining", async () => {
  expect(await logsOf(() => import("./snippets/nullish.js"))).toEqual([20, 0, "Untitled", undefined]);
});

test("var shares one binding, let makes one per iteration", async () => {
  expect(await logsOf(() => import("./snippets/closures.js"))).toEqual([[3, 3, 3], [0, 1, 2]]);
});

test("this depends on the call, bind fixes it, arrows inherit it", async () => {
  expect(await logsOf(() => import("./snippets/this.js"))).toEqual([1, "TypeError", 2, [3, 4]]);
});

test("classes are functions with a prototype chain looked up at call time", async () => {
  expect(await logsOf(() => import("./snippets/prototypes.js"))).toEqual([
    "function", true, [true, false], "Rex was patched",
  ]);
});

test("sequential awaits add up, Promise.all overlaps", async () => {
  expect(await logsOf(() => import("./snippets/promises.js"))).toEqual([true, true]);
});

test("Promise.all rejects on the first failure; allSettled reports every outcome", async () => {
  const slowFinished: string[] = [];
  const slow = new Promise((r) => setTimeout(() => { slowFinished.push("slow"); r("slow"); }, 30));
  await expect(Promise.all([slow, Promise.reject(new Error("fast failure"))])).rejects.toThrow("fast failure");
  expect(slowFinished).toEqual([]);   // all() settled while the other promise was still running
  const results = await Promise.allSettled([slow, Promise.reject(new Error("x"))]);
  expect(results.map((r) => r.status)).toEqual(["fulfilled", "rejected"]);
  expect(slowFinished).toEqual(["slow"]);
});

test("synchronous code, then every microtask, then the next task", async () => {
  const logs = await logsOf(() => import("./snippets/event-loop.js"), 20);
  expect(logs.map((l) => String(l).split(":")[0])).toEqual(["1", "2", "3", "4", "5", "6"]);
});
