using System.Runtime.CompilerServices;

namespace PicoLog;

internal static class ConsoleWriteCoordinator
{
    private static readonly ConditionalWeakTable<TextWriter, object> WriterLocks = new();

    public static object OutputLock { get; } = new();

    public static object GetWriteLock(TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);

        return ReferenceEquals(writer, Console.Out)
            ? OutputLock
            : WriterLocks.GetValue(writer, static _ => new object());
    }
}
