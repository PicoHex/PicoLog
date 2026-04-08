using Microsoft.Extensions.Logging;
using PicoBench;
using PicoLog.Abs;
using MelLoggerFactory = Microsoft.Extensions.Logging.LoggerFactory;
using MelLogLevel = Microsoft.Extensions.Logging.LogLevel;
using PicoLogLevel = PicoLog.Abs.LogLevel;

namespace PicoLog.Benchmarks;

[BenchmarkClass(Description = "PicoLog vs Microsoft.Extensions.Logging")]
public partial class LoggingBenchmarks
{
    private const int BenchmarkQueueCapacity = 262144;
    private static readonly Func<string, Exception?, string> MelStringFormatter = static (state, _) => state;

    private PicoLog.Abs.ILogger _picoLogger = null!;
    private PicoLog.LoggerFactory _picoFactory = null!;
    private Microsoft.Extensions.Logging.ILogger _melSyncLogger = null!;
    private Microsoft.Extensions.Logging.ILoggerFactory _melSyncFactory = null!;
    private Microsoft.Extensions.Logging.ILogger _melAsyncLogger = null!;
    private Microsoft.Extensions.Logging.ILoggerFactory _melAsyncFactory = null!;

    [Params(1, 10, 100)]
    public int N { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _picoFactory = new PicoLog.LoggerFactory(
            [new NullSink()],
            new LoggerFactoryOptions
            {
                MinLevel = PicoLogLevel.Trace,
                QueueCapacity = BenchmarkQueueCapacity
            }
        );
        _picoLogger = _picoFactory.CreateLogger("Benchmark");

        _melSyncFactory = MelLoggerFactory.Create(builder =>
        {
            builder.SetMinimumLevel(MelLogLevel.Trace);
            builder.AddProvider(new NullLoggerProvider());
        });
        _melSyncLogger = _melSyncFactory.CreateLogger("Benchmark");

        _melAsyncFactory = MelLoggerFactory.Create(builder =>
        {
            builder.SetMinimumLevel(MelLogLevel.Trace);
            builder.AddProvider(new AsyncNullLoggerProvider(BenchmarkQueueCapacity));
        });
        _melAsyncLogger = _melAsyncFactory.CreateLogger("Benchmark");
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _picoFactory.Dispose();
        _melSyncFactory.Dispose();
        _melAsyncFactory.Dispose();
    }

    private static string CreateMessage(int iteration) => $"Hello from benchmark, iteration {iteration}";

    private static void LogMelString(Microsoft.Extensions.Logging.ILogger logger, string message) =>
        logger.Log(MelLogLevel.Information, default, message, exception: null, MelStringFormatter);

    [Benchmark]
    public void MicrosoftSyncDiscard()
    {
        for (var i = 0; i < N; i++)
            LogMelString(_melSyncLogger, CreateMessage(i));
    }

    [Benchmark(Baseline = true)]
    public void MicrosoftAsyncHandoff()
    {
        for (var i = 0; i < N; i++)
            LogMelString(_melAsyncLogger, CreateMessage(i));
    }

    [Benchmark]
    public void PicoAsyncHandoff()
    {
        for (var i = 0; i < N; i++)
            _picoLogger.Log(PicoLogLevel.Info, CreateMessage(i));
    }
}
