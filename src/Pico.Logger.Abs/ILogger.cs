namespace Pico.Logger.Abs;

public interface ILogger
{
    IDisposable BeginScope<TState>(TState state)
        where TState : notnull;
    void Log(LogLevel logLevel, string message, Exception? exception = null);
    ValueTask LogAsync(
        LogLevel logLevel,
        string message,
        Exception? exception = null,
        CancellationToken cancellationToken = default
    );
}

public interface ILogger<out TCategory> : ILogger;
