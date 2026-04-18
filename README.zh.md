# PicoLog

[English](README.md) | [简体中文](README.zh.md) | [繁體中文](README.zh-TW.md) | [Deutsch](README.de.md) | [Español](README.es.md) | [Français](README.fr.md) | [日本語](README.ja.md) | [Português (Brasil)](README.pt-BR.md) | [Русский](README.ru.md)

![CI](https://github.com/PicoHex/PicoLog/actions/workflows/ci.yml/badge.svg)
[![NuGet](https://img.shields.io/nuget/v/PicoLog.svg)](https://www.nuget.org/packages/PicoLog)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

PicoLog 是一个轻量、适合 AOT 的 .NET 日志框架，面向边缘、桌面、工具型与 IoT 工作负载。

当前设计有意保持精简：

- **一个 logger 模型**：`ILogger` / `ILogger<T>`
- **一个 DI 入口**：`AddPicoLog(...)`
- **一个生命周期拥有者**：`ILoggerFactory`

结构化属性是 log event 本身的一部分，而不是另一种 logger 类型。`PicoLog` 中承载运行时与扩展类型，例如 sink、formatter、`LogEntry` 与 flush 伴随契约；`PicoLog.Abs` 只保留面向使用者的契约。

## 功能特性

- **AOT 友好设计**：避免依赖大量反射的基础设施，并提供 Native AOT 示例
- **有界异步管线**：logger 会把条目交给按类别划分的有界通道管线，由后台任务继续处理
- **显式生命周期语义**：`FlushAsync()` 是运行中的屏障，`DisposeAsync()` 是关闭时的最终排空
- **`ILogger` 原生结构化属性**：原生重载会把键值属性保留到 `LogEntry.Properties`
- **精简 DI 表面积**：`AddPicoLog(...)` 只注册 `ILoggerFactory` 与类型化 `ILogger<T>`
- **内置 sink 与 formatter**：控制台、彩色控制台、文件，以及可读的文本 formatter
- **flush 伴随契约**：运行时仍通过 `IFlushableLoggerFactory` 与 `IFlushableLogSink` 暴露严格的 flush 能力
- **内置指标**：通过 `System.Diagnostics.Metrics` 发出队列、丢弃、sink 失败和关闭相关指标
- **基准项目**：基于 PicoBench，对 PicoLog 与 MEL 的交接成本进行对比
- **作用域支持**：嵌套 scope 通过 `AsyncLocal` 流动，并附加到每个 `LogEntry`

## 项目结构

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

## 安装

### 核心运行时

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

`FlushAsync()` **不是**释放操作。它是针对 flush 快照之前已接受条目的屏障。最终关闭排空和 sink 清理由 `DisposeAsync()` 负责。

### DI 集成

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

`AddPicoLog()` 是唯一的 DI 入口。业务代码通常只依赖 `ILogger<T>`。`ILoggerFactory` 则是 app root 上显式的 flush / shutdown 生命周期拥有者。

## 核心模型

### 一个 Logger 模型

PicoLog 不再把日志划分为“普通 logger”和“结构化 logger”两套接口。

- `ILogger` / `ILogger<T>` 是主要写入接口
- 普通事件使用 `Log(level, message, exception?)`
- 结构化事件使用 `Log(level, message, properties, exception)`
- 异步版本通过 `LogAsync(...)` 保持同样形状

`LogStructured()` 与 `LogStructuredAsync()` 仍然存在于 `LoggerExtensions` 中，但它们只是对原生 `ILogger` 重载的语法糖。

### 包分层

- **`PicoLog.Abs`**：面向使用者的契约，例如 `ILogger`、`ILogger<T>`、`ILoggerFactory`、`LogLevel` 与 `LoggerExtensions`
- **`PicoLog`**：运行时与扩展类型，例如 `LoggerFactory`、`Logger<T>`、`LogEntry`、`ILogSink`、`ILogFormatter`、`IFlushableLoggerFactory` 与 `IFlushableLogSink`
- **`PicoLog.DI`**：通过 `AddPicoLog(...)` 提供 PicoDI 集成

## 配置

### 最低级别

`LoggerFactory.MinLevel` 控制哪些条目会被接受。数值越小表示级别越严重，因此默认的 `Debug` 级别允许 `Emergency` 到 `Debug`，但会过滤 `Trace`。

```csharp
await using var loggerFactory = new LoggerFactory(sinks)
{
    MinLevel = LogLevel.Warning
};
```

### 生命周期所有权

`LoggerFactory` 拥有：

- 按类别缓存的 logger
- 每个类别的处理管线
- 后台排空任务
- 已注册的 sink

这意味着生命周期 API 应该属于 factory，而不是 `ILogger<T>`。

```csharp
await using var loggerFactory = new LoggerFactory(sinks);

var logger = new Logger<MyService>(loggerFactory);
logger.Info("Starting up");

await loggerFactory.FlushAsync();   // 运行中的 barrier
await loggerFactory.DisposeAsync(); // 最终 shutdown drain
```

如果你从 DI 中解析 `ILoggerFactory`，请记住它依然是应用级 singleton 生命周期拥有者。**从 scope 中解析它，并不意味着它归这个 scope 所有。**

### 队列压力

`LoggerFactoryOptions.QueueFullMode` 明确规定了同步和异步写入在队列承压时的行为。

`LogAsync()` 完成时，表示 logger 边界的交接处理已经完成。该条目可能：

- 已被接受
- 因队列策略被丢弃
- 在关闭期间被拒绝

它**不表示**某个 sink 已经完成持久化写入。

- `DropOldest` 会丢弃最旧的排队项，从而保持非阻塞。这是默认值。
- `DropWrite` 会拒绝新条目，并通过 `OnMessagesDropped` 报告。
- `Wait` 会让同步写入最多阻塞到 `SyncWriteTimeout`，并让异步写入等待队列空间直到请求取消。

```csharp
var options = new LoggerFactoryOptions
{
    MinLevel = LogLevel.Info,
    QueueFullMode = LogQueueFullMode.Wait,
    SyncWriteTimeout = TimeSpan.FromMilliseconds(500)
};

await using var loggerFactory = new LoggerFactory(sinks, options);
```

## 内置运行时组件

### Sink

- `ConsoleSink` 会将格式化后的纯文本条目写到标准输出。
- `ColoredConsoleSink` 会串行化颜色切换，避免控制台状态在并发写入之间泄漏。
- `FileSink` 会先在后台队列中批量处理 UTF-8 文件写入，再刷新到磁盘，并通过 `IFlushableLogSink` 支持 sink 级别 flush。

使用 `AddPicoLog()` 时，配置好的 sink 会在 logger factory 内部创建，因此 factory 仍是其唯一生命周期拥有者。

### Formatter

`ConsoleFormatter` 会生成易读文本行，其中包含时间戳、级别、类别、消息、可选结构化属性、异常文本和可选作用域。

```csharp
var formatter = new ConsoleFormatter();
var sink = new ConsoleSink(formatter);
```

### 结构化日志

结构化数据是 log event 本身的一部分。

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

这些属性会被保留在 `LogEntry.Properties` 上，再由 sink 或 formatter 决定如何消费。

例如，`ConsoleFormatter` 会把它们追加成紧凑文本形式：

```text
[2026-04-05 11:30:42.123] WARNING   [MyService] Cache miss {cacheKey="user:42", node="edge-a", attempt=3}
```

## 日志扩展

内置扩展方法定义在 `ILogger` 与 `ILogger<T>` 上：

- `Trace`, `Debug`, `Info`, `Notice`, `Warning`, `Error`, `Critical`, `Alert`, `Emergency`
- 异步对应方法，例如 `InfoAsync` 与 `ErrorAsync`
- `LogStructured` 与 `LogStructuredAsync`，作为对原生 property-aware `ILogger` 重载的便捷包装
- `ILoggerFactory` 与 `ILogSink` 上尽力而为的 `FlushAsync()` 扩展

`ILoggerFactory.FlushAsync()` 扩展位于 `PicoLog` 包中，而不是 `PicoLog.Abs`。严格的运行时能力仍由 `IFlushableLoggerFactory` 表达，而扩展方法让常见调用保持简单。

## PicoDI 集成

`AddPicoLog()` 会注册：

- 一个 singleton `ILoggerFactory`
- 类型化 `ILogger<T>` 适配器
- 在未显式配置 `WriteTo` 管线时的内置默认 sink 行为
- 启用 `ReadFrom.RegisteredSinks()` 时对 DI 已注册 sink 的可选桥接

对于新代码，请优先把 `WriteTo` sink builder 作为主要配置路径。

```csharp
container.AddPicoLog(options =>
{
    options.MinLevel = LogLevel.Info;
    options.WriteTo.ColoredConsole();
    options.WriteTo.File("logs/app.log");
});
```

你也可以桥接已经注册到 PicoDI 的 sink：

```csharp
container.Register(new SvcDescriptor(typeof(ILogSink), _ => new AuditSink()));

container.AddPicoLog(options =>
{
    options.ReadFrom.RegisteredSinks();
    options.WriteTo.ColoredConsole();
});
```

## 指标

核心 `PicoLog` 包会通过 `System.Diagnostics.Metrics` 发出一组精简内置指标，使用 meter 名称 `PicoLog`。

- `picolog.entries.enqueued`
- `picolog.entries.dropped`
- `picolog.sinks.failures`
- `picolog.writes.rejected_after_shutdown`
- `picolog.queue.entries`
- `picolog.shutdown.drain.duration`

这些 instrument 有意保持低基数和轻量。

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

示例项目会在启用 Native AOT 的情况下发布。

```xml
<PropertyGroup>
  <PublishAOT>true</PublishAOT>
</PropertyGroup>
```

```bash
dotnet publish -c Release -r win-x64 -p:PublishAOT=true
```

仓库还包含一个发布级验证脚本，它会发布示例、运行生成的可执行文件，并验证最终的关闭日志条目已经正确刷新：

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

## 性能说明

- logger 实例会在 `LoggerFactory` 内按类别缓存
- `LoggerFactory` 为每个类别拥有一个有界通道、一个类别管线和一个后台排空任务
- `FlushAsync()` 是针对 flush 快照之前已接受条目的 barrier，不是释放的捷径
- factory 释放时仍会在释放 sink 之前执行最终排空
- `FileSink` 会在自己的有界队列上批量写入，并通过 `IFlushableLogSink` 暴露 sink 级别 flush
- 选择 `DropOldest`、`DropWrite` 或 `Wait` 是吞吐量与交付之间的权衡，不是正确性错误

## 基准测试

仓库包含 `benchmarks/PicoLog.Benchmarks`，这是一个基于 PicoBench 的基准测试项目，用于比较 PicoLog 与 Microsoft.Extensions.Logging 基线的交接成本。

- `MicrosoftAsyncHandoff` 是轻量的基于字符串通道的 MEL 基线。
- `MicrosoftAsyncEntryHandoff` 是更公平的完整条目 MEL 基线，它会模拟 PicoLog 的 timestamp/category/message envelope 成本，但不加入真实 I/O。
- 像 `PicoWaitControl_*` 这样的 wait-mode benchmark 名称只是内部基准场景标签，不是公共 API 名称。

运行基准测试项目：

```bash
dotnet run -c Release --project benchmarks/PicoLog.Benchmarks
```

或直接发布并执行产物：

```bash
dotnet publish benchmarks/PicoLog.Benchmarks/PicoLog.Benchmarks.csproj -c Release -r win-x64
benchmarks/PicoLog.Benchmarks/bin/Release/net10.0/win-x64/publish/PicoLog.Benchmarks.exe main
```

## 扩展 PicoLog

### 自定义 Sink

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

如果某个 sink 没有实现 `IFlushableLogSink`，`ILogSink.FlushAsync()` 扩展会以尽力而为方式立即完成。

### 自定义 Formatter

```csharp
public sealed class CustomFormatter : ILogFormatter
{
    public string Format(LogEntry entry)
    {
        return $"{entry.Timestamp:yyyy-MM-dd HH:mm:ss} [{entry.Level}] {entry.Message}";
    }
}
```

## 测试

当前测试套件覆盖：

- 文件 sink 写入与异步释放竞争
- 按类别缓存 logger
- 最低级别过滤
- 结构化负载捕获与格式化
- 内置指标发出
- 在 factory 释放时捕获 scope 并完成刷新
- 已接受条目的 flush 屏障和尽力而为的 flush 扩展
- 在关闭开始后拒绝写入
- sink 失败隔离
- 异步尾部消息刷新
- 真实文件 sink 尾部持久化
- 已配置的 DI 文件输出
- PicoDI 类型化 logger 解析

示例也会通过 Native AOT 发布与执行来验证。

## 适用场景与非目标

### 优势

- 核心实现很小，也容易推理：`LoggerFactory` 拥有按类别的注册、处理管线、排空任务生命周期与 sink 生命周期，而每个 `InternalLogger` 仍是轻量、非拥有型的写入门面。
- 项目对 AOT 友好，并避免依赖大量反射的基础设施。
- 结构化属性和内置指标覆盖常见运维需求，同时不会强迫应用接入更庞大的日志生态。
- 队列压力行为是显式的，而不是隐藏的。
- flush 语义保持明确：`FlushAsync()` 是已接受工作的 barrier，`DisposeAsync()` 则负责最终 shutdown drain 和资源释放。
- 内置的 PicoDI 集成保持轻薄且可预测。

### 适合的场景

- 想要轻量日志核心、又不想引入更大日志生态的小型到中型 .NET 应用
- 对启动成本、二进制大小和 AOT 兼容性有要求的边缘、IoT、桌面与工具型工作负载
- 可以接受尽力交付、偶尔需要运行中 flush barrier，而且显式关闭排空已足够的应用日志场景
- 偏好少量基础原语，并愿意按需添加自定义 sink 或 formatter 的团队

### 非目标与薄弱点

- 这不是完整的可观测性平台。
- 它没有针对超高基数的 logger category 做优化，因为当前设计会为每个类别创建一个 factory 拥有的类别管线与一个后台排空任务。
- 对于不能接受静默丢失的审计或合规日志场景，它并不是默认合适的选择。
- 内置指标有意保持精简，不尝试建模更大的端到端遥测系统。

## 贡献

1. Fork 仓库
2. 创建功能分支
3. 完成你的更改
4. 添加或更新测试
5. 提交 pull request

## 许可证

本项目基于 MIT License 许可，详情请参见 [LICENSE](LICENSE) 文件。
