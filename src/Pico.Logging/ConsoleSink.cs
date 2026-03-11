namespace Pico.Logging;

public sealed class ConsoleSink : ILogSink
{
    private readonly ILogFormatter _formatter;
    private readonly TextWriter _writer;

    public ConsoleSink(ILogFormatter formatter, TextWriter? writer = null)
    {
        _formatter = formatter ?? throw new ArgumentNullException(nameof(formatter));
        _writer = writer ?? Console.Out;
    }

    public ValueTask WriteAsync(LogEntry entry, CancellationToken cancellationToken = default)
    {
        WriteColoredLog(entry.Level, _formatter.Format(entry));
        return ValueTask.CompletedTask;
    }

    private void WriteColoredLog(LogLevel level, string message)
    {
        var originalColor = Console.ForegroundColor;

        Console.ForegroundColor = level switch
        {
            LogLevel.Trace => ConsoleColor.Gray,
            LogLevel.Debug => ConsoleColor.Cyan,
            LogLevel.Info => ConsoleColor.Green,
            LogLevel.Notice => ConsoleColor.Blue,
            LogLevel.Warning => ConsoleColor.Yellow,
            LogLevel.Error => ConsoleColor.Red,
            LogLevel.Critical => ConsoleColor.DarkRed,
            LogLevel.Alert => ConsoleColor.Magenta,
            LogLevel.Emergency => ConsoleColor.DarkMagenta,
            _ => originalColor
        };

        _writer.WriteLine(message);
        Console.ForegroundColor = originalColor;
    }

    public void Dispose() { }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
