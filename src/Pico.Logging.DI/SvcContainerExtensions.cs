namespace Pico.Logging.DI;

public static class SvcContainerExtensions
{
    public static ISvcContainer AddLogging(
        this ISvcContainer container,
        LogLevel minLevel = LogLevel.Debug
    )
    {
        container
            .Register(new SvcDescriptor(
                typeof(ILogFormatter),
                static _ => new ConsoleFormatter(),
                SvcLifetime.Singleton))
            .Register(new SvcDescriptor(
                typeof(IEnumerable<ILogSink>),
                static s => new ILogSink[]
                {
                    new ConsoleSink((ILogFormatter)s.GetService(typeof(ILogFormatter))),
                    new FileSink((ILogFormatter)s.GetService(typeof(ILogFormatter)))
                },
                SvcLifetime.Singleton))
            .Register(new SvcDescriptor(
                typeof(ILoggerFactory),
                s => new LoggerFactory(
                    (IEnumerable<ILogSink>)s.GetService(typeof(IEnumerable<ILogSink>)))
                {
                    MinLevel = minLevel
                },
                SvcLifetime.Singleton))
            .RegisterSingleton(typeof(ILogger<>), typeof(Logger<>));
        return container;
    }
}
