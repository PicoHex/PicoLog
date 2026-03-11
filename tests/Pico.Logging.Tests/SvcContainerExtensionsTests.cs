namespace Pico.Logging.Tests;

public sealed class SvcContainerExtensionsTests
{
    [Test]
    public async Task AddLogging_ReturnsTheSameContainerInstance()
    {
        var container = new SvcContainer();
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
    public async Task AddLogging_ThrowsWhenResolvedWithBlankFilePath()
    {
        var container = new SvcContainer();
        container.AddLogging(LogLevel.Info, " ");

        await using var scope = container.CreateScope();

        ArgumentException? exception = null;

        try
        {
            _ = scope.GetService<ILoggerFactory>();
        }
        catch (ArgumentException ex)
        {
            exception = ex;
        }

        await Assert.That(exception).IsNotNull();
        await Assert.That(exception!.ParamName).IsEqualTo("filePath");
    }
}
