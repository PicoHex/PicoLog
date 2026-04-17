namespace PicoLog.DI;

internal sealed class OwnedLoggerFactory(LoggerFactory innerFactory, IAsyncDisposable ownedScope)
    : ILoggerFactory, IPicoLogControl, IFlushableLoggerFactory, IDisposable, IAsyncDisposable
{
    private int _disposeState;

    public ILogger CreateLogger(string categoryName) => innerFactory.CreateLogger(categoryName);

    public ValueTask FlushAsync(CancellationToken cancellationToken = default) =>
        innerFactory.FlushAsync(cancellationToken);

    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0)
            return;

        Exception? factoryException = null;

        try
        {
            await innerFactory.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            factoryException = ex;
        }

        try
        {
            await ownedScope.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception ex) when (factoryException is null)
        {
            factoryException = ex;
        }

        if (factoryException is not null)
            throw factoryException;
    }
}
