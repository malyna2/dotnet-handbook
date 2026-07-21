# Chapter 1: C# Language Mastery

_⏱️ Estimated read time: ~47 min ·     5552 words (study pace)_

A senior .NET developer is not someone who knows more keywords than a mid-level developer. The difference is that a senior understands what the language does *underneath* the syntax: where the bytes live, when work actually happens, why a seemingly innocent line allocates on the heap, and what the compiler is really generating on your behalf. This chapter walks through the C# language from that vantage point. We assume you can already write loops, classes, and `async` methods. Our job is to explain the "why" so deeply that the "what" becomes obvious.

We will move from the memory model up through the type system, then through the features that separate fluent C# from merely working C#: generics, LINQ, delegates, pattern matching, records, spans, and the modern syntax that has landed in the language over the last several releases.

## Value Types and Reference Types: The Foundation

Everything in C#'s type system descends from one distinction: a type is either a **value type** or a **reference type**. This is not a stylistic choice made by the language designers to annoy you; it dictates how instances are stored, copied, compared, and garbage-collected.

A **value type** (anything declared with `struct` or `enum`, plus all the primitives like `int`, `double`, `bool`, and `DateTime`) holds its data *directly*. When you assign one value-type variable to another, you copy the bits. Two variables end up with two independent copies.

A **reference type** (anything declared with `class`, plus arrays, delegates, and strings) holds a *reference* — effectively a managed pointer — to an object that lives elsewhere. When you assign one reference variable to another, you copy the reference, not the object. Now two variables point at the *same* object.

```csharp
struct PointValue { public int X; public int Y; }
class PointRef     { public int X; public int Y; }

var a = new PointValue { X = 1, Y = 1 };
var b = a;          // copies the bits
b.X = 99;
// a.X is still 1 — a and b are independent

var c = new PointRef { X = 1, Y = 1 };
var d = c;          // copies the reference
d.X = 99;
// c.X is now 99 — c and d are the same object
```

This single behavioral difference is the root of a hundred bugs and a hundred optimizations. Internalize it and most of the rest of this section follows naturally.

### Stack vs Heap: Where the Bytes Actually Live

Developers often summarize this as "value types go on the stack, reference types go on the heap." That is a useful first approximation and a dangerous belief to hold literally. The truth is more precise: **the storage location depends on where the variable lives, not only on its type.**

The **stack** is a per-thread region of memory that grows and shrinks with method calls. Each method call pushes a *stack frame* containing its parameters and local variables; when the method returns, the frame is popped and its memory is instantly reclaimed. The stack is extremely fast because allocation is just moving a pointer, and deallocation is automatic.

The **managed heap** is a shared region managed by the garbage collector (GC). Objects here live until the GC determines nothing references them anymore. Heap allocation is more expensive, and reclamation requires the GC to run.

Now the nuance:

- A value type declared as a **local variable** typically lives on the stack.
- A value type that is a **field of a class** lives *inside that class's object on the heap*. An `int` field of a heap object is on the heap.
- A value type **captured by a closure** or used in an `async` method or iterator is often hoisted into a compiler-generated heap object.
- A reference type's **reference** (the pointer-sized handle) follows the same rules as a value type — a local reference variable sits on the stack — but the **object it points to** is on the heap.

> **Gotcha:** "Value types are always on the stack" is false. Reason about *where the variable is declared*. The JIT is also free to keep things in registers or elide allocations entirely (escape analysis is limited in .NET today, but the point stands: the runtime, not you, decides).

Why should you care? Because heap allocations create GC pressure. High allocation rates mean more frequent collections, which mean pauses and CPU spent tracing objects. Much of high-performance .NET is the art of not allocating. That is why `Span<T>`, `struct`, and object pooling exist, and why we return to allocation cost repeatedly in this book.

### Boxing and Unboxing: The Hidden Tax

Because value types and reference types are stored so differently, the runtime needs a bridge when a value type must be treated as an object (its base type is ultimately `System.Object`, a reference type). That bridge is **boxing**.

**Boxing** wraps a value-type instance in a freshly allocated heap object and copies the value into it. **Unboxing** extracts the value back out, checking the type at runtime.

