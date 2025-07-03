namespace Pico.Logger.Abs;

public interface ILogSink : IDisposable, IAsyncDisposable
{
    ValueTask WriteAsync(LogEntry entry, CancellationToken cancellationToken = default);
}
