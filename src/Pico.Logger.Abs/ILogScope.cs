namespace Pico.Logger.Abs;

public interface ILogScope : IDisposable
{
    object State { get; }
}
