using System.Collections.Concurrent;
using Overviewarr.Releases;
using Overviewarr.Settings;
using Overviewarr.Sonarr;

namespace Overviewarr.Search;

/// <summary>
/// Search results per series, independent of any page (DESIGN.md sections 6 and 11). Every change builds a new
/// immutable <see cref="SeriesSnapshot"/> and raises <see cref="SeriesChanged"/>.
/// </summary>
public sealed class SearchCache : IDisposable
{
    private readonly ConcurrentDictionary<int, Entry> _entries = new();
    private readonly ISearchQueue _queue;
    private readonly IReleaseSearcher _searcher;
    private readonly TimeProvider _time;
    private readonly SettingsStore _settings;
    private readonly ILogger<SearchCache> _logger;

    public SearchCache(ISearchQueue queue, IReleaseSearcher searcher, TimeProvider time, SettingsStore settings, ILogger<SearchCache> logger)
    {
        _queue = queue;
        _searcher = searcher;
        _time = time;
        _settings = settings;
        _logger = logger;
        _settings.Changed += OnSettingsChanged;
    }

    /// <summary>Raised with the series ID after every change. Raised on background threads.</summary>
    public event Action<int>? SeriesChanged;

    public SeriesSnapshot GetSnapshot(int seriesId) =>
        _entries.TryGetValue(seriesId, out var entry) ? entry.Snapshot : SeriesSnapshot.Empty(seriesId);

    /// <summary>Marks a page as showing the series until the returned handle is disposed.</summary>
    public IDisposable Open(int seriesId)
    {
        while (true)
        {
            var entry = GetOrAdd(seriesId);
            lock (entry.Sync)
            {
                if (entry.Removed)
                {
                    continue;
                }

                entry.OpenPages++;
                return new PageHandle(this, entry);
            }
        }
    }

    /// <summary>
    /// Queues a search. Asking for a search that is already waiting or running joins it. The task completes with
    /// the search's final status, Done or Failed.
    /// </summary>
    public Task<SearchStatus> RequestSearch(SearchKey key)
    {
        while (true)
        {
            var entry = GetOrAdd(key.SeriesId);
            int generation;
            TaskCompletionSource<SearchStatus> completion;

            lock (entry.Sync)
            {
                if (entry.Removed)
                {
                    continue;
                }

                var current = entry.Statuses.GetValueOrDefault(key);
                if (current?.IsActive == true && entry.Pending.TryGetValue(key, out var pending))
                {
                    return pending.Task;
                }

                completion = new TaskCompletionSource<SearchStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
                entry.Pending[key] = completion;
                entry.Statuses[key] = new SearchStatus(key, SearchState.Waiting, current?.FetchedAt, current?.Results ?? []);
                generation = entry.Generation;
                Publish(entry, resultsChanged: false);
            }

            RaiseChanged(key.SeriesId);
            _queue.Enqueue(cancellationToken => RunAsync(entry, key, generation, cancellationToken));
            return completion.Task;
        }
    }

    /// <summary>Clears every series' searches and results. Searches still running are ignored when they finish.</summary>
    public void Clear()
    {
        foreach (var entry in _entries.Values)
        {
            List<(SearchKey Key, TaskCompletionSource<SearchStatus> Completion)> pending;

            lock (entry.Sync)
            {
                entry.Generation++;
                entry.Statuses.Clear();
                pending = entry.Pending.Select(p => (p.Key, p.Value)).ToList();
                entry.Pending.Clear();
                Publish(entry, resultsChanged: true);
            }

            foreach (var (key, completion) in pending)
            {
                completion.TrySetResult(new SearchStatus(key, SearchState.Failed, null, [], "The Sonarr connection changed."));
            }

            RaiseChanged(entry.SeriesId);
        }
    }

