using System.Runtime.CompilerServices;

// Prove it: after `throw;` the stack trace still starts in the method that threw; `throw ex;` restarts it at the rethrow.
foreach (bool resetTrace in new[] { false, true })
    try { OrderService.Get(42, resetTrace); }
    catch (Exception e) { Console.WriteLine($"{(resetTrace ? "throw ex;" : "throw;")}\n{e.StackTrace}"); }

static class OrderService
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Get(int id, bool resetTrace)
    {
        try { OrderStore.Load(id); }
        catch (InvalidOperationException) when (!resetTrace)
        {
            throw;                      // rethrows the same exception, its trace intact
        }
        catch (InvalidOperationException ex)
        {
#pragma warning disable CA2200         // the SDK flags the next line by default
            throw ex;                   // the trace restarts on this line
        }
    }
}

static class OrderStore
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Load(int id) => throw new InvalidOperationException($"order {id} is missing");
}
