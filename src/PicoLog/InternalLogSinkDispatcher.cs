namespace PicoLog;

internal sealed class InternalLogSinkDispatcher
{
    private readonly ILogSink[] _sinks;
    private readonly LoggerFactoryRuntime _runtime;
    private readonly ILogSink? _consoleFallbackSink;

    public InternalLogSinkDispatcher(LoggerFactoryRuntime runtime)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _sinks = _runtime.Sinks;
        _consoleFallbackSink = ResolveLastRegisteredConsoleFallbackSink(_sinks);
    }

    internal async Task DispatchEntryAsync(LogEntry entry)
    {
        foreach (var sink in _sinks)
            await WriteToSinkAsync(sink, entry).ConfigureAwait(false);
    }

    private async Task WriteToSinkAsync(ILogSink sink, LogEntry entry)
    {
        try
        {
            await sink.WriteAsync(entry).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _runtime.RecordSinkFailure();
            await HandleSinkWriteFailureAsync(sink, entry, ex).ConfigureAwait(false);
        }
    }

    private async Task HandleSinkWriteFailureAsync(
        ILogSink failingSink,
        LogEntry originalEntry,
        Exception exception
    )
    {
        if (!ShouldWriteFailureToConsoleFallback(failingSink))
        {
            WriteSinkFailureToDebug(originalEntry, exception);
            return;
        }

        await WriteFailureToConsoleFallbackAsync(originalEntry, exception).ConfigureAwait(false);
    }

    private bool ShouldWriteFailureToConsoleFallback(ILogSink failingSink) =>
        _consoleFallbackSink is not null && !ReferenceEquals(_consoleFallbackSink, failingSink);

    private static ILogSink? ResolveLastRegisteredConsoleFallbackSink(ILogSink[] sinks) =>
        sinks.LastOrDefault(static sink => sink is IConsoleFallbackSink);

    private static void WriteSinkFailureToDebug(LogEntry originalEntry, Exception exception) =>
        Debug.WriteLine($"Sink write error for '{originalEntry.Category}': {exception}");

    private static void WriteConsoleFallbackFailureToDebug(Exception fallbackException) =>
        Debug.WriteLine($"Fallback sink write error: {fallbackException}");

    private async Task WriteFailureToConsoleFallbackAsync(LogEntry originalEntry, Exception exception)
    {
        var errorEntry = CreateFallbackErrorEntry(originalEntry, exception);

        try
        {
            await _consoleFallbackSink!.WriteAsync(errorEntry).ConfigureAwait(false);
        }
        catch (Exception fallbackException)
        {
            WriteConsoleFallbackFailureToDebug(fallbackException);
        }
    }

    private static LogEntry CreateFallbackErrorEntry(LogEntry originalEntry, Exception exception) =>
        new()
        {
            Timestamp = TimeProvider.System.GetLocalNow(),
            Level = LogLevel.Error,
            Category = nameof(InternalLogSinkDispatcher),
            Message = $"Failed to write log entry to sink: {originalEntry.Message}",
            Exception = exception
        };
}
