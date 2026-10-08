# Part 1 · Module 13: Dates, Money and Strings

> **What this module makes you able to do.** Store and compute times, amounts and identifiers so that the result doesn't depend on the server's time zone, the user's culture, or a rounding rule nobody chose; and test code that depends on "now".

**Time:** reading ≈ 20 min; hands-on ≈ 50 min — the entry check 5 min, the experiment 15, the check at work 30.

## Entry check

*Three questions to answer without notes. All three right: skip to the next module. Otherwise, work through this one.*

**1.** A job runs "every day at 02:30 Europe/Berlin". What happens on 29 March 2026 and on 25 October 2026?

<details>
<summary>Answer</summary>

On 29 March the clocks jump from 02:00 to 03:00: 02:30 never happens, and `TimeZoneInfo.ConvertTimeToUtc` throws `ArgumentException` for it. On 25 October they fall back from 03:00 to 02:00: 02:30 happens twice, at two different instants, and the conversion silently picks the second (standard-time) one. Depending on the scheduler, the job runs twice, once, or not at all. Schedule in UTC, or write down the policy for the gap and the overlap. See [Chapter 26: Daylight Saving Time: gaps and overlaps](#daylight-saving-time-gaps-and-overlaps).
</details>

**2.** Why is `Math.Round(2.5)` equal to `2`, and why does `Math.Round(1.005, 2, MidpointRounding.AwayFromZero)` give `1` rather than `1.01`?

<details>
<summary>Answer</summary>

`Math.Round` defaults to `MidpointRounding.ToEven` (banker's rounding): a midpoint goes to the even neighbour. The second one is a `double` problem: the literal 1.005 is stored as the nearest binary fraction, 1.00499999999999989342, which is below the midpoint, so no rounding mode can take it up. With `decimal`, `1.005m` is exact and rounds to `1.01`. See [Chapter 26: Money and Numbers](#money-and-numbers).
</details>

**3.** What is wrong with `if (role.ToLower() == "admin")`, and what do you write instead?

<details>
<summary>Answer</summary>

`ToLower()` uses the current culture. Under Turkish (`tr-TR`), `"ADMIN".ToLower()` is `"admın"` with a dotless ı, so the check fails on a machine or request with that culture. An identifier is not text for humans: compare it ordinally, `string.Equals(role, "admin", StringComparison.OrdinalIgnoreCase)`. See [Chapter 26: String comparison and sorting: the quiet catastrophe](#string-comparison-and-sorting-the-quiet-catastrophe).
</details>

## Covers

- `DateTime` versus `DateTimeOffset` versus `DateOnly` and `TimeOnly`, and why server code never reads `DateTime.Now`;
- storing and computing in UTC; daylight saving gaps and overlaps; IANA versus Windows zone IDs;
- `TimeProvider` for code and tests that depend on time;
- money: `decimal`, never `double`; `Math.Round`'s banker's default; minor units plus a currency;
- culture-aware formatting for people, invariant formats for data;
- string comparison: ordinal for identifiers, a culture for display, and the overloads that pick a culture silently.

## The mechanism to explain without notes

**A value is correct only together with the context that gives it meaning: an instant needs UTC or an offset, a wall-clock time needs a zone, an amount needs a currency and a rounding rule, a comparison needs a declared kind.**

When the context is implicit, the machine supplies one, and every trap is that substitution:

- **Time.** `DateTime.Now` takes the server's zone; a `DateTime` with `Kind = Unspecified` takes whatever zone the next conversion assumes. Store instants as UTC or `DateTimeOffset`, keep the user's IANA zone ID beside any wall-clock value you must keep, and convert at the edge. A local day can be 23 or 25 hours long, so local arithmetic is wrong twice a year.
- **"Now".** `TimeProvider` makes the clock an injected dependency, so a test can fix it and move it.
- **Money.** `double` stores the nearest binary fraction, so most cents are not stored exactly; `decimal` stores a whole number and a power-of-ten scale, so they are. Rounding is a business rule: `Math.Round` defaults to half-to-even, formatting with `"F2"` rounds half away from zero, and splitting a total leaves remainders that a rule must place.
- **Culture.** `CurrentCulture` decides decimal marks, date order and casing. Text for people uses it; data for machines (JSON, logs, file names, keys) uses `CultureInfo.InvariantCulture` and ISO 8601.
- **Strings.** `==` and `Equals` are ordinal, but `ToUpper()`, `ToLower()`, `string.Compare`, `StartsWith(string)` and `IndexOf(string)` use the current culture unless you pass a `StringComparison`.

## Read (≈ 20 min)

1. [Chapter 26: Date and Time Done Right](#date-and-time-done-right): the four types, UTC, DST gaps and overlaps, zone IDs, NodaTime, `TimeProvider`, parsing.
2. [Chapter 26: Money and Numbers](#money-and-numbers): `decimal`, rounding modes, minor units with a currency, culture-aware number formatting.
3. [Chapter 26: CurrentCulture vs CurrentUICulture](#currentculture-vs-currentuiculture).
4. [Chapter 26: String comparison and sorting: the quiet catastrophe](#string-comparison-and-sorting-the-quiet-catastrophe).

## Prove it

Money first: predict each line before you run it. It needs only the .NET 10 SDK.

`verify/path/MoneyAndRounding/Program.cs` · run it from `verify/path` with `dotnet run --project MoneyAndRounding`:

```csharp
// Prove it: double cannot store most decimal fractions, decimal can; Math.Round sends a midpoint to
// the even neighbour unless told otherwise, and ToString("F2") does not round the way Math.Round does.
double d = 0.1 + 0.2;
decimal m = 0.1m + 0.2m;
Console.WriteLine($"0.1 + 0.2: double {d} (== 0.3: {d == 0.3}), decimal {m} (== 0.3: {m == 0.3m})");

double priceAsDouble = 1.005;
Console.WriteLine($"1.005 to cents, AwayFromZero: double {Math.Round(priceAsDouble, 2, MidpointRounding.AwayFromZero)}, " +
    $"decimal {Math.Round(1.005m, 2, MidpointRounding.AwayFromZero)}");
Console.WriteLine($"  the double 1.005 is really {priceAsDouble:F20}");

Console.WriteLine($"Math.Round(2.5m) = {Math.Round(2.5m)}, Math.Round(3.5m) = {Math.Round(3.5m)} (half to even: the default)");
Console.WriteLine($"Math.Round(2.5m, MidpointRounding.AwayFromZero) = {Math.Round(2.5m, MidpointRounding.AwayFromZero)}");
Console.WriteLine($"Math.Round(2.345m, 2) = {Math.Round(2.345m, 2)}, but 2.345m.ToString(\"F2\") = {2.345m.ToString("F2")}");

decimal share = Math.Round(100.00m / 3, 2);
Console.WriteLine($"100.00 split three ways: {share} each, {share * 3} in total; the missing cent needs a rule");
```

```text
0.1 + 0.2: double 0.30000000000000004 (== 0.3: False), decimal 0.3 (== 0.3: True)
1.005 to cents, AwayFromZero: double 1, decimal 1.01
  the double 1.005 is really 1.00499999999999989342
Math.Round(2.5m) = 2, Math.Round(3.5m) = 4 (half to even: the default)
Math.Round(2.5m, MidpointRounding.AwayFromZero) = 3
Math.Round(2.345m, 2) = 2.34, but 2.345m.ToString("F2") = 2.35
100.00 split three ways: 33.33 each, 99.99 in total; the missing cent needs a rule
```

The output is from `verify/path/reference-runs/money-and-rounding.txt` (.NET 10.0.12, Linux x64); it is the same on any machine, because none of it depends on the clock or the culture. What to notice:

- **The double was never 1.005.** An explicit `AwayFromZero` can't fix a value that is already below the midpoint. Equality fails for the same reason: `0.1 + 0.2` and `0.3` are two different binary approximations.
- **Two rounding rules in one invoice.** `Math.Round` keeps 2.34 (half to even), while `"F2"` prints 2.35 (half away from zero). Round once, with an explicit `MidpointRounding`, and format the rounded value, or the document disagrees with the ledger by a cent.
- **`decimal` is exact only for decimal fractions.** A third of 100.00 has no finite decimal form. Allocation needs a rule, such as giving the remainder cent to the first share, so that the parts add up to the total.

## Check at work

**Inspect.** Search your service for `DateTime.Now`, `DateTime.Parse(` and `ToString(` without a culture or format, `double` or `float` on any amount, `Math.Round(` without a `MidpointRounding`, and `.ToLower()` / `.ToUpper()` / `string.Compare(` in comparisons. In the database, look for money in `float` columns and for instants in `datetime`/`datetime2` columns whose zone nobody wrote down.

**Measure.** Run your test suite with another zone and culture: `TZ=Pacific/Chatham dotnet test` on Linux (UTC+12:45 in winter, +13:45 in summer), and one test fixture that sets `CultureInfo.CurrentCulture = new CultureInfo("tr-TR")`. Every new failure is a place where the machine's context leaked into a result.
