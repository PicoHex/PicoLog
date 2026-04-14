namespace PicoLog;

public sealed class LoggerFactory : ILoggerFactory, IDisposable, IAsyncDisposable
{
    private readonly ILogSink[] _sinks;
    private readonly Lock _registrationsLock = new();
    private readonly Dictionary<string, LoggerRegistration> _registrations =
        new(StringComparer.Ordinal);
    private readonly LoggerFactoryRuntime _runtime;

    public LoggerFactory(IEnumerable<ILogSink> sinks)
        : this(sinks, options: null) { }

    public LoggerFactory(IEnumerable<ILogSink> sinks, LoggerFactoryOptions? options)
    {
        _sinks = sinks?.ToArray() ?? throw new ArgumentNullException(nameof(sinks));
        _runtime = new LoggerFactoryRuntime(_sinks, options);
    }

    public LogLevel MinLevel
    {
        get => _runtime.MinLevel;
        set => _runtime.MinLevel = value;
    }

    public ILogger CreateLogger(string categoryName)
    {
        ObjectDisposedException.ThrowIf(!_runtime.IsAcceptingWrites, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(categoryName);

        lock (_registrationsLock)
        {
            ObjectDisposedException.ThrowIf(!_runtime.IsAcceptingWrites, this);

            if (_registrations.TryGetValue(categoryName, out var existingRegistration))
                return existingRegistration.Logger;

            var pipeline = new CategoryPipeline(categoryName, _runtime);
            var logger = new InternalLogger(categoryName, _runtime, pipeline);
            var registration = new LoggerRegistration(logger, pipeline);
            _registrations.Add(categoryName, registration);
            return logger;
        }
    }

    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    public async ValueTask DisposeAsync()
    {
        List<Exception>? exceptions = null;
        var drainStopwatch = Stopwatch.StartNew();
        LoggerRegistration[] registrations;

        lock (_registrationsLock)
        {
            if (!_runtime.TryBeginShutdown())
                return;

            registrations = [.. _registrations.Values];
            _registrations.Clear();
        }

        foreach (var registration in registrations)
        {
            try
            {
                await registration.Pipeline.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                (exceptions ??= []).Add(ex);
            }
        }

        PicoLogMetrics.RecordShutdownDrainDuration(drainStopwatch.Elapsed);

        foreach (var sink in _runtime.Sinks)
        {
            try
            {
                await DisposeSinkAsync(sink).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                (exceptions ??= []).Add(ex);
            }
        }

        if (exceptions is { Count: > 0 })
            throw new AggregateException(exceptions);
    }

    private static async ValueTask DisposeSinkAsync(ILogSink sink)
    {
        if (sink is IAsyncDisposable asyncDisposable)
        {
            await asyncDisposable.DisposeAsync().ConfigureAwait(false);
            return;
        }

        sink.Dispose();
    }

    private sealed class LoggerRegistration(InternalLogger logger, CategoryPipeline pipeline)
    {
        public InternalLogger Logger { get; } = logger;

        public CategoryPipeline Pipeline { get; } = pipeline;
    }
}
