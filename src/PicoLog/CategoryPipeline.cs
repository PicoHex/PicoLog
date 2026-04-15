namespace PicoLog;

internal sealed class CategoryPipeline : IDisposable, IAsyncDisposable
{
    private readonly string _categoryName;
    private readonly LoggerFactoryRuntime _runtime;
    private readonly InternalLogSinkDispatcher _sinkDispatcher;
    private readonly InternalLoggerQueue _queue;
    private readonly Task _processingTask;
    private readonly int _queueDepthProviderId;
    private readonly Lock _stateLock = new();
    private readonly SemaphoreSlim _flushSemaphore = new(1, 1);
    private int _disposeState;
    private int _flushPending;
    private int _activeWriteOperations;
    private int _activeDequeuedEntries;
    private int _activeDispatchOperations;
    private long _droppedEntries;
    private TaskCompletionSource? _writesResumedTcs;
    private TaskCompletionSource? _writesQuiescedTcs;
    private TaskCompletionSource? _idleTcs;

    public CategoryPipeline(string categoryName, LoggerFactoryRuntime runtime)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(categoryName);

        _categoryName = categoryName;
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _sinkDispatcher = new InternalLogSinkDispatcher(_runtime);
        _queue = new InternalLoggerQueue(_runtime);
        _processingTask = ProcessEntriesAsync();
        _queueDepthProviderId = PicoLogMetrics.RegisterQueueDepthProvider(_queue.GetQueuedEntryCount);
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

    private void EnterWriteOperationSync()
    {
        while (true)
        {
            Task waitTask;

            lock (_stateLock)
            {
                if (_flushPending == 0)
                {
                    _activeWriteOperations++;
                    return;
                }

                waitTask = (_writesResumedTcs ??= CreateSignal()).Task;
            }

            waitTask.GetAwaiter().GetResult();
        }
    }

    private async ValueTask EnterWriteOperationAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            Task waitTask;

            lock (_stateLock)
            {
                if (_flushPending == 0)
                {
                    _activeWriteOperations++;
                    return;
                }

                waitTask = (_writesResumedTcs ??= CreateSignal()).Task;
            }

            await waitTask.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private void ExitWriteOperation()
    {
        TaskCompletionSource? writesQuiesced = null;

        lock (_stateLock)
        {
            _activeWriteOperations--;

            if (_activeWriteOperations == 0 && _flushPending != 0)
                writesQuiesced = _writesQuiescedTcs;
        }

        writesQuiesced?.TrySetResult();
    }

    private async ValueTask BlockWritesAsync(CancellationToken cancellationToken)
    {
        Task? waitTask = null;

        lock (_stateLock)
        {
            _flushPending = 1;

            if (_activeWriteOperations != 0)
                waitTask = (_writesQuiescedTcs ??= CreateSignal()).Task;
        }

        if (waitTask is not null)
            await waitTask.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask WaitForIdleAsync(CancellationToken cancellationToken)
    {
        Task? waitTask = null;

        lock (_stateLock)
        {
            if (!IsIdleUnderLock())
                waitTask = (_idleTcs ??= CreateSignal()).Task;
        }

        if (waitTask is not null)
            await waitTask.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private void ResumeWrites()
    {
        TaskCompletionSource? writesResumed;

        lock (_stateLock)
        {
            _flushPending = 0;
            _writesQuiescedTcs = null;
            _idleTcs = null;
            writesResumed = _writesResumedTcs;
            _writesResumedTcs = null;
        }

        writesResumed?.TrySetResult();
    }

    private void BeginDispatch()
    {
        lock (_stateLock)
            _activeDispatchOperations++;
    }

    private void BeginDequeuedEntry()
    {
        lock (_stateLock)
            _activeDequeuedEntries++;
    }

    private void EndDequeuedEntry()
    {
        TaskCompletionSource? idleSignal = null;

        lock (_stateLock)
        {
            _activeDequeuedEntries--;

            if (IsIdleUnderLock())
                idleSignal = _idleTcs;
        }

        idleSignal?.TrySetResult();
    }

    private void EndDispatch()
    {
        TaskCompletionSource? idleSignal = null;

        lock (_stateLock)
        {
            _activeDispatchOperations--;

            if (IsIdleUnderLock())
                idleSignal = _idleTcs;
        }

        idleSignal?.TrySetResult();
    }

    private bool IsIdleUnderLock() =>
        _flushPending != 0
        && _activeWriteOperations == 0
        && _activeDequeuedEntries == 0
        && _activeDispatchOperations == 0
        && _queue.GetQueuedEntryCount() == 0;

    private static TaskCompletionSource CreateSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}
