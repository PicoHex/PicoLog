namespace PicoLog;

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

        AppendProperties(sb, entry.Properties);

        if (entry.Exception is not null)
            sb.AppendLine().Append($"EXCEPTION: {entry.Exception}");

        if (!(entry.Scopes?.Count > 0))
            return sb.ToString();

        sb.AppendLine().Append("SCOPES: [").Append(string.Join(" => ", entry.Scopes)).Append(']');

        return sb.ToString();
    }

    private static void AppendProperties(
        StringBuilder builder,
        IReadOnlyList<KeyValuePair<string, object?>>? properties
    )
    {
        if (properties is not { Count: > 0 })
            return;

        builder.Append(" {");

        for (var index = 0; index < properties.Count; index++)
        {
            if (index > 0)
                builder.Append(", ");

            var property = properties[index];
            builder.Append(property.Key).Append('=');
            AppendPropertyValue(builder, property.Value);
        }

        builder.Append('}');
    }

    private static void AppendPropertyValue(StringBuilder builder, object? value)
    {
        if (value is null)
        {
            builder.Append("null");
            return;
        }

        if (value is string text)
        {
            builder.Append('"');
            AppendEscapedString(builder, text);
            builder.Append('"');
            return;
        }

        if (value is char character)
        {
            builder.Append('"');
            AppendEscapedString(builder, character.ToString());
            builder.Append('"');
            return;
        }

        if (value is IFormattable formattable)
        {
            builder.Append(formattable.ToString(null, System.Globalization.CultureInfo.InvariantCulture));
            return;
        }

        builder.Append(value);
    }

    private static void AppendEscapedString(StringBuilder builder, string value)
    {
        foreach (var character in value)
        {
            builder.Append(character switch
            {
                '\\' => "\\\\",
                '"' => "\\\"",
                '\r' => "\\r",
                '\n' => "\\n",
                '\t' => "\\t",
                _ => character.ToString()
            });
        }
    }
}
