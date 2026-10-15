// Prove it: a survivor moves up one generation per collection; a finalizer keeps garbage alive for one more.
using System.Runtime.CompilerServices;

var survivor = new object();
Console.Write($"a live object:       gen {GC.GetGeneration(survivor)}");
for (int i = 0; i < 2; i++) { GC.Collect(); Console.Write($" -> gen {GC.GetGeneration(survivor)}"); }
Console.WriteLine("  (one step per collection it survives)");

WeakReference plain = Unreachable(), finalizable = UnreachableWithFinalizer();
GC.Collect();
Console.WriteLine($"after 1 collection:  plain alive {plain.IsAlive}, with finalizer alive {finalizable.IsAlive}");
GC.WaitForPendingFinalizers();                 // the finalizer thread runs ~WithFinalizer
Console.WriteLine($"finalizer has run:   {WithFinalizer.Ran}");
GC.Collect();
Console.WriteLine($"after 2 collections: plain alive {plain.IsAlive}, with finalizer alive {finalizable.IsAlive}");
GC.KeepAlive(survivor);

// Allocated in methods of their own, so no local in this method keeps them reachable.
// trackResurrection: true follows an object until its memory is reclaimed, not just until it is unreachable.
[MethodImpl(MethodImplOptions.NoInlining)]
static WeakReference Unreachable() => new(new object(), trackResurrection: true);
[MethodImpl(MethodImplOptions.NoInlining)]
static WeakReference UnreachableWithFinalizer() => new(new WithFinalizer(), trackResurrection: true);

class WithFinalizer
{
    public static bool Ran;
    ~WithFinalizer() => Ran = true;
}
