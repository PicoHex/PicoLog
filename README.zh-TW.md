# PicoLog

[English](README.md) | [简体中文](README.zh.md) | [繁體中文](README.zh-TW.md) | [Deutsch](README.de.md) | [Español](README.es.md) | [Français](README.fr.md) | [日本語](README.ja.md) | [Português (Brasil)](README.pt-BR.md) | [Русский](README.ru.md)

![CI](https://github.com/PicoHex/PicoLog/actions/workflows/ci.yml/badge.svg)
[![NuGet](https://img.shields.io/nuget/v/PicoLog.svg)](https://www.nuget.org/packages/PicoLog)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

一個輕量、適合 AOT 的 .NET 記錄框架，面向邊緣與 IoT 工作負載。此儲存庫包含契約、核心記錄實作、PicoDI 整合、範例應用程式，以及專用的基準測試專案。

## 功能特色

- **AOT 相容性**: 目標為 `net10.0`，並避免依賴大量反射的基礎設施
- **有界非同步管線**: logger 會將項目放入有界通道，並由背景工作將資料寫入 sink
- **顯式 flush 介面**: `IFlushableLoggerFactory`、`IFlushableLogSink` 與盡力而為的 `FlushAsync()` 擴充，讓執行中的 flush barrier 更明確，不會和 shutdown 混在一起
- **結構化屬性**: 可選的鍵值承載會透過 `LogEntry.Properties` 與內建格式化器輸出流動
- **內建指標**: 核心套件會透過 `System.Diagnostics.Metrics` 發出佇列、丟棄、sink 失敗與關閉相關指標
- **精簡表面積**: 只需要實作少量核心抽象，就能擴充整個系統
- **PicoDI 整合**: 內建 logger factory 與型別化 logger 的註冊，並支援透過 `WriteTo` 設定 sink，以及透過 `ReadFrom.RegisteredSinks()` 選擇性橋接 PicoDI 中已註冊的 sink
- **基準測試涵蓋**: 內含以 PicoBench 為基礎的基準測試專案，提供輕量與更公平的 MEL 非同步交接基線
- **Scope 支援**: 巢狀 scope 會透過 `AsyncLocal` 流動，並附加到每個 `LogEntry`

## 專案結構

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

## 安裝

### 核心記錄程式庫

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

await loggerFactory.FlushAsync(); // 目前已接受項目的 barrier，不等於 shutdown
```

`FlushAsync()` 在 factory 上不是釋放。它會等待 flush snapshot 之前已接受的項目穿過 factory pipeline，而 `DisposeAsync()` 仍然是負責最終 drain 與資源釋放的 shutdown 路徑。

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

`AddPicoLog()` 是聚焦的 DI-first 入口。預設容器表面維持精簡：用 `ILogger<T>` 寫入、用 `IPicoLogControl` 做明確 flush / shutdown。

## 設定

### 最低層級

`LoggerFactory.MinLevel` 會控制哪些項目會被接受。數值越小代表嚴重性越高，因此預設的 `Debug` 層級會允許 `Emergency` 到 `Debug`，但會過濾 `Trace`。

```csharp
await using var loggerFactory = new LoggerFactory(sinks)
{
    MinLevel = LogLevel.Warning
};
```

### 生命週期擁有權

`LoggerFactory` 擁有依分類快取的 logger、各分類管線、這些管線執行的背景排空工作，以及已註冊的 sink。`FlushAsync()` 是執行中的 barrier，不是釋放。它會等待 flush snapshot 之前已接受的項目穿過 factory pipeline。關閉時請使用 `DisposeAsync()` 完成最終 drain 並釋放 sink 資源。一旦開始釋放，新的寫入會被拒絕，而已經入列的項目仍會持續排空。

```csharp
await using var loggerFactory = new LoggerFactory(sinks);

var logger = new Logger<MyService>(loggerFactory);
logger.Info("Starting up");
await loggerFactory.FlushAsync();

// FlushAsync 是目前已接受項目的 barrier。
// DisposeAsync 會做最終 drain，並拒絕與 shutdown 競爭的寫入。
```

### 佇列壓力

`LoggerFactoryOptions.QueueFullMode` 讓同步與非同步寫入在佇列承壓時的處理方式保持明確。`LogAsync()` 與 `LogStructuredAsync()` 完成時，表示 logger 邊界的交接處理已經結束：項目可能已被接受、因佇列策略而丟棄，或在關閉期間被拒絕，而 `Wait` 模式下的背壓處理也在該邊界完成，並不表示 sink 已完成持久化:

- `DropOldest` 會丟棄佇列中最舊的項目，以維持非阻塞記錄。這是預設值。
- `DropWrite` 會拒絕新項目，並透過 `OnMessagesDropped` 回報丟棄情況。
- `Wait` 會讓同步記錄最多阻塞到 `SyncWriteTimeout`，並讓非同步記錄等待佇列空間，直到要求取消。

```csharp
var options = new LoggerFactoryOptions
{
    MinLevel = LogLevel.Info,
    QueueFullMode = LogQueueFullMode.Wait,
    SyncWriteTimeout = TimeSpan.FromMilliseconds(500)
};

await using var loggerFactory = new LoggerFactory(sinks, options);
```

### 內建 Sink

- `ConsoleSink` 會將格式化後的純文字項目寫入標準輸出。
- `ColoredConsoleSink` 會將顏色切換序列化，避免主控台狀態在並行寫入之間外漏。
- `FileSink` 會先在背景佇列中批次處理 UTF-8 檔案寫入，再沖刷到磁碟，並透過 `IFlushableLogSink` 支援 sink 層級 flush。`AddLogging()` 會在 logger factory 內建立已設定的 sink，因此 factory 仍是其生命週期的唯一擁有者。

### PicoDI 預設註冊

`AddLogging()` 會註冊:

- 單例 `ILoggerFactory`
- 型別化 `ILogger<T>` 介面卡
- 型別化 `IStructuredLogger<T>` 介面卡
- 在未明確設定 sink 管線時保留 legacy 預設 sinks
- 在啟用 `ReadFrom.RegisteredSinks()` 時附加 PicoDI 中已註冊的 sinks

在應用程式執行期間，你可以把 `ILoggerFactory.FlushAsync()` 當作盡力而為的 barrier 來呼叫。可用時它會轉送到 `IFlushableLoggerFactory`，否則會立即完成。關閉時，請明確釋放已解析的 factory，讓排隊中的記錄項目能在程序結束前排空完成。關閉開始後抵達的寫入會被拒絕，而不是在過晚時才被接受。

對於新程式碼，優先使用 `WriteTo` sink builder，讓內建 sink 與自訂 sink 共享同一條設定路徑。

```csharp
container.AddLogging(options =>
{
    options.MinLevel = LogLevel.Info;
    options.WriteTo.ColoredConsole();
    options.WriteTo.File("logs/app.log");
});
```

如果你已經把 sink 註冊到 PicoDI，也可以透過 `ReadFrom.RegisteredSinks()` 把它們橋接進日誌管線。

```csharp
container.Register(new SvcDescriptor(typeof(ILogSink), _ => new AuditSink()));

container.AddLogging(options =>
{
    options.ReadFrom.RegisteredSinks();
    options.WriteTo.ColoredConsole();
});
```

### 內建格式化器

`ConsoleFormatter` 會產生易讀的文字行，其中包含時間戳記、層級、分類、訊息、可選的結構化屬性、例外文字與可選 scope。

```csharp
var formatter = new ConsoleFormatter();
var sink = new ConsoleSink(formatter);
```

### 結構化記錄

PicoLog 會維持與 `ILogger` 的相容性，並透過 `IStructuredLogger` / `IStructuredLogger<T>` 提供保證的結構化記錄能力。當你使用內建 `LoggerFactory` 時，執行期 logger 會將 `LogStructured()` / `LogStructuredAsync()` 的承載保留在 `LogEntry.Properties` 上。

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

`ConsoleFormatter` 會在訊息後方以緊湊的文字形式附加結構化屬性，例如:

```text
[2026-04-05 11:30:42.123] WARNING   [MyService] Cache miss {cacheKey="user:42", node="edge-a", attempt=3}
```

### 記錄擴充方法

隨附的擴充方法定義在 `ILogger` 與 `ILogger<T>` 上:

- `Trace`, `Debug`, `Info`, `Notice`, `Warning`, `Error`, `Critical`, `Alert`, `Emergency`
- 非同步對應方法，例如 `InfoAsync` 與 `ErrorAsync`
- `LogStructured` 與 `LogStructuredAsync` 是盡力而為的介面卡，當執行期 logger 實作 `IStructuredLogger` 時會保留屬性，否則會退回成不含結構化承載的一般記錄
- `ILoggerFactory` 與 `ILogSink` 上盡力而為的 `FlushAsync()` 擴充，執行期型別支援 flush 時會轉送，否則會立即完成

如果你需要嚴格的結構化記錄契約，請直接依賴 `IStructuredLogger` / `IStructuredLogger<T>`。如果你需要嚴格的 flush 契約，請直接依賴 `IFlushableLoggerFactory` 或 `IFlushableLogSink`。

### 溢位行為

同步與非同步記錄都會寫入有界通道。

- 同步 `Log()` 與非同步 `LogAsync()` 都遵循 `LoggerFactoryOptions.QueueFullMode`。
- `LogAsync()` 或 `LogStructuredAsync()` 完成時，表示 logger 邊界的交接處理已經結束。項目可能已被接受、因佇列策略而丟棄，或在關閉期間被拒絕，並不表示 sink 已完成持久化。
- 預設值是 `DropOldest`，它偏向吞吐量與較低的呼叫端延遲，而不是保證交付。
- `Wait` 會讓背壓對呼叫端可見，同步寫入會阻塞到佇列空間可用或 `SyncWriteTimeout` 到期，非同步寫入則會等待佇列空間直到要求取消。
- `DropWrite` 會保留較早排入的項目，並透過 `OnMessagesDropped` 回報被丟棄的新項目。
- 一旦 factory 開始釋放，新的寫入會被拒絕，而已入列的項目仍會持續沖刷。

對一般應用程式記錄來說，預設的 `DropOldest` 行為通常可以接受。若是稽核型記錄，請優先考慮 `Wait` 或專用 sink 策略。

### 內建指標

核心 `PicoLog` 套件會透過 `System.Diagnostics.Metrics` 發出一組精簡的內建指標，所使用的 meter 名稱是 `PicoLog`。

- `picolog.entries.enqueued`
- `picolog.entries.dropped`
- `picolog.sinks.failures`
- `picolog.writes.rejected_after_shutdown`
- `picolog.queue.entries`
- `picolog.shutdown.drain.duration`

這些 instrument 的設計重點是低基數與輕量。你可以直接用 `MeterListener` 觀察，也可以橋接到更廣泛的遙測基礎設施。

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

範例專案會在啟用 Native AOT 的情況下發佈:

```xml
<PropertyGroup>
  <PublishAOT>true</PublishAOT>
</PropertyGroup>
```

```bash
dotnet publish -c Release -r win-x64 -p:PublishAOT=true
```

此儲存庫也包含一個發佈層級的驗證指令碼，會發佈範例、執行產生的可執行檔，並確認最後的關閉記錄項目已正確沖刷:

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

## 效能考量

- `LoggerFactory` 內部會依分類快取 logger 執行個體。
- `LoggerFactory` 為每個分類擁有一個有界通道、一個分類管線，以及一個背景排空工作。
- factory 上的 `FlushAsync()` 是針對 flush snapshot 之前已接受項目的 barrier，不是釋放的捷徑。
- 釋放 factory 仍然會在釋放 sink 前先做最終 drain。
- `FileSink` 會在自己的有界佇列上批次寫入，並在批次邊界或 flush 間隔邊界時沖刷，也會透過 `IFlushableLogSink` 暴露 sink 層級 flush。
- 選擇 `DropOldest`、`DropWrite` 或 `Wait` 是吞吐量與交付之間的取捨，不是正確性錯誤。

## 基準測試

儲存庫包含 `benchmarks/PicoLog.Benchmarks`，這是一個以 PicoBench 為基礎的基準測試專案，用來比較 PicoLog 與 Microsoft.Extensions.Logging 基線之間的交接成本。

- `MicrosoftAsyncHandoff` 是輕量的字串通道 MEL 基線。
- `MicrosoftAsyncEntryHandoff` 是更公平的完整項目 MEL 基線，它會模擬 PicoLog 的 timestamp/category/message envelope 成本，同時不加入真實 I/O。
- `PicoWaitControl_CachedMessage` 與 `PicoWaitHandoff_CachedMessage` 涵蓋 PicoLog 在 wait 模式下的背壓表現，作為相對比較。

執行基準測試專案:

```bash
dotnet run -c Release --project benchmarks/PicoLog.Benchmarks
```

或直接發佈並執行產物:

```bash
dotnet publish benchmarks/PicoLog.Benchmarks/PicoLog.Benchmarks.csproj -c Release -r win-x64
benchmarks/PicoLog.Benchmarks/bin/Release/net10.0/win-x64/publish/PicoLog.Benchmarks.exe main
```

基準測試應用程式會寫出:

- `benchmark-results.md`
- `benchmark-results-main.md`
- `benchmark-results-wait.md`

## 適用場景與非目標

### 優勢

- 核心實作很小，也容易推理: `LoggerFactory` 擁有依分類的 logger 註冊、分類管線、排空工作生命週期與 sink 生命週期，而每個 `InternalLogger` 仍是輕量、非擁有型的寫入外觀。
- 專案對 AOT 友善，並避免依賴大量反射的基礎設施，因此很適合 Native AOT、邊緣與 IoT 工作負載。
- 結構化屬性與內建指標覆蓋常見的營運需求，同時不會把更龐大的記錄生態系強加到應用程式中。
- 佇列壓力行為是明確的，不是隱藏起來的。呼叫端可以在 `DropOldest`、`DropWrite` 與 `Wait` 之間選擇，視吞吐量或交付何者更重要而定。
- flush 語意保持明確。`FlushAsync()` 是已接受工作的 barrier，而 `DisposeAsync()` 仍然是負責最終 drain 與資源釋放的 shutdown 路徑。
- 內建的 PicoDI 整合保持輕薄且可預測，不會引入龐大的 hosting 或設定堆疊。

### 適合的場景

- 想要輕量記錄核心，又不想採用更大記錄生態系的小型到中型 .NET 應用程式。
- 在邊緣、IoT、桌面與工具型工作負載中，啟動成本、二進位大小與 AOT 相容性很重要。
- 可以接受盡力交付、偶爾需要執行中 flush barrier，而且明確的關閉排空已足夠的應用程式記錄情境。
- 偏好少量基礎原語，並願意視需要加入自訂 sink 或格式化器的團隊。

### 非目標與弱點

- 這不是完整的可觀測性平台。它支援結構化屬性與少量內建指標，但不提供訊息範本剖析、enricher、滾動檔案管理、遠端傳輸 sink，或與更廣泛遙測生態的深度整合。
- 它沒有針對非常高基數的 logger 分類做最佳化。現在的設計會為每個分類建立一個 factory 擁有的分類管線與一個背景排空工作。
- 對於不能接受靜默遺失的稽核或合規記錄，它不是預設適合的選擇。預設佇列模式偏向吞吐量，`LogAsync()` 完成也不代表 sink 已完成持久化，而更強的交付保證需要明確設定，例如 `Wait` 或專用 sink 策略。
- 內建指標涵蓋佇列深度、已接受與已丟棄的項目、sink 失敗、關閉期間的延遲寫入，以及關閉排空持續時間。它們目前仍未提供依分類區分的指標、sink 延遲直方圖，或更大的端對端遙測模型。

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

如果 sink 沒有實作 `IFlushableLogSink`，`ILogSink.FlushAsync()` 擴充會以盡力而為的方式立即完成。

### 自訂格式化器

```csharp
public class CustomFormatter : ILogFormatter
{
    public string Format(LogEntry entry)
    {
        return $"{entry.Timestamp:yyyy-MM-dd HH:mm:ss} [{entry.Level}] {entry.Message}";
    }
}
```

## 測試

目前的測試套件涵蓋:

- 檔案 sink 寫入與非同步釋放競爭
- 依分類快取 logger
- 最低層級篩選
- 結構化承載擷取與格式化
- 內建指標發出
- 在 factory 釋放時擷取 scope 並完成沖刷
- 已接受項目的 flush barrier 與盡力而為的 flush 擴充
- 在關閉開始後拒絕寫入
- sink 失敗隔離
- 非同步尾端訊息沖刷
- 真實檔案 sink 尾端持久化
- 已設定的 DI 檔案輸出
- PicoDI 型別化 logger 解析

範例也會透過 Native AOT 發佈與執行來驗證。

## 貢獻

1. Fork 儲存庫
2. 建立功能分支
3. 進行你的變更
4. 新增或更新測試
5. 提交 pull request

## 授權

本專案採用 MIT License，詳情請參閱 [LICENSE](LICENSE) 檔案。
