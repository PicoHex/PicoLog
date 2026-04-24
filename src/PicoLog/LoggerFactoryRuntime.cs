namespace PicoLog;

internal sealed class LoggerFactoryRuntime
{
    private readonly LoggerScopeProvider _scopeProvider = new();
    private readonly LoggerFactoryOptions _options;
    private int _acceptingWrites = 1;
    private int _minLevel;

    public LoggerFactoryRuntime(ILogSink[] sinks, LoggerFactoryOptions? options)
    {
        ArgumentNullException.ThrowIfNull(sinks);
        Sinks = new ILogSink[sinks.Length];

        for (var index = 0; index < sinks.Length; index++)
        {
            Sinks[index] = sinks[index] switch
            {
                IFlushableLogSink flushable when flushable is not IConsoleFallbackSink
                    => new SinkFlushWrapper(flushable),
                var other => other
            };
        }

        _options = (options ?? new LoggerFactoryOptions()).CreateValidatedCopy();
        _minLevel = (int)_options.MinLevel;
    }

    public ILogSink[] Sinks { get; }

    public LogLevel MinLevel
    {
        get => (LogLevel)Volatile.Read(ref _minLevel);
        set => Volatile.Write(ref _minLevel, (int)value);
    }

    public bool IsAcceptingWrites => Volatile.Read(ref _acceptingWrites) != 0;

    public int QueueCapacity => _options.QueueCapacity;

    public LogQueueFullMode QueueFullMode => _options.QueueFullMode;

    public TimeSpan SyncWriteTimeout => _options.SyncWriteTimeout;

    public bool TryBeginShutdown() => Interlocked.Exchange(ref _acceptingWrites, 0) != 0;

    public bool IsEnabled(LogLevel logLevel) => MinLevel != LogLevel.None && logLevel <= MinLevel;

    public ILogScope BeginScope<TState>(TState state)
        where TState : notnull => _scopeProvider.Push(state);

    public IReadOnlyList<object>? CaptureScopes() => _scopeProvider.Capture();

    public void ReportDroppedMessages(string categoryName, long droppedCount)
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

    public void RecordEntryAccepted() => PicoLogMetrics.RecordEntryAccepted();

    public void RecordSinkFailure() => PicoLogMetrics.RecordSinkFailure();

    public void RecordRejectedAfterShutdown() => PicoLogMetrics.RecordRejectedAfterShutdown();
}
