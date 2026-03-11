namespace Pico.Logging;

public sealed class FileSink : ILogSink
{
    private readonly ILogFormatter _formatter;
    private readonly StreamWriter _writer;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private int _disposeState;

    public FileSink(ILogFormatter formatter, string filePath = "logs/test.log")
    {
        _formatter = formatter ?? throw new ArgumentNullException(nameof(formatter));
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var fullPath = Path.GetFullPath(filePath);
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
    }

    public async ValueTask WriteAsync(LogEntry entry, CancellationToken cancellationToken = default)
    {
        if (Volatile.Read(ref _disposeState) != 0)
            return;

        var lockTaken = false;

        try
        {
            await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            lockTaken = true;

            if (Volatile.Read(ref _disposeState) != 0)
                return;

            var message = _formatter.Format(entry);
            await _writer.WriteLineAsync(message).ConfigureAwait(false);
            await _writer.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (lockTaken)
                _semaphore.Release();
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0)
            return;

        _semaphore.Wait();
        try
        {
            _writer.Flush();
            _writer.Dispose();
        }
        finally
        {
            _semaphore.Release();
        }

        GC.SuppressFinalize(this);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0)
            return;

        await _semaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            await _writer.FlushAsync().ConfigureAwait(false);
            await _writer.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            _semaphore.Release();
        }

        GC.SuppressFinalize(this);
    }
}
