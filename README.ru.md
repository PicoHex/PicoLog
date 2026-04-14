# PicoLog

[English](README.md) | [简体中文](README.zh.md) | [繁體中文](README.zh-TW.md) | [Deutsch](README.de.md) | [Español](README.es.md) | [Français](README.fr.md) | [日本語](README.ja.md) | [Português (Brasil)](README.pt-BR.md) | [Русский](README.ru.md)

![CI](https://github.com/PicoHex/PicoLog/actions/workflows/ci.yml/badge.svg)
[![NuGet](https://img.shields.io/nuget/v/PicoLog.svg)](https://www.nuget.org/packages/PicoLog)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

Лёгкий, AOT-дружелюбный framework для логирования в .NET edge- и IoT-нагрузках. Репозиторий содержит контракты, основную реализацию logger, интеграцию с PicoDI, пример приложения и отдельный benchmark-проект.

## Возможности

- **Совместимость с AOT**: ориентирован на `net10.0` и избегает инфраструктуры с тяжёлой зависимостью от reflection
- **Ограниченный асинхронный pipeline**: logger помещают записи в ограниченный channel и записывают их в sink из фоновой задачи
- **Структурированные свойства**: необязательные пары ключ/значение проходят через `LogEntry.Properties` и вывод встроенного formatter
- **Встроенные метрики**: основной пакет публикует метрики очереди, потерь, ошибок sink и завершения через `System.Diagnostics.Metrics`
- **Минимальная поверхность API**: для расширения системы нужно реализовать лишь несколько основных абстракций
- **Интеграция с PicoDI**: встроенные регистрации logger factory и типизированных logger, с консольным логированием по умолчанию и необязательным файловым логированием при настройке пути к файлу
- **Покрытие benchmark-ами**: включает benchmark-проект на базе PicoBench с лёгкими и более справедливыми baseline для MEL async handoff
- **Поддержка scope**: вложенные scope проходят через `AsyncLocal` и прикрепляются к каждому `LogEntry`

## Структура проекта

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

## Установка

### Основная библиотека логирования

```bash
dotnet add package PicoLog
```

### Интеграция с PicoDI

```bash
dotnet add package PicoLog.DI
```

## Быстрый старт

### Базовое использование

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

### Интеграция с DI

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

## Конфигурация

### Минимальный уровень

`LoggerFactory.MinLevel` управляет тем, какие записи принимаются. Меньшие числовые значения означают более высокий приоритет, поэтому уровень `Debug` по умолчанию пропускает уровни от `Emergency` до `Debug`, но отфильтровывает `Trace`.

```csharp
await using var loggerFactory = new LoggerFactory(sinks)
{
    MinLevel = LogLevel.Warning
};
```

### Владение жизненным циклом

`LoggerFactory` владеет кэшированными logger по категориям, их pipeline-ами по категориям, фоновыми drain task, которые выполняют эти pipeline, а также зарегистрированными sink. Освобождайте factory при завершении работы, чтобы сбросить записи в очереди и освободить ресурсы sink. Как только начинается освобождение, новые записи отклоняются, а уже поставленные в очередь записи продолжают выгружаться.

```csharp
await using var loggerFactory = new LoggerFactory(sinks);

var logger = new Logger<MyService>(loggerFactory);
logger.Info("Starting up");

// queued entries are flushed when the factory is disposed;
// writes that race with shutdown are rejected
```

### Давление очереди

`LoggerFactoryOptions.QueueFullMode` явно задаёт поведение под давлением очереди как для синхронных, так и для асинхронных записей:

- `DropOldest` сохраняет неблокирующее логирование, отбрасывая самую старую запись в очереди. Это поведение по умолчанию.
- `DropWrite` отклоняет новую запись и сообщает о потере через `OnMessagesDropped`.
- `Wait` блокирует синхронное логирование до `SyncWriteTimeout`, а асинхронное логирование заставляет ждать свободного места в очереди, пока не будет запрошена отмена.

```csharp
var options = new LoggerFactoryOptions
{
    MinLevel = LogLevel.Info,
    QueueFullMode = LogQueueFullMode.Wait,
    SyncWriteTimeout = TimeSpan.FromMilliseconds(500)
};

await using var loggerFactory = new LoggerFactory(sinks, options);
```

### Встроенные Sink

- `ConsoleSink` пишет просто отформатированные записи в стандартный вывод.
- `ColoredConsoleSink` сериализует смену цветов, чтобы состояние консоли не протекало между параллельными записями.
- `FileSink` пакетирует записи UTF-8 в файл во фоновой очереди перед сбросом на диск. `AddLogging()` создаёт настроенные sink внутри logger factory, чтобы factory оставалась единственным владельцем их жизненного цикла.

### Значения по умолчанию в PicoDI

`AddLogging()` регистрирует:

- singleton `ILoggerFactory`
- типизированные адаптеры `ILogger<T>`
- типизированные адаптеры `IStructuredLogger<T>`
- console sink по умолчанию
- необязательный file sink, если настроен `FilePath`

При завершении работы явно освобождайте полученную factory, чтобы записи логов в очереди были сброшены до завершения процесса. Записи, пришедшие после начала shutdown, будут отклонены, а не приняты слишком поздно.

Вы можете включить файловое логирование через необязательный параметр `filePath`, установив `options.FilePath` или `options.File.FilePath` в overload конфигурации. Явно указанный путь к файлу считается явным opt-in для file sink.

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

### Встроенный formatter

`ConsoleFormatter` создаёт удобочитаемые строки с временной меткой, уровнем, категорией, сообщением, необязательными структурированными свойствами, текстом исключения и необязательными scope.

```csharp
var formatter = new ConsoleFormatter();
var sink = new ConsoleSink(formatter);
```

### Структурированное логирование

PicoLog сохраняет совместимость с `ILogger` и предоставляет гарантированное структурированное логирование через `IStructuredLogger` / `IStructuredLogger<T>`. При использовании встроенной `LoggerFactory` runtime logger сохраняет payload из `LogStructured()` / `LogStructuredAsync()` в `LogEntry.Properties`.

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

`ConsoleFormatter` добавляет структурированные свойства после сообщения в компактной текстовой форме, например:

```text
[2026-04-05 11:30:42.123] WARNING   [MyService] Cache miss {cacheKey="user:42", node="edge-a", attempt=3}
```

### Расширения логирования

Поставляемые методы расширения определены для `ILogger` и `ILogger<T>`:

- `Trace`, `Debug`, `Info`, `Notice`, `Warning`, `Error`, `Critical`, `Alert`, `Emergency`
- асинхронные аналоги, такие как `InfoAsync` и `ErrorAsync`
- `LogStructured` и `LogStructuredAsync` как best-effort адаптеры, которые сохраняют свойства, когда runtime logger реализует `IStructuredLogger`, и в противном случае откатываются к обычному логированию без структурированной payload

Если вам нужен строгий контракт структурированного логирования, зависите напрямую от `IStructuredLogger` / `IStructuredLogger<T>`.

### Поведение при переполнении

И синхронное, и асинхронное логирование записывают в ограниченный channel.

- И синхронный `Log()`, и асинхронный `LogAsync()` следуют `LoggerFactoryOptions.QueueFullMode`.
- Значение по умолчанию — `DropOldest`, которое предпочитает throughput и низкую задержку вызывающего кода вместо гарантированной доставки.
- `Wait` делает backpressure видимым для вызывающего кода, блокируя синхронные записи, пока не освободится место в очереди или не истечёт `SyncWriteTimeout`, и ожидая места в очереди для асинхронных записей, пока не будет запрошена отмена.
- `DropWrite` сохраняет более старые записи в очереди и сообщает о новых отброшенных записях через `OnMessagesDropped`.
- Как только начинается освобождение factory, новые записи отклоняются, а уже поставленные в очередь записи продолжают сбрасываться.

Для общего прикладного логирования поведение `DropOldest` по умолчанию обычно приемлемо. Для аудиторского логирования предпочтительнее `Wait` или выделенная стратегия sink.

### Встроенные метрики

Основной пакет `PicoLog` публикует небольшой встроенный набор метрик через `System.Diagnostics.Metrics`, используя имя meter `PicoLog`.

- `picolog.entries.enqueued`
- `picolog.entries.dropped`
- `picolog.sinks.failures`
- `picolog.writes.rejected_after_shutdown`
- `picolog.queue.entries`
- `picolog.shutdown.drain.duration`

Эти instruments спроектированы так, чтобы оставаться лёгкими и с низкой кардинальностью. Их можно наблюдать напрямую через `MeterListener` или подключать к более широкой телеметрической инфраструктуре.

```csharp
using var listener = new MeterListener();

listener.InstrumentPublished = (instrument, meterListener) =>
{
    if (instrument.Meter.Name == PicoLogMetrics.MeterName)
        meterListener.EnableMeasurementEvents(instrument);
};

listener.Start();
```

## Совместимость с AOT

Пример проекта публикуется с включённым Native AOT:

```xml
<PropertyGroup>
  <PublishAOT>true</PublishAOT>
</PropertyGroup>
```

```bash
dotnet publish -c Release -r win-x64 -p:PublishAOT=true
```

В репозитории также есть validation script уровня publish, который публикует sample, запускает сгенерированный исполняемый файл и проверяет, что финальные shutdown log entries были корректно сброшены:

```powershell
./scripts/Validate-AotSample.ps1
```

## Сборка из исходников

### Предварительные требования

- .NET SDK 10.0 или новее
- Git

### Клонирование и сборка

```bash
git clone https://github.com/PicoHex/PicoLog.git
cd PicoLog
dotnet restore
dotnet build --configuration Release
```

### Запуск тестов

```bash
dotnet test --solution ./PicoLog.slnx --configuration Release
```

## Соображения по производительности

- Экземпляры logger кэшируются по категориям внутри `LoggerFactory`.
- `LoggerFactory` владеет одним ограниченным channel, одним category pipeline и одной фоновой drain task на категорию.
- При освобождении factory все активные logger сбрасываются до освобождения sink.
- `FileSink` пакетирует записи в собственной ограниченной очереди и сбрасывает их на границах batch или flush interval.
- Выбор между `DropOldest`, `DropWrite` и `Wait` — это компромисс между throughput и доставкой, а не ошибка корректности.

## Benchmarks

Репозиторий включает `benchmarks/PicoLog.Benchmarks` — benchmark-проект на базе PicoBench для сравнения стоимости handoff в PicoLog с baseline-ами Microsoft.Extensions.Logging.

- `MicrosoftAsyncHandoff` — это лёгкий string-channel baseline для MEL.
- `MicrosoftAsyncEntryHandoff` — более справедливый full-entry baseline для MEL, который отражает стоимость envelope timestamp/category/message в PicoLog без добавления реального I/O.
- `PicoWaitControl_CachedMessage` и `PicoWaitHandoff_CachedMessage` покрывают backpressure PicoLog в режиме wait как относительное сравнение.

Запустите benchmark-проект:

```bash
dotnet run -c Release --project benchmarks/PicoLog.Benchmarks
```

Или опубликуйте и выполните артефакт напрямую:

```bash
dotnet publish benchmarks/PicoLog.Benchmarks/PicoLog.Benchmarks.csproj -c Release -r win-x64
benchmarks/PicoLog.Benchmarks/bin/Release/net10.0/win-x64/publish/PicoLog.Benchmarks.exe main
```

Benchmark-приложение записывает:

- `benchmark-results.md`
- `benchmark-results-main.md`
- `benchmark-results-wait.md`

## Подходящие сценарии и нецели

### Сильные стороны

- Основная реализация невелика и проста для понимания: `LoggerFactory` владеет регистрациями logger по категориям, category pipeline, временем жизни drain task и временем жизни sink, а каждый `InternalLogger` остаётся лёгким фасадом записи без владения.
- Проект дружелюбен к AOT и избегает инфраструктуры с тяжёлой зависимостью от reflection, что делает его хорошим выбором для Native AOT, edge и IoT-нагрузок.
- Структурированные свойства и встроенные метрики покрывают типичные эксплуатационные потребности, не навязывая приложению более крупную экосистему логирования.
- Поведение при давлении очереди сделано явным, а не скрытым. Вызывающий код может выбрать `DropOldest`, `DropWrite` или `Wait` в зависимости от того, что важнее: throughput или доставка.
- Встроенная интеграция PicoDI остаётся тонкой и предсказуемой, не вводя крупный hosting- или configuration-стек.

### Хорошо подходит

- Небольшим и средним .NET-приложениям, которым нужен лёгкий core логирования без перехода на более крупную экосистему логирования.
- Edge-, IoT-, desktop- и utility-нагрузкам, где важны стоимость запуска, размер бинарника и совместимость с AOT.
- Сценариям прикладного логирования, где приемлема best-effort доставка и достаточно явного flush при завершении работы.
- Командам, предпочитающим небольшой набор примитивов и готовым при необходимости добавлять custom sink или formatter.

### Нецели и слабые места

- Это не полноценная observability platform. Здесь поддерживаются структурированные свойства и небольшой встроенный набор метрик, но нет message-template parsing, enricher-ов, управления rolling file, remote transport sink или глубокой интеграции с более широкой телеметрической экосистемой.
- Решение не оптимизировано для logger category с очень высокой кардинальностью. Текущий дизайн создаёт один принадлежащий factory category pipeline и одну фоновую drain task на категорию.
- Это не вариант по умолчанию для audit или compliance logging, где тихая потеря недопустима. Режим очереди по умолчанию ориентирован на throughput, а более сильные гарантии доставки требуют явной настройки, такой как `Wait` или выделенная стратегия sink.
- Встроенные метрики покрывают глубину очереди, принятые и отброшенные записи, ошибки sink, поздние записи во время shutdown и длительность drain при shutdown. Они пока не предоставляют метрики по категориям, histogram latency для sink и более крупную end-to-end telemetry model.

## Расширение PicoLog

### Пользовательский Sink

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

### Пользовательский Formatter

```csharp
public class CustomFormatter : ILogFormatter
{
    public string Format(LogEntry entry)
    {
        return $"{entry.Timestamp:yyyy-MM-dd HH:mm:ss} [{entry.Level}] {entry.Message}";
    }
}
```

## Тесты

Текущий набор тестов покрывает:

- записи file sink, конкурирующие с асинхронным освобождением
- кэширование logger по категориям
- фильтрацию по минимальному уровню
- захват и форматирование структурированной payload
- публикацию встроенных метрик
- захват scope и flush при освобождении factory
- отклонение записей после начала shutdown
- изоляцию ошибок sink
- асинхронный flush хвостовых сообщений
- реальную персистентность хвоста file sink
- настроенный файловый вывод DI
- разрешение типизированных logger PicoDI

Пример также проверяется через публикацию и выполнение Native AOT.

## Участие

1. Сделайте fork репозитория
2. Создайте feature branch
3. Внесите изменения
4. Добавьте или обновите тесты
5. Отправьте pull request

## Лицензия

Этот проект распространяется по MIT License; подробности смотрите в файле [LICENSE](LICENSE).
