namespace Pico.Logger.Extensions;

public static class ContainerExtensions
{
    public static ISvcContainer RegisterLogger(this ISvcContainer container) =>
        container
            .RegisterSingle<ILogSink, ConsoleSink>()
            .RegisterSingle<ILogSink, FileSink>()
            .RegisterSingle<ILogFormatter, ConsoleFormatter>()
            .RegisterSingle<ILoggerFactory, LoggerFactory>()
            .RegisterSingle(typeof(ILogger<>), typeof(Logger<>));
}
