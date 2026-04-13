namespace PicoLog;

public sealed class ConsoleFormatter : ILogFormatter
{
    public string Format(LogEntry entry)
    {
        var sb = new StringBuilder(Math.Max(128, (entry.Message?.Length ?? 0) + 64))
            .Append('[')
            .Append(entry.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff", System.Globalization.CultureInfo.InvariantCulture))
            .Append("] ")
            .Append(GetLevelText(entry.Level))
            .Append(' ')
            .Append('[')
            .Append(entry.Category)
            .Append("] ")
            .Append(entry.Message);

        AppendProperties(sb, entry.Properties);

        if (entry.Exception is not null)
            sb.AppendLine().Append("EXCEPTION: ").Append(entry.Exception);

        if (!(entry.Scopes?.Count > 0))
            return sb.ToString();

        sb.AppendLine().Append("SCOPES: [");
        AppendScopes(sb, entry.Scopes);
        sb.Append(']');

        return sb.ToString();
    }

    private static string GetLevelText(LogLevel level) =>
        level switch
        {
            LogLevel.Trace => "TRACE    ",
            LogLevel.Debug => "DEBUG    ",
            LogLevel.Info => "INFO     ",
            LogLevel.Notice => "NOTICE   ",
            LogLevel.Warning => "WARNING  ",
            LogLevel.Error => "ERROR    ",
            LogLevel.Critical => "CRITICAL ",
            LogLevel.Alert => "ALERT    ",
            LogLevel.Emergency => "EMERGENCY",
            _ => "NONE     "
        };

    private static void AppendScopes(StringBuilder builder, IReadOnlyList<object> scopes)
    {
        for (var index = 0; index < scopes.Count; index++)
        {
            if (index > 0)
                builder.Append(" => ");

            builder.Append(scopes[index]);
        }
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
            AppendEscapedCharacter(builder, character);
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
            AppendEscapedCharacter(builder, character);
    }

    private static void AppendEscapedCharacter(StringBuilder builder, char character)
    {
        switch (character)
        {
            case '\\':
                builder.Append("\\\\");
                break;
            case '"':
                builder.Append("\\\"");
                break;
            case '\r':
                builder.Append("\\r");
                break;
            case '\n':
                builder.Append("\\n");
                break;
            case '\t':
                builder.Append("\\t");
                break;
            default:
                builder.Append(character);
                break;
        }
    }
}
