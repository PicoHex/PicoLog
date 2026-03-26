namespace Pico.Logging.Tests;

public sealed class LoggerExtensionsTests
{
    [Test]
    public async Task SyncExtensions_ForwardExpectedLevels_And_Exceptions()
    {
        var logger = new RecordingLogger();
        var exception = new InvalidOperationException("sync-failure");

        logger.Trace("trace");
        logger.Debug("debug");
        logger.Info("info");
        logger.Notice("notice", exception);
        logger.Warning("warning", exception);
        logger.Error("error", exception);
        logger.Critical("critical", exception);
        logger.Alert("alert", exception);
        logger.Emergency("emergency", exception);

        await Assert.That(logger.SyncEntries.Count).IsEqualTo(9);
        await Assert
            .That(logger.SyncEntries.Select(entry => entry.Level).ToArray())
            .IsEquivalentTo(

                [
                    LogLevel.Trace,
                    LogLevel.Debug,
                    LogLevel.Info,
                    LogLevel.Notice,
                    LogLevel.Warning,
                    LogLevel.Error,
                    LogLevel.Critical,
                    LogLevel.Alert,
                    LogLevel.Emergency
                ]
            );
        await Assert
            .That(logger.SyncEntries.Select(entry => entry.Message).ToArray())
            .IsEquivalentTo(

                [
                    "trace",
                    "debug",
                    "info",
                    "notice",
                    "warning",
                    "error",
                    "critical",
                    "alert",
                    "emergency"
                ]
            );
        await Assert.That(logger.SyncEntries[0].Exception is null).IsTrue();
        await Assert.That(logger.SyncEntries[1].Exception is null).IsTrue();
        await Assert.That(logger.SyncEntries[2].Exception is null).IsTrue();

        for (var index = 3; index < logger.SyncEntries.Count; index++)
            await Assert.That(logger.SyncEntries[index].Exception).IsSameReferenceAs(exception);
    }

    [Test]
    public async Task AsyncExtensions_ForwardExpectedLevels_Exceptions_And_CancellationToken()
    {
        var logger = new RecordingLogger();
        var exception = new InvalidOperationException("async-failure");
        using var cancellationSource = new CancellationTokenSource();
        var cancellationToken = cancellationSource.Token;

        await logger.TraceAsync("trace", cancellationToken);
        await logger.DebugAsync("debug", cancellationToken);
        await logger.InfoAsync("info", cancellationToken);
        await logger.NoticeAsync("notice", exception, cancellationToken);
        await logger.WarningAsync("warning", exception, cancellationToken);
        await logger.ErrorAsync("error", exception, cancellationToken);
        await logger.CriticalAsync("critical", exception, cancellationToken);
        await logger.AlertAsync("alert", exception, cancellationToken);
        await logger.EmergencyAsync("emergency", exception, cancellationToken);

        await Assert.That(logger.AsyncEntries.Count).IsEqualTo(9);
        await Assert
            .That(logger.AsyncEntries.Select(entry => entry.Level).ToArray())
            .IsEquivalentTo(

                [
                    LogLevel.Trace,
                    LogLevel.Debug,
                    LogLevel.Info,
                    LogLevel.Notice,
                    LogLevel.Warning,
                    LogLevel.Error,
                    LogLevel.Critical,
                    LogLevel.Alert,
                    LogLevel.Emergency
                ]
            );
        await Assert
            .That(logger.AsyncEntries.Select(entry => entry.Message).ToArray())
            .IsEquivalentTo(

                [
                    "trace",
                    "debug",
                    "info",
                    "notice",
                    "warning",
                    "error",
                    "critical",
                    "alert",
                    "emergency"
                ]
            );
        await Assert
            .That(logger.AsyncEntries.All(entry => entry.CancellationToken == cancellationToken))
            .IsTrue();
        await Assert.That(logger.AsyncEntries[0].Exception is null).IsTrue();
        await Assert.That(logger.AsyncEntries[1].Exception is null).IsTrue();
        await Assert.That(logger.AsyncEntries[2].Exception is null).IsTrue();

        for (var index = 3; index < logger.AsyncEntries.Count; index++)
            await Assert.That(logger.AsyncEntries[index].Exception).IsSameReferenceAs(exception);
    }

    private sealed class RecordingLogger : ILogger
    {
        public List<RecordedEntry> SyncEntries { get; } = [];
        public List<RecordedEntry> AsyncEntries { get; } = [];

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull => NoopDisposable.Instance;

        public void Log(LogLevel logLevel, string message, Exception? exception = null)
        {
            SyncEntries.Add(new RecordedEntry(logLevel, message, exception, default));
        }

        public Task LogAsync(
            LogLevel logLevel,
            string message,
            Exception? exception = null,
            CancellationToken cancellationToken = default
        )
        {
            AsyncEntries.Add(new RecordedEntry(logLevel, message, exception, cancellationToken));
            return Task.CompletedTask;
        }
    }

    private sealed record RecordedEntry(
        LogLevel Level,
        string Message,
        Exception? Exception,
        CancellationToken CancellationToken
    );

    private sealed class NoopDisposable : IDisposable
    {
        public static NoopDisposable Instance { get; } = new();

        public void Dispose() { }
    }
}
