namespace PicoLog;

/// <summary>
/// Typed adapter for <see cref="ILogger{TCategory}"/>.
/// </summary>
public sealed class Logger<TCategory> : ILogger<TCategory>
{
    private readonly ILogger _innerLogger;

    public Logger(ILoggerFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);

        _innerLogger = factory.CreateLogger(typeof(TCategory).FullName!);
    }

    public IDisposable BeginScope<TState>(TState state)
        where TState : notnull => _innerLogger.BeginScope(state);

    public void Log(LogLevel logLevel, string message, Exception? exception = null) =>
        _innerLogger.Log(logLevel, message, exception);

    public void Log(
        LogLevel logLevel,
        string message,
        IReadOnlyList<KeyValuePair<string, object?>>? properties,
        Exception? exception
    ) => _innerLogger.Log(logLevel, message, properties, exception);

    public Task LogAsync(
        LogLevel logLevel,
        string message,
        Exception? exception = null,
        CancellationToken cancellationToken = default
    ) => _innerLogger.LogAsync(logLevel, message, exception, cancellationToken);

    public Task LogAsync(
        LogLevel logLevel,
        string message,
        IReadOnlyList<KeyValuePair<string, object?>>? properties,
        Exception? exception,
        CancellationToken cancellationToken = default
    ) => _innerLogger.LogAsync(logLevel, message, properties, exception, cancellationToken);
}
