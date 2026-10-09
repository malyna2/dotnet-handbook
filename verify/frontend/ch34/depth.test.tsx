// Chapter 34: hydration mismatch, server-state caching with TanStack Query, and code splitting.
import { act, render, screen, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { hydrateRoot } from "react-dom/client";
import { renderToString } from "react-dom/server";
import type { ReactNode } from "react";
import { LastUpdated } from "./snippets/hydration";
import { CancelButton, OrderCount } from "./snippets/orders-query";
import { App } from "./snippets/lazy-route";

afterEach(() => { vi.useRealTimers(); vi.unstubAllGlobals(); });

test("hydration: server HTML that the client renders differently is a recoverable error", async () => {
  vi.useFakeTimers({ toFake: ["Date"] });
  vi.setSystemTime(new Date("2026-10-09T10:00:00Z"));
  const container = document.createElement("div");
  container.innerHTML = renderToString(<LastUpdated />);          // "the server"
  expect(container.textContent).toBe("Updated at 2026-10-09T10:00:00.000Z");

  vi.setSystemTime(new Date("2026-10-09T10:00:02Z"));              // the client runs later
  const errors: unknown[] = [];
  vi.spyOn(console, "error").mockImplementation(() => {});
  await act(async () => {
    hydrateRoot(container, <LastUpdated />, { onRecoverableError: (e) => errors.push(e) });
  });
  expect(errors.length).toBeGreaterThan(0);
  expect(String(errors[0])).toMatch(/[Hh]ydrat/);
  expect(container.textContent).toBe("Updated at 2026-10-09T10:00:02.000Z");   // client value wins
  vi.mocked(console.error).mockRestore();
});

function withClient(children: ReactNode, client: QueryClient) {
  return <QueryClientProvider client={client}>{children}</QueryClientProvider>;
}

test("one query key, one request: two components share the cached server state", async () => {
  let calls = 0;
  vi.stubGlobal("fetch", async () => { calls++; return new Response(JSON.stringify([{ id: 1, status: "open" }])); });
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(withClient(<><OrderCount /><OrderCount /></>, client));
  await waitFor(() => expect(screen.getAllByText("1 orders")).toHaveLength(2));
  expect(calls).toBe(1);

  render(withClient(<OrderCount />, client));            // mounts later, within staleTime
  expect(screen.getAllByText("1 orders")).toHaveLength(3);
  expect(calls).toBe(1);                                  // served from cache, no refetch
});

test("a mutation that invalidates the key refetches the list", async () => {
  const orders = [{ id: 1, status: "open" }, { id: 2, status: "open" }];
  const seen: string[] = [];
  vi.stubGlobal("fetch", async (url: string, init?: RequestInit) => {
    seen.push(`${init?.method ?? "GET"} ${url}`);
    if (url.endsWith("/cancel")) { orders.pop(); return new Response(null, { status: 204 }); }
    return new Response(JSON.stringify(orders));
  });
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(withClient(<><OrderCount /><CancelButton id={2} /></>, client));
  await screen.findByText("2 orders");
  screen.getByRole("button", { name: "Cancel" }).click();
  await screen.findByText("1 orders");
  expect(seen).toEqual(["GET /api/orders", "POST /api/orders/2/cancel", "GET /api/orders"]);
});

test("React.lazy fetches the page's module only when it is first rendered", async () => {
  const flag = globalThis as { reportsPageLoaded?: boolean };
  delete flag.reportsPageLoaded;
  const { rerender } = render(<App route="/" />);
  expect(screen.getByText("Home")).toBeTruthy();
  expect(flag.reportsPageLoaded).toBeUndefined();

  rerender(<App route="/reports" />);
  expect(screen.getByText("Loading…")).toBeTruthy();       // Suspense fallback while it loads
  expect(await screen.findByRole("heading", { name: "Reports" })).toBeTruthy();
  expect(flag.reportsPageLoaded).toBe(true);
});
