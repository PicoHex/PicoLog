# PicoLog

[English](README.md) | [简体中文](README.zh.md) | [繁體中文](README.zh-TW.md) | [Deutsch](README.de.md) | [Español](README.es.md) | [Français](README.fr.md) | [日本語](README.ja.md) | [Português (Brasil)](README.pt-BR.md) | [Русский](README.ru.md)

![CI](https://github.com/PicoHex/PicoLog/actions/workflows/ci.yml/badge.svg)
[![NuGet](https://img.shields.io/nuget/v/PicoLog.svg)](https://www.nuget.org/packages/PicoLog)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

Ein leichtgewichtiges, AOT-freundliches Logging-Framework für .NET-Edge- und IoT-Workloads. Das Repository enthält Verträge, die Kernimplementierung des Loggers, PicoDI-Integration, eine Beispielanwendung und ein eigenes Benchmark-Projekt.

## Funktionen

- **AOT-Kompatibilität**: zielt auf `net10.0` ab und vermeidet reflektionslastige Infrastruktur
- **Begrenzte asynchrone Pipeline**: Logger stellen Einträge in eine begrenzte Channel-Warteschlange ein und schreiben sie in einer Hintergrundaufgabe in Sinks
- **Strukturierte Eigenschaften**: optionale Schlüssel/Wert-Nutzdaten laufen durch `LogEntry.Properties` und die Ausgabe des integrierten Formatters
- **Integrierte Metriken**: das Kernpaket emittiert Warteschlangen-, Drop-, Sink-Fehler- und Shutdown-Metriken über `System.Diagnostics.Metrics`
- **Kleine Oberfläche**: nur wenige Kernabstraktionen müssen implementiert werden, um das System zu erweitern
- **PicoDI-Integration**: integrierte Registrierungen für die LoggerFactory und typisierte Logger mit Konsolenlogging standardmäßig und optionalem Dateilogging, wenn ein Dateipfad konfiguriert ist
- **Benchmark-Abdeckung**: enthält ein PicoBench-basiertes Benchmark-Projekt mit leichtgewichtigen und faireren MEL-Async-Handoff-Baselines
- **Scope-Unterstützung**: verschachtelte Scopes laufen über `AsyncLocal` und werden an jeden `LogEntry` angehängt

## Projektstruktur

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

## Installation

### Kern-Loggingbibliothek

```bash
dotnet add package PicoLog
```

### PicoDI-Integration

```bash
dotnet add package PicoLog.DI
```

## Schnellstart

### Grundlegende Verwendung

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

### DI-Integration

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

## Konfiguration

### Mindestlevel

`LoggerFactory.MinLevel` steuert, welche Einträge akzeptiert werden. Niedrigere numerische Werte sind schwerwiegender, daher erlaubt der Standardlevel `Debug` die Stufen von `Emergency` bis `Debug`, filtert aber `Trace` heraus.

```csharp
await using var loggerFactory = new LoggerFactory(sinks)
{
    MinLevel = LogLevel.Warning
};
```

### Besitz der Lebensdauer

`LoggerFactory` besitzt zwischengespeicherte Logger pro Kategorie, deren kategoriebasierte Pipelines, die Hintergrund-Drain-Tasks dieser Pipelines und die registrierten Sinks. Entsorge die Factory beim Herunterfahren, um eingereihte Einträge zu flushen und Sink-Ressourcen freizugeben. Sobald die Entsorgung beginnt, werden neue Schreibvorgänge abgewiesen, während bereits eingereihte Einträge weiter geleert werden.

```csharp
await using var loggerFactory = new LoggerFactory(sinks);

var logger = new Logger<MyService>(loggerFactory);
logger.Info("Starting up");

// queued entries are flushed when the factory is disposed;
// writes that race with shutdown are rejected
```

### Warteschlangendruck

`LoggerFactoryOptions.QueueFullMode` macht den Umgang mit Warteschlangendruck für synchrone und asynchrone Schreibvorgänge explizit:

- `DropOldest` hält Logging nicht blockierend, indem der älteste eingereihte Eintrag verworfen wird. Das ist der Standard.
- `DropWrite` lehnt den neuen Eintrag ab und meldet den Verlust über `OnMessagesDropped`.
- `Wait` blockiert synchrones Logging bis zu `SyncWriteTimeout` und lässt asynchrones Logging auf freien Platz in der Warteschlange warten, bis eine Abbruchanforderung erfolgt.

```csharp
var options = new LoggerFactoryOptions
{
    MinLevel = LogLevel.Info,
    QueueFullMode = LogQueueFullMode.Wait,
    SyncWriteTimeout = TimeSpan.FromMilliseconds(500)
};

await using var loggerFactory = new LoggerFactory(sinks, options);
```

### Integrierte Sinks

- `ConsoleSink` schreibt schlicht formatierte Einträge in die Standardausgabe.
- `ColoredConsoleSink` serialisiert Farbwechsel, damit der Konsolenzustand nicht über gleichzeitige Schreibvorgänge hinweg ausläuft.
- `FileSink` bündelt UTF-8-Dateischreibvorgänge in einer Hintergrundwarteschlange, bevor sie auf Datenträger geflusht werden. `AddLogging()` erstellt konfigurierte Sinks innerhalb der LoggerFactory, damit die Factory alleiniger Besitzer ihrer Lebensdauer bleibt.

### PicoDI-Standardregistrierungen

`AddLogging()` registriert:

- eine Singleton-`ILoggerFactory`
- typisierte `ILogger<T>`-Adapter
- typisierte `IStructuredLogger<T>`-Adapter
- standardmäßig einen Konsolen-Sink
- optional einen Datei-Sink, wenn `FilePath` konfiguriert ist

Beim Herunterfahren sollte die aufgelöste Factory explizit entsorgt werden, damit eingereihte Logeinträge vor dem Prozessende geflusht werden. Schreibvorgänge, die nach Beginn des Shutdowns eintreffen, werden abgewiesen, statt verspätet noch akzeptiert zu werden.

Du kannst Dateilogging über den optionalen Parameter `filePath`, über `options.FilePath` oder über `options.File.FilePath` in der Configure-Überladung aktivieren. Ein expliziter Dateipfad gilt als ausdrückliches Opt-in für den Datei-Sink.

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

### Integrierter Formatter

`ConsoleFormatter` erzeugt gut lesbare Zeilen mit Zeitstempel, Level, Kategorie, Nachricht, optionalen strukturierten Eigenschaften, Ausnahmetext und optionalen Scopes.

```csharp
var formatter = new ConsoleFormatter();
var sink = new ConsoleSink(formatter);
```

### Strukturiertes Logging

PicoLog bleibt zu `ILogger` kompatibel und stellt garantiert strukturiertes Logging über `IStructuredLogger` / `IStructuredLogger<T>` bereit. Wenn du die integrierte `LoggerFactory` verwendest, bewahrt der Laufzeit-Logger die Nutzdaten von `LogStructured()` / `LogStructuredAsync()` in `LogEntry.Properties`.

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

`ConsoleFormatter` hängt strukturierte Eigenschaften in kompakter Textform hinter der Nachricht an, zum Beispiel:

```text
[2026-04-05 11:30:42.123] WARNING   [MyService] Cache miss {cacheKey="user:42", node="edge-a", attempt=3}
```

### Logging-Erweiterungen

Die mitgelieferten Erweiterungsmethoden sind auf `ILogger` und `ILogger<T>` definiert:

- `Trace`, `Debug`, `Info`, `Notice`, `Warning`, `Error`, `Critical`, `Alert`, `Emergency`
- asynchrone Gegenstücke wie `InfoAsync` und `ErrorAsync`
- `LogStructured` und `LogStructuredAsync` als Best-Effort-Adapter, die Eigenschaften erhalten, wenn der Laufzeit-Logger `IStructuredLogger` implementiert, und andernfalls auf schlichtes Logging ohne strukturierte Nutzdaten zurückfallen

Wenn du einen strikten Vertrag für strukturiertes Logging brauchst, hänge direkt von `IStructuredLogger` / `IStructuredLogger<T>` ab.

### Überlaufverhalten

Sowohl synchrones als auch asynchrones Logging schreibt in einen begrenzten Channel.

- Synchrones `Log()` und asynchrones `LogAsync()` folgen beide `LoggerFactoryOptions.QueueFullMode`.
- Standard ist `DropOldest`, was Durchsatz und niedrige Aufruferlatenz gegenüber garantierter Zustellung bevorzugt.
- `Wait` macht Backpressure für den Aufrufer sichtbar, indem synchrone Schreibvorgänge blockieren, bis Platz in der Warteschlange verfügbar ist oder `SyncWriteTimeout` abläuft, und asynchrone Schreibvorgänge auf freien Platz warten, bis eine Abbruchanforderung erfolgt.
- `DropWrite` bewahrt ältere eingereihte Einträge und meldet verworfene neue Einträge über `OnMessagesDropped`.
- Sobald die Entsorgung der Factory beginnt, werden neue Schreibvorgänge abgewiesen, während bereits eingereihte Einträge weiter geflusht werden.

Für allgemeines Anwendungslogging ist das Standardverhalten `DropOldest` meist akzeptabel. Für Audit-ähnliches Logging sollte `Wait` oder eine dedizierte Sink-Strategie bevorzugt werden.

### Integrierte Metriken

Das Kernpaket `PicoLog` emittiert eine kleine integrierte Metrikoberfläche über `System.Diagnostics.Metrics` mit dem Meter-Namen `PicoLog`.

- `picolog.entries.enqueued`
- `picolog.entries.dropped`
- `picolog.sinks.failures`
- `picolog.writes.rejected_after_shutdown`
- `picolog.queue.entries`
- `picolog.shutdown.drain.duration`

Diese Instrumente sind darauf ausgelegt, kardinalitätsarm und leichtgewichtig zu bleiben. Sie können direkt mit `MeterListener` beobachtet oder in breitere Telemetrieinfrastruktur eingebunden werden.

```csharp
using var listener = new MeterListener();

listener.InstrumentPublished = (instrument, meterListener) =>
{
    if (instrument.Meter.Name == PicoLogMetrics.MeterName)
        meterListener.EnableMeasurementEvents(instrument);
};

listener.Start();
```

## AOT-Kompatibilität

Das Beispielprojekt wird mit aktiviertem Native AOT veröffentlicht:

```xml
<PropertyGroup>
  <PublishAOT>true</PublishAOT>
</PropertyGroup>
```

```bash
dotnet publish -c Release -r win-x64 -p:PublishAOT=true
```

Das Repository enthält außerdem ein Validierungsskript auf Veröffentlichungsniveau, das das Beispiel veröffentlicht, die erzeugte ausführbare Datei startet und überprüft, dass die finalen Shutdown-Logeinträge korrekt geflusht wurden:

```powershell
./scripts/Validate-AotSample.ps1
```

## Aus dem Quellcode erstellen

### Voraussetzungen

- .NET SDK 10.0 oder neuer
- Git

### Klonen und bauen

```bash
git clone https://github.com/PicoHex/PicoLog.git
cd PicoLog
dotnet restore
dotnet build --configuration Release
```

### Tests ausführen

```bash
dotnet test --solution ./PicoLog.slnx --configuration Release
```

## Performance-Überlegungen

- Logger-Instanzen werden innerhalb von `LoggerFactory` pro Kategorie zwischengespeichert.
- `LoggerFactory` besitzt pro Kategorie genau einen begrenzten Channel, eine Kategorien-Pipeline und eine Hintergrund-Drain-Task.
- Das Entsorgen der Factory flusht alle aktiven Logger, bevor die Sinks entsorgt werden.
- `FileSink` bündelt Schreibvorgänge in seiner eigenen begrenzten Warteschlange und flusht an Batch-Grenzen oder Flush-Intervall-Grenzen.
- Die Wahl von `DropOldest`, `DropWrite` oder `Wait` ist ein Trade-off zwischen Durchsatz und Zustellung, kein Korrektheitsfehler.

## Benchmarks

Das Repository enthält `benchmarks/PicoLog.Benchmarks`, ein PicoBench-basiertes Benchmark-Projekt zum Vergleich der PicoLog-Handoff-Kosten mit Microsoft.Extensions.Logging-Baselines.

- `MicrosoftAsyncHandoff` ist die leichtgewichtige string-channel-MEL-Baseline.
- `MicrosoftAsyncEntryHandoff` ist die fairere Full-Entry-MEL-Baseline, die PicoLogs timestamp/category/message envelope-Kosten nachbildet, ohne echtes I/O hinzuzufügen.
- `PicoWaitControl_CachedMessage` und `PicoWaitHandoff_CachedMessage` decken PicoLog-Backpressure im Wait-Modus als relativen Vergleich ab.

Benchmark-Projekt ausführen:

```bash
dotnet run -c Release --project benchmarks/PicoLog.Benchmarks
```

Oder das Artefakt direkt veröffentlichen und ausführen:

```bash
dotnet publish benchmarks/PicoLog.Benchmarks/PicoLog.Benchmarks.csproj -c Release -r win-x64
benchmarks/PicoLog.Benchmarks/bin/Release/net10.0/win-x64/publish/PicoLog.Benchmarks.exe main
```

Die Benchmark-App schreibt:

- `benchmark-results.md`
- `benchmark-results-main.md`
- `benchmark-results-wait.md`

## Eignung und Nicht-Ziele

### Stärken

- Die Kernimplementierung ist klein und leicht nachvollziehbar: `LoggerFactory` besitzt Registrierungen pro Kategorie, Kategorien-Pipelines, Lebensdauern der Drain-Tasks und Sink-Lebensdauern, während jeder `InternalLogger` eine leichtgewichtige, nicht besitzende Schreibfassade bleibt.
- Das Projekt ist AOT-freundlich und vermeidet reflektionslastige Infrastruktur, was es zu einer guten Wahl für Native AOT-, Edge- und IoT-Workloads macht.
- Strukturierte Eigenschaften und integrierte Metriken decken häufige betriebliche Anforderungen ab, ohne der Anwendung ein größeres Logging-Ökosystem aufzuzwingen.
- Das Verhalten bei Warteschlangendruck ist explizit statt versteckt. Aufrufer können zwischen `DropOldest`, `DropWrite` und `Wait` wählen, je nachdem, ob Durchsatz oder Zustellung wichtiger ist.
- Die integrierte PicoDI-Integration bleibt schlank und vorhersehbar, statt einen großen Hosting- oder Konfigurations-Stack einzuführen.

### Gute Eignung

- Kleine bis mittelgroße .NET-Anwendungen, die einen leichtgewichtigen Logging-Kern wollen, ohne ein größeres Logging-Ökosystem zu übernehmen.
- Edge-, IoT-, Desktop- und Utility-Workloads, bei denen Startkosten, Binärgröße und AOT-Kompatibilität wichtig sind.
- Anwendungsszenarien für Logging, in denen Best-Effort-Zustellung akzeptabel ist und explizites Flushen beim Shutdown ausreicht.
- Teams, die eine kleine Menge an Grundbausteinen bevorzugen und bei Bedarf eigene Sinks oder Formatter ergänzen.

### Nicht-Ziele und Schwachstellen

- Dies ist keine vollständige Observability-Plattform. Es unterstützt strukturierte Eigenschaften und eine kleine integrierte Metrikoberfläche, bietet aber kein Message-Template-Parsing, keine Enricher, kein Rolling-File-Management, keine Remote-Transport-Sinks und keine tiefe Integration in breitere Telemetrie-Ökosysteme.
- Es ist nicht für Logger-Kategorien mit sehr hoher Kardinalität optimiert. Das aktuelle Design erzeugt pro Kategorie eine Factory-eigene Kategorien-Pipeline und eine Hintergrund-Drain-Task.
- Es ist keine Standardwahl für Audit- oder Compliance-Logging, bei dem stiller Verlust inakzeptabel ist. Der Standardmodus der Warteschlange bevorzugt Durchsatz, und stärkere Zustellgarantien erfordern explizite Konfiguration wie `Wait` oder eine dedizierte Sink-Strategie.
- Integrierte Metriken decken Warteschlangentiefe, akzeptierte und verworfene Einträge, Sink-Fehler, späte Schreibvorgänge während des Shutdowns und die Dauer des Shutdown-Drainings ab. Sie bieten derzeit noch keine Metriken pro Kategorie, keine Sink-Latenz-Histogramme und kein größeres End-to-End-Telemetriemodell.

## PicoLog erweitern

### Eigener Sink

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

### Eigener Formatter

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

Die Testsuite deckt derzeit Folgendes ab:

- Datei-Sink-Schreibvorgänge, die mit asynchroner Entsorgung konkurrieren
- Logger-Caching nach Kategorie
- Filterung nach Mindestlevel
- Erfassung und Formatierung strukturierter Nutzdaten
- Emission integrierter Metriken
- Scope-Erfassung und Flush bei Entsorgung der Factory
- Ablehnung von Schreibvorgängen nach Beginn des Shutdowns
- Isolation von Sink-Fehlern
- asynchrones Flushen von Abschlussnachrichten
- Persistenz des Dateisink-Endes auf echter Datei
- konfigurierte DI-Dateiausgabe
- Auflösung typisierter PicoDI-Logger

Das Beispiel wird ebenfalls durch Native-AOT-Veröffentlichung und -Ausführung verifiziert.

## Mitwirken

1. Repository forken
2. Einen Feature-Branch erstellen
3. Änderungen vornehmen
4. Tests hinzufügen oder aktualisieren
5. Einen Pull Request einreichen

## Lizenz

Dieses Projekt ist unter der MIT License lizenziert – Details siehe in der Datei [LICENSE](LICENSE).
