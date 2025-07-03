namespace Pico.Logger;

public sealed class FileSink : ILogSink
{
    private readonly ILogFormatter _formatter;
    private readonly StreamWriter _writer;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private bool _disposed;

    public FileSink(ILogFormatter formatter)
    {
        _formatter = formatter;
        var filePath = "logs/test.log";

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
        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            var message = _formatter.Format(entry);
            await _writer.WriteLineAsync(message);
            await _writer.FlushAsync(cancellationToken);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _semaphore.Wait();
        try
        {
            _writer.Flush();
            _writer.Dispose();
        }
        finally
        {
            _disposed = true;
            _semaphore.Release();
            _semaphore.Dispose();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        await _semaphore.WaitAsync();
        try
        {
            await _writer.FlushAsync();
            await _writer.DisposeAsync();
        }
        finally
        {
            _disposed = true;
            _semaphore.Release();
            _semaphore.Dispose();
        }
    }

    ~FileSink() => Dispose();
}
