namespace Pico.Logging;

public sealed class ConsoleSink(ILogFormatter formatter, TextWriter? writer = null) : ILogSink
{
    private readonly ILogFormatter _formatter = formatter ?? throw new ArgumentNullException(nameof(formatter));
    private readonly TextWriter _writer = writer ?? Console.Out;

    public Task WriteAsync(LogEntry entry, CancellationToken cancellationToken = default)
    {
        _writer.WriteLine(_formatter.Format(entry));
        return Task.CompletedTask;
    }

    public void Dispose() { }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