```csharp
int n = 42;
object boxed = n;        // BOXING: allocates a heap object, copies 42 into it
int back = (int)boxed;   // UNBOXING: type-checked, copies the value back out
```

Boxing is silent. There is no keyword, no visible allocation. It happens whenever a value type is assigned to `object`, to a non-generic interface reference, or passed where `object` is expected.

```csharp
// Classic silent boxing traps:
ArrayList list = new();
list.Add(42);            // boxes — ArrayList stores objects

object o = 3.14;         // boxes

int x = 5;
Console.WriteLine("Value: " + x);   // boxes x to call object.ToString via concatenation in some overloads
IComparable cmp = 10;    // boxes — interface is a reference type
```

Each box is a heap allocation plus a copy. In a hot loop this destroys throughput. The fix is almost always **generics**, which we cover next: `List<int>` stores `int`s inline with no boxing, whereas the ancient `ArrayList` boxes every element.

> **Best practice:** Prefer generic collections (`List<T>`, `Dictionary<TKey,TValue>`) over the legacy non-generic ones precisely because generics eliminate boxing. When you implement `Equals`/`GetHashCode` on a struct, also implement `IEquatable<T>` so equality comparisons don't box.

### struct vs class: When to Choose Which

Given the tradeoffs, when should a type be a `struct`?

Microsoft's own guidance is conservative: make a type a `struct` only when it is small (roughly ≤ 16 bytes), logically represents a single value, is immutable, and is not boxed frequently. The reasons:

- **Copy cost.** Every assignment and every method call that takes the struct by value copies the whole thing. A large struct is expensive to pass around.
- **Mutability traps.** A mutable struct behaves surprisingly because copies are everywhere. `list[0].X = 5` on a `List<MutableStruct>` won't even compile (the indexer returns a copy), and modifying a struct returned from a property silently mutates a throwaway copy.

```csharp
// Mutable struct footgun
struct Counter { public int Value; public void Increment() => Value++; }

var counters = new Counter[3];
counters[0].Increment();   // works: array element is addressable in place
// but:
List<Counter> boxedish = new() { new Counter() };
// boxedish[0].Increment();  // compile error: indexer returns a copy
```

Choose `class` for entities with identity, for large aggregates, and for anything with polymorphic behavior. Choose `struct` for small immutable values like `Point`, `Money`, `DateTime`, or a coordinate — cases where copying is cheap and value semantics are what you actually want.

### readonly struct and ref struct

Two modern modifiers sharpen structs for performance-sensitive code.

A **`readonly struct`** guarantees the whole struct is immutable: every field must be `readonly`, and the compiler can therefore skip *defensive copies*. When you call a method on a non-readonly struct held in a `readonly` field or a `readonly` context, the compiler defensively copies it to prevent mutation — a hidden cost. Marking the struct `readonly` removes that.

```csharp
public readonly struct Money
{
    public decimal Amount { get; }
    public string Currency { get; }
    public Money(decimal amount, string currency) => (Amount, Currency) = (amount, currency);
    public Money Add(Money other) => new(Amount + other.Amount, Currency); // returns a new value
}
```

A **`ref struct`** is a struct that is *forbidden from ever living on the heap*. It can only exist on the stack. This is exactly the guarantee `Span<T>` needs. Because a `ref struct` can never be boxed, captured by a lambda, stored in a class field, or used across an `await`, the runtime can safely let it hold a pointer into stack memory or the interior of another object without the GC losing track of it.

```csharp
ref struct StackOnly
{
    public Span<byte> Buffer;   // fine: Span itself is a ref struct
}
// StackOnly cannot be a field of a class, cannot be boxed,
// cannot be used inside an async method or a lambda closure.
```

> **Rule of thumb:** `readonly struct` for immutable value semantics with zero defensive-copy overhead; `ref struct` for stack-only, allocation-free buffers like `Span<T>`.

## Generics: Type-Safe Reuse Without Boxing

Generics let you write algorithms and data structures parameterized by type, resolved at compile time with no boxing and full type safety. The CLR is generics-aware at the runtime level: for reference-type arguments it shares a single JIT-compiled implementation (all references are the same size), but for each value-type argument it produces a specialized version so `List<int>` truly stores `int`s inline.

### Constraints

