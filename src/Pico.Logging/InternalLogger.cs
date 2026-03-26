namespace Pico.Logging;

internal sealed class InternalLogger : ILogger, IDisposable, IAsyncDisposable
{
    private readonly string _categoryName;
    private readonly AsyncLocal<ImmutableStack<object>> _scopes = new();
    private readonly Channel<LogEntry> _channel;
    private readonly Task _processingTask;
    private readonly ILogSink[] _sinksArray;
    private readonly LoggerFactory _factory;
    private readonly ILogSink? _fallbackSink;
    private readonly int _queueCapacity;
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
    }

    public IDisposable BeginScope<TState>(TState state)
        where TState : notnull
    {
        if (_disposeState != 0)
            return Scope.Empty;

        var stack = _scopes.Value ?? ImmutableStack<object>.Empty;
        _scopes.Value = stack.Push(state);
        return new Scope(
            () => _scopes.Value = _scopes.Value?.Pop() ?? ImmutableStack<object>.Empty
        );
    }

    public void Log(LogLevel logLevel, string message, Exception? exception)
    {
        if (_disposeState != 0 || !_factory.IsEnabled(logLevel))
            return;

        var entry = CreateEntry(logLevel, message, exception);

        if (!TryEnqueueSync(entry))
            ReportDroppedMessage();
    }

    public async Task LogAsync(
        LogLevel logLevel,
        string message,
        Exception? exception = null,
        CancellationToken cancellationToken = default
    )
    {
        if (_disposeState != 0 || !_factory.IsEnabled(logLevel))
            return;

        var entry = CreateEntry(logLevel, message, exception);

        try
        {
            await _channel.Writer.WriteAsync(entry, cancellationToken).ConfigureAwait(false);
            Interlocked.Increment(ref _queuedEntries);
        }
        catch (ChannelClosedException)
        {
            // Ignore writes that race with shutdown.
        }
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
                    await LogSinkErrorAsync(sink, ex, entry).ConfigureAwait(false);
                }
            }
        }
    }

    private LogEntry CreateEntry(LogLevel logLevel, string message, Exception? exception) =>
        new()
        {
            Timestamp = DateTimeOffset.Now,
            Level = logLevel,
            Category = _categoryName,
            Message = message,
            Exception = exception,
            Scopes = _scopes.Value?.Reverse().ToList()
        };

    private bool TryEnqueueSync(LogEntry entry) =>
        _factory.QueueFullMode switch
        {
            LogQueueFullMode.Wait => TryEnqueueSyncWithWait(entry),
            LogQueueFullMode.DropWrite => TryEnqueueSyncDropWrite(entry),
            _ => TryEnqueueSyncDropOldest(entry)
        };

    private bool TryEnqueueSyncDropOldest(LogEntry entry)
    {
        var wasAtCapacity = Volatile.Read(ref _queuedEntries) >= _queueCapacity;

        if (!_channel.Writer.TryWrite(entry))
            return false;

        if (wasAtCapacity)
        {
            ReportDroppedMessage();
            return true;
        }

        Interlocked.Increment(ref _queuedEntries);
        return true;
    }

    private bool TryEnqueueSyncDropWrite(LogEntry entry)
    {
        if (Volatile.Read(ref _queuedEntries) >= _queueCapacity)
            return false;

        if (!_channel.Writer.TryWrite(entry))
            return false;

        Interlocked.Increment(ref _queuedEntries);
        return true;
    }

    private bool TryEnqueueSyncWithWait(LogEntry entry)
    {
        try
        {
            if (!_channel.Writer.WaitToWriteAsync().AsTask().Wait(_factory.SyncWriteTimeout))
                return false;

            if (!_channel.Writer.TryWrite(entry))
                return false;

            Interlocked.Increment(ref _queuedEntries);
            return true;
        }
        catch (AggregateException ex) when (ex.InnerException is ChannelClosedException)
        {
            return false;
        }
        catch (ChannelClosedException)
        {
            return false;
        }
    }

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

    private sealed class Scope(Action? onDispose) : IDisposable
    {
        public static Scope Empty { get; } = new(null);

        public void Dispose() => onDispose?.Invoke();
    }

    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0)
            return;

        _channel.Writer.TryComplete();

        try
        {
            await _processingTask.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            Debug.WriteLine($"Logger shutdown timed out for category '{_categoryName}'.");
        }
    }
}
