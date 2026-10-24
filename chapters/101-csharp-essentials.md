# Chapter 1: C# Essentials

Two facts explain most of what everyday C# does: what an assignment copies, what `==` compares, which method a call through a base class runs, where a null still gets past the compiler, when a LINQ query runs, what a lambda captures. A variable holds either a value or a reference to a shared object. And the compiler turns many lines into something else: an operator or a method picked from the variable's declared type, a hidden object that holds captured variables, a state machine that runs later.

What the stack and the heap are, and how the garbage collector reclaims what you allocate, is [Chapter 3: How .NET Runs Your Code](#chapter-3-how-net-runs-your-code). `Span<T>`, reflection and source generators are in [Chapter 17: Runtime Internals and Performance](#chapter-17-runtime-internals-and-performance).

## Value Types and Reference Types: The Foundation

Every C# type is either a **value type** or a **reference type**, and the distinction decides how instances are stored, copied, compared and collected.

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

An assignment, or passing an argument by value, copies what the variable holds: a struct's fields, or a class's reference. A struct that contains a reference-type field copies only that reference, so the two copies still share the object behind it.

### Stack vs Heap: Where the Bytes Actually Live

"Value types go on the stack, reference types on the heap" is folklore: **the storage location depends on where the variable lives, not only on its type.**

In brief: the **stack** is per-thread memory that grows and shrinks with method calls — allocation is a pointer bump, and a returning method reclaims its frame instantly. The **managed heap** is the shared region where objects live until the garbage collector proves them unreachable; [Chapter 3](#the-memory-model-stack-vs-managed-heap) covers both, and how the collector works. What matters at the language level is where a given *variable's* data ends up, and the answer is more subtle than the folklore:

- A value type declared as a **local variable** typically lives on the stack.
- A value type that is a **field of a class** lives *inside that class's object on the heap*. An `int` field of a heap object is on the heap.
- A value type **captured by a closure** or used in an `async` method or iterator is often hoisted into a compiler-generated heap object.
- A reference type's **reference** (the pointer-sized handle) follows the same rules as a value type — a local reference variable sits on the stack — but the **object it points to** is on the heap.

> **Gotcha:** The JIT is free to keep values in registers, and since .NET 9 and 10 it stack-allocates some objects that provably don't outlive the method (boxes, small arrays, some delegates). The runtime decides, not the type.

It matters because heap allocations drive garbage collection: a higher allocation rate means more frequent collections, with pauses and CPU spent tracing objects. `Span<T>`, structs and object pooling exist to avoid them ([Chapter 17: Runtime Internals and Performance](#chapter-17-runtime-internals-and-performance)).

### Boxing and Unboxing: The Hidden Tax

When a value type must be treated as an object (`System.Object` is a reference type), the runtime **boxes** it: it allocates a heap object and copies the value into it. **Unboxing** extracts the value back out, checking the type at runtime.

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
Console.WriteLine("Value: {0}", x);  // boxes: the parameter is object
IComparable cmp = 10;    // boxes — interface is a reference type
```

Each box is a heap allocation plus a copy. In a hot loop this destroys throughput. The fix is almost always **generics**, the next section.

> **Best practice:** Prefer generic collections (`List<T>`, `Dictionary<TKey,TValue>`) over the legacy non-generic ones precisely because generics eliminate boxing. When you implement `Equals`/`GetHashCode` on a struct, also implement `IEquatable<T>` so equality comparisons don't box.

### struct vs class: When to Choose Which

Microsoft's design guidelines are conservative: a `struct` only when the type is small (an instance size under 16 bytes), logically a single value, immutable, and rarely boxed. The reasons:

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

A **`ref struct`** is a struct that is *forbidden from ever living on the heap*. It can only exist on the stack. This is exactly the guarantee `Span<T>` needs ([Chapter 17](#chapter-17-runtime-internals-and-performance) covers it). Because a `ref struct` can never be boxed, captured by a lambda, stored in a class field, or used across an `await`, the runtime can safely let it hold a pointer into stack memory or the interior of another object without the GC losing track of it.

```csharp
ref struct StackOnly
{
    public Span<byte> Buffer;   // fine: Span itself is a ref struct
}
// StackOnly cannot be a field of a class, cannot be boxed,
// cannot be used inside an async method or a lambda closure.
```

## Classes, Inheritance and Virtual Dispatch

A class bundles state with the code allowed to change it. The object-oriented vocabulary that interviews test (encapsulation, inheritance, polymorphism) rests on three mechanisms: the compiler checks who may touch a member; the runtime builds an object from the base class outward; and a virtual call finds its target in the object's type at run time, while every other call is fixed at compile time. *When* to use inheritance is a design question, and [Chapter 10: Design Basics](#chapter-10-design-basics) owns it.

### Encapsulation and Access Modifiers

Encapsulation means callers change an object's state only through members that keep it valid. The compiler enforces it with six access levels:

| Modifier | Who can use the member |
|---|---|
| `public` | Any code |
| `private` | Code inside the same type, on any instance of it, not only `this` |
| `protected` | The type and types derived from it, in any assembly |
| `internal` | Any code in the same assembly |
| `protected internal` | The same assembly **or** a derived type anywhere (the union) |
| `private protected` | A derived type **and** in the same assembly (the intersection) |

Members default to `private` and top-level types to `internal`, so leaving the modifier off gives the narrowest sensible choice. Access is checked per type, not per object: an `Order` method may read another `Order`'s private fields, which is how hand-written `Equals` methods work. `internal` stops at the assembly boundary; `[assembly: InternalsVisibleTo("MyApp.Tests")]` opens it to a test project.

A **field** is storage. A **property** is a pair of methods (`get_Total`, `set_Total`) that reads like a field at the call site, and an auto-property (`public decimal Total { get; private set; }`) is one whose backing field the compiler declares for you. Public data goes behind properties for a mechanical reason: reading a field and calling a getter compile to different IL, so turning a public field into a property later breaks every assembly compiled against it. The property is also the only place to add validation, change notification or a narrower setter (`private set`, `init`); C# 14's `field` keyword adds that logic without a hand-declared field ([Modern Syntax You Should Be Using](#modern-syntax-you-should-be-using)).

### Constructors and Initialization Order

A constructor runs once per object to establish its invariants. A **static constructor** runs once per type, before the type's first use, and the runtime guarantees that even when threads race to it. In a class hierarchy the steps run in an order most people get wrong:

```csharp
var order = new Order();
Console.WriteLine($"after construction: Status = {order.Status}");

class Entity
{
    static Entity() => Console.WriteLine("Entity static constructor");
    protected Entity() { Console.WriteLine("Entity constructor body"); Describe(); }
    protected virtual void Describe() { }
}

class Order : Entity
{
    private readonly string _currency = Log("Order field initializer");
    public string? Status { get; private set; }

    static Order() => Console.WriteLine("Order static constructor");
    public Order() { Console.WriteLine("Order constructor body"); Status = "New"; }

    protected override void Describe() =>     // called from Entity's constructor
        Console.WriteLine($"Order.Describe: currency {_currency}, Status {Status ?? "null"}");

    static string Log(string message) { Console.WriteLine(message); return "EUR"; }
}
```

```text
Order static constructor
Order field initializer
Entity static constructor
Entity constructor body
Order.Describe: currency EUR, Status null
Order constructor body
after construction: Status = New
```

- **`new Order()` touches `Order` first**, so its static constructor runs first; `Entity`'s runs only when `Entity`'s constructor is about to be called.
- **The derived class's field initializers run before the base constructor.** The compiler places them at the top of `Order`'s constructor, ahead of the call to `base()`; then the base constructor runs, then the rest of `Order`'s body.
- **The object is an `Order` from its first instruction**, so the virtual call in the base constructor already runs `Order.Describe`. It sees the field initializer's value, but not the `Status` that `Order`'s constructor body hasn't assigned yet.

> **Pitfall.** A virtual call from a constructor hands a half-built object to a derived class (analyzer CA2214 flags it); keep constructors to checking arguments and assigning fields. A static constructor that throws is worse: the runtime records the failure, and every later use of the type in that process throws `TypeInitializationException` with the original exception inside, so the type stays broken until the process restarts.

### Inheritance and Virtual Dispatch

A derived class inherits its base class's members and can be used wherever the base is expected. A class has exactly one base class (`object` if you name none); several contracts come from interfaces. Four keywords decide what a call through a base-typed variable runs:

- **`virtual`** lets derived classes replace a method, and **`override`** replaces it. **`abstract`** is `virtual` without a body, which every non-abstract derived class must override.
- **`new`** declares a *different* method that happens to share the name. It hides the base method instead of replacing it; without the keyword the compiler warns (CS0108) and hides anyway.
- **`sealed`** on a class forbids deriving from it; on an `override` it stops further overriding.

```csharp
Dog dog = new();
Animal animal = dog;                    // the same object, declared as the base type

Console.WriteLine(dog.Speak());         // Woof
Console.WriteLine(animal.Speak());      // Woof: the override replaced the slot; the object decides
Console.WriteLine(dog.Kind());          // dog
Console.WriteLine(animal.Kind());       // animal: `new` added a second method; the declared type decides

class Animal
{
    public virtual string Speak() => "...";
    public string Kind() => "animal";
}

class Dog : Animal
{
    public override string Speak() => "Woof";
    public new string Kind() => "dog";  // hides Animal.Kind; without `new`, warning CS0108
}
```

The mechanism is the **method table**. Every object on the heap starts with a pointer to its type's method table, which holds one slot per virtual method. A derived type's table starts as a copy of its base's, and `override` writes the derived method into the inherited slot. A virtual call compiles to "call slot N of this object's table", so it runs whatever the *object's* type put there: `Dog.Speak`. A non-virtual call is bound by the compiler to the member of the variable's *declared* type and cannot change at run time, and a `new` method is just another method: through `Animal`, the call is `Animal.Kind`. `sealed` helps the JIT here too: when it knows no subclass can override a method, it can call it directly and inline it, and analyzer CA1852 suggests sealing internal types that nothing derives from.

> **Pay attention.** **`override` replaces, `new` hides: what a call through the base type runs.**
>
> An overridden method is found at run time in the object's method table, so the override runs whatever the variable's declared type. A `new` method takes no slot of the base type: the compiler binds each call by the declared type, so `animal.Kind()` runs `Animal.Kind` although the object is a `Dog`. The bug shows when a framework, a `List<Animal>` or any method that takes the base type calls the member: your `new` method silently never runs. Static methods and operators follow the same rule, which is why `==` on two `object` variables compares references ([Records, Value Equality, and with Expressions](#records-value-equality-and-with-expressions)).
>
> Fix: to change behaviour, make the base member `virtual` or `abstract` and `override` it. Treat warning CS0108 as an error, and write `new` only to shadow on purpose a member you can't change, such as one a library later added to its base class.

### Abstract Classes vs Interfaces

| | Abstract class | Interface |
|---|---|---|
| Instance fields and constructors | Yes | No (static fields only) |
| How many a type can have | One base class | Any number |
| Member bodies | Any member, `virtual` or not | Default implementations (C# 8), reachable only through the interface |
| Static members a type must supply | No | `static abstract` members (C# 11) |
| Adding a member later | A `virtual` member with a body breaks no one | Breaks every implementer, unless it has a default body |
| Typical use | A family of types that share state and code | A capability that unrelated types offer; a seam for dependency injection and test doubles |

A default implementation stays with the interface; the class does not inherit it:

```csharp
public interface INotifier
{
    void Send(string message);
    void SendAll(IEnumerable<string> messages)      // default implementation (C# 8)
    {
        foreach (var m in messages) Send(m);
    }
}

public class EmailNotifier : INotifier
{
    public void Send(string message) => Console.WriteLine($"email: {message}");
}

var email = new EmailNotifier();
// email.SendAll(["a", "b"]);         // CS1061: the class does not inherit the default body
INotifier notifier = email;
notifier.SendAll(["a", "b"]);         // the interface's body, calling EmailNotifier.Send
```

Static abstract members are what make generic math possible; [Generic Math and Static Abstract Members](#generic-math-and-static-abstract-members) shows them.

**Composition over inheritance.** Inheritance couples a derived class to its base's details: it receives every member whether it fits or not, and a change inside the base, such as a method that starts calling a virtual method the subclass overrides, can break subclasses nobody looked at (the *fragile base class* problem). Composition keeps the collaborator in a field, usually behind an interface, calls only what it needs, and lets a test or the container swap it. Inherit for a real "is-a" from a base designed for it, and seal the rest; [Chapter 10](#chapter-10-design-basics) builds its patterns on composition.

### Static Classes and Members

A `static` member belongs to the type, not to an instance: one copy per process, shared by every thread. A `static class` holds only static members; the compiler emits it as `abstract sealed`, so it can be neither instantiated nor derived from. Three consequences follow from "one copy per type":

- **Mutable static state is shared by every request.** A `static` dictionary used as a cache needs the same care as any shared state ([Thread Safety: Sharing State Correctly](#thread-safety-sharing-state-correctly) in Chapter 4).
- **Each closed generic type is a separate type**, so `Cache<int>.Count` and `Cache<string>.Count` are two different fields.
- **A static dependency can't be replaced in a test.** Code that calls `DateTime.UtcNow` or a static `Db.Save` directly is welded to them; an injected interface or `TimeProvider` ([Chapter 15](#timeprovider-testable-time-net-8)) can be swapped.

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

The full menu of constraints: `where T : struct` (non-nullable value type), `where T : class` (reference type), `where T : notnull`, `where T : unmanaged` (blittable value type, no references — enables pointer tricks), `where T : new()` (parameterless constructor), `where T : SomeBaseClass`, `where T : ISomeInterface`, and `where T : U` (one type parameter derived from another). Since C# 7.3 you can also constrain to a delegate or enum type.

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

## Records, Value Equality, and with Expressions

A struct compares by contents and a class by reference. A record gives a type contents-based comparison without hand-written code: it is a reference type (or `record struct` for a value type) for which the compiler generates **value-based equality**, a readable `ToString` and nondestructive mutation. Records are for *data* (DTOs, domain values, messages), where two instances with the same contents should be equal.

```csharp
public record Person(string First, string Last, int Age);   // positional record

var p1 = new Person("Ada", "Lovelace", 36);
var p2 = new Person("Ada", "Lovelace", 36);
Console.WriteLine(p1 == p2);        // True — value equality, compares all members
Console.WriteLine(p1);              // Person { First = Ada, Last = Lovelace, Age = 36 }
```

For a `class`, `==` compares references, so `p1 == p2` would be `False` unless you hand-wrote `Equals`, `GetHashCode` and the operators; for a record the compiler generates them.

The **`with` expression** performs *nondestructive mutation*: it creates a copy with some properties changed and leaves the original untouched.

```csharp
var older = p1 with { Age = 37 };   // new Person, only Age differs
// p1 is unchanged
```

A **`record struct`** gives a value type generated `Equals`, `GetHashCode`, `ToString`, `==` and `with` (a plain struct has no `==`, and its default `Equals` relies on reflection). `readonly record struct` is the most concise immutable value object, such as `Money` or `Coordinate`.

```csharp
public readonly record struct Coordinate(double Lat, double Lng);
```

> **Note:** Positional properties of a `record` and a `readonly record struct` are `init`-only, so immutable after construction; a positional `record struct` gets read-write properties. Immutability is what makes value equality safe: a record whose hash changes while it is a dictionary key is lost in the dictionary.

> **Pay attention.** **Why `==` and `Equals` can disagree, and what a record really compares.**
>
> - **`==` is chosen at compile time, from the declared types.** Operators are static, so for two `object` variables the compiler binds `object`'s `==`, a reference comparison, even when both hold equal strings; `a.Equals(b)` is virtual, runs `string.Equals` and returns `true`. A generic method constrained `where T : class` binds the same reference comparison even when `T` is `string`. Neither case gets a compiler warning.
> - **A record compares field by field, each with `EqualityComparer<T>.Default`**, after checking that both objects have the same runtime type. A `List<T>` or array field compares by reference, so two records holding equal-looking lists are unequal.
> - **`with` is a shallow copy.** The copy shares every reference-type member with the original: adding to the copy's list changes the original's.
>
> Fix: compare through the type you mean (`string`, not `object`), and keep records to values such as numbers, strings and nested records, or write `Equals(R? other)` and `GetHashCode` yourself, comparing collections with `SequenceEqual`.

## Strings: Immutable, Built, Interned, Compared

`string` is a reference type that behaves like a value. Its characters live inside the string object (a length, then UTF-16 code units), no method changes them after construction, and `==` and `Equals` are overloaded to compare the characters. Immutability is what makes a string safe to share between threads, to return without a defensive copy and to use as a dictionary key, whose hash code must never change. The price: every method that seems to modify a string (`Replace`, `Trim`, `ToUpper`, `+`) allocates a new one.

**Building.** `s += x` allocates a string of length `s.Length + x.Length` and copies both into it, so in a loop each iteration copies everything built so far. Appending one character n times copies 1 + 2 + … + n, about n²/2 characters: 10,000 appends copy some 50 million characters and leave 9,999 dead strings for the GC. `StringBuilder` appends into a growable buffer and copies once, in `ToString()`, so the work is linear. It doesn't help when the number of pieces is fixed: `a + b + c` compiles to one `string.Concat(a, b, c)` call that adds up the lengths and allocates once, and a `StringBuilder` there only adds an allocation. For a collection, `string.Join` does the same single allocation. [Chapter 17](#stringbuilder-and-string-mechanics) benchmarks the difference.

**Interpolation.** Since C# 10, `$"Order {id}: {total}"` assigned to a `string` compiles to a `DefaultInterpolatedStringHandler`: it writes each piece into a pooled buffer, formats value-type holes without boxing, and allocates only the final string. Each hole is formatted with the **current culture**, which suits text a person reads and breaks text a program reads (CSV, URLs, cache keys): on a server running as `de-DE`, `1234.5m` becomes `1234,5`. Pass the culture with `string.Create(CultureInfo.InvariantCulture, $"...")` or `FormattableString.Invariant($"...")`. When the final length is known up front, the other overload, `string.Create(length, state, (span, state) => ...)`, writes the characters straight into the new string's memory, with no buffer at all. In log calls, use message templates instead ([Chapter 9](#chapter-9-exceptions-logging-and-first-diagnosis) shows why).

**Interning.** The runtime keeps an *intern pool* with one object per distinct value. String literals usually go into it (the documentation doesn't guarantee it), so equal literals are typically one object, while a string built at run time is a new object. That is why string equality must compare characters, and why `==` on two `object` variables, a reference comparison, can say `true` for two literals and `false` for a literal and an equal built string. `string.Intern(s)` returns the pooled object, but the pool generally keeps its strings until the process ends, so never intern unbounded input such as user data.

**Comparing.** `==`, `Equals` and `Contains` compare ordinally, code unit by code unit. `StartsWith(string)`, `IndexOf(string)`, `CompareTo`, `ToUpper()` and sorting use the current culture unless you pass a `StringComparison`. Pass one every time; [Chapter 15: Dates, Money and Strings](#chapter-15-dates-money-and-strings) explains the culture rules and the bugs they cause.

```csharp
string literal = "order-42";
string built = "order-" + 42.ToString();                       // created at run time
Console.WriteLine(literal == built);                           // True: string's == compares characters
Console.WriteLine(ReferenceEquals(literal, built));            // False: two objects; only literals are interned
Console.WriteLine(ReferenceEquals(literal, string.Intern(built)));   // True: the pool hands back the literal

string s = "";
for (int i = 0; i < 5; i++) s += i;          // each += allocates a new string and copies everything so far
var sb = new StringBuilder();
for (int i = 0; i < 5; i++) sb.Append(i);    // writes into a growable buffer; one string at ToString()
Console.WriteLine(s == sb.ToString());       // True, "01234" both ways

CultureInfo.CurrentCulture = new CultureInfo("de-DE");
decimal price = 1234.5m;
Console.WriteLine($"{price}");                                          // 1234,5: the current culture
Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{price}"));   // 1234.5: the culture you pass
```

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

The `switch` *expression* (distinct from the older `switch` statement) returns a value, is exhaustive-checked by the compiler, and reads top-to-bottom with the first match winning.

## Nullable Reference Types

**Nullable reference types (NRT)**, enabled with `<Nullable>enable</Nullable>` (the default in new projects since .NET 6), flip C#'s default: a plain `string` is *non-nullable*, `string?` allows null, and the compiler's *flow analysis* warns when you might dereference a null.

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

NRT is **compile-time only**: the annotations are metadata, and the compiler adds no runtime null checks. The **null-forgiving operator** `!` tells the compiler "trust me, this isn't null"; use it sparingly, because it silences the safety net you enabled.

```csharp
string definitelyThere = maybe!;   // suppress the warning — you own the risk
```

> **Pay attention.** **Where a null gets past the compiler.**
>
> Flow analysis covers the code the compiler compiles, method by method. A null arrives wherever it doesn't run:
>
> - **Data from outside.** A deserializer or an ORM creates objects at run time. With default options, `System.Text.Json` ([Chapter 5](#serialization-systemtextjson-vs-newtonsoftjson)) puts a JSON `null`, or `null` for a missing constructor parameter, into a non-nullable `string`. Since .NET 9, `RespectNullableAnnotations` rejects the explicit `null` and `RespectRequiredConstructorParameters` the missing parameter; both are off by default. On a settable property, `required` rejects a missing value.
> - **Gaps in the analysis.** `new string[10]` holds ten nulls, and `default` of a struct leaves its non-nullable reference fields null, without a warning.
> - **Code compiled without NRT**, and every `!`.
>
> Fix: turn NRT on and treat its warnings as errors, then validate where data enters: `required`, the serializer options, and `ArgumentNullException.ThrowIfNull` in public methods.

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

A lambda that uses a local variable of its enclosing method forms a **closure**, and **it captures the variable, not its value.** The compiler moves the variable into a field of a hidden, heap-allocated object that the method and the lambda share, so later changes are visible to the lambda.

```csharp
// The infamous loop-capture bug (pre-C# 5 foreach, still relevant with for)
var actions = new List<Action>();
for (int i = 0; i < 3; i++)
    actions.Add(() => Console.WriteLine(i));

foreach (var a in actions) a();   // prints 3, 3, 3 — all share the same i
```

A `for` loop declares `i` once, so one hidden object serves every iteration: all three lambdas share it, and `i` is `3` by the time they run. A variable declared inside the loop body gets a new object per iteration:

```csharp
for (int i = 0; i < 3; i++)
{
    int copy = i;                        // new variable each iteration
    actions.Add(() => Console.WriteLine(copy));
}
// prints 0, 1, 2
```

> **Note:** Since C# 5, `foreach` declares its loop variable inside each iteration, so it doesn't have this bug; `for` still does. Capturing also allocates the hidden object, so closures created in a tight loop add GC pressure: .NET 10's JIT can stack-allocate some delegates, but not yet the object that holds captured variables.

## Extension Methods and Static Abstract Members

An **extension method** is a static method that the compiler lets you call *as if* it were an instance method on the first parameter's type, marked with `this`. This is how LINQ appears to add hundreds of methods to `IEnumerable<T>` without modifying it.

```csharp
public static class StringExtensions
{
    public static bool IsNullOrBlank(this string? s) => string.IsNullOrWhiteSpace(s);
}

bool empty = "   ".IsNullOrBlank();   // reads like an instance call; compiles to a static call
```

Extension methods can't access private members and are resolved by the `using` namespaces in scope.

> **New in C# 14 (.NET 10):** *extension members* generalize this `static`-`this` form. Inside an `extension` block you can now declare extension **properties**, operators, and static extension members — not just instance methods. The classic `this`-parameter syntax still works and interoperates with the new blocks, so nothing here is obsolete (see *Modern Syntax You Should Be Using* below for the timeline).

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

Each `yield return` hands one value to the consumer and *freezes the method's state* until the consumer asks for the next. This is **deferred execution**: the body doesn't run at all until enumeration begins, and it runs only as far as needed. It's what makes streaming large or infinite sequences memory-efficient, and LINQ's operators are built on it.

> **Gotcha:** Because iterators are deferred, exceptions and argument validation in an iterator method don't fire until the caller enumerates. If you want eager argument checks, split the method: a normal method that validates, then calls a private iterator.

## LINQ Internals: Deferred Execution and Expression Trees

LINQ is the previous three sections put together: extension methods on `IEnumerable<T>` that take your lambdas as delegates and return iterators. Two ideas explain most of its surprises: **deferred execution** and the **IEnumerable/IQueryable split**.

### Deferred vs Immediate Execution

Most LINQ operators (`Where`, `Select`, `OrderBy`, `Take`) are **deferred**: they return an object that holds the source and your lambda, and do no work. The work runs when something *enumerates* that object (`foreach`, or a terminal operator such as `ToList`, `Count`, `First` or `Sum`), and every enumeration calls `GetEnumerator` again, which starts a fresh run from the source.

```csharp
var query = numbers.Where(n => n > 10);   // nothing runs yet
numbers.Add(20);                          // still nothing has run
foreach (var n in query)                  // NOW the predicate executes
    Console.WriteLine(n);                 // sees 20 — query re-reads the source
```

Two consequences bite constantly:

1. **The query re-executes every time you enumerate it.** Two enumerations run the whole pipeline twice; over an EF Core query, that is two database round trips. If you need the results more than once, materialize with `ToList()`.
2. **Captured variables are read at enumeration time, not definition time.** The query is a recipe, not a snapshot.

```csharp
// Multiple enumeration — a real performance bug
IEnumerable<Order> pending = orders.Where(o => o.IsPending); // deferred
if (pending.Any())                       // enumerates once
    Process(pending.Count());            // enumerates AGAIN
// Two passes over the source. Materialize once: var list = pending.ToList();
```

**Immediate** operators run the pipeline right away: anything that returns a concrete collection or a scalar (`ToList`, `ToArray`, `ToDictionary`, `Count`, `Sum`, `First`, `Single`, `Any`).

### IEnumerable vs IQueryable

This split decides whether an ORM query filters in the database or drags the whole table into memory.

`IEnumerable<T>` operators take **`Func<...>` delegates**: compiled code that LINQ to Objects runs in memory.

`IQueryable<T>` operators take **`Expression<Func<...>>`**: the compiler turns the same lambda into an *expression tree*, a data structure describing the code, which a query provider such as EF Core walks and translates, typically into SQL.

```csharp
// IQueryable: the lambda becomes an expression tree, translated to SQL
IQueryable<Customer> q = dbContext.Customers.Where(c => c.City == "Kyiv");
// EF generates: SELECT ... FROM Customers WHERE City = 'Kyiv'

// IEnumerable: forces client-side evaluation from here on
IEnumerable<Customer> e = dbContext.Customers.AsEnumerable().Where(c => c.City == "Kyiv");
// Pulls the ENTIRE table into memory, then filters in C#
```

> **Pay attention.** **The declared type picks the `Where`.**
>
> Extension methods are bound at compile time. On a variable declared `IQueryable<T>`, the compiler picks `Queryable.Where` and builds an expression tree; on one declared `IEnumerable<T>`, even when the object behind it is an EF Core query, it picks `Enumerable.Where` and compiles a delegate. From there on, every filter runs in C# over the rows the SQL returned: one indexed predicate becomes "download a million rows, then filter". The usual culprits are a repository method that returns `IEnumerable<T>` and an early `AsEnumerable()` or `ToList()`.
>
> Fix: keep the query `IQueryable<T>` until its filters are applied, then materialize once.

A method EF Core can't translate no longer moves the filter to the client: since EF Core 3.0, an untranslatable expression in a `Where` or an `OrderBy` throws at run time, and only the final `Select` may run partly in C#.

### Expression Trees Directly

Expression trees are the machinery behind ORMs, mapping libraries and mocking frameworks, and you can build and inspect them yourself.

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

## LINQ Operators in Practice

Each LINQ to Objects operator is a loop with a name, so each trap below is a property of the loop behind it. The SQL notes are what EF Core sends for the same operator.

| You want | Use | The trap, and why |
|---|---|---|
| To project or filter | `Select`, `Where` | Both run again on every enumeration, side effects included ([Deferred vs Immediate Execution](#deferred-vs-immediate-execution)) |
| To flatten children | `SelectMany(o => o.Lines)` | `Select` would give a sequence of sequences |
| "Is there any?" | `Any()` | `Count() > 0` walks every element of a plain sequence (only an `ICollection<T>` answers from its `Count` property); EF Core sends `COUNT(*)` where `Any()` sends `EXISTS` |
| The first match | `First`, `FirstOrDefault` | `First` throws on an empty sequence; `FirstOrDefault` returns `default(T)`, and for `int` that `0` may be a real element: pass your own default, `FirstOrDefault(-1)` |
| Exactly one | `Single`, `SingleOrDefault` | They read past the match to prove there is no second: the whole sequence in memory, two rows from the database. `SingleOrDefault` still throws on two |
| To sort | `OrderBy(...).ThenBy(...)` | The sort is stable: ties keep source order. A second `OrderBy` sorts everything again, and EF Core keeps only the last one in `ORDER BY` |
| To group, then iterate once | `GroupBy` | Deferred: each enumeration regroups the whole source |
| To group, then look up by key | `ToLookup` | Runs immediately; a missing key gives an empty group, not an exception |
| To remove duplicates | `Distinct`, `DistinctBy` | `Distinct` uses `Equals` and `GetHashCode`, so a class without them compares references and keeps every object |
| To match two sequences | `Join`, `GroupJoin`, `LeftJoin` (.NET 10) | `Join` is an inner join and drops unmatched rows. It hashes the inner sequence once, O(n + m), where a `Where` inside a `Select` is O(n·m) |
| Batches | `Chunk(100)` | The last batch is shorter; each batch is a new array |
| One value from many | `Sum`, `Max`, `CountBy`, `Aggregate` | `Max`, and `Aggregate` without a seed, throw on an empty sequence; `Sum` returns 0 |

```csharp
Order[] orders = [new(1, "ann", 30m), new(2, "bob", 10m), new(3, "ann", 20m), new(4, "cid", 10m)];
int[] none = [];

Console.WriteLine(none.FirstOrDefault());                              // 0: no element, or the element 0?
Console.WriteLine(string.Join(" ", orders.OrderBy(o => o.Total).Select(o => o.Id)));   // 2 4 3 1: ties keep source order

var byCustomer = orders.ToLookup(o => o.Customer);                     // built once, here
Console.WriteLine(byCustomer["ann"].Sum(o => o.Total));                // 50
Console.WriteLine(byCustomer["zoe"].Count());                          // 0: a missing key is an empty group

Console.WriteLine(orders.Select(o => new Person(o.Customer)).Distinct().Count());   // 4: a class compares references
Console.WriteLine(orders.DistinctBy(o => o.Customer).Count());                      // 3: compare by a key instead

string[] vips = ["ann"];
var tagged = orders.LeftJoin(vips, o => o.Customer, v => v, (o, v) => $"{o.Id}:{v ?? "-"}");   // .NET 10
Console.WriteLine(string.Join(" ", tagged));                                       // 1:ann 2:- 3:ann 4:-

record Order(int Id, string Customer, decimal Total);
class Person(string name) { public string Name { get; } = name; }
```

## IDisposable, IAsyncDisposable, and the Dispose Pattern

The GC reclaims *managed memory*, but knows nothing about file handles, sockets, database connections or native memory. `IDisposable` is the contract for releasing those deterministically, and `using` compiles to a `try/finally` that calls `Dispose`.

```csharp
using (var stream = new FileStream("data.bin", FileMode.Open))
{
    // use stream
}   // Dispose() called automatically here, even on exception

// using declaration (C# 8) — disposes at end of enclosing scope
using var reader = new StreamReader("data.txt");
```

> **Pay attention.** **What happens to a connection nobody disposes.** The GC runs when the *heap* needs space; nothing runs when a *connection pool* runs dry. An undisposed `SqlConnection` stays out of its pool (100 connections by default), and the ADO.NET docs warn it might not return at all. Under load the pool empties, and each new `Open` waits up to 15 seconds, the default timeout, then throws, while memory and CPU look healthy. Fix: `using` or `await using` on every disposable you create; leave what you don't own (injected services, a DI-scoped `DbContext`) to the container that created it.

A class that merely *contains* disposable fields implements `Dispose` to dispose them, and nothing more. Only a class that directly owns an unmanaged resource needs the **full Dispose pattern**: a `Dispose(bool disposing)` method, a finalizer as a backstop, and `GC.SuppressFinalize` once `Dispose` has run. `disposing` is `false` on the finalizer path, where other managed objects may already be collected, so only unmanaged resources are released there. [Chapter 3](#finalization-and-the-relationship-to-idisposable) shows the pattern, the finalization queue and its cost.

**`IAsyncDisposable`** exists for resources whose cleanup involves I/O (flushing a buffer, closing a network stream) that shouldn't block a thread:

```csharp
await using var conn = new AsyncDbConnection();   // DisposeAsync() awaited at scope end
```

## Modern Syntax You Should Be Using

The language has accumulated syntax that removes ceremony; code reviews expect it in new code.

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

**The `field` keyword** (C# 14) gives a property accessor the compiler's backing field, so a property can normalize or validate its value without a hand-declared field:

```csharp
public class Customer
{
    public string Email
    {
        get;
        set => field = value.Trim().ToLowerInvariant();   // `field` is the compiler's backing field
    } = "";
}
```

In a type that already has a member named `field`, the word inside an accessor now means the keyword; write `@field` or `this.field` for the member.

**Null-conditional assignment** (C# 14) allows `?.` on the left of `=`. The assignment, right-hand side included, runs only when the target isn't null:

```csharp
customer?.Email = LoadEmail();   // when customer is null, LoadEmail() is never called
```

Compound assignment such as `+=` works the same way; `++` and `--` don't.

> **Where the language is heading.** C# 13 (.NET 9) added **params collections** (`params` accepts `Span<T>`, `ReadOnlySpan<T>` and other collection types, not just arrays) and `lock` support for .NET 9's dedicated `System.Threading.Lock` type. C# 14 ships with .NET 10, the current LTS. Besides `field`, **extension members** and null-conditional assignment, it brings implicit conversions between arrays, `Span<T>` and `ReadOnlySpan<T>`, `nameof` on unbound generic types (`nameof(List<>)`), modifiers such as `out` on lambda parameters without declared types, partial constructors and events, and user-defined compound assignment operators. C# 15 ships with .NET 11, an STS release at release candidate 2 since October 13, 2026 and supported from November 10, 2026. Its documentation lists union types, `closed` class hierarchies (a `switch` that handles every derived type needs no default arm), extension indexers, arguments in collection expressions (`[with(capacity: 100), ..items]`), labeled `break` and `continue`, and the first step of new memory-safety rules for unsafe code. [The appendix](#appendix-net-version-comparison-cheat-sheet) has the full version timeline.

## Prove it

A query with a counting predicate shows when LINQ runs and what a lambda captures. Predict the four counts and the two lists before you run it.

`verify/path/DeferredExecution/Program.cs` · run it from `verify/path` with `dotnet run --project DeferredExecution`:

```csharp
// Prove it: a LINQ query is a recipe. It runs again on every enumeration and reads captured variables when it runs.
int calls = 0, minimum = 2;
int[] numbers = [1, 2, 3, 4];
IEnumerable<int> query = numbers.Where(n => { calls++; return n >= minimum; });
Console.WriteLine($"query defined:        the predicate ran {calls} times");

int count = query.Count();                     // enumeration 1
int sum = query.Sum();                         // enumeration 2: the whole pipeline runs again
Console.WriteLine($"Count() and Sum():    the predicate ran {calls} times (count {count}, sum {sum})");

calls = 0;
List<int> list = query.ToList();               // one enumeration; from here on, a plain list
Console.WriteLine($"ToList(), then both:  the predicate ran {calls} times (count {list.Count}, sum {list.Sum()})");

minimum = 4;                                   // the lambda captured the variable, not its value 2
Console.WriteLine($"after minimum = 4:    query [{string.Join(", ", query)}], list [{string.Join(", ", list)}]");

IQueryable<int> queryable = numbers.AsQueryable().Where(n => n >= minimum);
Console.WriteLine($"an IQueryable holds an expression tree: {queryable.Expression}");
```

```text
query defined:        the predicate ran 0 times
Count() and Sum():    the predicate ran 8 times (count 3, sum 9)
ToList(), then both:  the predicate ran 4 times (count 3, sum 9)
after minimum = 4:    query [4], list [2, 3, 4]
an IQueryable holds an expression tree: System.Int32[].Where(n => (n >= value(Program+<>c__DisplayClass0_0).minimum))
```

What to notice:

- **Defining the query ran nothing.** `Where` only stored the array and the lambda.
- **Two terminal calls, two full runs.** `Count()` and `Sum()` each enumerated the query: 8 predicate calls for 4 numbers. Over an EF Core query, that is two SQL round trips.
- **`ToList()` ran it once.** After that, `Count` is a property of the list and `Sum()` reads the list, not the query.
- **The lambda read `minimum` when it ran.** Changed to 4 after the query was defined, the query now yields only 4; the list kept the snapshot it took. The lambda captured the variable, not its value.
- **The last line is what the capture looks like.** The `IQueryable` holds its lambda as data, an expression tree that a provider such as EF Core translates into SQL. In it, `minimum` is a field of a compiler-generated object (`<>c__DisplayClass0_0`, a name the compiler chooses), shared by the method and the lambda: that shared field is the captured variable.

## Three questions

**1.** Two `object` variables hold strings with the same text: `a == b` is `false` and `a.Equals(b)` is `true`. Then two `Order` records with the same `Id` and equal `List<string>` lines compare unequal with `==`. Why, in both cases?

<details>
<summary>Answer</summary>

- **`==` is chosen at compile time, from the declared types.** For two `object` variables the compiler binds `object`'s `==`, which compares references; `string`'s overload is never considered. The compiler doesn't warn when both sides are `object`, and a generic method constrained with `where T : class` binds the same reference comparison even when `T` is `string`.
- **`Equals` is chosen at run time, from the object.** It is virtual, so `a.Equals(b)` runs `string.Equals`, which compares the text.
- **A record compares field by field, each with its own `Equals`.** `List<T>` doesn't override `Equals`, so two lists with equal contents are two different references, and the records are unequal. `with` is shallow for the same reason: the copy shares the original's list.

Fix: declare the types you mean to compare (`string`, not `object`), and give a record that holds a collection its own `Equals(Order?)` and `GetHashCode` using `SequenceEqual`, or keep records to values: numbers, strings, nested records.
</details>

**2.** A repository method returns `IEnumerable<Order>`, built as `db.Orders.Where(o => o.IsPending)`. The caller calls `Any()`, then `Count()`, then filters by customer with `.Where(o => o.CustomerId == id)` and loops over the result. How many SQL queries run, and where does the customer filter run?

<details>
<summary>Answer</summary>

- **Three queries, the same `SELECT` each time.** The method returned a recipe, not results. Each of `Any()`, `Count()` and the loop enumerates it, and each enumeration sends the pending-orders query again; `Count()` and the loop read every pending row.
- **The customer filter runs in C#.** The declared type is `IEnumerable<Order>`, so the compiler binds `Enumerable.Where`, which takes a compiled delegate and filters rows after they arrive. Only `Queryable.Where`, bound when the declared type is `IQueryable<Order>`, receives an expression tree that EF Core can turn into SQL.

Fix: ask the database the question you mean, `db.Orders.Where(o => o.IsPending && o.CustomerId == id)`, then `CountAsync()` or `ToListAsync()` once, and work with the list. If several callers need to compose filters, keep `IQueryable<T>` inside the data layer and return materialized results from it.
</details>

**3.** The project has `<Nullable>enable</Nullable>` and treats warnings as errors. A message handler deserializes `record OrderPlaced(int OrderId, string Email)` with `JsonSerializer.Deserialize`, and `message.Email.Trim()` throws `NullReferenceException`. How did a null get into a non-nullable `string`, and what stops it?

<details>
<summary>Answer</summary>

- **The compiler checks only the code it compiles.** Nullable annotations are metadata, and flow analysis runs inside your methods; the compiled code has no null checks. The deserializer calls the record's constructor at run time with whatever the JSON holds, and no analysis runs there.
- **The JSON decided.** A payload without `email` passes `null` for the missing parameter, and `"email": null` passes it explicitly. With default options, `System.Text.Json` accepts both.

Fix: validate where data enters. Since .NET 9, `RespectNullableAnnotations = true` rejects an explicit `null` for a non-nullable parameter or property, and `RespectRequiredConstructorParameters = true` rejects a missing parameter; on a settable property, `required` rejects a missing value. Both options are off by default, and the first alone still lets a missing property through. In public methods that other code calls, `ArgumentNullException.ThrowIfNull`. Each `!` you write is a place where you told the compiler to stop checking.
</details>

## Check at work

**Inspect.** Search your data layer for methods that return `IEnumerable<T>` built from a `DbSet`, and check whether any caller filters or counts the result: each such caller pulls rows into memory. Search for records whose members are `List<T>`, arrays or dictionaries, and check whether anything compares them or uses them as dictionary keys. Then search for `new SqlConnection(`, `new FileStream(` and `new StreamReader(` without `using`, and count the `!` operators: each is a null check you switched off.

**Measure.** Turn on EF Core's SQL logging for one request (`LogTo`, or the `Microsoft.EntityFrameworkCore.Database.Command` category at `Information`) and count the statements: the same `SELECT` twice is a query enumerated twice. To find the rest at build time, enable analyzer CA1851 (possible multiple enumerations of `IEnumerable`, off by default) and count its warnings.

## Interview Questions

**Value type vs reference type — the practical difference?**
Value types (`struct`, `int`, `enum`) hold their data inline and are copied on assignment; reference types (`class`, arrays, `string`) hold a reference to heap data, so assignment copies the pointer, not the object. Value types typically live on the stack or inline within their container; reference types live on the heap. This drives copy semantics, equality defaults, and allocation behavior.

**Red flag:** "Value types live on the stack, reference types on the heap" stated as an absolute — a struct field inside a class lives on the heap; the real difference is copy semantics.

**What is boxing and why does it cost?**
Boxing wraps a value type in a heap object so it can be treated as `object` or an interface reference; unboxing extracts it back. It costs a heap allocation plus a copy, and adds GC pressure in hot paths. Generics and `Span<T>` largely eliminate the need; [Chapter 17](#chapter-17-runtime-internals-and-performance) shows how to measure allocations.

**`virtual`/`override` vs `new`: what does a call through a base-class variable run?**
With `override`, the derived method: a virtual call looks up its slot in the object's method table at run time, and the override has replaced that slot. With `new`, the base method: `new` declares a separate method that takes no slot of the base type, so the compiler binds the call by the variable's declared type. Code that handles objects as the base type never sees a `new` method.

**Red flag:** "`new` and `override` both override the method" — only `override` changes what a base-typed call runs.

**Abstract class or interface?**
An abstract class carries instance state, constructors and shared code, and a type can have only one base class. An interface is a contract a type can implement many of; since C# 8 it can carry default method bodies, reachable only through the interface, and since C# 11 `static abstract` members. Use an interface for a capability or a seam for dependency injection and tests, and an abstract class when a family of types genuinely shares state and code.

**What do `protected internal` and `private protected` allow?**
`protected internal` is the union: any code in the same assembly, plus derived types in any assembly. `private protected` is the intersection: only derived types, and only inside the same assembly. Members default to `private`, top-level types to `internal`.

**In what order is an object constructed, and why not call a virtual method from a constructor?**
The derived class's field initializers run first, then the base constructor, then the derived constructor body; static constructors run once per type before its first use. The object already has its final type during the base constructor, so a virtual call there runs the derived override on an object whose constructor body hasn't run yet, and it sees unassigned fields.

**Why are strings immutable, and when does `StringBuilder` help?**
A string's characters live inside the object and never change, which makes strings safe to share between threads and to use as dictionary keys; every "modification" allocates a new string. So `+=` in a loop copies everything built so far on each iteration, O(n²) characters, and `StringBuilder` makes it linear by appending into a buffer and copying once. For a fixed expression such as `a + b + c` it doesn't help: the compiler emits one `string.Concat` call that allocates once.

**What is string interning, and why doesn't `==` rely on it?**
The runtime keeps a pool with one object per distinct value, which usually holds the literals and always holds strings passed to `string.Intern`. Strings built at run time are separate objects, so equal strings are often different references; `string`'s `==` therefore compares characters. Interned strings generally live until the process ends, so interning unbounded input leaks memory.

**`ref` vs `out`?**
Both pass by reference. `ref` requires the variable to be initialized *before* the call and the method may read it; `out` requires the method to assign it before returning and the caller need not initialize it. Use `out` for "return extra values" (`TryParse`), `ref` for "read and modify in place."

**`IEnumerable<T>` vs `IQueryable<T>`?**
`IEnumerable` executes in memory with LINQ-to-Objects — the whole sequence is pulled and filtered client-side. `IQueryable` builds an expression tree that a provider (EF Core) translates to SQL, so filtering happens in the database. Accidentally forcing `IQueryable` to `IEnumerable` early (e.g. calling `.ToList()` then `.Where()`) drags the whole table into memory.

**What is deferred execution?**
LINQ query operators don't run when defined — they run when enumerated (`foreach`, `ToList`, `Count`). The query captures variables by reference, so results reflect state at *enumeration* time, and enumerating twice runs it twice. Materialize with `ToList()` when you need a stable snapshot or to avoid re-querying.

**`First`, `FirstOrDefault` or `Single`?**
`First` when any match will do and there must be one: it stops at the first and throws on none. `FirstOrDefault` when none is a normal case, remembering that `default(T)` for a value type is a real-looking `0`. `Single` when more than one is a bug you want caught: it reads past the match to prove there is no second, the whole sequence in memory or two rows from the database, and throws on two (`SingleOrDefault` too).

**`Any()` or `Count() > 0`?**
`Any()`: it stops at the first element, and EF Core translates it to `EXISTS`. `Count()` walks the whole sequence unless the source is an `ICollection<T>`, and becomes `COUNT(*)` in SQL.

**`GroupBy` or `ToLookup`?**
`GroupBy` is deferred and regroups the source on every enumeration; use it when you iterate the groups once. `ToLookup` groups immediately into a structure you can index by key many times, and a missing key returns an empty group instead of throwing.

**Delegates vs events?**
A delegate is a type-safe function pointer you can invoke and reassign. An `event` is a restricted wrapper over a delegate that only lets external code subscribe (`+=`) and unsubscribe (`-=`) — they can't invoke it or clear other subscribers. Events are the safe public surface for the observer pattern.

**What does a closure capture — the value or the variable?**
The variable, not its value at capture time. The compiler moves a captured local into a hidden heap object shared by the method and the lambda. A `for` loop declares its counter once, so every lambda created in the loop shares one `i` and sees its final value ([Closures and the Capture Trap](#closures-and-the-capture-trap) shows the code). Fix by copying into a loop-local (`int copy = i;`) and capturing `copy`; `foreach` variables are per-iteration since C# 5.

**Red flag:** "The lambda captures the value of `i` at that moment" — it captures the variable, so every lambda sees the final value.

**Struct vs class — when do you reach for a struct?**
Use a `struct` for small (~16 bytes or less), immutable, value-semantic data that's short-lived, to avoid heap allocation — e.g. a `Point` or a `Money`. Use a `class` for anything with identity, large state, inheritance, or reference-sharing needs. Big mutable structs are a trap: they copy on every pass and cause subtle bugs.

**What do records give you?**
`record` types generate value-based equality, `ToString`, a deconstructor, and `with`-expression non-destructive copying. `record` is a reference type; `record struct` is a value type. Reach for them for immutable DTOs and domain values where "two instances with the same data are equal" is the semantics you want.

**What is `Span<T>` for?**
`Span<T>` is a stack-only view over contiguous memory — an array slice, a string slice, or stack/native memory — that lets you slice and process without allocating or copying. It's a `ref struct`, so it can't be boxed, stored on the heap, or used across `await`. Ideal for parsing and buffer work in hot paths. Use `Memory<T>` when you need the same idea across async boundaries; [Chapter 17](#chapter-17-runtime-internals-and-performance) teaches both.

**`IDisposable` and `using` — what problem do they solve?**
Deterministic cleanup of unmanaged or expensive resources (file handles, sockets, DB connections) at a known point, rather than waiting for the GC/finalizer. `using` guarantees `Dispose()` runs even on exception. Use `await using` with `IAsyncDisposable` for resources with async teardown.

**What actually happens on `await`?**
The compiler rewrites the method into a state machine. At an `await`, if the awaited task isn't complete, the method *returns* to its caller and registers a continuation; when the task finishes, the continuation resumes the method (by default back on the captured context). It's not a thread — no thread is blocked while awaiting truly async I/O. [Chapter 4: Async Essentials](#chapter-4-async-essentials) teaches the mechanism.

**Red flag:** "`await` runs the method on a new background thread" — no thread is consumed at all during an awaited I/O wait.

> **Follow-up:** *Does `await` create a new thread?* No. For I/O it uses an I/O completion callback and no thread is consumed during the wait. A new thread only appears if you explicitly offload with `Task.Run`.