Constraints tell the compiler what you're allowed to *do* with a type parameter, and they enable specialization.

```csharp
public T CreateAndInit<T>() where T : class, IInitializable, new()
{
    var t = new T();     // allowed by new() constraint
    t.Initialize();      // allowed by IInitializable constraint
    return t;
}
```

The full menu of constraints: `where T : struct` (non-nullable value type), `where T : class` (reference type), `where T : notnull`, `where T : unmanaged` (blittable value type, no references — enables pointer tricks), `where T : new()` (parameterless constructor), `where T : SomeBaseClass`, `where T : ISomeInterface`, and `where T : U` (one type parameter derived from another). Since C# 11 you can also constrain to a delegate or enum type.

> **Gotcha:** The `new()` constraint compiles to `Activator.CreateInstance`, which historically had overhead. For hot paths, a factory delegate parameter can be faster.

### Covariance and Contravariance

Variance is about *substitutability of generic types*. If `Cat` derives from `Animal`, is `IEnumerable<Cat>` usable where `IEnumerable<Animal>` is expected? With variance, yes.

- **Covariance (`out`)**: a type parameter used only in *output* positions (return values) can vary *with* inheritance. `IEnumerable<out T>` is covariant, so `IEnumerable<Cat>` is an `IEnumerable<Animal>`. This is safe because everything you pull out is at least an `Animal`.
- **Contravariance (`in`)**: a type parameter used only in *input* positions (parameters) can vary *against* inheritance. `IComparer<in T>` and `Action<in T>` are contravariant, so an `IComparer<Animal>` can be used as an `IComparer<Cat>` — a comparer that handles any animal certainly handles cats.

```csharp
IEnumerable<string> strings = new List<string> { "a", "b" };
IEnumerable<object> objects = strings;      // covariance: out T

Action<object> printAny = o => Console.WriteLine(o);
Action<string> printString = printAny;      // contravariance: in T
```

> **Why arrays are dangerous:** `string[]` is treated as `object[]` (array covariance), but arrays are *mutable*, so `object[] arr = new string[1]; arr[0] = 42;` compiles and throws `ArrayTypeMismatchException` at runtime. Interface variance with `in`/`out` is verified at compile time precisely to avoid this hole — which is why a parameter can't be marked `out` if used as an input, and vice versa.

### Generic Math and Static Abstract Members

C# 11 introduced **static abstract interface members**, unlocking *generic math*. Before this, you could not write a generic `Sum<T>` because `+` is a static operator and interfaces couldn't require statics. Now `System.Numerics.INumber<T>` and friends declare operators as static abstract members.

```csharp
using System.Numerics;

public static T Sum<T>(IEnumerable<T> values) where T : INumber<T>
{
    T total = T.Zero;               // static abstract property
    foreach (var v in values)
        total += v;                 // static abstract operator +
    return total;
}

int i = Sum(new[] { 1, 2, 3 });          // 6
double d = Sum(new[] { 1.5, 2.5 });      // 4.0
```

This is a genuine leap: one algorithm, zero boxing, works for every numeric type including your own custom ones that implement the interface.

## LINQ Internals: Deferred Execution and Expression Trees

LINQ is the feature most developers use daily and understand least. Two ideas separate confident users from confused ones: **deferred execution** and the **IEnumerable/IQueryable split**.

### Deferred vs Immediate Execution

Most LINQ operators (`Where`, `Select`, `OrderBy`, `Take`) are **deferred**: calling them builds a query object but does *no work*. The work happens only when you *enumerate* the result — with `foreach`, or with a terminal operator like `ToList`, `Count`, `First`, or `Sum`.

```csharp
var query = numbers.Where(n => n > 10);   // nothing runs yet
numbers.Add(20);                          // still nothing has run
foreach (var n in query)                  // NOW the predicate executes
    Console.WriteLine(n);                 // sees 20 — query re-reads the source
```

This has two consequences that bite people constantly:

1. **The query re-executes every time you enumerate it.** Iterating a deferred query twice runs the whole pipeline twice, hitting the database or recomputing everything. If you need the results more than once, materialize with `ToList()`.
2. **Captured variables are read at enumeration time, not definition time.** The query is a recipe, not a snapshot.

