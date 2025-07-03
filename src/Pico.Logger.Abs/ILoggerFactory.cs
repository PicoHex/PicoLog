namespace Pico.Logger.Abs;

public interface ILoggerFactory
{
    ILogger CreateLogger(string categoryName);
}
