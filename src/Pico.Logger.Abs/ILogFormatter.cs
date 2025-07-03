namespace Pico.Logger.Abs;

public interface ILogFormatter
{
    string Format(LogEntry entry);
}