    /// <summary>Removes entries whose last page closed at least <paramref name="lifetime"/> ago.</summary>
    public int RemoveExpired(TimeSpan lifetime)
    {
        var now = _time.GetUtcNow();
        var removed = 0;

        foreach (var entry in _entries.Values)
        {
            lock (entry.Sync)
            {
                if (entry.OpenPages == 0 && now - entry.LastClosed >= lifetime && !entry.Statuses.Values.Any(s => s.IsActive))
                {
                    entry.Removed = true;
                    _entries.TryRemove(new KeyValuePair<int, Entry>(entry.SeriesId, entry));
                    removed++;
                }
            }
        }

        return removed;
    }

    public void Dispose() => _settings.Changed -= OnSettingsChanged;

    private async Task RunAsync(Entry entry, SearchKey key, int generation, CancellationToken cancellationToken)
    {
        lock (entry.Sync)
        {
            if (entry.Generation != generation || entry.Statuses.GetValueOrDefault(key) is not { State: SearchState.Waiting } waiting)
            {
                return;
            }

            entry.Statuses[key] = waiting with { State = SearchState.Running };
            Publish(entry, resultsChanged: false);
        }

        RaiseChanged(key.SeriesId);

        IReadOnlyList<ReleaseRecord>? results = null;
        string? error = null;
        var unauthorized = false;

        try
        {
            results = await _searcher.SearchAsync(key, cancellationToken);
        }
        catch (SonarrException exception)
        {
            error = exception.Message;
            unauthorized = exception.IsUnauthorized;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            error = "Overviewarr is shutting down.";
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Search {Key} failed", key);
            error = exception.Message;
        }

        SearchStatus final;
        TaskCompletionSource<SearchStatus>? completion;

        lock (entry.Sync)
        {
            if (entry.Generation != generation)
            {
                return;
            }

            var previous = entry.Statuses.GetValueOrDefault(key);
            final = results is not null
                ? new SearchStatus(key, SearchState.Done, _time.GetUtcNow(), results)
                : new SearchStatus(key, SearchState.Failed, previous?.FetchedAt, previous?.Results ?? [], error, unauthorized);
            entry.Statuses[key] = final;
            entry.Pending.Remove(key, out completion);
            Publish(entry, resultsChanged: results is not null);
        }

        completion?.TrySetResult(final);
        RaiseChanged(key.SeriesId);
    }

    private Entry GetOrAdd(int seriesId) => _entries.GetOrAdd(seriesId, id => new Entry(id, _time.GetUtcNow()));

    private static void Publish(Entry entry, bool resultsChanged)
    {
        var statuses = new Dictionary<SearchKey, SearchStatus>(entry.Statuses);
        var releases = resultsChanged ? ReleaseMerger.Merge(statuses.Values) : entry.Snapshot.Releases;
        entry.Snapshot = new SeriesSnapshot(entry.SeriesId, statuses, releases);
    }

    private void RaiseChanged(int seriesId)
    {
        try
        {
            SeriesChanged?.Invoke(seriesId);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "A SeriesChanged handler failed");
        }
    }

    private void OnSettingsChanged(SettingsChange change)
    {
        if (change.UrlChanged)
        {
            Clear();
        }
    }

    private void Close(Entry entry)
    {
        lock (entry.Sync)
        {
            entry.OpenPages--;
            if (entry.OpenPages == 0)
            {
                entry.LastClosed = _time.GetUtcNow();
            }
        }
    }

    private sealed class Entry(int seriesId, DateTimeOffset created)
    {
        public object Sync { get; } = new();

        public int SeriesId { get; } = seriesId;

        public Dictionary<SearchKey, SearchStatus> Statuses { get; } = [];

        /// <summary>The completions of waiting and running searches.</summary>
        public Dictionary<SearchKey, TaskCompletionSource<SearchStatus>> Pending { get; } = [];

        public SeriesSnapshot Snapshot { get; set; } = SeriesSnapshot.Empty(seriesId);

        public int Generation { get; set; }

        public int OpenPages { get; set; }

        public DateTimeOffset LastClosed { get; set; } = created;

        public bool Removed { get; set; }
    }

    private sealed class PageHandle(SearchCache cache, Entry entry) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                cache.Close(entry);
            }
        }
    }
}
