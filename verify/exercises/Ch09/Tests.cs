using Microsoft.Extensions.Logging;
using Xunit;

namespace Ch09.Exercises;

// Each defect is shown on the buggy code and shown absent on the fix, so the suite stays green
// and a regression in either direction turns it red. A capturing provider records exactly what
// every real logging provider (console, Serilog, OpenTelemetry) would receive.

public sealed class FailureLogTests
{
    private static readonly Guid OrderId = Guid.Parse("6f9c1d2e-0000-4000-8000-000000000042");

    [Fact]
    public async Task Buggy_service_logs_a_flat_string_without_the_exception_or_the_order()
    {
        var (log, factory) = Capture();
        var service = new BuggyPaymentService(new DecliningGateway(), factory.CreateLogger<BuggyPaymentService>());

        Assert.False(await service.ChargeAsync(OrderId, 120m, TestContext.Current.CancellationToken));

        Entry entry = Assert.Single(log);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.Null(entry.Exception);                                    // no type, no stack trace, no inner exception
        Assert.DoesNotContain("OrderId", entry.Properties.Keys);         // nothing to filter or join on
        Assert.Equal("Payment failed: Gateway timeout", entry.Message);  // the order appears nowhere
    }

    [Fact]
    public async Task Fixed_service_logs_the_exception_and_the_order_as_properties()
    {
        var (log, factory) = Capture();
        var service = new FixedPaymentService(new DecliningGateway(), factory.CreateLogger<FixedPaymentService>());

        Assert.False(await service.ChargeAsync(OrderId, 120m, TestContext.Current.CancellationToken));

        Entry entry = Assert.Single(log);
        Assert.IsType<HttpRequestException>(entry.Exception);
        Assert.IsType<TimeoutException>(entry.Exception!.InnerException);
        Assert.Equal(OrderId, entry.Properties["OrderId"]);
        Assert.Equal(120m, entry.Properties["Amount"]);
    }

    [Fact]
    public async Task Buggy_service_reports_a_cancelled_request_as_a_failed_payment()
    {
        var (log, factory) = Capture();
        var service = new BuggyPaymentService(new HangingGateway(), factory.CreateLogger<BuggyPaymentService>());
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        Assert.False(await service.ChargeAsync(OrderId, 120m, cts.Token));   // the caller is told "declined"

        Entry entry = Assert.Single(log);
        Assert.Equal(LogLevel.Error, entry.Level);                            // and on-call gets an error
        Assert.StartsWith("Payment failed: A task was canceled", entry.Message);
    }

    [Fact]
    public async Task Fixed_service_lets_cancellation_propagate_without_an_error()
    {
        var (log, factory) = Capture();
        var service = new FixedPaymentService(new HangingGateway(), factory.CreateLogger<FixedPaymentService>());
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ChargeAsync(OrderId, 120m, cts.Token));

        Assert.Empty(log);
    }

    private static (List<Entry> Log, ILoggerFactory Factory) Capture()
    {
        var log = new List<Entry>();
        return (log, LoggerFactory.Create(b => b.AddProvider(new CapturingProvider(log))));
    }
}

internal sealed class DecliningGateway : IPaymentGateway
{
    public Task ChargeAsync(Guid orderId, decimal amount, CancellationToken ct) =>
        throw new HttpRequestException("Gateway timeout", new TimeoutException("No response in 30 s"));
}

internal sealed class HangingGateway : IPaymentGateway
{
    public Task ChargeAsync(Guid orderId, decimal amount, CancellationToken ct) => Task.Delay(Timeout.Infinite, ct);
}

internal sealed record Entry(LogLevel Level, string Message, Exception? Exception, IReadOnlyDictionary<string, object?> Properties);

internal sealed class CapturingProvider(List<Entry> log) : ILoggerProvider, ILogger
{
    public ILogger CreateLogger(string categoryName) => this;
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;
    public void Dispose() { }

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        var properties = (state as IEnumerable<KeyValuePair<string, object?>> ?? [])
            .ToDictionary(p => p.Key, p => p.Value);
        lock (log) log.Add(new Entry(logLevel, formatter(state, exception), exception, properties));
    }
}
