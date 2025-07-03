namespace Pico.Logger;

public sealed class ConsoleSink(ILogFormatter formatter) : ILogSink
{
    public ValueTask WriteAsync(LogEntry entry, CancellationToken cancellationToken = default)
    {
        WriteColoredLog(entry.Level, formatter.Format(entry));
        return ValueTask.CompletedTask;
    }

    private static void WriteColoredLog(LogLevel level, string message)
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

        Console.WriteLine(message);
        Console.ForegroundColor = originalColor;
    }

    public void Dispose() { }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
