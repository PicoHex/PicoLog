# Source Projects

This folder contains the three source packages tracked by `PicoLog.slnx`:

- `PicoLog.Abs`: consumer-facing contracts such as `ILogger`, `ILogger<T>`, `ILoggerFactory`, and `LogLevel`
- `PicoLog`: the core runtime plus extensibility types such as `LoggerFactory`, `Logger<T>`, sinks, formatters, and `LogEntry`
- `PicoLog.DI`: PicoDI integration through `AddPicoLog(...)`

In short, `PicoLog.Abs` defines the shared API surface, `PicoLog` implements the runtime, and `PicoLog.DI` wires the runtime into PicoDI.

For installation, configuration, usage examples, and the full repository overview, see the root [README](../README.md).
