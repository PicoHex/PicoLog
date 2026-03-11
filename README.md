# Pico.Logger

![CI](https://github.com/PicoHex/Pico.Logger/actions/workflows/ci.yml/badge.svg)
[![NuGet](https://img.shields.io/nuget/v/Pico.Logging.svg)](https://www.nuget.org/packages/Pico.Logging)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

A lightweight, AOT-friendly logging framework for .NET edge and IoT workloads. The repository currently contains three packages: contracts, the core logger implementation, and Pico.DI integration.

## Features

- **AOT compatibility**: targets `net10.0` and avoids reflection-heavy infrastructure
- **Bounded async pipeline**: loggers enqueue entries into a bounded channel and write to sinks on a background task
- **Minimal surface area**: only a few core abstractions need to be implemented to extend the system
- **Pico.DI integration**: built-in registrations for formatter, sinks, logger factory, and typed loggers
- **Scope support**: nested scopes flow through `AsyncLocal` and are attached to each `LogEntry`

## Project Structure

```text
Pico.Logger/
├── src/
│   ├── Pico.Logging.Abs/       # Abstract interfaces and contracts
│   ├── Pico.Logging/           # Core logging implementation
│   └── Pico.Logging.DI/        # Dependency injection integration
├── samples/
│   └── Pico.Logging.Sample/    # Example usage
└── tests/                      # Test projects
```

## Installation

### Core Logging Library

```xml
<PackageReference Include="Pico.Logging" Version="2026.1.6" />
```

### Pico.DI Integration

```xml
<PackageReference Include="Pico.Logging.DI" Version="2026.1.6" />
```

## Quick Start

### Basic Usage

```csharp
using Pico.Logging;
using Pico.Logging.Abs;

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
```

### DI Integration

```csharp
using Pico.DI;
using Pico.DI.Abs;
using Pico.Logging.Abs;
using Pico.Logging.DI;

var container = new SvcContainer();

container.AddLogging(LogLevel.Info);
container.RegisterScoped<MyService, MyService>();

await using var scope = container.CreateScope();

var service = scope.GetService<MyService>();
await service.DoWorkAsync();

if (scope.GetService<ILoggerFactory>() is IAsyncDisposable loggerFactory)
{
    await loggerFactory.DisposeAsync();
}
```

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

`LoggerFactory` owns cached per-category loggers, their background drain tasks, and the registered sinks. Dispose the factory during shutdown to flush queued entries and release sink resources.

```csharp
await using var loggerFactory = new LoggerFactory(sinks);

var logger = new Logger<MyService>(loggerFactory);
logger.Info("Starting up");

// queued entries are flushed when the factory is disposed
```

### Built-in Sinks

- `ConsoleSink` writes formatted entries to standard output with level-based colors.
- `FileSink` appends UTF-8 text to a file path. When used through `AddLogging()`, the default output path is `logs/test.log`.

### Built-in Formatter

`ConsoleFormatter` produces human-readable lines with timestamp, level, category, message, exception text, and optional scopes.

```csharp
var formatter = new ConsoleFormatter();
var sink = new ConsoleSink(formatter);
```

### Logging Extensions

The shipped extension methods are defined on `ILogger` and `ILogger<T>`:

- `Trace`, `Debug`, `Info`, `Notice`, `Warning`, `Error`, `Critical`, `Alert`, `Emergency`
- Async counterparts such as `InfoAsync` and `ErrorAsync`

### Overflow Behavior

Synchronous `Log()` calls use a bounded channel with `DropOldest`. Under sustained overload, the oldest buffered entries can be discarded. `LogAsync()` uses `WriteAsync()` and applies backpressure instead of silently returning.

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

## Building from Source

### Prerequisites

- .NET SDK 10.0 or later
- Git

### Clone and Build

```bash
git clone https://github.com/PicoHex/Pico.Logger.git
cd Pico.Logger
dotnet restore
dotnet build --configuration Release
```

### Run Tests

```bash
dotnet test --configuration Release
```

## Performance Considerations

- Logger instances are cached per category inside `LoggerFactory`.
- Each internal logger owns one bounded channel and one background drain task.
- Factory disposal flushes all active loggers before disposing sinks.
- `FileSink` flushes each write for durability; this is simple and safe but not optimized for maximum throughput.

## Extending Pico.Logger

### Custom Sink

```csharp
public sealed class CustomSink : ILogSink
{
    public ValueTask WriteAsync(LogEntry entry, CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"CUSTOM: {entry.Message}");
        return ValueTask.CompletedTask;
    }

    public void Dispose() { }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

var formatter = new ConsoleFormatter();
await using var loggerFactory = new LoggerFactory([new CustomSink(), new FileSink(formatter)]);
```

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

- logger caching by category
- minimum-level filtering
- scope capture and flush on factory disposal
- sink failure isolation
- Pico.DI typed logger resolution

## Contributing

1. Fork the repository
2. Create a feature branch
3. Make your changes
4. Add or update tests
5. Submit a pull request

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.
