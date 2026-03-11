namespace Pico.Logging.Tests;

public sealed class LoggerFactoryTests
{
    [Test]
    public async Task FileSink_DoesNotThrowWhenWritesRaceWithDisposeAsync()
    {
        var filePath = Path.Combine(Path.GetTempPath(), $"pico-logger-{Guid.NewGuid():N}.log");
        await using var sink = new FileSink(new ConsoleFormatter(), filePath);

        var writeTasks = Enumerable
            .Range(0, 64)
            .Select(
                index =>
                    sink.WriteAsync(
                            new LogEntry
                            {
                                Timestamp = DateTimeOffset.UtcNow,
                                Level = LogLevel.Info,
                                Category = nameof(LoggerFactoryTests),
                                Message = $"message-{index}"
                            }
                        )
                        .AsTask()
            )
            .ToArray();

        await sink.DisposeAsync();
        await Task.WhenAll(writeTasks);

        if (File.Exists(filePath))
            File.Delete(filePath);
    }

    [Test]
    public async Task FileSink_Dispose_FlushesExistingContent_And_IgnoresLaterWrites()
    {
        var filePath = Path.Combine(Path.GetTempPath(), $"pico-logger-sync-{Guid.NewGuid():N}.log");

        try
        {
            var sink = new FileSink(new ConsoleFormatter(), filePath);

            await sink.WriteAsync(
                new LogEntry
                {
                    Timestamp = DateTimeOffset.UtcNow,
                    Level = LogLevel.Info,
                    Category = nameof(LoggerFactoryTests),
                    Message = "before-dispose"
                }
            );

            sink.Dispose();

            await sink.WriteAsync(
                new LogEntry
                {
                    Timestamp = DateTimeOffset.UtcNow,
                    Level = LogLevel.Warning,
                    Category = nameof(LoggerFactoryTests),
                    Message = "after-dispose"
                }
            );

            var contents = await File.ReadAllTextAsync(filePath);
            await Assert.That(contents).Contains("before-dispose");
            await Assert.That(contents.Contains("after-dispose")).IsFalse();
        }
        finally
        {
            if (File.Exists(filePath))
                File.Delete(filePath);
        }
    }

    [Test]
    public async Task CreateLogger_CachesLoggerPerCategory()
    {
        var sink = new CollectingSink();
        using var factory = new LoggerFactory([sink]);

        var first = factory.CreateLogger("Tests.Category");
        var second = factory.CreateLogger("Tests.Category");

        await Assert.That(first).IsSameReferenceAs(second);
    }

    [Test]
    public async Task TypedLogger_DelegatesSyncAsyncLogs_And_Scopes()
    {
        var sink = new CollectingSink();
        await using var factory = new LoggerFactory([sink]);
        ILogger<LoggerConsumer> logger = new Logger<LoggerConsumer>(factory);

        using (logger.BeginScope("typed-scope"))
        {
            logger.Warning("typed-sync");
            await logger.CriticalAsync("typed-async", new InvalidOperationException("typed-boom"));
        }

        await factory.DisposeAsync();

        var entries = sink.Entries.ToArray();
        await Assert.That(entries).Count().IsEqualTo(2);
        await Assert.That(entries[0].Category).IsEqualTo(typeof(LoggerConsumer).FullName!);
        await Assert.That(entries[0].Message).IsEqualTo("typed-sync");
        await Assert.That(entries[0].Level).IsEqualTo(LogLevel.Warning);
        await Assert.That(entries[1].Category).IsEqualTo(typeof(LoggerConsumer).FullName!);
        await Assert.That(entries[1].Message).IsEqualTo("typed-async");
        await Assert.That(entries[1].Level).IsEqualTo(LogLevel.Critical);
        await Assert.That(entries[1].Exception is InvalidOperationException).IsTrue();

        var scopes = (entries[1].Scopes ?? [])
            .Select(scope => scope.ToString() ?? string.Empty)
            .ToArray();
        await Assert.That(scopes).IsEquivalentTo(["typed-scope"]);
    }

    [Test]
    public async Task DisposeAsync_FlushesQueuedEntriesAndScopes()
    {
        var sink = new CollectingSink();
        await using var factory = new LoggerFactory([sink]);
        var logger = factory.CreateLogger("Tests.Category");

        using (logger.BeginScope("outer"))
        using (logger.BeginScope("inner"))
        {
            logger.Info("sync-message");
            await logger.ErrorAsync("async-message", new InvalidOperationException("boom"));
        }

        await factory.DisposeAsync();

        var entries = sink.Entries.ToArray();
        var firstScopes = (entries[0].Scopes ?? [])
            .Select(scope => scope.ToString() ?? string.Empty)
            .ToArray();
        var secondScopes = (entries[1].Scopes ?? [])
            .Select(scope => scope.ToString() ?? string.Empty)
            .ToArray();

        await Assert.That(entries).Count().IsEqualTo(2);
        await Assert.That(entries[0].Level).IsEqualTo(LogLevel.Info);
        await Assert.That(entries[0].Message).IsEqualTo("sync-message");
        await Assert.That(firstScopes).IsEquivalentTo(["outer", "inner"]);
        await Assert.That(entries[1].Level).IsEqualTo(LogLevel.Error);
        await Assert.That(entries[1].Message).IsEqualTo("async-message");
        await Assert.That(entries[1].Exception is InvalidOperationException).IsTrue();
        await Assert.That(secondScopes).IsEquivalentTo(["outer", "inner"]);
    }

