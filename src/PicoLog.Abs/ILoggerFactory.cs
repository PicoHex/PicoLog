namespace PicoLog.Abs;

public interface ILoggerFactory : IAsyncDisposable
{
    ILogger CreateLogger(string categoryName);
}
