using PicoLog;
using PicoLog.Abs;

namespace PicoLog.Benchmarks;

internal sealed class BackpressureSink(int spinIterations) : ILogSink
{
    public Task WriteAsync(LogEntry entry, CancellationToken cancellationToken = default)
    {
        Thread.SpinWait(spinIterations);
        return Task.CompletedTask;
    }

    public void Dispose() { }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
