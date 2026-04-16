# PicoLog

[English](README.md) | [简体中文](README.zh.md) | [繁體中文](README.zh-TW.md) | [Deutsch](README.de.md) | [Español](README.es.md) | [Français](README.fr.md) | [日本語](README.ja.md) | [Português (Brasil)](README.pt-BR.md) | [Русский](README.ru.md)

![CI](https://github.com/PicoHex/PicoLog/actions/workflows/ci.yml/badge.svg)
[![NuGet](https://img.shields.io/nuget/v/PicoLog.svg)](https://www.nuget.org/packages/PicoLog)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

.NET の edge および IoT ワークロード向けに設計された、軽量で AOT フレンドリーなロギングフレームワークです。このリポジトリには、契約、コアロガー実装、PicoDI 統合、サンプルアプリ、および専用のベンチマークプロジェクトが含まれています。

## 特長

- **AOT 互換性**: `net10.0` を対象とし、リフレクション依存の重い基盤を避けます
- **有界非同期パイプライン**: logger はエントリを有界チャネルに投入し、バックグラウンドタスクで sink に書き込みます
- **明示的な flush 補助インターフェイス**: `IFlushableLoggerFactory`、`IFlushableLogSink`、best-effort の `FlushAsync()` 拡張により、実行中の flush barrier を shutdown と混同せず明示できます
- **構造化プロパティ**: 任意のキー/値ペイロードは `LogEntry.Properties` と組み込みフォーマッタ出力を通じて流れます
- **組み込みメトリクス**: コアパッケージは `System.Diagnostics.Metrics` を通じてキュー、ドロップ、sink failure、shutdown のメトリクスを発行します
- **最小限の表面積**: システムを拡張するために実装すべきコア抽象はごく少数です
- **PicoDI 統合**: logger factory と型付き logger の登録が組み込まれており、既定ではコンソールロギング、ファイルパスが構成されている場合は任意でファイルロギングを使用できます
- **ベンチマーク網羅**: 軽量な MEL async handoff ベースラインと、より公平なベースラインを含む PicoBench ベースのベンチマークプロジェクトを収録しています
- **スコープ対応**: ネストされたスコープは `AsyncLocal` を通じて流れ、各 `LogEntry` に付加されます

## プロジェクト構成

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

## インストール

### コアロギングライブラリ

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

await loggerFactory.FlushAsync(); // ここまでに受理されたエントリの barrier であり、shutdown ではありません
```

factory 上の `FlushAsync()` は破棄ではありません。flush snapshot より前に受理されたエントリが factory pipeline を通過するまで待機します。`DisposeAsync()` は引き続き最終 drain とリソース解放のための shutdown パスです。

### DI 統合

```csharp
using PicoDI;
using PicoDI.Abs;
using PicoLog.Abs;
using PicoLog.DI;

ISvcContainer container = new SvcContainer();

container.AddLogging(options =>
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

await scope.GetService<ILoggerFactory>().FlushAsync();
await scope.GetService<ILoggerFactory>().DisposeAsync();
```

`ILoggerFactory` の `FlushAsync()` 拡張は best-effort です。`IFlushableLoggerFactory` をサポートしていれば転送し、そうでなければ即座に完了します。

## 構成

### 最小レベル

`LoggerFactory.MinLevel` は、どのエントリを受け入れるかを制御します。数値が小さいほど重大度が高いため、既定の `Debug` レベルでは `Emergency` から `Debug` までは許可されますが、`Trace` は除外されます。

```csharp
await using var loggerFactory = new LoggerFactory(sinks)
{
    MinLevel = LogLevel.Warning
};
```

### ライフサイクルの所有権

`LoggerFactory` は、カテゴリごとにキャッシュされた logger、それぞれのカテゴリ別パイプライン、それらのパイプラインで動作するバックグラウンド drain task、そして登録済み sink を所有します。`FlushAsync()` は実行中の barrier であり、破棄ではありません。flush snapshot より前に受理されたエントリが factory pipeline を通過するまで待機します。shutdown 時には `DisposeAsync()` を使って最終 drain と sink リソース解放を行ってください。破棄が始まると、新しい書き込みは拒否され、すでにキューに入っているエントリだけが drain を続けます。

```csharp
await using var loggerFactory = new LoggerFactory(sinks);

var logger = new Logger<MyService>(loggerFactory);
logger.Info("Starting up");
await loggerFactory.FlushAsync();

// FlushAsync は、ここまでに受理されたエントリの barrier です。
// DisposeAsync は最終 drain を行い、shutdown と競合する書き込みを拒否します。
```

### キュー圧力

`LoggerFactoryOptions.QueueFullMode` は、同期書き込みと非同期書き込みの両方におけるキュー圧力時の挙動を明示します。`LogAsync()` と `LogStructuredAsync()` の完了は、logger 境界での handoff 処理が完了したことを意味します。エントリは受理されることもあれば、キューポリシーで破棄されたり、shutdown 中に拒否されたりする可能性があり、`Wait` モードのバックプレッシャー処理もその境界で完了します。sink への永続書き込み完了を意味するわけではありません。

- `DropOldest` は、最も古いキュー済みエントリを破棄することで、非ブロッキングなロギングを維持します。これが既定値です。
- `DropWrite` は新しいエントリを拒否し、`OnMessagesDropped` を通じてドロップを報告します。
- `Wait` は同期ロギングを `SyncWriteTimeout` までブロックし、非同期ロギングではキャンセルが要求されるまでキューの空きを待機します。

```csharp
var options = new LoggerFactoryOptions
{
    MinLevel = LogLevel.Info,
    QueueFullMode = LogQueueFullMode.Wait,
    SyncWriteTimeout = TimeSpan.FromMilliseconds(500)
};

await using var loggerFactory = new LoggerFactory(sinks, options);
```

### 組み込み Sink

- `ConsoleSink` はプレーンに整形されたエントリを標準出力へ書き込みます。
- `ColoredConsoleSink` は色変更を直列化し、同時書き込み間でコンソール状態が漏れないようにします。
- `FileSink` は UTF-8 のファイル書き込みをバックグラウンドキューでバッチ化してからディスクへ flush し、`IFlushableLogSink` による sink レベル flush もサポートします。`AddLogging()` は構成済み sink を logger factory 内で生成するため、factory がそのライフサイクルの唯一の所有者であり続けます。

### PicoDI の既定登録

`AddLogging()` は次を登録します。

- singleton の `ILoggerFactory`
- 型付き `ILogger<T>` アダプタ
- 型付き `IStructuredLogger<T>` アダプタ
- 既定の console sink
- `FilePath` が構成されている場合の任意の file sink

アプリケーションの実行中は、`ILoggerFactory.FlushAsync()` を best-effort の barrier として呼び出せます。利用可能なら `IFlushableLoggerFactory` に転送し、そうでなければ即座に完了します。シャットダウン時には、解決済みの factory を明示的に破棄してください。これにより、プロセス終了前にキュー済みログエントリが drain されます。シャットダウン開始後に到着した書き込みは、遅れて受け付けられるのではなく拒否されます。

ファイルロギングは、任意の `filePath` パラメータ、`options.FilePath` の設定、または configure オーバーロード内での `options.File.FilePath` の設定で有効化できます。明示的なファイルパスは file sink へのオプトインとして扱われます。

```csharp
container.AddLogging(LogLevel.Info, "logs/app.log");
```

```csharp
container.AddLogging(options =>
{
    options.MinLevel = LogLevel.Info;
    options.FilePath = "logs/app.log";
});
```

```csharp
container.AddLogging(options =>
{
    options.MinLevel = LogLevel.Info;
    options.File.FilePath = "logs/app.log";
});
```

### 組み込みフォーマッタ

`ConsoleFormatter` は、タイムスタンプ、レベル、カテゴリ、メッセージ、任意の構造化プロパティ、例外テキスト、および任意のスコープを含む、人間が読みやすい行を生成します。

```csharp
var formatter = new ConsoleFormatter();
var sink = new ConsoleSink(formatter);
```

### 構造化ロギング

PicoLog は `ILogger` との互換性を保ちつつ、`IStructuredLogger` / `IStructuredLogger<T>` を通じて保証された構造化ロギングを提供します。組み込みの `LoggerFactory` を使用すると、ランタイム logger は `LogStructured()` / `LogStructuredAsync()` のペイロードを `LogEntry.Properties` 上に保持します。

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

`ConsoleFormatter` は、たとえば次のように、メッセージの後ろへ構造化プロパティをコンパクトなテキスト形式で追加します。

```text
[2026-04-05 11:30:42.123] WARNING   [MyService] Cache miss {cacheKey="user:42", node="edge-a", attempt=3}
```

### Logging 拡張

提供されている拡張メソッドは `ILogger` と `ILogger<T>` に対して定義されています。

- `Trace`, `Debug`, `Info`, `Notice`, `Warning`, `Error`, `Critical`, `Alert`, `Emergency`
- `InfoAsync` や `ErrorAsync` などの非同期版
- `LogStructured` と `LogStructuredAsync` は best-effort アダプタとして動作し、ランタイム logger が `IStructuredLogger` を実装している場合はプロパティを保持し、それ以外では構造化ペイロードなしの通常ロギングへフォールバックします
- `ILoggerFactory` と `ILogSink` に対する best-effort の `FlushAsync()` 拡張は、ランタイム型が flush をサポートしていれば転送し、そうでなければ即座に完了します

厳密な構造化ロギング契約が必要な場合は、`IStructuredLogger` / `IStructuredLogger<T>` に直接依存してください。厳密な flush 契約が必要な場合は、`IFlushableLoggerFactory` または `IFlushableLogSink` に直接依存してください。

### オーバーフロー時の挙動

同期ロギングと非同期ロギングはいずれも有界チャネルに書き込みます。

- 同期 `Log()` と非同期 `LogAsync()` はどちらも `LoggerFactoryOptions.QueueFullMode` に従います。
- `LogAsync()` または `LogStructuredAsync()` の完了は、logger 境界での handoff 処理が完了したことを意味します。エントリは受理されることもあれば、キューポリシーで破棄されたり、shutdown 中に拒否されたりする可能性があります。sink の永続完了を意味するものではありません。
- 既定値は `DropOldest` で、保証された配信よりもスループットと低い呼び出し元レイテンシを優先します。
- `Wait` は、同期書き込みではキューに空きができるか `SyncWriteTimeout` が経過するまでブロックし、非同期書き込みではキャンセルが要求されるまでキューの空きを待つことで、バックプレッシャーを呼び出し元に可視化します。
- `DropWrite` は古いキュー済みエントリを保持し、新たにドロップされたエントリを `OnMessagesDropped` を通じて報告します。
- factory の破棄が始まると、新しい書き込みは拒否され、すでにキュー済みのエントリだけが flush を継続します。

一般的なアプリケーションロギングでは、既定の `DropOldest` の挙動で十分なことが多いです。監査系ロギングでは、`Wait` または専用 sink 戦略を優先してください。

### 組み込みメトリクス

コア `PicoLog` パッケージは、`System.Diagnostics.Metrics` を通じて、小さな組み込みメトリクス面を `PicoLog` という meter 名で発行します。

- `picolog.entries.enqueued`
- `picolog.entries.dropped`
- `picolog.sinks.failures`
- `picolog.writes.rejected_after_shutdown`
- `picolog.queue.entries`
- `picolog.shutdown.drain.duration`

これらの instrument は、低カーディナリティかつ軽量であるよう設計されています。`MeterListener` で直接観測することも、より広いテレメトリ基盤へ橋渡しすることもできます。

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

このリポジトリには、サンプルを publish し、生成された実行ファイルを実行し、最終的な shutdown log entry が正しく flush されたことを検証する publish レベルの検証スクリプトも含まれています。

```powershell
./scripts/Validate-AotSample.ps1
```

## ソースからビルドする

### 前提条件

- .NET SDK 10.0 以降
- Git

### クローンとビルド

```bash
git clone https://github.com/PicoHex/PicoLog.git
cd PicoLog
dotnet restore
dotnet build --configuration Release
```

### テスト実行

```bash
dotnet test --solution ./PicoLog.slnx --configuration Release
```

## パフォーマンスに関する考慮事項

- logger インスタンスは `LoggerFactory` 内でカテゴリごとにキャッシュされます。
- `LoggerFactory` はカテゴリごとに 1 つの有界チャネル、1 つのカテゴリパイプライン、1 つのバックグラウンド drain task を所有します。
- factory 上の `FlushAsync()` は、flush snapshot より前に受理されたエントリに対する barrier であり、破棄の近道ではありません。
- factory の破棄は引き続き sink を破棄する前に最終 drain を行います。
- `FileSink` は独自の有界キューで書き込みをバッチ化し、バッチ境界または flush interval 境界で flush し、`IFlushableLogSink` による sink レベル flush も公開します。
- `DropOldest`、`DropWrite`、`Wait` の選択は、スループットと配信保証のトレードオフであり、正しさのバグではありません。

## ベンチマーク

このリポジトリには、PicoLog の handoff コストを Microsoft.Extensions.Logging ベースラインと比較するための PicoBench ベースのベンチマークプロジェクト `benchmarks/PicoLog.Benchmarks` が含まれています。

- `MicrosoftAsyncHandoff` は軽量な string-channel MEL ベースラインです。
- `MicrosoftAsyncEntryHandoff` は、実際の I/O を追加せずに PicoLog の timestamp/category/message envelope コストを反映する、より公平な full-entry MEL ベースラインです。
- `PicoWaitControl_CachedMessage` と `PicoWaitHandoff_CachedMessage` は、相対比較として PicoLog の wait モードにおけるバックプレッシャーをカバーします。

ベンチマークプロジェクトを実行するには:

```bash
dotnet run -c Release --project benchmarks/PicoLog.Benchmarks
```

または、成果物を publish して直接実行します。

```bash
dotnet publish benchmarks/PicoLog.Benchmarks/PicoLog.Benchmarks.csproj -c Release -r win-x64
benchmarks/PicoLog.Benchmarks/bin/Release/net10.0/win-x64/publish/PicoLog.Benchmarks.exe main
```

ベンチマークアプリは次のファイルを書き出します。

- `benchmark-results.md`
- `benchmark-results-main.md`
- `benchmark-results-wait.md`

## 適合性と非目標

### 強み

- コア実装は小さく把握しやすい構造です。`LoggerFactory` はカテゴリ別 logger 登録、カテゴリパイプライン、drain task のライフタイム、sink のライフタイムを所有し、各 `InternalLogger` は軽量で非所有の書き込みファサードとして保たれます。
- このプロジェクトは AOT フレンドリーで、リフレクション依存の重い基盤を避けているため、Native AOT、edge、IoT ワークロードに適しています。
- 構造化プロパティと組み込みメトリクスは、より大きなロギングエコシステムをアプリケーションに強制することなく、一般的な運用要件をカバーします。
- キュー圧力時の挙動は隠されず明示的です。呼び出し元は、スループットと配信のどちらを重視するかに応じて `DropOldest`、`DropWrite`、`Wait` を選べます。
- flush の意味も明示的です。`FlushAsync()` はすでに受理された作業に対する barrier であり、`DisposeAsync()` は最終 drain とリソース解放のための shutdown パスのままです。
- 組み込みの PicoDI 統合は、大規模な hosting や configuration スタックを導入せず、薄く予測可能なままです。

### 適しているケース

- より大きなロギングエコシステムを採用せずに、軽量なロギングコアを求める小〜中規模の .NET アプリケーション
- 起動コスト、バイナリサイズ、AOT 互換性が重要な edge、IoT、desktop、utility 系ワークロード
- best-effort 配信が許容され、実行中の flush barrier が時々役に立ち、明示的な shutdown drain で十分なアプリケーションロギングのシナリオ
- 少数のプリミティブを好み、必要に応じて custom sink や formatter を追加することに抵抗がないチーム

### 非目標と弱点

- これは完全な observability platform ではありません。構造化プロパティと小規模な組み込みメトリクス面はサポートしますが、message-template parsing、enricher、rolling file management、remote transport sink、または広範なテレメトリエコシステムとの深い統合は提供しません。
- 非常に高いカーディナリティの logger category には最適化されていません。現在の設計では、カテゴリごとに factory 所有の category pipeline とバックグラウンド drain task を 1 つずつ作成します。
- 静かな損失が許容されない audit や compliance logging には既定では適していません。既定の queue mode はスループットを優先し、`LogAsync()` の完了は sink の永続完了ではなく、より強い配信保証には `Wait` や専用 sink 戦略のような明示的構成が必要です。
- 組み込みメトリクスは queue depth、受理・ドロップされたエントリ、sink failure、shutdown 中の遅延書き込み、shutdown drain duration をカバーします。まだカテゴリ別メトリクス、sink latency histogram、より大きな end-to-end telemetry model は公開していません。

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

sink が `IFlushableLogSink` を実装していない場合、`ILogSink.FlushAsync()` 拡張は best-effort として即座に完了します。

### カスタムフォーマッタ

```csharp
public class CustomFormatter : ILogFormatter
{
    public string Format(LogEntry entry)
    {
        return $"{entry.Timestamp:yyyy-MM-dd HH:mm:ss} [{entry.Level}] {entry.Message}";
    }
}
```

## テスト

現在のテストスイートは次をカバーしています。

- 非同期破棄と競合する file sink 書き込み
- カテゴリごとの logger キャッシュ
- 最小レベルフィルタリング
- 構造化ペイロードのキャプチャと整形
- 組み込みメトリクスの発行
- factory 破棄時の scope キャプチャと flush
- 受理済みエントリに対する flush barrier と best-effort flush 拡張
- shutdown 開始後の書き込み拒否
- sink failure の分離
- 末尾メッセージの非同期 flush
- 実ファイルに対する file sink 末尾の永続化
- 構成済みの DI file 出力
- PicoDI 型付き logger の解決

サンプルは Native AOT の publish と実行によっても検証されます。

## コントリビューション

1. リポジトリを fork する
2. 機能ブランチを作成する
3. 変更を加える
4. テストを追加または更新する
5. pull request を送る

## ライセンス

このプロジェクトは MIT License の下で提供されています。詳細は [LICENSE](LICENSE) ファイルを参照してください。
