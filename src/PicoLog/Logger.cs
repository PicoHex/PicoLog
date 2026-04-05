namespace PicoLog;

public sealed class Logger<TCategory> : IStructuredLogger<TCategory>
{
    private readonly ILogger _innerLogger;
    private readonly IStructuredLogger? _structuredLogger;

    public Logger(ILoggerFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);

        _innerLogger = factory.CreateLogger(typeof(TCategory).FullName!);
        _structuredLogger = _innerLogger as IStructuredLogger;
    }

    public IDisposable BeginScope<TState>(TState state)
        where TState : notnull => _innerLogger.BeginScope(state);

    public void Log(LogLevel logLevel, string message, Exception? exception = null) =>
        _innerLogger.Log(logLevel, message, exception);

    public Task LogAsync(
        LogLevel logLevel,
        string message,
        Exception? exception = null,
        CancellationToken cancellationToken = default
    ) => _innerLogger.LogAsync(logLevel, message, exception, cancellationToken);

    public void LogStructured(
        LogLevel logLevel,
        string message,
        IReadOnlyList<KeyValuePair<string, object?>>? properties = null,
        Exception? exception = null
    )
    {
        if (_structuredLogger is not null)
        {
            _structuredLogger.LogStructured(logLevel, message, properties, exception);
            return;
        }

        _innerLogger.Log(logLevel, message, exception);
    }

    public Task LogStructuredAsync(
        LogLevel logLevel,
        string message,
        IReadOnlyList<KeyValuePair<string, object?>>? properties = null,
        Exception? exception = null,
        CancellationToken cancellationToken = default
    )
    {
        if (_structuredLogger is not null)
            return _structuredLogger.LogStructuredAsync(
                logLevel,
                message,
                properties,
                exception,
                cancellationToken
            );

        return _innerLogger.LogAsync(logLevel, message, exception, cancellationToken);
    }
}
