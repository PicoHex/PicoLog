namespace PicoLog.Abs;

/// <summary>
/// PicoLog-specific lifecycle companion for flushing accepted entries and coordinating shutdown.
/// </summary>
/// <remarks>
/// This interface exists so DI consumers can depend on an explicit control plane without pulling
/// sink/extensibility contracts or relying on narrower legacy factory companions.
/// </remarks>
public interface IPicoLogControl : IAsyncDisposable
{
    ValueTask FlushAsync(CancellationToken cancellationToken = default);
}