```csharp
// Multiple enumeration — a real performance bug
IEnumerable<Order> pending = orders.Where(o => o.IsPending); // deferred
if (pending.Any())                       // enumerates once
    Process(pending.Count());            // enumerates AGAIN
// Two passes over the source. Materialize once: var list = pending.ToList();
```

**Immediate** operators force execution right away: `ToList`, `ToArray`, `ToDictionary`, `Count`, `Sum`, `Average`, `First`, `Single`, `Any`. Anything that returns a concrete collection or a scalar must run the pipeline now.

### IEnumerable vs IQueryable

This is the deepest LINQ concept and the one that determines whether your ORM query runs in the database or drags the whole table into memory.

`IEnumerable<T>` uses **`Func<...>` delegates** — compiled code. LINQ-to-Objects operates in memory, running your lambdas as ordinary methods.

`IQueryable<T>` uses **`Expression<Func<...>>` — expression trees**. Instead of compiled code, the lambda is captured as a *data structure describing the code*. A query provider (Entity Framework, for instance) walks that tree and translates it into something else — SQL, typically.

```csharp
// IQueryable: the lambda becomes an expression tree, translated to SQL
IQueryable<Customer> q = dbContext.Customers.Where(c => c.City == "Kyiv");
// EF generates: SELECT ... FROM Customers WHERE City = 'Kyiv'

// IEnumerable: forces client-side evaluation from here on
IEnumerable<Customer> e = dbContext.Customers.AsEnumerable().Where(c => c.City == "Kyiv");
// Pulls the ENTIRE table into memory, then filters in C#
```

> **Critical pitfall:** Calling `AsEnumerable()`, `ToList()`, or using a method EF can't translate *too early* switches from `IQueryable` to `IEnumerable`, moving all subsequent filtering to the client. A `Where` that should have been one indexed SQL predicate becomes "download a million rows, then filter." Keep operations in `IQueryable` for as long as possible.

### Expression Trees Directly

You can build and inspect expression trees yourself. This is the machinery behind ORMs, mapping libraries, and mocking frameworks.

```csharp
using System.Linq.Expressions;

// A lambda assigned to Expression<T> is captured as a tree, not compiled
Expression<Func<int, bool>> expr = x => x > 5;

var body = (BinaryExpression)expr.Body;
Console.WriteLine(body.NodeType);      // GreaterThan
Console.WriteLine(body.Left);          // x
Console.WriteLine(body.Right);         // 5

// Compile the tree into a real delegate at runtime
Func<int, bool> compiled = expr.Compile();
Console.WriteLine(compiled(10));       // True
```

The key mental model: `Func<int,bool> f = x => x > 5;` gives you *executable code*, while `Expression<Func<int,bool>> e = x => x > 5;` gives you *a description of that code* that another system can read, transform, or translate.

## Delegates, Events, Lambdas, and Closures

A **delegate** is a type-safe reference to a method — effectively an object holding a method pointer (and, for instance methods, the target). `Func`, `Action`, and `Predicate` are just built-in generic delegate types:

- `Action<...>` returns `void`.
- `Func<..., TResult>` returns a value (last type parameter).
- `Predicate<T>` is `Func<T, bool>` by another name.

```csharp
Func<int, int, int> add = (a, b) => a + b;
Action<string> log = msg => Console.WriteLine(msg);
Predicate<int> isEven = n => n % 2 == 0;
```

### Events: Delegates with Guardrails

An **event** is a delegate field wrapped so that outside code can only `+=` (subscribe) and `-=` (unsubscribe) — it cannot invoke the delegate or overwrite the whole invocation list. This encapsulation is the entire point of the `event` keyword.

```csharp
public class Button
{
    public event EventHandler? Clicked;         // subscribers can only add/remove
    protected void OnClick() => Clicked?.Invoke(this, EventArgs.Empty); // only the owner raises it
}
```

> **Gotcha:** Events are a classic source of **memory leaks**. When a long-lived publisher holds a subscription to a short-lived subscriber's handler, the subscriber can't be collected — the delegate keeps it alive. Always unsubscribe (`-=`), or use weak event patterns, when lifetimes differ.

### Closures and the Capture Trap

