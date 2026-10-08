# Part 1 · Module 10: Design Basics

> **What this module makes you able to do.** Explain each SOLID principle by what breaks without it, name the smell in a piece of code and the refactoring that removes it, and choose between a plain `if`, a Strategy and a Decorator — including when no pattern is the right answer.

**Time:** reading ≈ 35 min; hands-on ≈ 35 min — the entry check 5 min, the check at work 30.

## Entry check

*Three questions to answer without notes. All three right: skip to the next module. Otherwise, work through this one.*

**1.** `Square` inherits from `Rectangle` and overrides both setters to keep the sides equal. Every line compiles. Which principle does it break, and how does a caller find out?

<details>
<summary>Answer</summary>

Liskov Substitution. A caller written against `Rectangle` relies on its contract: setting `Height` leaves `Width` alone. Set width 5 and height 4 on a `Square` and the area is 16, not 20. The compiler checks that the types fit; only the behaviour shows that the subtype broke the promise, so the failure surfaces as a wrong result far from the class that caused it. The fix is a different model (a shared `IShape`, or no inheritance), not more overrides: [Chapter 5: SOLID](#solid).
</details>

**2.** A shipping-cost `switch` has gained a fourth case this year, and the same `switch` exists in three files. What do you refactor it into, and what would make you leave it alone?

<details>
<summary>Answer</summary>

A Strategy: one class (or one `Func<Order, decimal>`) per method, chosen once where the object graph is composed. Adding a method then means adding a class, not editing every copy of the `switch` (Open/Closed). Leave it alone when there is one `switch`, in one place, that rarely changes: the pattern would add indirection to buy flexibility nobody uses (YAGNI). The duplicated `switch` is the pain that pays for it: [Chapter 5: Strategy](#strategy).
</details>

**3.** You need caching around `IProductRepository` without editing `SqlProductRepository`. How, and why does it satisfy both Single Responsibility and Open/Closed?

<details>
<summary>Answer</summary>

A Decorator: `CachingProductRepository` implements `IProductRepository` and wraps another instance of it, answering from the cache and delegating misses inward. Callers can't tell the difference because the interface is the same. Caching gets its own class with its own reason to change (SRP), and behaviour is added by adding code, not by editing the tested repository (OCP). Register it with Scrutor's `Decorate`, since the built-in container has no decorator registration: [Chapter 5: Decorator](#decorator).
</details>

## Covers

- each SOLID letter as the failure it prevents, not as a slogan;
- names, small functions that do what their name says, and comments that say *why*;
- the common code smells, and the named refactoring each one points to;
- Strategy and Decorator, the two patterns used every day;
- when not to use a pattern: speculative generality and the wrong abstraction.

## The mechanism to explain without notes

**Every design rule here is a bet on where the next change will land: it gathers the code that changes for one reason into one place, behind a seam, and the indirection that costs is repaid only if that change actually comes.**

Read the principles as failures they prevent:

- **Single Responsibility.** Code that changes for different reasons, living in one class, makes every change risk the unrelated one next to it.
- **Open/Closed.** A conditional copied into many places makes every new case a hunt; a new class per case keeps tested code closed.
- **Liskov.** A subtype that breaks its base type's contract fails in code that never mentions the subtype.
- **Interface Segregation.** A fat interface couples every implementer to methods it doesn't use, and invites `NotImplementedException`.
- **Dependency Inversion.** High-level policy that `new`s its infrastructure can't be tested or recomposed; depending on an abstraction lets the composition root choose.

Strategy and Decorator are those principles as code: Strategy moves a varying algorithm behind an interface, so a new case is a new class; Decorator adds behaviour around an interface without touching what it wraps. Names, small functions and *why*-comments do the same job locally: the reader learns what the code means without reconstructing it.

The bet can lose. An interface with one implementation, a factory for one product, a hierarchy "for flexibility" all charge every reader for a change that never arrives. Write the simple version first, and refactor toward a pattern when the pain is real: the same `switch` edited for the third time, a class with three reasons to change.

## Read (≈ 35 min)

1. [Chapter 5: What a Design Pattern Actually Is](#what-a-design-pattern-actually-is), including *The Danger of Overusing Patterns*: a pattern as a recorded trade-off, and YAGNI.
2. [Chapter 5: Principles: The Foundation Under the Patterns](#principles-the-foundation-under-the-patterns): SOLID with before-and-after code, then DRY, KISS, YAGNI, Demeter and composition over inheritance.
3. [Chapter 5: Strategy](#strategy) and [Decorator](#decorator): the two patterns to know cold, and when a `Func<>` is enough.
4. [Chapter 5: Clean Code & Code Smells](#clean-code-code-smells): naming, functions, comments, the smells table, a worked refactoring, and refactoring under tests.

## Check at work

**Inspect.** Open the class in your service that changes most often (`git log --format= --name-only | sort | uniq -c | sort -rn | head`). List its reasons to change; more than one is a candidate for Extract Class. Then search for `switch` statements on the same type or enum in more than one file, and for interfaces with exactly one implementation and no test double. Good: each is justified in one sentence. Bad: "we might need it".

**Measure.** In your next three pull requests, name every smell you notice from the Chapter 5 table in a review comment, with the refactoring it points to. Count how many the author agreed with; the ones they didn't are where your reasoning needs one more sentence.
