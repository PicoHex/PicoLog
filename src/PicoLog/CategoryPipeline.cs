namespace PicoLog;

internal sealed class CategoryPipeline : IDisposable, IAsyncDisposable
{
    private readonly string _categoryName;
    private readonly LoggerFactoryRuntime _runtime;
    private readonly InternalLogSinkDispatcher _sinkDispatcher;
    private readonly InternalLoggerQueue _queue;
    private readonly Task _processingTask;
    private readonly int _queueDepthProviderId;
    private int _disposeState;
    private long _droppedEntries;

    public CategoryPipeline(string categoryName, LoggerFactoryRuntime runtime)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(categoryName);

        _categoryName = categoryName;
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _sinkDispatcher = new InternalLogSinkDispatcher(_runtime);
        _queue = new InternalLoggerQueue(_runtime);
        _processingTask = _sinkDispatcher.ProcessEntriesAsync(_queue);
        _queueDepthProviderId = PicoLogMetrics.RegisterQueueDepthProvider(_queue.GetQueuedEntryCount);
    }

    public void Write(LogEntry entry) => HandleWriteResult(_queue.TryEnqueueSync(entry));

    public Task WriteAsync(LogEntry entry, CancellationToken cancellationToken)
    {
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

    private void HandleWriteResult(LogWriteResult result)
    {
        if (result is LogWriteResult.AcceptedAfterEviction or LogWriteResult.DroppedNewWrite)
            ReportDroppedMessage();
        else if (result == LogWriteResult.RejectedAfterShutdown)
            _runtime.RecordRejectedAfterShutdown();
    }

    private void ReportDroppedMessage()
    {
        var dropped = Interlocked.Increment(ref _droppedEntries);
        _runtime.ReportDroppedMessages(_categoryName, dropped);
    }
}
