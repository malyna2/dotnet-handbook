# Chapter 26: Data Structures, Algorithms & System Design Fundamentals

_⏱️ Estimated read time: ~31 min ·     4630 words (study pace)_

Most working developers can build features all day without ever writing a sorting algorithm from scratch. So why does this material still matter? Because the moment you cross from "mid-level" to "senior," your job stops being "make it work" and becomes "make it work *at scale, under constraints, and predictably*." A senior engineer is the person who looks at a nested loop over a growing list and quietly senses it will become a production incident in six months. That instinct is not magic — it is fluency in the language of data structures, algorithms, and system design.

This chapter is a refresher and a consolidation. You already know C#. Here we sharpen the mental models: what each collection in the .NET base class library actually does under the hood, how to reason about cost, and how to zoom out to whole-system design. Think of it as learning the physics of your codebase so you can predict how it behaves before you run it.

## Big-O: The Language of "How Bad Does This Get?"

Big-O notation describes how the *cost* of an operation grows as the *input* grows. It deliberately ignores constants and lower-order terms because they wash out at scale. If one algorithm takes `3n + 50` steps and another takes `n²`, then for small `n` the first might even be slower — but we care about the trend, and eventually `n²` dwarfs everything.

The useful analogy: Big-O is like the *fuel efficiency rating* of an algorithm, not its top speed. A truck rated at 20 MPG will always beat one rated at 5 MPG on a long enough trip, regardless of who has the faster engine off the line.

We track two dimensions:

- **Time complexity** — how the number of operations grows.
- **Space complexity** — how the extra memory grows (not counting the input itself).

There is often a trade between them. Caching results (a hash map) turns an O(n²) scan into O(n) time but costs O(n) space. Senior engineers negotiate this trade consciously.

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

The single most valuable skill in this section is matching a problem to a structure. Each structure trades away something to be fast at something else. Let's walk them in the order you'll reach for them.

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
for (int i = 0; i < 1000; i++) list.Add(i); // ~10 internal resizes total

