namespace PicoLog.Tests;

public sealed class SvcContainerExtensionsTests
{
    private sealed class PrefixFormatter(string prefix) : ILogFormatter
    {
        public string Format(LogEntry entry) => $"{prefix}|{entry.Level}|{entry.Category}|{entry.Message}";
    }

    private sealed class RecordingSink : ILogSink
    {
        private readonly ConcurrentQueue<LogEntry> _entries = [];
        private int _disposeCallCount;

        public IReadOnlyCollection<LogEntry> Entries => _entries.ToArray();

        public int DisposeCallCount => Volatile.Read(ref _disposeCallCount);

        public Task WriteAsync(LogEntry entry, CancellationToken cancellationToken = default)
        {
            _entries.Enqueue(entry);
            return Task.CompletedTask;
        }

        public void Dispose() => Interlocked.Increment(ref _disposeCallCount);

        public ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref _disposeCallCount);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingFlushableSink : IFlushableLogSink
    {
        private readonly ConcurrentQueue<LogEntry> _entries = [];
        private int _flushCallCount;
        private int _disposeCallCount;

        public IReadOnlyCollection<LogEntry> Entries => _entries.ToArray();

        public int FlushCallCount => Volatile.Read(ref _flushCallCount);

        public int DisposeCallCount => Volatile.Read(ref _disposeCallCount);

        public Task WriteAsync(LogEntry entry, CancellationToken cancellationToken = default)
        {
            _entries.Enqueue(entry);
            return Task.CompletedTask;
        }

        public ValueTask FlushAsync(CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _flushCallCount);
            return ValueTask.CompletedTask;
        }

        public void Dispose() => Interlocked.Increment(ref _disposeCallCount);

