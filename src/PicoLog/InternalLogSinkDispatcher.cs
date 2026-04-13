namespace PicoLog;

internal sealed class InternalLogSinkDispatcher
{
    private readonly ILogSink[] _sinks;
    private readonly LoggerFactory _factory;
    private readonly ILogSink? _fallbackSink;

    public InternalLogSinkDispatcher(ILogSink[] sinks, LoggerFactory factory)
    {
        _sinks = sinks ?? throw new ArgumentNullException(nameof(sinks));
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _fallbackSink = _sinks.FirstOrDefault(static sink => sink is IConsoleFallbackSink);
    }

    public async Task ProcessEntriesAsync(InternalLoggerQueue queue)
    {
        while (await queue.WaitToReadAsync().ConfigureAwait(false))
        {
            while (queue.TryRead(out var entry))
                await DispatchEntryAsync(entry).ConfigureAwait(false);
        }
    }

    private async Task DispatchEntryAsync(LogEntry entry)
    {
        foreach (var sink in _sinks)
        {
            try
            {
                await sink.WriteAsync(entry).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _factory.RecordSinkFailure();
                await ReportSinkErrorAsync(sink, ex, entry).ConfigureAwait(false);
            }
        }
    }

    private async Task ReportSinkErrorAsync(ILogSink failingSink, Exception ex, LogEntry originalEntry)
    {
        if (_fallbackSink is null || ReferenceEquals(_fallbackSink, failingSink))
        {
            Debug.WriteLine($"Sink write error for '{originalEntry.Category}': {ex}");
            return;
        }

        var errorEntry = new LogEntry
        {
            Timestamp = TimeProvider.System.GetLocalNow(),
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
}
