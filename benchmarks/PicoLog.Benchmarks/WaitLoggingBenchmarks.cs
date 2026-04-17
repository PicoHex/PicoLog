using PicoBench;
using PicoLog;
using PicoLog.Abs;
using PicoLogLevel = PicoLog.Abs.LogLevel;

namespace PicoLog.Benchmarks;

[BenchmarkClass(Description = "PicoLog Wait-mode handoff")]
public sealed partial class WaitLoggingBenchmarks
{
    private const int WaitBenchmarkQueueCapacity = 4;
    private const int WaitSinkSpinIterations = 256;
    private static readonly string CachedMessage = "Hello from benchmark, cached message";

    private PicoLog.Abs.ILogger _picoWaitControlLogger = null!;
    private PicoLog.LoggerFactory _picoWaitControlFactory = null!;
    private PicoLog.Abs.ILogger _picoWaitLogger = null!;
    private PicoLog.LoggerFactory _picoWaitFactory = null!;

    [Params(20)]
    public int N { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _picoWaitControlFactory = new PicoLog.LoggerFactory(
            [new NullSink()],
            new LoggerFactoryOptions
            {
                MinLevel = PicoLogLevel.Trace,
                QueueCapacity = WaitBenchmarkQueueCapacity,
                QueueFullMode = LogQueueFullMode.Wait,
                SyncWriteTimeout = TimeSpan.FromSeconds(30)
            }
        );
        _picoWaitControlLogger = _picoWaitControlFactory.CreateLogger("Benchmark.Wait.Control");

        _picoWaitFactory = new PicoLog.LoggerFactory(
            [new BackpressureSink(WaitSinkSpinIterations)],
            new LoggerFactoryOptions
            {
                MinLevel = PicoLogLevel.Trace,
                QueueCapacity = WaitBenchmarkQueueCapacity,
                QueueFullMode = LogQueueFullMode.Wait,
                SyncWriteTimeout = TimeSpan.FromSeconds(30)
            }
        );
        _picoWaitLogger = _picoWaitFactory.CreateLogger("Benchmark.Wait");
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _picoWaitControlFactory.Dispose();
        _picoWaitFactory.Dispose();
    }

    [Benchmark(Baseline = true)]
    public void PicoWaitControl_CachedMessage()
    {
        for (var i = 0; i < N; i++)
            _picoWaitControlLogger.Log(PicoLogLevel.Info, CachedMessage);
    }

    [Benchmark]
    public void PicoWaitHandoff_CachedMessage()
    {
        for (var i = 0; i < N; i++)
            _picoWaitLogger.Log(PicoLogLevel.Info, CachedMessage);
    }
}
