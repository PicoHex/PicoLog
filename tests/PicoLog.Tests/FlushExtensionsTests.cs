namespace PicoLog.Tests;

public sealed class FlushExtensionsTests
{
    [Test]
    public async Task LoggerFactoryFlushExtension_Completes_WhenFactoryDoesNotSupportFlush()
    {
        ILoggerFactory factory = new PlainLoggerFactory();

        await factory.FlushAsync();
        await factory.DisposeAsync();
    }

    [Test]
    public async Task LoggerFactoryFlushExtension_ForwardsToFlushableFactory()
    {
        using var cancellationSource = new CancellationTokenSource();
        ILoggerFactory factory = new RecordingFlushableLoggerFactory();

        await factory.FlushAsync(cancellationSource.Token);

        var flushableFactory = (RecordingFlushableLoggerFactory)factory;
        await Assert.That(flushableFactory.FlushCallCount).IsEqualTo(1);
        await Assert.That(flushableFactory.CancellationToken == cancellationSource.Token).IsTrue();
        await factory.DisposeAsync();
    }

    [Test]
    public async Task LogSinkFlushExtension_Completes_WhenSinkDoesNotSupportFlush()
    {
        ILogSink sink = new PlainSink();

        await sink.FlushAsync();
        await sink.DisposeAsync();
    }

    [Test]
    public async Task LogSinkFlushExtension_ForwardsToFlushableSink()
    {
        using var cancellationSource = new CancellationTokenSource();
        ILogSink sink = new RecordingFlushableSink();

        await sink.FlushAsync(cancellationSource.Token);

        var flushableSink = (RecordingFlushableSink)sink;
        await Assert.That(flushableSink.FlushCallCount).IsEqualTo(1);
        await Assert.That(flushableSink.CancellationToken == cancellationSource.Token).IsTrue();
        await sink.DisposeAsync();
    }

    private sealed class PlainLoggerFactory : ILoggerFactory
    {
        public ILogger CreateLogger(string categoryName) => new NoopLogger();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class RecordingFlushableLoggerFactory : IFlushableLoggerFactory
    {
        public int FlushCallCount { get; private set; }

        public CancellationToken CancellationToken { get; private set; }

        public ILogger CreateLogger(string categoryName) => new NoopLogger();

        public ValueTask FlushAsync(CancellationToken cancellationToken = default)
        {
            FlushCallCount++;
            CancellationToken = cancellationToken;
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class PlainSink : ILogSink
    {
        public Task WriteAsync(LogEntry entry, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public void Dispose() { }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class RecordingFlushableSink : IFlushableLogSink
    {
        public int FlushCallCount { get; private set; }

        public CancellationToken CancellationToken { get; private set; }

        public Task WriteAsync(LogEntry entry, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public ValueTask FlushAsync(CancellationToken cancellationToken = default)
        {
            FlushCallCount++;
            CancellationToken = cancellationToken;
            return ValueTask.CompletedTask;
        }

        public void Dispose() { }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class NoopLogger : ILogger
    {
        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull => NoopDisposable.Instance;

        public void Log(LogLevel logLevel, string message, Exception? exception = null) { }

        public void Log(
            LogLevel logLevel,
            string message,
            IReadOnlyList<KeyValuePair<string, object?>>? properties,
            Exception? exception
        ) { }

        public Task LogAsync(
            LogLevel logLevel,
            string message,
            Exception? exception = null,
            CancellationToken cancellationToken = default
        ) => Task.CompletedTask;

        public Task LogAsync(
            LogLevel logLevel,
            string message,
            IReadOnlyList<KeyValuePair<string, object?>>? properties,
            Exception? exception,
            CancellationToken cancellationToken = default
        ) => Task.CompletedTask;
    }

    private sealed class NoopDisposable : IDisposable
    {
        public static NoopDisposable Instance { get; } = new();

        public void Dispose() { }
    }
}
