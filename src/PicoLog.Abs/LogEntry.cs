namespace PicoLog.Abs;

public sealed record LogEntry
{
    public DateTimeOffset Timestamp { get; init; }
    public LogLevel Level { get; init; }
    public string? Category { get; init; }
    public string? Message { get; init; }
    public Exception? Exception { get; init; }
    public IReadOnlyList<object>? Scopes { get; init; }
    public IReadOnlyList<KeyValuePair<string, object?>>? Properties { get; init; }
}
