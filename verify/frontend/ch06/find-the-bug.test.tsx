// Chapter 6, Exercises: each test shows the defect on the printed code and its absence on the fix.
import { act, render, screen } from "@testing-library/react";
import { ProductSearch as Buggy } from "./find-the-bug/ProductSearch";
import { ProductSearch as Fixed } from "./find-the-bug/ProductSearch.fixed";
import * as buggySave from "./find-the-bug/saveAll.js";
import * as fixedSave from "./find-the-bug/saveAll.fixed.js";

type Product = { id: number; name: string };

// A fake API whose responses the test releases in any order; it honours AbortSignal like fetch.
function controllableFetch() {
  const pending = new Map<string, (r: Response) => void>();
  vi.stubGlobal("fetch", (url: string, init?: RequestInit) => new Promise<Response>((resolve, reject) => {
    const q = new URL(url, "http://localhost").searchParams.get("q")!;
    pending.set(q, resolve);
    init?.signal?.addEventListener("abort", () => reject(new DOMException("aborted", "AbortError")));
  }));
  return async (q: string, body: Product[] | null, status = 200) => {
    await act(async () => {
      pending.get(q)!(new Response(body ? JSON.stringify(body) : "{}", { status }));
    });
  };
}

afterEach(() => vi.unstubAllGlobals());

describe("ProductSearch: the slow response that wins", () => {
  for (const [name, Component, expected] of [
    ["printed code shows results for the OLD query", Buggy, "Lapel pin"],
    ["fix shows results for the query on screen", Fixed, "Laptop stand"],
  ] as const) {
    test(name, async () => {
      const respond = controllableFetch();
      const { rerender } = render(<Component query="lap" />);
      rerender(<Component query="laptop" />);
      await respond("laptop", [{ id: 2, name: "Laptop stand" }]);
      await respond("lap", [{ id: 1, name: "Lapel pin" }]);   // the earlier request finishes last
      expect(screen.getAllByRole("listitem").map((li) => li.textContent)).toEqual([expected]);
    });
  }

  test("printed code crashes the page on an error response; the fix shows the error", async () => {
    vi.spyOn(console, "error").mockImplementation(() => {});
    vi.spyOn(console, "warn").mockImplementation(() => {});
    try {
      // An error response with a JSON body (ProblemDetails, say) parses fine and is not an array.
      let respond = controllableFetch();
      const { container } = render(<Buggy query="x" />);
      await expect(respond("x", null, 500)).rejects.toThrow(/results.map is not a function/);
      expect(container.innerHTML).toBe("");      // no error boundary: React unmounted the tree

      respond = controllableFetch();
      render(<Fixed query="y" />);
      await respond("y", null, 500);
      expect(screen.getByRole("alert").textContent).toBe("Search failed: 500");
    } finally {
      vi.mocked(console.error).mockRestore();
      vi.mocked(console.warn).mockRestore();
    }
  });
});

describe("saveAll: the save that said done", () => {
  function api(failOn?: string) {
    const done: string[] = [];
    return {
      done,
      save: (line: string) => new Promise<void>((resolve, reject) => setTimeout(() => {
        if (line === failOn) reject(new Error(`line ${line} rejected`));
        else { done.push(line); resolve(); }
      }, 20)),
    };
  }

  test("printed code reports success before anything is saved", async () => {
    const a = api();
    const toasts: string[] = [];
    await buggySave.onSaveClicked(["a", "b"], a, (t: string) => toasts.push(`${t} (saved so far: ${a.done.length})`));
    expect(toasts).toEqual(["Saved 2 lines (saved so far: 0)"]);
  });

  test("printed code reports success when a save fails, and the failure is unhandled", async () => {
    const errors: unknown[] = [];
    const saved = process.listeners("unhandledRejection");
    process.removeAllListeners("unhandledRejection");
    process.on("unhandledRejection", (e) => errors.push(e));
    try {
      const toasts: string[] = [];
      await buggySave.onSaveClicked(["a", "b"], api("b"), (t: string) => toasts.push(t));
      await new Promise((r) => setTimeout(r, 60));
      expect(toasts).toEqual(["Saved 2 lines"]);
      expect(errors).toHaveLength(1);
    } finally {
      process.removeAllListeners("unhandledRejection");
      for (const l of saved) process.on("unhandledRejection", l);
    }
  });

  test("fix waits for every save and reports the failure", async () => {
    const ok = api();
    const toasts: string[] = [];
    await fixedSave.onSaveClicked(["a", "b"], ok, (t: string) => toasts.push(`${t} (saved: ${ok.done.length})`));
    await fixedSave.onSaveClicked(["a", "b"], api("b"), (t: string) => toasts.push(t));
    expect(toasts).toEqual(["Saved 2 lines (saved: 2)", "Save failed: line b rejected"]);
  });
});
