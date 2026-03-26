namespace Pico.Logging;

public sealed class FileSink : ILogSink
{
    private readonly Channel<string> _channel;
    private readonly ILogFormatter _formatter;
    private readonly FileSinkOptions _options;
    private readonly StreamWriter _writer;
    private readonly Task _processingTask;
    private int _disposeState;

    public FileSink(ILogFormatter formatter, string filePath = "logs/test.log")
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
        _processingTask = Task.Run(async () => await ProcessWritesAsync().ConfigureAwait(false));
    }

    public async Task WriteAsync(LogEntry entry, CancellationToken cancellationToken = default)
    {
        if (Volatile.Read(ref _disposeState) != 0)
            return;

        try
        {
            var message = _formatter.Format(entry);
            await _channel.Writer.WriteAsync(message, cancellationToken).ConfigureAwait(false);
        }
        catch (ChannelClosedException)
        {
            // Ignore writes that race with shutdown.
        }
    }

    private async ValueTask ProcessWritesAsync()
    {
        var batch = new List<string>(_options.BatchSize);

        await foreach (var message in _channel.Reader.ReadAllAsync())
        {
            batch.Add(message);
            await DrainBatchAsync(batch).ConfigureAwait(false);
        }
    }

    private async ValueTask DrainBatchAsync(List<string> batch)
    {
        if (_options.FlushInterval > TimeSpan.Zero)
        {
            while (batch.Count < _options.BatchSize)
            {
                using var cts = new CancellationTokenSource(_options.FlushInterval);

                try
                {
                    var message = await _channel.Reader.ReadAsync(cts.Token).ConfigureAwait(false);
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
            }
        }
        else
        {
            while (batch.Count < _options.BatchSize && _channel.Reader.TryRead(out var message))
                batch.Add(message);
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

        _channel.Writer.TryComplete();

        await _processingTask.ConfigureAwait(false);
        await _writer.FlushAsync().ConfigureAwait(false);
        await _writer.DisposeAsync().ConfigureAwait(false);

        GC.SuppressFinalize(this);
    }
}
