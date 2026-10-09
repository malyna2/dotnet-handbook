// Chapter 6, "React by Its Mechanism" and "XSS from the Front End", rendered in jsdom.
import { act, fireEvent, render, screen } from "@testing-library/react";
import { StrictMode } from "react";
import { OrderList } from "./snippets/components";
import { Quantity } from "./snippets/state-snapshot";
import { Profile } from "./snippets/hooks-order";
import { ChatRoom } from "./snippets/effect-cleanup";
import { Basket } from "./snippets/keys";
import { Comment, CommentUnsafe } from "./snippets/xss";

afterEach(() => vi.unstubAllGlobals());

test("props flow down, events flow up through a callback", () => {
  const cancelled: number[] = [];
  const orders = [{ id: 7, customer: "Ada", total: 12 }, { id: 9, customer: "Lin", total: 3.5 }];
  render(<OrderList orders={orders} onCancel={(id) => cancelled.push(id)} />);
  expect(screen.getAllByRole("listitem").map((li) => li.textContent)).toEqual([
    "Ada: 12.00Cancel", "Lin: 3.50Cancel",
  ]);
  fireEvent.click(screen.getAllByRole("button", { name: "Cancel" })[1]);
  expect(cancelled).toEqual([9]);
});

test("an empty list renders the fallback", () => {
  render(<OrderList orders={[]} onCancel={() => {}} />);
  expect(screen.getByText("No orders yet.")).toBeTruthy();
});

test("state is a snapshot per render; updater functions queue", () => {
  render(<Quantity />);
  fireEvent.click(screen.getByRole("button", { name: "+3 (wrong)" }));
  expect(screen.getByRole("status").textContent).toBe("1");
  fireEvent.click(screen.getByRole("button", { name: "+3" }));
  expect(screen.getByRole("status").textContent).toBe("4");
});

test("a hook after an early return breaks React's call-order bookkeeping", () => {
  vi.spyOn(console, "error").mockImplementation(() => {});
  const first = render(<Profile user={null} />);
  expect(() => first.rerender(<Profile user={{ name: "Ada" }} />)).toThrow(/more hooks than during the previous render/);
  const second = render(<Profile user={{ name: "Ada" }} />);
  expect(() => second.rerender(<Profile user={null} />)).toThrow(/fewer hooks than expected/);
  vi.mocked(console.error).mockRestore();
});

test("an effect re-runs when a dependency changes, after cleaning up the previous run", () => {
  const log: string[] = [];
  vi.stubGlobal("createConnection", (room: string) => ({
    connect: () => log.push(`connect ${room}`),
    disconnect: () => log.push(`disconnect ${room}`),
  }));
  const { rerender, unmount } = render(<ChatRoom roomId="general" />);
  rerender(<ChatRoom roomId="general" />);   // same dependency: the effect does not run again
  rerender(<ChatRoom roomId="travel" />);
  unmount();
  expect(log).toEqual(["connect general", "disconnect general", "connect travel", "disconnect travel"]);
});

test("StrictMode runs setup, cleanup, setup once more in development", () => {
  const log: string[] = [];
  vi.stubGlobal("createConnection", (room: string) => ({
    connect: () => log.push(`connect ${room}`),
    disconnect: () => log.push(`disconnect ${room}`),
  }));
  render(<StrictMode><ChatRoom roomId="general" /></StrictMode>);
  expect(log).toEqual(["connect general", "disconnect general", "connect general"]);
});

test("index keys hand a removed row's state to its neighbour; stable keys do not", () => {
  const items = [{ id: 1, name: "Tea" }, { id: 2, name: "Milk" }];
  for (const keyByIndex of [true, false]) {
    const { rerender, unmount } = render(<Basket items={items} keyByIndex={keyByIndex} />);
    fireEvent.change(screen.getByLabelText("Note for Tea"), { target: { value: "green" } });
    rerender(<Basket items={items.slice(1)} keyByIndex={keyByIndex} />);   // remove Tea
    const milkNote = (screen.getByLabelText("Note for Milk") as HTMLInputElement).value;
    expect(milkNote).toBe(keyByIndex ? "green" : "");
    unmount();
  }
});

test("JSX text is escaped; dangerouslySetInnerHTML parses markup", () => {
  const attack = `<img src="x" onerror="alert(1)">`;
  const { container: safe } = render(<Comment text={attack} />);
  expect(safe.querySelector("img")).toBeNull();
  expect(safe.textContent).toBe(attack);

  const { container: unsafe } = render(<CommentUnsafe html={attack} />);
  expect(unsafe.querySelector("img")).not.toBeNull();
});

test("React 19 does not render a javascript: URL into href", () => {
  vi.spyOn(console, "error").mockImplementation(() => {});
  const { container } = render(<a href={"javascript:alert(1)"}>profile</a>);
  const href = container.querySelector("a")!.getAttribute("href") ?? "";
  expect(href.startsWith("javascript:alert")).toBe(false);
  expect(href).toContain("React has blocked a javascript: URL");
  vi.mocked(console.error).mockRestore();
  void act;
});
