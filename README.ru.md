# PicoLog

[English](README.md) | [简体中文](README.zh.md) | [繁體中文](README.zh-TW.md) | [Deutsch](README.de.md) | [Español](README.es.md) | [Français](README.fr.md) | [日本語](README.ja.md) | [Português (Brasil)](README.pt-BR.md) | [Русский](README.ru.md)

![CI](https://github.com/PicoHex/PicoLog/actions/workflows/ci.yml/badge.svg)
[![NuGet](https://img.shields.io/nuget/v/PicoLog.svg)](https://www.nuget.org/packages/PicoLog)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

PicoLog, это лёгкий, дружественный к AOT framework для логирования в .NET edge, desktop, utility и IoT нагрузках.

Текущий дизайн намеренно сделан небольшим:

- **одна модель logger**: `ILogger` / `ILogger<T>`
- **одна точка входа DI**: `AddPicoLog(...)`
- **один владелец жизненного цикла**: `ILoggerFactory`

Структурированные свойства, это часть самого события лога, а не отдельный тип logger. Типы runtime и extensibility, такие как sinks, formatters, `LogEntry` и flush companions, находятся в `PicoLog`, а контракты для потребителей находятся в `PicoLog.Abs`.

## Возможности

- **AOT-дружественный дизайн**: избегает инфраструктуры с тяжёлой зависимостью от reflection и включает пример с Native AOT
- **Ограниченный асинхронный pipeline**: logger передают записи в pipeline по категориям, построенные на bounded channels
- **Явная семантика жизненного цикла**: `FlushAsync()` это барьер во время работы, `DisposeAsync()` это drain при shutdown
- **Структурированные свойства на `ILogger`**: нативные overload сохраняют payload ключ/значение в `LogEntry.Properties`
- **Небольшая DI-поверхность**: `AddPicoLog(...)` регистрирует `ILoggerFactory` и типизированные адаптеры `ILogger<T>`
- **Встроенные sinks и formatter**: console, colored console, file и читаемый text formatter
- **Контракты flush companion**: возможности flush во время runtime остаются доступны через `IFlushableLoggerFactory` и `IFlushableLogSink`
- **Встроенные метрики**: метрики очереди, потерь, ошибок sink и shutdown через `System.Diagnostics.Metrics`
- **Benchmark-проект**: benchmarks на базе PicoBench для стоимости handoff в PicoLog и baseline из MEL
- **Поддержка scope**: вложенные scope проходят через `AsyncLocal` и прикрепляются к каждому `LogEntry`

## Структура проекта

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

## Установка

### Основной runtime

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

`FlushAsync()` **не** является освобождением ресурсов. Это барьер для записей, которые уже были приняты до снимка flush. Используйте `DisposeAsync()` для финального drain при shutdown и очистки sinks.

### Интеграция с DI

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

`AddPicoLog()` это единственная точка входа DI. Код приложения обычно должен зависеть от `ILogger<T>`. `ILoggerFactory` это явный владелец жизненного цикла в корне приложения для flush и shutdown.

## Основная модель

### Одна модель logger

PicoLog больше не разделяет logging на интерфейсы logger для «plain» и «structured».

- `ILogger` / `ILogger<T>` это основная поверхность записи
- обычные события используют `Log(level, message, exception?)`
- структурированные события используют `Log(level, message, properties, exception)`
- асинхронные варианты следуют той же форме через `LogAsync(...)`

`LogStructured()` и `LogStructuredAsync()` всё ещё существуют как удобные wrapper в `LoggerExtensions`, но это просто синтаксический сахар над нативными overload `ILogger`.

### Разделение пакетов

- **`PicoLog.Abs`**: контракты для потребителей, такие как `ILogger`, `ILogger<T>`, `ILoggerFactory`, `LogLevel` и `LoggerExtensions`
- **`PicoLog`**: типы runtime и extensibility, такие как `LoggerFactory`, `Logger<T>`, `LogEntry`, `ILogSink`, `ILogFormatter`, `IFlushableLoggerFactory` и `IFlushableLogSink`
- **`PicoLog.DI`**: интеграция с PicoDI через `AddPicoLog(...)`

## Конфигурация

### Минимальный уровень

`LoggerFactory.MinLevel` управляет тем, какие записи принимаются. Меньшие числовые значения более серьёзны, поэтому уровень по умолчанию `Debug` пропускает значения от `Emergency` до `Debug`, но отфильтровывает `Trace`.

```csharp
await using var loggerFactory = new LoggerFactory(sinks)
{
    MinLevel = LogLevel.Warning
};
```

### Владение жизненным циклом

`LoggerFactory` владеет:

- кэшированными logger по категориям
- pipeline по категориям
- фоновыми задачами drain
- зарегистрированными sinks

Это означает, что API жизненного цикла относятся к factory story, а не к `ILogger<T>`.

```csharp
await using var loggerFactory = new LoggerFactory(sinks);

var logger = new Logger<MyService>(loggerFactory);
logger.Info("Starting up");

await loggerFactory.FlushAsync();   // mid-run barrier
await loggerFactory.DisposeAsync(); // final shutdown drain
```

Если вы получаете `ILoggerFactory` из DI, помните, что это всё ещё singleton-владелец жизненного цикла на уровне приложения. Получение из scope **не** делает её принадлежащей этому scope.

### Давление на очередь

`LoggerFactoryOptions.QueueFullMode` делает давление на очередь явным как для синхронной, так и для асинхронной записи.

Завершение `LogAsync()` означает, что обработка handoff на границе logger на этом этапе уже закончилась. Запись могла быть:

- принята
- отброшена политикой очереди
- отклонена во время shutdown

Это **не** означает, что sink уже завершил надёжную запись.

- `DropOldest` сохраняет logging неблокирующим, отбрасывая самую старую запись в очереди. Это поведение по умолчанию.
- `DropWrite` отклоняет новую запись и сообщает о потере через `OnMessagesDropped`.
- `Wait` блокирует синхронный logging до `SyncWriteTimeout` и заставляет асинхронный logging ждать место в очереди, пока не будет запрошена отмена.

```csharp
var options = new LoggerFactoryOptions
{
    MinLevel = LogLevel.Info,
    QueueFullMode = LogQueueFullMode.Wait,
    SyncWriteTimeout = TimeSpan.FromMilliseconds(500)
};

await using var loggerFactory = new LoggerFactory(sinks, options);
```

## Встроенные элементы runtime

### Sinks

- `ConsoleSink` пишет просто отформатированные записи в стандартный вывод.
- `ColoredConsoleSink` сериализует смену цвета, чтобы состояние console не протекало между конкурентными записями.
- `FileSink` группирует UTF-8 записи в файл в фоновой очереди перед flush на диск и поддерживает flush на уровне sink через `IFlushableLogSink`.

При использовании `AddPicoLog()` настроенные sinks создаются внутри logger factory, поэтому factory остаётся единственным владельцем их времени жизни.

### Formatter

`ConsoleFormatter` создаёт читаемые строки с timestamp, уровнем, категорией, сообщением, необязательными структурированными свойствами, текстом исключения и необязательными scope.

```csharp
var formatter = new ConsoleFormatter();
var sink = new ConsoleSink(formatter);
```

### Структурированное logging

Структурированные данные являются частью самого события лога.

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

Эти свойства сохраняются в `LogEntry.Properties`, а sinks или formatters уже решают, как их использовать.

Например, `ConsoleFormatter` добавляет их в компактной текстовой форме:

```text
[2026-04-05 11:30:42.123] WARNING   [MyService] Cache miss {cacheKey="user:42", node="edge-a", attempt=3}
```

## Расширения logging

Поставляемые методы расширения определены для `ILogger` и `ILogger<T>`:

- `Trace`, `Debug`, `Info`, `Notice`, `Warning`, `Error`, `Critical`, `Alert`, `Emergency`
- асинхронные аналоги, такие как `InfoAsync` и `ErrorAsync`
- `LogStructured` и `LogStructuredAsync` как удобные wrapper над нативными overload `ILogger`, умеющими работать со свойствами
- best-effort расширения `FlushAsync()` для `ILoggerFactory` и `ILogSink`

Расширение `ILoggerFactory.FlushAsync()` находится в `PicoLog`, а не в `PicoLog.Abs`. Строгая runtime-возможность по-прежнему представлена `IFlushableLoggerFactory`, а расширение сохраняет простой общий вызов.

## Интеграция с PicoDI

`AddPicoLog()` регистрирует:

- singleton `ILoggerFactory`
- типизированные адаптеры `ILogger<T>`
- встроенное поведение sink по умолчанию, если явный pipeline `WriteTo` не настроен
- необязательный мост к sinks, зарегистрированным в DI, когда включён `ReadFrom.RegisteredSinks()`

Для нового кода лучше предпочитать builder sinks `WriteTo` как основной путь конфигурации.

```csharp
container.AddPicoLog(options =>
{
    options.MinLevel = LogLevel.Info;
    options.WriteTo.ColoredConsole();
    options.WriteTo.File("logs/app.log");
});
```

Вы также можете подключить sinks, уже зарегистрированные в PicoDI:

```csharp
container.Register(new SvcDescriptor(typeof(ILogSink), _ => new AuditSink()));

container.AddPicoLog(options =>
{
    options.ReadFrom.RegisteredSinks();
    options.WriteTo.ColoredConsole();
});
```

## Метрики

Основной пакет `PicoLog` публикует небольшую встроенную поверхность метрик через `System.Diagnostics.Metrics`, используя имя meter `PicoLog`.

- `picolog.entries.enqueued`
- `picolog.entries.dropped`
- `picolog.sinks.failures`
- `picolog.writes.rejected_after_shutdown`
- `picolog.queue.entries`
- `picolog.shutdown.drain.duration`

Эти инструменты намеренно имеют низкую кардинальность и остаются лёгкими.

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

Пример проекта публикуется с включённым Native AOT.

```xml
<PropertyGroup>
  <PublishAOT>true</PublishAOT>
</PropertyGroup>
```

```bash
dotnet publish -c Release -r win-x64 -p:PublishAOT=true
```

В репозитории также есть скрипт проверки на уровне publish, который публикует пример, запускает сгенерированный исполняемый файл и проверяет, что финальные shutdown записи лога были корректно flushed:

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

## Примечания по производительности

- экземпляры logger кэшируются по категориям внутри `LoggerFactory`
- `LoggerFactory` владеет одним bounded channel, одним category pipeline и одной фоновой задачей drain на каждую категорию
- `FlushAsync()` это барьер для записей, принятых до flush snapshot, а не shortcut для освобождения ресурсов
- освобождение factory по-прежнему выполняет финальный drain перед освобождением sinks
- `FileSink` группирует записи в собственной bounded queue и предоставляет flush на уровне sink через `IFlushableLogSink`
- выбор `DropOldest`, `DropWrite` или `Wait` это компромисс между throughput и delivery, а не ошибка корректности

## Benchmarks

Репозиторий включает `benchmarks/PicoLog.Benchmarks`, benchmark-проект на базе PicoBench для сравнения стоимости handoff в PicoLog с baseline из Microsoft.Extensions.Logging.

- `MicrosoftAsyncHandoff` это лёгкий baseline MEL со string-channel.
- `MicrosoftAsyncEntryHandoff` это более честный baseline MEL с полной записью, который отражает стоимость envelope timestamp/category/message у PicoLog без добавления реального I/O.
- имена benchmark для режима wait, такие как `PicoWaitControl_*`, это внутренние метки benchmark-сценариев, а не публичные имена API.

Запустите benchmark-проект:

```bash
dotnet run -c Release --project benchmarks/PicoLog.Benchmarks
```

Или опубликуйте и выполните артефакт напрямую:

```bash
dotnet publish benchmarks/PicoLog.Benchmarks/PicoLog.Benchmarks.csproj -c Release -r win-x64
benchmarks/PicoLog.Benchmarks/bin/Release/net10.0/win-x64/publish/PicoLog.Benchmarks.exe main
```

## Расширение PicoLog

### Пользовательский Sink

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

Если sink не реализует `IFlushableLogSink`, расширение `ILogSink.FlushAsync()` работает в режиме best-effort и завершается сразу.

### Пользовательский Formatter

```csharp
public sealed class CustomFormatter : ILogFormatter
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
- захват и форматирование структурированного payload
- публикацию встроенных метрик
- захват scope и flush при освобождении factory
- flush-барьеры для принятых записей и best-effort flush расширения
- отклонение записи после начала shutdown
- изоляцию ошибок sink
- flush асинхронных tail-message
- реальную сохранность хвоста file sink
- настроенный DI file output
- разрешение типизированных logger в PicoDI

Пример также проверяется через publish и выполнение Native AOT.

## Подходящие сценарии и нецели

### Сильные стороны

- Основная реализация небольшая и её легко понимать: `LoggerFactory` владеет регистрациями по категориям, pipeline, временем жизни drain-задач и временем жизни sinks, а каждый `InternalLogger` остаётся лёгким фасадом записи без владения ресурсами.
- Проект дружественен к AOT и избегает инфраструктуры с тяжёлой зависимостью от reflection.
- Структурированные свойства и встроенные метрики покрывают типичные эксплуатационные потребности, не навязывая приложению более крупную logging-экосистему.
- Поведение под давлением очереди явно выражено, а не скрыто.
- Семантика flush остаётся явной: `FlushAsync()` это барьер для уже принятой работы, а `DisposeAsync()` остаётся путём shutdown для финального drain и освобождения ресурсов.
- Встроенная интеграция PicoDI остаётся тонкой и предсказуемой.

### Хорошо подходит для

- Небольших и средних .NET приложений, которым нужен лёгкий logging-core без перехода на более крупную logging-экосистему
- Edge, IoT, desktop и utility-нагрузок, где важны стоимость запуска, размер бинарника и совместимость с AOT
- Сценариев application logging, где acceptable best-effort delivery, иногда полезны flush-барьеры в середине работы, и достаточно явного drain при shutdown
- Команд, которые предпочитают небольшой набор примитивов и готовы при необходимости добавлять собственные sinks или formatters

### Нецели и слабые места

- Это не полноценная observability-платформа.
- Это не оптимальный вариант для logger-категорий с очень высокой кардинальностью, потому что текущий дизайн создаёт один принадлежащий factory category pipeline и одну фоновую drain-задачу на каждую категорию.
- Это не вариант по умолчанию для audit или compliance logging, где тихая потеря неприемлема.
- Встроенные метрики намеренно небольшие и не пытаются моделировать более крупную end-to-end telemetry system.

## Участие в проекте

1. Сделайте fork репозитория
2. Создайте feature-ветку
3. Внесите изменения
4. Добавьте или обновите тесты
5. Отправьте pull request

## Лицензия

Этот проект распространяется по лицензии MIT. Подробности смотрите в файле [LICENSE](LICENSE).
