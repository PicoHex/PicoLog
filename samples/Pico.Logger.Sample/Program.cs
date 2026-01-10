// Initialize DI container and configure logging

var container = new SvcContainer();

// Register console logger with application type
container
    .RegisterSingleton<ILogSink, ConsoleSink>()
    .RegisterSingleton<ILogSink, FileSink>()
    .RegisterSingleton<ILogFormatter, ConsoleFormatter>()
    .RegisterSingleton<ILoggerFactory, LoggerFactory>()
    .RegisterSingleton(typeof(ILogger<>), typeof(Logger<>));
container.RegisterScoped<IService, Service>();

await using var scope = container.CreateScope();

// Create typed logger instance
var service = scope.GetService<IService>();

await service.WriteLogAsync();

// Demonstrate basic async logging

Console.ReadKey();
