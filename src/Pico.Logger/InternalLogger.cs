namespace Pico.Logger;

internal sealed class InternalLogger : ILogger, IDisposable, IAsyncDisposable
{
    private readonly string _categoryName;
    private readonly AsyncLocal<ImmutableStack<object>> _scopes = new();
    private readonly Channel<LogEntry> _channel;
    private readonly Task _processingTask;
    private readonly ILogSink[] _sinksArray;
    private readonly LoggerFactory _factory;
    private readonly ILogSink _defaultSink;

    public InternalLogger(string categoryName, IEnumerable<ILogSink> sinks, LoggerFactory factory)
    {
        _sinksArray = sinks.ToArray() ?? throw new ArgumentNullException(nameof(sinks));
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _categoryName = categoryName;
        _channel = Channel.CreateBounded<LogEntry>(
            new BoundedChannelOptions(65535) { FullMode = BoundedChannelFullMode.DropOldest }
        );
        _processingTask = Task.Run(ProcessEntries);
        _defaultSink =
            _sinksArray.FirstOrDefault(p => p is ConsoleSink)
            ?? throw new InvalidOperationException("No ConsoleLogSink registered");
    }

    public IDisposable BeginScope<TState>(TState state)
        where TState : notnull
    {
        var stack = _scopes.Value ?? ImmutableStack<object>.Empty;
        _scopes.Value = stack.Push(state);
        return new Scope(
            () => _scopes.Value = _scopes.Value?.Pop() ?? ImmutableStack<object>.Empty
        );
    }

    public void Log(LogLevel logLevel, string message, Exception? exception)
    {
        if (!IsEnabled(logLevel))
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
        if (!IsEnabled(logLevel))
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

        await _channel.Writer.WriteAsync(entry, cancellationToken);
    }

    private bool IsEnabled(LogLevel logLevel) =>
        _factory.MinLevel != LogLevel.None && logLevel <= _factory.MinLevel;

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
                        await LogSinkErrorAsync(ex, entry).ConfigureAwait(false);
                    }
                }
            );
        }
    }

    private async ValueTask LogSinkErrorAsync(Exception ex, LogEntry originalEntry)
    {
        var errorEntry = new LogEntry
        {
            Timestamp = DateTimeOffset.Now,
            Level = LogLevel.Error,
            Category = nameof(InternalLogger),
            Message = $"Failed to write log entry to sink: {originalEntry.Message}",
            Exception = ex
        };

        await _defaultSink.WriteAsync(errorEntry).ConfigureAwait(false);
    }

    private class Scope(Action onDispose) : IDisposable
    {
        public void Dispose() => onDispose();
    }

    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    public async ValueTask DisposeAsync()
    {
        _channel.Writer.Complete();

        try
        {
            await _processingTask.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            // Log timeout if needed
        }

        foreach (var sink in _sinksArray)
        {
            await TryDisposeSinkAsync(sink).ConfigureAwait(false);
        }
    }

    private async ValueTask TryDisposeSinkAsync(ILogSink sink)
    {
        try
        {
            if (sink is IAsyncDisposable asyncDisposable)
                await asyncDisposable.DisposeAsync().ConfigureAwait(false);
            else if (sink is IDisposable disposable)
                disposable.Dispose();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Sink disposal error: {ex}");
        }
    }
}
