namespace PicoLog;

internal static class ConsoleSinkWriter
{
    public static Task WriteAsync(TextWriter writer, string message)
    {
        lock (ConsoleWriteCoordinator.GetWriteLock(writer))
            writer.WriteLine(message);

        return Task.CompletedTask;
    }

    public static Task WriteAsync<TState>(
        TextWriter writer,
        string message,
        TState state,
        Action<TextWriter, string, TState> consoleWrite
    )
    {
        lock (ConsoleWriteCoordinator.GetWriteLock(writer))
        {
            if (!ReferenceEquals(writer, Console.Out))
            {
                writer.WriteLine(message);
                return Task.CompletedTask;
            }

            consoleWrite(writer, message, state);
        }

        return Task.CompletedTask;
    }
}
