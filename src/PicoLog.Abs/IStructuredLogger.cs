namespace PicoLog.Abs;

public interface IStructuredLogger : ILogger
{
    void LogStructured(
        LogLevel logLevel,
        string message,
        IReadOnlyList<KeyValuePair<string, object?>>? properties = null,
        Exception? exception = null
    );

    Task LogStructuredAsync(
        LogLevel logLevel,
        string message,
        IReadOnlyList<KeyValuePair<string, object?>>? properties = null,
        Exception? exception = null,
        CancellationToken cancellationToken = default
    );
}

public interface IStructuredLogger<out TCategory> : ILogger<TCategory>, IStructuredLogger;
