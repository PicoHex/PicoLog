namespace PicoLog;

internal sealed class CategoryPipeline : IDisposable, IAsyncDisposable
{
    private readonly string _categoryName;
    private readonly LoggerFactoryRuntime _runtime;
    private readonly InternalLogSinkDispatcher _sinkDispatcher;
    private readonly InternalLoggerQueue _queue;
    private readonly Task _processingTask;
    private readonly int _queueDepthProviderId;
    private readonly SemaphoreSlim _flushSemaphore = new(1, 1);
    private readonly FlushQuiesceCoordinator _flushQuiesceCoordinator = new();
    private int _disposeState;
    private int _activeDequeuedEntries;
    private int _activeDispatchOperations;
    private long _droppedEntries;

    public CategoryPipeline(string categoryName, LoggerFactoryRuntime runtime)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(categoryName);

        _categoryName = categoryName;
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _sinkDispatcher = new InternalLogSinkDispatcher(_runtime);
        _queue = new InternalLoggerQueue(_runtime);
        _processingTask = ProcessEntriesAsync();
        _queueDepthProviderId = PicoLogMetrics.RegisterQueueDepthProvider(
            _queue.GetQueuedEntryCount
        );
    }

    public void Write(LogEntry entry)
    {
        EnterWriteOperationSync();

        try
        {
            HandleWriteResult(_queue.TryEnqueueSync(entry));
        }
        finally
        {
            ExitWriteOperation();
        }
    }

    public async Task WriteAsync(LogEntry entry, CancellationToken cancellationToken)
    {
        await EnterWriteOperationAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var writeTask = _queue.TryEnqueueAsync(entry, cancellationToken);
            if (writeTask.IsCompletedSuccessfully)
            {
                HandleWriteResult(writeTask.Result);
                return;
            }

            HandleWriteResult(await writeTask.ConfigureAwait(false));
        }
        finally
        {
            ExitWriteOperation();
        }
    }

    public async ValueTask FlushAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeState) != 0, this);

        await _flushSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeState) != 0, this);

            await BlockWritesAsync(cancellationToken).ConfigureAwait(false);
            await WaitForIdleAsync(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
        }
        finally
        {
            ResumeWrites();
            _flushSemaphore.Release();
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

    private async Task ProcessEntriesAsync()
    {
        while (await _queue.WaitToReadAsync().ConfigureAwait(false))
        {
            while (_queue.TryRead(out var entry))
            {
                BeginDequeuedEntry();

                try
                {
                    BeginDispatch();

                    try
                    {
                        await _sinkDispatcher.DispatchEntryAsync(entry).ConfigureAwait(false);
                    }
                    finally
                    {
                        EndDispatch();
                    }
                }
                finally
                {
                    EndDequeuedEntry();
                }
            }
        }
    }

    private void EnterWriteOperationSync() => _flushQuiesceCoordinator.EnterWriteOperationSync();

    private ValueTask EnterWriteOperationAsync(CancellationToken cancellationToken) =>
        _flushQuiesceCoordinator.EnterWriteOperationAsync(cancellationToken);

    private void ExitWriteOperation() => _flushQuiesceCoordinator.ExitWriteOperation();

    private ValueTask BlockWritesAsync(CancellationToken cancellationToken) =>
        _flushQuiesceCoordinator.BlockWritesAsync(cancellationToken);

    private ValueTask WaitForIdleAsync(CancellationToken cancellationToken) =>
        _flushQuiesceCoordinator.WaitForIdleAsync(IsOwnerIdleUnderLock, cancellationToken);

    private void ResumeWrites() => _flushQuiesceCoordinator.ResumeWrites();

    private void BeginDispatch() =>
        _flushQuiesceCoordinator.BeginOwnerActivity(() => _activeDispatchOperations++);

    private void BeginDequeuedEntry() =>
        _flushQuiesceCoordinator.BeginOwnerActivity(() => _activeDequeuedEntries++);

    private void EndDequeuedEntry() =>
        _flushQuiesceCoordinator.EndOwnerActivity(
            () => _activeDequeuedEntries--,
            IsOwnerIdleUnderLock
        );

    private void EndDispatch() =>
        _flushQuiesceCoordinator.EndOwnerActivity(
            () => _activeDispatchOperations--,
            IsOwnerIdleUnderLock
        );

    private bool IsOwnerIdleUnderLock() =>
        _activeDequeuedEntries == 0
        && _activeDispatchOperations == 0
        && _queue.GetQueuedEntryCount() == 0;
}
