namespace Pico.Logging.Abs;

public interface ILogSink : IDisposable, IAsyncDisposable
{
    Task WriteAsync(LogEntry entry, CancellationToken cancellationToken = default);
}
