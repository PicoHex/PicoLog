namespace PicoLog.Tests;

public sealed class SvcContainerExtensionsTests
{
    [Test]
    public async Task AddLogging_ReturnsTheSameContainerInstance()
    {
        ISvcContainer container = new SvcContainer();
        var filePath = Path.Combine(Path.GetTempPath(), $"pico-logger-di-{Guid.NewGuid():N}.log");

        try
        {
            var result = PicoLog
                .DI
                .SvcContainerExtensions
                .AddLogging(container, LogLevel.Info, filePath);

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
            PicoLog.DI.SvcContainerExtensions.AddLogging(container, LogLevel.Info, " ");
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
            PicoLog
                .DI
                .SvcContainerExtensions
                .AddLogging(
                    container,
                    options =>
                    {
                        options.MinLevel = LogLevel.Warning;
                        options.UseColoredConsole = false;
                        options.FilePath = filePath;
                        options.Factory.QueueCapacity = 8;
                        options.Factory.QueueFullMode = LogQueueFullMode.Wait;
                        options.File.BatchSize = 4;
                        options.File.FlushInterval = TimeSpan.FromMilliseconds(5);
                    }
                );

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

        PicoLog.DI.SvcContainerExtensions.AddLogging(
            container,
            options =>
            {
                options.MinLevel = LogLevel.Info;
                options.UseColoredConsole = false;
            }
        );

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

        PicoLog.DI.SvcContainerExtensions.AddLogging(
            container,
            options =>
            {
                options.MinLevel = LogLevel.Info;
            }
        );

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
            PicoLog.DI.SvcContainerExtensions.AddLogging(
                container,
                options =>
                {
                    options.UseColoredConsole = false;
                    options.Factory.MinLevel = LogLevel.Error;
                    options.FilePath = filePath;
                }
            );

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
            PicoLog.DI.SvcContainerExtensions.AddLogging(
                container,
                options =>
                {
                    options.EnableFileSink = true;
                }
            );
        }
        catch (InvalidOperationException ex)
        {
            exception = ex;
        }

        await Assert.That(exception).IsNotNull();
        await Assert.That(exception!.Message).Contains("explicitly configured FilePath");
    }
}
