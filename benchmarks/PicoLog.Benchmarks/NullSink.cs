using PicoLog;
using PicoLog.Abs;

namespace PicoLog.Benchmarks;

/// <summary>
/// A no-op sink that discards all log entries.
/// Used to measure pure logging pipeline overhead without I/O.
/// </summary>
internal sealed class NullSink : ILogSink
{
    public Task WriteAsync(LogEntry entry, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public void Dispose() { }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