A **lambda** can capture variables from its enclosing scope, forming a **closure**. The subtle and important truth: **closures capture variables, not values.** The compiler hoists the captured variable into a heap-allocated object shared by the outer method and the lambda. They see the *same* variable, and later mutations are visible to the lambda.

```csharp
// The infamous loop-capture bug (pre-C# 5 foreach, still relevant with for)
var actions = new List<Action>();
for (int i = 0; i < 3; i++)
    actions.Add(() => Console.WriteLine(i));

foreach (var a in actions) a();   // prints 3, 3, 3 — all share the same i
```

All three lambdas captured the *same* `i`, which is `3` by the time they run. The fix is to capture a fresh variable per iteration:

```csharp
for (int i = 0; i < 3; i++)
{
    int copy = i;                        // new variable each iteration
    actions.Add(() => Console.WriteLine(copy));
}
// prints 0, 1, 2
```

> **Note:** Since C# 5, `foreach` creates a fresh loop variable per iteration, so `foreach` doesn't exhibit this bug — but the classic `for` loop still does. Also be aware closures allocate: capturing a variable creates a heap object, so tight loops that create closures generate GC pressure.

## Nullable Reference Types

Historically, any reference could be `null`, and `NullReferenceException` was the most common .NET crash. **Nullable reference types (NRT)**, enabled with `<Nullable>enable</Nullable>`, flip the default: a plain `string` is now considered *non-nullable*, and you must write `string?` to allow null. The compiler then performs *flow analysis* and warns when you might dereference a null.

```csharp
#nullable enable
string name = null;        // warning: assigning null to non-nullable
string? maybe = null;      // fine, explicitly nullable

void Print(string? s)
{
    // Console.WriteLine(s.Length);   // warning: possible null dereference
    if (s is not null)
        Console.WriteLine(s.Length);  // OK: flow analysis narrowed s to non-null
}
```

Crucially, NRT is a **compile-time-only** feature enforced by warnings. It does not add runtime null checks; the annotations are metadata. The **null-forgiving operator** `!` tells the compiler "trust me, this isn't null" — use it sparingly, because it silences the very safety net you enabled.

```csharp
string definitelyThere = maybe!;   // suppress the warning — you own the risk
```

> **Best practice:** Turn NRT on for new projects and treat the warnings as errors. It moves an entire class of bugs from production runtime to your editor.

## Pattern Matching

Pattern matching lets you test a value's shape and extract data in one expressive step. It has grown from a simple `is` check into a small sublanguage.

```csharp
// Type + declaration pattern
if (shape is Circle c) return Math.PI * c.Radius * c.Radius;

// switch expression with property, relational, and logical patterns
decimal Discount(Customer cust) => cust switch
{
    { Orders.Count: > 100 } => 0.2m,             // property pattern
    { IsVip: true }         => 0.15m,            // property pattern
    { Age: >= 65 }          => 0.1m,             // relational pattern
    null                    => 0m,               // constant pattern
    _                       => 0.05m             // discard = default
};

// positional pattern (uses Deconstruct)
static string Quadrant(Point p) => p switch
{
    (0, 0)             => "origin",
    (var x, var y) when x > 0 && y > 0 => "first",
    _                  => "other"
};

// list patterns (C# 11)
int[] data = { 1, 2, 3 };
string desc = data switch
{
    []            => "empty",
    [var single]  => $"one element: {single}",
    [var f, .., var l] => $"first {f}, last {l}",   // slice pattern ..
    _             => "many"
};
```

The `switch` *expression* (distinct from the older `switch` statement) returns a value, is exhaustive-checked by the compiler, and reads top-to-bottom with the first match winning. This is a functional, declarative style that replaces sprawling `if/else` ladders.

## Records, Value Equality, and with Expressions

A **record** is a reference type (or `record struct` for a value type) that the compiler outfits with **value-based equality**, a readable `ToString`, and nondestructive mutation. Records exist for *data* — DTOs, domain values, messages — where two instances with the same contents should be considered equal.

```csharp
public record Person(string First, string Last, int Age);   // positional record

var p1 = new Person("Ada", "Lovelace", 36);
var p2 = new Person("Ada", "Lovelace", 36);
Console.WriteLine(p1 == p2);        // True — value equality, compares all members
Console.WriteLine(p1);              // Person { First = Ada, Last = Lovelace, Age = 36 }
```

