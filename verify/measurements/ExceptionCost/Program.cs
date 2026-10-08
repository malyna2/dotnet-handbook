// Chapter 5, "Exceptions vs Result, Settled Properly": what one throw/catch pair costs on .NET 10,
// by the number of frames between the throw and the catch: caught and dropped, caught and its stack
// trace read (what logging the exception does), and a failure returned as a value instead.
// Run in Release: dotnet run -c Release
using System.Diagnostics;
using System.Runtime.CompilerServices;

int[] depths = [1, 10, 20, 50];
const int Iterations = 20_000;

foreach (var depth in depths) Measure(depth, 2_000); // warm-up: tier-up and type loading
Console.WriteLine($"{"frames",6} {"throw/catch",14} {"+ StackTrace",14} {"return value",14}");
foreach (var depth in depths)
{
    var (throwUs, traceUs, returnUs) = Measure(depth, Iterations);
    Console.WriteLine($"{depth,6} {throwUs,11:F2} µs {traceUs,11:F2} µs {returnUs,11:F3} µs");
}

static (double ThrowUs, double TraceUs, double ReturnUs) Measure(int depth, int iterations)
{
    var sw = Stopwatch.StartNew();
    for (var i = 0; i < iterations; i++)
    {
        try { Throws(depth); } catch (InvalidOperationException) { }
    }
    var throwUs = sw.Elapsed.TotalMicroseconds / iterations;

    sw.Restart();
    var chars = 0;
    for (var i = 0; i < iterations; i++)
    {
        try { Throws(depth); } catch (InvalidOperationException ex) { chars += ex.StackTrace!.Length; }
    }
    var traceUs = sw.Elapsed.TotalMicroseconds / iterations;
    if (chars == 0) throw new UnreachableException();

    sw.Restart();
    var failures = 0;
    for (var i = 0; i < iterations; i++)
        if (!Returns(depth)) failures++;
    var returnUs = sw.Elapsed.TotalMicroseconds / iterations;
    if (failures != iterations) throw new UnreachableException();
    return (throwUs, traceUs, returnUs);
}

[MethodImpl(MethodImplOptions.NoInlining)]
static void Throws(int depth)
{
    if (depth == 1) throw new InvalidOperationException("not found");
    Throws(depth - 1);
}

[MethodImpl(MethodImplOptions.NoInlining)]
static bool Returns(int depth) => depth == 1 ? false : Returns(depth - 1);
