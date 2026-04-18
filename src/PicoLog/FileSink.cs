namespace PicoLog;

public sealed class FileSink : ILogSink, IFlushableLogSink
{
    private readonly Channel<string> _channel;
    private readonly ILogFormatter _formatter;
    private readonly FileSinkOptions _options;
    private readonly StreamWriter _writer;
    private readonly Task _processingTask;
    private readonly Lock _batchDelayStateLock = new();
    private readonly SemaphoreSlim _flushSemaphore = new(1, 1);
    private readonly FlushQuiesceCoordinator _flushQuiesceCoordinator = new();
    private int _disposeState;
    private int _queuedMessages;
    private int _activeDequeuedMessages;
    private int _activeBatchOperations;
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

    private ValueTask<bool> EnterWriteOperationAsync(CancellationToken cancellationToken)
        => _flushQuiesceCoordinator.TryEnterWriteOperationAsync(
            CanEnterWriteOperationUnderLock,
            cancellationToken
        );

    private void ExitWriteOperation()
        => _flushQuiesceCoordinator.ExitWriteOperation();

    private ValueTask BlockWritesAsync(CancellationToken cancellationToken)
        => _flushQuiesceCoordinator.BlockWritesAsync(cancellationToken);

    private ValueTask WaitForIdleAsync(CancellationToken cancellationToken)
        => _flushQuiesceCoordinator.WaitForIdleAsync(IsOwnerIdleUnderLock, cancellationToken);

    private void ResumeWrites()
        => _flushQuiesceCoordinator.ResumeWrites();

    private void BeginBatch()
        => _flushQuiesceCoordinator.BeginOwnerActivity(() => _activeBatchOperations++);

    private void BeginDequeuedMessage()
        => _flushQuiesceCoordinator.BeginOwnerActivity(() => _activeDequeuedMessages++);

    private void EndDequeuedMessage()
        => _flushQuiesceCoordinator.EndOwnerActivity(
            () => _activeDequeuedMessages--,
            IsOwnerIdleUnderLock
        );

    private void EndBatch()
        => _flushQuiesceCoordinator.EndOwnerActivity(
            () => _activeBatchOperations--,
            IsOwnerIdleUnderLock
        );

    private bool IsFlushPending() => _flushQuiesceCoordinator.IsFlushPending();

    private bool IsOwnerIdleUnderLock() =>
        _activeDequeuedMessages == 0
        && _activeBatchOperations == 0
        && Volatile.Read(ref _queuedMessages) == 0;

    private void RegisterBatchDelayCancellationSource(CancellationTokenSource source)
    {
        lock (_batchDelayStateLock)
            _batchDelayCancellationSource = source;
    }

    private void ClearBatchDelayCancellationSource(CancellationTokenSource source)
    {
        lock (_batchDelayStateLock)
        {
            if (ReferenceEquals(_batchDelayCancellationSource, source))
                _batchDelayCancellationSource = null;
        }
    }

    private void CancelBatchDelayWait()
    {
        CancellationTokenSource? batchDelayCancellationSource;

        lock (_batchDelayStateLock)
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

    private bool CanEnterWriteOperationUnderLock() => Volatile.Read(ref _disposeState) == 0;
}
