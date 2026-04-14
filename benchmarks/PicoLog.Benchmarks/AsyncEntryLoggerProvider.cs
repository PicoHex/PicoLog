using System.Threading.Channels;
using PicoLog.Abs;

namespace PicoLog.Benchmarks;

internal sealed class AsyncEntryLoggerProvider : Microsoft.Extensions.Logging.ILoggerProvider, IDisposable
{
    private readonly Channel<LogEntry> _channel;
    private readonly Task _consumerTask;
    private int _disposed;

    public AsyncEntryLoggerProvider(int queueCapacity)
    {
        _channel = Channel.CreateBounded<LogEntry>(
            new BoundedChannelOptions(queueCapacity)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                AllowSynchronousContinuations = false
            }
        );
        _consumerTask = Task.Run(ConsumeAsync);
    }

    public Microsoft.Extensions.Logging.ILogger CreateLogger(string categoryName) =>
        new AsyncEntryLogger(categoryName, _channel);

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

    private sealed class AsyncEntryLogger(string categoryName, Channel<LogEntry> channel)
        : Microsoft.Extensions.Logging.ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

        public void Log<TState>(
            Microsoft.Extensions.Logging.LogLevel logLevel,
            Microsoft.Extensions.Logging.EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            var message = state as string ?? formatter(state, exception);
            channel.Writer.TryWrite(
                new LogEntry
                {
                    Timestamp = TimeProvider.System.GetLocalNow(),
                    Level = MapLogLevel(logLevel),
                    Category = categoryName,
                    Message = message,
                    Exception = exception,
                    Scopes = null,
                    Properties = null
                }
            );
        }

        private static PicoLog.Abs.LogLevel MapLogLevel(Microsoft.Extensions.Logging.LogLevel logLevel) =>
            logLevel switch
            {
                Microsoft.Extensions.Logging.LogLevel.Trace => PicoLog.Abs.LogLevel.Trace,
                Microsoft.Extensions.Logging.LogLevel.Debug => PicoLog.Abs.LogLevel.Debug,
                Microsoft.Extensions.Logging.LogLevel.Information => PicoLog.Abs.LogLevel.Info,
                Microsoft.Extensions.Logging.LogLevel.Warning => PicoLog.Abs.LogLevel.Warning,
                Microsoft.Extensions.Logging.LogLevel.Error => PicoLog.Abs.LogLevel.Error,
                Microsoft.Extensions.Logging.LogLevel.Critical => PicoLog.Abs.LogLevel.Critical,
                _ => PicoLog.Abs.LogLevel.Info
            };
    }
}
