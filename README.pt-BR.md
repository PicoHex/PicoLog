# PicoLog

[English](README.md) | [简体中文](README.zh.md) | [繁體中文](README.zh-TW.md) | [Deutsch](README.de.md) | [Español](README.es.md) | [Français](README.fr.md) | [日本語](README.ja.md) | [Português (Brasil)](README.pt-BR.md) | [Русский](README.ru.md)

![CI](https://github.com/PicoHex/PicoLog/actions/workflows/ci.yml/badge.svg)
[![NuGet](https://img.shields.io/nuget/v/PicoLog.svg)](https://www.nuget.org/packages/PicoLog)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

Um framework de logging leve e compatível com AOT para cargas de trabalho .NET em edge e IoT. O repositório contém contratos, a implementação principal do logger, integração com PicoDI, um aplicativo de exemplo e um projeto de benchmark dedicado.

## Recursos

- **Compatibilidade com AOT**: tem como alvo `net10.0` e evita infraestrutura fortemente dependente de reflexão
- **Pipeline assíncrono limitado**: os loggers enfileiram entradas em um canal limitado e gravam em sinks em uma tarefa em segundo plano
- **Companheiros de flush explícitos**: `IFlushableLoggerFactory`, `IFlushableLogSink` e extensões `FlushAsync()` best-effort deixam claras as barreiras de flush durante a execução, sem confundi-las com desligamento
- **Propriedades estruturadas**: cargas opcionais de chave/valor fluem por `LogEntry.Properties` e pela saída do formatador integrado
- **Métricas integradas**: o pacote principal emite métricas de fila, descarte, falha de sink e desligamento por meio de `System.Diagnostics.Metrics`
- **Superfície mínima**: apenas algumas abstrações principais precisam ser implementadas para estender o sistema
- **Integração com PicoDI**: registros integrados para a logger factory e loggers tipados, com logging em console por padrão e logging em arquivo opcional quando um caminho de arquivo é configurado
- **Cobertura de benchmark**: inclui um projeto de benchmark baseado em PicoBench com linhas de base MEL de async handoff leves e mais justas
- **Suporte a escopos**: escopos aninhados fluem por `AsyncLocal` e são anexados a cada `LogEntry`

## Estrutura do projeto

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

## Instalação

### Biblioteca principal de logging

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

await loggerFactory.FlushAsync(); // barreira para as entradas aceitas até aqui, não é desligamento
```

`FlushAsync()` na factory não é descarte. Ele espera que as entradas aceitas antes do snapshot de flush atravessem o pipeline da factory, enquanto `DisposeAsync()` continua sendo o caminho de desligamento para a drenagem final e a liberação de recursos.

### Integração com DI

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

A extensão `FlushAsync()` em `ILoggerFactory` é best-effort. Ela encaminha para `IFlushableLoggerFactory` quando houver suporte e, caso contrário, é concluída imediatamente.

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

`LoggerFactory` é proprietária dos loggers em cache por categoria, de seus pipelines por categoria, das tarefas de drenagem em segundo plano executadas por esses pipelines e dos sinks registrados. `FlushAsync()` é uma barreira em tempo de execução, não descarte. Ele espera que as entradas aceitas antes do snapshot de flush atravessem o pipeline da factory. Use `DisposeAsync()` durante o desligamento para a drenagem final e a liberação de recursos dos sinks. Depois que o descarte começa, novas gravações são rejeitadas enquanto as entradas já enfileiradas continuam sendo drenadas.

```csharp
await using var loggerFactory = new LoggerFactory(sinks);

var logger = new Logger<MyService>(loggerFactory);
logger.Info("Starting up");
await loggerFactory.FlushAsync();

// FlushAsync é uma barreira para as entradas aceitas até aqui.
// DisposeAsync faz a drenagem final e rejeita gravações que concorram com o desligamento.
```

### Pressão de fila

`LoggerFactoryOptions.QueueFullMode` torna explícito o tratamento da pressão de fila tanto para gravações síncronas quanto assíncronas. `LogAsync()` e `LogStructuredAsync()` são concluídos quando o tratamento de handoff no limite do logger termina. A entrada pode ter sido aceita, descartada pela política da fila ou rejeitada durante o desligamento, e a contrapressão no modo `Wait` também é resolvida ali. Isso não significa que um sink tenha terminado uma gravação durável:

- `DropOldest` mantém o logging sem bloqueio ao descartar a entrada enfileirada mais antiga. Esse é o padrão.
- `DropWrite` rejeita a nova entrada e reporta a perda por meio de `OnMessagesDropped`.
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

### Sinks integrados

- `ConsoleSink` grava entradas formatadas de forma simples na saída padrão.
- `ColoredConsoleSink` serializa as mudanças de cor para que o estado do console não vaze entre gravações concorrentes.
- `FileSink` agrupa gravações UTF-8 em arquivo em uma fila em segundo plano antes de descarregá-las no disco e oferece flush em nível de sink por meio de `IFlushableLogSink`. `AddLogging()` cria os sinks configurados dentro da logger factory para que a factory continue sendo a única proprietária de seu ciclo de vida.

### Padrões do PicoDI

`AddLogging()` registra:

- um `ILoggerFactory` singleton
- adaptadores tipados `ILogger<T>`
- adaptadores tipados `IStructuredLogger<T>`
- um sink de console por padrão
- um sink de arquivo opcional quando `FilePath` está configurado

Durante a vida da aplicação, você pode chamar `ILoggerFactory.FlushAsync()` como uma barreira best-effort. Ela encaminha para `IFlushableLoggerFactory` quando estiver disponível e, caso contrário, é concluída imediatamente. Ao desligar, descarte explicitamente a factory resolvida para que as entradas de log enfileiradas sejam drenadas antes do encerramento do processo. Gravações que chegarem depois que o desligamento começar serão rejeitadas em vez de aceitas tardiamente.

Você pode habilitar logging em arquivo pelo parâmetro opcional `filePath`, definindo `options.FilePath` ou definindo `options.File.FilePath` na sobrecarga de configuração. Um caminho de arquivo explícito é tratado como opt-in explícito para o file sink.

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

### Formatador integrado

`ConsoleFormatter` produz linhas legíveis com timestamp, nível, categoria, mensagem, propriedades estruturadas opcionais, texto de exceção e escopos opcionais.

```csharp
var formatter = new ConsoleFormatter();
var sink = new ConsoleSink(formatter);
```

### Logging estruturado

O PicoLog mantém compatibilidade com `ILogger` e expõe logging estruturado garantido por meio de `IStructuredLogger` / `IStructuredLogger<T>`. Quando você usa a `LoggerFactory` integrada, o logger em tempo de execução preserva as cargas de `LogStructured()` / `LogStructuredAsync()` em `LogEntry.Properties`.

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

`ConsoleFormatter` acrescenta propriedades estruturadas após a mensagem em uma forma textual compacta, por exemplo:

```text
[2026-04-05 11:30:42.123] WARNING   [MyService] Cache miss {cacheKey="user:42", node="edge-a", attempt=3}
```

### Extensões de logging

Os métodos de extensão enviados são definidos em `ILogger` e `ILogger<T>`:

- `Trace`, `Debug`, `Info`, `Notice`, `Warning`, `Error`, `Critical`, `Alert`, `Emergency`
- equivalentes assíncronos como `InfoAsync` e `ErrorAsync`
- `LogStructured` e `LogStructuredAsync` como adaptadores best-effort que preservam propriedades quando o logger em tempo de execução implementa `IStructuredLogger`, e caso contrário fazem fallback para logging simples sem carga estruturada
- extensões `FlushAsync()` best-effort em `ILoggerFactory` e `ILogSink` que encaminham quando o tipo em tempo de execução oferece flush e, caso contrário, são concluídas imediatamente

Se você precisa de um contrato estrito de logging estruturado, dependa diretamente de `IStructuredLogger` / `IStructuredLogger<T>`. Se você precisa de um contrato estrito de flush, dependa diretamente de `IFlushableLoggerFactory` ou `IFlushableLogSink`.

### Comportamento em caso de overflow

Tanto o logging síncrono quanto o assíncrono escrevem em um canal limitado.

- Tanto `Log()` síncrono quanto `LogAsync()` assíncrono seguem `LoggerFactoryOptions.QueueFullMode`.
- A conclusão de `LogAsync()` ou `LogStructuredAsync()` significa que o tratamento de handoff no limite do logger terminou ali. A entrada pode ter sido aceita, descartada pela política da fila ou rejeitada durante o desligamento. Isso não significa conclusão durável no sink.
- O padrão é `DropOldest`, que favorece throughput e baixa latência do chamador em vez de entrega garantida.
- `Wait` torna a contrapressão visível para o chamador, bloqueando gravações síncronas até que haja espaço na fila ou `SyncWriteTimeout` expire, e aguardando espaço na fila para gravações assíncronas até que o cancelamento seja solicitado.
- `DropWrite` preserva entradas mais antigas na fila e reporta novas entradas descartadas por meio de `OnMessagesDropped`.
- Depois que o descarte da factory começa, novas gravações são rejeitadas enquanto as entradas já enfileiradas continuam sendo descarregadas.

Para logging geral de aplicações, o comportamento padrão `DropOldest` costuma ser aceitável. Para logging de auditoria, prefira `Wait` ou uma estratégia de sink dedicada.

### Métricas integradas

O pacote principal `PicoLog` emite uma pequena superfície de métricas integradas por meio de `System.Diagnostics.Metrics`, usando o nome de meter `PicoLog`.

- `picolog.entries.enqueued`
- `picolog.entries.dropped`
- `picolog.sinks.failures`
- `picolog.writes.rejected_after_shutdown`
- `picolog.queue.entries`
- `picolog.shutdown.drain.duration`

Esses instrumentos foram projetados para permanecer leves e de baixa cardinalidade. Eles podem ser observados diretamente com `MeterListener` ou conectados a uma infraestrutura de telemetria mais ampla.

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

O projeto de exemplo publica com Native AOT habilitado:

```xml
<PropertyGroup>
  <PublishAOT>true</PublishAOT>
</PropertyGroup>
```

```bash
dotnet publish -c Release -r win-x64 -p:PublishAOT=true
```

O repositório também inclui um script de validação em nível de publicação que publica o exemplo, executa o binário gerado e verifica se as entradas finais de log de desligamento foram descarregadas corretamente:

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

### Executar testes

```bash
dotnet test --solution ./PicoLog.slnx --configuration Release
```

## Considerações de desempenho

- Instâncias de logger são armazenadas em cache por categoria dentro de `LoggerFactory`.
- `LoggerFactory` possui um canal limitado, um pipeline por categoria e uma tarefa de drenagem em segundo plano por categoria.
- `FlushAsync()` na factory é uma barreira para entradas aceitas antes do snapshot de flush, não um atalho para descarte.
- O descarte da factory continua fazendo a drenagem final antes de descartar os sinks.
- `FileSink` agrupa gravações em sua própria fila limitada, descarrega em limites de lote ou de intervalo de flush e expõe flush em nível de sink por meio de `IFlushableLogSink`.
- Escolher `DropOldest`, `DropWrite` ou `Wait` é uma troca entre throughput e entrega, não um bug de correção.

## Benchmarks

O repositório inclui `benchmarks/PicoLog.Benchmarks`, um projeto de benchmark baseado em PicoBench para comparar os custos de handoff do PicoLog com linhas de base do Microsoft.Extensions.Logging.

- `MicrosoftAsyncHandoff` é a linha de base MEL leve baseada em canal de string.
- `MicrosoftAsyncEntryHandoff` é a linha de base MEL mais justa de entrada completa, que espelha o custo do envelope timestamp/category/message do PicoLog sem adicionar I/O real.
- `PicoWaitControl_CachedMessage` e `PicoWaitHandoff_CachedMessage` cobrem a contrapressão do PicoLog no modo wait como comparação relativa.

Execute o projeto de benchmark:

```bash
dotnet run -c Release --project benchmarks/PicoLog.Benchmarks
```

Ou publique e execute o artefato diretamente:

```bash
dotnet publish benchmarks/PicoLog.Benchmarks/PicoLog.Benchmarks.csproj -c Release -r win-x64
benchmarks/PicoLog.Benchmarks/bin/Release/net10.0/win-x64/publish/PicoLog.Benchmarks.exe main
```

O aplicativo de benchmark grava:

- `benchmark-results.md`
- `benchmark-results-main.md`
- `benchmark-results-wait.md`

## Adequação e não objetivos

### Pontos fortes

- A implementação principal é pequena e fácil de entender: `LoggerFactory` possui registros de logger por categoria, pipelines por categoria, tempos de vida de drain task e tempos de vida de sink, enquanto cada `InternalLogger` continua sendo uma fachada de escrita leve e sem propriedade.
- O projeto é AOT-friendly e evita infraestrutura fortemente dependente de reflexão, o que o torna uma boa opção para cargas Native AOT, edge e IoT.
- Propriedades estruturadas e métricas integradas cobrem necessidades operacionais comuns sem forçar um ecossistema de logging maior dentro da aplicação.
- O comportamento sob pressão de fila é explícito em vez de oculto. Chamadores podem escolher entre `DropOldest`, `DropWrite` e `Wait` dependendo se throughput ou entrega importa mais.
- A semântica de flush continua explícita. `FlushAsync()` é uma barreira para trabalho já aceito, enquanto `DisposeAsync()` continua sendo o caminho de desligamento para a drenagem final e a liberação de recursos.
- A integração PicoDI integrada permanece enxuta e previsível em vez de introduzir uma grande pilha de hospedagem ou configuração.

### Bom encaixe

- Aplicações .NET pequenas e médias que querem um núcleo de logging leve sem adotar um ecossistema maior de logging.
- Cargas de trabalho edge, IoT, desktop e utilitárias em que custo de inicialização, tamanho binário e compatibilidade com AOT importam.
- Cenários de logging de aplicação em que entrega best-effort é aceitável, barreiras de flush durante a execução são úteis às vezes e drenagem explícita no desligamento é suficiente.
- Equipes que preferem um pequeno conjunto de primitivas e se sentem confortáveis em adicionar sinks ou formatadores personalizados conforme necessário.

### Não objetivos e pontos fracos

- Esta não é uma plataforma completa de observabilidade. Ela dá suporte a propriedades estruturadas e a uma pequena superfície de métricas integradas, mas não fornece parsing de message template, enrichers, gerenciamento de arquivos rotativos, sinks de transporte remoto ou integração profunda com ecossistemas de telemetria mais amplos.
- Ela não é otimizada para categorias de logger com cardinalidade muito alta. O design atual cria um pipeline por categoria pertencente à factory e uma drain task em segundo plano por categoria.
- Ela não é uma escolha padrão para logging de auditoria ou conformidade, em que perda silenciosa é inaceitável. O modo de fila padrão favorece throughput, a conclusão de `LogAsync()` não é conclusão durável no sink, e garantias de entrega mais fortes exigem configuração explícita, como `Wait` ou uma estratégia de sink dedicada.
- As métricas integradas cobrem profundidade da fila, entradas aceitas e descartadas, falhas de sink, gravações tardias durante o desligamento e duração da drenagem no desligamento. Elas ainda não expõem métricas por categoria, histogramas de latência de sink ou um modelo maior de telemetria ponta a ponta.

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

Se um sink não implementar `IFlushableLogSink`, a extensão `ILogSink.FlushAsync()` continua sendo best-effort e é concluída imediatamente.

### Formatador personalizado

```csharp
public class CustomFormatter : ILogFormatter
{
    public string Format(LogEntry entry)
    {
        return $"{entry.Timestamp:yyyy-MM-dd HH:mm:ss} [{entry.Level}] {entry.Message}";
    }
}
```

## Testes

Atualmente, a suíte de testes cobre:

- gravações do file sink concorrendo com o descarte assíncrono
- cache de logger por categoria
- filtragem por nível mínimo
- captura e formatação de carga estruturada
- emissão de métricas integradas
- captura de scope e flush no descarte da factory
- barreiras de flush para entradas aceitas e extensões de flush best-effort
- rejeição de gravações após o início do desligamento
- isolamento de falhas de sink
- flush assíncrono de mensagens finais
- persistência real da cauda do file sink
- saída de arquivo DI configurada
- resolução de logger tipado do PicoDI

O exemplo também é verificado por publicação e execução com Native AOT.

## Contribuindo

1. Faça um fork do repositório
2. Crie uma branch de recurso
3. Faça suas alterações
4. Adicione ou atualize testes
5. Envie um pull request

## Licença

Este projeto está licenciado sob a MIT License. Consulte o arquivo [LICENSE](LICENSE) para mais detalhes.