        public ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref _disposeCallCount);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class OrderingSink(string name, ConcurrentQueue<string> writes) : ILogSink
    {
        public Task WriteAsync(LogEntry entry, CancellationToken cancellationToken = default)
        {
            writes.Enqueue(name);
            return Task.CompletedTask;
        }

        public void Dispose() { }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    [Test]
    public async Task AddLogging_ReturnsTheSameContainerInstance()
    {
        ISvcContainer container = new SvcContainer();
        var filePath = Path.Combine(Path.GetTempPath(), $"pico-logger-di-{Guid.NewGuid():N}.log");

        try
        {
            var result = container.AddPicoLog(LogLevel.Info, filePath);

            await Assert.That(result).IsSameReferenceAs(container);
        }
        finally
        {
            if (File.Exists(filePath))
                File.Delete(filePath);
        }
    }

    [Test]
    public async Task AddLogging_ThrowsWhenConfiguredWithBlankFilePath()
    {
        ISvcContainer container = new SvcContainer();

        ArgumentException? exception = null;

        try
        {
            container.AddPicoLog(LogLevel.Info, " ");
        }
        catch (ArgumentException ex)
        {
            exception = ex;
        }

        await Assert.That(exception).IsNotNull();
        await Assert.That(exception!.ParamName).IsEqualTo("FilePath");
    }

    [Test]
    public async Task AddLogging_ConfigureOverload_AppliesFactoryAndFileOptions()
    {
        ISvcContainer container = new SvcContainer();
        var filePath = Path.Combine(Path.GetTempPath(), $"pico-logger-di-{Guid.NewGuid():N}.log");

        try
        {
            container.AddPicoLog(options =>
            {
                options.MinLevel = LogLevel.Warning;
                options.UseColoredConsole = false;
                options.FilePath = filePath;
                options.Factory.QueueCapacity = 8;
                options.Factory.QueueFullMode = LogQueueFullMode.Wait;
                options.File.BatchSize = 4;
                options.File.FlushInterval = TimeSpan.FromMilliseconds(5);
            });

            await using var scope = container.CreateScope();
            var factory = (ILoggerFactory)scope.GetService(typeof(ILoggerFactory));
            var logger = factory.CreateLogger("Tests.Category");

            logger.Info("ignored");
            await logger.ErrorAsync("written");

            await factory.DisposeAsync();

            var contents = await File.ReadAllTextAsync(filePath);
            await Assert.That(contents.Contains("ignored")).IsFalse();
            await Assert.That(contents).Contains("written");
        }
        finally
        {
            if (File.Exists(filePath))
                File.Delete(filePath);
        }
    }

    [Test]
    public async Task AddLogging_ConfigureOverload_DoesNotCreateFileSinkByDefault()
    {
        ISvcContainer container = new SvcContainer();

        container.AddPicoLog(options =>
        {
            options.MinLevel = LogLevel.Info;
            options.UseColoredConsole = false;
        });

        await using var scope = container.CreateScope();
        var factory = (LoggerFactory)scope.GetService(typeof(ILoggerFactory));
        var sinksField = typeof(LoggerFactory).GetField(
            "_sinks",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic
        );
        var sinks = (ILogSink[])sinksField!.GetValue(factory)!;

        await Assert.That(sinks).Count().IsEqualTo(1);
        await Assert.That(sinks[0] is ConsoleSink).IsTrue();

        await factory.DisposeAsync();
    }

    [Test]
    public async Task AddLogging_ConfigureOverload_UsesColoredConsoleByDefault()
    {
        ISvcContainer container = new SvcContainer();

        container.AddPicoLog(options =>
        {
            options.MinLevel = LogLevel.Info;
        });

        await using var scope = container.CreateScope();
        var factory = (LoggerFactory)scope.GetService(typeof(ILoggerFactory));
        var sinksField = typeof(LoggerFactory).GetField(
            "_sinks",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic
        );
        var sinks = (ILogSink[])sinksField!.GetValue(factory)!;

        await Assert.That(sinks).Count().IsEqualTo(1);
        await Assert.That(sinks[0] is ColoredConsoleSink).IsTrue();

        await factory.DisposeAsync();
    }

    [Test]
    public async Task AddLogging_ConfigureOverload_HonorsNestedFactoryMinLevel()
    {
        ISvcContainer container = new SvcContainer();
        var filePath = Path.Combine(Path.GetTempPath(), $"pico-logger-di-{Guid.NewGuid():N}.log");

        try
        {
            container.AddPicoLog(options =>
            {
                options.UseColoredConsole = false;
                options.Factory.MinLevel = LogLevel.Error;
                options.FilePath = filePath;
            });

            await using var scope = container.CreateScope();
            var factory = (ILoggerFactory)scope.GetService(typeof(ILoggerFactory));
            var logger = factory.CreateLogger("Tests.Category");

            logger.Warning("ignored-warning");
            await logger.CriticalAsync("written-critical");

            await factory.DisposeAsync();

            var contents = await File.ReadAllTextAsync(filePath);
            await Assert.That(contents.Contains("ignored-warning")).IsFalse();
            await Assert.That(contents).Contains("written-critical");
        }
        finally
        {
            if (File.Exists(filePath))
                File.Delete(filePath);
        }
    }

    [Test]
    public async Task AddLogging_ThrowsWhenFileSinkEnabledWithoutExplicitFilePath()
    {
        ISvcContainer container = new SvcContainer();

        InvalidOperationException? exception = null;

        try
        {
            container.AddPicoLog(options =>
            {
                options.EnableFileSink = true;
            });
        }
        catch (InvalidOperationException ex)
        {
            exception = ex;
        }

        await Assert.That(exception).IsNotNull();
        await Assert.That(exception!.Message).Contains("explicitly configured FilePath");
    }

    [Test]
    public async Task AddLogging_ConfigureOverload_UsesNestedFilePath_AsFileSinkOptIn()
    {
        ISvcContainer container = new SvcContainer();
        var filePath = Path.Combine(Path.GetTempPath(), $"pico-logger-di-{Guid.NewGuid():N}.log");

        try
        {
            container.AddPicoLog(options =>
            {
                options.MinLevel = LogLevel.Info;
                options.UseColoredConsole = false;
                options.File.FilePath = filePath;
            });

            await using var scope = container.CreateScope();
            var factory = (ILoggerFactory)scope.GetService(typeof(ILoggerFactory));
            var logger = factory.CreateLogger("Tests.Category");

            await logger.InfoAsync("nested-file-path");
            await factory.DisposeAsync();

            await Assert.That(File.Exists(filePath)).IsTrue();
            var contents = await File.ReadAllTextAsync(filePath);
            await Assert.That(contents).Contains("nested-file-path");
        }
        finally
        {
            if (File.Exists(filePath))
                File.Delete(filePath);
        }
    }

    [Test]
    public async Task AddPicoLog_ResolvesTypedLogger_And_PreservesPropertiesThroughExtensions()
    {
        ISvcContainer container = new SvcContainer();
        var filePath = Path.Combine(Path.GetTempPath(), $"pico-logger-structured-{Guid.NewGuid():N}.log");

        try
        {
            container.AddPicoLog(options =>
            {
                options.MinLevel = LogLevel.Info;
                options.UseColoredConsole = false;
                options.FilePath = filePath;
            });

            await using var scope = container.CreateScope();
            var logger = scope.GetService<ILogger<LoggerConsumer>>();
            var control = scope.GetService<IPicoLogControl>();

            logger.LogStructured(
                LogLevel.Warning,
                "structured-di-message",
                [new("tenant", "alpha"), new("attempt", 3)]
            );
            await control.DisposeAsync();

            var contents = await File.ReadAllTextAsync(filePath);
            await Assert.That(contents).Contains(typeof(LoggerConsumer).FullName!);
            await Assert.That(contents).Contains("structured-di-message");
            await Assert.That(contents).Contains("{tenant=\"alpha\", attempt=3}");
        }
        finally
        {
            if (File.Exists(filePath))
                File.Delete(filePath);
        }
    }

    [Test]
    public async Task AddPicoLog_ResolvesPicoLogControl_AsSameInstanceAsILoggerFactory()
    {
        ISvcContainer container = new SvcContainer();

        container.AddPicoLog(options =>
        {
            options.MinLevel = LogLevel.Info;
            options.UseColoredConsole = false;
        });

        await using var scope = container.CreateScope();
        var loggerFactory = scope.GetService<ILoggerFactory>();
        var logControl = scope.GetService<IPicoLogControl>();

        await Assert.That(loggerFactory).IsNotNull();
        await Assert.That(logControl).IsNotNull();
        await Assert.That(logControl).IsSameReferenceAs(loggerFactory);

        await logControl.DisposeAsync();
    }

    [Test]
    public async Task AddLogging_LegacyEntryPoint_StillRegistersLegacyContracts()
    {
        ISvcContainer container = new SvcContainer();

#pragma warning disable CS0618
        container.AddLogging(options =>
        {
            options.MinLevel = LogLevel.Info;
            options.UseColoredConsole = false;
        });
#pragma warning restore CS0618

        await using var scope = container.CreateScope();
        var loggerFactory = scope.GetService<ILoggerFactory>();
        var flushableFactory = scope.GetService<IFlushableLoggerFactory>();
        var structuredLogger = scope.GetService<IStructuredLogger<LoggerConsumer>>();

        await Assert.That(flushableFactory).IsSameReferenceAs(loggerFactory);
        await Assert.That(structuredLogger).IsNotNull();

        await loggerFactory.DisposeAsync();
    }

    [Test]
    public async Task AddLogging_ConfigureOverload_UsesConfiguredFormatter_ForFileSinkOutput()
    {
        ISvcContainer container = new SvcContainer();
        var filePath = Path.Combine(Path.GetTempPath(), $"pico-logger-di-{Guid.NewGuid():N}.log");

        try
        {
            container.AddPicoLog(options =>
            {
                options.MinLevel = LogLevel.Info;
                options.UseColoredConsole = false;
                options.FilePath = filePath;
                options.Formatter = new PrefixFormatter("custom");
            });

            await using var scope = container.CreateScope();
            var factory = scope.GetService<ILoggerFactory>();
            var logger = factory.CreateLogger("Tests.Category");

            await logger.WarningAsync("formatted-message");
            await factory.DisposeAsync();

            var contents = await File.ReadAllTextAsync(filePath);
            await Assert.That(contents).Contains("custom|Warning|Tests.Category|formatted-message");
        }
        finally
        {
            if (File.Exists(filePath))
                File.Delete(filePath);
        }
    }

    [Test]
    public async Task AddLogging_ThrowsWhenFormatterIsNull()
    {
        ISvcContainer container = new SvcContainer();

        ArgumentNullException? exception = null;

        try
        {
            container.AddPicoLog(options =>
            {
                options.Formatter = null!;
            });
        }
        catch (ArgumentNullException ex)
        {
            exception = ex;
        }

        await Assert.That(exception).IsNotNull();
        await Assert.That(exception!.ParamName).IsEqualTo("Formatter");
    }

    [Test]
    public async Task AddLogging_ConfigureOverload_ExplicitTopLevelFilePath_RemainsFileSinkOptIn_WhenEnableFlagIsReset()
    {
        ISvcContainer container = new SvcContainer();
        var filePath = Path.Combine(Path.GetTempPath(), $"pico-logger-di-{Guid.NewGuid():N}.log");

        try
        {
            container.AddPicoLog(options =>
            {
                options.MinLevel = LogLevel.Info;
                options.UseColoredConsole = false;
                options.FilePath = filePath;
                options.EnableFileSink = false;
            });

            await using var scope = container.CreateScope();
            var factory = (ILoggerFactory)scope.GetService(typeof(ILoggerFactory));
            var logger = factory.CreateLogger("Tests.Category");

            await logger.InfoAsync("top-level-file-path");
            await factory.DisposeAsync();

            await Assert.That(File.Exists(filePath)).IsTrue();
            var contents = await File.ReadAllTextAsync(filePath);
            await Assert.That(contents).Contains("top-level-file-path");
        }
        finally
        {
            if (File.Exists(filePath))
                File.Delete(filePath);
        }
    }

    [Test]
    public async Task AddLogging_WriteTo_CustomSinkInstance_ReceivesEntries_And_IsDisposedByFactory()
    {
        ISvcContainer container = new SvcContainer();
        var sink = new RecordingSink();

        container.AddPicoLog(options =>
        {
            options.WriteTo.Sink(sink);
        });

        await using var scope = container.CreateScope();
        var factory = scope.GetService<ILoggerFactory>();
        var logger = factory.CreateLogger("Tests.Category");

        await logger.WarningAsync("custom-sink-message");
        await factory.DisposeAsync();

        await Assert.That(sink.Entries.Select(entry => entry.Message ?? string.Empty).ToArray())
            .IsEquivalentTo(["custom-sink-message"]);
        await Assert.That(sink.DisposeCallCount).IsEqualTo(1);
    }

    [Test]
    public async Task AddLogging_WriteTo_CustomSinkFactory_IsLazy_And_CreatesOneSinkInstance()
    {
        ISvcContainer container = new SvcContainer();
        var createCount = 0;
        RecordingSink? createdSink = null;

        container.AddPicoLog(options =>
        {
            options.WriteTo.Sink(() =>
            {
                Interlocked.Increment(ref createCount);
                createdSink = new RecordingSink();
                return createdSink;
            });
        });

        await Assert.That(createCount).IsEqualTo(0);

        await using var scope = container.CreateScope();
        var loggerFactory = scope.GetService<ILoggerFactory>();
        var logControl = scope.GetService<IPicoLogControl>();

        await Assert.That(logControl).IsSameReferenceAs(loggerFactory);
        await Assert.That(createCount).IsEqualTo(1);

        var logger = loggerFactory.CreateLogger("Tests.Category");
        await logger.InfoAsync("lazy-created-sink");
        await loggerFactory.DisposeAsync();

        await Assert.That(createdSink).IsNotNull();
        await Assert.That(createdSink!.Entries.Select(entry => entry.Message ?? string.Empty).ToArray())
            .IsEquivalentTo(["lazy-created-sink"]);
        await Assert.That(createdSink.DisposeCallCount).IsEqualTo(1);
    }

    [Test]
    public async Task AddLogging_WriteTo_ExplicitSinks_DoNotAlsoAddLegacyDefaultSinks()
    {
        ISvcContainer container = new SvcContainer();
        var sink = new RecordingSink();

        container.AddPicoLog(options =>
        {
            options.UseColoredConsole = true;
            options.FilePath = Path.Combine(Path.GetTempPath(), $"pico-logger-di-{Guid.NewGuid():N}.log");
            options.WriteTo.Sink(sink);
        });

        await using var scope = container.CreateScope();
        var factory = (LoggerFactory)scope.GetService(typeof(ILoggerFactory));
        var sinksField = typeof(LoggerFactory).GetField(
            "_sinks",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic
        );
        var sinks = (ILogSink[])sinksField!.GetValue(factory)!;

        await Assert.That(sinks).Count().IsEqualTo(1);
        await Assert.That(ReferenceEquals(sinks[0], sink)).IsTrue();

        await factory.DisposeAsync();
    }

    [Test]
    public async Task AddLogging_WriteTo_File_UsesExistingFileOptions_And_Formatter()
    {
        ISvcContainer container = new SvcContainer();
        var filePath = Path.Combine(Path.GetTempPath(), $"pico-logger-di-{Guid.NewGuid():N}.log");

        try
        {
            container.AddPicoLog(options =>
            {
                options.Formatter = new PrefixFormatter("write-to");
                options.FilePath = filePath;
                options.File.BatchSize = 4;
                options.File.FlushInterval = TimeSpan.FromMilliseconds(5);
                options.WriteTo.File();
            });

            await using var scope = container.CreateScope();
            var factory = scope.GetService<ILoggerFactory>();
            var logger = factory.CreateLogger("Tests.Category");

            await logger.WarningAsync("write-to-file");
            await factory.DisposeAsync();

            var contents = await File.ReadAllTextAsync(filePath);
            await Assert.That(contents).Contains("write-to|Warning|Tests.Category|write-to-file");
        }
        finally
        {
            if (File.Exists(filePath))
                File.Delete(filePath);
        }
    }

    [Test]
    public async Task AddLogging_WriteTo_File_WithExplicitPath_UsesThatPathWithoutTopLevelFilePath()
    {
        ISvcContainer container = new SvcContainer();
        var filePath = Path.Combine(Path.GetTempPath(), $"pico-logger-di-{Guid.NewGuid():N}.log");

        try
        {
            container.AddPicoLog(options =>
            {
                options.Formatter = new PrefixFormatter("write-to-explicit-path");
                options.WriteTo.File(filePath);
            });

            await using var scope = container.CreateScope();
            var factory = scope.GetService<ILoggerFactory>();
            var logger = factory.CreateLogger("Tests.Category");

            await logger.WarningAsync("write-to-explicit-path-message");
            await factory.DisposeAsync();

            var contents = await File.ReadAllTextAsync(filePath);
            await Assert.That(contents).Contains(
                "write-to-explicit-path|Warning|Tests.Category|write-to-explicit-path-message"
            );
        }
        finally
        {
            if (File.Exists(filePath))
                File.Delete(filePath);
        }
    }

    [Test]
    public async Task AddLogging_WriteTo_File_WithConfigureDelegate_UsesConfiguredPathAndOptions()
    {
        ISvcContainer container = new SvcContainer();
        var filePath = Path.Combine(Path.GetTempPath(), $"pico-logger-di-{Guid.NewGuid():N}.log");

        try
        {
            container.AddPicoLog(options =>
            {
                options.Formatter = new PrefixFormatter("write-to-configure");
                options.File.BatchSize = 8;
                options.WriteTo.File(file =>
                {
                    file.FilePath = filePath;
                    file.BatchSize = 2;
                    file.FlushInterval = TimeSpan.FromMilliseconds(5);
                });
            });

            await using var scope = container.CreateScope();
            var factory = scope.GetService<ILoggerFactory>();
            var logger = factory.CreateLogger("Tests.Category");

            await logger.WarningAsync("write-to-configure-message");
            await factory.DisposeAsync();

            var contents = await File.ReadAllTextAsync(filePath);
            await Assert.That(contents).Contains(
                "write-to-configure|Warning|Tests.Category|write-to-configure-message"
            );
        }
        finally
        {
            if (File.Exists(filePath))
                File.Delete(filePath);
        }
    }

    [Test]
    public async Task AddLogging_WriteTo_File_WithConfigureDelegate_UsesStableSnapshot_And_RunsOnce()
    {
        ISvcContainer container = new SvcContainer();
        var firstPath = Path.Combine(Path.GetTempPath(), $"pico-logger-di-{Guid.NewGuid():N}-a.log");
        var secondPath = Path.Combine(Path.GetTempPath(), $"pico-logger-di-{Guid.NewGuid():N}-b.log");
        var configuredPath = firstPath;
        var configureCount = 0;

        try
        {
            container.AddPicoLog(options =>
            {
                options.Formatter = new PrefixFormatter("stable-snapshot");
                options.WriteTo.File(file =>
                {
                    Interlocked.Increment(ref configureCount);
                    file.FilePath = configuredPath;
                });
            });

            configuredPath = secondPath;

            await using var scope = container.CreateScope();
            var factory = scope.GetService<ILoggerFactory>();
            var logger = factory.CreateLogger("Tests.Category");

            await logger.WarningAsync("stable-snapshot-message");
            await factory.DisposeAsync();

            await Assert.That(configureCount).IsEqualTo(1);
            await Assert.That(File.Exists(firstPath)).IsTrue();
            await Assert.That(File.Exists(secondPath)).IsFalse();

            var contents = await File.ReadAllTextAsync(firstPath);
            await Assert.That(contents).Contains(
                "stable-snapshot|Warning|Tests.Category|stable-snapshot-message"
            );
        }
        finally
        {
            if (File.Exists(firstPath))
                File.Delete(firstPath);

            if (File.Exists(secondPath))
                File.Delete(secondPath);
        }
    }

    [Test]
    public async Task AddLogging_WriteTo_File_ThrowsWhenFilePathIsNotExplicitlyConfigured()
    {
        ISvcContainer container = new SvcContainer();

        InvalidOperationException? exception = null;

        try
        {
            container.AddPicoLog(options =>
            {
                options.WriteTo.File();
            });
        }
        catch (InvalidOperationException ex)
        {
            exception = ex;
        }

        await Assert.That(exception).IsNotNull();
        await Assert.That(exception!.Message).Contains("explicitly configured FilePath");
    }

    [Test]
    public async Task AddLogging_WriteTo_FlushableCustomSink_ParticipatesInFactoryFlush_WithoutDisposal()
    {
        ISvcContainer container = new SvcContainer();
        var sink = new RecordingFlushableSink();

        container.AddPicoLog(options =>
        {
            options.WriteTo.Sink(sink);
        });

        await using var scope = container.CreateScope();
        var factory = scope.GetService<ILoggerFactory>();
        var control = scope.GetService<IPicoLogControl>();
        var logger = factory.CreateLogger("Tests.Category");

        await logger.InfoAsync("flushable-custom-sink");
        await control.FlushAsync();

        await Assert.That(sink.FlushCallCount).IsEqualTo(1);
        await Assert.That(sink.DisposeCallCount).IsEqualTo(0);

        await control.DisposeAsync();
        await Assert.That(sink.DisposeCallCount).IsEqualTo(1);
    }

    [Test]
    public async Task AddLogging_WriteTo_BuiltInConsoleMethods_CreateExpectedSinkTypes()
    {
        ISvcContainer container = new SvcContainer();

        container.AddPicoLog(options =>
        {
            options.WriteTo.Console();
            options.WriteTo.ColoredConsole();
        });

        await using var scope = container.CreateScope();
        var factory = (LoggerFactory)scope.GetService(typeof(ILoggerFactory));
        var sinksField = typeof(LoggerFactory).GetField(
            "_sinks",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic
        );
        var sinks = (ILogSink[])sinksField!.GetValue(factory)!;

        await Assert.That(sinks).Count().IsEqualTo(2);
        await Assert.That(sinks[0] is ConsoleSink).IsTrue();
        await Assert.That(sinks[1] is ColoredConsoleSink).IsTrue();

        await factory.DisposeAsync();
    }

    [Test]
    public async Task AddLogging_ReadFrom_RegisteredSinks_ResolvesAllRegisteredSinks_InRegistrationOrder()
    {
        ISvcContainer container = new SvcContainer();
        var writes = new ConcurrentQueue<string>();

        container.Register(new SvcDescriptor(typeof(ILogSink), _ => new OrderingSink("first", writes)));
        container.Register(new SvcDescriptor(typeof(ILogSink), _ => new OrderingSink("second", writes)));

        container.AddPicoLog(options =>
        {
            options.ReadFrom.RegisteredSinks();
        });

        await using var scope = container.CreateScope();
        var factory = scope.GetService<ILoggerFactory>();
        var logger = factory.CreateLogger("Tests.Category");

        await logger.InfoAsync("registered-sinks");
        await factory.DisposeAsync();

        await Assert.That(string.Join("|", writes.ToArray())).IsEqualTo("first|second");
    }

    [Test]
    public async Task AddLogging_ReadFrom_RegisteredSinks_DoesNotDispose_DiOwnedSinkInstances()
    {
        ISvcContainer container = new SvcContainer();
        var sink = new RecordingSink();

        container.Register(new SvcDescriptor(typeof(ILogSink), _ => sink));

        container.AddPicoLog(options =>
        {
            options.ReadFrom.RegisteredSinks();
        });

        await using var scope = container.CreateScope();
        var factory = scope.GetService<ILoggerFactory>();
        await factory.DisposeAsync();

        await Assert.That(sink.DisposeCallCount).IsEqualTo(0);
    }

    [Test]
    public async Task AddLogging_ReadFrom_RegisteredSinks_Flushes_FlushableDiSinks_WithoutOwningThem()
    {
        ISvcContainer container = new SvcContainer();
        var sink = new RecordingFlushableSink();

        container.Register(new SvcDescriptor(typeof(ILogSink), _ => sink));

        container.AddPicoLog(options =>
        {
            options.ReadFrom.RegisteredSinks();
        });

        await using var scope = container.CreateScope();
        var factory = scope.GetService<ILoggerFactory>();
        var control = scope.GetService<IPicoLogControl>();
        var logger = factory.CreateLogger("Tests.Category");

        await logger.InfoAsync("flush-registered-sink");
        await control.FlushAsync();

        await Assert.That(sink.FlushCallCount).IsEqualTo(1);
        await Assert.That(sink.DisposeCallCount).IsEqualTo(0);

        await control.DisposeAsync();
        await Assert.That(sink.DisposeCallCount).IsEqualTo(0);
    }

    [Test]
    public async Task AddLogging_ReadFrom_RegisteredSinks_CombinesWith_WriteTo_And_AppendsWriteToAfterRegisteredSinks()
    {
        ISvcContainer container = new SvcContainer();
        var writes = new ConcurrentQueue<string>();

        container.Register(new SvcDescriptor(typeof(ILogSink), _ => new OrderingSink("registered", writes)));

        container.AddPicoLog(options =>
        {
            options.ReadFrom.RegisteredSinks();
            options.WriteTo.Sink(new OrderingSink("explicit", writes));
        });

        await using var scope = container.CreateScope();
        var factory = scope.GetService<ILoggerFactory>();
        var logger = factory.CreateLogger("Tests.Category");

        await logger.WarningAsync("ordered-combination");
        await factory.DisposeAsync();

        await Assert.That(string.Join("|", writes.ToArray())).IsEqualTo("registered|explicit");
    }

    [Test]
    public async Task AddLogging_ReadFrom_RegisteredSinks_ThrowsWhenNoRegisteredSinksExist_AndNoWriteToSinksExist()
    {
        ISvcContainer container = new SvcContainer();

        InvalidOperationException? exception = null;

        try
        {
            container.AddPicoLog(options =>
            {
                options.ReadFrom.RegisteredSinks();
            });

            await using var scope = container.CreateScope();
            _ = scope.GetService<ILoggerFactory>();
        }
        catch (InvalidOperationException ex)
        {
            exception = ex;
        }

        await Assert.That(exception).IsNotNull();
        await Assert.That(exception!.Message).Contains("at least one registered ILogSink");
    }

    [Test]
    public async Task AddLogging_ReadFrom_RegisteredSinks_PropagatesRegisteredSinkActivationFailures()
    {
        ISvcContainer container = new SvcContainer();
        var fallbackSink = new RecordingSink();

        container.Register(
            new SvcDescriptor(
                typeof(ILogSink),
                _ => throw new InvalidOperationException("sink activation failed")
            )
        );

        container.AddPicoLog(options =>
        {
            options.ReadFrom.RegisteredSinks();
            options.WriteTo.Sink(fallbackSink);
        });

        InvalidOperationException? exception = null;

        try
        {
            await using var scope = container.CreateScope();
            _ = scope.GetService<ILoggerFactory>();
        }
        catch (InvalidOperationException ex)
        {
            exception = ex;
        }

        await Assert.That(exception).IsNotNull();
        await Assert.That(exception!.Message).IsEqualTo("sink activation failed");
        await Assert.That(fallbackSink.Entries).Count().IsEqualTo(0);
    }

    [Test]
    public async Task AddLogging_ReadFrom_RegisteredSinks_DoesNotAddLegacyDefaultConsoleSink()
    {
        ISvcContainer container = new SvcContainer();
        var sink = new RecordingSink();

        container.Register(new SvcDescriptor(typeof(ILogSink), _ => sink));

        container.AddPicoLog(options =>
        {
            options.UseColoredConsole = true;
            options.ReadFrom.RegisteredSinks();
        });

        await using var scope = container.CreateScope();
        var factory = scope.GetService<ILoggerFactory>();
        var logger = factory.CreateLogger("Tests.Category");

        await logger.WarningAsync("registered-only");
        await factory.DisposeAsync();

        await Assert.That(sink.Entries.Select(entry => entry.Message ?? string.Empty).ToArray())
            .IsEquivalentTo(["registered-only"]);
    }
}
