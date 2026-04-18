namespace PicoLog;

internal sealed class FlushQuiesceCoordinator
{
    private readonly Lock _stateLock = new();
    private int _flushPending;
    private int _activeWriteOperations;
    private TaskCompletionSource? _writesResumedTcs;
    private TaskCompletionSource? _writesQuiescedTcs;
    private TaskCompletionSource? _idleTcs;

    public void EnterWriteOperationSync()
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

    public async ValueTask EnterWriteOperationAsync(CancellationToken cancellationToken)
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

    public async ValueTask<bool> TryEnterWriteOperationAsync(
        Func<bool> canEnterWriteUnderLock,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(canEnterWriteUnderLock);

        while (true)
        {
            Task waitTask;

            lock (_stateLock)
            {
                if (!canEnterWriteUnderLock())
                    return false;

                if (_flushPending == 0)
                {
                    _activeWriteOperations++;
                    return true;
                }

                waitTask = (_writesResumedTcs ??= CreateSignal()).Task;
            }

            await waitTask.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public void ExitWriteOperation()
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

    public async ValueTask BlockWritesAsync(CancellationToken cancellationToken)
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

    public async ValueTask WaitForIdleAsync(
        Func<bool> isOwnerIdleUnderLock,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(isOwnerIdleUnderLock);

        Task? waitTask = null;

        lock (_stateLock)
        {
            if (!IsIdleUnderLock(isOwnerIdleUnderLock))
                waitTask = (_idleTcs ??= CreateSignal()).Task;
        }

        if (waitTask is not null)
            await waitTask.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public void ResumeWrites()
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

    public bool IsFlushPending()
    {
        lock (_stateLock)
            return _flushPending != 0;
    }

    public void BeginOwnerActivity(Action beginOwnerActivityUnderLock)
    {
        ArgumentNullException.ThrowIfNull(beginOwnerActivityUnderLock);

        lock (_stateLock)
            beginOwnerActivityUnderLock();
    }

    public void EndOwnerActivity(
        Action endOwnerActivityUnderLock,
        Func<bool> isOwnerIdleUnderLock
    )
    {
        ArgumentNullException.ThrowIfNull(endOwnerActivityUnderLock);
        ArgumentNullException.ThrowIfNull(isOwnerIdleUnderLock);

        TaskCompletionSource? idleSignal = null;

        lock (_stateLock)
        {
            endOwnerActivityUnderLock();

            if (IsIdleUnderLock(isOwnerIdleUnderLock))
                idleSignal = _idleTcs;
        }

        idleSignal?.TrySetResult();
    }

    private bool IsIdleUnderLock(Func<bool> isOwnerIdleUnderLock) =>
        _flushPending != 0 && _activeWriteOperations == 0 && isOwnerIdleUnderLock();

    private static TaskCompletionSource CreateSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}
