namespace PicoLog.Tests;

public sealed class SvcContainerExtensionsTests
{
    private sealed class PrefixFormatter(string prefix) : ILogFormatter
    {
        public string Format(LogEntry entry) => $"{prefix}|{entry.Level}|{entry.Category}|{entry.Message}";
    }

    [Test]
    public async Task AddLogging_ReturnsTheSameContainerInstance()
    {
        ISvcContainer container = new SvcContainer();
        var filePath = Path.Combine(Path.GetTempPath(), $"pico-logger-di-{Guid.NewGuid():N}.log");

        try
        {
            var result = container.AddLogging(LogLevel.Info, filePath);

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
            container.AddLogging(LogLevel.Info, " ");
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
            container.AddLogging(options =>
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

        container.AddLogging(options =>
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

        container.AddLogging(options =>
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
            container.AddLogging(options =>
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
            container.AddLogging(options =>
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
            container.AddLogging(options =>
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
    public async Task AddLogging_ResolvesStructuredLogger_And_PreservesProperties()
    {
        ISvcContainer container = new SvcContainer();
        var filePath = Path.Combine(Path.GetTempPath(), $"pico-logger-structured-{Guid.NewGuid():N}.log");

        try
        {
            container.AddLogging(options =>
            {
                options.MinLevel = LogLevel.Info;
                options.UseColoredConsole = false;
                options.FilePath = filePath;
            });

            await using var scope = container.CreateScope();
            var structuredLogger = scope.GetService<IStructuredLogger<LoggerConsumer>>();
            var factory = scope.GetService<ILoggerFactory>();

            structuredLogger.LogStructured(
                LogLevel.Warning,
                "structured-di-message",
                [new("tenant", "alpha"), new("attempt", 3)]
            );
            await factory.DisposeAsync();

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
    public async Task AddLogging_ResolvesFlushableLoggerFactory_AsSameInstanceAsILoggerFactory()
    {
        ISvcContainer container = new SvcContainer();

        container.AddLogging(options =>
        {
            options.MinLevel = LogLevel.Info;
            options.UseColoredConsole = false;
        });

        await using var scope = container.CreateScope();
        var loggerFactory = scope.GetService<ILoggerFactory>();
        var flushableFactory = scope.GetService<IFlushableLoggerFactory>();

        await Assert.That(loggerFactory).IsNotNull();
        await Assert.That(flushableFactory).IsNotNull();
        await Assert.That(flushableFactory).IsSameReferenceAs(loggerFactory);

        await loggerFactory.DisposeAsync();
    }

    [Test]
    public async Task AddLogging_ConfigureOverload_UsesConfiguredFormatter_ForFileSinkOutput()
    {
        ISvcContainer container = new SvcContainer();
        var filePath = Path.Combine(Path.GetTempPath(), $"pico-logger-di-{Guid.NewGuid():N}.log");

        try
        {
            container.AddLogging(options =>
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
            container.AddLogging(options =>
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
            container.AddLogging(options =>
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
}
