namespace PicoLog.Tests;

// Extraction guardrail: keep flush as an internal helper-only concern here.
// Do not unify queue-policy semantics or reorder lifecycle steps while sharing coordination logic.
public sealed class LoggerFactoryFlushTests
{
    [Test]
    public async Task FlushAsync_DoesNotDisposeSinks_And_PreservesLoggerCaching()
    {
        var sink = new CollectingFlushableSink();
        ILoggerFactory factory = new LoggerFactory([sink]);

        try
        {
            var firstLogger = factory.CreateLogger("Tests.Category");
            await firstLogger.InfoAsync("before-flush");

            await factory.FlushAsync();

            await Assert.That(sink.DisposeCallCount).IsEqualTo(0);
            await Assert.That(sink.FlushCallCount).IsEqualTo(1);
            await Assert.That(sink.Entries.Select(entry => entry.Message ?? string.Empty).ToArray())
                .IsEquivalentTo(["before-flush"]);

            var secondLogger = factory.CreateLogger("Tests.Category");
            await secondLogger.InfoAsync("after-flush");
            await factory.FlushAsync();

            await Assert.That(firstLogger).IsSameReferenceAs(secondLogger);
            await Assert.That(sink.DisposeCallCount).IsEqualTo(0);
            await Assert.That(sink.FlushCallCount).IsEqualTo(2);
            await Assert.That(sink.Entries.Select(entry => entry.Message ?? string.Empty).ToArray())
                .IsEquivalentTo(["before-flush", "after-flush"]);
        }
        finally
        {
            await factory.DisposeAsync();
        }
    }

    [Test]
    public async Task FlushAsync_WaitsForActiveSinkWrites_And_InvokesOptionalSinkFlush()
    {
        var sink = new CoordinatedFlushableSink();
        ILoggerFactory factory = new LoggerFactory([sink]);

        try
        {
            var logger = factory.CreateLogger("Tests.Category");

            await logger.InfoAsync("payload");
            await sink.WriteStarted;

            var flushTask = factory.FlushAsync().AsTask();

            await Assert.That(flushTask.IsCompleted).IsFalse();
            await Assert.That(sink.FlushCallCount).IsEqualTo(0);

            sink.ReleaseWrite();

            await flushTask;

            await Assert.That(sink.FlushCallCount).IsEqualTo(1);
            await Assert.That(sink.Entries.Select(entry => entry.Message ?? string.Empty).ToArray())
                .IsEquivalentTo(["payload"]);
        }
        finally
        {
            sink.ReleaseWrite();
            await factory.DisposeAsync();
        }
    }

    [Test]
    public async Task FlushAsync_Cancellation_DoesNotLeavePipelineWritesBlocked()
    {
        var sink = new CoordinatedFlushableSink();
        ILoggerFactory factory = new LoggerFactory([sink]);

        try
        {
            var logger = factory.CreateLogger("Tests.Category");

            await logger.InfoAsync("first");
            await sink.WriteStarted;

            using var cancellationSource = new CancellationTokenSource();
            var flushTask = factory.FlushAsync(cancellationSource.Token).AsTask();

            await Assert.That(flushTask.IsCompleted).IsFalse();
            cancellationSource.Cancel();
            sink.ReleaseWrite();

            AggregateException? aggregateException = null;

            try
            {
                await flushTask;
            }
            catch (AggregateException ex)
            {
                aggregateException = ex;
            }
            catch (OperationCanceledException)
            {
                // expected
            }

            if (aggregateException is not null)
            {
                await Assert.That(aggregateException.InnerExceptions.OfType<OperationCanceledException>())
                    .IsNotEmpty();
            }

            await Assert.That(sink.FlushCallCount).IsEqualTo(0);

            await logger.InfoAsync("second");
            await factory.FlushAsync();

            await Assert.That(sink.FlushCallCount).IsEqualTo(1);
            await Assert.That(sink.Entries.Select(entry => entry.Message ?? string.Empty).ToArray())
                .IsEquivalentTo(["first", "second"]);
        }
        finally
        {
            sink.ReleaseWrite();
            await factory.DisposeAsync();
        }
    }

