# PicoLog

[English](README.md) | [简体中文](README.zh.md) | [繁體中文](README.zh-TW.md) | [Deutsch](README.de.md) | [Español](README.es.md) | [Français](README.fr.md) | [日本語](README.ja.md) | [Português (Brasil)](README.pt-BR.md) | [Русский](README.ru.md)

![CI](https://github.com/PicoHex/PicoLog/actions/workflows/ci.yml/badge.svg)
[![NuGet](https://img.shields.io/nuget/v/PicoLog.svg)](https://www.nuget.org/packages/PicoLog)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

Un marco de logging ligero y compatible con AOT para cargas de trabajo .NET en edge e IoT. El repositorio contiene contratos, la implementación principal del logger, integración con PicoDI, una aplicación de ejemplo y un proyecto de benchmarks dedicado.

## Características

- **Compatibilidad con AOT**: apunta a `net10.0` y evita infraestructura con uso intensivo de reflexión
- **Canalización asíncrona acotada**: los loggers encolan entradas en un canal acotado y las escriben en sinks desde una tarea en segundo plano
- **Propiedades estructuradas**: las cargas opcionales de clave/valor fluyen a través de `LogEntry.Properties` y de la salida del formateador integrado
- **Métricas integradas**: el paquete principal emite métricas de cola, descarte, fallos de sink y apagado mediante `System.Diagnostics.Metrics`
- **Superficie mínima**: solo hace falta implementar unas pocas abstracciones principales para ampliar el sistema
- **Integración con PicoDI**: registros integrados para la logger factory y loggers tipados, con logging a consola por defecto y logging a archivo opcional cuando se configura una ruta de archivo
- **Cobertura de benchmarks**: incluye un proyecto de benchmarks basado en PicoBench con líneas base MEL de handoff asíncrono ligeras y más justas
- **Soporte de scopes**: los scopes anidados fluyen mediante `AsyncLocal` y se adjuntan a cada `LogEntry`

## Estructura del proyecto

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

## Instalación

### Biblioteca principal de logging

```bash
dotnet add package PicoLog
```

### Integración con PicoDI

```bash
dotnet add package PicoLog.DI
```

## Inicio rápido

### Uso básico

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

### Integración con DI

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

## Configuración

### Nivel mínimo

`LoggerFactory.MinLevel` controla qué entradas se aceptan. Los valores numéricos más bajos son más graves, por lo que el nivel predeterminado `Debug` permite desde `Emergency` hasta `Debug`, pero filtra `Trace`.

```csharp
await using var loggerFactory = new LoggerFactory(sinks)
{
    MinLevel = LogLevel.Warning
};
```

### Propiedad del ciclo de vida

`LoggerFactory` es propietaria de los loggers en caché por categoría, de sus canalizaciones por categoría, de las tareas en segundo plano que drenan esas canalizaciones y de los sinks registrados. Desecha la factory durante el apagado para vaciar las entradas en cola y liberar los recursos de los sinks. Una vez que comienza la liberación, las escrituras nuevas se rechazan mientras las entradas ya encoladas siguen drenándose.

```csharp
await using var loggerFactory = new LoggerFactory(sinks);

var logger = new Logger<MyService>(loggerFactory);
logger.Info("Starting up");

// queued entries are flushed when the factory is disposed;
// writes that race with shutdown are rejected
```

### Presión de cola

`LoggerFactoryOptions.QueueFullMode` hace explícito el manejo de la presión de cola tanto para escrituras síncronas como asíncronas:

- `DropOldest` mantiene el logging sin bloqueo descartando la entrada más antigua encolada. Es el valor predeterminado.
- `DropWrite` rechaza la entrada nueva y notifica la pérdida mediante `OnMessagesDropped`.
- `Wait` bloquea el logging síncrono hasta `SyncWriteTimeout` y hace que el logging asíncrono espere espacio en la cola hasta que se solicite cancelación.

```csharp
var options = new LoggerFactoryOptions
{
    MinLevel = LogLevel.Info,
    QueueFullMode = LogQueueFullMode.Wait,
    SyncWriteTimeout = TimeSpan.FromMilliseconds(500)
};

await using var loggerFactory = new LoggerFactory(sinks, options);
```

### Sinks integrados

- `ConsoleSink` escribe entradas con formato simple en la salida estándar.
- `ColoredConsoleSink` serializa los cambios de color para que el estado de la consola no se mezcle entre escrituras concurrentes.
- `FileSink` agrupa escrituras UTF-8 en archivo en una cola en segundo plano antes de vaciarlas a disco. `AddLogging()` crea los sinks configurados dentro de la logger factory para que la factory siga siendo la única propietaria de su ciclo de vida.

### Valores predeterminados de PicoDI

`AddLogging()` registra:

- un `ILoggerFactory` singleton
- adaptadores tipados `ILogger<T>`
- adaptadores tipados `IStructuredLogger<T>`
- un sink de consola por defecto
- un sink de archivo opcional cuando `FilePath` está configurado

Al apagar, desecha explícitamente la factory resuelta para que las entradas de log en cola se vacíen antes de que termine el proceso. Las escrituras que lleguen después de iniciado el apagado se rechazan en lugar de aceptarse tarde.

Puedes habilitar logging a archivo mediante el parámetro opcional `filePath`, estableciendo `options.FilePath` o estableciendo `options.File.FilePath` en la sobrecarga de configuración. Una ruta de archivo explícita se trata como una activación explícita del sink de archivo.

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

### Formateador integrado

`ConsoleFormatter` produce líneas legibles con marca de tiempo, nivel, categoría, mensaje, propiedades estructuradas opcionales, texto de excepción y scopes opcionales.

```csharp
var formatter = new ConsoleFormatter();
var sink = new ConsoleSink(formatter);
```

### Logging estructurado

PicoLog sigue siendo compatible con `ILogger` y expone logging estructurado garantizado mediante `IStructuredLogger` / `IStructuredLogger<T>`. Cuando usas la `LoggerFactory` integrada, el logger en tiempo de ejecución conserva las cargas de `LogStructured()` / `LogStructuredAsync()` en `LogEntry.Properties`.

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

`ConsoleFormatter` añade las propiedades estructuradas después del mensaje en un formato textual compacto, por ejemplo:

```text
[2026-04-05 11:30:42.123] WARNING   [MyService] Cache miss {cacheKey="user:42", node="edge-a", attempt=3}
```

### Extensiones de logging

Los métodos de extensión incluidos están definidos sobre `ILogger` y `ILogger<T>`:

- `Trace`, `Debug`, `Info`, `Notice`, `Warning`, `Error`, `Critical`, `Alert`, `Emergency`
- equivalentes asíncronos como `InfoAsync` y `ErrorAsync`
- `LogStructured` y `LogStructuredAsync` como adaptadores best-effort que conservan propiedades cuando el logger en tiempo de ejecución implementa `IStructuredLogger`, y en caso contrario vuelven al logging simple sin carga estructurada

Si necesitas un contrato estricto de logging estructurado, depende directamente de `IStructuredLogger` / `IStructuredLogger<T>`.

### Comportamiento ante desbordamiento

Tanto el logging síncrono como el asíncrono escriben en un canal acotado.

- Tanto `Log()` síncrono como `LogAsync()` asíncrono siguen `LoggerFactoryOptions.QueueFullMode`.
- El valor predeterminado es `DropOldest`, que favorece el rendimiento y la baja latencia del llamador frente a la entrega garantizada.
- `Wait` hace visible la contrapresión al llamador bloqueando las escrituras síncronas hasta que haya espacio en la cola o expire `SyncWriteTimeout`, y esperando espacio en cola para las escrituras asíncronas hasta que se solicite cancelación.
- `DropWrite` conserva las entradas más antiguas en cola e informa de las nuevas entradas descartadas mediante `OnMessagesDropped`.
- Una vez que comienza la liberación de la factory, las nuevas escrituras se rechazan mientras las entradas ya encoladas continúan vaciándose.

Para el logging general de aplicaciones, el comportamiento predeterminado `DropOldest` suele ser aceptable. Para logging de tipo auditoría, conviene preferir `Wait` o una estrategia de sink dedicada.

### Métricas integradas

El paquete principal `PicoLog` emite una pequeña superficie de métricas integradas a través de `System.Diagnostics.Metrics` usando el nombre de meter `PicoLog`.

- `picolog.entries.enqueued`
- `picolog.entries.dropped`
- `picolog.sinks.failures`
- `picolog.writes.rejected_after_shutdown`
- `picolog.queue.entries`
- `picolog.shutdown.drain.duration`

Estos instrumentos están diseñados para mantener baja cardinalidad y ligereza. Se pueden observar directamente con `MeterListener` o integrarse en una infraestructura de telemetría más amplia.

```csharp
using var listener = new MeterListener();

listener.InstrumentPublished = (instrument, meterListener) =>
{
    if (instrument.Meter.Name == PicoLogMetrics.MeterName)
        meterListener.EnableMeasurementEvents(instrument);
};

listener.Start();
```

## Compatibilidad con AOT

El proyecto de ejemplo se publica con Native AOT habilitado:

```xml
<PropertyGroup>
  <PublishAOT>true</PublishAOT>
</PropertyGroup>
```

```bash
dotnet publish -c Release -r win-x64 -p:PublishAOT=true
```

El repositorio también incluye un script de validación a nivel de publicación que publica el ejemplo, ejecuta el binario generado y verifica que las entradas finales de log de apagado se hayan vaciado correctamente:

```powershell
./scripts/Validate-AotSample.ps1
```

## Compilar desde el código fuente

### Requisitos previos

- .NET SDK 10.0 o posterior
- Git

### Clonar y compilar

```bash
git clone https://github.com/PicoHex/PicoLog.git
cd PicoLog
dotnet restore
dotnet build --configuration Release
```

### Ejecutar pruebas

```bash
dotnet test --solution ./PicoLog.slnx --configuration Release
```

## Consideraciones de rendimiento

- Las instancias de logger se almacenan en caché por categoría dentro de `LoggerFactory`.
- `LoggerFactory` posee un canal acotado, una canalización por categoría y una tarea de drenado en segundo plano por categoría.
- Al liberar la factory, se vacían todos los loggers activos antes de liberar los sinks.
- `FileSink` agrupa escrituras en su propia cola acotada y vacía en los límites de lote o de intervalo de flush.
- Elegir `DropOldest`, `DropWrite` o `Wait` es una compensación entre rendimiento y entrega, no un error de corrección.

## Benchmarks

El repositorio incluye `benchmarks/PicoLog.Benchmarks`, un proyecto de benchmarks basado en PicoBench para comparar los costes de handoff de PicoLog con las líneas base de Microsoft.Extensions.Logging.

- `MicrosoftAsyncHandoff` es la línea base MEL ligera basada en un canal de cadenas.
- `MicrosoftAsyncEntryHandoff` es la línea base MEL más justa de entrada completa, que refleja el coste del envoltorio timestamp/category/message de PicoLog sin añadir E/S real.
- `PicoWaitControl_CachedMessage` y `PicoWaitHandoff_CachedMessage` cubren la contrapresión de PicoLog en modo wait como comparación relativa.

Ejecuta el proyecto de benchmarks:

```bash
dotnet run -c Release --project benchmarks/PicoLog.Benchmarks
```

O publica y ejecuta el artefacto directamente:

```bash
dotnet publish benchmarks/PicoLog.Benchmarks/PicoLog.Benchmarks.csproj -c Release -r win-x64
benchmarks/PicoLog.Benchmarks/bin/Release/net10.0/win-x64/publish/PicoLog.Benchmarks.exe main
```

La aplicación de benchmarks escribe:

- `benchmark-results.md`
- `benchmark-results-main.md`
- `benchmark-results-wait.md`

## Encaje y no objetivos

### Fortalezas

- La implementación principal es pequeña y fácil de razonar: `LoggerFactory` posee los registros de logger por categoría, las canalizaciones por categoría, la vida útil de las tareas de drenado y la vida útil de los sinks, mientras que cada `InternalLogger` sigue siendo una fachada ligera de escritura sin propiedad.
- El proyecto es compatible con AOT y evita infraestructura con mucho uso de reflexión, lo que lo convierte en una buena opción para cargas Native AOT, edge e IoT.
- Las propiedades estructuradas y las métricas integradas cubren necesidades operativas comunes sin obligar a la aplicación a adoptar un ecosistema de logging mayor.
- El comportamiento ante la presión de cola es explícito en lugar de oculto. Quien llama puede elegir entre `DropOldest`, `DropWrite` y `Wait` según le importe más el rendimiento o la entrega.
- La integración integrada con PicoDI se mantiene fina y predecible en lugar de introducir una gran pila de hosting o configuración.

### Buen encaje

- Aplicaciones .NET pequeñas o medianas que quieren un núcleo de logging ligero sin adoptar un ecosistema mayor.
- Cargas de trabajo edge, IoT, de escritorio y utilitarias donde importan el coste de arranque, el tamaño binario y la compatibilidad con AOT.
- Escenarios de logging de aplicación donde la entrega best-effort es aceptable y basta con un vaciado explícito al apagar.
- Equipos que prefieren un conjunto pequeño de primitivas y se sienten cómodos añadiendo sinks o formateadores personalizados cuando sea necesario.

### No objetivos y puntos débiles

- Esta no es una plataforma de observabilidad completa. Soporta propiedades estructuradas y una pequeña superficie de métricas integradas, pero no ofrece análisis de plantillas de mensaje, enrichers, gestión de archivos rotativos, sinks de transporte remoto ni integración profunda con ecosistemas de telemetría más amplios.
- No está optimizado para categorías de logger de cardinalidad muy alta. El diseño actual crea una canalización por categoría propiedad de la factory y una tarea de drenado en segundo plano por categoría.
- No es una opción predeterminada para logging de auditoría o cumplimiento donde la pérdida silenciosa sea inaceptable. El modo de cola predeterminado favorece el rendimiento, y unas garantías de entrega más fuertes requieren configuración explícita como `Wait` o una estrategia de sink dedicada.
- Las métricas integradas cubren profundidad de cola, entradas aceptadas y descartadas, fallos de sink, escrituras tardías durante el apagado y duración del drenado de apagado. Aún no exponen métricas por categoría, histogramas de latencia de sink ni un modelo de telemetría de extremo a extremo más amplio.

## Extender PicoLog

### Sink personalizado

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

### Formateador personalizado

```csharp
public class CustomFormatter : ILogFormatter
{
    public string Format(LogEntry entry)
    {
        return $"{entry.Timestamp:yyyy-MM-dd HH:mm:ss} [{entry.Level}] {entry.Message}";
    }
}
```

## Pruebas

La suite de pruebas cubre actualmente:

- escrituras del file sink compitiendo con la liberación asíncrona
- almacenamiento en caché de logger por categoría
- filtrado por nivel mínimo
- captura y formateo de cargas estructuradas
- emisión de métricas integradas
- captura de scope y flush al liberar la factory
- rechazo de escrituras una vez iniciado el apagado
- aislamiento de fallos de sink
- flush asíncrono de mensajes finales
- persistencia real del tramo final del file sink
- salida DI a archivo configurada
- resolución de loggers tipados de PicoDI

La muestra también se verifica mediante publicación y ejecución con Native AOT.

## Contribuir

1. Haz un fork del repositorio
2. Crea una rama de funcionalidad
3. Realiza tus cambios
4. Añade o actualiza pruebas
5. Envía un pull request

## Licencia

Este proyecto está licenciado bajo la MIT License; consulta el archivo [LICENSE](LICENSE) para más detalles.
