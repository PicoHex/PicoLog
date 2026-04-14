namespace PicoLog.Abs;

public interface ILogSink : IDisposable, IAsyncDisposable
{
    /// <summary>
    /// Writes a log entry to the sink.
    /// </summary>
    /// <remarks>
    /// A single sink instance can be shared across multiple category loggers within the same
    /// <see cref="ILoggerFactory"/>. Implementations must therefore tolerate concurrent
    /// <see cref="WriteAsync(LogEntry, CancellationToken)"/> calls and synchronize any shared
    /// mutable state or writers as needed.
    /// </remarks>
    Task WriteAsync(LogEntry entry, CancellationToken cancellationToken = default);
}
