namespace PicoLog;

public sealed class FileSink : ILogSink, IFlushableLogSink
{
    private readonly Channel<string> _channel;
    private readonly ILogFormatter _formatter;
    private readonly FileSinkOptions _options;
    private readonly StreamWriter _writer;
    private readonly Task _processingTask;
    private readonly Lock _stateLock = new();
    private readonly SemaphoreSlim _flushSemaphore = new(1, 1);
    private int _disposeState;
    private int _flushPending;
    private int _queuedMessages;
    private int _activeWriteOperations;
    private int _activeDequeuedMessages;
    private int _activeBatchOperations;
    private TaskCompletionSource? _writesResumedTcs;
    private TaskCompletionSource? _writesQuiescedTcs;
    private TaskCompletionSource? _idleTcs;
    private CancellationTokenSource? _batchDelayCancellationSource;

    public FileSink(ILogFormatter formatter, string filePath = FileSinkOptions.DefaultFilePath)
        : this(formatter, new FileSinkOptions { FilePath = filePath }) { }

    public FileSink(ILogFormatter formatter, FileSinkOptions options)
    {
        _formatter = formatter ?? throw new ArgumentNullException(nameof(formatter));
        _options = (
            options ?? throw new ArgumentNullException(nameof(options))
        ).CreateValidatedCopy();

        var fullPath = Path.GetFullPath(_options.FilePath);
        var directory = Path.GetDirectoryName(fullPath)!;

        if (!Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var fileStream = new FileStream(
            fullPath,
            FileMode.OpenOrCreate,
            FileAccess.Write,
            FileShare.Read,
            bufferSize: 4096,
            useAsync: true
        );

        fileStream.Seek(0, SeekOrigin.End);

        _writer = new StreamWriter(fileStream, Encoding.UTF8);
        _channel = Channel.CreateBounded<string>(
            new BoundedChannelOptions(_options.QueueCapacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                AllowSynchronousContinuations = false
            }
        );
        _processingTask = ProcessWritesAsync().AsTask();
    }

    public async Task WriteAsync(LogEntry entry, CancellationToken cancellationToken = default)
    {
        if (!await EnterWriteOperationAsync(cancellationToken).ConfigureAwait(false))
            return;

        try
        {
            if (Volatile.Read(ref _disposeState) != 0)
                return;

            var message = _formatter.Format(entry);
            await _channel.Writer.WriteAsync(message, cancellationToken).ConfigureAwait(false);
            Interlocked.Increment(ref _queuedMessages);
        }
        catch (ChannelClosedException)
        {
            // Ignore writes that race with shutdown.
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
            CancelBatchDelayWait();
            await WaitForIdleAsync(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            await _writer.FlushAsync().ConfigureAwait(false);
        }
        finally
        {
            ResumeWrites();
            _flushSemaphore.Release();
        }
    }

    private async ValueTask ProcessWritesAsync()
    {
        var batch = new List<string>(_options.BatchSize);

        await foreach (var message in _channel.Reader.ReadAllAsync())
        {
            Interlocked.Decrement(ref _queuedMessages);
            BeginDequeuedMessage();
            batch.Add(message);

            try
            {
                BeginBatch();

                try
                {
                await DrainBatchAsync(batch).ConfigureAwait(false);
                }
                finally
                {
                    EndBatch();
                }
            }
            finally
            {
                EndDequeuedMessage();
            }
        }
    }

    private async ValueTask DrainBatchAsync(List<string> batch)
    {
        if (_options.FlushInterval > TimeSpan.Zero)
        {
            while (batch.Count < _options.BatchSize)
            {
                if (IsFlushPending())
                    break;

                using var cts = new CancellationTokenSource(_options.FlushInterval);
                RegisterBatchDelayCancellationSource(cts);

                try
                {
                    var message = await _channel.Reader.ReadAsync(cts.Token).ConfigureAwait(false);
                    Interlocked.Decrement(ref _queuedMessages);
                    batch.Add(message);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (ChannelClosedException)
                {
                    break;
                }
                finally
                {
                    ClearBatchDelayCancellationSource(cts);
                }
            }
        }
        else
        {
            while (batch.Count < _options.BatchSize && _channel.Reader.TryRead(out var message))
            {
                Interlocked.Decrement(ref _queuedMessages);
                batch.Add(message);
            }
        }

        foreach (var message in batch)
            await _writer.WriteLineAsync(message).ConfigureAwait(false);

        await _writer.FlushAsync().ConfigureAwait(false);
        batch.Clear();
    }

    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0)
            return;

        CancelBatchDelayWait();
        _channel.Writer.TryComplete();

        await _processingTask.ConfigureAwait(false);
        await _writer.FlushAsync().ConfigureAwait(false);
        await _writer.DisposeAsync().ConfigureAwait(false);

        GC.SuppressFinalize(this);
    }

    private async ValueTask<bool> EnterWriteOperationAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            Task waitTask;

            lock (_stateLock)
            {
                if (Volatile.Read(ref _disposeState) != 0)
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

    private void BeginBatch()
    {
        lock (_stateLock)
            _activeBatchOperations++;
    }

    private void BeginDequeuedMessage()
    {
        lock (_stateLock)
            _activeDequeuedMessages++;
    }

    private void EndDequeuedMessage()
    {
        TaskCompletionSource? idleSignal = null;

        lock (_stateLock)
        {
            _activeDequeuedMessages--;

            if (IsIdleUnderLock())
                idleSignal = _idleTcs;
        }

        idleSignal?.TrySetResult();
    }

    private void EndBatch()
    {
        TaskCompletionSource? idleSignal = null;

        lock (_stateLock)
        {
            _activeBatchOperations--;

            if (IsIdleUnderLock())
                idleSignal = _idleTcs;
        }

        idleSignal?.TrySetResult();
    }

    private bool IsFlushPending()
    {
        lock (_stateLock)
            return _flushPending != 0;
    }

    private bool IsIdleUnderLock() =>
        _flushPending != 0
        && _activeWriteOperations == 0
        && _activeDequeuedMessages == 0
        && _activeBatchOperations == 0
        && Volatile.Read(ref _queuedMessages) == 0;

    private void RegisterBatchDelayCancellationSource(CancellationTokenSource source)
    {
        lock (_stateLock)
            _batchDelayCancellationSource = source;
    }

    private void ClearBatchDelayCancellationSource(CancellationTokenSource source)
    {
        lock (_stateLock)
        {
            if (ReferenceEquals(_batchDelayCancellationSource, source))
                _batchDelayCancellationSource = null;
        }
    }

    private void CancelBatchDelayWait()
    {
        CancellationTokenSource? batchDelayCancellationSource;

        lock (_stateLock)
            batchDelayCancellationSource = _batchDelayCancellationSource;

        if (batchDelayCancellationSource is null)
            return;

        try
        {
            batchDelayCancellationSource.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Ignore cancellation races with completed batch-delay waits.
        }
    }

    private static TaskCompletionSource CreateSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}
