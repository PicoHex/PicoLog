namespace Pico.Logging;

public sealed class ConsoleFormatter : ILogFormatter
{
    public string Format(LogEntry entry)
    {
        var level = entry.Level.ToString().ToUpperInvariant();

        var sb = new StringBuilder()
            .Append($"[{entry.Timestamp:yyyy-MM-dd HH:mm:ss.fff}] ")
            .Append(level.PadRight(9))
            .Append(' ')
            .Append($"[{entry.Category}] ")
            .Append(entry.Message);

        if (entry.Exception is not null)
            sb.AppendLine().Append($"EXCEPTION: {entry.Exception}");

        if (!(entry.Scopes?.Count > 0))
            return sb.ToString();

        sb.AppendLine().Append("SCOPES: [").Append(string.Join(" => ", entry.Scopes)).Append(']');

        return sb.ToString();
    }
}
