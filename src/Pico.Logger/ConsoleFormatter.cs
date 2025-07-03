namespace Pico.Logger;

public sealed class ConsoleFormatter : ILogFormatter
{
    public string Format(LogEntry entry)
    {
        var sb = new StringBuilder()
            .Append($"[{entry.Timestamp:yyyy-MM-dd HH:mm:ss.fff}] ")
            .Append($"{entry.Level.ToString().ToUpper(), -8}")
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
