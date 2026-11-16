namespace Outbox.Core;

public sealed record OrderPlaced(long OrderId, long CustomerId, decimal Total);

/// <summary>Worth retrying: a timeout, a deadlock, a dependency that is briefly down.</summary>
public sealed class TransientException(string message) : Exception(message);

/// <summary>Retrying cannot help: the message itself is wrong. It goes to the dead-letter topic at once.</summary>
public sealed class PermanentException(string message) : Exception(message);

public enum HandleResult { Processed, Duplicate }
