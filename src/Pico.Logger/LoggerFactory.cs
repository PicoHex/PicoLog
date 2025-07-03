namespace Pico.Logger;

public sealed class LoggerFactory(IEnumerable<ILogSink> sinks) : ILoggerFactory
{
    public LogLevel MinLevel { get; set; } = LogLevel.Debug;

    public ILogger CreateLogger(string categoryName) =>
        new InternalLogger(categoryName, sinks, this);
}
