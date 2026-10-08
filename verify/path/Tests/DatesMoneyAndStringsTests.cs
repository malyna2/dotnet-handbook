using Xunit;

namespace LearningPath.Tests;

// Part 1, Module 13 (Dates, Money and Strings): the experiment printed on its page, run as its own
// process. Each line holds both halves (the trap and the fix), so a change in either direction fails.
public sealed class DatesMoneyAndStringsTests
{
    [Fact]
    public async Task Double_loses_cents_decimal_keeps_them_and_rounding_modes_disagree()
    {
        var run = await Experiment.RunAsync("MoneyAndRounding");

        Assert.Equal(0, run.ExitCode);
        Assert.Equal(
            [
                "0.1 + 0.2: double 0.30000000000000004 (== 0.3: False), decimal 0.3 (== 0.3: True)",
                "1.005 to cents, AwayFromZero: double 1, decimal 1.01",
                "  the double 1.005 is really 1.00499999999999989342",
                "Math.Round(2.5m) = 2, Math.Round(3.5m) = 4 (half to even: the default)",
                "Math.Round(2.5m, MidpointRounding.AwayFromZero) = 3",
                "Math.Round(2.345m, 2) = 2.34, but 2.345m.ToString(\"F2\") = 2.35",
                "100.00 split three ways: 33.33 each, 99.99 in total; the missing cent needs a rule",
            ],
            run.Lines);
    }
}