    [Test]
    public async Task DisposeAsync_FlushesAsyncTailMessages()
    {
        var sink = new CollectingSink();
        await using var factory = new LoggerFactory([sink]);
        var logger = factory.CreateLogger("Tests.Category");

        await logger.DebugAsync("tail-debug");
        await logger.NoticeAsync("tail-notice");
        await logger.InfoAsync("tail-info");

        await factory.DisposeAsync();

        var messages = sink.Entries
            .Select(entry => entry.Message?.ToString() ?? string.Empty)
            .ToArray();

        await Assert.That(messages).IsEquivalentTo(["tail-debug", "tail-notice", "tail-info"]);
    }

    [Test]
    public async Task MinLevel_FiltersMessagesBelowThreshold()
    {
        var sink = new CollectingSink();
        await using var factory = new LoggerFactory([sink]) { MinLevel = LogLevel.Warning };
        var logger = factory.CreateLogger("Tests.Category");

        logger.Info("ignored");
        await logger.ErrorAsync("recorded");

        await WaitForEntriesAsync(sink, 1);

        var entries = sink.Entries.ToArray();
        await Assert.That(entries).Count().IsEqualTo(1);
        await Assert.That(entries[0].Level).IsEqualTo(LogLevel.Error);
        await Assert.That(entries[0].Message).IsEqualTo("recorded");
    }

    [Test]
    public async Task Logging_ContinuesWhenOneSinkThrowsWithoutConsoleFallback()
    {
        var collectingSink = new CollectingSink();
        await using var factory = new LoggerFactory([new ThrowingSink(), collectingSink]);
        var logger = factory.CreateLogger("Tests.Category");

        await logger.WarningAsync("still-recorded");

        await WaitForEntriesAsync(collectingSink, 1);

        await factory.DisposeAsync();

        var entries = collectingSink.Entries.ToArray();
        await Assert.That(entries).Count().IsEqualTo(1);
        await Assert.That(entries[0].Level).IsEqualTo(LogLevel.Warning);
        await Assert.That(entries[0].Message).IsEqualTo("still-recorded");
    }

    [Test]
    public async Task Logging_WithConsoleFallback_WritesSinkFailureDetails()
    {
        using var writer = new StringWriter();
        await using var factory = new LoggerFactory(
            [new ThrowingSink(), new ConsoleSink(new TestFormatter(), writer)]
        );
        var logger = factory.CreateLogger("Tests.Category");

        await logger.WarningAsync("payload");
        await factory.DisposeAsync();

        var output = writer.ToString();

        await Assert
            .That(output)
            .Contains("Error|Failed to write log entry to sink: payload|Sink failure");
    }

    [Test]
    public async Task ConsoleSink_WritesMessages_For_All_LogLevels()
    {
        using var writer = new StringWriter();
        await using var sink = new ConsoleSink(new TestFormatter(), writer);

        foreach (var level in Enum.GetValues<LogLevel>())
        {
            await sink.WriteAsync(
                new LogEntry
                {
                    Timestamp = DateTimeOffset.UtcNow,
                    Level = level,
                    Category = nameof(LoggerFactoryTests),
                    Message = $"level-{level}"
                }
            );
        }

        var output = writer.ToString();

        foreach (var level in Enum.GetValues<LogLevel>())
            await Assert.That(output).Contains($"{level}|level-{level}");
    }

    [Test]
    public async Task AddLogging_ResolvesTypedLoggerFromContainer()
    {
        var filePath = Path.Combine(Path.GetTempPath(), $"pico-logger-di-{Guid.NewGuid():N}.log");

        try
        {
            var container = new SvcContainer();
            container.AddLogging(LogLevel.Info, filePath);
            container.RegisterScoped<LoggerConsumer, LoggerConsumer>();

            await using var scope = container.CreateScope();
            var consumer = scope.GetService<LoggerConsumer>();
            var factory = scope.GetService<ILoggerFactory>();

            await Assert.That(consumer).IsNotNull();
            await Assert.That(consumer!.Logger).IsNotNull();

            if (factory is IAsyncDisposable asyncDisposable)
                await asyncDisposable.DisposeAsync();
        }
        finally
        {
            if (File.Exists(filePath))
                File.Delete(filePath);
        }
    }

