namespace Pico.Logging;

public sealed class LoggerFactory(IEnumerable<ILogSink> sinks)
    : ILoggerFactory,
        IDisposable,
        IAsyncDisposable
{
    private readonly ILogSink[] _sinks =
        sinks?.ToArray() ?? throw new ArgumentNullException(nameof(sinks));
    private readonly ConcurrentDictionary<string, InternalLogger> _loggers =
        new(StringComparer.Ordinal);
    private int _disposeState;

    public LogLevel MinLevel { get; set; } = LogLevel.Debug;

    public ILogger CreateLogger(string categoryName)
    {
        ObjectDisposedException.ThrowIf(_disposeState != 0, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(categoryName);

        return _loggers.GetOrAdd(categoryName, name => new InternalLogger(name, _sinks, this));
    }

    internal bool IsEnabled(LogLevel logLevel) => MinLevel != LogLevel.None && logLevel <= MinLevel;

    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0)
            return;

        List<Exception>? exceptions = null;

        foreach (var logger in _loggers.Values)
        {
            try
            {
                await logger.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                (exceptions ??=  []).Add(ex);
            }
        }

        _loggers.Clear();

        foreach (var sink in _sinks)
        {
            try
            {
                await DisposeSinkAsync(sink).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                (exceptions ??=  []).Add(ex);
            }
        }

        if (exceptions is { Count: > 0 })
            throw new AggregateException(exceptions);
    }

    private static async ValueTask DisposeSinkAsync(ILogSink sink)
    {
        if (sink is IAsyncDisposable asyncDisposable)
        {
            await asyncDisposable.DisposeAsync().ConfigureAwait(false);
            return;
        }

        sink.Dispose();
    }
}
