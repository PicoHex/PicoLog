# PicoLog

[English](README.md) | [简体中文](README.zh.md) | [繁體中文](README.zh-TW.md) | [Deutsch](README.de.md) | [Español](README.es.md) | [Français](README.fr.md) | [日本語](README.ja.md) | [Português (Brasil)](README.pt-BR.md) | [Русский](README.ru.md)

![CI](https://github.com/PicoHex/PicoLog/actions/workflows/ci.yml/badge.svg)
[![NuGet](https://img.shields.io/nuget/v/PicoLog.svg)](https://www.nuget.org/packages/PicoLog)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

Un framework de journalisation léger et compatible AOT pour les charges de travail .NET edge et IoT. Le dépôt contient les contrats, l’implémentation principale du logger, l’intégration PicoDI, une application d’exemple et un projet de benchmarks dédié.

## Fonctionnalités

- **Compatibilité AOT** : cible `net10.0` et évite une infrastructure fortement basée sur la réflexion
- **Pipeline asynchrone borné** : les loggers placent les entrées dans un canal borné et écrivent vers des sinks depuis une tâche en arrière-plan
- **Compagnons de flush explicites** : `IFlushableLoggerFactory`, `IFlushableLogSink` et les extensions `FlushAsync()` best-effort rendent explicites les barrières de flush en cours d’exécution sans les confondre avec l’arrêt
- **Propriétés structurées** : les charges utiles clé/valeur facultatives transitent via `LogEntry.Properties` et la sortie du formateur intégré
- **Métriques intégrées** : le package principal émet des métriques de file, de perte, d’échec de sink et d’arrêt via `System.Diagnostics.Metrics`
- **Surface minimale** : seules quelques abstractions principales doivent être implémentées pour étendre le système
- **Intégration PicoDI** : enregistrements intégrés pour la logger factory et les loggers typés, avec journalisation console par défaut et journalisation fichier optionnelle lorsqu’un chemin de fichier est configuré
- **Couverture de benchmarks** : inclut un projet de benchmarks basé sur PicoBench avec des références MEL de handoff asynchrone légères et plus équitables
- **Prise en charge des scopes** : les scopes imbriqués transitent via `AsyncLocal` et sont attachés à chaque `LogEntry`

## Structure du projet

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

### Bibliothèque principale de journalisation

```bash
dotnet add package PicoLog
```

### Intégration PicoDI

```bash
dotnet add package PicoLog.DI
```

## Démarrage rapide

### Utilisation de base

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

await loggerFactory.FlushAsync(); // barrière pour les entrées déjà acceptées, pas un arrêt
```

`FlushAsync()` sur la factory n’est pas une suppression. Elle attend que les entrées acceptées avant l’instantané de flush aient franchi le pipeline de la factory, tandis que `DisposeAsync()` reste la voie d’arrêt pour le drain final et la libération des ressources.

### Intégration DI

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

await scope.GetService<ILoggerFactory>().FlushAsync();
await scope.GetService<ILoggerFactory>().DisposeAsync();
```

L’extension `FlushAsync()` sur `ILoggerFactory` est best-effort. Elle relaie vers `IFlushableLoggerFactory` quand c’est pris en charge, sinon elle se termine immédiatement.

## Configuration

### Niveau minimal

`LoggerFactory.MinLevel` contrôle quelles entrées sont acceptées. Les valeurs numériques plus faibles sont plus sévères ; ainsi, le niveau `Debug` par défaut autorise `Emergency` à `Debug`, mais filtre `Trace`.

```csharp
await using var loggerFactory = new LoggerFactory(sinks)
{
    MinLevel = LogLevel.Warning
};
```

### Propriété du cycle de vie

`LoggerFactory` possède les loggers mis en cache par catégorie, leurs pipelines par catégorie, les tâches d’évacuation en arrière-plan exécutées par ces pipelines et les sinks enregistrés. `FlushAsync()` est une barrière en cours d’exécution, pas une suppression. Elle attend que les entrées acceptées avant l’instantané de flush aient franchi le pipeline de la factory. Utilisez `DisposeAsync()` lors de l’arrêt pour le drain final et la libération des ressources des sinks. Dès que la suppression commence, les nouvelles écritures sont rejetées tandis que les entrées déjà en file d’attente continuent d’être évacuées.

```csharp
await using var loggerFactory = new LoggerFactory(sinks);

var logger = new Logger<MyService>(loggerFactory);
logger.Info("Starting up");
await loggerFactory.FlushAsync();

// FlushAsync est une barrière pour les entrées déjà acceptées.
// DisposeAsync effectue le drain final et rejette les écritures en course avec l’arrêt.
```

### Pression sur la file d’attente

`LoggerFactoryOptions.QueueFullMode` rend explicite la gestion de la pression de file pour les écritures synchrones et asynchrones. `LogAsync()` et `LogStructuredAsync()` se terminent quand le traitement du handoff à la frontière du logger est terminé : l’entrée peut avoir été acceptée, supprimée par la politique de file, ou rejetée pendant l’arrêt, et la contre-pression en mode `Wait` y est également résolue. Cela ne veut pas dire qu’un sink a terminé une écriture durable :

- `DropOldest` conserve un logging non bloquant en supprimant l’entrée la plus ancienne de la file. C’est le comportement par défaut.
- `DropWrite` rejette la nouvelle entrée et signale la perte via `OnMessagesDropped`.
- `Wait` bloque le logging synchrone jusqu’à `SyncWriteTimeout` et fait attendre le logging asynchrone jusqu’à ce qu’une place se libère dans la file ou qu’une annulation soit demandée.

```csharp
var options = new LoggerFactoryOptions
{
    MinLevel = LogLevel.Info,
    QueueFullMode = LogQueueFullMode.Wait,
    SyncWriteTimeout = TimeSpan.FromMilliseconds(500)
};

await using var loggerFactory = new LoggerFactory(sinks, options);
```

### Sinks intégrés

- `ConsoleSink` écrit des entrées simplement formatées vers la sortie standard.
- `ColoredConsoleSink` sérialise les changements de couleur afin que l’état de la console ne fuit pas entre écritures concurrentes.
- `FileSink` regroupe les écritures de fichiers UTF-8 dans une file en arrière-plan avant de les vider sur disque et prend en charge le flush au niveau du sink via `IFlushableLogSink`. `AddLogging()` crée les sinks configurés à l’intérieur de la logger factory afin que la factory reste l’unique propriétaire de leur durée de vie.

### Valeurs par défaut PicoDI

`AddLogging()` enregistre :

- un `ILoggerFactory` singleton
- des adaptateurs typés `ILogger<T>`
- des adaptateurs typés `IStructuredLogger<T>`
- un sink console par défaut
- un sink fichier optionnel lorsque `FilePath` est configuré

Pendant la vie de l’application, vous pouvez appeler `ILoggerFactory.FlushAsync()` comme barrière best-effort. Elle relaie vers `IFlushableLoggerFactory` quand elle est disponible, sinon elle se termine immédiatement. Lors de l’arrêt, supprimez explicitement la factory résolue afin que les entrées de log en file d’attente soient drainées avant la fin du processus. Les écritures qui arrivent après le début de l’arrêt sont rejetées au lieu d’être acceptées trop tard.

Vous pouvez activer la journalisation fichier soit via le paramètre optionnel `filePath`, soit en définissant `options.FilePath`, soit en définissant `options.File.FilePath` dans la surcharge de configuration. Un chemin de fichier explicite est traité comme un opt-in explicite pour le sink fichier.

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

### Formateur intégré

`ConsoleFormatter` produit des lignes lisibles contenant l’horodatage, le niveau, la catégorie, le message, les propriétés structurées facultatives, le texte d’exception et les scopes facultatifs.

```csharp
var formatter = new ConsoleFormatter();
var sink = new ConsoleSink(formatter);
```

### Journalisation structurée

PicoLog reste compatible avec `ILogger` et expose une journalisation structurée garantie via `IStructuredLogger` / `IStructuredLogger<T>`. Lorsque vous utilisez la `LoggerFactory` intégrée, le logger d’exécution conserve les charges de `LogStructured()` / `LogStructuredAsync()` dans `LogEntry.Properties`.

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

`ConsoleFormatter` ajoute les propriétés structurées après le message sous une forme textuelle compacte, par exemple :

```text
[2026-04-05 11:30:42.123] WARNING   [MyService] Cache miss {cacheKey="user:42", node="edge-a", attempt=3}
```

### Extensions de logging

Les méthodes d’extension fournies sont définies sur `ILogger` et `ILogger<T>` :

- `Trace`, `Debug`, `Info`, `Notice`, `Warning`, `Error`, `Critical`, `Alert`, `Emergency`
- leurs équivalents asynchrones tels que `InfoAsync` et `ErrorAsync`
- `LogStructured` et `LogStructuredAsync` comme adaptateurs best-effort qui préservent les propriétés lorsque le logger d’exécution implémente `IStructuredLogger`, et qui sinon reviennent à une journalisation simple sans charge structurée
- des extensions `FlushAsync()` best-effort sur `ILoggerFactory` et `ILogSink` qui relaient quand le type d’exécution prend en charge le flush, sinon se terminent immédiatement

Si vous avez besoin d’un contrat strict de journalisation structurée, dépendez directement de `IStructuredLogger` / `IStructuredLogger<T>`. Si vous avez besoin d’un contrat strict de flush, dépendez directement de `IFlushableLoggerFactory` ou `IFlushableLogSink`.

### Comportement en cas de saturation

La journalisation synchrone comme asynchrone écrit dans un canal borné.

- `Log()` synchrone et `LogAsync()` asynchrone suivent tous deux `LoggerFactoryOptions.QueueFullMode`.
- La fin de `LogAsync()` ou `LogStructuredAsync()` signifie que le traitement du handoff à la frontière du logger y est terminé. L’entrée peut avoir été acceptée, supprimée par la politique de file, ou rejetée pendant l’arrêt. Elle ne signifie pas une fin durable côté sink.
- Le comportement par défaut est `DropOldest`, qui favorise le débit et une faible latence côté appelant plutôt qu’une livraison garantie.
- `Wait` rend la contre-pression visible pour l’appelant en bloquant les écritures synchrones jusqu’à ce qu’une place se libère ou que `SyncWriteTimeout` expire, et en faisant attendre les écritures asynchrones jusqu’à ce qu’une place se libère ou qu’une annulation soit demandée.
- `DropWrite` préserve les anciennes entrées en file d’attente et signale les nouvelles entrées supprimées via `OnMessagesDropped`.
- Dès que la suppression de la factory commence, les nouvelles écritures sont rejetées tandis que les entrées déjà en file d’attente continuent d’être vidées.

Pour la journalisation applicative générale, le comportement par défaut `DropOldest` est généralement acceptable. Pour une journalisation de type audit, préférez `Wait` ou une stratégie de sink dédiée.

### Métriques intégrées

Le package principal `PicoLog` émet une petite surface de métriques intégrées via `System.Diagnostics.Metrics` en utilisant le nom de meter `PicoLog`.

- `picolog.entries.enqueued`
- `picolog.entries.dropped`
- `picolog.sinks.failures`
- `picolog.writes.rejected_after_shutdown`
- `picolog.queue.entries`
- `picolog.shutdown.drain.duration`

Ces instruments sont conçus pour rester légers et à faible cardinalité. Ils peuvent être observés directement avec `MeterListener` ou reliés à une infrastructure de télémétrie plus large.

```csharp
using var listener = new MeterListener();

listener.InstrumentPublished = (instrument, meterListener) =>
{
    if (instrument.Meter.Name == PicoLogMetrics.MeterName)
        meterListener.EnableMeasurementEvents(instrument);
};

listener.Start();
```

## Compatibilité AOT

Le projet d’exemple se publie avec Native AOT activé :

```xml
<PropertyGroup>
  <PublishAOT>true</PublishAOT>
</PropertyGroup>
```

```bash
dotnet publish -c Release -r win-x64 -p:PublishAOT=true
```

Le dépôt inclut également un script de validation au niveau publication qui publie l’exemple, exécute le binaire généré et vérifie que les dernières entrées de log d’arrêt ont bien été vidées :

```powershell
./scripts/Validate-AotSample.ps1
```

## Construction depuis les sources

### Prérequis

- .NET SDK 10.0 ou version ultérieure
- Git

### Cloner et construire

```bash
git clone https://github.com/PicoHex/PicoLog.git
cd PicoLog
dotnet restore
dotnet build --configuration Release
```

### Exécuter les tests

```bash
dotnet test --solution ./PicoLog.slnx --configuration Release
```

## Considérations de performance

- Les instances de logger sont mises en cache par catégorie à l’intérieur de `LoggerFactory`.
- `LoggerFactory` possède un canal borné, un pipeline de catégorie et une tâche d’évacuation en arrière-plan par catégorie.
- `FlushAsync()` sur la factory est une barrière pour les entrées acceptées avant l’instantané de flush, pas un raccourci de suppression.
- La suppression de la factory effectue toujours le drain final avant de supprimer les sinks.
- `FileSink` regroupe les écritures dans sa propre file bornée, effectue un flush aux limites de lot ou d’intervalle de flush, et expose un flush au niveau du sink via `IFlushableLogSink`.
- Choisir `DropOldest`, `DropWrite` ou `Wait` est un compromis débit/livraison, pas un bug de correction.

## Benchmarks

Le dépôt inclut `benchmarks/PicoLog.Benchmarks`, un projet de benchmarks basé sur PicoBench pour comparer les coûts de handoff de PicoLog avec les références Microsoft.Extensions.Logging.

- `MicrosoftAsyncHandoff` est la référence MEL légère basée sur un canal de chaînes.
- `MicrosoftAsyncEntryHandoff` est la référence MEL plus équitable à entrée complète, qui reflète le coût de l’enveloppe timestamp/category/message de PicoLog sans ajouter de véritable E/S.
- `PicoWaitControl_CachedMessage` et `PicoWaitHandoff_CachedMessage` couvrent la contre-pression en mode wait de PicoLog à titre de comparaison relative.

Exécutez le projet de benchmarks :

```bash
dotnet run -c Release --project benchmarks/PicoLog.Benchmarks
```

Ou publiez et exécutez directement l’artefact :

```bash
dotnet publish benchmarks/PicoLog.Benchmarks/PicoLog.Benchmarks.csproj -c Release -r win-x64
benchmarks/PicoLog.Benchmarks/bin/Release/net10.0/win-x64/publish/PicoLog.Benchmarks.exe main
```

L’application de benchmarks écrit :

- `benchmark-results.md`
- `benchmark-results-main.md`
- `benchmark-results-wait.md`

## Pertinence et non-objectifs

### Points forts

- L’implémentation principale est petite et facile à raisonner : `LoggerFactory` possède les enregistrements de logger par catégorie, les pipelines de catégorie, les durées de vie des tâches d’évacuation et des sinks, tandis que chaque `InternalLogger` reste une façade d’écriture légère sans propriété.
- Le projet est compatible AOT et évite les infrastructures fortement basées sur la réflexion, ce qui en fait un bon choix pour les charges Native AOT, edge et IoT.
- Les propriétés structurées et les métriques intégrées couvrent les besoins opérationnels courants sans imposer à l’application un écosystème de journalisation plus vaste.
- Le comportement sous pression de file est explicite plutôt que caché. Les appelants peuvent choisir entre `DropOldest`, `DropWrite` et `Wait` selon que le débit ou la livraison compte davantage.
- La sémantique de flush reste explicite. `FlushAsync()` est une barrière pour le travail déjà accepté, tandis que `DisposeAsync()` reste la voie d’arrêt pour le drain final et la libération des ressources.
- L’intégration PicoDI intégrée reste fine et prévisible au lieu d’introduire une grosse pile d’hébergement ou de configuration.

### Bons cas d’usage

- Applications .NET petites à moyennes qui veulent un cœur de journalisation léger sans adopter un écosystème de journalisation plus large.
- Charges edge, IoT, desktop et utilitaires où le coût de démarrage, la taille binaire et la compatibilité AOT comptent.
- Scénarios de journalisation applicative où une livraison best-effort est acceptable, où des barrières de flush en cours d’exécution sont parfois utiles, et où un drain explicite à l’arrêt suffit.
- Équipes qui préfèrent un petit ensemble de primitives et acceptent d’ajouter des sinks ou des formateurs personnalisés selon les besoins.

### Non-objectifs et points faibles

- Ce n’est pas une plateforme complète d’observabilité. Elle prend en charge les propriétés structurées et une petite surface de métriques intégrées, mais ne fournit pas l’analyse de modèles de messages, les enrichers, la gestion de fichiers rotatifs, les sinks de transport distants ni une intégration poussée avec des écosystèmes de télémétrie plus larges.
- Elle n’est pas optimisée pour des catégories de logger à très forte cardinalité. La conception actuelle crée un pipeline de catégorie appartenant à la factory et une tâche d’évacuation en arrière-plan par catégorie.
- Ce n’est pas un choix par défaut pour la journalisation d’audit ou de conformité lorsque la perte silencieuse est inacceptable. Le mode de file d’attente par défaut privilégie le débit, la fin de `LogAsync()` n’est pas une fin durable côté sink, et des garanties de livraison plus fortes nécessitent une configuration explicite telle que `Wait` ou une stratégie de sink dédiée.
- Les métriques intégrées couvrent la profondeur de file, les entrées acceptées et rejetées, les échecs de sink, les écritures tardives pendant l’arrêt et la durée d’évacuation à l’arrêt. Elles n’exposent pas encore de métriques par catégorie, d’histogrammes de latence des sinks ni de modèle de télémétrie de bout en bout plus vaste.

## Étendre PicoLog

### Sink personnalisé

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

Si un sink n’implémente pas `IFlushableLogSink`, l’extension `ILogSink.FlushAsync()` reste best-effort et se termine immédiatement.

### Formateur personnalisé

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

La suite de tests couvre actuellement :

- les écritures du file sink en concurrence avec la suppression asynchrone
- la mise en cache des loggers par catégorie
- le filtrage par niveau minimal
- la capture et le formatage des charges structurées
- l’émission des métriques intégrées
- la capture des scopes et le flush lors de la suppression de la factory
- les barrières de flush pour les entrées acceptées et les extensions de flush best-effort
- le rejet des écritures après le début de l’arrêt
- l’isolation des échecs de sink
- le flush asynchrone des messages de fin
- la persistance réelle de la fin du file sink
- la sortie fichier DI configurée
- la résolution des loggers typés PicoDI

L’exemple est également vérifié via la publication et l’exécution Native AOT.

## Contribution

1. Forkez le dépôt
2. Créez une branche de fonctionnalité
3. Apportez vos modifications
4. Ajoutez ou mettez à jour des tests
5. Soumettez une pull request

## Licence

Ce projet est distribué sous MIT License ; consultez le fichier [LICENSE](LICENSE) pour plus de détails.