    [Test]
    public async Task FlushAsync_OnEmptyQueues_Completes_And_InvokesOptionalSinkFlush()
    {
        var sink = new CollectingFlushableSink();
        ILoggerFactory factory = new LoggerFactory([sink]);

        try
        {
            _ = factory.CreateLogger("Tests.Category");

            await factory.FlushAsync();

            await Assert.That(sink.FlushCallCount).IsEqualTo(1);
            await Assert.That(sink.DisposeCallCount).IsEqualTo(0);
            await Assert.That(sink.Entries.Select(entry => entry.Message ?? string.Empty).ToArray())
                .Count()
                .IsEqualTo(0);
        }
        finally
        {
            await factory.DisposeAsync();
        }
    }

    [Test]
    public async Task ConcurrentFlushAsync_OnSameFactory_Completes_WithoutDeadlock_And_FlushesEachCall()
    {
        var sink = new CoordinatedFlushableSink();
        ILoggerFactory factory = new LoggerFactory([sink]);

        try
        {
            var logger = factory.CreateLogger("Tests.Category");

            await logger.InfoAsync("payload");
            await sink.WriteStarted;

            var firstFlushTask = factory.FlushAsync().AsTask();
            var secondFlushTask = factory.FlushAsync().AsTask();

            await Assert.That(firstFlushTask.IsCompleted).IsFalse();
            await Assert.That(secondFlushTask.IsCompleted).IsFalse();

            sink.ReleaseWrite();

            await Task.WhenAll(firstFlushTask, secondFlushTask).WaitAsync(TimeSpan.FromSeconds(5));

            await Assert.That(sink.FlushCallCount).IsEqualTo(2);
            await Assert.That(sink.Entries.Select(entry => entry.Message ?? string.Empty).ToArray())
                .IsEquivalentTo(["payload"]);
        }
        finally
        {
            sink.ReleaseWrite();
            await factory.DisposeAsync();
        }
    }

    [Test]
    public async Task FlushAsync_And_DisposeAsync_Race_Completes_WithoutDeadlock_WhenFlushAlreadyStarted()
    {
        var sink = new CoordinatedFlushableSink();
        ILoggerFactory factory = new LoggerFactory([sink]);

        try
        {
            var logger = factory.CreateLogger("Tests.Category");

            await logger.InfoAsync("payload");
            await sink.WriteStarted;

            var flushTask = factory.FlushAsync().AsTask();
            var disposeTask = factory.DisposeAsync().AsTask();

            await Assert.That(flushTask.IsCompleted).IsFalse();
            await Assert.That(disposeTask.IsCompleted).IsFalse();

            sink.ReleaseWrite();

            await Task.WhenAll(flushTask, disposeTask).WaitAsync(TimeSpan.FromSeconds(5));

            await Assert.That(sink.FlushCallCount).IsEqualTo(1);
            await Assert.That(sink.DisposeCallCount).IsEqualTo(1);
            await Assert.That(sink.Entries.Select(entry => entry.Message ?? string.Empty).ToArray())
                .IsEquivalentTo(["payload"]);
        }
        finally
        {
            sink.ReleaseWrite();
            await factory.DisposeAsync();
        }
    }

    [Test]
    public async Task FlushAsync_PersistsTailMessages_ToFileSink_WithoutDisposal()
    {
        var filePath = Path.Combine(Path.GetTempPath(), $"pico-logger-flush-{Guid.NewGuid():N}.log");
        ILoggerFactory? factory = null;

        try
        {
            factory = new LoggerFactory(
                [
                    new FileSink(
                        new ConsoleFormatter(),
                        new FileSinkOptions
                        {
                            FilePath = filePath,
                            BatchSize = 32,
                            FlushInterval = TimeSpan.FromMinutes(5)
                        }
                    )
                ]
            );
            var logger = factory.CreateLogger("Tests.Category");

            await logger.InfoAsync("tail-before-dispose");
            await factory.FlushAsync();

            await Assert.That(File.Exists(filePath)).IsTrue();
            var contents = await ReadAllTextWithSharedReadWriteAsync(filePath);
            await Assert.That(contents).Contains("tail-before-dispose");
        }
        finally
        {
            if (factory is not null)
                await factory.DisposeAsync();

            if (File.Exists(filePath))
                File.Delete(filePath);
        }
    }

