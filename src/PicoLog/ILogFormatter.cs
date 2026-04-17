namespace PicoLog;

public interface ILogFormatter
{
    string Format(LogEntry entry);
}