Contrast with a `class`, where `==` compares references, so `p1 == p2` would be `False` unless you hand-wrote `Equals`/`GetHashCode`. The compiler generates all of that for records.

The **`with` expression** performs *nondestructive mutation*: it creates a copy with some properties changed, leaving the original untouched — ideal for immutable data.

```csharp
var older = p1 with { Age = 37 };   // new Person, only Age differs
// p1 is unchanged
```

A **`record struct`** gives value equality on a value type (structs already compare by value, but records add the tuned `Equals`/`GetHashCode`/`ToString` and `with`). Use `readonly record struct` for immutable value objects — it's the most concise way to define something like `Money` or `Coordinate`.

```csharp
public readonly record struct Coordinate(double Lat, double Lng);
```

> **Note:** Records use `init`-only setters by default, so positional record properties are immutable after construction. This immutability is a feature — it makes value equality meaningful and makes records safe to share.

## Tuples and Deconstruction

**Value tuples** (`(int, string)`) are lightweight, unnamed-or-named aggregates backed by the `ValueTuple` struct — no heap allocation, unlike the old `Tuple` class. They shine for returning multiple values without defining a type.

```csharp
(int min, int max) MinMax(int[] xs) => (xs.Min(), xs.Max());

var result = MinMax(new[] { 3, 1, 4, 1, 5 });
Console.WriteLine($"{result.min}..{result.max}");   // 1..5

// Deconstruction into separate variables
var (lo, hi) = MinMax(new[] { 3, 1, 4 });
```

**Deconstruction** works for any type that provides a `Deconstruct` method (records generate one automatically). It's the mechanism behind positional patterns.

```csharp
public class Rect
{
    public int W { get; init; }
    public int H { get; init; }
    public void Deconstruct(out int w, out int h) => (w, h) = (W, H);
}

var (w, h) = new Rect { W = 4, H = 3 };   // calls Deconstruct
```

## Span<T>, Memory<T>, and stackalloc

`Span<T>` is one of the most important performance features in modern .NET. It is a `ref struct` representing a **contiguous region of memory** — a slice — regardless of whether that memory is an array, a stack buffer, or unmanaged memory. Because it's a *view*, slicing is allocation-free: no copying, just a pointer and a length.

```csharp
int[] array = { 10, 20, 30, 40, 50 };
Span<int> span = array;
Span<int> middle = span.Slice(1, 3);   // view over {20, 30, 40}, no allocation
middle[0] = 99;                        // array[1] is now 99 — same memory
```

Because `Span<T>` is a `ref struct`, it lives only on the stack and cannot be stored in a field, boxed, or captured across `await`. When you need slice-like semantics that *can* live on the heap or cross async boundaries, use **`Memory<T>`**, its heap-friendly cousin (call `.Span` to get a `Span<T>` for the actual work).

**`stackalloc`** allocates a buffer directly on the stack, and since C# assigns it to a `Span<T>`, you get a fast, GC-free scratch buffer:

```csharp
Span<byte> buffer = stackalloc byte[128];   // stack allocation, zero heap pressure
Utf8Formatter.TryFormat(12345, buffer, out int written, default);
// use buffer[..written] with no allocation
```

> **Best practice:** Reach for `Span<T>` in parsing, serialization, and buffer manipulation to eliminate intermediate allocations. Keep `stackalloc` sizes small and bounded — the stack is limited (typically ~1 MB), and overflowing it crashes the process with no catchable exception.

## IDisposable, IAsyncDisposable, and the Dispose Pattern

The GC reclaims *managed memory* automatically, but it knows nothing about *unmanaged resources*: file handles, sockets, database connections, native memory. `IDisposable` is the contract for releasing those deterministically.

```csharp
using (var stream = new FileStream("data.bin", FileMode.Open))
{
    // use stream
}   // Dispose() called automatically here, even on exception

// using declaration (C# 8) — disposes at end of enclosing scope
using var reader = new StreamReader("data.txt");
```

For a class that owns unmanaged resources directly, the **full Dispose pattern** coordinates deterministic disposal with the GC's finalizer as a safety net:

