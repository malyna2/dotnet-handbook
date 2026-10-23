# Chapter 2: Data Structures and Algorithms Essentials

This chapter makes you able to say what a piece of code costs as its input grows, and to pick the .NET collection whose cost matches the job: why `Dictionary` lookups are constant time and what makes them linear, why `List<T>.Add` is cheap on average and occasionally not, why `Array.Sort` is not stable, and when a plain list beats every clever structure.

The same habit applied to whole systems (load estimates, caches, queues, replicas) is [Chapter 23: System Design](#chapter-23-system-design); measuring the cost of real code with a profiler and BenchmarkDotNet is [Chapter 17: Runtime Internals and Performance](#chapter-17-runtime-internals-and-performance).

## Big-O: The Language of "How Bad Does This Get?"

Big-O notation describes how the *cost* of an operation grows as the *input* grows. It deliberately ignores constants and lower-order terms because they wash out at scale. If one algorithm takes `3n + 50` steps and another takes `n²`, then for small `n` the first might even be slower — but we care about the trend, and eventually `n²` dwarfs everything.

The useful analogy: Big-O is like the *fuel efficiency rating* of an algorithm, not its top speed. A truck rated at 20 MPG will always beat one rated at 5 MPG on a long enough trip, regardless of who has the faster engine off the line.

We track two dimensions:

- **Time complexity** — how the number of operations grows.
- **Space complexity** — how the extra memory grows (not counting the input itself).

There is often a trade between them. Caching results (a hash map) turns an O(n²) scan into O(n) time but costs O(n) space. Make that trade on purpose, and say so when you explain a solution.

### The Common Classes

| Big-O | Name | Grows like... | Real example |
|-------|------|---------------|--------------|
| O(1) | Constant | Doesn't grow | `dict[key]`, `list[i]`, `stack.Push` |
| O(log n) | Logarithmic | Halving each step | Binary search, balanced-tree lookup |
| O(n) | Linear | One pass | `list.Contains`, summing an array |
| O(n log n) | Linearithmic | Sorting | `Array.Sort`, merge sort |
| O(n²) | Quadratic | Nested loop | Naive dedup, bubble sort |
| O(2ⁿ) | Exponential | Doubling per element | Naive recursive Fibonacci, subset enumeration |
| O(n!) | Factorial | All orderings | Brute-force traveling salesman |

A concrete feel for the numbers: for `n = 1,000,000`, an O(n) algorithm does a million steps (instant), O(n log n) does ~20 million (fine), and O(n²) does a *trillion* (your request times out). This is why "it worked on my machine with 100 rows" is not proof of anything.

### Amortized Analysis

Some operations are *usually* cheap but *occasionally* expensive, and amortized analysis tells you the average cost over a sequence. The classic case is `List<T>.Add`. Adding to a dynamic array is O(1) — until the backing array fills, at which point .NET allocates a new array (typically double the size) and copies everything, an O(n) operation. But because doublings happen exponentially rarely, the *amortized* cost per Add is still O(1).

> **The intuition:** you pay a big cost rarely enough that, spread across all the cheap operations, it averages out to constant. It is like a phone plan with a large one-time activation fee — annoying once, negligible per call over two years.

Amortized O(1) is not the same as worst-case O(1). If you have a hard latency ceiling on *every* operation (real-time systems, some trading paths), that occasional O(n) resize can matter, and you'd pre-size the collection.

## Core Data Structures and Their .NET Types

Each structure trades away something to be fast at something else.

### Arrays: `T[]`

The bedrock. A contiguous block of memory holding fixed-size elements. Index access is O(1) because the address is just `base + i * elementSize` — pure arithmetic, no searching. That contiguity also makes arrays cache-friendly: the CPU prefetches neighboring elements, so iterating an array is often dramatically faster than a linked structure even at the same Big-O.

The catch: the size is fixed at creation. Inserting in the middle means shifting everything after it (O(n)), and growing means allocating a new array.

```csharp
int[] scores = new int[3];
scores[0] = 90;          // O(1)
int first = scores[0];   // O(1)
// scores[3] = 1;        // throws IndexOutOfRangeException — no auto-grow
```

Use raw arrays when the size is known and stable, when you need maximum throughput over a hot loop, or when interop / `Span<T>` slicing is involved.

### `List<T>`: The Dynamic Array

`List<T>` is the workhorse — an array that grows for you. Internally it holds a `T[]` and a `Count`. When you `Add` past capacity, it allocates a new array (doubling) and copies. This is why:

- `Add` at the end is **amortized O(1)**.
- Indexing `list[i]` is **O(1)**.
- `Insert(0, x)` or `RemoveAt(0)` is **O(n)** — every later element shifts by one.
- `Contains` / `IndexOf` is **O(n)** — a linear scan.

```csharp
var list = new List<int>();
for (int i = 0; i < 1000; i++) list.Add(i); // 9 array allocations: capacity 4, 8, 16 ... 1024

// If you know the size, pre-size to skip the resizes and copies:
var sized = new List<int>(capacity: 1000);
```

> **Best practice:** if you know roughly how many items you'll add, pass a capacity to the constructor. You skip a chain of allocations and array copies, which reduces GC pressure — a cheap win.

> **Pitfall:** reaching for `Insert(0, ...)` in a loop to build a reversed list is a classic accidental O(n²). Either add to the end and reverse once, or use a different structure.

### `LinkedList<T>`: When Middle-Insertion Dominates

A doubly linked list stores each element in a node with `Next` and `Previous` pointers. Insertion or removal *given a node reference* is O(1) — you just rewire pointers, no shifting. But you pay for it: indexing is O(n) (you must walk the chain), every node is a separate heap allocation (bad cache behavior, more GC), and the pointer overhead roughly triples memory per element.

```csharp
var ll = new LinkedList<string>();
var node = ll.AddLast("b");
ll.AddFirst("a");            // O(1), no shifting
ll.AddAfter(node, "c");      // O(1) given the node
```

> **Honest truth:** `LinkedList<T>` is rarely the right answer in modern .NET. Because arrays are so cache-friendly, `List<T>` frequently outperforms `LinkedList<T>` even for operations where the linked list has better Big-O, unless you're doing many splices in the middle *and* already hold node references. Measure before choosing it.

### `Dictionary<K,V>` and `HashSet<T>`: The Hash Table

This is the structure that will save you most often. A `Dictionary<K,V>` gives **average O(1)** insert, lookup, and delete by key. `HashSet<T>` is the same machinery without values — a set for fast membership tests and deduplication.

**How it achieves O(1):** the key's `GetHashCode()` produces an integer; the dictionary maps that hash to a *bucket* (an index into an internal array). Ideally each bucket holds one entry, so finding a key is: hash it, jump to the bucket, done. No scanning.

**Collisions** happen when two keys hash to the same bucket. .NET handles this with chaining — the bucket points to a small chain of entries, and lookup walks that short chain comparing with `Equals`. As long as collisions are rare, chains stay tiny and average cost stays O(1). If your hash function is terrible (returns the same value for everything), every key collides, chains degenerate into a linked list, and lookups become O(n). That is the worst case.

**The `GetHashCode`/`Equals` contract** is therefore load-bearing. When you use a custom type as a key, you must honor it:

- If `a.Equals(b)` is true, then `a.GetHashCode() == b.GetHashCode()` **must** be true.
- `GetHashCode` should be stable for the object's lifetime as a key, and spread values well.
- Equal objects must stay equal — never mutate a field used in the hash while the object is in a dictionary.

```csharp
public sealed class Point : IEquatable<Point>
{
    public int X { get; }
    public int Y { get; }
    public Point(int x, int y) { X = x; Y = y; }

    public bool Equals(Point? other) =>
        other is not null && X == other.X && Y == other.Y;

    public override bool Equals(object? obj) => Equals(obj as Point);

    // Combine fields; HashCode.Combine handles good distribution for you.
    public override int GetHashCode() => HashCode.Combine(X, Y);
}
```

> **Best practice:** use a `record` or `readonly record struct` for key types. The compiler generates a correct, value-based `Equals` and `GetHashCode` for you, and immutability protects you from the "mutated a key" bug. [Chapter 1](#records-value-equality-and-with-expressions) shows what a record compares, and why a collection-valued member breaks it.

> **Pitfall:** overriding `Equals` but forgetting `GetHashCode` (or vice versa) silently breaks dictionary and set behavior — objects you consider equal end up in different buckets and "disappear." The compiler warns you; don't ignore it.

> **Gotcha:** `string.GetHashCode()` is randomized per process in .NET (Core and later): the same string hashes differently in the next run. That defends dictionaries against inputs crafted to collide, and it means a hash code is never something to persist, send to another process, or use as a stable ID.

Use a dictionary whenever you find yourself scanning a list to find a matching item by some key. That `O(n)` `First(x => x.Id == id)` inside a loop is an `O(n²)` waiting to happen; a `Dictionary<Id, T>` makes it O(n) total.

### `SortedDictionary<K,V>` and `SortedSet<T>`: Trees

When you need keys kept *in sorted order*, hash tables can't help (hashing scrambles order by design). These are backed by self-balancing binary search trees (red-black trees). Operations are **O(log n)** — slower than a hash table's O(1), but you gain ordered iteration and efficient range queries.

```csharp
var leaderboard = new SortedDictionary<int, string>();
leaderboard[500] = "alice";
leaderboard[250] = "bob";
leaderboard[999] = "carol";
// Iterates in ascending key order: 250, 500, 999
foreach (var (score, name) in leaderboard)
    Console.WriteLine($"{score}: {name}");
```

Reach for these when you need "smallest/largest," "next key above X," or ordered traversal. If you only need order *once* at the end, it's usually cheaper to keep a `List<T>` and sort it (O(n log n) once) than to pay O(log n) on every insert.

### `Stack<T>` and `Queue<T>`: Ordering Discipline

Both are thin, efficient wrappers over an array. They don't do anything you couldn't do with a `List<T>`; their value is *intent* and *safety* — they expose only the operations that make sense.

- **`Stack<T>`** — LIFO (last in, first out). `Push`/`Pop`/`Peek`, all amortized O(1). Think undo history, DFS traversal, or expression evaluation — anything where the most recent thing is the next to handle.
- **`Queue<T>`** — FIFO (first in, first out). `Enqueue`/`Dequeue`/`Peek`, all amortized O(1). Think work pipelines, BFS traversal, buffering — process in arrival order.

```csharp
var undo = new Stack<string>();
undo.Push("type A"); undo.Push("type B");
Console.WriteLine(undo.Pop()); // "type B" — most recent first

var jobs = new Queue<string>();
jobs.Enqueue("email1"); jobs.Enqueue("email2");
Console.WriteLine(jobs.Dequeue()); // "email1" — arrival order
```

### `PriorityQueue<TElement, TPriority>` (.NET 6+)

Long overdue in the BCL, this gives you a queue ordered by priority rather than arrival. Internally it's a **binary heap**: `Enqueue` and `Dequeue` are O(log n), and peeking the minimum is O(1). The lowest priority value dequeues first by default.

```csharp
var pq = new PriorityQueue<string, int>();
pq.Enqueue("low-priority task", 5);
pq.Enqueue("urgent!", 1);
pq.Enqueue("normal task", 3);
Console.WriteLine(pq.Dequeue()); // "urgent!" (priority 1 = lowest = first)
```

This is the engine behind Dijkstra's shortest-path, A* pathfinding, event simulations, and "process the most important item next" schedulers.

> **Gotcha:** `PriorityQueue` does not guarantee first-in, first-out order among elements with equal priority; a heap reorders them freely. If arrival order must break ties, make it part of the priority, for example a `(priority, sequenceNumber)` tuple.

### Choosing a Collection at a Glance

| Collection | Lookup | Add / remove | Order | Reach for it when... |
|------------|--------|--------------|-------|----------------------|
| `T[]` | O(1) by index, O(n) search | fixed size | as written | the size is known and the loop is hot |
| `List<T>` | O(1) by index, O(n) `Contains` | O(1) amortized at the end, O(n) at the front | insertion | a growable sequence you iterate or append to |
| `Dictionary<K,V>` | O(1) average by key | O(1) average | none | you find things **by key** |
| `HashSet<T>` | O(1) average `Contains` | O(1) average | none | membership tests and de-duplication |
| `SortedDictionary<K,V>` / `SortedSet<T>` | O(log n) | O(log n) | sorted | lookups **and** ordered iteration or ranges |
| `Stack<T>` / `Queue<T>` | top or front only | O(1) amortized | LIFO / FIFO | most recent first / arrival order |
| `PriorityQueue<E,P>` | O(1) peek at the minimum | O(log n) | by priority | "the most important item next" |
| `LinkedList<T>` | O(n) | O(1) given a node | insertion | many splices at nodes you already hold (rare) |

For a lookup table built **once and read many times**, such as reference data loaded at start-up, .NET 8 added `FrozenDictionary<TKey,TValue>` and `FrozenSet<T>` (`System.Collections.Frozen`): they spend more time on construction to make every later read faster than `Dictionary`/`HashSet`.

### When *Not* to Reach for a Fancy Structure

> **Best practice:** for small collections (a handful to a few dozen items), a plain `List<T>` with a linear scan often *beats* a `Dictionary` or `SortedSet`. Hashing has constant overhead, tree nodes fragment memory, and cache locality wins at small `n`. Don't build an index for ten items.

## Key Algorithms and Patterns

You rarely implement these from scratch at work, but recognizing when a problem *is* one of these is the payoff, and interviews test the same recognition. Most of them lean on a structure from the previous section: binary search on a sorted array, BFS on a `Queue<T>`, dedup on a `HashSet<T>`, memoization on a `Dictionary`.

### Sorting, and Why `Array.Sort` Is Introsort

.NET's `Array.Sort` doesn't use one algorithm; it uses **introsort** (introspective sort), a hybrid that gets the best of several:

- It starts with **quicksort**, which is fast on average (O(n log n)) with excellent cache behavior.
- If recursion goes too deep (a sign quicksort is hitting its O(n²) worst case on a pathological input), it switches to **heapsort**, which guarantees O(n log n).
- For small partitions (roughly 16 or fewer elements), it switches to **insertion sort**, which has low overhead and is fast on tiny, nearly-sorted runs.

The lesson: production sorting is an engineering compromise, not a textbook algorithm. You get guaranteed O(n log n) worst case *and* good real-world speed.

```csharp
var nums = new[] { 5, 2, 8, 1, 9 };
Array.Sort(nums);                              // in-place, introsort
var people = list.OrderBy(p => p.Age).ToList(); // LINQ: stable sort, new list
```

> Note: `Array.Sort` is *not* stable (equal elements may be reordered), while LINQ's `OrderBy` *is* stable. If preserving original order of equal keys matters, use `OrderBy`.

### Binary Search

If data is *already sorted*, you can find an element in **O(log n)** by repeatedly halving the search range — like finding a word in a dictionary by opening to the middle, not reading page by page. Each comparison eliminates half the remaining candidates.

```csharp
int[] sorted = { 1, 3, 5, 7, 9, 11 };
int idx = Array.BinarySearch(sorted, 7); // returns 3

var list = new List<int> { 1, 3, 5, 7, 9 };
int pos = list.BinarySearch(6);
// negative result: the bitwise complement is the insertion point
if (pos < 0) pos = ~pos; // where 6 would go to keep it sorted
```

> **Pitfall:** binary search is only correct on sorted data. `List.BinarySearch` on an unsorted list returns garbage silently — no exception. The cost of keeping data sorted must be weighed against the lookup savings.

### Two Pointers

Walk a collection with two indices moving under some rule — often from both ends inward, or one chasing the other. It turns many O(n²) brute-force scans into O(n). Classic use: checking if a sorted array has a pair summing to a target.

```csharp
// Does the sorted array contain two numbers adding up to target?
static bool HasPairWithSum(int[] sorted, int target)
{
    int left = 0, right = sorted.Length - 1;
    while (left < right)
    {
        int sum = sorted[left] + sorted[right];
        if (sum == target) return true;
        if (sum < target) left++;   // need bigger, move left up
        else right--;               // need smaller, move right down
    }
    return false;
}
```

### Sliding Window

A specialized two-pointer pattern for contiguous subarrays or substrings. Instead of recomputing over every window from scratch (O(n·k)), you slide a window and adjust incrementally — add the entering element, remove the leaving one — for O(n).

```csharp
// Max sum of any contiguous window of size k.
static int MaxWindowSum(int[] nums, int k)
{
    int windowSum = 0;
    for (int i = 0; i < k; i++) windowSum += nums[i];
    int best = windowSum;
    for (int i = k; i < nums.Length; i++)
    {
        windowSum += nums[i] - nums[i - k]; // slide: add new, drop old
        best = Math.Max(best, windowSum);
    }
    return best;
}
```

### Hashing for Dedup and Lookup

The most reached-for pattern in real code. Any time you're asking "have I seen this before?" or "does a matching item exist?", a `HashSet<T>` or `Dictionary<K,V>` turns a nested O(n²) scan into a single O(n) pass.

```csharp
// Find the first duplicate in one pass, O(n) time, O(n) space.
static int? FirstDuplicate(int[] nums)
{
    var seen = new HashSet<int>();
    foreach (var n in nums)
        if (!seen.Add(n)) return n; // Add returns false if already present
    return null;
}
```

### BFS and DFS on Graphs

Many real problems are graphs in disguise: social connections, dependency trees, file systems, org charts, state machines. Two traversal strategies:

- **BFS (breadth-first)** — explore level by level using a **queue**. Finds the shortest path in an unweighted graph. Think ripples spreading from a stone.
- **DFS (depth-first)** — follow one path to its end before backtracking, using a **stack** (or recursion). Good for detecting cycles, topological sorting, exhaustive exploration.

```csharp
static int ShortestHops(Dictionary<int, List<int>> graph, int start, int goal)
{
    var visited = new HashSet<int> { start };
    var queue = new Queue<(int node, int dist)>();
    queue.Enqueue((start, 0));
    while (queue.Count > 0)
    {
        var (node, dist) = queue.Dequeue();
        if (node == goal) return dist;
        foreach (var next in graph[node])
            if (visited.Add(next))            // mark visited exactly once
                queue.Enqueue((next, dist + 1));
    }
    return -1; // unreachable
}
```

> **Pitfall:** always track a `visited` set. Without it, any graph with a cycle sends your traversal into an infinite loop.

### Recursion vs Iteration

Recursion expresses tree- and graph-shaped problems elegantly — the code mirrors the structure. But each call consumes a stack frame, and deep recursion (tens of thousands of levels) throws `StackOverflowException`, which you *cannot* catch. Iteration with an explicit `Stack<T>` is uglier but bounded only by heap memory. For deep or unbounded structures, prefer the explicit stack.

### Dynamic Programming Intuition

DP sounds intimidating but the core idea is simple: **don't solve the same subproblem twice.** If a problem breaks into overlapping subproblems, cache each answer (memoization) and reuse it. Naive recursive Fibonacci is O(2ⁿ) because it recomputes the same values exponentially; caching makes it O(n).

```csharp
static long Fib(int n, Dictionary<int, long> memo)
{
    if (n < 2) return n;
    if (memo.TryGetValue(n, out var cached)) return cached;
    long result = Fib(n - 1, memo) + Fib(n - 2, memo);
    memo[n] = result;   // remember so we never recompute this
    return result;
}
```

The two hallmarks that signal DP: **overlapping subproblems** (the same smaller question comes up repeatedly) and **optimal substructure** (the best overall answer is built from best answers to sub-parts). Coin change, edit distance, and knapsack are canonical examples.

### Greedy

A greedy algorithm makes the locally best choice at each step and hopes it leads to a global optimum. It's fast and simple — but only *correct* for problems with the right structure. Making change with standard coin denominations works greedily (always take the largest coin that fits); with arbitrary denominations it can fail, and you need DP. The skill is knowing *when* greedy is provably correct versus when it's a seductive trap.

## Choosing the Right Tool for a Real Problem

When a task lands on your desk, resist jumping to code. Frame it first:

1. **What are the operations, and how often?** Mostly reads by key? A dictionary. Mostly ordered iteration? A sorted structure or sort-once list. Insert/remove at ends? A stack or queue.
2. **What's the realistic `n`?** Ten items and a linear scan is fine. Ten million and that scan is your bottleneck.
3. **What are the constraints?** Memory ceiling? Latency ceiling on *every* op (worst-case matters, not just amortized)? Ordering requirements?
4. **What's the dominant cost?** Optimize the operation that runs most often or on the largest data. A slow one-time setup with fast repeated lookups is usually the right trade.

> The framing itself is the skill. "Which data structure is best?" has no answer; "what does this problem actually need, and what's the simplest structure that delivers it within the constraints?" does.

> **Pay attention.** **LINQ hides the loop, not its cost.** Each operator is a pass over its source, so a lambda that calls `Contains`, `First` or `Any` on a `List<T>` runs a full scan per element. `orders.Where(o => vipIds.Contains(o.CustomerId))` is O(n·m) with `vipIds` as a list and O(n) with it as a `HashSet<int>`, and the code looks the same. Likewise `Distinct`, `GroupBy` and `ToLookup` build hash tables internally (O(n) time, O(n) space), and `OrderBy` sorts (O(n log n)). Fix: before writing a lambda, ask what each call inside it costs per element, and build a set or dictionary once outside the query.

## Prove it

One program shows three mechanisms from this chapter: the list's doubling array, a scan against a hash lookup, and what a mutated key does to a hash set. `Key` counts its `Equals` calls, so each cost shows as a count rather than a timing that changes from run to run. Predict the counts before you run it.

`verify/path/CollectionCosts/Program.cs` · run it from `verify/path` with `dotnet run --project CollectionCosts`:

```csharp
// Prove it: a List<T> doubles its array as it grows; a hash set finds a key by its hash and loses a key whose hash changed.
var list = new List<Key>();
var capacities = new List<int>();
for (int i = 0; i < 10_000; i++)
{
    list.Add(new Key(i));
    if (capacities.Count == 0 || capacities[^1] != list.Capacity) capacities.Add(list.Capacity);
}
Console.WriteLine($"capacities while adding 10,000: {string.Join(" ", capacities)}");

var set = new HashSet<Key>(list);
var probe = new Key(9_999);
Key.EqualsCalls = 0; list.Contains(probe);
Console.WriteLine($"list.Contains(9999): {Key.EqualsCalls} Equals calls (a scan)");
Key.EqualsCalls = 0; set.Contains(probe);
Console.WriteLine($"set.Contains(9999):  {Key.EqualsCalls} Equals call (hash, bucket, one compare)");

Key stored = list[42];
stored.Id = 1_000_042;                       // mutate a key while it is in the set
Console.WriteLine($"after mutating key 42: set.Contains(it) {set.Contains(stored)}, set.Count {set.Count}, " +
                  $"still there by reference: {set.Any(k => ReferenceEquals(k, stored))}");

sealed class Key(int id)
{
    public static int EqualsCalls;
    public int Id = id;
    public override bool Equals(object? obj) { EqualsCalls++; return obj is Key other && other.Id == Id; }
    public override int GetHashCode() => Id;
}
```

```text
capacities while adding 10,000: 4 8 16 32 64 128 256 512 1024 2048 4096 8192 16384
list.Contains(9999): 10000 Equals calls (a scan)
set.Contains(9999):  1 Equals call (hash, bucket, one compare)
after mutating key 42: set.Contains(it) False, set.Count 10000, still there by reference: True
```

What to notice:

- **The capacity starts at 4 and doubles.** Thirteen arrays for 10,000 items. The twelve copies moved 4 + 8 + … + 8,192 = 16,380 elements in all, fewer than two per item added: that bounded total is the amortized O(1) of `Add`. The last array has 16,384 slots for 10,000 items; the 6,384 empty ones are what doubling costs in memory, and a capacity passed to the constructor avoids both the copies and the slack.
- **The scan compared every item.** `List<T>.Contains` calls `Equals` on each element in turn, and 9,999 is the last one: 10,000 calls. The set computed the probe's hash, went to one bucket and called `Equals` once. At 10,000 items the gap is four orders of magnitude, and it grows with `n`.
- **The mutated key is still in the set, and lookups can't find it.** The set filed the object under hash 42 and stored that hash in the entry. `Contains` hashes the new `Id`, 1,000,042, and searches that hash's bucket; even in the same bucket, it compares the stored hash before it calls `Equals`, and 42 doesn't match. `Count` still says 10,000 and enumeration still yields the object: the entry is lost to `Contains` and `Remove`, not removed. A `Dictionary` loses a mutated key the same way.

## Three questions

**1.** An `Order` class overrides `Equals` to compare `Id`, but not `GetHashCode`. `orders.Distinct()` returns orders with the same `Id` twice, and `dictionary[order]` throws `KeyNotFoundException` for an order equal to the one stored. Why doesn't `Distinct`, which "removes equal items", use your `Equals`?

<details>
<summary>Answer</summary>

- **It never gets that far.** `Distinct` builds a hash set as it goes, and a hash set compares only items whose hash codes match ([the hash table](#dictionarykv-and-hashsett-the-hash-table) above). `Equals` is the last check, not the first.
- **The inherited `GetHashCode` is per object.** `object.GetHashCode` is tied to the instance, not to its fields, so two separate orders with the same `Id` almost always get different hash codes, land in different buckets, and are never compared. The dictionary lookup fails for the same reason.
- **The contract the class broke:** equal objects must return equal hash codes. The compiler warns about it (CS0659 when a class overrides `Equals` without `GetHashCode`); a project that suppresses the warning ships the bug.

Fix: override `GetHashCode` from the same fields as `Equals` (`HashCode.Combine(Id)` for one field), make those fields immutable, or use a `record`, which generates both from the same members. For a one-off comparison, pass an `IEqualityComparer<Order>` to `Distinct` or to the dictionary's constructor.
</details>

**2.** A request handler builds a `List<OrderLine>` with two million `Add` calls. Each `Add` is O(1), yet the handler allocates about twice the memory its final list holds, and Gen 2 collections climb under load. Why?

<details>
<summary>Answer</summary>

- **Every growth allocates a new array and abandons the old one.** The capacity goes 4, 8, 16 … 2,097,152: about twenty arrays, and their sizes add up to roughly twice the last one. With 8-byte references, the last array is 16 MB, and the handler allocated about 32 MB to get there. The final array alone is the list's memory; each earlier one is garbage the moment the next one replaces it.
- **The big arrays skip Gen 0.** An array of 85,000 bytes or more is allocated on the Large Object Heap, which is collected only with Gen 2 ([Chapter 3](#generations-0-1-and-2)). With 8-byte references that is every array from capacity 16,384 up: eight per request, seven of them dead before the handler returns, and each one waits for a full collection.
- **Amortized O(1) speaks about time, not allocation.** The total copy work is O(n), as promised; the memory the copies leave behind is the part the Big-O doesn't show.

Fix: pass the expected count as the capacity, which allocates one array of the right size and copies nothing. Better still, ask whether the handler needs all two million items at once: streaming them (`IAsyncEnumerable<T>`, paging, a database cursor) keeps memory flat. [Chapter 17](#the-large-object-heap-loh) covers what the Large Object Heap does under load.
</details>

**3.** `List<T>.BinarySearch` on an unsorted list returns a wrong index instead of throwing. Why doesn't .NET check that the list is sorted, and what else makes a binary search wrong on a sorted list?

<details>
<summary>Answer</summary>

- **The check would cost more than the search.** Proving a list is sorted means reading every element, O(n); the search reads about log₂ n of them, 14 for 10,000 items. A check would turn the O(log n) operation into an O(n) one, so sortedness is a precondition the caller guarantees, not something the method verifies ([Binary Search](#binary-search)).
- **On unsorted data, each halving discards the wrong half.** The search compares the middle element and throws away the half that "can't" hold the value; when the order is wrong, that half may hold it, and the search ends on a negative result or a wrong position without any error.
- **The second trap: a different comparer.** A list sorted with `StringComparer.OrdinalIgnoreCase` is not sorted for the default comparer, which orders by culture and case. Searching it with the default comparer breaks the same precondition with the same silence.

Fix: sort and search with the same comparer (`list.Sort(comparer)`, then `list.BinarySearch(item, comparer)`), and keep the list sorted on every insert, at the position `~result` returns. If the data changes often, a `SortedSet<T>` or a `Dictionary` usually costs less than keeping a list sorted by hand.
</details>

## Check at work

**Inspect.** Search for lookups by key inside loops and lambdas: `.Contains(`, `.Any(`, `.First(` and `.FirstOrDefault(` on a `List<T>`, each called once per element of another collection. Each is a scan per element, and a `HashSet<T>` or `Dictionary<TKey,TValue>` built once before the loop removes it. Then search for `Insert(0,` and `RemoveAt(0)` in loops, classes that override `Equals` without `GetHashCode` (search the build for a suppressed CS0659), mutable properties read by a `GetHashCode`, and hash codes written to a database or a cache key.

**Measure.** For one hot endpoint, find the largest collection it builds per request and its realistic `n` in production, from logs or a row count. If a lookup runs inside a loop over it, time both versions with [BenchmarkDotNet](#benchmarking-with-benchmarkdotnet) at that `n`, and write down where the plain list stops winning.

## Interview Questions

Algorithm questions reward the same order every time: restate the problem and its edge cases, give the brute-force solution and its complexity, then improve it by naming the structure or pattern that removes the repeated work, and finish by testing it aloud on a small input. The general approach to an interview is in [Chapter 36: Senior Behaviours, Career and Interviews](#chapter-36-senior-behaviours-career-and-interviews).

**What does Big-O describe, and what does it leave out?**
How cost grows with input size, ignoring constants and lower-order terms. It leaves out exactly what dominates at small `n`: constant overhead and cache behavior. That is why a linear scan over ten items can beat a dictionary, and why `List<T>` often beats `LinkedList<T>` despite a worse Big-O for middle inserts.

**Why is `List<T>.Add` O(1) if it sometimes copies the whole array?**
It is *amortized* O(1). When the backing array fills, the list allocates one twice as large and copies, an O(n) step; because the capacity doubles, those copies happen so rarely that the total work over n adds is O(n), so O(1) per add on average. The worst case of a single add is still O(n), which is why a latency-critical path pre-sizes the list.

**How does a `Dictionary` achieve O(1), and when does it degrade?**
`GetHashCode` picks a bucket, and `Equals` resolves the short chain of entries that collide there. It degrades towards O(n) when many keys collide (a poor hash) and silently loses entries when a key's hash changes after insertion (a mutated key), so keys should be immutable and implement `Equals` and `GetHashCode` consistently.

**Red flag:** overriding `Equals` without `GetHashCode` — equal keys land in different buckets and lookups "lose" them.

**`Dictionary` vs `SortedDictionary` — when each?**
`Dictionary` for lookups by key at O(1) average with no ordering; `SortedDictionary` (a red-black tree) when you also need ordered iteration, minimum and maximum, or range queries, at O(log n) per operation. If order matters only once, at the end, a dictionary plus one sort is usually cheaper.

**Is `Array.Sort` stable? What algorithm does it use?**
No. It is introsort: quicksort, switching to heapsort when recursion gets too deep and to insertion sort for partitions of about 16 elements or fewer, which guarantees O(n log n) in the worst case. LINQ's `OrderBy` is stable; use it when equal keys must keep their original order.

**You need the shortest path in an unweighted graph. BFS or DFS?**
BFS: it explores level by level with a queue, so the first time it reaches the goal it has used the fewest edges. DFS (a stack or recursion) suits cycle detection, topological sorting and exhaustive search. Both need a `visited` set, or a cycle loops forever; with weighted edges, use Dijkstra on a `PriorityQueue`.

**How do you find duplicates in an array of n items?**
One pass with a `HashSet<T>`: `Add` returns `false` for an item already seen. O(n) time and O(n) space, against O(n²) for comparing every pair; sorting first gives O(n log n) time with no extra set, a valid trade when memory is tight.

**When is recursion the wrong choice?**
When the depth can grow with the input. Each call uses a stack frame, and a `StackOverflowException` cannot be caught: the process terminates. For deep or unbounded structures, iterate with an explicit `Stack<T>`, which is bounded only by heap memory.

**What makes a problem a dynamic-programming problem?**
Overlapping subproblems (the same smaller question recurs) and optimal substructure (the best answer is built from the best answers to its parts). Cache each subproblem's answer, by memoization or by filling a table bottom-up, and an exponential recursion such as naive Fibonacci becomes linear.

**Red flag:** reaching for greedy without arguing why it is correct — greedy coin change works for standard denominations and fails for arbitrary ones, where DP is needed.

## Sources & Further Reading

- Microsoft Learn — *System.Collections.Generic Namespace* and the individual type references (`List<T>`, `Dictionary<TKey,TValue>`, `HashSet<T>`, `SortedDictionary<TKey,TValue>`, `Queue<T>`, `Stack<T>`, `PriorityQueue<TElement,TPriority>`). https://learn.microsoft.com/en-us/dotnet/api/system.collections.generic
- Microsoft Learn — *Array.Sort Method* (documents the introsort hybrid used by the runtime). https://learn.microsoft.com/en-us/dotnet/api/system.array.sort
- Microsoft Learn — *Guidelines for overriding Equals() and GetHashCode()* and the `HashCode.Combine` reference.
- Microsoft Learn — *System.Collections.Frozen Namespace* (`FrozenDictionary<TKey,TValue>`, `FrozenSet<T>`) and the `PriorityQueue<TElement,TPriority>` remarks on equal priorities.
- Thomas H. Cormen, Charles E. Leiserson, Ronald L. Rivest, Clifford Stein — *Introduction to Algorithms (CLRS)*, 4th edition (Big-O, sorting, graph algorithms, dynamic programming, amortized analysis).
- Gayle Laakmann McDowell — *Cracking the Coding Interview*, 6th edition (data-structure selection, interview algorithm patterns).
