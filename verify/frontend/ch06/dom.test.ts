// Chapter 6, "The DOM and Events", the forms section and the storage section, run in jsdom.
import { logsOf } from "../logs";

afterEach(() => { document.body.innerHTML = ""; vi.unstubAllGlobals(); });

test("a click handler changes the live tree, not the HTML text", async () => {
  document.body.innerHTML = `<button id="save">Save</button><p id="status"></p>`;
  await import("./snippets/dom-basics.js");
  (document.querySelector("#save") as HTMLButtonElement).click();
  expect(document.querySelector("#status")!.textContent).toBe("Saving...");
});

test("one delegated listener handles rows added after it was attached", async () => {
  const cancelled: number[] = [];
  vi.stubGlobal("cancelOrder", (id: number) => cancelled.push(id));
  document.body.innerHTML = `<ul id="orders"><li>#1 <button data-order-id="1">Cancel</button></li></ul>`;
  await import("./snippets/delegation.js");

  document.querySelector("#orders")!.insertAdjacentHTML("beforeend",
    `<li>#2 <button data-order-id="2"><span>Cancel</span></button></li>`);
  (document.querySelector("[data-order-id='2'] span") as HTMLElement).click();   // inner element
  (document.querySelector("[data-order-id='1']") as HTMLElement).click();
  (document.querySelector("#orders li") as HTMLElement).click();                 // not a button
  expect(cancelled).toEqual([2, 1]);
});

test("bubbling: the event visits the target, then each ancestor; stopPropagation ends it", () => {
  document.body.innerHTML = `<div id="outer"><button id="inner">x</button></div>`;
  const seen: string[] = [];
  document.querySelector("#outer")!.addEventListener("click", () => seen.push("outer"));
  document.querySelector("#outer")!.addEventListener("click", () => seen.push("outer-capture"), { capture: true });
  document.querySelector("#inner")!.addEventListener("click", () => seen.push("inner"));
  (document.querySelector("#inner") as HTMLElement).click();
  expect(seen).toEqual(["outer-capture", "inner", "outer"]);

  seen.length = 0;
  document.querySelector("#inner")!.addEventListener("click", (e) => e.stopPropagation());
  (document.querySelector("#inner") as HTMLElement).click();
  expect(seen).toEqual(["outer-capture", "inner"]);
});

test("a <button> inside a form submits it unless it says type=button", () => {
  document.body.innerHTML = `<form><button id="a">Add line</button><button id="b" type="button">Add line</button></form>`;
  expect((document.querySelector("#a") as HTMLButtonElement).type).toBe("submit");
  expect((document.querySelector("#b") as HTMLButtonElement).type).toBe("button");
  let submits = 0;
  document.querySelector("form")!.addEventListener("submit", (e) => { e.preventDefault(); submits++; });
  (document.querySelector("#a") as HTMLButtonElement).click();
  (document.querySelector("#b") as HTMLButtonElement).click();
  expect(submits).toBe(1);
});

test("submit handler: preventDefault, FormData from named fields, fetch, show the outcome", async () => {
  let sent: FormData | undefined;
  vi.stubGlobal("fetch", async (_url: string, init: RequestInit) => {
    sent = init.body as FormData;
    return new Response(null, { status: 422 });
  });
  document.body.innerHTML = `
    <form id="signup" action="/api/signup" method="post">
      <input name="email" value="a@example.com"><input id="no-name" value="ignored">
      <button>Sign up</button><output></output>
    </form>`;
  await import("./snippets/form-submit.js");
  (document.querySelector("button") as HTMLButtonElement).click();
  await vi.waitFor(() => expect(document.querySelector("output")!.textContent).toBe("Failed: 422"));
  expect([...sent!.keys()]).toEqual(["email"]);
});

test("localStorage stores strings only", async () => {
  expect(await logsOf(() => import("./snippets/storage.js"))).toEqual(["[object Object]", 2]);
});
