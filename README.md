# PicoLog

[English](README.md) | [简体中文](README.zh.md) | [繁體中文](README.zh-TW.md) | [Deutsch](README.de.md) | [Español](README.es.md) | [Français](README.fr.md) | [日本語](README.ja.md) | [Português (Brasil)](README.pt-BR.md) | [Русский](README.ru.md)

![CI](https://github.com/PicoHex/PicoLog/actions/workflows/ci.yml/badge.svg)
[![NuGet](https://img.shields.io/nuget/v/PicoLog.svg)](https://www.nuget.org/packages/PicoLog)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

A lightweight, AOT-friendly logging framework for .NET edge and IoT workloads. The repository contains contracts, the core logger implementation, PicoDI integration, a sample app, and a dedicated benchmark project.

## Features

- **AOT compatibility**: targets `net10.0` and avoids reflection-heavy infrastructure
- **Bounded async pipeline**: loggers enqueue entries into a bounded channel and write to sinks on a background task
- **Explicit flush companions**: `IFlushableLoggerFactory`, `IFlushableLogSink`, and best-effort `FlushAsync()` extensions make mid-run flush barriers explicit without conflating them with shutdown
- **Structured properties**: optional key/value payloads flow through `LogEntry.Properties` and the built-in formatter output
- **Built-in metrics**: the core package emits queue, drop, sink-failure, and shutdown metrics through `System.Diagnostics.Metrics`
- **Minimal surface area**: only a few core abstractions need to be implemented to extend the system
- **PicoDI integration**: built-in registrations for the logger factory and typed loggers, with `WriteTo` sink configuration and optional `ReadFrom.RegisteredSinks()` bridging for PicoDI-registered sinks
- **Benchmark coverage**: includes a PicoBench-based benchmark project with lightweight and fairer MEL async handoff baselines
- **Scope support**: nested scopes flow through `AsyncLocal` and are attached to each `LogEntry`

## Project Structure

```text
PicoLog/
├── src/
│   ├── PicoLog.Abs/       # Abstract interfaces and contracts
│   ├── PicoLog/           # Core logging implementation
│   └── PicoLog.DI/        # Dependency injection integration
├── benchmarks/
│   └── PicoLog.Benchmarks/# PicoBench-based benchmark project
├── samples/
│   └── PicoLog.Sample/    # Example usage
└── tests/                # Test projects
```

## Installation

### Core Logging Library

```bash
dotnet add package PicoLog
```

### PicoDI Integration

```bash
dotnet add package PicoLog.DI
```

## Quick Start

### Basic Usage

```csharp
using PicoLog;
using PicoLog.Abs;

var formatter = new ConsoleFormatter();
using var consoleSink = new ConsoleSink(formatter);
await using var fileSink = new FileSink(formatter, "logs/app.log");
await using var loggerFactory = new LoggerFactory([consoleSink, fileSink])
{
    MinLevel = LogLevel.Info
};

var logger = new Logger<MyService>(loggerFactory);

logger.Info("Application starting");
logger.Warning("This is a warning");
await logger.ErrorAsync("Error occurred", new InvalidOperationException("Something went wrong"));
logger.LogStructured(
    LogLevel.Info,
    "Request completed",
    new KeyValuePair<string, object?>[]
    {
        new("requestId", "req-42"),
        new("statusCode", 200),
        new("elapsedMs", 18.7)
    }
);

await loggerFactory.FlushAsync(); // barrier for entries accepted so far, not shutdown
```

`FlushAsync()` on the factory is not disposal. It waits until entries accepted before the flush snapshot have crossed the factory pipeline, while `DisposeAsync()` remains the shutdown path for the final drain and resource release.

### DI Integration

```csharp
using PicoDI;
using PicoDI.Abs;
using PicoLog;
using PicoLog.Abs;
using PicoLog.DI;

ISvcContainer container = new SvcContainer();

container.AddPicoLog(options =>
{
    options.MinLevel = LogLevel.Info;
    options.Factory.QueueFullMode = LogQueueFullMode.Wait;
    options.File.BatchSize = 64;
    options.WriteTo.ColoredConsole();
    options.WriteTo.File("logs/app.log");
});
container.RegisterScoped<IMyService, MyService>();

await using var scope = container.CreateScope();

var service = scope.GetService<IMyService>();
var logger = scope.GetService<ILogger<MyService>>();
var logControl = scope.GetService<IPicoLogControl>();
await service.DoWorkAsync();

logger.LogStructured(
    LogLevel.Info,
    "DI structured event",
    [new("tenant", "alpha"), new("attempt", 3)]
);

await logControl.FlushAsync();
await logControl.DisposeAsync();
```

`AddPicoLog()` is the focused DI-first entry point. It keeps the default container surface small: `ILogger<T>` for writes and `IPicoLogControl` for explicit flush/shutdown.

`PicoLog.Abs` is the consumer-facing contract package. `PicoLog` owns runtime/extensibility contracts such as sinks, formatters, flush companions, and `LogEntry`, so application code can stay focused on `ILogger<T>` and `IPicoLogControl` while extension authors target the main runtime package directly.

## Configuration

### Minimum Level

`LoggerFactory.MinLevel` controls which entries are accepted. Lower numeric values are more severe, so the default `Debug` level allows `Emergency` through `Debug`, but filters `Trace`.

```csharp
await using var loggerFactory = new LoggerFactory(sinks)
{
    MinLevel = LogLevel.Warning
};
```

### Lifecycle Ownership

`LoggerFactory` owns cached per-category loggers, their per-category pipelines, the background drain tasks those pipelines run, and the registered sinks. `FlushAsync()` is a mid-run barrier, not disposal. It waits for entries accepted before the flush snapshot to cross the factory pipeline. Use `DisposeAsync()` during shutdown for the final drain and sink resource release. Once disposal begins, new writes are rejected while already queued entries continue draining.

```csharp
await using var loggerFactory = new LoggerFactory(sinks);

var logger = new Logger<MyService>(loggerFactory);
logger.Info("Starting up");
await loggerFactory.FlushAsync();

// FlushAsync is a barrier for accepted entries so far.
// DisposeAsync performs final drain and rejects writes that race with shutdown.
```

### Queue Pressure

`LoggerFactoryOptions.QueueFullMode` makes queue pressure handling explicit for both sync and async writes. `LogAsync()` and `LogStructuredAsync()` complete when logger-boundary handoff handling has finished there: the entry may have been accepted, dropped by queue policy, or rejected during shutdown, and `Wait` mode backpressure is accounted for at that boundary. They do not mean a sink has durably finished writing.

- `DropOldest` keeps logging non-blocking by discarding the oldest queued entry. This is the default.
- `DropWrite` rejects the new entry and reports the drop through `OnMessagesDropped`.
- `Wait` blocks sync logging up to `SyncWriteTimeout` and makes async logging await queue space until cancellation is requested.

```csharp
var options = new LoggerFactoryOptions
{
    MinLevel = LogLevel.Info,
    QueueFullMode = LogQueueFullMode.Wait,
    SyncWriteTimeout = TimeSpan.FromMilliseconds(500)
};

await using var loggerFactory = new LoggerFactory(sinks, options);
```

### Built-in Sinks

- `ConsoleSink` writes plain formatted entries to standard output.
- `ColoredConsoleSink` serializes color changes so console state does not leak across concurrent writes.
- `FileSink` batches UTF-8 file writes on a background queue before flushing to disk and supports sink-level flush through `IFlushableLogSink`. `AddLogging()` creates configured sinks inside the logger factory so the factory remains the single owner of their lifetime.

### PicoDI Defaults

`AddPicoLog()` registers:

- a singleton `ILoggerFactory`
- a singleton `IPicoLogControl`
- typed `ILogger<T>` adapters
- legacy default sinks when no explicit sink pipeline is configured
- optional DI-registered sinks when `ReadFrom.RegisteredSinks()` is enabled

During the app lifetime, call `IPicoLogControl.FlushAsync()` when you need an explicit barrier for already accepted entries. When shutting down, dispose the resolved control so queued log entries are drained before process exit. Writes that arrive after shutdown begins are rejected instead of being accepted late.

For new code, prefer the `WriteTo` sink builder so built-in and custom sinks share the same configuration path.

```csharp
container.AddPicoLog(options =>
{
    options.MinLevel = LogLevel.Info;
    options.WriteTo.ColoredConsole();
    options.WriteTo.File("logs/app.log");
});
```

You can also bridge sinks already registered in PicoDI by enabling `ReadFrom.RegisteredSinks()`.

```csharp
container.Register(new SvcDescriptor(typeof(ILogSink), _ => new AuditSink()));

container.AddPicoLog(options =>
{
    options.ReadFrom.RegisteredSinks();
    options.WriteTo.ColoredConsole();
});
```

### Built-in Formatter

`ConsoleFormatter` produces human-readable lines with timestamp, level, category, message, optional structured properties, exception text, and optional scopes.

```csharp
var formatter = new ConsoleFormatter();
var sink = new ConsoleSink(formatter);
```

### Structured Logging

PicoLog keeps `ILogger` compatible and exposes guaranteed structured logging through `IStructuredLogger` / `IStructuredLogger<T>`. When you use the built-in `LoggerFactory`, the runtime logger preserves `LogStructured()` / `LogStructuredAsync()` payloads on `LogEntry.Properties`.

```csharp
logger.LogStructured(
    LogLevel.Warning,
    "Cache miss",
    new KeyValuePair<string, object?>[]
    {
        new("cacheKey", "user:42"),
        new("node", "edge-a"),
        new("attempt", 3)
    }
);
```

`ConsoleFormatter` appends structured properties in a compact textual form after the message, for example:

```text
[2026-04-05 11:30:42.123] WARNING   [MyService] Cache miss {cacheKey="user:42", node="edge-a", attempt=3}
```

### Logging Extensions

The shipped extension methods are defined on `ILogger` and `ILogger<T>`:

- `Trace`, `Debug`, `Info`, `Notice`, `Warning`, `Error`, `Critical`, `Alert`, `Emergency`
- Async counterparts such as `InfoAsync` and `ErrorAsync`
- `LogStructured` and `LogStructuredAsync` as best-effort adapters that preserve properties when the runtime logger implements `IStructuredLogger`, and otherwise fall back to plain logging without structured payloads
- Best-effort `FlushAsync()` extensions on `ILoggerFactory` and `ILogSink` that forward when the runtime type supports flushing and otherwise complete immediately

If you need a strict structured-logging contract, depend on `IStructuredLogger` / `IStructuredLogger<T>` directly. If you need a strict flush contract, depend on `IFlushableLoggerFactory` or `IFlushableLogSink` directly.

### Overflow Behavior

Both sync and async logging write into a bounded channel.

- Sync `Log()` and async `LogAsync()` both follow `LoggerFactoryOptions.QueueFullMode`.
- Completion of `LogAsync()` or `LogStructuredAsync()` means logger-boundary handoff handling has finished there. The entry may have been accepted, dropped by queue policy, or rejected during shutdown. It does not mean durable sink completion.
- The default is `DropOldest`, which favors throughput and low caller latency over guaranteed delivery.
- `Wait` makes backpressure visible to the caller by blocking sync writes until queue space becomes available or `SyncWriteTimeout` elapses, and by awaiting queue space for async writes until cancellation is requested.
- `DropWrite` preserves older queued entries and reports dropped new entries through `OnMessagesDropped`.
- Once factory disposal begins, new writes are rejected while already queued entries continue flushing.

For general application logging, the default `DropOldest` behavior is usually acceptable. For audit-style logging, prefer `Wait` or a dedicated sink strategy.

### Built-in Metrics

The core `PicoLog` package emits a small built-in metrics surface through `System.Diagnostics.Metrics` using meter name `PicoLog`.

- `picolog.entries.enqueued`
- `picolog.entries.dropped`
- `picolog.sinks.failures`
- `picolog.writes.rejected_after_shutdown`
- `picolog.queue.entries`
- `picolog.shutdown.drain.duration`

These instruments are designed to stay low-cardinality and lightweight. They can be observed with `MeterListener` directly or bridged into broader telemetry infrastructure.

```csharp
using var listener = new MeterListener();

listener.InstrumentPublished = (instrument, meterListener) =>
{
    if (instrument.Meter.Name == PicoLogMetrics.MeterName)
        meterListener.EnableMeasurementEvents(instrument);
};

listener.Start();
```

## AOT Compatibility

The sample project publishes with Native AOT enabled:

```xml
<PropertyGroup>
  <PublishAOT>true</PublishAOT>
</PropertyGroup>
```

```bash
dotnet publish -c Release -r win-x64 -p:PublishAOT=true
```

The repository also includes a publish-level validation script that publishes the sample, runs the generated executable, and verifies the final shutdown log entries were flushed correctly:

```powershell
./scripts/Validate-AotSample.ps1
```

## Building from Source

### Prerequisites

- .NET SDK 10.0 or later
- Git

### Clone and Build

```bash
git clone https://github.com/PicoHex/PicoLog.git
cd PicoLog
dotnet restore
dotnet build --configuration Release
```

### Run Tests

```bash
dotnet test --solution ./PicoLog.slnx --configuration Release
```

## Performance Considerations

- Logger instances are cached per category inside `LoggerFactory`.
- LoggerFactory owns one bounded channel, one category pipeline, and one background drain task per category.
- `FlushAsync()` on the factory is a barrier for entries accepted before the flush snapshot, not a disposal shortcut.
- Factory disposal still performs the final drain before disposing sinks.
- `FileSink` batches writes on its own bounded queue, flushes at batch boundaries or flush-interval boundaries, and exposes sink-level flush through `IFlushableLogSink`.
- Choosing `DropOldest`, `DropWrite`, or `Wait` is a throughput-vs-delivery tradeoff, not a correctness bug.

## Benchmarks

The repository includes `benchmarks/PicoLog.Benchmarks`, a PicoBench-based benchmark project for comparing PicoLog handoff costs against Microsoft.Extensions.Logging baselines.

- `MicrosoftAsyncHandoff` is the lightweight string-channel MEL baseline.
- `MicrosoftAsyncEntryHandoff` is the fairer full-entry MEL baseline that mirrors PicoLog's timestamp/category/message envelope cost without adding real I/O.
- `PicoWaitControl_CachedMessage` and `PicoWaitHandoff_CachedMessage` cover PicoLog wait-mode backpressure as a relative comparison.

Run the benchmark project:

```bash
dotnet run -c Release --project benchmarks/PicoLog.Benchmarks
```

Or publish and execute the artifact directly:

```bash
dotnet publish benchmarks/PicoLog.Benchmarks/PicoLog.Benchmarks.csproj -c Release -r win-x64
benchmarks/PicoLog.Benchmarks/bin/Release/net10.0/win-x64/publish/PicoLog.Benchmarks.exe main
```

The benchmark app writes:

- `benchmark-results.md`
- `benchmark-results-main.md`
- `benchmark-results-wait.md`

## Fit and Non-Goals

### Strengths

- The core implementation is small and easy to reason about: `LoggerFactory` owns per-category logger registrations, category pipelines, drain-task lifetimes, and sink lifetimes, while each `InternalLogger` remains a lightweight non-owning write facade.
- The project is AOT-friendly and avoids reflection-heavy infrastructure, which makes it a good fit for Native AOT, edge, and IoT workloads.
- Structured properties and built-in metrics cover common operational needs without forcing a larger logging ecosystem into the application.
- Queue pressure behavior is explicit rather than hidden. Callers can choose between `DropOldest`, `DropWrite`, and `Wait` depending on whether throughput or delivery matters more.
- Flush semantics stay explicit. `FlushAsync()` is a barrier for already accepted work, while `DisposeAsync()` remains the shutdown path for final drain and resource release.
- The built-in PicoDI integration stays thin and predictable instead of introducing a large hosting or configuration stack.

### Good Fit

- Small to medium .NET applications that want a lightweight logging core without adopting a larger logging ecosystem.
- Edge, IoT, desktop, and utility-style workloads where startup cost, binary size, and AOT compatibility matter.
- Application logging scenarios where best-effort delivery is acceptable, mid-run flush barriers are occasionally useful, and explicit shutdown draining is enough.
- Teams that prefer a small set of primitives and are comfortable adding custom sinks or formatters as needed.

### Non-Goals and Weak Spots

- This is not a full observability platform. It supports structured properties and a small built-in metrics surface, but it does not provide message-template parsing, enrichers, rolling file management, remote transport sinks, or deep integration with broader telemetry ecosystems.
- It is not optimized for very high-cardinality logger categories. The current design creates one factory-owned category pipeline and one background drain task per category.
- It is not a default fit for audit or compliance logging where silent loss is unacceptable. The default queue mode favors throughput, `LogAsync()` completion is not durable sink completion, and stronger delivery guarantees require explicit configuration such as `Wait` or a dedicated sink strategy.
- Built-in metrics cover queue depth, accepted and dropped entries, sink failures, late writes during shutdown, and shutdown drain duration. They do not yet expose per-category metrics, sink latency histograms, or a larger end-to-end telemetry model.

## Extending PicoLog

### Custom Sink

```csharp
public sealed class CustomSink : ILogSink, IFlushableLogSink
{
    public Task WriteAsync(LogEntry entry, CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"CUSTOM: {entry.Message}");
        return Task.CompletedTask;
    }

    public ValueTask FlushAsync(CancellationToken cancellationToken = default)
        => ValueTask.CompletedTask;

    public void Dispose() { }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

var formatter = new ConsoleFormatter();
await using var loggerFactory = new LoggerFactory([new CustomSink(), new FileSink(formatter)]);
await loggerFactory.FlushAsync();
```

If a sink does not implement `IFlushableLogSink`, the `ILogSink.FlushAsync()` extension is best-effort and completes immediately.

### Custom Formatter

```csharp
public class CustomFormatter : ILogFormatter
{
    public string Format(LogEntry entry)
    {
        return $"{entry.Timestamp:yyyy-MM-dd HH:mm:ss} [{entry.Level}] {entry.Message}";
    }
}
```

## Tests

The test suite currently covers:

- file sink writes racing with async disposal
- logger caching by category
- minimum-level filtering
- structured payload capture and formatting
- built-in metrics emission
- scope capture and flush on factory disposal
- flush barriers for accepted entries and best-effort flush extensions
- rejecting writes after shutdown begins
- sink failure isolation
- async tail-message flushing
- real file sink tail persistence
- configured DI file output
- PicoDI typed logger resolution

The sample also gets verified through Native AOT publish and execution.

## Contributing

1. Fork the repository
2. Create a feature branch
3. Make your changes
4. Add or update tests
5. Submit a pull request

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.
