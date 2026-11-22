namespace Overviewarr.Search;

/// <summary>Once an hour, removes cache entries whose last page closed more than 24 hours ago.</summary>
public sealed class CacheEvictionService(SearchCache cache, TimeProvider time) : BackgroundService
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(24);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1), time);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                cache.RemoveExpired(Lifetime);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }
}