```csharp
public class NativeBuffer : IDisposable
{
    private IntPtr _handle = Marshal.AllocHGlobal(1024);
    private bool _disposed;

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);   // we cleaned up; skip the finalizer
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;
        if (disposing)
        {
            // release managed resources here
        }
        // release unmanaged resources
        Marshal.FreeHGlobal(_handle);
        _handle = IntPtr.Zero;
        _disposed = true;
    }

    ~NativeBuffer() => Dispose(false);   // finalizer: last-resort cleanup
}
```

The `bool disposing` distinction matters: when called from `Dispose()` (`disposing == true`), other managed objects are still alive and safe to touch; when called from the finalizer (`disposing == false`), they may already be collected, so you only release unmanaged resources.

**`IAsyncDisposable`** exists for resources whose cleanup involves I/O (flushing a buffer, closing a network stream) that shouldn't block a thread:

```csharp
await using var conn = new AsyncDbConnection();   // DisposeAsync() awaited at scope end
```

> **Best practice:** Most classes don't need a finalizer. Only write one if you directly hold unmanaged resources. If you merely *contain* other `IDisposable` fields, implement `Dispose` to dispose them and skip the finalizer entirely (it has real overhead — finalizable objects survive an extra GC generation).

## Iterators and yield return

The `yield return` keyword turns a method into an **iterator** — the compiler generates a state machine implementing `IEnumerator<T>` that produces values *lazily*, one at a time, pausing between them.

```csharp
public static IEnumerable<int> Fibonacci()
{
    int a = 0, b = 1;
    while (true)                       // infinite sequence — safe because it's lazy
    {
        yield return a;
        (a, b) = (b, a + b);
    }
}

foreach (var n in Fibonacci().Take(10))   // only 10 values ever computed
    Console.Write($"{n} ");               // 0 1 1 2 3 5 8 13 21 34
```

Each `yield return` hands one value to the consumer and *freezes the method's state* until the consumer asks for the next. This is deferred execution again — the body doesn't run at all until enumeration begins, and it runs only as far as needed. It's what makes streaming large or infinite sequences memory-efficient.

> **Gotcha:** Because iterators are deferred, exceptions and argument validation in an iterator method don't fire until the caller enumerates. If you want eager argument checks, split the method: a normal method that validates, then calls a private iterator.

## Extension Methods and Static Abstract Members

An **extension method** is a static method that the compiler lets you call *as if* it were an instance method on the first parameter's type, marked with `this`. This is how LINQ appears to add hundreds of methods to `IEnumerable<T>` without modifying it.

```csharp
public static class StringExtensions
{
    public static bool IsNullOrBlank(this string? s) => string.IsNullOrWhiteSpace(s);
}

bool empty = "   ".IsNullOrBlank();   // reads like an instance call; compiles to a static call
```

Extension methods are purely compile-time sugar — there's no runtime magic, just a static call the compiler rewrites. They can't access private members and are resolved by the `using` namespaces in scope.

We covered **static abstract interface members** under generic math; the broader point is that interfaces can now declare `static` members (operators, factory methods, constants), which combined with generics enables abstractions that were previously impossible in C#.

## Attributes and Reflection

**Attributes** attach declarative metadata to code — classes, methods, properties, parameters. They do nothing by themselves; they're inert data compiled into the assembly, waiting to be read by **reflection** or by tooling.

```csharp
[AttributeUsage(AttributeTargets.Property)]
public sealed class DisplayNameAttribute : Attribute
{
    public string Name { get; }
    public DisplayNameAttribute(string name) => Name = name;
}

public class Product
{
    [DisplayName("Product Title")]
    public string Title { get; set; } = "";
}
```

**Reflection** is the API for inspecting types and metadata at runtime and even invoking members dynamically.

```csharp
var prop = typeof(Product).GetProperty(nameof(Product.Title))!;
var attr = prop.GetCustomAttribute<DisplayNameAttribute>();
Console.WriteLine(attr?.Name);   // "Product Title"

// Dynamic invocation
var product = Activator.CreateInstance<Product>();
prop.SetValue(product, "Widget");
Console.WriteLine(prop.GetValue(product));   // "Widget"
```

