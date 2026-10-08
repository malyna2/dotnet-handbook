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
