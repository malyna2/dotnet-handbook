# Part 1 · Module 7: C# Essentials

> **What this module makes you able to do.** Predict what everyday C# does before you run it: what an assignment copies, what `==` compares, where a null still gets past the compiler, when a LINQ query runs and where its filter executes, what a lambda captures, and what `Dispose` gives back.

**Time:** reading ≈ 25 min; hands-on ≈ 1 h — the experiment 15 min, the questions 15, the check at work 30.

## Covers

- what an assignment copies for a struct and for a class, and where boxing allocates;
- what `==` and `Equals` compare, and what a record changes about both;
- what nullable reference types check, and where a null still gets in;
- when a LINQ query runs, and why enumerating it twice runs it twice;
- where a `Where` runs: in memory over `IEnumerable<T>`, in the database over `IQueryable<T>`;
- what a lambda captures, and what `Dispose` is for.

## The mechanism to explain without notes

**A variable holds either a value or a reference to a shared object, and the compiler turns many lines into something else: a method picked from the variable's declared type, or an object that runs later.**

- **What a copy copies.** Assigning a struct copies its fields; assigning a class copies the reference, so both variables see one object. Boxing is the bridge between the two: a value type assigned to `object` or to an interface is copied into a new heap object.
- **What `==` compares.** `==` is an operator, chosen at compile time from the declared types: on a class it compares references unless the type overloads it, as `string` and records do. `Equals` is a virtual method, chosen at run time from the object. A record generates both and compares field by field, each field with its own `Equals`, so a `List<T>` field still compares by reference.
- **What the compiler can't see.** Nullable reference types are annotations plus flow analysis at compile time; the compiled code checks nothing. A null still arrives from code the compiler didn't analyze: a deserializer, `default`, a new array, a `!`.
- **When the code runs.** `Where` and `Select` return an object that holds the source and your lambda. It runs each time something enumerates it, and reads captured variables at that moment, because a lambda captures the variable itself: the compiler moves it into a hidden object that the method and the lambda share.
- **Where the code runs.** The declared type picks the `Where`. On `IEnumerable<T>` it is `Enumerable.Where`, which runs your compiled lambda in memory; on `IQueryable<T>` it is `Queryable.Where`, which receives the lambda as an expression tree that EF Core translates into SQL. An EF query declared as `IEnumerable<T>` runs every later filter in C#, after reading every row the SQL returns.
- **What `Dispose` is for.** The GC reclaims memory when memory runs short. Nothing reclaims a pooled connection or a file handle when those run short, so `using` compiles to a `try/finally` that calls `Dispose` and returns them on time.

## Read (≈ 25 min)

1. [Chapter 1: Value Types and Reference Types: The Foundation](#value-types-and-reference-types-the-foundation): what a copy copies, why "value types live on the stack" is folklore, boxing, and when a `struct` is the right choice.
2. [Chapter 1: Records, Value Equality, and with Expressions](#records-value-equality-and-with-expressions): what the compiler generates for a record, and why `==` and `Equals` can disagree.
3. [Chapter 1: Nullable Reference Types](#nullable-reference-types): what the compiler checks, and where a null gets in anyway.
4. [Chapter 1: LINQ Internals: Deferred Execution and Expression Trees](#linq-internals-deferred-execution-and-expression-trees): when a query runs, and why the declared type decides whether its filter becomes SQL.
5. [Chapter 1: Iterators and yield return](#iterators-and-yield-return): the compiler-generated state machine that makes LINQ lazy.
6. [Chapter 1: Closures and the Capture Trap](#closures-and-the-capture-trap): what a lambda captures, and why a `for` loop shares one variable.
7. [Chapter 1: IDisposable, IAsyncDisposable, and the Dispose Pattern](#idisposable-iasyncdisposable-and-the-dispose-pattern): what `Dispose` is for, and what happens to a connection nobody disposes.

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
