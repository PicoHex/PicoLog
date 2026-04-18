# PicoLog

[English](README.md) | [简体中文](README.zh.md) | [繁體中文](README.zh-TW.md) | [Deutsch](README.de.md) | [Español](README.es.md) | [Français](README.fr.md) | [日本語](README.ja.md) | [Português (Brasil)](README.pt-BR.md) | [Русский](README.ru.md)

![CI](https://github.com/PicoHex/PicoLog/actions/workflows/ci.yml/badge.svg)
[![NuGet](https://img.shields.io/nuget/v/PicoLog.svg)](https://www.nuget.org/packages/PicoLog)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

PicoLog é um framework de logging leve e compatível com AOT para cargas de trabalho .NET de edge, desktop, utilitários e IoT.

O design atual é intencionalmente pequeno:

- **um modelo de logger**: `ILogger` / `ILogger<T>`
- **um ponto de entrada de DI**: `AddPicoLog(...)`
- **um dono do ciclo de vida**: `ILoggerFactory`

As propriedades estruturadas fazem parte do próprio evento de log, não de um tipo de logger separado. Tipos de runtime e extensibilidade, como sinks, formatters, `LogEntry` e companions de flush, ficam em `PicoLog`, enquanto os contratos voltados ao consumidor ficam em `PicoLog.Abs`.

## Recursos

- **Design compatível com AOT**: evita infraestrutura fortemente dependente de reflexão e inclui um exemplo com Native AOT
- **Pipeline assíncrono limitado**: os loggers entregam entradas a pipelines por categoria apoiados por canais limitados
- **Semântica explícita de ciclo de vida**: `FlushAsync()` é uma barreira durante a execução, `DisposeAsync()` é o drain de desligamento
- **Propriedades estruturadas em `ILogger`**: overloads nativos preservam cargas de chave/valor em `LogEntry.Properties`
- **Superfície de DI pequena**: `AddPicoLog(...)` registra `ILoggerFactory` e adaptadores tipados `ILogger<T>`
- **Sinks e formatter integrados**: console, console colorido, arquivo e um formatter de texto legível
- **Contratos companion de flush**: os recursos de flush em runtime continuam disponíveis por meio de `IFlushableLoggerFactory` e `IFlushableLogSink`
- **Métricas integradas**: métricas de fila, descarte, falha de sink e desligamento via `System.Diagnostics.Metrics`
- **Projeto de benchmark**: benchmarks baseados em PicoBench para os custos de handoff do PicoLog e linhas de base MEL
- **Suporte a escopos**: escopos aninhados fluem por `AsyncLocal` e são anexados a cada `LogEntry`

## Estrutura do projeto

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

## Instalação

### Runtime principal

```bash
dotnet add package PicoLog
```

### Integração com PicoDI

```bash
dotnet add package PicoLog.DI
```

## Início rápido

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

`FlushAsync()` **não** é descarte. É uma barreira para entradas que já tinham sido aceitas antes do snapshot de flush. Use `DisposeAsync()` para o drain final no desligamento e a limpeza dos sinks.

### Integração com DI

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

`AddPicoLog()` é o único ponto de entrada de DI. O código de negócio normalmente deve depender de `ILogger<T>`. `ILoggerFactory` é o dono explícito do ciclo de vida na raiz da aplicação para flush e desligamento.

## Modelo central

### Um modelo de logger

O PicoLog não divide mais o logging entre interfaces de logger “plain” e “structured”.

- `ILogger` / `ILogger<T>` é a principal superfície de escrita
- eventos simples usam `Log(level, message, exception?)`
- eventos estruturados usam `Log(level, message, properties, exception)`
- variantes assíncronas seguem o mesmo formato por meio de `LogAsync(...)`

`LogStructured()` e `LogStructuredAsync()` ainda existem como wrappers de conveniência em `LoggerExtensions`, mas são apenas açúcar sintático sobre os overloads nativos de `ILogger`.

### Divisão de pacotes

- **`PicoLog.Abs`**: contratos voltados ao consumidor, como `ILogger`, `ILogger<T>`, `ILoggerFactory`, `LogLevel` e `LoggerExtensions`
- **`PicoLog`**: tipos de runtime e extensibilidade, como `LoggerFactory`, `Logger<T>`, `LogEntry`, `ILogSink`, `ILogFormatter`, `IFlushableLoggerFactory` e `IFlushableLogSink`
- **`PicoLog.DI`**: integração com PicoDI por meio de `AddPicoLog(...)`

## Configuração

### Nível mínimo

`LoggerFactory.MinLevel` controla quais entradas são aceitas. Valores numéricos menores são mais severos, então o nível padrão `Debug` permite de `Emergency` até `Debug`, mas filtra `Trace`.

```csharp
await using var loggerFactory = new LoggerFactory(sinks)
{
    MinLevel = LogLevel.Warning
};
```

### Propriedade do ciclo de vida

`LoggerFactory` possui:

- loggers em cache por categoria
- pipelines por categoria
- tarefas de drain em segundo plano
- sinks registrados

Isso significa que as APIs de ciclo de vida pertencem à história da factory, não a `ILogger<T>`.

```csharp
await using var loggerFactory = new LoggerFactory(sinks);

var logger = new Logger<MyService>(loggerFactory);
logger.Info("Starting up");

await loggerFactory.FlushAsync();   // mid-run barrier
await loggerFactory.DisposeAsync(); // final shutdown drain
```

Se você resolver `ILoggerFactory` a partir de DI, lembre-se de que ela ainda é a singleton de ciclo de vida no nível da aplicação. Resolvê-la a partir de um scope **não** a torna pertencente a esse scope.

### Pressão de fila

`LoggerFactoryOptions.QueueFullMode` torna explícita a pressão de fila para escritas síncronas e assíncronas.

A conclusão de `LogAsync()` significa que o tratamento do handoff no limite do logger terminou ali. A entrada pode ter sido:

- aceita
- descartada pela política da fila
- rejeitada durante o desligamento

Isso **não** significa que um sink terminou uma escrita durável.

- `DropOldest` mantém o logging sem bloqueio descartando a entrada enfileirada mais antiga. Esse é o padrão.
- `DropWrite` rejeita a nova entrada e informa o descarte por meio de `OnMessagesDropped`.
- `Wait` bloqueia o logging síncrono até `SyncWriteTimeout` e faz o logging assíncrono aguardar espaço na fila até que o cancelamento seja solicitado.

```csharp
var options = new LoggerFactoryOptions
{
    MinLevel = LogLevel.Info,
    QueueFullMode = LogQueueFullMode.Wait,
    SyncWriteTimeout = TimeSpan.FromMilliseconds(500)
};

await using var loggerFactory = new LoggerFactory(sinks, options);
```

## Peças de runtime integradas

### Sinks

- `ConsoleSink` escreve entradas formatadas de forma simples na saída padrão.
- `ColoredConsoleSink` serializa as mudanças de cor para que o estado do console não vaze entre escritas concorrentes.
- `FileSink` agrupa escritas UTF-8 em arquivo em uma fila em segundo plano antes de fazer flush para o disco e oferece suporte a flush no nível do sink por meio de `IFlushableLogSink`.

Ao usar `AddPicoLog()`, os sinks configurados são criados dentro da logger factory, então a factory continua sendo a única dona de seu tempo de vida.

### Formatter

`ConsoleFormatter` produz linhas legíveis com timestamp, nível, categoria, mensagem, propriedades estruturadas opcionais, texto de exceção e escopos opcionais.

```csharp
var formatter = new ConsoleFormatter();
var sink = new ConsoleSink(formatter);
```

### Logging estruturado

Os dados estruturados fazem parte do próprio evento de log.

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

Essas propriedades são preservadas em `LogEntry.Properties`, e os sinks ou formatters decidem como consumi-las.

Por exemplo, `ConsoleFormatter` as acrescenta em uma forma textual compacta:

```text
[2026-04-05 11:30:42.123] WARNING   [MyService] Cache miss {cacheKey="user:42", node="edge-a", attempt=3}
```

## Extensões de logging

Os métodos de extensão fornecidos são definidos em `ILogger` e `ILogger<T>`:

- `Trace`, `Debug`, `Info`, `Notice`, `Warning`, `Error`, `Critical`, `Alert`, `Emergency`
- equivalentes assíncronos como `InfoAsync` e `ErrorAsync`
- `LogStructured` e `LogStructuredAsync` como wrappers de conveniência sobre os overloads nativos de `ILogger` com suporte a propriedades
- extensões `FlushAsync()` best-effort em `ILoggerFactory` e `ILogSink`

A extensão `ILoggerFactory.FlushAsync()` fica em `PicoLog`, não em `PicoLog.Abs`. A capacidade estrita de runtime continua sendo `IFlushableLoggerFactory`, enquanto a extensão mantém simples o ponto de chamada comum.

## Integração com PicoDI

`AddPicoLog()` registra:

- um `ILoggerFactory` singleton
- adaptadores tipados `ILogger<T>`
- comportamento padrão de sink embutido quando nenhum pipeline `WriteTo` explícito é configurado
- ponte opcional de sinks registrados em DI quando `ReadFrom.RegisteredSinks()` está habilitado

Para código novo, prefira o builder de sink `WriteTo` como caminho principal de configuração.

```csharp
container.AddPicoLog(options =>
{
    options.MinLevel = LogLevel.Info;
    options.WriteTo.ColoredConsole();
    options.WriteTo.File("logs/app.log");
});
```

Você também pode fazer a ponte com sinks já registrados no PicoDI:

```csharp
container.Register(new SvcDescriptor(typeof(ILogSink), _ => new AuditSink()));

container.AddPicoLog(options =>
{
    options.ReadFrom.RegisteredSinks();
    options.WriteTo.ColoredConsole();
});
```

## Métricas

O pacote principal `PicoLog` emite uma pequena superfície de métricas integradas por meio de `System.Diagnostics.Metrics`, usando o nome de meter `PicoLog`.

- `picolog.entries.enqueued`
- `picolog.entries.dropped`
- `picolog.sinks.failures`
- `picolog.writes.rejected_after_shutdown`
- `picolog.queue.entries`
- `picolog.shutdown.drain.duration`

Esses instrumentos são intencionalmente leves e de baixa cardinalidade.

```csharp
using var listener = new MeterListener();

listener.InstrumentPublished = (instrument, meterListener) =>
{
    if (instrument.Meter.Name == PicoLogMetrics.MeterName)
        meterListener.EnableMeasurementEvents(instrument);
};

listener.Start();
```

## Compatibilidade com AOT

O projeto de exemplo publica com Native AOT habilitado.

```xml
<PropertyGroup>
  <PublishAOT>true</PublishAOT>
</PropertyGroup>
```

```bash
dotnet publish -c Release -r win-x64 -p:PublishAOT=true
```

O repositório também inclui um script de validação em nível de publish que publica o exemplo, executa o binário gerado e verifica se as entradas finais de log de desligamento foram descarregadas corretamente:

```powershell
./scripts/Validate-AotSample.ps1
```

## Compilando a partir do código-fonte

### Pré-requisitos

- .NET SDK 10.0 ou posterior
- Git

### Clonar e compilar

```bash
git clone https://github.com/PicoHex/PicoLog.git
cd PicoLog
dotnet restore
dotnet build --configuration Release
```

### Executar os testes

```bash
dotnet test --solution ./PicoLog.slnx --configuration Release
```

## Notas de desempenho

- instâncias de logger são colocadas em cache por categoria dentro de `LoggerFactory`
- `LoggerFactory` possui um canal limitado, um pipeline de categoria e uma tarefa de drain em segundo plano por categoria
- `FlushAsync()` é uma barreira para entradas aceitas antes do snapshot de flush, não um atalho para descarte
- o descarte da factory ainda realiza o drain final antes de descartar os sinks
- `FileSink` agrupa gravações em sua própria fila limitada e expõe flush no nível do sink por meio de `IFlushableLogSink`
- escolher `DropOldest`, `DropWrite` ou `Wait` é um trade-off entre throughput e entrega, não um bug de correção

## Benchmarks

O repositório inclui `benchmarks/PicoLog.Benchmarks`, um projeto de benchmark baseado em PicoBench para comparar os custos de handoff do PicoLog com linhas de base do Microsoft.Extensions.Logging.

- `MicrosoftAsyncHandoff` é a linha de base MEL leve com canal de string.
- `MicrosoftAsyncEntryHandoff` é a linha de base MEL mais justa, com entrada completa, que espelha o custo do envelope de timestamp/categoria/mensagem do PicoLog sem adicionar I/O real.
- nomes de benchmark de modo wait, como `PicoWaitControl_*`, são rótulos internos de cenário de benchmark, não nomes públicos de API.

Execute o projeto de benchmark:

```bash
dotnet run -c Release --project benchmarks/PicoLog.Benchmarks
```

Ou publique e execute o artefato diretamente:

```bash
dotnet publish benchmarks/PicoLog.Benchmarks/PicoLog.Benchmarks.csproj -c Release -r win-x64
benchmarks/PicoLog.Benchmarks/bin/Release/net10.0/win-x64/publish/PicoLog.Benchmarks.exe main
```

## Estendendo o PicoLog

### Sink personalizado

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

Se um sink não implementar `IFlushableLogSink`, a extensão `ILogSink.FlushAsync()` é best effort e é concluída imediatamente.

### Formatter personalizado

```csharp
public sealed class CustomFormatter : ILogFormatter
{
    public string Format(LogEntry entry)
    {
        return $"{entry.Timestamp:yyyy-MM-dd HH:mm:ss} [{entry.Level}] {entry.Message}";
    }
}
```

## Testes

A suíte de testes atualmente cobre:

- gravações do file sink correndo em paralelo com descarte assíncrono
- cache de logger por categoria
- filtragem por nível mínimo
- captura e formatação de payload estruturado
- emissão de métricas integradas
- captura de scope e flush no descarte da factory
- barreiras de flush para entradas aceitas e extensões de flush best effort
- rejeição de gravações após o início do desligamento
- isolamento de falha de sink
- flush de mensagens finais assíncronas
- persistência real da cauda do file sink
- saída de arquivo DI configurada
- resolução de logger tipado no PicoDI

O exemplo também é verificado por meio de publish e execução com Native AOT.

## Adequação e não objetivos

### Pontos fortes

- A implementação central é pequena e fácil de entender: `LoggerFactory` possui registros por categoria, pipelines, tempos de vida das tarefas de drain e tempos de vida dos sinks, enquanto cada `InternalLogger` continua sendo uma fachada de escrita leve e sem posse.
- O projeto é compatível com AOT e evita infraestrutura fortemente dependente de reflexão.
- Propriedades estruturadas e métricas integradas cobrem necessidades operacionais comuns sem forçar um ecossistema de logging maior dentro da aplicação.
- O comportamento sob pressão de fila é explícito, não oculto.
- A semântica de flush continua explícita: `FlushAsync()` é uma barreira para trabalho já aceito, enquanto `DisposeAsync()` continua sendo o caminho de desligamento para drain final e liberação de recursos.
- A integração integrada com PicoDI continua enxuta e previsível.

### Boa opção para

- Aplicações .NET pequenas e médias que querem um núcleo de logging leve sem adotar um ecossistema de logging maior
- Cargas de trabalho de edge, IoT, desktop e utilitários em que custo de inicialização, tamanho do binário e compatibilidade com AOT importam
- Cenários de logging de aplicação em que entrega best effort é aceitável, barreiras de flush durante a execução às vezes são úteis e um drain explícito no desligamento é suficiente
- Equipes que preferem um conjunto pequeno de primitivas e se sentem confortáveis adicionando sinks ou formatters personalizados quando necessário

### Não objetivos e pontos fracos

- Esta não é uma plataforma completa de observabilidade.
- Não é otimizado para categorias de logger com cardinalidade muito alta, porque o design atual cria um pipeline por categoria pertencente à factory e uma tarefa de drain em segundo plano por categoria.
- Não é a escolha padrão para logging de auditoria ou conformidade em que perda silenciosa é inaceitável.
- As métricas integradas são intencionalmente pequenas e não tentam modelar um sistema de telemetria ponta a ponta maior.

## Contribuindo

1. Faça um fork do repositório
2. Crie uma branch de recurso
3. Faça suas alterações
4. Adicione ou atualize testes
5. Envie um pull request

## Licença

Este projeto é licenciado sob a licença MIT. Consulte o arquivo [LICENSE](LICENSE) para mais detalhes.
