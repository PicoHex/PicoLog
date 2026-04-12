namespace PicoLog;

internal enum LogWriteResult
{
    Accepted,
    Dropped,
    RejectedAfterShutdown
}

internal sealed class InternalLoggerQueue
{
    private readonly Channel<LogEntry> _channel;
    private readonly ChannelWriter<LogEntry> _writer;
    private readonly ChannelReader<LogEntry> _reader;
    private readonly LoggerFactory _factory;
    private readonly int _queueCapacity;
    private readonly LogQueueFullMode _queueFullMode;
    private readonly TimeSpan _syncWriteTimeout;
    private int _queuedEntries;
    private int _shutdownStarted;

    public InternalLoggerQueue(LoggerFactory factory)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _queueCapacity = _factory.QueueCapacity;
        _queueFullMode = _factory.QueueFullMode;
        _syncWriteTimeout = _factory.SyncWriteTimeout;
        _channel = Channel.CreateBounded<LogEntry>(
            new BoundedChannelOptions(_queueCapacity)
            {
                FullMode = _queueFullMode switch
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
        _writer = _channel.Writer;
        _reader = _channel.Reader;
    }

    public async Task<bool> WaitToReadAsync() => await _reader.WaitToReadAsync().ConfigureAwait(false);

    public bool TryRead([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out LogEntry? entry)
    {
        if (!_reader.TryRead(out entry))
        {
            entry = null;
            return false;
        }

        Interlocked.Decrement(ref _queuedEntries);
        return true;
    }

    public LogWriteResult TryEnqueueSync(LogEntry entry) =>
        _queueFullMode switch
        {
            LogQueueFullMode.Wait => TryEnqueueSyncWithWait(entry),
            LogQueueFullMode.DropWrite => TryEnqueueSyncDropWrite(entry),
            _ => TryEnqueueSyncDropOldest(entry)
        };

    public ValueTask<LogWriteResult> TryEnqueueAsync(LogEntry entry, CancellationToken cancellationToken) =>
        _queueFullMode switch
        {
            LogQueueFullMode.Wait => TryEnqueueAsyncWithWait(entry, cancellationToken),
            LogQueueFullMode.DropWrite => ValueTask.FromResult(TryEnqueueSyncDropWrite(entry)),
            _ => ValueTask.FromResult(TryEnqueueSyncDropOldest(entry))
        };

    public void Complete()
    {
        Volatile.Write(ref _shutdownStarted, 1);
        _writer.TryComplete();
    }

    public long GetQueuedEntryCount() => Volatile.Read(ref _queuedEntries);

    private LogWriteResult TryEnqueueSyncDropOldest(LogEntry entry)
    {
        var wasAtCapacity = Volatile.Read(ref _queuedEntries) >= _queueCapacity;

        if (!_writer.TryWrite(entry))
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

        if (!_writer.TryWrite(entry))
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
                if (_writer.TryWrite(entry))
                {
                    Interlocked.Increment(ref _queuedEntries);
                    _factory.RecordEntryAccepted();
                    return LogWriteResult.Accepted;
                }

                var remaining = _syncWriteTimeout - Stopwatch.GetElapsedTime(startTimestamp);

                if (remaining <= TimeSpan.Zero)
                    return LogWriteResult.Dropped;

                var waitOperation = _writer.WaitToWriteAsync();

                if (waitOperation.IsCompletedSuccessfully)
                {
                    if (!waitOperation.Result)
                        return DetermineFailedWriteResult();

                    continue;
                }

                var waitTask = waitOperation.AsTask();

                if (!waitTask.Wait(remaining))
                    return LogWriteResult.Dropped;

                if (!waitTask.GetAwaiter().GetResult())
                    return DetermineFailedWriteResult();

                if (!_writer.TryWrite(entry))
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

    private async ValueTask<LogWriteResult> TryEnqueueAsyncWithWait(
        LogEntry entry,
        CancellationToken cancellationToken
    )
    {
        try
        {
            while (true)
            {
                if (_writer.TryWrite(entry))
                {
                    Interlocked.Increment(ref _queuedEntries);
                    _factory.RecordEntryAccepted();
                    return LogWriteResult.Accepted;
                }

                if (!await _writer.WaitToWriteAsync(cancellationToken).ConfigureAwait(false))
                    return DetermineFailedWriteResult();

                if (!_writer.TryWrite(entry))
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
    }

    private LogWriteResult DetermineFailedWriteResult() =>
        Volatile.Read(ref _shutdownStarted) != 0 || !_factory.IsAcceptingWrites
            ? LogWriteResult.RejectedAfterShutdown
            : LogWriteResult.Dropped;
}
