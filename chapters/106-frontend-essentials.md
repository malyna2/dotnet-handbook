# Chapter 6: Frontend Essentials

The moment your API meets a browser, problems land on your desk that you cannot hand off: why the SPA works locally and fails in production, why a request "blocked by CORS" still changed the database, why the page freezes while your endpoint answers in 40 ms, where the login token should live. Most .NET teams ship a React or Angular frontend next to their API, and their interviews ask backend candidates frontend questions. This chapter gives you a junior-plus foundation: enough to read and change a SPA, debug it in the browser's DevTools, answer the standard questions by their mechanism, and design the API boundary so the frontend team can move fast.

It follows the path a page takes: how the browser turns HTML, CSS and JavaScript into pixels; each language in turn, with JavaScript the way interviews test it; the DOM and events; TypeScript as a C# developer meets it. Then the boundary you own (`fetch`, CORS, cookies, typed clients), React by its mechanism and how Angular differs, routing and forms, accessibility, the npm and Vite toolchain, XSS, and the Core Web Vitals that measure the result.

Every JavaScript and TypeScript sample here runs: the repository tests them in `verify/frontend/` (pinned TypeScript, React and Angular, Vitest with jsdom). Rendering strategies, performance and testing in depth, Content Security Policy and SPA authentication are in [Chapter 34: Frontend and Full-Stack in Depth](#chapter-34-frontend-and-full-stack-in-depth).

## The Web the Browser Sees

Three languages run in every browser, and they have distinct jobs.

**HTML** is the document structure: a tree of nested elements (`<header>`, `<article>`, `<button>`). **CSS** is presentation: selectors match elements and apply styling rules (`color`, `display: flex`, `grid-template-columns`). **JavaScript** is behaviour: it runs code, reacts to user input, and changes the page.

When the browser parses HTML, it builds the **DOM** (Document Object Model), an in-memory tree of objects representing the document. JavaScript does not edit your HTML text; it manipulates this live tree:

```javascript
const btn = document.querySelector("#save");
btn.addEventListener("click", () => {
  document.querySelector("#status").textContent = "Saving...";
});
```

Every visible change on a web page is ultimately a DOM mutation; React, Angular and Blazor are machines for deciding *which* mutations to make.

### From Bytes to Pixels: the Rendering Pipeline

The browser turns a response into pixels in a fixed sequence, and most "the page is slow" problems are one step waiting for another.

```text
 HTML ──► DOM ───┐
                 ├──► render tree ──► layout ──► paint ──► composite
 CSS ───► CSSOM ─┘
 <script>: may pause the HTML parser; runs on the same main thread as all of it
```

1. **HTML → DOM**, incrementally, as bytes arrive. A *preload scanner* reads ahead and starts downloading the stylesheets, scripts and images it finds.
2. **CSS → CSSOM**, not incrementally: a later rule can override an earlier one, so nothing can be styled until all the CSS is in. **CSS is render-blocking.**
3. **Render tree**: DOM plus CSSOM, only what is displayed (`display: none` is left out; `visibility: hidden` still takes space).
4. **Layout** (*reflow*) computes every box's size and position; one element's size change can move everything after it.
5. **Paint** fills in each layer's pixels; **composite** stacks the layers on screen. `transform` and `opacity` changes can often skip straight to compositing, which is why smooth animations use them.

**What blocks what.** A classic `<script src>` **blocks the parser**: the browser stops building the DOM, downloads and runs the script, then continues, because the script might read or write the DOM built so far. A script may also ask for computed styles, so it waits for the CSS before it. CSS blocks scripts; scripts block parsing:

```html
<script src="/legacy-widget.js"></script>          <!-- blocks parsing until run -->
<script defer src="/app.js"></script>               <!-- runs after parsing, in order -->
<script async src="/analytics.js"></script>         <!-- runs on arrival, any order -->
<script type="module" src="/assets/index-4f9a1c.js"></script>  <!-- deferred by default -->
```

`defer` scripts run after parsing, in document order, before `DOMContentLoaded`; `async` scripts run whenever they arrive. Use `defer` (or a module script, which is what Vite emits) for application code, `async` for independent scripts such as analytics, and a plain `<script>` in the `<head>` almost never.

**After the first paint, the pipeline re-runs on every change**, batched: ten DOM writes in a row cost one layout before the next frame. The expensive pattern interleaves writes with reads of layout values:

```javascript
for (const row of rows) {
  row.style.width = container.offsetWidth + "px";   // read forces layout, write invalidates it
}
```

Each `offsetWidth` read after a write forces a *synchronous* layout, so 500 rows mean 500 layouts: **layout thrashing**. Read once outside the loop, then write.

> **Pay attention.** **Why a page can be "loaded" and still blank or frozen.** All of this, and all your JavaScript, runs on **one main thread**: while a script runs, nothing is parsed, laid out, painted or clicked. A render-blocking stylesheet on a slow CDN keeps the screen blank however fast your API is; a 300 ms loop makes every click in that window wait 300 ms. The fix: get work off the critical path (`defer`, less CSS, split bundles) and break long work into pieces.

### SPA vs. the Classic Request/Response

A **traditional web app** (Razor Pages, MVC) renders full HTML on the server for every navigation, and the browser runs the whole pipeline for each new page. A **Single-Page Application (SPA)** loads once, then takes over navigation: JavaScript intercepts clicks, fetches JSON from your API and re-renders parts of the DOM. React, Angular and Vue dominate. The upside is app-like fluidity; the cost is complexity, a large JavaScript download that must run before the user sees anything, and harder SEO. Server-side rendering, static generation and hydration sit in between ([Chapter 34](#chapter-34-frontend-and-full-stack-in-depth)).

## HTML: Structure, Semantics and Forms

The element you choose decides what the browser does for free: keyboard focus, form submission, screen-reader announcements, what a search engine indexes.

### Semantic Elements Carry Behaviour

A **semantic** element says what its content *is*, not how it looks:

- **Landmarks** (`<header>`, `<nav>`, `<main>`, `<aside>`, `<footer>`): screen-reader users jump between them the way you scan a page.
- **Headings** `<h1>`–`<h6>` form the outline; choose the level by structure, never by font size.
- **`<button>` versus `<a>`**: a link *goes somewhere* (an `href`, a new tab, a URL change); a button *does something*. A `<div>` with a click handler is neither: not focusable, deaf to Enter, announced as plain text.
- **`<ul>`/`<ol>`** for lists, **`<table>`** with `<th>` for tabular data only, **`<label>`/`<fieldset>`/`<legend>`** to tie text to form controls.

If everything is a `<div>`, the structure is gone.

### How a Form Works Without JavaScript

A `<form>` is already a complete client for your API, and what it does on its own explains both the bugs SPAs inherit and your server's model binding ([Chapter 5: Model Binding & Validation](#model-binding-validation)).

```html
<form id="signup" action="/api/signup" method="post">
  <label for="email">Email</label>
  <input id="email" name="email" type="email" required autocomplete="email">

  <button>Sign up</button>
  <output></output>
</form>
```

When the user submits, the browser:

1. **Validates** the markup constraints (`required`, `type="email"`, `min`/`max`, `pattern`). If one fails, it shows a message, focuses the field, and the `submit` event never fires.
2. **Collects** every enabled control with a **`name`** attribute. A field without `name` is silently not sent: the usual reason "the field is always null" in a model-bound action.
3. **Encodes** them: into the query string for `get`, as `application/x-www-form-urlencoded` for `post`, or `multipart/form-data` (needed for files).
4. **Navigates**: sends the request and replaces the page with the response.

> **Gotcha.** **A `<button>` inside a form is a submit button unless it says otherwise.** The default `type` is `submit`, so an "Add line" button written as `<button>Add line</button>` submits the form, which in a SPA means a full page reload and lost state. Write `type="button"` on every button that is not meant to submit.

A SPA keeps steps 1–3 and replaces step 4: cancel the navigation with `preventDefault()`, build the body with `FormData`, send it with `fetch`:

```javascript
const form = document.querySelector("#signup");

form.addEventListener("submit", async (event) => {
  event.preventDefault();                  // stop the browser's own full-page POST
  const body = new FormData(form);         // every field with a name attribute
  const res = await fetch(form.action, { method: "POST", body });
  form.querySelector("output").textContent = res.ok ? "Saved" : `Failed: ${res.status}`;
});
```

Don't set `Content-Type` yourself for a `FormData` body: the browser writes `multipart/form-data` *with the boundary* that separates the parts, and a header without it is unparseable on the server. And built-in validation is a convenience, never a defence; the server validates again.

## CSS: The Box Model, the Cascade and Layout

You will be asked why an element is wider than its `width`, why your style "doesn't apply", and how to put two things side by side: the box model, the cascade, and the layout modes.

### The Box Model

Every element renders as a rectangular box made of four layers, from the inside out: **content**, **padding** (inside the border, takes the background), **border**, and **margin** (transparent space outside the border).

By default (`box-sizing: content-box`), `width` is the *content* width only: `width: 200px; padding: 16px; border: 1px solid` is 234 pixels on screen. Hence the reset at the top of nearly every stylesheet, after which `width` includes padding and border:

```css
*, *::before, *::after { box-sizing: border-box; }
```

**Vertical margins collapse**: between two stacked blocks the gap is the larger margin, not the sum. And **`display`** decides how a box takes part in layout: `block` takes the full width and stacks, `inline` flows in a line of text and ignores `width`/`height`, `flex` and `grid` make the element a layout container.

### The Cascade and Specificity

Several rules often set the same property on the same element. The **cascade** is the algorithm that picks the winner, and it compares, in this order:

1. **Origin and importance.** Browser defaults lose to your stylesheets; a declaration marked `!important` beats normal ones (and reverses the origin order).
2. **Cascade layers.** Rules in `@layer` blocks lose to rules outside any layer, and later layers beat earlier ones, whatever the selectors say. This is how a design system keeps its base styles easy to override.
3. **Specificity** of the selector, below.
4. **Order of appearance.** If everything else ties, the rule that comes last wins.

**Specificity** is a three-part score, compared left to right like a version number: (number of **IDs**, number of **classes, attribute selectors and pseudo-classes**, number of **element types and pseudo-elements**).

| Selector | Score | Notes |
|---|---|---|
| `p` | (0, 0, 1) | one type |
| `.price` | (0, 1, 0) | one class beats any number of types |
| `ul li.price:hover` | (0, 2, 2) | class + pseudo-class, two types |
| `#cart .price` | (1, 1, 0) | one ID beats any number of classes |
| `style="color: red"` | beats all selectors | an inline style is not a selector |

"My CSS doesn't apply" is almost always a more specific rule elsewhere; DevTools' Styles panel lists every matching rule with the losers struck through. Inheritance is separate: `color` and `font-*` flow to children, `margin` and `width` don't, and any rule matching the element directly beats an inherited value.

> **Pitfall.** Fighting specificity with `!important` or longer selectors escalates. That war, plus CSS's single global namespace, is why teams use **scoped styles**: CSS Modules and Angular component styles rewrite class names so rules can't leak, Tailwind keeps every selector at one class, Blazor has CSS isolation. Find out which one a codebase uses before writing a rule.

### Flexbox and Grid

**Flexbox** lays children out along **one axis**: `justify-content` aligns along it, `align-items` across it, `gap` spaces them, and `flex: 1` on a child means "take the remaining space". **Grid** lays them out in **two dimensions**: columns and rows defined on the container, `fr` units as fractions of free space, and `repeat(auto-fill, minmax(…))` for as many columns as fit, with no media query.

```css
.toolbar {                      /* flexbox: one row, items centred vertically */
  display: flex;
  align-items: center;
  gap: 8px;
}
.toolbar .search { flex: 1; }   /* the search box takes the leftover width */

.products {                     /* grid: as many 220px+ columns as fit */
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(220px, 1fr));
  gap: 16px;
}
```

| You need | Use |
|---|---|
| Items in a row (toolbar, button group, nav), alignment within it | Flexbox |
| A whole-page shell: header, sidebar, main, footer | Grid |
| A responsive set of equal cards | Grid with `auto-fill` / `minmax` |
| Centre one thing in a box | Either: `display: grid; place-items: center` is the shortest |

**Responsive design** rests on the `<meta name="viewport" content="width=device-width, initial-scale=1">` tag (without it, phones render a desktop page shrunk), `rem` units, and mobile-first media queries (`@media (min-width: 768px)` adds to the small layout).

## JavaScript the Way Interviews Test It

JavaScript looks familiar to a C# developer, but under the syntax it is a different machine: dynamically typed, prototype-based, single-threaded. Interview questions aim at exactly the places where C# intuition gives the wrong answer.

### Types, Coercion and Equality

Seven primitive types (`number`, `string`, `boolean`, `undefined`, `null`, `bigint`, `symbol`) plus objects, which include arrays and functions. `number` is a 64-bit float like `double`, so money doesn't belong in it ([Chapter 15](#chapter-15-dates-money-and-strings)).

Values have types, variables don't, and operators **coerce** their operands to whatever type the operator needs:

```javascript
console.log(1 + "2");            // "12": + with a string concatenates
console.log("3" * "4");          // 12: * converts both to numbers
console.log(0 == "");            // true: "" becomes 0
console.log(0 == "0");           // true: "0" becomes 0
console.log("" == "0");          // false: two strings
console.log(null == undefined);  // true: a special rule
console.log(null == 0);          // false
console.log(NaN === NaN);        // false: use Number.isNaN
console.log(typeof null);        // "object": a historic bug
```

`==` converts the operands to a common type first, by rules so tangled that it isn't even transitive (`0 == ""` and `0 == "0"`, yet `"" != "0"`). `===` never converts. **Use `===` everywhere**; the one common exception is `x == null`, true for exactly `null` and `undefined`. Objects compare by reference under both, and there is no `Equals` override.

### Truthiness and the Nullish Operators

In a condition any value converts to a boolean, and exactly eight are **falsy**: `false`, `0`, `-0`, `0n`, `""`, `null`, `undefined`, `NaN`. Everything else is truthy, including `"0"`, `[]` and `{}`. So `if (count)` is a bug the day `count` is legitimately zero, and `||` is a trap for defaults:

```javascript
const settings = { pageSize: 0, title: "" };

console.log(settings.pageSize || 20);  // 20: 0 is falsy, so || throws it away
console.log(settings.pageSize ?? 20);  // 0: ?? replaces only null and undefined
console.log(settings.title || "Untitled");  // "Untitled"
console.log(settings.owner?.name);     // undefined: ?. stops at the missing owner
```

`??` and `?.` behave like their C# namesakes. Reach for `??` whenever zero or an empty string is a valid value.

### Scope and Closures

There are two generations of variable declaration:

- **`var`** is **function-scoped**: one variable for the whole function, *hoisted* to its top, so it exists (as `undefined`) before its declaration line.
- **`let`** and **`const`** are **block-scoped** like C# locals, and throw if used before their declaration. `const` fixes the *binding*, not the value: a `const` array can still be pushed to.

A **closure** is a function together with the variables it captured from the scope where it was created. Closures capture *variables*, not values, which is where `var` and `let` behave differently in a loop:

```javascript
const withVar = [];
for (var i = 0; i < 3; i++) withVar.push(() => i);
console.log(withVar.map((f) => f()));  // [3, 3, 3]: one i for the whole loop

const withLet = [];
for (let j = 0; j < 3; j++) withLet.push(() => j);
console.log(withLet.map((f) => f()));  // [0, 1, 2]: a fresh j per iteration
```

With `var` all three functions share one `i`, which is 3 when they run; `let` creates a binding per iteration. C# had the same trap with `foreach` until C# 5 ([Chapter 1: Closures and the Capture Trap](#closures-and-the-capture-trap)). Closures also give private state without a class: a local variable lives on as long as a function returned from its scope still uses it. Write `const` by default, `let` when you reassign, `var` never.

### `this` Is Decided by the Call

In JavaScript, `this` is an implicit parameter set by **how the function is called**, not where it is written:

- **`obj.method()`**: `this` is `obj`, the thing before the dot.
- **A plain call `fn()`**: `undefined` in strict mode, which classes and modules always use.
- **`new Fn()`**: `this` is the newly created object.
- **`fn.call(x)`, `fn.apply(x)`, `fn.bind(x)`**: `this` is `x`, explicitly; `bind` returns a new function with `this` fixed for good.
- **Arrow functions** have no `this` of their own; they capture the surrounding one.

```javascript
const cart = {
  items: 0,
  add() { this.items++; return this.items; },
  addLater() { return [1, 2].map(() => this.add()); },
};

console.log(cart.add());               // 1: called as cart.add(), so this is cart

const add = cart.add;                  // the function, detached from its object
try { add(); } catch (e) { console.log(e.constructor.name); }  // TypeError: this is undefined

console.log(cart.add.bind(cart)());    // 2: bind fixes this for good
console.log(cart.addLater());          // [3, 4]: arrow functions use the this of the code around them
```

The detached call is the real-world bug: passing `this.handleClick` or `service.load` as a callback detaches the method, and it fails later with "cannot read properties of undefined". Wrap it in an arrow function (`() => this.handleClick()`) or `bind` it.

### Prototypes and Classes

Each object has a hidden link to another object, its **prototype**; a property not found on the object is looked up along this **prototype chain** until found or `null`. The `class` syntax builds exactly that structure:

```javascript
class Animal {
  constructor(name) { this.name = name; }
  speak() { return `${this.name} makes a sound`; }
}

const rex = new Animal("Rex");
console.log(typeof Animal);                                // "function": a class is a function
console.log(Object.getPrototypeOf(rex) === Animal.prototype);  // true
console.log(Object.hasOwn(rex, "name"), Object.hasOwn(rex, "speak"));  // true false

Animal.prototype.speak = function () { return `${this.name} was patched`; };
console.log(rex.speak());   // "Rex was patched": the lookup happens at call time, on the chain
```

`name` lives on the instance; `speak` lives once, on `Animal.prototype`, shared by every instance, and `extends` links one prototype to the next. Because the lookup is dynamic, replacing a method on the prototype changes every existing object, there is no compile-time `virtual`/`override` contract, and a method's `this` is still decided by the call. Language-enforced privacy comes from `#private` fields.

### Promises and async/await

A **promise** is JavaScript's `Task`: *pending*, then settled exactly once, *fulfilled* with a value or *rejected* with an error. An `async` function returns a promise, and `await` resumes with the value or throws the rejection. The model transfers from [Chapter 4: Async Essentials](#chapter-4-async-essentials), with differences:

- **No thread pool.** Every continuation runs on the one main thread when the event loop gets to it (next section). `await` frees the thread while waiting; it never moves CPU work elsewhere.
- **Promises start immediately and can't be cancelled.** Calling an `async` function starts the work, like a hot `Task`. The `CancellationToken` equivalent is an `AbortController`, whose `signal` you pass to `fetch`.
- **`Promise.all` fails fast**: it rejects on the *first* rejection while the others keep running unobserved, whereas `Task.WhenAll` waits for every task. `Promise.allSettled` waits for all and reports each outcome.
- **A rejection nobody handles** becomes an *unhandled rejection*: a console error in the browser, a process crash by default in Node. The usual cause is a promise nobody awaited.

The same `await`-in-a-loop question as in C# comes up here:

```javascript
const delay = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

let start = Date.now();
await delay(100);
await delay(100);
console.log(Date.now() - start >= 200);   // true: the second wait starts after the first ends

start = Date.now();
await Promise.all([delay(100), delay(100)]);
console.log(Date.now() - start < 200);    // true: both waits run at the same time
```

Await in sequence only when a call needs the previous result.

### The Event Loop: Tasks and Microtasks

One thread, one call stack: everything that happens later (a timer, a response, a click) is queued, and the **event loop** decides what runs next, from two kinds of queue:

- **Tasks** (macrotasks): running a script, a `setTimeout` callback, a click, a worker message.
- **Microtasks**: promise callbacks (including the code after an `await`), `queueMicrotask`.

The rule: **run one task to completion; then every microtask, including ones queued meanwhile, until the queue is empty; then render if a frame is due; then the next task.**

```text
   ┌─► take ONE task ──► run to completion ──► drain ALL microtasks ──► render? ──┐
   └──────────────────────────────────────────────────────────────────────────────┘
```

So in this program the order is fixed:

```javascript
console.log("1: sync");
setTimeout(() => console.log("6: timer (a task)"), 0);
Promise.resolve().then(() => console.log("4: then (a microtask)"));

async function save() {
  console.log("2: save() up to its first await");
  await null;
  console.log("5: rest of save() (a microtask)");
}
save();
console.log("3: sync, after save() returned");
```

The script is one task, so 1, 2 and 3 run first; line 2 shows that an `async` function runs synchronously until its first `await`, like a C# one ([Chapter 4: What `await` Actually Does](#what-await-actually-does)). Then the microtasks, in queued order: 4, 5. The timer is a task, so it comes last even with a delay of 0.

> **Pay attention.** **Why "async" code still freezes the page.** `await` only helps while you are *waiting*. While JavaScript computes (sorting 50,000 rows, parsing a huge response), clicks, typing and rendering wait for the current task and all its microtasks to finish; and because the loop drains the whole microtask queue before rendering, a promise chain that keeps queueing microtasks blocks rendering like a `while` loop. Fixes: do less on the client (paginate on the server, usually your job); split long work into chunks that yield with a real task (`await new Promise(r => setTimeout(r, 0))`); or move it to a **Web Worker**, a separate thread without DOM access.

Races look different here: no two pieces of JavaScript run at once, so no torn writes or locks, but between two `await`s anything can run. The race is about *ordering*, as in the exercise at the end.

### Modules

Modern JavaScript files are **ES modules**: each has its own scope, runs in strict mode, and shares code through `export` and `import`. Static imports let tools see the whole dependency graph, so a bundler can drop unused code (*tree-shaking*); `import()` as a function loads a module on demand, the hook for code splitting ([Chapter 34](#chapter-34-frontend-and-full-stack-in-depth)). `require()` in older Node code is CommonJS, the original module system.

## The DOM and Events

The DOM API is how JavaScript reads and changes the page. Frameworks call it for you, but you need it to debug them, and interviews ask about events in particular.

### Reading and Changing the Tree

`querySelector`/`querySelectorAll` find elements by CSS selector. `textContent` sets plain text; `innerHTML` *parses* HTML, which is how XSS gets in ([XSS from the Front End](#xss-from-the-front-end)). `classList` toggles classes so styling stays in CSS, and `dataset.orderId` reads a `data-order-id` attribute.

### Bubbling, Capturing and Delegation

An event travels through the tree in three phases: **capturing**, from the window down to the target; the **target**; then **bubbling**, back up through every ancestor. `addEventListener` listens while bubbling, or while capturing with `{ capture: true }`. So a click on a button in a `<div>` runs the `<div>`'s capturing listeners, the button's, then the `<div>`'s bubbling ones. Two methods are often confused: **`stopPropagation()`** stops the event travelling further, while **`preventDefault()`** cancels the browser's **default action** (following a link, submitting a form) and lets it keep bubbling.

Bubbling makes **event delegation** possible: one listener on the container, and `event.target` says which row was clicked:

```javascript
// One listener on the list handles clicks on every row, including rows added later.
const list = document.querySelector("#orders");

list.addEventListener("click", (event) => {
  const button = event.target.closest("button[data-order-id]");
  if (!button || !list.contains(button)) return;   // a click elsewhere in the list
  cancelOrder(Number(button.dataset.orderId));
});
```

`event.target` may be an icon *inside* the button, so `closest()` walks up to it (`currentTarget` is the element the listener is on). One listener replaces thousands, and rows added later just work. React uses the same mechanism: it listens once at the app's root element and dispatches to your handlers.

## TypeScript for C# Developers

**TypeScript** is JavaScript with a static type system added at compile time. The compiler checks the types and then **erases** them: plain JavaScript runs in the browser. It will feel like home until it doesn't; these are the differences that bite.

### Structural Typing

C# is **nominal**: a type fits an interface only if it declares it. TypeScript is **structural**: a value fits a type if it has the right *shape*.

```typescript
interface Point { x: number; y: number }

function distanceFromOrigin(p: Point): number {
  return Math.hypot(p.x, p.y);
}

const pin = { x: 3, y: 4, label: "Warehouse" };
console.log(distanceFromOrigin(pin));   // 5: pin has x and y, so it is a Point; no "implements"

class Pixel { constructor(public x: number, public y: number) {} }
console.log(distanceFromOrigin(new Pixel(6, 8)));   // 10: a class that never heard of Point
```

Extra properties are fine on an existing object, but an object **literal** written where a type is expected gets an *excess property check*: `const p: Point = { x: 1, y: 2, label: "A" }` is an error, since an unread property is probably a typo. The flip side: an `OrderId` and a `CustomerId` that are both `string` are interchangeable.

### Unions and Narrowing

A **union type** `A | B` is one or the other, and before you use anything specific the compiler makes you prove which: **narrowing**, by `typeof`, `in`, `instanceof`, or a shared literal field. The common pattern is the **discriminated union**:

```typescript
type Order = { id: number; total: number };

type LoadResult =
  | { kind: "ok"; order: Order }
  | { kind: "not-found" }
  | { kind: "error"; status: number };

function describe(result: LoadResult): string {
  switch (result.kind) {
    case "ok":
      return `Order ${result.order.id}: ${result.order.total}`;  // narrowed: order exists here
    case "not-found":
      return "No such order";
    case "error":
      return `Failed with ${result.status}`;
    default: {
      const unreachable: never = result;   // a new case that is not handled stops the build here
      return unreachable;
    }
  }
}

console.log(describe({ kind: "ok", order: { id: 7, total: 30 } }));  // "Order 7: 30"
console.log(describe({ kind: "error", status: 503 }));               // "Failed with 503"
```

This is C#'s pattern matching over a closed set of records, plus the `never` line, which turns a forgotten fourth `kind` into a compile error. With `strict` on, `null` is part of a type only if the union says so (`string | null`): C#'s nullable reference types, enforced as errors.

### Generics

Generics look and work like C#'s, with constraints written as `extends`. Combined with `keyof` (the union of a type's property names) they can express relationships C# can't:

```typescript
function firstBy<T, K extends keyof T>(items: T[], key: K, value: T[K]): T | undefined {
  return items.find((item) => item[key] === value);
}

const users = [{ id: 1, email: "a@example.com" }, { id: 2, email: "b@example.com" }];
console.log(firstBy(users, "email", "b@example.com")?.id);   // 2
// firstBy(users, "emial", "x")  -> compile error: "emial" is not a key of the user type
// firstBy(users, "id", "2")     -> compile error: id is a number
```

`T[K]` is "the type of property `K` of `T`". **Utility types** build on this: `Partial<T>` (a PATCH body), `Pick`, `Omit`, `Record<string, number>`. Generics are erased: there is no `typeof(T)` at run time.

### Where TypeScript Stops: Types Are Erased

Because types vanish, nothing checks that data arriving at run time (a `fetch` body, `JSON.parse`, `localStorage`) matches them:

```typescript
type Order = { id: number; total: number };

const fromServer = JSON.parse('{"id": "42", "total": "19.90"}');   // type: any
const order = fromServer as Order;   // a promise to the compiler, checked by nobody

console.log(typeof order.id);        // "string": the type said number
console.log(order.total + 1);        // "19.901": string concatenation, no error anywhere

function isOrder(value: unknown): value is Order {   // a runtime check the compiler trusts
  return typeof value === "object" && value !== null
    && typeof (value as Order).id === "number"
    && typeof (value as Order).total === "number";
}
console.log(isOrder(fromServer));    // false
```

`as` is not a C# cast: it converts and checks nothing. `any` switches checking off; `unknown` is the safe counterpart you must narrow. A function returning `value is Order` is a *type guard*, a runtime check the compiler trusts. Validate at the boundary with a schema library (Zod is common), or use a client generated from your OpenAPI document so at least the types match the contract.

| | C# | TypeScript |
|---|---|---|
| Compatibility | Nominal: declared inheritance | Structural: matching shape |
| At run time | Types exist (reflection, `is`, `typeof(T)`) | Erased; plain JavaScript runs |
| A cast | Checked conversion, throws if wrong | `as`: unchecked assertion |
| Closed set of cases | Records + pattern matching | Discriminated unions + `never` |
| Null safety | Nullable reference types (warnings) | `strictNullChecks` (errors) |
| Escape hatch | `dynamic` | `any` (prefer `unknown`) |

## Talking to the API from the Browser

This is the boundary you own: a `fetch` call, the browser's same-origin rules, cookies, and the contract that keeps both sides honest.

### fetch and Its One Surprise

Take a minimal API endpoint:

```csharp
app.MapGet("/api/orders/{id:int}", async (int id, IOrderService svc) =>
{
    var order = await svc.GetAsync(id);
    return order is null ? Results.NotFound() : Results.Ok(order);
});
```

and the SPA call that consumes it:

```typescript
type Order = { id: number; total: number };

async function loadOrder(id: number, signal?: AbortSignal): Promise<Order> {
  const res = await fetch(`/api/orders/${id}`, { credentials: "include", signal });
  if (!res.ok) throw new Error(`Order ${id} failed: ${res.status}`);  // fetch does not throw on 404 or 500
  return (await res.json()) as Order;
}
```

Four details carry most of the weight:

- **`fetch` rejects only when no response arrives** (network down, aborted, refused by CORS). A `404` or `500` is a *successful* fetch with `res.ok === false`; skip the check and your ProblemDetails body flows into the app as data, as the exercise shows.
- **`credentials`**: `"same-origin"` (the default) sends cookies only to the page's own origin, `"include"` cross-origin too (if the server's CORS allows it), `"omit"` never. A relative URL resolves against the page's origin, so a SPA served by the API's host needs no CORS at all.
- **`signal`** takes an `AbortController`'s signal, the `CancellationToken` counterpart; `AbortSignal.timeout(5000)` aborts itself, since `fetch` has no timeout.
- **`res.json()` returns `any`**, so `as Order` is the unchecked assertion from the TypeScript section.

### The Same-Origin Policy and CORS, Seen from the Browser

An **origin** is *(scheme, host, port)*: `https://app.example.com` and `https://api.example.com` differ, and so do `localhost:5173` and `localhost:5000`. The **Same-Origin Policy** stops script on one origin from *reading* another origin's responses; otherwise any page could script your bank with your cookies and read the answers. It does not stop *sending*: forms and images always could.

**CORS** is how a server tells the browser which other origins may read its responses; the ASP.NET Core side is in [Chapter 5: HTTP and Web APIs](#chapter-5-http-and-web-apis). A cross-origin `fetch` carries an `Origin` header and takes one of two paths:

- **A simple request** (`GET`, `HEAD` or `POST`, safelisted headers only, and a body typed `text/plain`, `application/x-www-form-urlencoded` or `multipart/form-data`) is **sent at once**, since a plain form could send it. If `Access-Control-Allow-Origin` doesn't match, the script gets an error, but the server has already run the request.
- **Anything else** (`PUT`, `PATCH`, `DELETE`, an `Authorization` header, a JSON body: most SPA calls) gets a **preflight**, an `OPTIONS` request asking permission first:

```text
OPTIONS /api/orders/42 HTTP/1.1                    ← preflight
Origin: https://app.example.com
Access-Control-Request-Method: PUT
Access-Control-Request-Headers: authorization, content-type

HTTP/1.1 204 No Content
Access-Control-Allow-Origin: https://app.example.com
Access-Control-Allow-Methods: GET, POST, PUT
Access-Control-Allow-Headers: authorization, content-type
Access-Control-Max-Age: 600                        ← cache this answer for 600 s

PUT /api/orders/42 HTTP/1.1                         ← now the real request
Origin: https://app.example.com
```

Without `Access-Control-Max-Age` a preflight answer is cached for 5 seconds, and browsers cap the value. A credentialed request also needs `Access-Control-Allow-Credentials: true` and the exact origin, never `*`.

**In DevTools**, the Console shows "blocked by CORS policy" with the reason, and the Network tab shows the preflight as its own `OPTIONS` row. A failed preflight (typically authentication middleware answering `401` to an `OPTIONS` that carries no credentials) means the real request is never sent; a failed *simple* request was sent and executed, only its response hidden. That is why CORS is no security control: [Chapter 12: CORS Is Not Access Control](#cors-is-not-access-control).

### Cookies and Sessions

**Cookies** are how a server plants data in the browser that comes back automatically on every later request to that domain. The server sets them with `Set-Cookie`:

```text
Set-Cookie: sessionId=abc123; HttpOnly; Secure; SameSite=Lax; Max-Age=3600
```

Those attributes are security-critical:

- **HttpOnly**: JavaScript cannot read the cookie (`document.cookie`), so an XSS bug cannot steal it.
- **Secure**: only sent over HTTPS.
- **SameSite** governs requests coming from *another site*: `Strict` never sends the cookie, `Lax` only on top-level navigations with safe methods (following a link), which blocks the classic **CSRF** form post, and `None` (requires `Secure`) always. Some browsers default to `Lax`; set it explicitly.

**Site is not origin.** `SameSite` compares scheme plus registrable domain, so `app.example.com` and `api.example.com` are the same site but different origins: a `Lax` cookie *is* sent from app to API, while CORS still governs reading the response. One registrable domain for SPA and API is what makes cookie authentication between them workable.

A **session** keeps an opaque ID in the cookie and the state in a *shared* server-side store (Redis, SQL), so any instance can serve the user.

### Cookies, localStorage and sessionStorage: Where Browser State Lives

| | Cookie | `localStorage` | `sessionStorage` |
|---|---|---|---|
| Sent to the server | Automatically | No | No |
| Readable by any script on the origin | Not if `HttpOnly` | Yes | Yes |
| Lifetime | `Max-Age`/`Expires` or session | Until cleared | Until the tab closes |
| Size | A few KB per cookie | About 5 MiB per origin | About 5 MiB per origin |

Web storage is a synchronous key-value store of **strings**, so objects must go through JSON on the way in and out:

```javascript
localStorage.setItem("cart", { items: 2 });
console.log(localStorage.getItem("cart"));   // "[object Object]": values are always strings

localStorage.setItem("cart", JSON.stringify({ items: 2 }));
console.log(JSON.parse(localStorage.getItem("cart")).items);   // 2
```

Fine for preferences and drafts; poor for an access token, which one XSS bug or compromised npm package can read and send anywhere. An `HttpOnly` cookie can't be read by script, at the price of CSRF protection. Current guidance keeps tokens out of the browser entirely, the **Backend-for-Frontend** pattern in [Chapter 34](#chapter-34-frontend-and-full-stack-in-depth); the flows are in [Chapter 12: Authorization Code Flow with PKCE](#authorization-code-flow-with-pkce).

### Owning the Contract: OpenAPI and Typed Clients

For frontend velocity, nothing beats an accurate **OpenAPI document** ([Chapter 5: REST and OpenAPI](#rest-and-openapi)) with the client *generated* from it: hand-written types drift silently, generated ones break at *compile time*:

```bash
# NSwag example: OpenAPI -> typed TS client
nswag openapi2tsclient /input:swagger.json /output:src/api-client.ts
```

Rename a property, regenerate, and TypeScript flags every broken usage; generate in CI so the client is never older than the contract ([Chapter 22](#api-versioning-backward-compatibility) covers versioning).

## React by Its Mechanism

React's model is small enough to learn by mechanism: **the UI is a function of state**. A component, given props and state, *returns a description* of the screen. When state changes, React calls it again, compares the new description with the previous one (*reconciliation*) and applies the minimal DOM mutations.

### Components and Props

A component is a function that returns **JSX**, syntax sugar that compiles `<OrderRow order={o} />` to a function call building a plain object. **Props** are its read-only input. Data flows down through props; changes flow up through callbacks:

```tsx
type Order = { id: number; customer: string; total: number };

type OrderRowProps = { order: Order; onCancel: (id: number) => void };

function OrderRow({ order, onCancel }: OrderRowProps) {
  return (
    <li>
      {order.customer}: {order.total.toFixed(2)}
      <button onClick={() => onCancel(order.id)}>Cancel</button>
    </li>
  );
}

function OrderList({ orders, onCancel }: { orders: Order[]; onCancel: (id: number) => void }) {
  if (orders.length === 0) return <p>No orders yet.</p>;
  return <ul>{orders.map((o) => <OrderRow key={o.id} order={o} onCancel={onCancel} />)}</ul>;
}
```

Conditions are a plain `if`, lists a `map`, and `{expression}` embeds any value. In Blazor terms, `[Parameter]` properties are props and `EventCallback` is the callback.

### State and Re-rendering

`useState` gives a component **state** that survives between calls: a value and a setter. The setter doesn't change the variable in front of you; it asks React to **re-render**, calling the component (and its children) again, which is cheap because the DOM changes only where the output differs.

Each render sees its state as a **snapshot**, constant for the duration of that render, which explains the most common state bug:

```tsx
import { useState } from "react";

function Quantity() {
  const [count, setCount] = useState(0);

  function addThreeWrong() {
    setCount(count + 1);   // count is 0 in this render: "set to 1"
    setCount(count + 1);   // "set to 1" again
    setCount(count + 1);   // the next render shows 1
  }

  function addThree() {
    setCount((c) => c + 1);   // updaters are queued and run in order
    setCount((c) => c + 1);
    setCount((c) => c + 1);   // the next render shows 3
  }

  return (
    <>
      <output>{count}</output>
      <button onClick={addThreeWrong}>+3 (wrong)</button>
      <button onClick={addThree}>+3</button>
    </>
  );
}
```

React **batches** the updates in an event handler into one re-render; when the next value depends on the previous one, pass an updater function. Two more rules follow from the mechanism. **Render must be pure** (no fetching, subscribing or DOM writes in the body), because React may call it at any time. And **state is replaced, not mutated**: React compares old and new with `Object.is`, so `items.push(x); setItems(items)` passes the *same* array and the update is skipped. Write `setItems([...items, x])`.

> **Pay attention.** **Why "I set the state and the variable still has the old value".** The setter schedules a new render in which `useState` returns the new value; the current render's `count` is a constant, captured by every closure created during it. Log it right after `setCount(5)` and you see the old value. Need the new value now? It is the one you passed to the setter.

### The Rules of Hooks

Functions starting with `use` are **hooks**. React keeps a list of hook slots per component instance and matches each hook call to its slot **by call order**, not by name. Hence the rules: call hooks **only at the top level** of a component or custom hook (never in conditions, loops or after an early return), and **only from React functions**. Break the first and the slots shift:

```tsx
import { useState } from "react";

type User = { name: string };

function Profile({ user }: { user: User | null }) {
  const [theme] = useState("light");           // hook 1 on every render
  if (!user) return <p className={theme}>Signed out</p>;   // an early return...
  const [tab, setTab] = useState("orders");     // ...so hook 2 runs on some renders only
  return <button onClick={() => setTab("settings")}>{user.name}: {tab}</button>;
}
```

When `user` goes from `null` to a value, React throws "Rendered more hooks than during the previous render"; the other way round, "Rendered fewer hooks than expected". Move every hook above the early return. The `eslint-plugin-react-hooks` lint rule catches this and missing effect dependencies at build time.

### Effects and Their Dependency Arrays

Code that talks to the outside world (a subscription, a timer, a non-React widget) goes into an **effect**. `useEffect(setup, dependencies)` runs `setup` **after** the render reaches the DOM; the function `setup` returns is the **cleanup**, called before the effect runs again and on unmount.

The **dependency array** decides when the effect re-runs:

| Dependencies | The effect runs |
|---|---|
| omitted | after every render |
| `[]` | after the first render only (and cleans up on unmount) |
| `[roomId]` | after the first render, and again whenever `roomId` changed (compared with `Object.is`) |

```tsx
import { useEffect } from "react";

function ChatRoom({ roomId }: { roomId: string }) {
  useEffect(() => {
    const connection = createConnection(roomId);   // the effect reads roomId...
    connection.connect();
    return () => connection.disconnect();          // ...cleanup undoes it before the next run
  }, [roomId]);                                    // ...so roomId is a dependency

  return <h2>Room {roomId}</h2>;
}
```

Switch from `"general"` to `"travel"` and unmount, and the calls are: connect general, disconnect general, connect travel, disconnect travel. **Every value the effect reads must be listed**; leave one out and the effect keeps the values of the render that created it, a *stale closure* (the chat stays in the old room).

In development, `<StrictMode>` deliberately runs each effect **setup, cleanup, setup** on mount to expose broken cleanups: an API called twice in development is that, and the fix is a correct cleanup. Many effects shouldn't exist: derive values during render, and handle clicks in the click handler. Fetching in effects is full of traps (see the exercise); most teams use a data-fetching library ([Chapter 34](#chapter-34-frontend-and-full-stack-in-depth)).

### Keys in Lists

When a list re-renders, React matches old and new items by **`key`**. State belongs to a *position in the tree*, and for list items the key *is* that identity:

```tsx
import { useState } from "react";

type Item = { id: number; name: string };

function Line({ item }: { item: Item }) {
  const [note, setNote] = useState("");   // state belongs to the position React matched
  return (
    <li>
      {item.name} <input aria-label={`Note for ${item.name}`} value={note}
                         onChange={(e) => setNote(e.target.value)} />
    </li>
  );
}

function Basket({ items, keyByIndex }: { items: Item[]; keyByIndex: boolean }) {
  return (
    <ul>
      {items.map((item, index) =>
        <Line key={keyByIndex ? index : item.id} item={item} />)}
    </ul>
  );
}
```

Type a note on "Tea", then remove Tea. With `key={index}`, Milk is now at index 0, React concludes item 0 survived, and Milk inherits Tea's note; with `key={item.id}` Tea's row is destroyed and Milk keeps its own. Use a stable ID from the data. Index keys are safe only for lists that never reorder, insert or delete, and `Math.random()` keys recreate every row on every render.

### How Angular Differs

Many .NET shops chose **Angular**, and its design will look familiar. Where React is a rendering library you assemble a stack around, Angular is a complete, opinionated framework: router, forms, HTTP client, dependency injection, testing and a CLI. Three mechanisms set it apart.

**Dependency injection** is built in and resembles ASP.NET Core's: `providedIn: "root"` is a singleton, providers on a component give its subtree its own instance (Angular's scoped lifetime), and `inject()` requests a dependency:

```typescript
import { Component, Injectable, inject, signal } from "@angular/core";
import { HttpClient } from "@angular/common/http";

type Order = { id: number; total: number };

@Injectable({ providedIn: "root" })            // one instance for the app: a singleton
export class OrderService {
  private http = inject(HttpClient);
  getOrders() { return this.http.get<Order[]>("/api/orders"); }   // an Observable
}

@Component({
  selector: "app-orders",
  template: `@for (o of orders(); track o.id) { <li>{{ o.id }}: {{ o.total }}</li> }`,
})
export class OrdersComponent {
  orders = signal<Order[]>([]);
  constructor() { inject(OrderService).getOrders().subscribe((o) => this.orders.set(o)); }
}
```

Templates use Angular's own syntax (`track` is Angular's `key`) and are compiled ahead of time.

**RxJS observables** are Angular's async currency. Unlike a promise, an **Observable** is *lazy* (no request until someone subscribes), emits *many* values over time, and is *cancellable* (unsubscribing aborts the request). Operators compose streams, as in the classic search box:

```typescript
import { Subject, debounceTime, distinctUntilChanged, from, switchMap } from "rxjs";

const searchTerms = new Subject<string>();     // the input's (input) handler calls searchTerms.next(text)

searchTerms.pipe(
  debounceTime(300),                            // wait for a 300 ms pause in typing
  distinctUntilChanged(),                       // skip a term equal to the previous one
  switchMap((term) => from(searchApi(term))),   // a new term drops the previous request
).subscribe((results) => render(results));
```

`switchMap` unsubscribes from the previous request when a new term arrives, so a slow response for an old term can never overwrite a newer one: the race in this chapter's exercise, solved by one operator. The costs are a steep learning curve and a new kind of leak, a subscription that outlives its component (the `async` pipe and `takeUntilDestroyed()` exist for that).

**Change detection** is how Angular knows when to update the DOM. For most of Angular's life, **zone.js** patched the browser's async APIs (timers, events, promises, XHR) so Angular heard about every callback and then re-checked templates: no need to say what changed, at the cost of checking far more often than necessary. Modern Angular uses **signals**, reactive values that know who reads them:

```typescript
import { computed, signal } from "@angular/core";

const quantity = signal(2);
const price = signal(9.5);
const total = computed(() => quantity() * price());   // remembers which signals it read

console.log(total());   // 19
quantity.set(3);        // marks total stale; nothing recomputes yet
console.log(total());   // 28.5: recomputed on the next read
```

A template that reads a signal is updated when that signal changes. Since Angular 21, new applications are **zoneless** by default: signal updates, template event handlers, the `async` pipe and `markForCheck` trigger change detection, not zone.js. Older codebases still run on zones, often with `OnPush` to check less.

## Routing and Forms in a SPA

### Client-Side Routing

A SPA router maps URLs to components without asking the server: on a link click it calls `preventDefault()`, changes the address bar with the **History API** (`history.pushState`, no request), and renders the route's component; Back fires a `popstate` event the router handles.

Hence the classic deployment bug: navigation works, but **refreshing** `/orders/42` or opening a shared link returns **404**, because now the browser does ask the server for `/orders/42`. The server must answer unknown non-file paths with `index.html` and let the router take over. In ASP.NET Core:

```csharp
app.UseStaticFiles();                                  // the built SPA: index.html, assets/*
app.MapControllers();                                  // /api/...
app.Map("/api/{**rest}", () => Results.NotFound());    // unknown API paths stay 404s
app.MapFallbackToFile("index.html");                   // everything else: let the SPA route it
```

Without the third line, a mistyped API URL also gets `index.html` with `200 OK`, and the SPA fails later with a baffling JSON parse error instead of a clean 404 (checked against ASP.NET Core 10). **Route guards are UX, not security**: hiding `/admin` in the router stops nobody calling `/api/admin`. And SPA navigation does not move focus or announce the new page as a full load does ([Chapter 34](#chapter-34-frontend-and-full-stack-in-depth) has the fix).

### Forms in a SPA

A **controlled** React input keeps its value in state (`value={email}` plus an `onChange`), so you can validate on every keystroke; an **uncontrolled** one keeps it in the DOM and you read it on submit with `FormData`. Angular has *template-driven* and *reactive* forms (a typed form model in the component); larger codebases mostly use reactive. Three backend-facing points decide whether forms feel solid:

- **Validate twice, for different reasons**: on the client for instant feedback, on the server because only that one counts.
- **Map server errors to fields.** `ValidationProblemDetails` carries an `errors` object keyed by field name; shown next to the inputs, a `400` becomes a fixable form. Agree on the field-name casing once.
- **Expect double submits.** Disable the button while pending, and make the endpoint safe to repeat (an idempotency key, [Chapter 20](#chapter-20-distributed-systems)).

## Accessibility: The Part That Is Now Law

Accessibility has moved from "good practice we should get to" to "a legal requirement with a date attached". The **European Accessibility Act**, applicable since June 2025, obliges a broad set of consumer-facing products and services sold in the EU (e-commerce, banking, transport ticketing, e-books, telecoms) to meet accessibility requirements, with the harmonised standard EN 301 549 pointing at **WCAG 2.1/2.2 level AA**; the public sector was already covered by the Web Accessibility Directive. In the US, Section 508 covers federal procurement, and ADA litigation over inaccessible websites has been steady for a decade. So **inaccessible markup is a compliance defect, not a polish item**, and "the designer didn't specify it" stopped being an answer.

### WCAG, and How to Actually Think About It

WCAG is organised under four principles — the **POUR** acronym — and they are worth knowing as a reasoning tool rather than a checklist:

- **Perceivable.** Can the user receive the information at all? Text alternatives for images, captions for video, sufficient colour contrast, not conveying meaning by colour alone.
- **Operable.** Can they drive it? Everything reachable and usable by keyboard, no traps, enough time, no seizure-inducing flashing, skip links past repeated navigation.
- **Understandable.** Is it predictable? Consistent navigation, labelled inputs, errors identified in text and explained, no surprising context changes on focus.
- **Robust.** Will assistive technology parse it? Valid markup, correct name/role/value for every control.

Conformance comes in levels A, AA, AAA. **AA is the target** — it is what the regulations reference, and AAA includes requirements (like 7:1 contrast) that are not achievable for most designs.

### Semantic HTML First, ARIA Second

Almost every accessibility bug in a .NET shop's UI comes from the same root cause the HTML section warned about: a `<div>` with a click handler doing the job of a `<button>`.

```html
<!-- Not focusable, not keyboard-operable, no role, no state.
     A screen reader announces nothing useful. -->
<div class="btn" onclick="submit()">Save</div>

<!-- Focusable, Enter/Space activated, announced as a button,
     disabled state understood — all of it for free. -->
<button type="button" onclick="submit()">Save</button>
```

The native element carries a *name*, a *role*, and *state* that browsers and assistive technology already agree on. Recreating that with ARIA means reimplementing focus handling, keyboard activation, `aria-pressed`/`aria-expanded` state, and disabled semantics — correctly, in every browser and screen reader combination.

This is why the **first rule of ARIA** is: don't use ARIA. If a native element or attribute exists with the semantics you need, use it. ARIA adds *semantics* to markup; it never adds *behaviour*. `role="button"` on a `<div>` does not make Enter activate it, does not make it focusable, and does not make it a button — it makes a screen reader announce "button" for something that then does nothing when the user presses Enter, which is worse than saying nothing at all.

> **Pitfall.** The most common harmful pattern is ARIA applied to paper over a structural problem: `aria-label` on a `<div>` acting as a heading, `role="navigation"` on something that should be a `<nav>`, or `aria-live` sprinkled everywhere to force announcements. Bad ARIA is measurably worse than no ARIA — the WebAIM annual survey has consistently found pages using ARIA average *more* detected errors than pages without it.

Reach for ARIA when you genuinely have no native equivalent: a tab set, a combobox with an autocomplete listbox, a tree view, a live region for asynchronous status. And when you do, follow the **ARIA Authoring Practices Guide** patterns exactly — including the keyboard interaction table, which is the part people skip and the part users notice.

### Keyboard Operability and Focus

Test this today, on the app you are working on: put your mouse down and try to complete your primary user journey. This single exercise finds most of the serious problems.

What to look for:

- **Everything interactive is reachable** by Tab, in an order that matches the visual layout. If you find yourself reaching for `tabindex="3"` to fix the order, the DOM order is wrong — fix that instead. The only `tabindex` values you should normally use are `0` (put this in the natural order) and `-1` (focusable by script only, not by Tab).
- **Focus is visible.** `outline: none` with no replacement is the single most damaging line of CSS for keyboard users. If the default ring is ugly, style `:focus-visible` — don't remove it.
- **Modals trap focus while open, and return it on close.** Open a dialog, Tab through it: focus must not escape to the page behind. When it closes, focus goes back to the element that opened it, or the user is dumped at the top of the document with no idea where they are. The native `<dialog>` element with `showModal()` handles most of this for you.
- **Skip links.** A "skip to main content" link as the first focusable element saves keyboard users from tabbing through forty navigation items on every page.
- **No focus traps you didn't intend** — the classic being an embedded third-party widget you can Tab into but not out of.

### Forms, Where It Matters Most

Forms are where inaccessible UI stops being an inconvenience and starts costing people money.

```html
<!-- The label is programmatically associated: clicking it focuses the
     input, and a screen reader announces the label with the field. -->
<label for="email">Email address</label>
<input id="email" name="email" type="email"
       autocomplete="email"
       aria-describedby="email-hint email-error"
       aria-invalid="true" />
<p id="email-hint">We'll only use this for order updates.</p>
<p id="email-error" class="error">Enter an email address in the format name@example.com.</p>
```

The rules that carry most of the weight: every input has a real `<label>` (placeholder text is not a label — it disappears when the user types and is often too low-contrast); errors are associated with their field via `aria-describedby` and `aria-invalid`, not just coloured red; error text says what to do, not "invalid input"; `autocomplete` attributes are set so browsers and password managers can fill fields; and required fields are marked in text, not only with an asterisk whose meaning is explained in a legend nobody reads.

For asynchronous validation and status messages, an `aria-live="polite"` region announces changes without stealing focus. Use it sparingly and only for genuine status; a live region on a chat log that fires on every keystroke is a torture device.

How to test all of this, automatically in CI and by hand with a keyboard and a screen reader, is in [Chapter 34](#chapter-34-frontend-and-full-stack-in-depth).

> **Best practice.** Fix accessibility in your shared components, not in your pages. A design system where the `Button`, `Modal`, `Field` and `Table` components are correct once means hundreds of screens are correct by default — and it turns accessibility from a per-feature tax into a solved infrastructure problem. This is the same leverage argument as any other cross-cutting concern in this book.

## Tooling: npm, package.json, Lockfiles and Vite

Browsers cannot run TypeScript or JSX, so a toolchain sits between your source and the browser; when "it builds on my machine" fails in CI, look here.

### npm, package.json and the Lockfile

**npm** is NuGet's counterpart. **`package.json`** holds `scripts` (`npm run dev`, `npm test`), `dependencies` (shipped in the bundle) and `devDependencies` (build and test tools). Versions are semver ranges, a caret by default: `"^19.2.0"` is any 19.x from 19.2.0 up, `"~19.2.0"` is 19.2.x only. A range is a policy, not a version: two installs a week apart can differ.

The **lockfile**, `package-lock.json`, records the exact version and integrity hash of every package in the tree. Commit it. `npm install` may update it; **`npm ci`**, for CI and Docker, deletes `node_modules`, installs exactly the lockfile, and fails if it disagrees with `package.json` ([Chapter 27: Lockfiles](#lockfiles) compares NuGet).

> **Pitfall.** A small app pulls in hundreds of transitive packages, and install scripts run arbitrary code on the machine that installs them, including a CI runner with secrets. Commit the lockfile, build with `npm ci`, take `npm audit` seriously, and review what a new dependency brings ([Chapter 27](#chapter-27-security-in-depth-and-the-supply-chain)).

### Vite: Dev Server and Build

A **bundler** walks the import graph, compiles TypeScript and JSX, drops unused exports and emits a few optimised files. **Vite** is the current default for new React and Vue projects (Angular's CLI uses Vite and esbuild internally), in two modes:

- **`vite` (dev server)** doesn't bundle: it serves source as native ES modules, compiling each file when requested, and **hot module replacement** swaps just the edited module into the running page.
- **`vite build`** bundles with Rolldown (Vite 8; earlier versions used Rollup): tree-shaking, minification, chunks, and **content-hashed file names** (`index-4f9a1c.js`), so assets can be cached forever while `index.html` never is.

Two features matter at the boundary with your API:

```javascript
// vite.config.js
import { defineConfig } from "vite";

export default defineConfig({
  server: {
    proxy: { "/api": "http://localhost:5080" },   // dev server forwards /api to Kestrel
  },
});
```

The **dev proxy** gives the browser one origin in development: `/api` calls go to `localhost:5173` and Vite forwards them to Kestrel, so no CORS is needed and the code uses production's relative URLs.

**Environment variables** are compiled in: only `VITE_`-prefixed ones reach client code (`import.meta.env.VITE_API_URL`), written into the bundle **at build time**. So they are public (never a secret), and the bundle is tied to one environment; to promote one artifact through environments ([Chapter 13](#chapter-13-git-and-cicd)), fetch a `config.json` at start-up instead.

## XSS from the Front End

The server's side is in [Chapter 12: Cross-Site Scripting (XSS)](#cross-site-scripting-xss). From the browser's side: **XSS happens when attacker-controlled text reaches a place where the browser interprets text as code**, a *sink*:

- HTML parsing: `innerHTML`, `outerHTML`, `insertAdjacentHTML`, `document.write`.
- URLs that execute: `href` or `src` set to `javascript:…`.
- Code from strings: `eval`, `new Function`, `setTimeout("string")`.
- Inline event-handler attributes: `onerror="…"`, `onclick="…"`.

Frameworks are safe **by default** because their normal path avoids sinks: JSX `{text}` becomes a **text node**, never parsed, and Angular *sanitizes* values bound into HTML or URL contexts. The danger is the escape hatches:

```tsx
function Comment({ text }: { text: string }) {
  return <p>{text}</p>;   // text becomes a text node: markup in it is shown, never parsed
}

function CommentUnsafe({ html }: { html: string }) {
  return <p dangerouslySetInnerHTML={{ __html: html }} />;   // parsed as HTML: an XSS sink
}
```

Given `<img src="x" onerror="alert(1)">`, the first shows the characters; the second creates a real `<img>` whose `onerror` runs. Angular's escape hatch is `DomSanitizer.bypassSecurityTrustHtml`. React 19 replaces a `javascript:` URL in `href` with one that throws (older versions only warned); still check user-built URLs for `http:`/`https:`. If you must render user HTML (Markdown, rich text), sanitize it with a maintained library such as DOMPurify, never a regex. Tokens kept out of JavaScript's reach and a **Content Security Policy** ([Chapter 34](#chapter-34-frontend-and-full-stack-in-depth)) limit what a successful injection can do.

## Core Web Vitals

Google's **Core Web Vitals** are three numbers for what users experience, each tied to a mechanism from this chapter:

| Metric | Measures | Good | Poor | Usual causes |
|---|---|---|---|---|
| **LCP**, Largest Contentful Paint | Loading: when the largest image or text block in the viewport is rendered | ≤ 2.5 s | > 4 s | slow server response (your TTFB), render-blocking CSS and JS, a large hero image, a CSR app that renders nothing until its bundle runs |
| **INP**, Interaction to Next Paint | Responsiveness: the delay from a click, tap or key press until the next frame is painted, across the whole visit | ≤ 200 ms | > 500 ms | long tasks on the main thread: heavy event handlers, big re-renders, JSON processing |
| **CLS**, Cumulative Layout Shift | Visual stability: how much visible content moves unexpectedly | ≤ 0.1 | > 0.25 | images and embeds without dimensions, banners and ads injected above content, web fonts that change text size |

INP replaced First Input Delay in 2024: FID measured only the first interaction's input delay, INP every interaction through to the next paint. A page passes when the **75th percentile** of real visits is "good" on all three. **Field data** from real users (the `web-vitals` library, Chrome's public report) is the truth; **lab data** from one Lighthouse load is reproducible but synthetic, and can't measure INP because nobody clicks. The backend's share is bigger than expected: LCP starts with your server's time to first byte, and a CSR page's LCP waits for the API call that fills it. [Chapter 34](#chapter-34-frontend-and-full-stack-in-depth) covers measuring and fixing each.

## How Much Frontend Should You Actually Learn?

Optimize for *effectiveness at the boundary*, not for becoming a frontend engineer:

- **Fluent:** enough HTML, CSS, JavaScript and TypeScript to read a SPA, make small changes, write a `fetch` call and debug in DevTools; the Network tab and Console are your first stop when "the frontend is broken".
- **Deep:** the API contract: OpenAPI, generated clients, versioning, auth flows (OIDC, PKCE, the BFF), CORS, real-time. This is *your* territory.
- **Aware:** how React and Angular structure an app, well enough to review PRs and design APIs that fit; rendering strategies and the build pipeline conceptually.

> **The single most valuable investment:** owning the contract boundary. A documented, versioned, typed API with clear auth turns frontend integration from a negotiation into a formality.

### Picking the UI Stack

A short decision guide:

1. **Content-heavy, public, SEO-critical?** Server-rendered — Razor Pages/MVC, Blazor Static SSR, or a JS meta-framework with SSR.
2. **Internal line-of-business app, .NET team?** Blazor (Static SSR + interactive islands, or Auto) is a strong, low-friction default.
3. **Rich, public, ecosystem-hungry SPA with JS talent available?** React/Angular/Vue against a REST API, ideally behind a BFF.
4. **Cross-platform desktop/mobile from one C# codebase?** MAUI, or Blazor Hybrid if reusing web UI; Avalonia if Linux desktop matters; native if platform polish is the product.

[Chapter 34](#chapter-34-frontend-and-full-stack-in-depth) compares Blazor, rendering strategies and native clients. There is no universal answer, only the one that fits *this* team, audience and performance budget, chosen explicitly.

## Sources & Further Reading

- **MDN Web Docs** (developer.mozilla.org): how browsers work, `<script>`, the cascade, the execution model and microtasks, Fetch, CORS, `Set-Cookie`, storage quotas, events.
- **React** (react.dev): state as a snapshot, rules of hooks, synchronizing with effects, rendering lists. **Angular** (angular.dev): DI, signals, zoneless, security. **RxJS** (rxjs.dev).
- **TypeScript Handbook** (typescriptlang.org); **Vite** (vite.dev); **npm** (docs.npmjs.com): `package-lock.json`, `npm ci`.
- **web.dev** and the **`web-vitals` README** (GitHub `GoogleChrome/web-vitals`); **WCAG 2.2** and the **ARIA Authoring Practices Guide** (w3.org).

## Exercises

### Find the bug

This search component works in the demo. In production, users report that the results sometimes don't match what they typed, and that the page occasionally goes blank.

```tsx
import { useEffect, useState } from "react";

type Product = { id: number; name: string };

export function ProductSearch({ query }: { query: string }) {
  const [results, setResults] = useState<Product[]>([]);

  useEffect(() => {
    fetch(`/api/products?q=${encodeURIComponent(query)}`)
      .then((res) => res.json())
      .then((data: Product[]) => setResults(data));
  }, [query]);

  return (
    <ul>
      {results.map((p) => <li key={p.id}>{p.name}</li>)}
    </ul>
  );
}
```

Explain both symptoms by their mechanism, then fix them.

<details>
<summary>Answer</summary>

**Wrong results: a race between responses.** Each `query` change starts a new request and nothing stops the old one. The user types "lap", then "laptop"; the slower "lap" response arrives *last*, its `setResults` runs last, and the list shows "lap" results under "laptop". No data race, just callback *ordering*, and an effect that starts async work without a cleanup.

**Blank page: the unchecked response.** `fetch` does not reject on a `500`. The ProblemDetails body parses fine, `setResults` stores an *object*, and the next render's `results.map` throws. With no error boundary, React unmounts the whole tree. The `data: Product[]` annotation changed nothing: types are erased.

The fix aborts the previous request in the cleanup (which runs before the next effect and on unmount) and checks the status:

```tsx
useEffect(() => {
  const controller = new AbortController();
  fetch(`/api/products?q=${encodeURIComponent(query)}`, { signal: controller.signal })
    .then((res) => {
      if (!res.ok) throw new Error(`Search failed: ${res.status}`);
      return res.json();
    })
    .then((data: Product[]) => { setResults(data); setError(null); })
    .catch((err) => { if (err.name !== "AbortError") setError(err.message); });
  return () => controller.abort();   // a newer query cancels this one, and so does unmounting
}, [query]);
```

(with an `error` state rendered as `<p role="alert">`). An `ignore` flag set in the cleanup also works, but aborting saves the server the work; debouncing reduces requests but does not fix the race. Verified in `verify/frontend/ch06`: with responses released out of order the printed component shows the old query's results and the fix the new one's; on a `500` the printed one throws `results.map is not a function` and React empties the page, while the fix shows the error.
</details>

A second one, in plain JavaScript. The toast says everything was saved, but some lines are missing from the database, and nobody sees an error.

```javascript
export async function saveAll(lines, api) {
  lines.forEach(async (line) => {
    await api.save(line);
  });
  return lines.length;
}

export async function onSaveClicked(lines, api, toast) {
  try {
    const count = await saveAll(lines, api);
    toast(`Saved ${count} lines`);
  } catch (e) {
    toast(`Save failed: ${e.message}`);
  }
}
```

<details>
<summary>Answer</summary>

`forEach` calls the `async` callback per line and **ignores the promise it returns**, so `saveAll` starts every save, waits for none, and the success toast appears before anything is saved. A later failure rejects a promise nobody holds: the `try`/`catch` finished long ago, and the browser logs an unhandled rejection nobody reads. It is the JavaScript twin of C#'s `async void` ([Chapter 4](#chapter-4-async-essentials)).

Keep the promises: `await Promise.all(lines.map((line) => api.save(line)))` saves concurrently and rejects if any fails; `for (const line of lines) await api.save(line);` saves one at a time, in order. Verified in the repository: the printed code toasts "Saved 2 lines" with zero saved, and a failing save yields a success toast plus an unhandled rejection; the fix toasts after both saves and reports the failure.
</details>

### What would you do

The SPA team is starting a new React app against your API. Their first two requests: enable CORS with `AllowAnyOrigin()` "because the dev server runs on localhost:5173 and it's blocking us", and return the JWT in the login response body so they can keep it in `localStorage`. They need to demo on Friday.

<details>
<summary>How a senior engineer reasons about it</summary>

Separate the problem from the requested solution. The first problem is local development across two origins, which Vite's dev proxy solves better than CORS. In production, one host needs no CORS; otherwise configure the real origins per environment, never a wildcard on an authenticated API.

The second request is a security decision dressed up as a convenience, so it deserves a short conversation, not a silent yes or no. A token in `localStorage` is readable by every script on the origin, including a compromised dependency; offer the BFF with an `HttpOnly` cookie at its realistic cost, and if Friday really can't wait, agree on what is temporary and write it down with a date.

What makes the answer senior is that the team leaves unblocked, with a working setup today, and the decision about tokens made explicitly by the people who own the risk rather than by default.
</details>

### Go check

On the SPA your API serves, answer from the browser and the code, not from memory:

- Filter DevTools' Network tab by `OPTIONS`: how many preflights per page load, and do they carry `Access-Control-Max-Age`? Could SPA and API share an origin?
- Run Lighthouse on the slowest important page. What is the LCP element, and how much of LCP is your server's time to first byte?
- Search the frontend for `dangerouslySetInnerHTML`, `innerHTML`, `bypassSecurityTrust` and `eval(`. Where does each one's data come from?
- Is `package-lock.json` committed, does CI use `npm ci`, and is any `VITE_` variable a secret?
- Refresh on a deep link like `/orders/42`, and request a non-existent `/api/...` URL: do you get the page and a 404?

## Interview Questions

**What happens between typing a URL and seeing the page?**
DNS, TCP, TLS and HTTP ([Chapter 5](#chapter-5-http-and-web-apis)); then DOM (incremental), CSSOM (render-blocking), scripts (classic ones block the parser), layout, paint, composite. Later DOM changes re-run part of it.

**`==` versus `===`?**
`===` never converts types; `==` coerces first, with rules that aren't even transitive. Use `===`; `x == null` is the one common exception.

**Red flag:** "`===` compares references and `==` compares values." Both compare objects by reference; the difference is coercion.

**How is `this` determined?**
By the call: `obj.f()` gives `obj`, a plain call `undefined` in strict mode, `new` the new object, `bind` a fixed value; arrow functions use the surrounding `this`. A method passed as a callback loses it.

**Explain the event loop. Which runs first: a `setTimeout(…, 0)` callback or a resolved promise's `then`?**
One task runs to completion, then all microtasks, then rendering, then the next task. Promise callbacks are microtasks, timers are tasks, so `then` runs first.

**Red flag:** "`await` moves the work to a background thread." There is one thread; `await` only stops a function from blocking it while it waits, and CPU work still freezes the page.

**`Promise.all` versus `Task.WhenAll`?**
`Promise.all` rejects on the first failure while the others keep running unobserved; `Task.WhenAll` waits for all. `Promise.allSettled` is the wait-for-everything shape.

**How does TypeScript's type system differ from C#'s?**
Structural, not nominal; erased at run time, so `as` checks nothing and API data needs validation or a generated client.

**When does the browser send a CORS preflight, and what does a CORS error mean?**
For cross-origin requests a plain form couldn't send (other methods, `Authorization`, JSON). A CORS error means the browser withheld the response; a simple request already ran on the server.

**Where should a SPA keep its access token?**
Not in `localStorage`, readable by every script on the origin; prefer a BFF with an `HttpOnly` cookie.

**Why doesn't mutating state re-render in React, and why can't hooks be conditional?**
React compares the new state to the old with `Object.is`, so a mutated array passed back is "unchanged". Hooks are matched to their slots by call order, so a conditional hook shifts every slot after it.

**How do Angular's change detection and observables differ from React?**
React re-renders on a setter call. Angular used zone.js to re-check after every async callback; since v21 it is zoneless by default, driven by signals. Observables are lazy, multi-value and cancellable.

**Why do frameworks prevent XSS by default, and how does it come back?**
`{text}` becomes a text node, never parsed as HTML. It comes back through `dangerouslySetInnerHTML`, `innerHTML`, `bypassSecurityTrustHtml`, `javascript:` URLs and `eval`.

**What are the Core Web Vitals?**
LCP ≤ 2.5 s, INP ≤ 200 ms, CLS ≤ 0.1, at the 75th percentile of real visits. Lighthouse is lab data and cannot measure INP.

**Red flag:** "Lighthouse says 98, so performance is fine." One synthetic load is not your users.
