# PicoLog

[English](README.md) | [简体中文](README.zh.md) | [繁體中文](README.zh-TW.md) | [Deutsch](README.de.md) | [Español](README.es.md) | [Français](README.fr.md) | [日本語](README.ja.md) | [Português (Brasil)](README.pt-BR.md) | [Русский](README.ru.md)

![CI](https://github.com/PicoHex/PicoLog/actions/workflows/ci.yml/badge.svg)
[![NuGet](https://img.shields.io/nuget/v/PicoLog.svg)](https://www.nuget.org/packages/PicoLog)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

一个轻量、适合 AOT 的 .NET 日志框架，面向边缘与 IoT 工作负载。该仓库包含契约、核心日志实现、PicoDI 集成、示例应用以及专用基准测试项目。

## 功能特性

- **AOT 兼容性**: 面向 `net10.0`，避免依赖大量反射的基础设施
- **有界异步管线**: logger 会将条目加入有界通道，并由后台任务写入 sink
- **结构化属性**: 可选的键值负载会通过 `LogEntry.Properties` 以及内置格式化器输出流转
- **内置指标**: 核心包会通过 `System.Diagnostics.Metrics` 发出队列、丢弃、sink 失败和关闭相关指标
- **精简的表面积**: 只需实现少量核心抽象即可扩展整个系统
- **PicoDI 集成**: 内置注册 logger factory 和类型化 logger，默认启用控制台日志，在配置文件路径时可选启用文件日志
- **基准测试覆盖**: 包含基于 PicoBench 的基准项目，提供轻量和更公平的 MEL 异步交接基线
- **作用域支持**: 嵌套作用域通过 `AsyncLocal` 流转，并附加到每个 `LogEntry`

## 项目结构

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

## 安装

### 核心日志库

```bash
dotnet add package PicoLog
```

### PicoDI 集成

```bash
dotnet add package PicoLog.DI
```

## 快速开始

### 基本用法

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
```

### DI 集成

```csharp
using PicoDI;
using PicoDI.Abs;
using PicoLog.Abs;
using PicoLog.DI;

ISvcContainer container = new SvcContainer();

PicoLog.DI.SvcContainerExtensions.AddLogging(container, options =>
{
    options.MinLevel = LogLevel.Info;
    options.FilePath = "logs/app.log";
    options.UseColoredConsole = true;
    options.Factory.QueueFullMode = LogQueueFullMode.Wait;
    options.File.BatchSize = 64;
});
container.RegisterScoped<IMyService, MyService>();

await using var scope = container.CreateScope();

var service = scope.GetService<IMyService>();
var structuredLogger = scope.GetService<IStructuredLogger<MyService>>();
await service.DoWorkAsync();

structuredLogger.LogStructured(
    LogLevel.Info,
    "DI structured event",
    [new("tenant", "alpha"), new("attempt", 3)]
);

await scope.GetService<ILoggerFactory>().DisposeAsync();
```

## 配置

### 最低级别

`LoggerFactory.MinLevel` 控制哪些条目会被接受。数值越小表示级别越严重，因此默认的 `Debug` 级别会允许 `Emergency` 到 `Debug`，但会过滤掉 `Trace`。

```csharp
await using var loggerFactory = new LoggerFactory(sinks)
{
    MinLevel = LogLevel.Warning
};
```

### 生命周期所有权

`LoggerFactory` 拥有按类别缓存的 logger、它们各自的按类别管线、这些管线运行的后台排空任务，以及已注册的 sink。请在关闭期间释放 factory，以便刷新排队中的条目并释放 sink 资源。一旦开始释放，新的写入会被拒绝，而已经入队的条目会继续排空。

```csharp
await using var loggerFactory = new LoggerFactory(sinks);

var logger = new Logger<MyService>(loggerFactory);
logger.Info("Starting up");

// queued entries are flushed when the factory is disposed;
// writes that race with shutdown are rejected
```

### 队列压力

`LoggerFactoryOptions.QueueFullMode` 明确规定了同步和异步写入在队列承压时的处理方式:

- `DropOldest` 会丢弃队列中最旧的条目，以保持日志写入非阻塞。这是默认值。
- `DropWrite` 会拒绝新条目，并通过 `OnMessagesDropped` 报告丢弃情况。
- `Wait` 会让同步日志最多阻塞到 `SyncWriteTimeout`，并让异步日志等待队列空间，直到请求取消。

```csharp
var options = new LoggerFactoryOptions
{
    MinLevel = LogLevel.Info,
    QueueFullMode = LogQueueFullMode.Wait,
    SyncWriteTimeout = TimeSpan.FromMilliseconds(500)
};

await using var loggerFactory = new LoggerFactory(sinks, options);
```

### 内置 Sink

- `ConsoleSink` 会将纯文本格式化后的条目写到标准输出。
- `ColoredConsoleSink` 会串行化颜色切换，避免控制台状态在并发写入之间泄漏。
- `FileSink` 会先在后台队列中批量处理 UTF-8 文件写入，再刷新到磁盘。`AddLogging()` 会在 logger factory 内部创建已配置的 sink，因此 factory 仍然是其生命周期的唯一所有者。

### PicoDI 默认注册

`AddLogging()` 会注册:

- 单例 `ILoggerFactory`
- 类型化 `ILogger<T>` 适配器
- 类型化 `IStructuredLogger<T>` 适配器
- 默认的控制台 sink
- 在配置 `FilePath` 时可选启用的文件 sink

关闭时，请显式释放已解析的 factory，这样进程退出前会刷新排队中的日志条目。关闭开始后到达的写入会被拒绝，而不是在过晚时仍被接受。

你可以通过可选的 `filePath` 参数、设置 `options.FilePath`，或在 configure 重载中设置 `options.File.FilePath` 来启用文件日志。显式文件路径会被视为主动启用文件 sink。

```csharp
PicoLog.DI.SvcContainerExtensions.AddLogging(container, LogLevel.Info, "logs/app.log");
```

```csharp
PicoLog.DI.SvcContainerExtensions.AddLogging(container, options =>
{
    options.MinLevel = LogLevel.Info;
    options.FilePath = "logs/app.log";
});
```

```csharp
PicoLog.DI.SvcContainerExtensions.AddLogging(container, options =>
{
    options.MinLevel = LogLevel.Info;
    options.File.FilePath = "logs/app.log";
});
```

### 内置格式化器

`ConsoleFormatter` 会生成便于阅读的文本行，其中包含时间戳、级别、类别、消息、可选的结构化属性、异常文本以及可选作用域。

```csharp
var formatter = new ConsoleFormatter();
var sink = new ConsoleSink(formatter);
```

### 结构化日志

PicoLog 保持与 `ILogger` 兼容，并通过 `IStructuredLogger` / `IStructuredLogger<T>` 提供有保证的结构化日志能力。当你使用内置 `LoggerFactory` 时，运行时 logger 会把 `LogStructured()` / `LogStructuredAsync()` 的负载保留在 `LogEntry.Properties` 上。

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

`ConsoleFormatter` 会在消息后以紧凑的文本形式附加结构化属性，例如:

```text
[2026-04-05 11:30:42.123] WARNING   [MyService] Cache miss {cacheKey="user:42", node="edge-a", attempt=3}
```

### 日志扩展

随附的扩展方法定义在 `ILogger` 和 `ILogger<T>` 上:

- `Trace`, `Debug`, `Info`, `Notice`, `Warning`, `Error`, `Critical`, `Alert`, `Emergency`
- 异步对应方法，例如 `InfoAsync` 和 `ErrorAsync`
- `LogStructured` 和 `LogStructuredAsync` 作为尽力而为的适配器，当运行时 logger 实现了 `IStructuredLogger` 时会保留属性，否则会回退为不带结构化负载的普通日志

如果你需要严格的结构化日志契约，请直接依赖 `IStructuredLogger` / `IStructuredLogger<T>`。

### 溢出行为

同步和异步日志都会写入一个有界通道。

- 同步 `Log()` 和异步 `LogAsync()` 都遵循 `LoggerFactoryOptions.QueueFullMode`。
- 默认值是 `DropOldest`，它更偏向吞吐量和较低的调用方延迟，而不是保证交付。
- `Wait` 会让调用方明确感受到背压，同步写入会阻塞到队列空间可用或 `SyncWriteTimeout` 到期，异步写入则会等待队列空间，直到请求取消。
- `DropWrite` 会保留较早入队的条目，并通过 `OnMessagesDropped` 报告被丢弃的新条目。
- 一旦 factory 开始释放，新的写入会被拒绝，而已经入队的条目会继续刷新。

对于一般应用日志，默认的 `DropOldest` 行为通常可以接受。对于审计类日志，请优先考虑 `Wait` 或专用的 sink 策略。

### 内置指标

核心 `PicoLog` 包会通过 `System.Diagnostics.Metrics` 发出一组精简的内置指标，使用的 meter 名称为 `PicoLog`。

- `picolog.entries.enqueued`
- `picolog.entries.dropped`
- `picolog.sinks.failures`
- `picolog.writes.rejected_after_shutdown`
- `picolog.queue.entries`
- `picolog.shutdown.drain.duration`

这些 instrument 旨在保持低基数和轻量。你可以直接使用 `MeterListener` 观察它们，也可以将它们桥接到更广泛的遥测基础设施中。

```csharp
using var listener = new MeterListener();

listener.InstrumentPublished = (instrument, meterListener) =>
{
    if (instrument.Meter.Name == PicoLogMetrics.MeterName)
        meterListener.EnableMeasurementEvents(instrument);
};

listener.Start();
```

## AOT 兼容性

示例项目会在启用 Native AOT 的情况下发布:

```xml
<PropertyGroup>
  <PublishAOT>true</PublishAOT>
</PropertyGroup>
```

```bash
dotnet publish -c Release -r win-x64 -p:PublishAOT=true
```

仓库还包含一个发布级验证脚本，它会发布示例、运行生成的可执行文件，并验证最终的关闭日志条目已经正确刷新:

```powershell
./scripts/Validate-AotSample.ps1
```

## 从源码构建

### 前置条件

- .NET SDK 10.0 或更高版本
- Git

### 克隆并构建

```bash
git clone https://github.com/PicoHex/PicoLog.git
cd PicoLog
dotnet restore
dotnet build --configuration Release
```

### 运行测试

```bash
dotnet test --solution ./PicoLog.slnx --configuration Release
```

## 性能考量

- `LoggerFactory` 内部会按类别缓存 logger 实例。
- `LoggerFactory` 为每个类别拥有一个有界通道、一个类别管线和一个后台排空任务。
- 释放 factory 时，会在释放 sink 之前刷新所有活跃 logger。
- `FileSink` 在自身的有界队列上批量写入，并在批次边界或刷新间隔边界执行刷新。
- 选择 `DropOldest`、`DropWrite` 或 `Wait` 是吞吐量与交付能力之间的权衡，不是正确性缺陷。

## 基准测试

仓库包含 `benchmarks/PicoLog.Benchmarks`，这是一个基于 PicoBench 的基准测试项目，用于比较 PicoLog 与 Microsoft.Extensions.Logging 基线之间的交接成本。

- `MicrosoftAsyncHandoff` 是轻量的基于字符串通道的 MEL 基线。
- `MicrosoftAsyncEntryHandoff` 是更公平的完整条目 MEL 基线，它会镜像 PicoLog 的 timestamp/category/message envelope 成本，而不加入真实 I/O。
- `PicoWaitControl_CachedMessage` 和 `PicoWaitHandoff_CachedMessage` 用于覆盖 PicoLog 在 wait 模式下的背压表现，作为相对比较。

运行基准测试项目:

```bash
dotnet run -c Release --project benchmarks/PicoLog.Benchmarks
```

或者直接发布并执行产物:

```bash
dotnet publish benchmarks/PicoLog.Benchmarks/PicoLog.Benchmarks.csproj -c Release -r win-x64
benchmarks/PicoLog.Benchmarks/bin/Release/net10.0/win-x64/publish/PicoLog.Benchmarks.exe main
```

基准测试应用会写出:

- `benchmark-results.md`
- `benchmark-results-main.md`
- `benchmark-results-wait.md`

## 适用场景与非目标

### 优势

- 核心实现很小，也容易推理: `LoggerFactory` 拥有按类别的 logger 注册、类别管线、排空任务生命周期和 sink 生命周期，而每个 `InternalLogger` 仍然是轻量、无所有权的写入门面。
- 项目对 AOT 友好，并避免依赖大量反射的基础设施，因此很适合 Native AOT、边缘和 IoT 工作负载。
- 结构化属性和内置指标覆盖了常见的运维需求，同时不会强迫应用接入更庞大的日志生态。
- 队列压力行为是显式的，而不是隐藏的。调用方可以在 `DropOldest`、`DropWrite` 和 `Wait` 之间选择，取决于吞吐量还是交付更重要。
- 内置 PicoDI 集成保持轻薄且可预测，不会引入庞大的托管或配置栈。

### 适用场景

- 想要轻量日志核心、又不想引入更大日志生态的小型到中型 .NET 应用。
- 对启动成本、二进制大小和 AOT 兼容性有要求的边缘、IoT、桌面和工具类工作负载。
- 可以接受尽力交付，并且显式关闭刷新已经足够的应用日志场景。
- 偏好少量基础原语，并愿意按需添加自定义 sink 或格式化器的团队。

### 非目标与薄弱点

- 这不是一个完整的可观测性平台。它支持结构化属性和一小组内置指标，但不提供消息模板解析、enricher、滚动文件管理、远程传输 sink，或与更广泛遥测生态的深度集成。
- 它没有针对超高基数的 logger 类别进行优化。当前设计会为每个类别创建一个 factory 拥有的类别管线和一个后台排空任务。
- 对于不能接受静默丢失的审计或合规日志场景，它并不是默认合适的选择。默认队列模式偏向吞吐量，而更强的交付保证需要显式配置，例如 `Wait` 或专用 sink 策略。
- 内置指标覆盖队列深度、已接受和已丢弃条目、sink 失败、关闭期间的迟到写入以及关闭排空时长。它们目前还没有提供按类别划分的指标、sink 延迟直方图或更大的端到端遥测模型。

## 扩展 PicoLog

### 自定义 Sink

```csharp
public sealed class CustomSink : ILogSink
{
    public Task WriteAsync(LogEntry entry, CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"CUSTOM: {entry.Message}");
        return Task.CompletedTask;
    }

    public void Dispose() { }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

var formatter = new ConsoleFormatter();
await using var loggerFactory = new LoggerFactory([new CustomSink(), new FileSink(formatter)]);
```

### 自定义格式化器

```csharp
public class CustomFormatter : ILogFormatter
{
    public string Format(LogEntry entry)
    {
        return $"{entry.Timestamp:yyyy-MM-dd HH:mm:ss} [{entry.Level}] {entry.Message}";
    }
}
```

## 测试

当前测试套件覆盖:

- 文件 sink 写入与异步释放并发竞争
- 按类别缓存 logger
- 最低级别过滤
- 结构化负载捕获与格式化
- 内置指标发出
- 在 factory 释放时捕获 scope 并完成刷新
- 在关闭开始后拒绝写入
- sink 失败隔离
- 异步尾部消息刷新
- 真实文件 sink 尾部持久化
- 已配置的 DI 文件输出
- PicoDI 类型化 logger 解析

示例也会通过 Native AOT 发布和执行进行验证。

## 贡献

1. Fork 仓库
2. 创建功能分支
3. 完成你的更改
4. 添加或更新测试
5. 提交 pull request

## 许可证

本项目基于 MIT License 许可，详情请参见 [LICENSE](LICENSE) 文件。
