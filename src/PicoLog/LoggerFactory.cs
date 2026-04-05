namespace PicoLog;

public sealed class LoggerFactory : ILoggerFactory, IDisposable, IAsyncDisposable
{
    private readonly ILogSink[] _sinks;
    private readonly ConcurrentDictionary<string, InternalLogger> _loggers =
        new(StringComparer.Ordinal);
    private readonly LoggerScopeProvider _scopeProvider = new();
    private readonly LoggerFactoryOptions _options;
    private int _disposeState;
    private LogLevel _minLevel;

    public LoggerFactory(IEnumerable<ILogSink> sinks)
        : this(sinks, options: null) { }

    public LoggerFactory(IEnumerable<ILogSink> sinks, LoggerFactoryOptions? options)
    {
        _sinks = sinks?.ToArray() ?? throw new ArgumentNullException(nameof(sinks));
        _options = (options ?? new LoggerFactoryOptions()).CreateValidatedCopy();
        _minLevel = _options.MinLevel;
    }

    public LogLevel MinLevel
    {
        get => _minLevel;
        set => _minLevel = value;
    }

    public ILogger CreateLogger(string categoryName)
    {
        ObjectDisposedException.ThrowIf(_disposeState != 0, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(categoryName);

        return _loggers.GetOrAdd(categoryName, name => new InternalLogger(name, _sinks, this));
    }

    internal bool IsAcceptingWrites => Volatile.Read(ref _disposeState) == 0;

    internal bool IsEnabled(LogLevel logLevel) => MinLevel != LogLevel.None && logLevel <= MinLevel;

    internal ILogScope BeginScope<TState>(TState state)
        where TState : notnull => _scopeProvider.Push(state);

    internal IReadOnlyList<object>? CaptureScopes() => _scopeProvider.Capture();

    internal int QueueCapacity => _options.QueueCapacity;

    internal LogQueueFullMode QueueFullMode => _options.QueueFullMode;

    internal TimeSpan SyncWriteTimeout => _options.SyncWriteTimeout;

    internal void ReportDroppedMessages(string categoryName, long droppedCount)
    {
        PicoLogMetrics.RecordDroppedEntry();
        _options.OnMessagesDropped?.Invoke(categoryName, droppedCount);

        if (_options.OnMessagesDropped is not null)
            return;

        if (droppedCount == 1 || (droppedCount & (droppedCount - 1)) == 0)
            Debug.WriteLine(
                $"Dropped {droppedCount} log entr{(droppedCount == 1 ? "y" : "ies")} for '{categoryName}'."
            );
    }

    internal void RecordEntryAccepted() => PicoLogMetrics.RecordEntryAccepted();

    internal void RecordSinkFailure() => PicoLogMetrics.RecordSinkFailure();

    internal void RecordRejectedAfterShutdown() => PicoLogMetrics.RecordRejectedAfterShutdown();

    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0)
            return;

        List<Exception>? exceptions = null;
        var drainStopwatch = Stopwatch.StartNew();

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
        PicoLogMetrics.RecordShutdownDrainDuration(drainStopwatch.Elapsed);

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
