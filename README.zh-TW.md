# PicoLog

[English](README.md) | [简体中文](README.zh.md) | [繁體中文](README.zh-TW.md) | [Deutsch](README.de.md) | [Español](README.es.md) | [Français](README.fr.md) | [日本語](README.ja.md) | [Português (Brasil)](README.pt-BR.md) | [Русский](README.ru.md)

![CI](https://github.com/PicoHex/PicoLog/actions/workflows/ci.yml/badge.svg)
[![NuGet](https://img.shields.io/nuget/v/PicoLog.svg)](https://www.nuget.org/packages/PicoLog)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

PicoLog 是一個輕量、適合 AOT 的 .NET 記錄框架，面向邊緣、桌面、工具型與 IoT 工作負載。

目前設計刻意保持精簡：

- **一個 logger 模型**：`ILogger` / `ILogger<T>`
- **一個 DI 入口**：`AddPicoLog(...)`
- **一個生命週期擁有者**：`ILoggerFactory`

結構化屬性是 log event 本身的一部分，而不是另一種 logger 類型。`PicoLog` 承載執行期與擴充型別，例如 sink、formatter、`LogEntry` 與 flush 伴隨契約；`PicoLog.Abs` 只保留面向使用者的契約。

## 功能特色

- **AOT 友善設計**：避免依賴大量反射的基礎設施，並提供 Native AOT 範例
- **有界非同步管線**：logger 會把項目交給按分類劃分的有界通道管線，再由背景工作繼續處理
- **明確生命週期語義**：`FlushAsync()` 是執行中的 barrier，`DisposeAsync()` 是關閉時的最終排空
- **`ILogger` 原生結構化屬性**：原生 overload 會把鍵值屬性保留到 `LogEntry.Properties`
- **精簡 DI 表面積**：`AddPicoLog(...)` 只註冊 `ILoggerFactory` 與型別化 `ILogger<T>`
- **內建 sink 與 formatter**：控制台、彩色控制台、檔案，以及可讀的文字 formatter
- **flush 伴隨契約**：執行期仍透過 `IFlushableLoggerFactory` 與 `IFlushableLogSink` 暴露嚴格的 flush 能力
- **內建指標**：透過 `System.Diagnostics.Metrics` 發出佇列、丟棄、sink 失敗與關閉相關指標
- **基準專案**：基於 PicoBench，比較 PicoLog 與 MEL 的交接成本
- **Scope 支援**：巢狀 scope 會透過 `AsyncLocal` 流動，並附加到每個 `LogEntry`

## 專案結構

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

## 安裝

### 核心執行期

```bash
dotnet add package PicoLog
```

### PicoDI 整合

```bash
dotnet add package PicoLog.DI
```

## 快速開始

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

`FlushAsync()` **不是**釋放操作。它是針對 flush 快照之前已接受項目的 barrier。最終關閉排空與 sink 清理由 `DisposeAsync()` 負責。

### DI 整合

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

`AddPicoLog()` 是唯一的 DI 入口。業務程式碼通常只依賴 `ILogger<T>`。`ILoggerFactory` 則是 app root 上明確的 flush / shutdown 生命週期擁有者。

## 核心模型

### 一個 Logger 模型

PicoLog 不再把記錄切分為「一般 logger」和「結構化 logger」兩套介面。

- `ILogger` / `ILogger<T>` 是主要寫入介面
- 一般事件使用 `Log(level, message, exception?)`
- 結構化事件使用 `Log(level, message, properties, exception)`
- 非同步版本透過 `LogAsync(...)` 保持相同形狀

`LogStructured()` 與 `LogStructuredAsync()` 仍然存在於 `LoggerExtensions` 中，但它們只是對原生 `ILogger` overload 的語法糖。

### 套件分層

- **`PicoLog.Abs`**：面向使用者的契約，例如 `ILogger`、`ILogger<T>`、`ILoggerFactory`、`LogLevel` 與 `LoggerExtensions`
- **`PicoLog`**：執行期與擴充型別，例如 `LoggerFactory`、`Logger<T>`、`LogEntry`、`ILogSink`、`ILogFormatter`、`IFlushableLoggerFactory` 與 `IFlushableLogSink`
- **`PicoLog.DI`**：透過 `AddPicoLog(...)` 提供 PicoDI 整合

## 設定

### 最低層級

`LoggerFactory.MinLevel` 會控制哪些項目會被接受。數值越小代表嚴重性越高，因此預設的 `Debug` 層級允許 `Emergency` 到 `Debug`，但會過濾 `Trace`。

```csharp
await using var loggerFactory = new LoggerFactory(sinks)
{
    MinLevel = LogLevel.Warning
};
```

### 生命週期擁有權

`LoggerFactory` 擁有：

- 依分類快取的 logger
- 每個分類的處理管線
- 背景排空工作
- 已註冊的 sink

這表示生命週期 API 應該屬於 factory，而不是 `ILogger<T>`。

```csharp
await using var loggerFactory = new LoggerFactory(sinks);

var logger = new Logger<MyService>(loggerFactory);
logger.Info("Starting up");

await loggerFactory.FlushAsync();   // 執行中的 barrier
await loggerFactory.DisposeAsync(); // 最終 shutdown drain
```

如果你從 DI 解析 `ILoggerFactory`，請記住它依然是應用程式層級的 singleton 生命週期擁有者。**從 scope 中解析它，並不代表它歸這個 scope 所有。**

### 佇列壓力

`LoggerFactoryOptions.QueueFullMode` 明確規定了同步與非同步寫入在佇列承壓時的行為。

`LogAsync()` 完成時，表示 logger 邊界的交接處理已完成。該項目可能：

- 已被接受
- 因佇列策略而被丟棄
- 在關閉期間被拒絕

它**不表示**某個 sink 已完成持久化寫入。

- `DropOldest` 會丟棄最舊的排隊項，從而保持非阻塞。這是預設值。
- `DropWrite` 會拒絕新項目，並透過 `OnMessagesDropped` 回報。
- `Wait` 會讓同步寫入最多阻塞到 `SyncWriteTimeout`，並讓非同步寫入等待佇列空間直到要求取消。

```csharp
var options = new LoggerFactoryOptions
{
    MinLevel = LogLevel.Info,
    QueueFullMode = LogQueueFullMode.Wait,
    SyncWriteTimeout = TimeSpan.FromMilliseconds(500)
};

await using var loggerFactory = new LoggerFactory(sinks, options);
```

## 內建執行期元件

### Sink

- `ConsoleSink` 會將格式化後的純文字項目寫到標準輸出。
- `ColoredConsoleSink` 會將顏色切換序列化，避免主控台狀態在並行寫入之間外漏。
- `FileSink` 會先在背景佇列中批次處理 UTF-8 檔案寫入，再刷新到磁碟，並透過 `IFlushableLogSink` 支援 sink 層級 flush。

使用 `AddPicoLog()` 時，設定好的 sink 會在 logger factory 內部建立，因此 factory 仍是它們唯一的生命週期擁有者。

### Formatter

`ConsoleFormatter` 會產生易讀的文字行，其中包含時間戳記、層級、分類、訊息、可選結構化屬性、例外文字與可選 scope。

```csharp
var formatter = new ConsoleFormatter();
var sink = new ConsoleSink(formatter);
```

### 結構化記錄

結構化資料是 log event 本身的一部分。

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

這些屬性會保留在 `LogEntry.Properties` 上，再由 sink 或 formatter 決定如何消費。

例如，`ConsoleFormatter` 會把它們附加成緊湊的文字形式：

```text
[2026-04-05 11:30:42.123] WARNING   [MyService] Cache miss {cacheKey="user:42", node="edge-a", attempt=3}
```

## 記錄擴充方法

內建擴充方法定義在 `ILogger` 與 `ILogger<T>` 上：

- `Trace`, `Debug`, `Info`, `Notice`, `Warning`, `Error`, `Critical`, `Alert`, `Emergency`
- 非同步對應方法，例如 `InfoAsync` 與 `ErrorAsync`
- `LogStructured` 與 `LogStructuredAsync`，作為對原生 property-aware `ILogger` overload 的便捷包裝
- `ILoggerFactory` 與 `ILogSink` 上盡力而為的 `FlushAsync()` 擴充

`ILoggerFactory.FlushAsync()` 擴充位於 `PicoLog` 套件中，而不是 `PicoLog.Abs`。嚴格的執行期能力仍由 `IFlushableLoggerFactory` 表達，而擴充方法讓常見呼叫保持簡單。

## PicoDI 整合

`AddPicoLog()` 會註冊：

- 一個 singleton `ILoggerFactory`
- 型別化 `ILogger<T>` 介面卡
- 在未明確設定 `WriteTo` 管線時的內建預設 sink 行為
- 啟用 `ReadFrom.RegisteredSinks()` 時對 DI 已註冊 sink 的可選橋接

對於新程式碼，請優先把 `WriteTo` sink builder 當作主要設定路徑。

```csharp
container.AddPicoLog(options =>
{
    options.MinLevel = LogLevel.Info;
    options.WriteTo.ColoredConsole();
    options.WriteTo.File("logs/app.log");
});
```

你也可以橋接已經註冊到 PicoDI 的 sink：

```csharp
container.Register(new SvcDescriptor(typeof(ILogSink), _ => new AuditSink()));

container.AddPicoLog(options =>
{
    options.ReadFrom.RegisteredSinks();
    options.WriteTo.ColoredConsole();
});
```

## 指標

核心 `PicoLog` 套件會透過 `System.Diagnostics.Metrics` 發出一組精簡內建指標，使用 meter 名稱 `PicoLog`。

- `picolog.entries.enqueued`
- `picolog.entries.dropped`
- `picolog.sinks.failures`
- `picolog.writes.rejected_after_shutdown`
- `picolog.queue.entries`
- `picolog.shutdown.drain.duration`

這些 instrument 有意保持低基數與輕量。

```csharp
using var listener = new MeterListener();

listener.InstrumentPublished = (instrument, meterListener) =>
{
    if (instrument.Meter.Name == PicoLogMetrics.MeterName)
        meterListener.EnableMeasurementEvents(instrument);
};

listener.Start();
```

## AOT 相容性

範例專案會在啟用 Native AOT 的情況下發佈。

```xml
<PropertyGroup>
  <PublishAOT>true</PublishAOT>
</PropertyGroup>
```

```bash
dotnet publish -c Release -r win-x64 -p:PublishAOT=true
```

此儲存庫也包含一個發佈層級的驗證指令碼，會發佈範例、執行產生的可執行檔，並確認最後的關閉記錄項目已正確刷新：

```powershell
./scripts/Validate-AotSample.ps1
```

## 從原始碼建置

### 先決條件

- .NET SDK 10.0 或更新版本
- Git

### 複製並建置

```bash
git clone https://github.com/PicoHex/PicoLog.git
cd PicoLog
dotnet restore
dotnet build --configuration Release
```

### 執行測試

```bash
dotnet test --solution ./PicoLog.slnx --configuration Release
```

## 效能說明

- logger 執行個體會在 `LoggerFactory` 內依分類快取
- `LoggerFactory` 為每個分類擁有一個有界通道、一個分類管線，以及一個背景排空工作
- `FlushAsync()` 是針對 flush 快照之前已接受項目的 barrier，不是釋放的捷徑
- factory 釋放時仍會在釋放 sink 前執行最終排空
- `FileSink` 會在自己的有界佇列上批次寫入，並透過 `IFlushableLogSink` 暴露 sink 層級 flush
- 選擇 `DropOldest`、`DropWrite` 或 `Wait` 是吞吐量與交付之間的取捨，不是正確性錯誤

## 基準測試

儲存庫包含 `benchmarks/PicoLog.Benchmarks`，這是一個以 PicoBench 為基礎的基準測試專案，用來比較 PicoLog 與 Microsoft.Extensions.Logging 基線的交接成本。

- `MicrosoftAsyncHandoff` 是輕量的字串通道 MEL 基線。
- `MicrosoftAsyncEntryHandoff` 是更公平的完整項目 MEL 基線，它會模擬 PicoLog 的 timestamp/category/message envelope 成本，但不加入真實 I/O。
- 像 `PicoWaitControl_*` 這樣的 wait-mode benchmark 名稱只是內部基準場景標籤，不是公共 API 名稱。

執行基準測試專案：

```bash
dotnet run -c Release --project benchmarks/PicoLog.Benchmarks
```

或直接發佈並執行產物：

```bash
dotnet publish benchmarks/PicoLog.Benchmarks/PicoLog.Benchmarks.csproj -c Release -r win-x64
benchmarks/PicoLog.Benchmarks/bin/Release/net10.0/win-x64/publish/PicoLog.Benchmarks.exe main
```

## 擴充 PicoLog

### 自訂 Sink

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

如果某個 sink 沒有實作 `IFlushableLogSink`，`ILogSink.FlushAsync()` 擴充會以盡力而為方式立即完成。

### 自訂 Formatter

```csharp
public sealed class CustomFormatter : ILogFormatter
{
    public string Format(LogEntry entry)
    {
        return $"{entry.Timestamp:yyyy-MM-dd HH:mm:ss} [{entry.Level}] {entry.Message}";
    }
}
```

## 測試

目前測試套件涵蓋：

- 檔案 sink 寫入與非同步釋放競爭
- 依分類快取 logger
- 最低層級篩選
- 結構化負載擷取與格式化
- 內建指標發出
- 在 factory 釋放時擷取 scope 並完成刷新
- 已接受項目的 flush 屏障與盡力而為的 flush 擴充
- 在關閉開始後拒絕寫入
- sink 失敗隔離
- 非同步尾端訊息刷新
- 真實檔案 sink 尾端持久化
- 已設定的 DI 檔案輸出
- PicoDI 型別化 logger 解析

範例也會透過 Native AOT 發佈與執行來驗證。

## 適用場景與非目標

### 優勢

- 核心實作很小，也容易推理：`LoggerFactory` 擁有依分類的註冊、處理管線、排空工作生命週期與 sink 生命週期，而每個 `InternalLogger` 仍是輕量、非擁有型的寫入門面。
- 專案對 AOT 友善，並避免依賴大量反射的基礎設施。
- 結構化屬性與內建指標覆蓋常見營運需求，同時不會強迫應用程式接入更龐大的記錄生態。
- 佇列壓力行為是明確的，而不是隱藏的。
- flush 語義保持明確：`FlushAsync()` 是已接受工作的 barrier，`DisposeAsync()` 則負責最終 shutdown drain 和資源釋放。
- 內建的 PicoDI 整合保持輕薄且可預測。

### 適合的場景

- 想要輕量記錄核心、又不想引入更大記錄生態的小型到中型 .NET 應用
- 對啟動成本、二進位大小和 AOT 相容性有要求的邊緣、IoT、桌面與工具型工作負載
- 可以接受盡力交付、偶爾需要執行中 flush barrier，而且明確關閉排空已足夠的應用程式記錄情境
- 偏好少量基礎原語，並願意按需加入自訂 sink 或 formatter 的團隊

### 非目標與薄弱點

- 這不是完整的可觀測性平台。
- 它沒有針對超高基數的 logger category 做最佳化，因為目前設計會為每個分類建立一個 factory 擁有的分類管線與一個背景排空工作。
- 對於不能接受靜默遺失的稽核或合規記錄情境，它並不是預設合適的選擇。
- 內建指標有意保持精簡，不嘗試建模更大的端對端遙測系統。

## 貢獻

1. Fork 儲存庫
2. 建立功能分支
3. 進行你的變更
4. 新增或更新測試
5. 提交 pull request

## 授權

本專案採用 MIT License，詳情請參閱 [LICENSE](LICENSE) 檔案。