// If you know the size, pre-size to skip the resizes and copies:
var sized = new List<int>(capacity: 1000);
```

> **Best practice:** if you know roughly how many items you'll add, pass a capacity to the constructor. You skip a chain of allocations and array copies, which reduces GC pressure — a cheap, senior-level win.

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

> **Best practice:** use a `record` or `readonly record struct` for key types. The compiler generates a correct, value-based `Equals` and `GetHashCode` for you, and immutability protects you from the "mutated a key" bug.

> **Pitfall:** overriding `Equals` but forgetting `GetHashCode` (or vice versa) silently breaks dictionary and set behavior — objects you consider equal end up in different buckets and "disappear." The compiler warns you; don't ignore it.

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

### When *Not* to Reach for a Fancy Structure

> **Best practice:** for small collections (a handful to a few dozen items), a plain `List<T>` with a linear scan often *beats* a `Dictionary` or `SortedSet`. Hashing has constant overhead, tree nodes fragment memory, and cache locality wins at small `n`. Don't build an index for ten items.

The discipline of a senior engineer isn't reaching for the most sophisticated structure — it's reaching for the *simplest one that meets the actual constraints*. Premature "optimization" with heavyweight structures adds complexity and can be slower. Know your `n`.

## Key Algorithms and Patterns

You rarely implement these from scratch at work, but recognizing when a problem *is* one of these — that's the payoff. Interviews test the same recognition.

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

A greedy algorithm makes the locally best choice at each step and hopes it leads to a global optimum. It's fast and simple — but only *correct* for problems with the right structure. Making change with standard coin denominations works greedily (always take the largest coin that fits); with arbitrary denominations it can fail, and you need DP. The senior skill is knowing *when* greedy is provably correct versus when it's a seductive trap.

## Choosing the Right Tool for a Real Problem

When a task lands on your desk, resist jumping to code. Frame it first:

1. **What are the operations, and how often?** Mostly reads by key? A dictionary. Mostly ordered iteration? A sorted structure or sort-once list. Insert/remove at ends? A stack or queue.
2. **What's the realistic `n`?** Ten items and a linear scan is fine. Ten million and that scan is your bottleneck.
3. **What are the constraints?** Memory ceiling? Latency ceiling on *every* op (worst-case matters, not just amortized)? Ordering requirements?
4. **What's the dominant cost?** Optimize the operation that runs most often or on the largest data. A slow one-time setup with fast repeated lookups is usually the right trade.

> The framing itself is the senior move. Juniors ask "which data structure is best?" Seniors ask "what does this problem actually need, and what's the simplest structure that delivers it within the constraints?"

## System Design Fundamentals

Zoom out from a single function to an entire service serving millions of users. System design has no single right answer — it's about making and *justifying* trade-offs. Both in interviews and in real architecture reviews, a repeatable process keeps you from flailing.

### A Repeatable Approach

1. **Clarify requirements.** Never design against a vague prompt. Separate *functional* requirements (what it does) from *non-functional* ones (how well: latency, availability, consistency, durability). Ask: how many users? Read-heavy or write-heavy? Is stale data acceptable?

2. **Estimate scale (back-of-the-envelope).** Turn "millions of users" into numbers. Daily active users, requests per second, storage per year, bandwidth. These numbers decide your architecture — a system doing 10 requests/second and one doing 100,000 are fundamentally different machines.

3. **Define the APIs.** A few endpoint signatures nail down the contract and surface hidden requirements. `POST /shorten`, `GET /{code}`.

4. **Design the data model.** What entities, what access patterns, SQL or NoSQL? Access patterns should *drive* the schema, not the other way around.

5. **Sketch high-level components.** Boxes and arrows: clients, load balancer, app servers, caches, databases, queues. Show the request flow.

6. **Identify and address bottlenecks.** Where does it break under load? Single database? Add read replicas and a cache. Hot path? Add a CDN. Traffic spikes? Add a queue to absorb bursts.

### The Building Blocks

- **Load balancer** — spreads incoming requests across many identical app servers, enabling *horizontal scaling* and removing single points of failure. The traffic cop of your system.
- **Cache** (e.g., Redis) — an in-memory store for hot data, turning slow database reads into microsecond lookups. The 80/20 rule applies: a small cache of the most-requested data absorbs most of the load. Watch for cache invalidation and staleness.
- **CDN** — geographically distributed edge servers that serve static assets (images, JS, video) close to users, cutting latency and offloading your origin.
- **Database with replicas** — a primary handles writes; read replicas handle reads. Since most systems are read-heavy, this scales reads dramatically. The cost is *replication lag* — replicas are slightly behind (eventual consistency).
- **Message queue** (e.g., RabbitMQ, Kafka) — decouples producers from consumers. The web request drops a job on the queue and returns instantly; workers process asynchronously. Absorbs traffic spikes and smooths load. This is the async pattern from earlier chapters, applied at architecture scale.
- **Object storage** (e.g., S3, Azure Blob) — cheap, durable, effectively infinite storage for large blobs. Don't put user-uploaded videos in your relational database; put a URL there and the bytes in object storage.

### Scaling Patterns

- **Vertical scaling** — a bigger machine. Simple, but has a hard ceiling and a single point of failure.
- **Horizontal scaling** — more machines behind a load balancer. Nearly unlimited, but requires your app servers to be *stateless* (no session data stored locally) so any server can handle any request.
- **Caching** — the highest-leverage move for read-heavy systems.
- **Database scaling** — replicas for read throughput; *sharding* (partitioning data across databases by some key) for write throughput when one database can't hold the load.
- **Asynchronous processing** — push slow work (emails, image processing, analytics) off the request path onto queues and workers.

### Worked Mini-Example: A URL Shortener

Let's tie it together. Design a service that turns long URLs into short codes (like `bit.ly`).

**1. Requirements.** Functional: create a short code for a URL; redirect a short code to the original. Non-functional: very read-heavy (redirects vastly outnumber creations), low-latency redirects, high availability (a down redirect breaks every published link).

**2. Scale estimate.** Say 100 million new URLs per month — roughly 40 writes/second. If reads are 100× writes, that's ~4,000 reads/second. Storage: 100M/month × 12 × several years × ~500 bytes ≈ low terabytes. Modest writes, heavy reads — this shape *screams* "cache the reads."

**3. API.**
```
POST /shorten   { "url": "https://very/long/url" }  ->  { "code": "aZ3x9" }
GET  /{code}                                         ->  301 redirect to original
```

**4. Data model.** A single mapping: `code -> longUrl` (plus metadata like created-at, owner). The only access patterns are "look up by code" and "insert." This is a perfect key-value workload — a NoSQL store or a well-indexed SQL table both work.

**5. Generating the code.** Take an auto-incrementing ID and **Base62-encode** it (`0-9`, `a-z`, `A-Z`). Base62 packs ~62³ ≈ 238,000 codes into 3 characters and ~62⁷ ≈ 3.5 trillion into 7 — plenty, and short. This is exactly the "choose the encoding for the constraint" thinking from Big-O made concrete: a base conversion.

```csharp
public static class Base62
{
    private const string Alphabet =
        "0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";

