namespace PicoLog;

/// <summary>
/// Optional companion contract for <see cref="ILogSink"/> implementations that can flush buffered
/// writes without disposing the sink.
/// </summary>
/// <remarks>
/// Flush applies only to the sink's own buffering and completion boundary. It does not imply that a
/// wider <see cref="ILoggerFactory"/> pipeline has been drained unless the owning factory also
/// coordinates a factory-level flush.
/// </remarks>
public interface IFlushableLogSink : ILogSink
{
    ValueTask FlushAsync(CancellationToken cancellationToken = default);
}
