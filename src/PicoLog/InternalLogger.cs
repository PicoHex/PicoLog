namespace PicoLog;

internal sealed class InternalLogger : IStructuredLogger, IDisposable, IAsyncDisposable
{
    private enum LogWriteResult
    {
        Accepted,
        Dropped,
        RejectedAfterShutdown
    }

    private readonly string _categoryName;
    private readonly Channel<LogEntry> _channel;
    private readonly Task _processingTask;
    private readonly ILogSink[] _sinksArray;
    private readonly LoggerFactory _factory;
    private readonly ILogSink? _fallbackSink;
    private readonly int _queueCapacity;
    private readonly int _queueDepthProviderId;
    private int _disposeState;
    private int _queuedEntries;
    private long _droppedEntries;

    public InternalLogger(string categoryName, IEnumerable<ILogSink> sinks, LoggerFactory factory)
    {
        _sinksArray = sinks.ToArray() ?? throw new ArgumentNullException(nameof(sinks));
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _categoryName = categoryName;
        _queueCapacity = _factory.QueueCapacity;
        _channel = Channel.CreateBounded<LogEntry>(
            new BoundedChannelOptions(_queueCapacity)
            {
                FullMode = _factory.QueueFullMode switch
                {
                    LogQueueFullMode.DropOldest => BoundedChannelFullMode.DropOldest,
                    LogQueueFullMode.DropWrite => BoundedChannelFullMode.DropWrite,
                    LogQueueFullMode.Wait => BoundedChannelFullMode.Wait,
                    _ => BoundedChannelFullMode.DropOldest
                },
                SingleReader = true,
                AllowSynchronousContinuations = false
            }
        );
        _processingTask = Task.Run(async () => await ProcessEntries().ConfigureAwait(false));
        _fallbackSink = _sinksArray.FirstOrDefault(p => p is ConsoleSink or ColoredConsoleSink);
        _queueDepthProviderId = PicoLogMetrics.RegisterQueueDepthProvider(GetQueuedEntryCount);
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
        HandleWriteResult(TryEnqueueSync(entry));
    }

    private async Task WriteAsync(
        LogLevel logLevel,
        string message,
        IReadOnlyList<KeyValuePair<string, object?>>? properties,
        Exception? exception,
        CancellationToken cancellationToken
    )
    {
        if (!CanAcceptWrite(logLevel))
            return;

        var entry = CreateEntry(logLevel, message, exception, properties);
        HandleWriteResult(await TryEnqueueAsync(entry, cancellationToken).ConfigureAwait(false));
    }

