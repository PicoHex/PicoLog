namespace PicoLog.Tests;

public sealed class LoggerFactoryTests
{

    [Test]
    public async Task FileSink_DoesNotThrowWhenWritesRaceWithDisposeAsync()
    {
        var filePath = Path.Combine(Path.GetTempPath(), $"pico-logger-{Guid.NewGuid():N}.log");
        await using var sink = new FileSink(
            new ConsoleFormatter(),
            new FileSinkOptions
            {
                FilePath = filePath,
                BatchSize = 8,
                FlushInterval = TimeSpan.FromMilliseconds(5)
            }
        );

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
    public async Task FileSink_BatchesQueuedMessages_And_PersistsAllContent()
    {
        var filePath = Path.Combine(
            Path.GetTempPath(),
            $"pico-logger-batch-{Guid.NewGuid():N}.log"
        );

        try
        {
            await using var sink = new FileSink(
                new ConsoleFormatter(),
                new FileSinkOptions
                {
                    FilePath = filePath,
                    BatchSize = 16,
                    FlushInterval = TimeSpan.FromMilliseconds(10)
                }
            );

            var writes = Enumerable
                .Range(0, 40)
                .Select(
                    index =>
                        sink.WriteAsync(
                            new LogEntry
                            {
                                Timestamp = DateTimeOffset.UtcNow,
                                Level = LogLevel.Info,
                                Category = nameof(LoggerFactoryTests),
                                Message = $"batch-{index}"
                            }
                        )
                );

            await Task.WhenAll(writes);
            await sink.DisposeAsync();

            var contents = await File.ReadAllTextAsync(filePath);

            for (var index = 0; index < 40; index++)
                await Assert.That(contents).Contains($"batch-{index}");
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
    public async Task CreateLogger_ReturnsLogger_ThatIsNotDisposable()
    {
        var sink = new CollectingSink();
        using var factory = new LoggerFactory([sink]);

        var logger = factory.CreateLogger("Tests.Category");

        await Assert.That(logger is IDisposable).IsFalse();
        await Assert.That(logger is IAsyncDisposable).IsFalse();
    }

    [Test]
    public async Task CreateLogger_PreservesStructuredProperties_ThroughNativeILoggerOverload()
    {
        var sink = new CollectingSink();
        using var factory = new LoggerFactory([sink]);

        var logger = factory.CreateLogger("Tests.Category");
        IReadOnlyList<KeyValuePair<string, object?>> properties =
        [
            new("tenant", "alpha"),
            new("attempt", 3)
        ];

        logger.Log(LogLevel.Warning, "structured-runtime-instance", properties, exception: null);

        await factory.DisposeAsync();

        var entry = sink.Entries.Single();
        await Assert.That(entry.Properties).IsNotNull();
        await Assert.That(entry.Properties!).Count().IsEqualTo(2);
        await Assert.That(entry.Properties[0].Key).IsEqualTo("tenant");
        await Assert.That(entry.Properties[0].Value).IsEqualTo("alpha");
        await Assert.That(entry.Properties[1].Key).IsEqualTo("attempt");
        await Assert.That(entry.Properties[1].Value).IsEqualTo(3);
    }

    [Test]
    public async Task CreateLogger_ConcurrentSameCategory_ReturnsSharedLogger_And_CreatesOnePipeline()
    {
        var sink = new CollectingSink();
        var factory = new LoggerFactory([sink]);

        try
        {
            const int callerCount = 128;
            const string categoryName = "Tests.Concurrent.Category";
            using var startGate = new ManualResetEventSlim(initialState: false);
            var createTasks = Enumerable
                .Range(0, callerCount)
                .Select(
                    _ =>
                        Task.Run(() =>
                        {
                            startGate.Wait();
                            return factory.CreateLogger(categoryName);
                        })
                )
                .ToArray();

            startGate.Set();

            var loggers = await Task.WhenAll(createTasks);
            var firstLogger = loggers[0];

            await Assert.That(loggers).Count().IsEqualTo(callerCount);

            foreach (var logger in loggers.Skip(1))
                await Assert.That(logger).IsSameReferenceAs(firstLogger);

            await Assert.That(GetFactoryRegistrationCount(factory)).IsEqualTo(1);
            await Assert.That(GetQueueDepthProviderCount(factory)).IsEqualTo(1);

            await factory.DisposeAsync();

            await Assert.That(GetQueueDepthProviderCount(factory)).IsEqualTo(0);
        }
        finally
        {
            await factory.DisposeAsync();
        }
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
    public async Task TypedLogger_StructuredExtensions_PreserveProperties_ThroughSharedDispatchPath()
    {
        var sink = new CollectingSink();
        await using var factory = new LoggerFactory([sink]);
        ILogger<LoggerConsumer> logger = new Logger<LoggerConsumer>(factory);
        IReadOnlyList<KeyValuePair<string, object?>> properties =
        [
            new("tenant", "alpha"),
            new("attempt", 3)
        ];

        logger.Log(LogLevel.Warning, "typed-sync-structured", properties, exception: null);
        await logger.LogAsync(LogLevel.Error, "typed-async-structured", properties, exception: null);
        await factory.DisposeAsync();

        var entries = sink.Entries.ToArray();
        var firstProperties = entries[0].Properties;
        var secondProperties = entries[1].Properties;
        await Assert.That(entries).Count().IsEqualTo(2);
        await Assert.That(entries[0].Category).IsEqualTo(typeof(LoggerConsumer).FullName!);
        await Assert.That(entries[0].Message).IsEqualTo("typed-sync-structured");
        await Assert.That(firstProperties).IsNotNull();
        await Assert.That(firstProperties!).Count().IsEqualTo(2);
        await Assert.That(firstProperties[0].Key).IsEqualTo("tenant");
        await Assert.That(firstProperties[0].Value).IsEqualTo("alpha");
        await Assert.That(entries[1].Category).IsEqualTo(typeof(LoggerConsumer).FullName!);
        await Assert.That(entries[1].Message).IsEqualTo("typed-async-structured");
        await Assert.That(secondProperties).IsNotNull();
        await Assert.That(secondProperties!).Count().IsEqualTo(2);
        await Assert.That(secondProperties[1].Key).IsEqualTo("attempt");
        await Assert.That(secondProperties[1].Value).IsEqualTo(3);
    }

    [Test]
    public async Task StructuredLogger_PreservesProperties_And_FormatterRendersThem()
    {
        var sink = new CollectingSink();
        await using var factory = new LoggerFactory([sink]);
        var logger = factory.CreateLogger("Tests.Category");
        IReadOnlyList<KeyValuePair<string, object?>> properties =
        [
            new("tenant", "alpha"),
            new("attempt", 3),
            new("nullable", null)
        ];

        logger.Log(LogLevel.Warning, "structured-message", properties, exception: null);
        await factory.DisposeAsync();

        var entry = sink.Entries.Single();
        await Assert.That(entry.Properties).IsNotNull();
        await Assert.That(entry.Properties!).Count().IsEqualTo(3);
        await Assert.That(entry.Properties![0].Key).IsEqualTo("tenant");
        await Assert.That(entry.Properties[0].Value).IsEqualTo("alpha");
        await Assert.That(entry.Properties[1].Key).IsEqualTo("attempt");
        await Assert.That(entry.Properties[1].Value).IsEqualTo(3);
        await Assert.That(entry.Properties[2].Key).IsEqualTo("nullable");
        await Assert.That(entry.Properties[2].Value is null).IsTrue();

        var rendered = new ConsoleFormatter().Format(entry);
        await Assert.That(rendered).Contains("structured-message");
        await Assert.That(rendered).Contains("{tenant=\"alpha\", attempt=3, nullable=null}");
    }

    [Test]
    public async Task ConsoleFormatter_RendersExpectedOutput_WithEscapedProperties_Exception_And_Scopes()
    {
        var formatter = new ConsoleFormatter();
        var entry = new LogEntry
        {
            Timestamp = new DateTimeOffset(2026, 4, 13, 8, 9, 10, 123, TimeSpan.Zero),
            Level = LogLevel.Warning,
            Category = "Tests.Category",
            Message = "hello",
            Exception = new InvalidOperationException("boom"),
            Properties =
            [
                new("quote", "a\"b"),
                new("line", "x\ny"),
                new("tab", "\t"),
                new("slash", "\\"),
                new("letter", 'z'),
                new("number", 3),
                new("nullable", null)
            ],
            Scopes =  ["outer", "inner"]
        };

        var rendered = formatter.Format(entry);
        var expected =
            "[2026-04-13 08:09:10.123] WARNING   [Tests.Category] hello {quote=\"a\\\"b\", line=\"x\\ny\", tab=\"\\t\", slash=\"\\\\\", letter=\"z\", number=3, nullable=null}"
            + Environment.NewLine
            + "EXCEPTION: System.InvalidOperationException: boom"
            + Environment.NewLine
            + "SCOPES: [outer => inner]";

        await Assert.That(rendered).IsEqualTo(expected);
    }

    [Test]
    public async Task StructuredLogger_CopiesProperties_On_EntryCreation()
    {
        var sink = new CollectingSink();
        await using var factory = new LoggerFactory([sink]);
        var logger = factory.CreateLogger("Tests.Category");
        var properties = new List<KeyValuePair<string, object?>> { new("tenant", "alpha") };

        logger.Log(LogLevel.Info, "snapshot", properties, exception: null);
        properties[0] = new KeyValuePair<string, object?>("tenant", "mutated");
        properties.Add(new KeyValuePair<string, object?>("attempt", 2));

        await factory.DisposeAsync();

        var entry = sink.Entries.Single();
        await Assert.That(entry.Properties).IsNotNull();
        await Assert.That(entry.Properties!).Count().IsEqualTo(1);
        await Assert.That(entry.Properties[0].Key).IsEqualTo("tenant");
        await Assert.That(entry.Properties[0].Value).IsEqualTo("alpha");
    }

    [Test]
    public async Task StructuredLogger_CopiesArrayProperties_On_EntryCreation()
    {
        var sink = new CollectingSink();
        await using var factory = new LoggerFactory([sink]);
        var logger = factory.CreateLogger("Tests.Category");
        KeyValuePair<string, object?>[] properties = [new("tenant", "alpha")];

        logger.Log(LogLevel.Info, "snapshot", properties, exception: null);
        properties[0] = new KeyValuePair<string, object?>("tenant", "mutated");

        await factory.DisposeAsync();

        var entry = sink.Entries.Single();
        await Assert.That(entry.Properties).IsNotNull();
        await Assert.That(entry.Properties!).Count().IsEqualTo(1);
        await Assert.That(entry.Properties[0].Key).IsEqualTo("tenant");
        await Assert.That(entry.Properties[0].Value).IsEqualTo("alpha");
    }

    [Test]
    public async Task StructuredLogger_EmptyProperties_RemainNull()
    {
        var sink = new CollectingSink();
        await using var factory = new LoggerFactory([sink]);
        var logger = factory.CreateLogger("Tests.Category");
        KeyValuePair<string, object?>[] properties = [];

        logger.Log(LogLevel.Info, "empty", properties, exception: null);
        await factory.DisposeAsync();

        var entry = sink.Entries.Single();
        await Assert.That(entry.Properties is null).IsTrue();
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
    public async Task BeginScope_FlowsAcrossCategoriesWithinFactory()
    {
        var sink = new CollectingSink();
        await using var factory = new LoggerFactory([sink]);
        var outerLogger = factory.CreateLogger("Tests.Outer");
        var innerLogger = factory.CreateLogger("Tests.Inner");

        using (outerLogger.BeginScope("shared-scope"))
            innerLogger.Warning("other-category");

        await factory.DisposeAsync();

        var entry = sink.Entries.Single();
        var scopes = (entry.Scopes ?? [])
            .Select(scope => scope.ToString() ?? string.Empty)
            .ToArray();

        await Assert.That(entry.Category).IsEqualTo("Tests.Inner");
        await Assert.That(scopes).IsEquivalentTo(["shared-scope"]);
    }

    [Test]
    public async Task BeginScope_ReturnsILogScope_WithOriginalState()
    {
        var sink = new CollectingSink();
        await using var factory = new LoggerFactory([sink]);
        var logger = factory.CreateLogger("Tests.Category");

        using var scope = logger.BeginScope("inspectable-scope");

        await Assert.That(scope is ILogScope).IsTrue();
        await Assert.That(((ILogScope)scope).State).IsEqualTo("inspectable-scope");
    }

    [Test]
    public async Task BeginScope_DoesNotResurrectDisposedOuterScope_WhenDisposedOutOfOrder()
    {
        var sink = new CollectingSink();
        await using var factory = new LoggerFactory([sink]);
        var logger = factory.CreateLogger("Tests.Category");

        var outer = logger.BeginScope("outer");
        var inner = logger.BeginScope("inner");

        outer.Dispose();
        inner.Dispose();

        logger.Info("after-out-of-order-dispose");
        await factory.DisposeAsync();

        var entry = sink.Entries.Single();
        await Assert.That(entry.Message).IsEqualTo("after-out-of-order-dispose");
        await Assert.That(entry.Scopes ?? []).Count().IsEqualTo(0);
    }

    [Test]
    public async Task BeginScope_CapturesDisposedOuterScope_WhileInnerIsStillActive()
    {
        var sink = new CollectingSink();
        await using var factory = new LoggerFactory([sink]);
        var logger = factory.CreateLogger("Tests.Category");

        var outer = logger.BeginScope("outer");
        using var inner = logger.BeginScope("inner");

        outer.Dispose();
        logger.Info("while-inner-still-active");
        await factory.DisposeAsync();

        var entry = sink.Entries.Single();
        var scopes = (entry.Scopes ?? [])
            .Select(scope => scope.ToString() ?? string.Empty)
            .ToArray();

        await Assert.That(scopes).IsEquivalentTo(["outer", "inner"]);
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
    public async Task AlreadyCreatedLogger_ObservesLaterMinLevelChanges()
    {
        var sink = new CollectingSink();
        await using var factory = new LoggerFactory([sink]) { MinLevel = LogLevel.Warning };
        var logger = factory.CreateLogger("Tests.Category");

        logger.Info("ignored-before-change");
        factory.MinLevel = LogLevel.Info;
        logger.Info("recorded-after-lowering-threshold");
        factory.MinLevel = LogLevel.Error;
        logger.Warning("ignored-after-tightening-threshold");
        await logger.ErrorAsync("recorded-after-tightening-threshold");

        await factory.DisposeAsync();

        var entries = sink.Entries.ToArray();
        await Assert.That(entries).Count().IsEqualTo(2);
        await Assert.That(entries[0].Level).IsEqualTo(LogLevel.Info);
        await Assert.That(entries[0].Message).IsEqualTo("recorded-after-lowering-threshold");
        await Assert.That(entries[1].Level).IsEqualTo(LogLevel.Error);
        await Assert.That(entries[1].Message).IsEqualTo("recorded-after-tightening-threshold");
    }

    [Test]
    public async Task DropWrite_Mode_ReportsDroppedMessages_WhenQueueIsFull()
    {
        long reportedDropCount = 0;
        var sink = new BlockingSink();
        var options = new LoggerFactoryOptions
        {
            QueueCapacity = 1,
            QueueFullMode = LogQueueFullMode.DropWrite,
            OnMessagesDropped = (_, droppedCount) => reportedDropCount = droppedCount
        };
        await using var factory = new LoggerFactory([sink], options);
        var logger = factory.CreateLogger("Tests.Category");

        logger.Info("first");
        await sink.WriteStarted;
        logger.Info("second");
        logger.Info("third");

        sink.Release();
        await factory.DisposeAsync();

        await Assert.That(reportedDropCount).IsEqualTo(1);
        await Assert.That(sink.WrittenCount).IsEqualTo(2);
    }

    [Test]
    public async Task DropWrite_Mode_AsyncWrites_ReportDroppedMessages_WhenQueueIsFull()
    {
        long reportedDropCount = 0;
        var sink = new BlockingSink();
        var options = new LoggerFactoryOptions
        {
            QueueCapacity = 1,
            QueueFullMode = LogQueueFullMode.DropWrite,
            OnMessagesDropped = (_, droppedCount) => reportedDropCount = droppedCount
        };
        await using var factory = new LoggerFactory([sink], options);
        var logger = factory.CreateLogger("Tests.Category");

        await logger.InfoAsync("first");
        await sink.WriteStarted;
        await logger.InfoAsync("second");
        await logger.InfoAsync("third");

        sink.Release();
        await factory.DisposeAsync();

        await Assert.That(reportedDropCount).IsEqualTo(1);
        await Assert.That(sink.WrittenCount).IsEqualTo(2);
    }

    [Test]
    public async Task DropOldest_Mode_ReportsDroppedMessages_WhenQueueIsFull()
    {
        long reportedDropCount = 0;
        var sink = new BlockingSink();
        var options = new LoggerFactoryOptions
        {
            QueueCapacity = 1,
            QueueFullMode = LogQueueFullMode.DropOldest,
            OnMessagesDropped = (_, droppedCount) => reportedDropCount = droppedCount
        };
        await using var factory = new LoggerFactory([sink], options);
        var logger = factory.CreateLogger("Tests.Category");

        logger.Info("first");
        await sink.WriteStarted;
        logger.Info("second");
        logger.Info("third");

        sink.Release();
        await factory.DisposeAsync();

        await Assert.That(reportedDropCount).IsEqualTo(1);
        await Assert.That(sink.WrittenCount).IsEqualTo(2);
    }

    [Test]
    public async Task DropOldest_Mode_AsyncWrites_ReportDroppedMessages_WhenQueueIsFull()
    {
        long reportedDropCount = 0;
        var sink = new BlockingSink();
        var options = new LoggerFactoryOptions
        {
            QueueCapacity = 1,
            QueueFullMode = LogQueueFullMode.DropOldest,
            OnMessagesDropped = (_, droppedCount) => reportedDropCount = droppedCount
        };
        await using var factory = new LoggerFactory([sink], options);
        var logger = factory.CreateLogger("Tests.Category");

        await logger.InfoAsync("first");
        await sink.WriteStarted;
        await logger.InfoAsync("second");
        await logger.InfoAsync("third");

        sink.Release();
        await factory.DisposeAsync();

        await Assert.That(reportedDropCount).IsEqualTo(1);
        await Assert.That(sink.WrittenCount).IsEqualTo(2);
    }

    [Test]
    public async Task Wait_Mode_BlocksSyncWrites_UntilQueueSpaceIsAvailable()
    {
        var sink = new BlockingSink();
        long reportedDropCount = 0;
        var options = new LoggerFactoryOptions
        {
            QueueCapacity = 1,
            QueueFullMode = LogQueueFullMode.Wait,
            SyncWriteTimeout = TimeSpan.FromSeconds(1),
            OnMessagesDropped = (_, droppedCount) => reportedDropCount = droppedCount
        };
        await using var factory = new LoggerFactory([sink], options);
        var logger = factory.CreateLogger("Tests.Category");

        logger.Info("first");
        await sink.WriteStarted;
        logger.Info("second");

        var thirdWrite = Task.Run(() => logger.Info("third"));

        await Task.Delay(100);
        await Assert.That(thirdWrite.IsCompleted).IsFalse();

        sink.Release();
        await thirdWrite;
        await factory.DisposeAsync();

        await Assert.That(sink.WrittenCount).IsEqualTo(3);
        await Assert.That(reportedDropCount).IsEqualTo(0);
    }

    [Test]
    public async Task Wait_Mode_BlocksAsyncWrites_UntilQueueSpaceIsAvailable()
    {
        var sink = new BlockingSink();
        long reportedDropCount = 0;
        var options = new LoggerFactoryOptions
        {
            QueueCapacity = 1,
            QueueFullMode = LogQueueFullMode.Wait,
            SyncWriteTimeout = TimeSpan.FromSeconds(1),
            OnMessagesDropped = (_, droppedCount) => reportedDropCount = droppedCount
        };
        await using var factory = new LoggerFactory([sink], options);
        var logger = factory.CreateLogger("Tests.Category");

        await logger.InfoAsync("first");
        await sink.WriteStarted;
        await logger.InfoAsync("second");

        var thirdWrite = logger.InfoAsync("third");

        await Task.Delay(100);
        await Assert.That(thirdWrite.IsCompleted).IsFalse();

        sink.Release();
        await thirdWrite;
        await factory.DisposeAsync();

        await Assert.That(sink.WrittenCount).IsEqualTo(3);
        await Assert.That(reportedDropCount).IsEqualTo(0);
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
            [new ThrowingSink(), new ColoredConsoleSink(new TestFormatter(), writer)]
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
    public async Task Logging_WithPlainConsoleFallback_WritesSinkFailureDetails()
    {
        using var writer = new StringWriter();
        await using var factory = new LoggerFactory(
            [new ThrowingSink(), new ConsoleSink(new TestFormatter(), writer)]
        );
        var logger = factory.CreateLogger("Tests.Category");

        await logger.WarningAsync("payload");
        await factory.DisposeAsync();

        await Assert
            .That(writer.ToString())
            .Contains("Error|Failed to write log entry to sink: payload|Sink failure");
    }

    [Test]
    public async Task Logging_WithMultipleConsoleFallbacks_UsesLastRegisteredConsoleSink()
    {
        using var firstWriter = new StringWriter();
        using var secondWriter = new StringWriter();
        await using var factory = new LoggerFactory(

            [
                new ThrowingSink(),
                new ConsoleSink(new TestFormatter(), firstWriter),
                new ColoredConsoleSink(new TestFormatter(), secondWriter)
            ]
        );
        var logger = factory.CreateLogger("Tests.Category");

        await logger.WarningAsync("payload");
        await factory.DisposeAsync();

        await Assert
            .That(firstWriter.ToString())
            .DoesNotContain("Failed to write log entry to sink: payload");
        await Assert
            .That(secondWriter.ToString())
            .Contains("Error|Failed to write log entry to sink: payload|Sink failure");
    }

    [Test]
    public async Task Logging_WithCustomSink_DoesNotUseIt_AsConsoleFallback()
    {
        var customSink = new CustomFallbackLookingSink();
        var collectingSink = new CollectingSink();
        await using var factory = new LoggerFactory(
            [new ThrowingSink(), customSink, collectingSink]
        );
        var logger = factory.CreateLogger("Tests.Category");

        await logger.WarningAsync("payload");
        await factory.DisposeAsync();

        await Assert.That(collectingSink.Entries).Count().IsEqualTo(1);
        await Assert.That(collectingSink.Entries.Single().Message).IsEqualTo("payload");
        await Assert.That(customSink.Entries).Count().IsEqualTo(1);
        await Assert.That(customSink.Entries.Single().Message).IsEqualTo("payload");
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
    public async Task ConsoleSink_SerializesWrites_WhenSharingNonConsoleWriter()
    {
        await AssertBuiltInConsoleSinkSerializesWritesToSharedNonConsoleWriterAsync(
            static writer => new ConsoleSink(new TestFormatter(), writer)
        );
    }

    [Test]
    public async Task ColoredConsoleSink_WritesPlainText_WhenWriterIsNotConsoleOut()
    {
        using var writer = new StringWriter();
        await using var sink = new ColoredConsoleSink(new TestFormatter(), writer);

        await sink.WriteAsync(
            new LogEntry
            {
                Timestamp = DateTimeOffset.UtcNow,
                Level = LogLevel.Warning,
                Category = nameof(LoggerFactoryTests),
                Message = "colored-message"
            }
        );

        await Assert.That(writer.ToString()).Contains("Warning|colored-message");
    }

    [Test]
    public async Task ColoredConsoleSink_SerializesWrites_WhenSharingNonConsoleWriter()
    {
        await AssertBuiltInConsoleSinkSerializesWritesToSharedNonConsoleWriterAsync(
            static writer => new ColoredConsoleSink(new TestFormatter(), writer)
        );
    }

    [Test]
#pragma warning disable TUnit0055 // Intentional: this test must exercise the real Console.Out path.
    public async Task ColoredConsoleSink_UsesConsoleOutPath_And_RestoresForegroundColor()
    {
        var originalWriter = Console.Out;
        using var redirectedWriter = new StringWriter();

        try
        {
            Console.SetOut(redirectedWriter);
            var colorBeforeWrite = Console.ForegroundColor;

            await using var sink = new ColoredConsoleSink(new TestFormatter());

            await sink.WriteAsync(
                new LogEntry
                {
                    Timestamp = DateTimeOffset.UtcNow,
                    Level = LogLevel.Error,
                    Category = nameof(LoggerFactoryTests),
                    Message = "console-out-message"
                }
            );

            await Assert.That(redirectedWriter.ToString()).Contains("Error|console-out-message");
            await Assert.That(Console.ForegroundColor).IsEqualTo(colorBeforeWrite);
        }
        finally
        {
            Console.SetOut(originalWriter);
        }
    }
#pragma warning restore TUnit0055

    [Test]
    public async Task AddLogging_ResolvesTypedLoggerFromContainer()
    {
        var filePath = Path.Combine(Path.GetTempPath(), $"pico-logger-di-{Guid.NewGuid():N}.log");

        try
        {
            ISvcContainer container = new SvcContainer();
            container.AddPicoLog(LogLevel.Info, filePath);
            container.RegisterScoped<LoggerConsumer, LoggerConsumer>();

            await using var scope = container.CreateScope();
            var consumer = scope.GetService<LoggerConsumer>();
            var factory = scope.GetService<ILoggerFactory>();

            await Assert.That(consumer).IsNotNull();
            await Assert.That(consumer!.Logger).IsNotNull();

            await factory.DisposeAsync();
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
            ISvcContainer container = new SvcContainer();
            container.AddPicoLog(LogLevel.Info, filePath);

            await using var scope = container.CreateScope();
            var factory = (ILoggerFactory)scope.GetService(typeof(ILoggerFactory));
            var logger = factory.CreateLogger("Tests.Category");

            await logger.InfoAsync("configured-path");

            await factory!.DisposeAsync();

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
            ISvcContainer container = new SvcContainer();
            container.AddPicoLog(LogLevel.Warning, filePath);
            container.RegisterScoped<LoggerConsumer, LoggerConsumer>();

            await using var scope = container.CreateScope();
            var consumer = scope.GetService<LoggerConsumer>();
            var factory = (ILoggerFactory)scope.GetService(typeof(ILoggerFactory));

            await Assert.That(consumer).IsNotNull();

            consumer!.Logger.Info("ignored-by-min-level");
            await consumer.Logger.ErrorAsync("written-by-typed-logger");

            await factory.DisposeAsync();

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

    [Test]
    public async Task DisposeAsync_WaitsForActiveSinkWrites_BeforeDisposingSinks()
    {
        var sink = new CoordinatedDisposalSink();
        await using var factory = new LoggerFactory([sink]);
        var logger = factory.CreateLogger("Tests.Category");

        await logger.InfoAsync("payload");
        await sink.WriteStarted;

        var disposeTask = factory.DisposeAsync().AsTask();
        await Task.Delay(50);

        await Assert.That(sink.DisposeCallCount).IsEqualTo(0);

        sink.ReleaseWrite();
        await disposeTask;

        await Assert.That(sink.DisposeCallCount).IsEqualTo(1);
    }

    [Test]
    public async Task DisposeAsync_RejectsWrites_After_Shutdown_Begins()
    {
        var sink = new CoordinatedDisposalSink();
        await using var factory = new LoggerFactory([sink]);
        var logger = factory.CreateLogger("Tests.Category");

        await logger.InfoAsync("before-dispose");
        await sink.WriteStarted;

        var disposeTask = factory.DisposeAsync().AsTask();
        await Task.Delay(50);

        logger.Warning("after-dispose-sync");
        await logger.ErrorAsync("after-dispose-async");

        sink.ReleaseWrite();
        await disposeTask;

        var messages = sink.Entries.Select(entry => entry.Message).ToArray();
        await Assert.That(messages).Count().IsEqualTo(1);
        await Assert.That(messages[0]).IsEqualTo("before-dispose");
    }

    [Test]
    public async Task FactoryDisposeAsync_ClassifiesPendingWaitWrites_AsRejectedAfterShutdown()
    {
        using var listener = new MeterListener();
        var measurements = new ConcurrentDictionary<string, ConcurrentQueue<double>>(
            StringComparer.Ordinal
        );

        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == PicoLogMetrics.MeterName)
                meterListener.EnableMeasurementEvents(instrument);
        };

        listener.SetMeasurementEventCallback<long>(
            (instrument, measurement, _, _) =>
                measurements
                    .GetOrAdd(instrument.Name, _ => new ConcurrentQueue<double>())
                    .Enqueue(measurement)
        );
        listener.Start();

        var sink = new BlockingSink();
        long reportedDropCount = 0;
        var options = new LoggerFactoryOptions
        {
            QueueCapacity = 1,
            QueueFullMode = LogQueueFullMode.Wait,
            OnMessagesDropped = (_, count) => reportedDropCount = count
        };
        await using var factory = new LoggerFactory([sink], options);
        var logger = factory.CreateLogger("Tests.LoggerDispose");

        logger.Info("first");
        await sink.WriteStarted;
        logger.Info("second");

        var pendingWriteTask = logger.InfoAsync("third");
        await Task.Delay(50);
        await Assert.That(pendingWriteTask.IsCompleted).IsFalse();

        var disposeTask = factory.DisposeAsync().AsTask();
        await Task.Delay(50);

        sink.Release();
        await Task.WhenAll(disposeTask, pendingWriteTask);
        listener.RecordObservableInstruments();

        await Assert.That(sink.WrittenCount).IsEqualTo(2);
        await Assert.That(reportedDropCount).IsEqualTo(0);
        await AssertMeasurementAtLeastAsync(
            measurements,
            PicoLogMetrics.ShutdownRejectedWritesName,
            1
        );
    }

    [Test]
    public async Task DropOldest_Mode_ReportsAcceptedAfterEviction_AsDropped_Publicly()
    {
        long reportedDropCount = 0;
        var sink = new BlockingSink();
        var options = new LoggerFactoryOptions
        {
            QueueCapacity = 1,
            QueueFullMode = LogQueueFullMode.DropOldest,
            OnMessagesDropped = (_, droppedCount) => reportedDropCount = droppedCount
        };
        await using var factory = new LoggerFactory([sink], options);
        var logger = factory.CreateLogger("Tests.Category");

        logger.Info("first");
        await sink.WriteStarted;
        logger.Info("second");
        logger.Info("third");

        sink.Release();
        await factory.DisposeAsync();

        var messages = sink.Entries.Select(entry => entry.Message ?? string.Empty).ToArray();
        await Assert.That(messages).IsEquivalentTo(["first", "third"]);
        await Assert.That(reportedDropCount).IsEqualTo(1);
    }

    [Test]
    public async Task DropWrite_Mode_ReportsDroppedNewWrite_Publicly_WithoutEvictingQueuedEntry()
    {
        long reportedDropCount = 0;
        var sink = new BlockingSink();
        var options = new LoggerFactoryOptions
        {
            QueueCapacity = 1,
            QueueFullMode = LogQueueFullMode.DropWrite,
            OnMessagesDropped = (_, droppedCount) => reportedDropCount = droppedCount
        };
        await using var factory = new LoggerFactory([sink], options);
        var logger = factory.CreateLogger("Tests.Category");

        logger.Info("first");
        await sink.WriteStarted;
        logger.Info("second");
        logger.Info("third");

        sink.Release();
        await factory.DisposeAsync();

        var messages = sink.Entries.Select(entry => entry.Message ?? string.Empty).ToArray();
        await Assert.That(messages).IsEquivalentTo(["first", "second"]);
        await Assert.That(reportedDropCount).IsEqualTo(1);
    }

    [Test]
    public async Task Metrics_Record_RejectedWrites_DroppedWrites_SinkFailures_And_ShutdownDuration()
    {
        using var listener = new MeterListener();
        var measurements = new ConcurrentDictionary<string, ConcurrentQueue<double>>(
            StringComparer.Ordinal
        );

        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == PicoLogMetrics.MeterName)
                meterListener.EnableMeasurementEvents(instrument);
        };

        listener.SetMeasurementEventCallback<long>(
            (instrument, measurement, _, _) =>
                measurements
                    .GetOrAdd(instrument.Name, _ => new ConcurrentQueue<double>())
                    .Enqueue(measurement)
        );
        listener.SetMeasurementEventCallback<double>(
            (instrument, measurement, _, _) =>
                measurements
                    .GetOrAdd(instrument.Name, _ => new ConcurrentQueue<double>())
                    .Enqueue(measurement)
        );
        listener.Start();

        var sink = new BlockingSink();
        var options = new LoggerFactoryOptions
        {
            QueueCapacity = 1,
            QueueFullMode = LogQueueFullMode.DropWrite
        };
        await using var dropFactory = new LoggerFactory([sink], options);
        var dropLogger = dropFactory.CreateLogger("Tests.Drop");

        dropLogger.Info("first");
        await sink.WriteStarted;
        dropLogger.Info("second");
        await RecordObservableMeasurementUntilAsync(
            listener,
            measurements,
            PicoLogMetrics.QueuedEntriesName,
            value => value == 1
        );
        dropLogger.Info("third");
        sink.Release();
        await dropFactory.DisposeAsync();

        var collectingSink = new CollectingSink();
        await using var failureFactory = new LoggerFactory([new ThrowingSink(), collectingSink]);
        var failureLogger = failureFactory.CreateLogger("Tests.Failures");
        await failureLogger.WarningAsync("payload");
        await failureFactory.DisposeAsync();

        var coordinatedSink = new CoordinatedDisposalSink();
        await using var shutdownFactory = new LoggerFactory([coordinatedSink]);
        var shutdownLogger = shutdownFactory.CreateLogger("Tests.Shutdown");
        await shutdownLogger.InfoAsync("before-shutdown");
        await coordinatedSink.WriteStarted;

        var shutdownDisposeTask = shutdownFactory.DisposeAsync().AsTask();
        await Task.Delay(50);
        shutdownLogger.Info("after-shutdown");
        coordinatedSink.ReleaseWrite();
        await shutdownDisposeTask;
        listener.RecordObservableInstruments();

        await AssertMeasurementAtLeastAsync(measurements, PicoLogMetrics.EntriesEnqueuedName, 4);
        await AssertMeasurementAtLeastAsync(measurements, PicoLogMetrics.EntriesDroppedName, 1);
        await AssertMeasurementAtLeastAsync(measurements, PicoLogMetrics.SinkFailuresName, 1);
        await AssertMeasurementAtLeastAsync(
            measurements,
            PicoLogMetrics.ShutdownRejectedWritesName,
            1
        );
        await AssertMeasurementAtLeastAsync(
            measurements,
            PicoLogMetrics.ShutdownDrainDurationName,
            0
        );
        await AssertMeasurementContainsAsync(measurements, PicoLogMetrics.QueuedEntriesName, 1);
        await AssertAllMeasurementsAtLeastAsync(measurements, PicoLogMetrics.QueuedEntriesName, 0);
    }

    private sealed class CollectingSink : ILogSink
    {
        private readonly ConcurrentQueue<LogEntry> _entries = [];

        public IReadOnlyCollection<LogEntry> Entries => _entries.ToArray();

        public Task WriteAsync(LogEntry entry, CancellationToken cancellationToken = default)
        {
            _entries.Enqueue(entry);
            return Task.CompletedTask;
        }

        public void Dispose() { }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class ThrowingSink : ILogSink
    {
        public Task WriteAsync(LogEntry entry, CancellationToken cancellationToken = default) =>
            Task.FromException(new InvalidOperationException("Sink failure"));

        public void Dispose() { }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class BlockingSink : ILogSink
    {
        private readonly ConcurrentQueue<LogEntry> _entries = [];
        private readonly TaskCompletionSource _writeStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _writtenCount;

        public IReadOnlyCollection<LogEntry> Entries => _entries.ToArray();

        public int WrittenCount => _writtenCount;

        public Task WriteStarted => _writeStarted.Task;

        public async Task WriteAsync(LogEntry entry, CancellationToken cancellationToken = default)
        {
            _entries.Enqueue(entry);
            Interlocked.Increment(ref _writtenCount);
            _writeStarted.TrySetResult();

            await _release.Task.WaitAsync(cancellationToken);
        }

        public void Release() => _release.TrySetResult();

        public void Dispose() => Release();

        public ValueTask DisposeAsync()
        {
            Release();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ThrowOnDisposeSink : ILogSink
    {
        public Task WriteAsync(LogEntry entry, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public void Dispose() => throw new InvalidOperationException("dispose failure");

        public ValueTask DisposeAsync() =>
            ValueTask.FromException(new InvalidOperationException("dispose failure"));
    }

    private sealed class CustomFallbackLookingSink : ILogSink
    {
        private readonly ConcurrentQueue<LogEntry> _entries = [];

        public IReadOnlyCollection<LogEntry> Entries => _entries.ToArray();

        public Task WriteAsync(LogEntry entry, CancellationToken cancellationToken = default)
        {
            _entries.Enqueue(entry);
            return Task.CompletedTask;
        }

        public void Dispose() { }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class CoordinatedDisposalSink : ILogSink
    {
        private readonly ConcurrentQueue<LogEntry> _entries = [];
        private readonly TaskCompletionSource _writeStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _disposeCallCount;

        public IReadOnlyCollection<LogEntry> Entries => _entries.ToArray();

        public Task WriteStarted => _writeStarted.Task;

        public int DisposeCallCount => _disposeCallCount;

        public async Task WriteAsync(LogEntry entry, CancellationToken cancellationToken = default)
        {
            _entries.Enqueue(entry);
            _writeStarted.TrySetResult();
            await _release.Task.WaitAsync(cancellationToken);
        }

        public void ReleaseWrite() => _release.TrySetResult();

        public void Dispose() => Interlocked.Increment(ref _disposeCallCount);

        public ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref _disposeCallCount);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class TestFormatter : ILogFormatter
    {
        public string Format(LogEntry entry) =>
            $"{entry.Level}|{entry.Message}|{entry.Exception?.Message}";
    }

    private sealed class ConcurrentWriteDetectingWriter : TextWriter
    {
        private readonly ConcurrentQueue<string?> _lines = [];
        private readonly TaskCompletionSource _firstWriteEntered =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _releaseWrites =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _activeWriteCount;
        private int _enteredWriteCount;
        private int _concurrentWriteCount;

        public override System.Text.Encoding Encoding => System.Text.Encoding.UTF8;

        public Task FirstWriteEntered => _firstWriteEntered.Task;

        public IReadOnlyCollection<string?> Lines => _lines.ToArray();

        public int ConcurrentWriteCount => Volatile.Read(ref _concurrentWriteCount);

        public override void WriteLine(string? value)
        {
            if (Interlocked.Increment(ref _activeWriteCount) > 1)
                Interlocked.Increment(ref _concurrentWriteCount);

            if (Interlocked.Increment(ref _enteredWriteCount) == 1)
                _firstWriteEntered.TrySetResult();

            try
            {
                _releaseWrites.Task.GetAwaiter().GetResult();
                _lines.Enqueue(value);
            }
            finally
            {
                Interlocked.Decrement(ref _activeWriteCount);
            }
        }

        public void ReleaseWrites() => _releaseWrites.TrySetResult();
    }

    private static async Task AssertMeasurementAtLeastAsync(
        ConcurrentDictionary<string, ConcurrentQueue<double>> measurements,
        string instrumentName,
        double minimumValue
    )
    {
        await Assert.That(measurements.TryGetValue(instrumentName, out var values)).IsTrue();
        await Assert.That(values!).IsNotEmpty();
        await Assert.That(values!.Sum()).IsGreaterThanOrEqualTo(minimumValue);
    }

    private static async Task AssertMeasurementContainsAsync(
        ConcurrentDictionary<string, ConcurrentQueue<double>> measurements,
        string instrumentName,
        double expectedValue
    )
    {
        await Assert.That(measurements.TryGetValue(instrumentName, out var values)).IsTrue();
        await Assert.That(values!).Contains(expectedValue);
    }

    private static async Task AssertAllMeasurementsAtLeastAsync(
        ConcurrentDictionary<string, ConcurrentQueue<double>> measurements,
        string instrumentName,
        double minimumValue
    )
    {
        await Assert.That(measurements.TryGetValue(instrumentName, out var values)).IsTrue();
        await Assert.That(values!).IsNotEmpty();
        await Assert.That(values!.All(value => value >= minimumValue)).IsTrue();
    }

    private static async Task RecordObservableMeasurementUntilAsync(
        MeterListener listener,
        ConcurrentDictionary<string, ConcurrentQueue<double>> measurements,
        string instrumentName,
        Func<double, bool> predicate
    )
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            listener.RecordObservableInstruments();

            if (measurements.TryGetValue(instrumentName, out var values) && values.Any(predicate))
                return;

            await Task.Delay(10);
        }

        throw new InvalidOperationException(
            $"Did not observe expected measurement for '{instrumentName}' within the sampling window."
        );
    }

    private static async Task AssertBuiltInConsoleSinkSerializesWritesToSharedNonConsoleWriterAsync(
        Func<TextWriter, ILogSink> createSink
    )
    {
        using var writer = new ConcurrentWriteDetectingWriter();
        await using var sink = createSink(writer);

        var firstWrite = Task.Run(
            () =>
                sink.WriteAsync(
                    new LogEntry
                    {
                        Timestamp = DateTimeOffset.UtcNow,
                        Level = LogLevel.Warning,
                        Category = nameof(LoggerFactoryTests),
                        Message = "first"
                    }
                )
        );

        await writer.FirstWriteEntered;

        var secondWrite = Task.Run(
            () =>
                sink.WriteAsync(
                    new LogEntry
                    {
                        Timestamp = DateTimeOffset.UtcNow,
                        Level = LogLevel.Error,
                        Category = nameof(LoggerFactoryTests),
                        Message = "second"
                    }
                )
        );

        await Task.Delay(50);
        writer.ReleaseWrites();

        await Task.WhenAll(firstWrite, secondWrite);

        await Assert.That(writer.ConcurrentWriteCount).IsEqualTo(0);
        await Assert.That(writer.Lines).Count().IsEqualTo(2);
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

    private static int GetFactoryRegistrationCount(LoggerFactory factory)
    {
        var registrationsField = typeof(LoggerFactory).GetField(
            "_registrations",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic
        );
        var registrations = registrationsField!.GetValue(factory)!;
        var countProperty = registrations.GetType().GetProperty("Count");

        return (int)countProperty!.GetValue(registrations)!;
    }

    private static int GetQueueDepthProviderCount(LoggerFactory factory)
    {
        var runtimeField = typeof(LoggerFactory).GetField(
            "_runtime",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic
        );
        var runtime = runtimeField!.GetValue(factory)!;
        var providersField = typeof(PicoLogMetrics).GetField(
            "QueueDepthProviders",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic
        );
        var providers = (System.Collections.IDictionary)providersField!.GetValue(null)!;
        var matchingProviderCount = 0;

        foreach (System.Collections.DictionaryEntry providerEntry in providers)
        {
            if (providerEntry.Value is not Delegate provider)
                continue;

            var queue = provider.Target;

            if (queue is null)
                continue;

            var queueRuntimeField = queue
                .GetType()
                .GetField(
                    "_runtime",
                    System.Reflection.BindingFlags.Instance
                        | System.Reflection.BindingFlags.NonPublic
                );
            var queueRuntime = queueRuntimeField?.GetValue(queue);

            if (ReferenceEquals(queueRuntime, runtime))
                matchingProviderCount++;
        }

        return matchingProviderCount;
    }
}

public sealed class LoggerConsumer(ILogger<LoggerConsumer> logger)
{
    public ILogger<LoggerConsumer> Logger { get; } = logger;
}
