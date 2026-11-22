using System.Threading.Channels;

namespace Overviewarr.Search;

/// <summary>Runs queued work in order.</summary>
public interface ISearchQueue
{
    void Enqueue(Func<CancellationToken, Task> work);
}

/// <summary>Keeps waiting searches in order in a <see cref="Channel"/> and runs them on 2 workers.</summary>
public sealed class SearchQueue(ILogger<SearchQueue> logger) : BackgroundService, ISearchQueue
{
    public const int Workers = 2;

    private readonly Channel<Func<CancellationToken, Task>> _channel =
        Channel.CreateUnbounded<Func<CancellationToken, Task>>(new UnboundedChannelOptions { SingleReader = false, SingleWriter = false });

    public void Enqueue(Func<CancellationToken, Task> work) => _channel.Writer.TryWrite(work);

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.WhenAll(Enumerable.Range(0, Workers).Select(_ => RunAsync(stoppingToken)));

    private async Task RunAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var work in _channel.Reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    await work(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception exception)
                {
                    logger.LogError(exception, "A search failed unexpectedly");
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }
}
