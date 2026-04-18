namespace PicoLog.Abs;

public interface ILogger
{
    IDisposable BeginScope<TState>(TState state)
        where TState : notnull;

    void Log(LogLevel logLevel, string message, Exception? exception = null);

    void Log(
        LogLevel logLevel,
        string message,
        IReadOnlyList<KeyValuePair<string, object?>>? properties,
        Exception? exception
    );

    /// <summary>
    /// Asynchronously logs a message.
    /// </summary>
    /// <remarks>
    /// Completion indicates that the logger accepted the write or finished any configured
    /// backpressure handling at the logger boundary. It does not, by itself, guarantee that the
    /// entry has already been durably written by downstream sinks.
    /// </remarks>
    Task LogAsync(
        LogLevel logLevel,
        string message,
        Exception? exception = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Asynchronously logs a message with optional structured properties.
    /// </summary>
    /// <remarks>
    /// Completion indicates that the logger accepted the write or finished any configured
    /// backpressure handling at the logger boundary. It does not, by itself, guarantee that the
    /// entry has already been durably written by downstream sinks.
    /// </remarks>
    Task LogAsync(
        LogLevel logLevel,
        string message,
        IReadOnlyList<KeyValuePair<string, object?>>? properties,
        Exception? exception,
        CancellationToken cancellationToken = default
    );
}

public interface ILogger<out TCategory> : ILogger;
