namespace PicoLog;

public static class FlushExtensions
{
    extension(ILoggerFactory factory)
    {
        /// <summary>
        /// Best-effort flush helper for <see cref="ILoggerFactory"/>.
        /// </summary>
        /// <remarks>
        /// When the runtime factory also implements <see cref="IFlushableLoggerFactory"/>, the
        /// flush is forwarded to the implementation. Otherwise this call completes immediately.
        /// </remarks>
        public ValueTask FlushAsync(CancellationToken cancellationToken = default) =>
            factory is IFlushableLoggerFactory flushable
                ? flushable.FlushAsync(cancellationToken)
                : default;
    }

    extension(ILogSink sink)
    {
        /// <summary>
        /// Best-effort flush helper for <see cref="ILogSink"/>.
        /// </summary>
        /// <remarks>
        /// When the runtime sink also implements <see cref="IFlushableLogSink"/>, the flush is
        /// forwarded to the implementation. Otherwise this call completes immediately.
        /// </remarks>
        public ValueTask FlushAsync(CancellationToken cancellationToken = default) =>
            sink is IFlushableLogSink flushable
                ? flushable.FlushAsync(cancellationToken)
                : default;
    }
}
