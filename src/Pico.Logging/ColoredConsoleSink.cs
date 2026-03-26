namespace Pico.Logging;

public sealed class ColoredConsoleSink(ILogFormatter formatter, TextWriter? writer = null) : ILogSink
{
    private static readonly Lock ConsoleLock = new();
    private readonly ILogFormatter _formatter = formatter ?? throw new ArgumentNullException(nameof(formatter));
    private readonly TextWriter _writer = writer ?? Console.Out;

    public Task WriteAsync(LogEntry entry, CancellationToken cancellationToken = default)
    {
        var message = _formatter.Format(entry);

        lock (ConsoleLock)
        {
            if (!ReferenceEquals(_writer, Console.Out))
            {
                _writer.WriteLine(message);
                return Task.CompletedTask;
            }
        }

        lock (ConsoleLock)
        {
            var originalColor = Console.ForegroundColor;

            try
            {
                Console.ForegroundColor = entry.Level switch
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
            }
            finally
            {
                Console.ForegroundColor = originalColor;
            }
        }

        return Task.CompletedTask;
    }

    public void Dispose() { }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
