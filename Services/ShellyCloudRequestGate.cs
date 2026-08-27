namespace VigdalsMorningsguide.Services;

public sealed class ShellyCloudRequestGate
{
    private static readonly TimeSpan MinimumRequestSpacing =
        TimeSpan.FromMilliseconds(1100);

    private readonly SemaphoreSlim _semaphore =
        new(
            1,
            1);

    private DateTimeOffset _lastRequestStartedAt =
        DateTimeOffset.MinValue;

    public async ValueTask<IAsyncDisposable> EnterAsync(
        CancellationToken cancellationToken)
    {
        await _semaphore.WaitAsync(
            cancellationToken);

        try
        {
            var earliestNextRequest =
                _lastRequestStartedAt +
                MinimumRequestSpacing;

            var delay =
                earliestNextRequest -
                DateTimeOffset.UtcNow;

            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(
                    delay,
                    cancellationToken);
            }

            _lastRequestStartedAt =
                DateTimeOffset.UtcNow;

            return new Lease(
                _semaphore);
        }
        catch
        {
            _semaphore.Release();
            throw;
        }
    }

    private sealed class Lease : IAsyncDisposable
    {
        private SemaphoreSlim? _semaphore;

        public Lease(
            SemaphoreSlim semaphore)
        {
            _semaphore =
                semaphore;
        }

        public ValueTask DisposeAsync()
        {
            var semaphore =
                Interlocked.Exchange(
                    ref _semaphore,
                    null);

            semaphore?.Release();

            return ValueTask.CompletedTask;
        }
    }
}
