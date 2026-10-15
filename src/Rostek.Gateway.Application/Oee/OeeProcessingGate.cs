namespace Rostek.Gateway.Application.Oee;

// Serializes context/metric changes, never network dispatch. Shared also covers manually composed services.
public sealed class OeeProcessingGate
{
    public static OeeProcessingGate Shared { get; } = new();
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public async Task<IDisposable> EnterAsync(CancellationToken cancellationToken)
    {
        await _semaphore.WaitAsync(cancellationToken);
        return new Lease(_semaphore);
    }

    private sealed class Lease(SemaphoreSlim semaphore) : IDisposable
    {
        public void Dispose() => semaphore.Release();
    }
}
