namespace PicoLog;

internal sealed class InternalLogger : IStructuredLogger, IDisposable, IAsyncDisposable
{
    private readonly string _categoryName;
    private readonly Task _processingTask;
    private readonly LoggerFactory _factory;
    private readonly InternalLogSinkDispatcher _sinkDispatcher;
    private readonly InternalLoggerQueue _queue;
    private readonly int _queueDepthProviderId;
    private int _disposeState;
    private long _droppedEntries;

    public InternalLogger(string categoryName, IEnumerable<ILogSink> sinks, LoggerFactory factory)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _categoryName = categoryName;
        _sinkDispatcher = new InternalLogSinkDispatcher(
            sinks?.ToArray() ?? throw new ArgumentNullException(nameof(sinks)),
            _factory
        );
        _queue = new InternalLoggerQueue(_factory);
        _processingTask = Task.Run(async () => await _sinkDispatcher.ProcessEntriesAsync(_queue).ConfigureAwait(false));
        _queueDepthProviderId = PicoLogMetrics.RegisterQueueDepthProvider(_queue.GetQueuedEntryCount);
    }

    public IDisposable BeginScope<TState>(TState state)
        where TState : notnull
    {
        if (_disposeState != 0 || !_factory.IsAcceptingWrites)
            return LoggerScopeProvider.Empty;

        return _factory.BeginScope(state);
    }

    public void Log(LogLevel logLevel, string message, Exception? exception = null) =>
        Write(logLevel, message, properties: null, exception);

    public void LogStructured(
        LogLevel logLevel,
        string message,
        IReadOnlyList<KeyValuePair<string, object?>>? properties = null,
        Exception? exception = null
    ) => Write(logLevel, message, properties, exception);

    public Task LogAsync(
        LogLevel logLevel,
        string message,
        Exception? exception = null,
        CancellationToken cancellationToken = default
    ) => WriteAsync(logLevel, message, properties: null, exception, cancellationToken);

    public Task LogStructuredAsync(
        LogLevel logLevel,
        string message,
        IReadOnlyList<KeyValuePair<string, object?>>? properties = null,
        Exception? exception = null,
        CancellationToken cancellationToken = default
    ) => WriteAsync(logLevel, message, properties, exception, cancellationToken);

    private void Write(
        LogLevel logLevel,
        string message,
        IReadOnlyList<KeyValuePair<string, object?>>? properties,
        Exception? exception
    )
    {
        if (!CanAcceptWrite(logLevel))
            return;

        var entry = CreateEntry(logLevel, message, exception, properties);
        HandleWriteResult(_queue.TryEnqueueSync(entry));
    }

    private Task WriteAsync(
        LogLevel logLevel,
        string message,
        IReadOnlyList<KeyValuePair<string, object?>>? properties,
        Exception? exception,
        CancellationToken cancellationToken
    )
    {
        if (!CanAcceptWrite(logLevel))
            return Task.CompletedTask;

        var entry = CreateEntry(logLevel, message, exception, properties);

        var writeTask = _queue.TryEnqueueAsync(entry, cancellationToken);
        if (writeTask.IsCompletedSuccessfully)
        {
            HandleWriteResult(writeTask.Result);
            return Task.CompletedTask;
        }

        return AwaitWriteAsync(writeTask);

        async Task AwaitWriteAsync(ValueTask<LogWriteResult> pendingWrite)
        {
            HandleWriteResult(await pendingWrite.ConfigureAwait(false));
        }
    }

    private bool CanAcceptWrite(LogLevel logLevel)
    {
        if (_disposeState != 0 || !_factory.IsAcceptingWrites)
        {
            _factory.RecordRejectedAfterShutdown();
            return false;
        }

        return _factory.IsEnabled(logLevel);
    }

    private void HandleWriteResult(LogWriteResult result)
    {
        if (result == LogWriteResult.Dropped)
            ReportDroppedMessage();
        else if (result == LogWriteResult.RejectedAfterShutdown)
            _factory.RecordRejectedAfterShutdown();
    }

    private LogEntry CreateEntry(
        LogLevel logLevel,
        string message,
        Exception? exception,
        IReadOnlyList<KeyValuePair<string, object?>>? properties
    ) =>
        new()
        {
            Timestamp = GetTimestamp(),
            Level = logLevel,
            Category = _categoryName,
            Message = message,
            Exception = exception,
            Scopes = _factory.CaptureScopes(),
            Properties = SnapshotProperties(properties)
        };

    private static IReadOnlyList<KeyValuePair<string, object?>>? SnapshotProperties(
        IReadOnlyList<KeyValuePair<string, object?>>? properties
    )
    {
        if (properties is not { Count: > 0 })
            return null;

        if (properties is KeyValuePair<string, object?>[] array)
        {
            return array.Length switch
            {
                1 => [array[0]],
                2 => [array[0], array[1]],
                3 => [array[0], array[1], array[2]],
                4 => [array[0], array[1], array[2], array[3]],
                _ => array.ToArray()
            };
        }

        return properties.Count switch
        {
            1 => [properties[0]],
            2 => [properties[0], properties[1]],
            3 => [properties[0], properties[1], properties[2]],
            4 => [properties[0], properties[1], properties[2], properties[3]],
            _ => CopyProperties(properties)
        };
    }

    private static KeyValuePair<string, object?>[] CopyProperties(
        IReadOnlyList<KeyValuePair<string, object?>> properties
    )
    {
        var snapshot = new KeyValuePair<string, object?>[properties.Count];

        for (var index = 0; index < properties.Count; index++)
            snapshot[index] = properties[index];

        return snapshot;
    }

    private void ReportDroppedMessage()
    {
        var dropped = Interlocked.Increment(ref _droppedEntries);
        _factory.ReportDroppedMessages(_categoryName, dropped);
    }

    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0)
            return;

        try
        {
            _queue.Complete();
            await _processingTask.ConfigureAwait(false);
        }
        finally
        {
            PicoLogMetrics.UnregisterQueueDepthProvider(_queueDepthProviderId);
        }
    }

    private static DateTimeOffset GetTimestamp() => TimeProvider.System.GetLocalNow();
}
