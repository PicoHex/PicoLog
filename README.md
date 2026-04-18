# PicoLog

[English](README.md) | [简体中文](README.zh.md) | [繁體中文](README.zh-TW.md) | [Deutsch](README.de.md) | [Español](README.es.md) | [Français](README.fr.md) | [日本語](README.ja.md) | [Português (Brasil)](README.pt-BR.md) | [Русский](README.ru.md)

![CI](https://github.com/PicoHex/PicoLog/actions/workflows/ci.yml/badge.svg)
[![NuGet](https://img.shields.io/nuget/v/PicoLog.svg)](https://www.nuget.org/packages/PicoLog)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

PicoLog is a lightweight, AOT-friendly logging framework for .NET edge, desktop, utility, and IoT workloads.

The current design is intentionally small:

- **one logger model**: `ILogger` / `ILogger<T>`
- **one DI entrypoint**: `AddPicoLog(...)`
- **one lifecycle owner**: `ILoggerFactory`

Structured properties are part of the log event itself, not a separate logger type. Runtime and extensibility types such as sinks, formatters, `LogEntry`, and flush companions live in `PicoLog`, while consumer-facing contracts live in `PicoLog.Abs`.

## Features

- **AOT-friendly design**: avoids reflection-heavy infrastructure and ships with a Native AOT sample
- **Bounded async pipeline**: loggers hand off entries into per-category pipelines backed by bounded channels
- **Explicit lifecycle semantics**: `FlushAsync()` is a mid-run barrier; `DisposeAsync()` is shutdown drain
- **Structured properties on `ILogger`**: native overloads preserve key/value payloads on `LogEntry.Properties`
- **Small DI surface**: `AddPicoLog(...)` registers `ILoggerFactory` and typed `ILogger<T>` adapters
- **Built-in sinks and formatter**: console, colored console, file, and a readable text formatter
- **Flush companion contracts**: runtime flush capabilities remain available through `IFlushableLoggerFactory` and `IFlushableLogSink`
- **Built-in metrics**: queue, drop, sink-failure, and shutdown metrics via `System.Diagnostics.Metrics`
- **Benchmark project**: PicoBench-based benchmarks for PicoLog handoff costs and MEL baselines
- **Scope support**: nested scopes flow through `AsyncLocal` and are attached to each `LogEntry`

## Project Structure

```text
PicoLog/
├── src/
│   ├── PicoLog.Abs/        # Consumer-facing contracts (ILogger, ILogger<T>, ILoggerFactory, LogLevel)
│   ├── PicoLog/            # Runtime implementation and extensibility contracts
│   └── PicoLog.DI/         # PicoDI integration via AddPicoLog(...)
├── benchmarks/
│   └── PicoLog.Benchmarks/ # PicoBench-based benchmark project
├── samples/
│   └── PicoLog.Sample/     # End-to-end sample app
└── tests/                  # Test projects
```

## Installation

### Core Runtime

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
logger.Warning("Configuration file is missing an optional section");

logger.Log(
    LogLevel.Info,
    "Request completed",
    [
        new("requestId", "req-42"),
        new("statusCode", 200),
        new("elapsedMs", 18.7)
    ],
    exception: null
);

await logger.ErrorAsync(
    "Error occurred",
    new InvalidOperationException("Something went wrong")
);

await loggerFactory.FlushAsync();
```

`FlushAsync()` is **not** disposal. It is a barrier for entries that were already accepted before the flush snapshot. Use `DisposeAsync()` for final shutdown drain and sink cleanup.

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
var loggerFactory = scope.GetService<ILoggerFactory>();

await service.DoWorkAsync();

logger.Log(
    LogLevel.Info,
    "DI structured event",
    [new("tenant", "alpha"), new("attempt", 3)],
    exception: null
);

await loggerFactory.FlushAsync();
await loggerFactory.DisposeAsync();
```

`AddPicoLog()` is the only DI entrypoint. Business code should normally depend on `ILogger<T>`. `ILoggerFactory` is the explicit lifecycle owner at the app root for flush and shutdown.

## Core Model

### One Logger Model

PicoLog no longer splits logging into “plain” and “structured” logger interfaces.

- `ILogger` / `ILogger<T>` is the main write surface
- plain events use `Log(level, message, exception?)`
- structured events use `Log(level, message, properties, exception)`
- async variants follow the same shape through `LogAsync(...)`

`LogStructured()` and `LogStructuredAsync()` still exist as convenience wrappers in `LoggerExtensions`, but they are just sugar over the native `ILogger` overloads.

### Package Split

- **`PicoLog.Abs`**: consumer-facing contracts such as `ILogger`, `ILogger<T>`, `ILoggerFactory`, `LogLevel`, and `LoggerExtensions`
- **`PicoLog`**: runtime and extensibility types such as `LoggerFactory`, `Logger<T>`, `LogEntry`, `ILogSink`, `ILogFormatter`, `IFlushableLoggerFactory`, and `IFlushableLogSink`
- **`PicoLog.DI`**: PicoDI integration via `AddPicoLog(...)`

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

`LoggerFactory` owns:

- cached per-category loggers
- per-category pipelines
- background drain tasks
- registered sinks

That means lifecycle APIs belong on the factory story, not on `ILogger<T>`.

```csharp
await using var loggerFactory = new LoggerFactory(sinks);

var logger = new Logger<MyService>(loggerFactory);
logger.Info("Starting up");

await loggerFactory.FlushAsync();   // mid-run barrier
await loggerFactory.DisposeAsync(); // final shutdown drain
```

If you resolve `ILoggerFactory` from DI, remember it is still the app-level singleton lifecycle owner. Resolving it from a scope does **not** make it scope-owned.

### Queue Pressure

`LoggerFactoryOptions.QueueFullMode` makes queue pressure explicit for both sync and async writes.

Completion of `LogAsync()` means logger-boundary handoff handling has finished there. The entry may have been:

- accepted
- dropped by queue policy
- rejected during shutdown

It does **not** mean a sink has durably finished writing.

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

## Built-in Runtime Pieces

### Sinks

- `ConsoleSink` writes plain formatted entries to standard output.
- `ColoredConsoleSink` serializes color changes so console state does not leak across concurrent writes.
- `FileSink` batches UTF-8 file writes on a background queue before flushing to disk and supports sink-level flush through `IFlushableLogSink`.

When using `AddPicoLog()`, configured sinks are created inside the logger factory, so the factory remains the single owner of their lifetime.

### Formatter

`ConsoleFormatter` produces readable lines with timestamp, level, category, message, optional structured properties, exception text, and optional scopes.

```csharp
var formatter = new ConsoleFormatter();
var sink = new ConsoleSink(formatter);
```

### Structured Logging

Structured data is part of the log event itself.

```csharp
logger.Log(
    LogLevel.Warning,
    "Cache miss",
    new KeyValuePair<string, object?>[]
    {
        new("cacheKey", "user:42"),
        new("node", "edge-a"),
        new("attempt", 3)
    },
    exception: null
);
```

Those properties are preserved on `LogEntry.Properties`, and sinks or formatters decide how to consume them.

For example, `ConsoleFormatter` appends them in a compact textual form:

```text
[2026-04-05 11:30:42.123] WARNING   [MyService] Cache miss {cacheKey="user:42", node="edge-a", attempt=3}
```

## Logging Extensions

The shipped extension methods are defined on `ILogger` and `ILogger<T>`:

- `Trace`, `Debug`, `Info`, `Notice`, `Warning`, `Error`, `Critical`, `Alert`, `Emergency`
- async counterparts such as `InfoAsync` and `ErrorAsync`
- `LogStructured` and `LogStructuredAsync` as convenience wrappers over the native property-aware `ILogger` overloads
- best-effort `FlushAsync()` extensions on `ILoggerFactory` and `ILogSink`

The `ILoggerFactory.FlushAsync()` extension lives in `PicoLog`, not `PicoLog.Abs`. The strict runtime capability remains `IFlushableLoggerFactory`, while the extension keeps the common call site simple.

## PicoDI Integration

`AddPicoLog()` registers:

- a singleton `ILoggerFactory`
- typed `ILogger<T>` adapters
- built-in default sink behavior when no explicit `WriteTo` pipeline is configured
- optional bridging of DI-registered sinks when `ReadFrom.RegisteredSinks()` is enabled

For new code, prefer the `WriteTo` sink builder as the primary configuration path.

```csharp
container.AddPicoLog(options =>
{
    options.MinLevel = LogLevel.Info;
    options.WriteTo.ColoredConsole();
    options.WriteTo.File("logs/app.log");
});
```

You can also bridge sinks already registered in PicoDI:

```csharp
container.Register(new SvcDescriptor(typeof(ILogSink), _ => new AuditSink()));

container.AddPicoLog(options =>
{
    options.ReadFrom.RegisteredSinks();
    options.WriteTo.ColoredConsole();
});
```

## Metrics

The core `PicoLog` package emits a small built-in metrics surface through `System.Diagnostics.Metrics` using meter name `PicoLog`.

- `picolog.entries.enqueued`
- `picolog.entries.dropped`
- `picolog.sinks.failures`
- `picolog.writes.rejected_after_shutdown`
- `picolog.queue.entries`
- `picolog.shutdown.drain.duration`

These instruments are intentionally low-cardinality and lightweight.

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

The sample project publishes with Native AOT enabled.

```xml
<PropertyGroup>
  <PublishAOT>true</PublishAOT>
</PropertyGroup>
```

```bash
dotnet publish -c Release -r win-x64 -p:PublishAOT=true
```

The repository also includes a publish-level validation script that publishes the sample, runs the generated executable, and verifies that final shutdown log entries were flushed correctly:

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

## Performance Notes

- logger instances are cached per category inside `LoggerFactory`
- `LoggerFactory` owns one bounded channel, one category pipeline, and one background drain task per category
- `FlushAsync()` is a barrier for entries accepted before the flush snapshot, not a disposal shortcut
- factory disposal still performs the final drain before disposing sinks
- `FileSink` batches writes on its own bounded queue and exposes sink-level flush through `IFlushableLogSink`
- choosing `DropOldest`, `DropWrite`, or `Wait` is a throughput-vs-delivery tradeoff, not a correctness bug

## Benchmarks

The repository includes `benchmarks/PicoLog.Benchmarks`, a PicoBench-based benchmark project for comparing PicoLog handoff costs against Microsoft.Extensions.Logging baselines.

- `MicrosoftAsyncHandoff` is the lightweight string-channel MEL baseline.
- `MicrosoftAsyncEntryHandoff` is the fairer full-entry MEL baseline that mirrors PicoLog's timestamp/category/message envelope cost without adding real I/O.
- wait-mode benchmark names such as `PicoWaitControl_*` are internal benchmark scenario labels, not public API names.

Run the benchmark project:

```bash
dotnet run -c Release --project benchmarks/PicoLog.Benchmarks
```

Or publish and execute the artifact directly:

```bash
dotnet publish benchmarks/PicoLog.Benchmarks/PicoLog.Benchmarks.csproj -c Release -r win-x64
benchmarks/PicoLog.Benchmarks/bin/Release/net10.0/win-x64/publish/PicoLog.Benchmarks.exe main
```

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
public sealed class CustomFormatter : ILogFormatter
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

## Fit and Non-Goals

### Strengths

- The core implementation is small and easy to reason about: `LoggerFactory` owns per-category registrations, pipelines, drain-task lifetimes, and sink lifetimes, while each `InternalLogger` remains a lightweight non-owning write facade.
- The project is AOT-friendly and avoids reflection-heavy infrastructure.
- Structured properties and built-in metrics cover common operational needs without forcing a larger logging ecosystem into the application.
- Queue pressure behavior is explicit rather than hidden.
- Flush semantics stay explicit: `FlushAsync()` is a barrier for already accepted work, while `DisposeAsync()` remains the shutdown path for final drain and resource release.
- The built-in PicoDI integration stays thin and predictable.

### Good Fit

- Small to medium .NET applications that want a lightweight logging core without adopting a larger logging ecosystem
- Edge, IoT, desktop, and utility-style workloads where startup cost, binary size, and AOT compatibility matter
- Application logging scenarios where best-effort delivery is acceptable, mid-run flush barriers are occasionally useful, and explicit shutdown draining is enough
- Teams that prefer a small set of primitives and are comfortable adding custom sinks or formatters as needed

### Non-Goals and Weak Spots

- This is not a full observability platform.
- It is not optimized for very high-cardinality logger categories because the current design creates one factory-owned category pipeline and one background drain task per category.
- It is not a default fit for audit or compliance logging where silent loss is unacceptable.
- Built-in metrics are intentionally small and do not attempt to model a larger end-to-end telemetry system.

## Contributing

1. Fork the repository
2. Create a feature branch
3. Make your changes
4. Add or update tests
5. Submit a pull request

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.
