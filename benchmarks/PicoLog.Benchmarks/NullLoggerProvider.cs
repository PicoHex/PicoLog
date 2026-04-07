namespace PicoLog.Benchmarks;

/// <summary>
/// A no-op logger provider for Microsoft.Extensions.Logging.
/// Produces loggers that discard all output, matching the NullSink behaviour.
/// </summary>
internal sealed class NullLoggerProvider : Microsoft.Extensions.Logging.ILoggerProvider
{
    public Microsoft.Extensions.Logging.ILogger CreateLogger(string categoryName) =>
        NullMelLogger.Instance;

    public void Dispose() { }

    private sealed class NullMelLogger : Microsoft.Extensions.Logging.ILogger
    {
        public static readonly NullMelLogger Instance = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

        public void Log<TState>(
            Microsoft.Extensions.Logging.LogLevel logLevel,
            Microsoft.Extensions.Logging.EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            // intentionally empty – measures framework overhead only
        }
    }
}
