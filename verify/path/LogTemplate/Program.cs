// Prove it: a message template keeps {OrderId} as a named, typed property; an interpolated string arrives flat.
using Microsoft.Extensions.Logging;

using var factory = LoggerFactory.Create(logging => logging.AddProvider(new PrintingProvider()));
ILogger logger = factory.CreateLogger("Orders");
int orderId = 42;

logger.LogInformation("Order {OrderId} placed", orderId);
logger.LogInformation($"Order {orderId} placed");

// The smallest provider there is: it prints what every real provider receives.
sealed class PrintingProvider : ILoggerProvider, ILogger
{
    public ILogger CreateLogger(string categoryName) => this;
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;
    public void Dispose() { }

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        Console.WriteLine($"message:  {formatter(state, exception)}");
        if (state is IEnumerable<KeyValuePair<string, object?>> properties)
            foreach (var (name, value) in properties)
                Console.WriteLine($"  {name} = {value} ({value?.GetType().Name})");
    }
}
