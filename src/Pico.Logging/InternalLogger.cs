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
    private int _disposeState;

    public InternalLogger(string categoryName, IEnumerable<ILogSink> sinks, LoggerFactory factory)
    {
        _sinksArray = sinks.ToArray() ?? throw new ArgumentNullException(nameof(sinks));
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _categoryName = categoryName;
        _channel = Channel.CreateBounded<LogEntry>(
            new BoundedChannelOptions(65535) { FullMode = BoundedChannelFullMode.DropOldest }
        );
        _processingTask = Task.Run(ProcessEntries);
        _fallbackSink = _sinksArray.FirstOrDefault(p => p is ConsoleSink);
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

        var entry = new LogEntry
        {
            Timestamp = DateTimeOffset.Now,
            Level = logLevel,
            Category = _categoryName,
            Message = message,
            Exception = exception,
            Scopes = _scopes.Value?.Reverse().ToList()
        };

        _channel.Writer.TryWrite(entry); // Fire and forget
    }

    public async ValueTask LogAsync(
        LogLevel logLevel,
        string message,
        Exception? exception = null,
        CancellationToken cancellationToken = default
    )
    {
        if (_disposeState != 0 || !_factory.IsEnabled(logLevel))
            return;

        var entry = new LogEntry
        {
            Timestamp = DateTimeOffset.Now,
            Level = logLevel,
            Category = _categoryName,
            Message = message,
            Exception = exception,
            Scopes = _scopes.Value?.Reverse().ToList()
        };

        try
        {
            await _channel.Writer.WriteAsync(entry, cancellationToken).ConfigureAwait(false);
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
            await Parallel.ForEachAsync(
                _sinksArray,
                async (sink, ct) =>
                {
                    try
                    {
                        await sink.WriteAsync(entry, ct).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        await LogSinkErrorAsync(sink, ex, entry).ConfigureAwait(false);
                    }
                }
            );
        }
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
