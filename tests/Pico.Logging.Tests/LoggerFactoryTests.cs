namespace Pico.Logging.Tests;

public sealed class LoggerFactoryTests
{
    [Fact]
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

    [Fact]
    public void CreateLogger_CachesLoggerPerCategory()
    {
        var sink = new CollectingSink();
        using var factory = new LoggerFactory([sink]);

        var first = factory.CreateLogger("Tests.Category");
        var second = factory.CreateLogger("Tests.Category");

        Assert.Same(first, second);
    }

    [Fact]
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

        Assert.Collection(
            sink.Entries,
            entry =>
            {
                Assert.Equal(LogLevel.Info, entry.Level);
                Assert.Equal("sync-message", entry.Message);
                Assert.Equal(["outer", "inner"], entry.Scopes);
            },
            entry =>
            {
                Assert.Equal(LogLevel.Error, entry.Level);
                Assert.Equal("async-message", entry.Message);
                Assert.IsType<InvalidOperationException>(entry.Exception);
                Assert.Equal(["outer", "inner"], entry.Scopes);
            }
        );
    }

    [Fact]
    public async Task MinLevel_FiltersMessagesBelowThreshold()
    {
        var sink = new CollectingSink();
        await using var factory = new LoggerFactory([sink]) { MinLevel = LogLevel.Warning };
        var logger = factory.CreateLogger("Tests.Category");

        logger.Info("ignored");
        await logger.ErrorAsync("recorded");

        await WaitForEntriesAsync(sink, 1);

        var entry = Assert.Single(sink.Entries);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.Equal("recorded", entry.Message);
    }

    [Fact]
    public async Task Logging_ContinuesWhenOneSinkThrowsWithoutConsoleFallback()
    {
        var collectingSink = new CollectingSink();
        await using var factory = new LoggerFactory([new ThrowingSink(), collectingSink]);
        var logger = factory.CreateLogger("Tests.Category");

        await logger.WarningAsync("still-recorded");

        await factory.DisposeAsync();

        var entry = Assert.Single(collectingSink.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Equal("still-recorded", entry.Message);
    }

    [Fact]
    public async Task AddLogging_ResolvesTypedLoggerFromContainer()
    {
        var container = new SvcContainer();
        container.AddLogging(LogLevel.Info);
        container.RegisterScoped<LoggerConsumer, LoggerConsumer>();

        await using var scope = container.CreateScope();
        var consumer = scope.GetService<LoggerConsumer>();
        var factory = scope.GetService<ILoggerFactory>();

        Assert.NotNull(consumer);
        Assert.NotNull(consumer.Logger);

        if (factory is IAsyncDisposable asyncDisposable)
            await asyncDisposable.DisposeAsync();
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
