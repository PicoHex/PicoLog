// Set up the container and the default logging pipeline.

ISvcContainer container = new SvcContainer();

container
    .AddLogging(options =>
        {
            options.MinLevel = LogLevel.Debug;
            options.UseColoredConsole = true;
        }
    );
container.RegisterScoped<IService, Service>();

await using var scope = container.CreateScope();
var loggerFactory = scope.GetService<ILoggerFactory>();

// Run the sample workload.
var service = scope.GetService<IService>();

await service.WriteLogAsync();

await ((IAsyncDisposable)loggerFactory).DisposeAsync();

// The explicit disposal flushes queued log entries before exit.
