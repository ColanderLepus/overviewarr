using Microsoft.Extensions.Time.Testing;
using Overviewarr.Releases;
using Overviewarr.Search;
using Overviewarr.Settings;
using Overviewarr.Sonarr;

namespace Overviewarr.Tests.Search;

public sealed class SearchCacheTests : IDisposable
{
    private const int SeriesId = 1;

    private static readonly SearchKey Season1 = SearchKey.Season(SeriesId, 1);
    private static readonly SearchKey Season2 = SearchKey.Season(SeriesId, 2);
    private static readonly SearchKey Episode5 = SearchKey.Episode(SeriesId, 1, 1005);

    private readonly TempDirectory _directory = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 22, 12, 0, 0, TimeSpan.Zero));
    private readonly ManualQueue _queue = new();
    private readonly Dictionary<SearchKey, Func<IReadOnlyList<ReleaseRecord>>> _results = [];
    private readonly SettingsStore _settings;
    private readonly SearchCache _cache;
    private readonly List<int> _changes = [];

    public SearchCacheTests()
    {
        _settings = new SettingsStore(new AppPaths(_directory.Path, _directory.Path), new ListLogger<SettingsStore>());
        _cache = new SearchCache(_queue, new FakeSearcher(key => _results[key]()), _time, _settings, new ListLogger<SearchCache>());
        _cache.SeriesChanged += _changes.Add;
    }

    public void Dispose()
    {
        _cache.Dispose();
        _directory.Dispose();
    }

    private static ReleaseRecord Release(string guid, SearchKey search, string title = "Show") =>
        TestReleases.Make(guid, title: title, search: search, episodes: [(search.SeasonNumber, 1)]);

    private async Task<SearchStatus> SearchAsync(SearchKey key, params ReleaseRecord[] releases)
    {
        _results[key] = () => releases;
        var search = _cache.RequestSearch(key);
        await _queue.RunAllAsync();
        return await search;
    }

    private SeriesSnapshot Snapshot => _cache.GetSnapshot(SeriesId);

    [Fact]
    public async Task Search_moves_from_waiting_through_running_to_done()
    {
        _results[Season1] = () =>
        {
            Assert.Equal(SearchState.Running, Snapshot.Searches[Season1].State);
            return [Release("a", Season1)];
        };

        var search = _cache.RequestSearch(Season1);
        Assert.Equal(SearchState.Waiting, Snapshot.Searches[Season1].State);
        Assert.False(search.IsCompleted);

        await _queue.RunAllAsync();

        var status = Snapshot.Searches[Season1];
        Assert.Same(status, await search);
        Assert.Equal(SearchState.Done, status.State);
        Assert.Equal(_time.GetUtcNow(), status.FetchedAt);
        Assert.Equal(["a"], Snapshot.Releases.Select(r => r.Guid));
        Assert.All(_changes, id => Assert.Equal(SeriesId, id));
        Assert.Equal(3, _changes.Count);
    }

    [Fact]
    public async Task Asking_for_a_waiting_search_joins_it()
    {
        _results[Season1] = () => [Release("a", Season1)];

        var first = _cache.RequestSearch(Season1);
        var second = _cache.RequestSearch(Season1);

        Assert.Single(_queue.Work);
        Assert.Same(first, second);

        await _queue.RunAllAsync();
        Assert.Equal(SearchState.Done, (await second).State);
    }

    [Fact]
    public async Task Releases_are_merged_by_guid_and_indexer_and_the_newest_copy_wins()
    {
        await SearchAsync(Season1, Release("a", Season1, "old copy"), Release("b", Season1));
        _time.Advance(TimeSpan.FromMinutes(1));
        await SearchAsync(Episode5, Release("a", Episode5, "new copy"));

        var merged = Snapshot.Releases.ToDictionary(r => r.Guid);
        Assert.Equal(2, merged.Count);
        Assert.Equal("new copy", merged["a"].Title);
        Assert.Equal([Season1, Episode5], merged["a"].Searches);
        Assert.Equal([Season1], merged["b"].Searches);
    }

    [Fact]
    public async Task Same_guid_from_another_indexer_is_another_release()
    {
        await SearchAsync(Season1, TestReleases.Make("a", indexerId: 1, search: Season1), TestReleases.Make("a", indexerId: 2, search: Season1));

        Assert.Equal(2, Snapshot.Releases.Count);
    }

    [Fact]
    public async Task Running_a_search_again_replaces_only_its_own_results()
    {
        await SearchAsync(Season1, Release("a", Season1), Release("b", Season1));
        await SearchAsync(Episode5, Release("c", Episode5));
        await SearchAsync(Season1, Release("a", Season1));

        Assert.Equal(["a", "c"], Snapshot.Releases.Select(r => r.Guid).Order());
    }

    [Fact]
    public async Task Failed_search_keeps_its_results()
    {
        await SearchAsync(Season1, Release("a", Season1));
        _results[Season1] = () => throw new SonarrException("No indexers available");

        var search = _cache.RequestSearch(Season1);
        await _queue.RunAllAsync();

        var status = Snapshot.Searches[Season1];
        Assert.Equal(SearchState.Failed, status.State);
        Assert.Equal("No indexers available", status.Error);
        Assert.Equal(["a"], Snapshot.Releases.Select(r => r.Guid));
        Assert.Same(status, await search);
    }

    [Fact]
    public async Task Unauthorized_failure_is_marked()
    {
        _results[Season1] = () => throw new SonarrException("Sonarr rejected the API key.", isUnauthorized: true);

        _ = _cache.RequestSearch(Season1);
        await _queue.RunAllAsync();

        Assert.True(Snapshot.Searches[Season1].Unauthorized);
    }

    [Fact]
    public async Task Season_of_a_release_without_matched_episodes_comes_from_its_searches()
    {
        await SearchAsync(Season2, TestReleases.Make("a", search: Season2, episodes: []));

        Assert.Equal([2], Snapshot.Releases.Single().Seasons.Select(s => s.Number));
    }

    [Fact]
    public async Task Saving_a_different_url_clears_the_cache_and_ignores_running_searches()
    {
        await _settings.SaveAsync(new SonarrConnection("http://sonarr:8989", "key"));
        await SearchAsync(Season1, Release("a", Season1));
        _results[Season2] = () => [Release("b", Season2)];
        var search = _cache.RequestSearch(Season2);

        await _settings.SaveAsync(new SonarrConnection("http://other:8989", "key"));
        await _queue.RunAllAsync();

        Assert.Empty(Snapshot.Releases);
        Assert.Empty(Snapshot.Searches);
        Assert.Equal(SearchState.Failed, (await search).State);
    }

    [Fact]
    public async Task Saving_the_same_url_keeps_the_cache()
    {
        await _settings.SaveAsync(new SonarrConnection("http://sonarr:8989", "key"));
        await SearchAsync(Season1, Release("a", Season1));

        await _settings.SaveAsync(new SonarrConnection("http://sonarr:8989", "new-key"));

        Assert.Single(Snapshot.Releases);
    }

    [Fact]
    public async Task Entry_is_removed_24_hours_after_the_last_page_closed()
    {
        var page = _cache.Open(SeriesId);
        await SearchAsync(Season1, Release("a", Season1));

        _time.Advance(TimeSpan.FromHours(30));
        Assert.Equal(0, _cache.RemoveExpired(TimeSpan.FromHours(24)));

        page.Dispose();
        _time.Advance(TimeSpan.FromHours(23));
        Assert.Equal(0, _cache.RemoveExpired(TimeSpan.FromHours(24)));

        _time.Advance(TimeSpan.FromHours(1));
        Assert.Equal(1, _cache.RemoveExpired(TimeSpan.FromHours(24)));
        Assert.Empty(Snapshot.Releases);
    }

    private sealed class ManualQueue : ISearchQueue
    {
        public Queue<Func<CancellationToken, Task>> Work { get; } = new();

        public void Enqueue(Func<CancellationToken, Task> work) => Work.Enqueue(work);

        public async Task RunAllAsync()
        {
            while (Work.TryDequeue(out var work))
            {
                await work(CancellationToken.None);
            }
        }
    }

    private sealed class FakeSearcher(Func<SearchKey, IReadOnlyList<ReleaseRecord>> respond) : IReleaseSearcher
    {
        public Task<IReadOnlyList<ReleaseRecord>> SearchAsync(SearchKey key, CancellationToken cancellationToken) =>
            Task.FromResult(respond(key));
    }
}
