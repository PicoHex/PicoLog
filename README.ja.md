# PicoLog

[English](README.md) | [简体中文](README.zh.md) | [繁體中文](README.zh-TW.md) | [Deutsch](README.de.md) | [Español](README.es.md) | [Français](README.fr.md) | [日本語](README.ja.md) | [Português (Brasil)](README.pt-BR.md) | [Русский](README.ru.md)

![CI](https://github.com/PicoHex/PicoLog/actions/workflows/ci.yml/badge.svg)
[![NuGet](https://img.shields.io/nuget/v/PicoLog.svg)](https://www.nuget.org/packages/PicoLog)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

PicoLog は、.NET の edge、desktop、utility、IoT ワークロード向けの軽量で AOT フレンドリーなロギングフレームワークです。

現在の設計は意図的に小さく保たれています。

- **1 つの logger モデル**: `ILogger` / `ILogger<T>`
- **1 つの DI エントリポイント**: `AddPicoLog(...)`
- **1 つのライフサイクル所有者**: `ILoggerFactory`

構造化プロパティは別の logger 型ではなく、ログイベントそのものの一部です。sink、formatter、`LogEntry`、flush companion などのランタイムおよび拡張型は `PicoLog` にあり、利用者向けの契約は `PicoLog.Abs` にあります。

## 特長

- **AOT フレンドリーな設計**: リフレクション依存の重い基盤を避け、Native AOT サンプルも含みます
- **有界非同期パイプライン**: logger はエントリを、有界チャネルに支えられたカテゴリごとのパイプラインへ受け渡します
- **明示的なライフサイクル意味論**: `FlushAsync()` は実行中のバリアであり、`DisposeAsync()` はシャットダウン時のドレインです
- **`ILogger` 上の構造化プロパティ**: ネイティブオーバーロードにより、キー/値ペイロードが `LogEntry.Properties` に保持されます
- **小さな DI サーフェス**: `AddPicoLog(...)` は `ILoggerFactory` と型付き `ILogger<T>` アダプターを登録します
- **組み込み sink と formatter**: console、colored console、file、読みやすい text formatter を備えます
- **flush companion 契約**: ランタイムの flush 機能は `IFlushableLoggerFactory` と `IFlushableLogSink` を通じて引き続き利用できます
- **組み込みメトリクス**: queue、drop、sink failure、shutdown メトリクスを `System.Diagnostics.Metrics` で提供します
- **ベンチマークプロジェクト**: PicoLog の handoff コストと MEL ベースラインを比較する PicoBench ベースのベンチマークを用意しています
- **スコープ対応**: ネストされたスコープは `AsyncLocal` を通じて流れ、各 `LogEntry` に付加されます

## プロジェクト構成

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

## インストール

### コアランタイム

```bash
dotnet add package PicoLog
```

### PicoDI 統合

```bash
dotnet add package PicoLog.DI
```

## クイックスタート

### 基本的な使い方

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

`FlushAsync()` は **dispose ではありません**。これは、flush スナップショットより前にすでに受理されていたエントリに対するバリアです。最終的なシャットダウンドレインと sink のクリーンアップには `DisposeAsync()` を使ってください。

### DI 統合

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

`AddPicoLog()` は唯一の DI エントリポイントです。業務コードは通常 `ILogger<T>` に依存するべきです。`ILoggerFactory` は、flush と shutdown を担うアプリルートの明示的なライフサイクル所有者です。

## コアモデル

### 1 つの logger モデル

PicoLog は、もはや logging を「plain」logger インターフェイスと「structured」logger インターフェイスに分割しません。

- `ILogger` / `ILogger<T>` が主な書き込みサーフェスです
- 通常イベントは `Log(level, message, exception?)` を使います
- 構造化イベントは `Log(level, message, properties, exception)` を使います
- 非同期バリアントも `LogAsync(...)` で同じ形に従います

`LogStructured()` と `LogStructuredAsync()` は `LoggerExtensions` の便利なラッパーとして今も存在しますが、ネイティブな `ILogger` オーバーロードの糖衣構文にすぎません。

### パッケージ分割

- **`PicoLog.Abs`**: `ILogger`、`ILogger<T>`、`ILoggerFactory`、`LogLevel`、`LoggerExtensions` などの利用者向け契約
- **`PicoLog`**: `LoggerFactory`、`Logger<T>`、`LogEntry`、`ILogSink`、`ILogFormatter`、`IFlushableLoggerFactory`、`IFlushableLogSink` などのランタイムおよび拡張型
- **`PicoLog.DI`**: `AddPicoLog(...)` による PicoDI 統合

## 設定

### 最小レベル

`LoggerFactory.MinLevel` は、どのエントリを受け入れるかを制御します。数値が低いほど重大なので、既定の `Debug` レベルでは `Emergency` から `Debug` までは通し、`Trace` は除外します。

```csharp
await using var loggerFactory = new LoggerFactory(sinks)
{
    MinLevel = LogLevel.Warning
};
```

### ライフサイクル所有権

`LoggerFactory` が所有するもの:

- カテゴリごとにキャッシュされた logger
- カテゴリごとのパイプライン
- バックグラウンドのドレインタスク
- 登録された sink

つまり、ライフサイクル API は `ILogger<T>` ではなく factory の話に属します。

```csharp
await using var loggerFactory = new LoggerFactory(sinks);

var logger = new Logger<MyService>(loggerFactory);
logger.Info("Starting up");

await loggerFactory.FlushAsync();   // mid-run barrier
await loggerFactory.DisposeAsync(); // final shutdown drain
```

DI から `ILoggerFactory` を解決しても、それは依然としてアプリ全体の singleton ライフサイクル所有者です。scope から解決したからといって、その scope が所有するわけでは **ありません**。

### キュー圧力

`LoggerFactoryOptions.QueueFullMode` は、同期書き込みと非同期書き込みの両方でキュー圧力を明示します。

`LogAsync()` の完了は、logger 境界での handoff 処理がそこで完了したことを意味します。エントリは次のいずれかだった可能性があります。

- 受理された
- キューポリシーにより破棄された
- シャットダウン中に拒否された

これは、sink が永続的な書き込みを完了したことを意味するものでは **ありません**。

- `DropOldest` は、最も古いキュー済みエントリを破棄して、logging を非ブロッキングに保ちます。これが既定です。
- `DropWrite` は新しいエントリを拒否し、その drop を `OnMessagesDropped` で報告します。
- `Wait` は同期 logging を `SyncWriteTimeout` までブロックし、非同期 logging ではキャンセル要求が来るまでキューの空きを待機させます。

```csharp
var options = new LoggerFactoryOptions
{
    MinLevel = LogLevel.Info,
    QueueFullMode = LogQueueFullMode.Wait,
    SyncWriteTimeout = TimeSpan.FromMilliseconds(500)
};

await using var loggerFactory = new LoggerFactory(sinks, options);
```

## 組み込みランタイム要素

### Sinks

- `ConsoleSink` は単純に整形されたエントリを標準出力へ書き込みます。
- `ColoredConsoleSink` は色変更を直列化し、console 状態が同時書き込み間で漏れないようにします。
- `FileSink` は UTF-8 のファイル書き込みをバックグラウンドキューでバッチ化してからディスクへ flush し、`IFlushableLogSink` による sink レベル flush もサポートします。

`AddPicoLog()` を使う場合、設定された sink は logger factory 内で作成されるため、factory がその寿命の唯一の所有者のままです。

### Formatter

`ConsoleFormatter` は、タイムスタンプ、レベル、カテゴリ、メッセージ、任意の構造化プロパティ、例外テキスト、任意のスコープを含む読みやすい行を生成します。

```csharp
var formatter = new ConsoleFormatter();
var sink = new ConsoleSink(formatter);
```

### 構造化 logging

構造化データはログイベントそのものの一部です。

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

これらのプロパティは `LogEntry.Properties` に保持され、どのように利用するかは sink や formatter が決めます。

たとえば `ConsoleFormatter` は、これらをコンパクトなテキスト形式で付加します。

```text
[2026-04-05 11:30:42.123] WARNING   [MyService] Cache miss {cacheKey="user:42", node="edge-a", attempt=3}
```

## Logging 拡張

同梱されている拡張メソッドは `ILogger` と `ILogger<T>` に定義されています。

- `Trace`, `Debug`, `Info`, `Notice`, `Warning`, `Error`, `Critical`, `Alert`, `Emergency`
- `InfoAsync` や `ErrorAsync` のような非同期版
- プロパティ対応のネイティブ `ILogger` オーバーロードに対する便利ラッパーとしての `LogStructured` と `LogStructuredAsync`
- `ILoggerFactory` と `ILogSink` に対する best-effort の `FlushAsync()` 拡張

`ILoggerFactory.FlushAsync()` 拡張は `PicoLog` にあり、`PicoLog.Abs` にはありません。厳密なランタイム機能は `IFlushableLoggerFactory` のままで、拡張は一般的な呼び出し位置をシンプルに保ちます。

## PicoDI 統合

`AddPicoLog()` が登録するもの:

- singleton の `ILoggerFactory`
- 型付き `ILogger<T>` アダプター
- 明示的な `WriteTo` パイプラインが構成されていない場合の組み込み既定 sink 挙動
- `ReadFrom.RegisteredSinks()` が有効な場合の、DI 登録済み sink の任意ブリッジ

新しいコードでは、主な設定経路として `WriteTo` sink builder を優先してください。

```csharp
container.AddPicoLog(options =>
{
    options.MinLevel = LogLevel.Info;
    options.WriteTo.ColoredConsole();
    options.WriteTo.File("logs/app.log");
});
```

すでに PicoDI に登録されている sink をブリッジすることもできます。

```csharp
container.Register(new SvcDescriptor(typeof(ILogSink), _ => new AuditSink()));

container.AddPicoLog(options =>
{
    options.ReadFrom.RegisteredSinks();
    options.WriteTo.ColoredConsole();
});
```

## メトリクス

コア `PicoLog` パッケージは、meter 名 `PicoLog` を使って `System.Diagnostics.Metrics` 経由で小さな組み込みメトリクス面を公開します。

- `picolog.entries.enqueued`
- `picolog.entries.dropped`
- `picolog.sinks.failures`
- `picolog.writes.rejected_after_shutdown`
- `picolog.queue.entries`
- `picolog.shutdown.drain.duration`

これらのインストゥルメントは意図的に低カーディナリティで軽量です。

```csharp
using var listener = new MeterListener();

listener.InstrumentPublished = (instrument, meterListener) =>
{
    if (instrument.Meter.Name == PicoLogMetrics.MeterName)
        meterListener.EnableMeasurementEvents(instrument);
};

listener.Start();
```

## AOT 互換性

サンプルプロジェクトは Native AOT を有効にして publish されます。

```xml
<PropertyGroup>
  <PublishAOT>true</PublishAOT>
</PropertyGroup>
```

```bash
dotnet publish -c Release -r win-x64 -p:PublishAOT=true
```

このリポジトリには、サンプルを publish し、生成された実行ファイルを走らせ、最終的な shutdown ログエントリが正しく flush されたことを検証する publish レベルの検証スクリプトも含まれています。

```powershell
./scripts/Validate-AotSample.ps1
```

## ソースからビルドする

### 前提条件

- .NET SDK 10.0 以降
- Git

### クローンしてビルドする

```bash
git clone https://github.com/PicoHex/PicoLog.git
cd PicoLog
dotnet restore
dotnet build --configuration Release
```

### テストを実行する

```bash
dotnet test --solution ./PicoLog.slnx --configuration Release
```

## パフォーマンスに関する注記

- logger インスタンスは `LoggerFactory` 内でカテゴリごとにキャッシュされます
- `LoggerFactory` はカテゴリごとに 1 つの有界チャネル、1 つのカテゴリパイプライン、1 つのバックグラウンドドレインタスクを所有します
- `FlushAsync()` は flush スナップショット前に受理されたエントリに対するバリアであり、dispose の近道ではありません
- factory の dispose は、sink を dispose する前に最終ドレインを引き続き行います
- `FileSink` は独自の有界キューで書き込みをバッチ化し、`IFlushableLogSink` を通じて sink レベル flush を公開します
- `DropOldest`、`DropWrite`、`Wait` の選択は、スループットと配送のトレードオフであり、正しさのバグではありません

## ベンチマーク

このリポジトリには `benchmarks/PicoLog.Benchmarks` が含まれており、PicoLog の handoff コストを Microsoft.Extensions.Logging ベースラインと比較する PicoBench ベースのベンチマークプロジェクトになっています。

- `MicrosoftAsyncHandoff` は、軽量な文字列チャネル MEL ベースラインです。
- `MicrosoftAsyncEntryHandoff` は、PicoLog の timestamp/category/message エンベロープコストを、実際の I/O を追加せずに反映する、より公正な完全エントリ MEL ベースラインです。
- `PicoWaitControl_*` のような wait モードのベンチマーク名は、公開 API 名ではなく内部的なベンチマークシナリオラベルです。

ベンチマークプロジェクトを実行します。

```bash
dotnet run -c Release --project benchmarks/PicoLog.Benchmarks
```

または、成果物を直接 publish して実行します。

```bash
dotnet publish benchmarks/PicoLog.Benchmarks/PicoLog.Benchmarks.csproj -c Release -r win-x64
benchmarks/PicoLog.Benchmarks/bin/Release/net10.0/win-x64/publish/PicoLog.Benchmarks.exe main
```

## PicoLog を拡張する

### カスタム Sink

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

sink が `IFlushableLogSink` を実装していない場合、`ILogSink.FlushAsync()` 拡張は best-effort であり、すぐに完了します。

### カスタム Formatter

```csharp
public sealed class CustomFormatter : ILogFormatter
{
    public string Format(LogEntry entry)
    {
        return $"{entry.Timestamp:yyyy-MM-dd HH:mm:ss} [{entry.Level}] {entry.Message}";
    }
}
```

## テスト

現在のテストスイートがカバーしているもの:

- 非同期 dispose と競合する file sink 書き込み
- カテゴリ別の logger キャッシュ
- 最小レベルによるフィルタリング
- 構造化ペイロードの取得と整形
- 組み込みメトリクスの発行
- factory dispose 時の scope 取得と flush
- 受理済みエントリに対する flush バリアと best-effort flush 拡張
- shutdown 開始後の書き込み拒否
- sink failure の分離
- 非同期 tail-message flush
- 実ファイル sink における tail の永続化
- 設定済み DI file 出力
- PicoDI 型付き logger 解決

サンプルは Native AOT publish と実行でも検証されます。

## 適合する用途と非目標

### 強み

- コア実装は小さく、把握しやすい構造です。`LoggerFactory` はカテゴリごとの登録、パイプライン、ドレインタスクの寿命、sink の寿命を所有し、各 `InternalLogger` は軽量で所有権を持たない書き込みファサードのままです。
- このプロジェクトは AOT フレンドリーで、リフレクション依存の重い基盤を避けています。
- 構造化プロパティと組み込みメトリクスは、より大きな logging エコシステムをアプリケーションに持ち込まずに、一般的な運用要件をカバーします。
- キュー圧力の挙動は隠されず、明示的です。
- flush の意味論は明確なままです。`FlushAsync()` はすでに受理された作業に対するバリアであり、`DisposeAsync()` は最終ドレインとリソース解放のための shutdown 経路です。
- 組み込みの PicoDI 統合は薄く、予測しやすいままです。

### 適したケース

- 大きな logging エコシステムを採用せず、軽量な logging コアを求める中小規模の .NET アプリケーション
- 起動コスト、バイナリサイズ、AOT 互換性が重要な edge、IoT、desktop、utility 系ワークロード
- best-effort 配送を許容でき、実行中の flush バリアがときどき役立ち、明示的な shutdown drain で十分なアプリケーション logging シナリオ
- 小さなプリミティブ集合を好み、必要に応じてカスタム sink や formatter を追加できるチーム

### 非目標と弱い点

- これは完全な observability プラットフォームではありません。
- 現在の設計ではカテゴリごとに factory 所有のパイプラインとバックグラウンドドレインタスクを 1 つずつ作成するため、非常に高カーディナリティな logger カテゴリには最適化されていません。
- サイレントロスが許されない監査やコンプライアンス logging には、既定の選択肢ではありません。
- 組み込みメトリクスは意図的に小さく、より大きなエンドツーエンドのテレメトリシステムをモデル化しようとはしていません。

## コントリビュート

1. リポジトリを fork する
2. 機能ブランチを作成する
3. 変更を加える
4. テストを追加または更新する
5. Pull Request を送る

## ライセンス

このプロジェクトは MIT License の下で提供されています。詳細は [LICENSE](LICENSE) ファイルを参照してください。
