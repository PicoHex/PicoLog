namespace Pico.Logger.Abs;

public sealed record LogEntry
{
    public DateTimeOffset Timestamp { get; set; }
    public LogLevel Level { get; set; }
    public string? Category { get; set; }
    public string? Message { get; set; }
    public Exception? Exception { get; set; }
    public IReadOnlyList<object>? Scopes { get; set; }
}
