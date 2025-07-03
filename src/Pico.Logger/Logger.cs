namespace Pico.Logger;

public sealed class Logger<TCategory>(ILoggerFactory factory) : ILogger<TCategory>
{
    private readonly ILogger _innerLogger = factory.CreateLogger(typeof(TCategory).FullName!);

    public IDisposable BeginScope<TState>(TState state)
        where TState : notnull => _innerLogger.BeginScope(state);

    public void Log(LogLevel logLevel, string message, Exception? exception = null) =>
        _innerLogger.Log(logLevel, message, exception);

    public async ValueTask LogAsync(
        LogLevel logLevel,
        string message,
        Exception? exception = null,
        CancellationToken cancellationToken = default
    ) => await _innerLogger.LogAsync(logLevel, message, exception, cancellationToken);
}