    private async ValueTask ProcessEntries()
    {
        await foreach (var entry in _channel.Reader.ReadAllAsync())
        {
            Interlocked.Decrement(ref _queuedEntries);

            foreach (var sink in _sinksArray)
            {
                try
                {
                    await sink.WriteAsync(entry).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _factory.RecordSinkFailure();
                    await LogSinkErrorAsync(sink, ex, entry).ConfigureAwait(false);
                }
            }
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
            Timestamp = DateTimeOffset.Now,
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

        var snapshot = new KeyValuePair<string, object?>[properties.Count];

        for (var index = 0; index < properties.Count; index++)
            snapshot[index] = properties[index];

        return snapshot;
    }

    private LogWriteResult TryEnqueueSync(LogEntry entry) =>
        _factory.QueueFullMode switch
        {
            LogQueueFullMode.Wait => TryEnqueueSyncWithWait(entry),
            LogQueueFullMode.DropWrite => TryEnqueueSyncDropWrite(entry),
            _ => TryEnqueueSyncDropOldest(entry)
        };

    private Task<LogWriteResult> TryEnqueueAsync(LogEntry entry, CancellationToken cancellationToken) =>
        _factory.QueueFullMode switch
        {
            LogQueueFullMode.Wait => TryEnqueueAsyncWithWait(entry, cancellationToken),
            LogQueueFullMode.DropWrite => Task.FromResult(TryEnqueueSyncDropWrite(entry)),
            _ => Task.FromResult(TryEnqueueSyncDropOldest(entry))
        };

    private LogWriteResult TryEnqueueSyncDropOldest(LogEntry entry)
    {
        var wasAtCapacity = Volatile.Read(ref _queuedEntries) >= _queueCapacity;

        if (!_channel.Writer.TryWrite(entry))
            return DetermineFailedWriteResult();

        _factory.RecordEntryAccepted();

        if (wasAtCapacity)
            return LogWriteResult.Dropped;

        Interlocked.Increment(ref _queuedEntries);
        return LogWriteResult.Accepted;
    }

    private LogWriteResult TryEnqueueSyncDropWrite(LogEntry entry)
    {
        if (Volatile.Read(ref _queuedEntries) >= _queueCapacity)
            return LogWriteResult.Dropped;

        if (!_channel.Writer.TryWrite(entry))
            return DetermineFailedWriteResult();

        Interlocked.Increment(ref _queuedEntries);
        _factory.RecordEntryAccepted();
        return LogWriteResult.Accepted;
    }

    private LogWriteResult TryEnqueueSyncWithWait(LogEntry entry)
    {
        var startTimestamp = Stopwatch.GetTimestamp();

        try
        {
            while (true)
            {
                var remaining = _factory.SyncWriteTimeout - Stopwatch.GetElapsedTime(startTimestamp);

                if (remaining <= TimeSpan.Zero)
                    return LogWriteResult.Dropped;

                var waitTask = _channel.Writer.WaitToWriteAsync().AsTask();

                if (!waitTask.Wait(remaining))
                    return LogWriteResult.Dropped;

                if (!waitTask.GetAwaiter().GetResult())
                    return DetermineFailedWriteResult();

                if (!_channel.Writer.TryWrite(entry))
                    continue;

                Interlocked.Increment(ref _queuedEntries);
                _factory.RecordEntryAccepted();
                return LogWriteResult.Accepted;
            }
        }
        catch (AggregateException ex) when (ex.InnerException is ChannelClosedException)
        {
            return DetermineFailedWriteResult();
        }
        catch (ChannelClosedException)
        {
            return DetermineFailedWriteResult();
        }
    }

    private async Task<LogWriteResult> TryEnqueueAsyncWithWait(
        LogEntry entry,
        CancellationToken cancellationToken
    )
    {
        try
        {
            while (await _channel.Writer.WaitToWriteAsync(cancellationToken).ConfigureAwait(false))
            {
                if (!_channel.Writer.TryWrite(entry))
                    continue;

                Interlocked.Increment(ref _queuedEntries);
                _factory.RecordEntryAccepted();
                return LogWriteResult.Accepted;
            }
        }
        catch (ChannelClosedException)
        {
            return DetermineFailedWriteResult();
        }

        return DetermineFailedWriteResult();
    }

    private LogWriteResult DetermineFailedWriteResult() =>
        _disposeState != 0 || !_factory.IsAcceptingWrites
            ? LogWriteResult.RejectedAfterShutdown
            : LogWriteResult.Dropped;

    private void ReportDroppedMessage()
    {
        var dropped = Interlocked.Increment(ref _droppedEntries);
        _factory.ReportDroppedMessages(_categoryName, dropped);
    }

    private async ValueTask LogSinkErrorAsync(
        ILogSink failingSink,
        Exception ex,
        LogEntry originalEntry
    )
    {
        if (_fallbackSink is null || ReferenceEquals(_fallbackSink, failingSink))
        {
            Debug.WriteLine($"Sink write error for '{originalEntry.Category}': {ex}");
            return;
        }

        var errorEntry = new LogEntry
        {
            Timestamp = DateTimeOffset.Now,
            Level = LogLevel.Error,
            Category = nameof(InternalLogger),
            Message = $"Failed to write log entry to sink: {originalEntry.Message}",
            Exception = ex
        };

        try
        {
            await _fallbackSink.WriteAsync(errorEntry).ConfigureAwait(false);
        }
        catch (Exception fallbackException)
        {
            Debug.WriteLine($"Fallback sink write error: {fallbackException}");
        }
    }

    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0)
            return;

        try
        {
            _channel.Writer.TryComplete();
            await _processingTask.ConfigureAwait(false);
        }
        finally
        {
            PicoLogMetrics.UnregisterQueueDepthProvider(_queueDepthProviderId);
        }
    }

    private long GetQueuedEntryCount() =>
        _channel.Reader.CanCount ? _channel.Reader.Count : Volatile.Read(ref _queuedEntries);
}