    [Test]
    public async Task FileSinkFlushAsync_PersistsQueuedMessages_WithoutDisposal()
    {
        var filePath = Path.Combine(
            Path.GetTempPath(),
            $"pico-file-sink-flush-{Guid.NewGuid():N}.log"
        );
        ILogSink? sink = null;

        try
        {
            sink = new FileSink(
                new ConsoleFormatter(),
                new FileSinkOptions
                {
                    FilePath = filePath,
                    BatchSize = 32,
                    FlushInterval = TimeSpan.FromMinutes(5)
                }
            );

            await sink.WriteAsync(
                new LogEntry
                {
                    Timestamp = DateTimeOffset.UtcNow,
                    Level = LogLevel.Info,
                    Category = nameof(LoggerFactoryFlushTests),
                    Message = "sink-tail-before-dispose"
                }
            );

            await sink.FlushAsync();

            await Assert.That(File.Exists(filePath)).IsTrue();
            var contents = await ReadAllTextWithSharedReadWriteAsync(filePath);
            await Assert.That(contents).Contains("sink-tail-before-dispose");
        }
        finally
        {
            if (sink is not null)
                await sink.DisposeAsync();

            if (File.Exists(filePath))
                File.Delete(filePath);
        }
    }

    private sealed class CollectingFlushableSink : IFlushableLogSink
    {
        private readonly ConcurrentQueue<LogEntry> _entries = [];
        private int _flushCallCount;
        private int _disposeCallCount;

        public IReadOnlyCollection<LogEntry> Entries => _entries.ToArray();

        public int FlushCallCount => Volatile.Read(ref _flushCallCount);

        public int DisposeCallCount => Volatile.Read(ref _disposeCallCount);

        public Task WriteAsync(LogEntry entry, CancellationToken cancellationToken = default)
        {
            _entries.Enqueue(entry);
            return Task.CompletedTask;
        }

        public ValueTask FlushAsync(CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _flushCallCount);
            return ValueTask.CompletedTask;
        }

        public void Dispose() => Interlocked.Increment(ref _disposeCallCount);

        public ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref _disposeCallCount);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class CoordinatedFlushableSink : IFlushableLogSink
    {
        private readonly ConcurrentQueue<LogEntry> _entries = [];
        private readonly TaskCompletionSource _writeStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _releaseWrite =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _flushCallCount;
        private int _disposeCallCount;

        public IReadOnlyCollection<LogEntry> Entries => _entries.ToArray();

        public Task WriteStarted => _writeStarted.Task;

        public int FlushCallCount => Volatile.Read(ref _flushCallCount);

        public int DisposeCallCount => Volatile.Read(ref _disposeCallCount);

        public async Task WriteAsync(LogEntry entry, CancellationToken cancellationToken = default)
        {
            _entries.Enqueue(entry);
            _writeStarted.TrySetResult();
            await _releaseWrite.Task.WaitAsync(cancellationToken);
        }

        public void ReleaseWrite() => _releaseWrite.TrySetResult();

        public ValueTask FlushAsync(CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _flushCallCount);
            return ValueTask.CompletedTask;
        }

        public void Dispose() => Interlocked.Increment(ref _disposeCallCount);

        public ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref _disposeCallCount);
            return ValueTask.CompletedTask;
        }
    }

    private static async Task<string> ReadAllTextWithSharedReadWriteAsync(string filePath)
    {
        await using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite,
            bufferSize: 4096,
            useAsync: true
        );
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync();
    }
}