    public static string Encode(long id)
    {
        if (id == 0) return "0";
        Span<char> buf = stackalloc char[11];   // 62^11 > long.MaxValue
        var i = buf.Length;
        while (id > 0)
        {
            buf[--i] = Alphabet[(int)(id % 62)];
            id /= 62;
        }
        return new string(buf[i..]);
    }
}
```

(Fill the buffer from the end and slice — building most-significant-digit-first with `Insert(0, …)` in a loop would be exactly the accidental O(n²) this chapter warned about.)

**6. Components and flow.**
- A **load balancer** fronts several stateless app servers.
- On `POST /shorten`: get the next ID, Base62-encode it, store `code -> url`, return the code.
- On `GET /{code}`: **check the cache first** (Redis). On a hit — the overwhelming common case — redirect immediately. On a miss, read the database, populate the cache, then redirect. This is the hash-lookup pattern from earlier in the chapter, scaled to a distributed system: the cache *is* a giant dictionary.
- The database uses **read replicas** so redirect reads that miss the cache still scale.

**7. Bottlenecks.**
- *Redirect latency* — solved by the cache; hot links live in memory.
- *Single database for writes* — 40 writes/second is trivial for one primary, so no sharding needed yet. (Knowing *not* to over-engineer is as senior as knowing how to shard.)
- *Availability* — multiple app servers plus replicas remove single points of failure; object storage or a CDN isn't needed since there are no large assets.

Notice how the estimate in step 2 justified every later decision. That traceability — from numbers to architecture — is what a good design review looks for.

### A Second Angle: Rate Limiting

Rate limiting protects a service from abuse and overload — "at most N requests per user per minute." A clean, common algorithm is the **token bucket**: each user has a bucket that refills at a steady rate; each request spends a token; an empty bucket means rejection. It uses the same primitives we've discussed — a per-user counter in a fast store like Redis, checked and updated on each request. At scale you'd run this in a shared cache so all app servers agree on the count, illustrating again how a humble data structure (a counter map) becomes a distributed system component.

## Bringing It Together

The through-line of this chapter is a single habit: **reason about cost before you commit to code.** At the small scale, that means picking the collection whose Big-O matches your access pattern and your `n`. At the large scale, it means estimating load and composing building blocks whose trade-offs you understand. The techniques differ in size, not in kind — a distributed cache is a dictionary, a message queue is a `Queue<T>` that survives across machines, and a load balancer is horizontal scaling made physical.

Senior engineers aren't the ones who memorized the most algorithms. They're the ones who reliably ask "how does this behave as it grows?" — and have the vocabulary to answer.

## Sources & Further Reading

- Microsoft Learn — *System.Collections.Generic Namespace* and the individual type references (`List<T>`, `Dictionary<TKey,TValue>`, `HashSet<T>`, `SortedDictionary<TKey,TValue>`, `Queue<T>`, `Stack<T>`, `PriorityQueue<TElement,TPriority>`). https://learn.microsoft.com/en-us/dotnet/api/system.collections.generic
- Microsoft Learn — *Array.Sort Method* (documents the introsort hybrid used by the runtime). https://learn.microsoft.com/en-us/dotnet/api/system.array.sort
- Microsoft Learn — *Guidelines for overriding Equals() and GetHashCode()* and the `HashCode.Combine` reference.
- Thomas H. Cormen, Charles E. Leiserson, Ronald L. Rivest, Clifford Stein — *Introduction to Algorithms (CLRS)*, 4th edition (Big-O, sorting, graph algorithms, dynamic programming, amortized analysis).
- Gayle Laakmann McDowell — *Cracking the Coding Interview*, 6th edition (data-structure selection, interview algorithm patterns).
- Alex Xu — *System Design Interview: An Insider's Guide*, Volumes 1 & 2 (the design process, building blocks, URL shortener and rate limiter case studies).
