namespace PicoLog.Tests;

public sealed class ILoggerStructuredCallSiteTests
{
    [Test]
    public async Task NativeStructuredOverloads_AllowUnambiguousCallShapes()
    {
        ILogger logger = new RecordingLogger();
        IReadOnlyList<KeyValuePair<string, object?>> properties = [new("tenant", "alpha")];
        var exception = new InvalidOperationException("boom");
        using var cancellationSource = new CancellationTokenSource();
        var cancellationToken = cancellationSource.Token;

        logger.Log(LogLevel.Info, "plain");
        logger.Log(LogLevel.Warning, "with-exception", exception);
        logger.Log(LogLevel.Error, "with-properties", properties, exception);

        await logger.LogAsync(LogLevel.Info, "plain-async");
        await logger.LogAsync(LogLevel.Warning, "with-exception-async", exception, cancellationToken);
        await logger.LogAsync(LogLevel.Error, "with-properties-async", properties, exception, cancellationToken);

        var recordingLogger = (RecordingLogger)logger;

        await Assert.That(recordingLogger.SyncStructuredEntries.Count).IsEqualTo(1);
        await Assert.That(recordingLogger.AsyncStructuredEntries.Count).IsEqualTo(1);
        await Assert.That(recordingLogger.SyncEntries.Count).IsEqualTo(2);
        await Assert.That(recordingLogger.AsyncEntries.Count).IsEqualTo(2);
    }

    private sealed class RecordingLogger : ILogger
    {
        public List<(LogLevel Level, string Message, Exception? Exception)> SyncEntries { get; } = [];
        public List<(LogLevel Level, string Message, Exception? Exception, CancellationToken CancellationToken)> AsyncEntries { get; } = [];
        public List<(LogLevel Level, string Message, IReadOnlyList<KeyValuePair<string, object?>>? Properties, Exception? Exception)> SyncStructuredEntries { get; } = [];
        public List<(LogLevel Level, string Message, IReadOnlyList<KeyValuePair<string, object?>>? Properties, Exception? Exception, CancellationToken CancellationToken)> AsyncStructuredEntries { get; } = [];

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull => NoopDisposable.Instance;

        public void Log(LogLevel logLevel, string message, Exception? exception = null) =>
            SyncEntries.Add((logLevel, message, exception));

        public void Log(
            LogLevel logLevel,
            string message,
            IReadOnlyList<KeyValuePair<string, object?>>? properties,
            Exception? exception
        ) => SyncStructuredEntries.Add((logLevel, message, properties, exception));

        public Task LogAsync(
            LogLevel logLevel,
            string message,
            Exception? exception = null,
            CancellationToken cancellationToken = default
        )
        {
            AsyncEntries.Add((logLevel, message, exception, cancellationToken));
            return Task.CompletedTask;
        }

        public Task LogAsync(
            LogLevel logLevel,
            string message,
            IReadOnlyList<KeyValuePair<string, object?>>? properties,
            Exception? exception,
            CancellationToken cancellationToken = default
        )
        {
            AsyncStructuredEntries.Add((logLevel, message, properties, exception, cancellationToken));
            return Task.CompletedTask;
        }
    }

    private sealed class NoopDisposable : IDisposable
    {
        public static NoopDisposable Instance { get; } = new();

        public void Dispose() { }
    }
}
