using PicoBench;
using PicoLog;
using PicoLog.Abs;
using PicoLogLevel = PicoLog.Abs.LogLevel;

namespace PicoLog.Benchmarks;

public partial class LoggingBenchmarks
{
    private static readonly string CachedMessage = "Hello from benchmark, cached message";
    private static readonly string OneScopeState = "scope-1";
    private static readonly string TwoScopeState = "scope-2";
    private static readonly DateTimeOffset CachedTimestamp = DateTimeOffset.UnixEpoch;
    private static readonly KeyValuePair<string, object?>[] OnePropertySet = [new("iteration", 42)];
    private static readonly KeyValuePair<string, object?>[] FourPropertySet =
    [
        new("iteration", 42),
        new("node", "edge-a"),
        new("success", true),
        new("elapsedMs", 12.5)
    ];

    private DateTimeOffset _lastTimestamp;
    private LogEntry? _lastEntry;

    [Benchmark]
    public void PicoAsyncHandoff_CachedMessage()
    {
        for (var i = 0; i < N; i++)
            _picoLogger.Log(PicoLogLevel.Info, CachedMessage);
    }

    [Benchmark]
    public void PicoAsyncHandoff_CachedMessage_OneScope()
    {
        using var scope = _picoLogger.BeginScope(OneScopeState);

        for (var i = 0; i < N; i++)
            _picoLogger.Log(PicoLogLevel.Info, CachedMessage);
    }

    [Benchmark]
    public void PicoAsyncHandoff_CachedMessage_TwoScopes()
    {
        using var outerScope = _picoLogger.BeginScope(OneScopeState);
        using var innerScope = _picoLogger.BeginScope(TwoScopeState);

        for (var i = 0; i < N; i++)
            _picoLogger.Log(PicoLogLevel.Info, CachedMessage);
    }

    [Benchmark]
    public void PicoAsyncHandoff_CachedMessage_OneProperty()
    {
        for (var i = 0; i < N; i++)
            _picoLogger.LogStructured(PicoLogLevel.Info, CachedMessage, OnePropertySet);
    }

    [Benchmark]
    public void PicoAsyncHandoff_CachedMessage_FourProperties()
    {
        for (var i = 0; i < N; i++)
            _picoLogger.LogStructured(PicoLogLevel.Info, CachedMessage, FourPropertySet);
    }

    [Benchmark]
    public void TimestampNowOnly()
    {
        for (var i = 0; i < N; i++)
            _lastTimestamp = DateTimeOffset.Now;
    }

    [Benchmark]
    public void LogEntryAllocateOnly()
    {
        for (var i = 0; i < N; i++)
        {
            _lastEntry = new LogEntry
            {
                Timestamp = CachedTimestamp,
                Level = PicoLogLevel.Info,
                Category = "Benchmark",
                Message = CachedMessage,
                Exception = null,
                Scopes = null,
                Properties = null
            };
        }
    }
}