Reflection powers serializers, DI containers, ORMs, and validators. But it's **slow** compared to direct calls (it bypasses JIT optimizations and does runtime lookups) and it defeats trimming/AOT analysis because the linker can't see reflective usage statically.

> **Best practice:** Use reflection for configuration-time work (wiring up a container once at startup), not in hot paths. When you must reflect repeatedly, cache `PropertyInfo`/`MethodInfo` objects or compile delegates from expression trees. Better yet, consider source generators.

## Source Generators (Conceptual)

A **source generator** is a compiler plugin that runs *during compilation*, inspects your code (via the Roslyn syntax and semantic model), and *emits additional C# source* that gets compiled alongside your own. It's the modern answer to problems previously solved with runtime reflection or reflection-emit.

The mental model: instead of discovering metadata and building behavior at *runtime* (slow, AOT-hostile), a source generator does that discovery at *compile time* and writes plain code you could have written by hand. The result is faster, trimming-friendly, and debuggable.

```csharp
// You write a partial declaration with an attribute:
[JsonSerializable(typeof(Person))]
public partial class MyJsonContext : JsonSerializerContext { }

// System.Text.Json's source generator emits the other 'partial' half at compile time:
// fully typed, reflection-free serialization code for Person.
```

You don't usually write generators; you consume them. `System.Text.Json`, `LoggerMessage`, regex (`[GeneratedRegex]`), and many libraries ship generators that replace reflection with generated code. Knowing they exist explains *why* modern .NET can serialize JSON or match regex with zero runtime reflection and full Native AOT compatibility.

## Modern Syntax You Should Be Using

The language has accumulated syntax that removes ceremony. A senior developer wields these fluently.

**File-scoped namespaces** drop a level of indentation for the common one-namespace-per-file case:

```csharp
namespace MyApp.Services;   // everything below is in this namespace, no braces

public class OrderService { }
```

**Global usings** declare a `using` once for the whole project (often in a `GlobalUsings.cs`, or auto-generated via `<ImplicitUsings>enable</ImplicitUsings>`):

```csharp
global using System;
global using System.Collections.Generic;
```

**Primary constructors** (C# 12, now on any class/struct) let you declare constructor parameters on the type itself and use them anywhere in the body:

```csharp
public class Repository(DbContext db, ILogger<Repository> logger)
{
    public Task<int> CountAsync() => db.Set<Order>().CountAsync();   // params in scope
    public void Log(string m) => logger.LogInformation(m);
}
```

**Required members** force initialization at construction without writing a constructor, checked by the compiler:

```csharp
public class Config
{
    public required string ConnectionString { get; init; }
    public required int Port { get; init; }
}
var c = new Config { ConnectionString = "...", Port = 5432 };   // omit either → compile error
```

**Collection expressions** (C# 12) unify collection initialization with `[...]` and the spread operator `..`:

```csharp
int[] a = [1, 2, 3];
List<int> b = [0, ..a, 4];        // spread: [0, 1, 2, 3, 4]
Span<int> s = [1, 2, 3];          // works for spans too
```

**Raw string literals** handle text with quotes, backslashes, and newlines without escaping — perfect for JSON, SQL, and regex:

```csharp
string json = """
    {
        "name": "Ada",
        "active": true
    }
    """;   // no escaping; leading whitespace trimmed to the closing delimiter's column
```

The triple-quote (or more) delimiters mean embedded `"` need no escaping, and the closing quotes' indentation sets the baseline that's stripped from every line — so your literal stays visually aligned with your code.

## Bringing It Together

The through-line of this chapter is that C#'s surface features rest on a small number of deep mechanisms: the value/reference divide governs memory and copying; generics give reuse without boxing; deferred execution and expression trees make LINQ both lazy and translatable; closures capture variables by reference and can leak or surprise; and `ref struct`/`Span<T>` let you write allocation-free code with compile-time safety. The modern syntax layered on top — records, patterns, primary constructors, collection expressions — is not cosmetic; each one encodes an intent (immutability, exhaustiveness, data-shaping) that the compiler can check and optimize.

Master these fundamentals and you stop guessing about performance and behavior. You'll know, before you run the profiler, which line boxes, which query hits the database twice, and which struct is being copied defensively. That predictive understanding — reasoning about what the runtime *will* do — is precisely what senior means.
