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
    private PicoLog.Abs.ILogger _picoLogger = null!;
    private PicoLog.LoggerFactory _picoFactory = null!;
    private Microsoft.Extensions.Logging.ILogger _melLogger = null!;
    private Microsoft.Extensions.Logging.ILoggerFactory _melFactory = null!;

    [Params(1, 10, 100)]
    public int N { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        // PicoLog: use a NullSink so we only measure pipeline overhead.
        _picoFactory = new PicoLog.LoggerFactory(
            [new NullSink()],
            new LoggerFactoryOptions { MinLevel = PicoLogLevel.Trace });
        _picoLogger = _picoFactory.CreateLogger("Benchmark");

        // Microsoft.Extensions.Logging: use a NullLoggerProvider for a fair comparison.
        _melFactory = MelLoggerFactory.Create(builder =>
        {
            builder.SetMinimumLevel(MelLogLevel.Trace);
            builder.AddProvider(new NullLoggerProvider());
        });
        _melLogger = _melFactory.CreateLogger("Benchmark");
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _picoFactory.Dispose();
        _melFactory.Dispose();
    }

    // ── Synchronous logging ────────────────────────────────────────────

    [Benchmark(Baseline = true)]
    public void MicrosoftLogging()
    {
        for (var i = 0; i < N; i++)
            _melLogger.LogInformation("Hello from MEL, iteration {Iteration}", i);
    }

    [Benchmark]
    public void PicoLogging()
    {
        for (var i = 0; i < N; i++)
            _picoLogger.Log(PicoLogLevel.Info, $"Hello from PicoLog, iteration {i}");
    }
}
