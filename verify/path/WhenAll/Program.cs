// Prove it: await Task.WhenAll rethrows ONE exception; the WhenAll task holds all of them.
Task first = Fail(300, "A (listed first, fails last)");
Task second = Fail(50, "B (listed second, fails first)");
Task all = Task.WhenAll(first, second);

try
{
    await all;
}
catch (Exception e)
{
    Console.WriteLine($"await threw:   {e.GetType().Name}: {e.Message}");
    Console.WriteLine($"all.Exception: {all.Exception!.InnerExceptions.Count} inner exceptions");
    foreach (Exception inner in all.Exception.InnerExceptions)
        Console.WriteLine($"  - {inner.Message}");
}

try { all.Wait(); }                             // the blocking API throws the wrapper instead
catch (AggregateException e) { Console.WriteLine($".Wait() threw: AggregateException with {e.InnerExceptions.Count} inner exceptions"); }

try { await Task.WhenAll(FailTyped(300, "A"), FailTyped(50, "B")); }
catch (Exception e) { Console.WriteLine($"WhenAll over Task<int> threw: {e.Message} (argument order this time)"); }

static async Task Fail(int ms, string name) { await Task.Delay(ms); throw new InvalidOperationException(name); }
static async Task<int> FailTyped(int ms, string name) { await Task.Delay(ms); throw new InvalidOperationException(name); }
