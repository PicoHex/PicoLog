namespace PicoLog.Abs;

/// <summary>
/// Optional companion contract for <see cref="ILoggerFactory"/> implementations that can flush
/// accepted log entries without shutting the factory down.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="FlushAsync(CancellationToken)"/> acts as a barrier for log entries that the factory
/// accepted before the flush operation established its snapshot. The operation waits until those
/// entries have been processed through the factory-owned pipelines.
/// </para>
/// <para>
/// Flush is not disposal. Implementations must remain usable for subsequent writes after the flush
/// completes successfully.
/// </para>
/// </remarks>
public interface IFlushableLoggerFactory : ILoggerFactory
{
    ValueTask FlushAsync(CancellationToken cancellationToken = default);
}