    [Test]
    public async Task AddLogging_UsesConfiguredFilePath()
    {
        var filePath = Path.Combine(Path.GetTempPath(), $"pico-logger-di-{Guid.NewGuid():N}.log");

        try
        {
            var container = new SvcContainer();
            container.AddLogging(LogLevel.Info, filePath);

            await using var scope = container.CreateScope();
            var factory = (ILoggerFactory)scope.GetService(typeof(ILoggerFactory));
            var logger = factory.CreateLogger("Tests.Category");

            await logger.InfoAsync("configured-path");

            if (factory is IAsyncDisposable asyncDisposable)
                await asyncDisposable.DisposeAsync();

            await Assert.That(File.Exists(filePath)).IsTrue();
            var contents = await File.ReadAllTextAsync(filePath);
            await Assert.That(contents).Contains("configured-path");
        }
        finally
        {
            if (File.Exists(filePath))
                File.Delete(filePath);
        }
    }

    [Test]
    public async Task LoggerFactory_PersistsTailMessagesToFileSink()
    {
        var filePath = Path.Combine(Path.GetTempPath(), $"pico-logger-file-{Guid.NewGuid():N}.log");

        try
        {
            var formatter = new ConsoleFormatter();
            await using var factory = new LoggerFactory([new FileSink(formatter, filePath)]);
            var logger = factory.CreateLogger("Tests.Category");

            await logger.DebugAsync("tail-debug");
            await logger.NoticeAsync("tail-notice");
            await logger.InfoAsync("tail-info");

            await factory.DisposeAsync();

            var contents = await File.ReadAllTextAsync(filePath);
            await Assert.That(contents).Contains("tail-debug");
            await Assert.That(contents).Contains("tail-notice");
            await Assert.That(contents).Contains("tail-info");
        }
        finally
        {
            if (File.Exists(filePath))
                File.Delete(filePath);
        }
    }

    [Test]
    public async Task AddLogging_TypedLoggerUsesResolvedFactory()
    {
        var filePath = Path.Combine(Path.GetTempPath(), $"pico-logger-di-{Guid.NewGuid():N}.log");

        try
        {
            var container = new SvcContainer();
            container.AddLogging(LogLevel.Warning, filePath);
            container.RegisterScoped<LoggerConsumer, LoggerConsumer>();

            await using var scope = container.CreateScope();
            var consumer = scope.GetService<LoggerConsumer>();
            var factory = (ILoggerFactory)scope.GetService(typeof(ILoggerFactory));

            await Assert.That(consumer).IsNotNull();

            consumer!.Logger.Info("ignored-by-min-level");
            await consumer.Logger.ErrorAsync("written-by-typed-logger");

            if (factory is IAsyncDisposable asyncDisposable)
                await asyncDisposable.DisposeAsync();

            var contents = await File.ReadAllTextAsync(filePath);
            await Assert.That(contents.Contains("ignored-by-min-level")).IsFalse();
            await Assert.That(contents).Contains("written-by-typed-logger");
            await Assert.That(contents).Contains(typeof(LoggerConsumer).FullName!);
        }
        finally
        {
            if (File.Exists(filePath))
                File.Delete(filePath);
        }
    }

    [Test]
    public async Task DisposeAsync_AggregatesSinkDisposalFailures()
    {
        await using var factory = new LoggerFactory([new ThrowOnDisposeSink()]);

        AggregateException? exception = null;

        try
        {
            await factory.DisposeAsync();
        }
        catch (AggregateException ex)
        {
            exception = ex;
        }

        await Assert.That(exception).IsNotNull();
        await Assert.That(exception!.InnerExceptions).Count().IsEqualTo(1);
        await Assert.That(exception.InnerExceptions[0].Message).IsEqualTo("dispose failure");
    }

    private sealed class CollectingSink : ILogSink
    {
        private readonly ConcurrentQueue<LogEntry> _entries = [];

        public IReadOnlyCollection<LogEntry> Entries => _entries.ToArray();

        public ValueTask WriteAsync(LogEntry entry, CancellationToken cancellationToken = default)
        {
            _entries.Enqueue(entry);
            return ValueTask.CompletedTask;
        }

        public void Dispose() { }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class ThrowingSink : ILogSink
    {
        public ValueTask WriteAsync(
            LogEntry entry,
            CancellationToken cancellationToken = default
        ) => ValueTask.FromException(new InvalidOperationException("Sink failure"));

        public void Dispose() { }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class ThrowOnDisposeSink : ILogSink
    {
        public ValueTask WriteAsync(
            LogEntry entry,
            CancellationToken cancellationToken = default
        ) => ValueTask.CompletedTask;

        public void Dispose() => throw new InvalidOperationException("dispose failure");

        public ValueTask DisposeAsync() =>
            ValueTask.FromException(new InvalidOperationException("dispose failure"));
    }

    private sealed class TestFormatter : ILogFormatter
    {
        public string Format(LogEntry entry) =>
            $"{entry.Level}|{entry.Message}|{entry.Exception?.Message}";
    }

    private static async Task WaitForEntriesAsync(CollectingSink sink, int expectedCount)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (sink.Entries.Count >= expectedCount)
                return;

            await Task.Delay(10);
        }
    }
}

public sealed class LoggerConsumer(ILogger<LoggerConsumer> logger)
{
    public ILogger<LoggerConsumer> Logger { get; } = logger;
}
