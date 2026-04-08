using System.Threading.Channels;

namespace PicoLog.Benchmarks;

internal sealed class AsyncNullLoggerProvider : Microsoft.Extensions.Logging.ILoggerProvider, IDisposable
{
    private readonly Channel<string> _channel;
    private readonly Task _consumerTask;
    private int _disposed;

    public AsyncNullLoggerProvider(int queueCapacity)
    {
        _channel = Channel.CreateBounded<string>(
            new BoundedChannelOptions(queueCapacity)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                AllowSynchronousContinuations = false
            }
        );
        _consumerTask = Task.Run(ConsumeAsync);
    }

    public Microsoft.Extensions.Logging.ILogger CreateLogger(string categoryName) => new AsyncNullLogger(_channel);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _channel.Writer.TryComplete();
        _consumerTask.GetAwaiter().GetResult();
    }

    private async Task ConsumeAsync()
    {
        await foreach (var _ in _channel.Reader.ReadAllAsync())
        {
        }
    }

    private sealed class AsyncNullLogger(Channel<string> channel) : Microsoft.Extensions.Logging.ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

        public void Log<TState>(
            Microsoft.Extensions.Logging.LogLevel logLevel,
            Microsoft.Extensions.Logging.EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (exception is not null)
                _ = formatter(state, exception);

            var message = state as string ?? formatter(state, exception);
            channel.Writer.TryWrite(message);
        }
    }
}
