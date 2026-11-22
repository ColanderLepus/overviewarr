using Overviewarr.Grabbing;
using Overviewarr.Releases;
using Overviewarr.Search;
using Overviewarr.Sonarr;

namespace Overviewarr.Tests.Grabbing;

public class GrabSenderTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 22, 12, 0, 0, TimeSpan.Zero);
    private static readonly SearchKey Season1 = SearchKey.Season(1, 1);
    private static readonly SearchKey Season2 = SearchKey.Season(1, 2);

    private readonly List<ReleaseRecord> _grabbed = [];
    private readonly List<SearchKey> _searchedAgain = [];
    private readonly Dictionary<SearchKey, SearchStatus> _newResults = [];
    private readonly Dictionary<string, string> _refusals = [];

    private GrabSender CreateSender() => new(
        key =>
        {
            _searchedAgain.Add(key);
            return Task.FromResult(_newResults[key]);
        },
        release =>
        {
            if (_refusals.TryGetValue(release.Guid, out var message))
            {
                throw new SonarrException(message);
            }

            _grabbed.Add(release);
            return Task.CompletedTask;
        });

    private static ReleaseRecord Release(string guid, SearchKey search, string title = "old") =>
        TestReleases.Make(guid, title: title) with { Searches = [search] };

    private static SeriesSnapshot Snapshot(params (SearchKey Key, int MinutesAgo)[] searches) =>
        new(1, searches.ToDictionary(s => s.Key, s => new SearchStatus(s.Key, SearchState.Done, Now.AddMinutes(-s.MinutesAgo), [])), []);

    [Fact]
    public async Task Fresh_releases_are_sent_one_at_a_time_in_order()
    {
        ReleaseRecord[] selected = [Release("a", Season1), Release("b", Season1)];

        var results = await CreateSender().SendAsync(selected, Snapshot((Season1, 5)), Now);

        Assert.Equal(["a", "b"], _grabbed.Select(r => r.Guid));
        Assert.All(results, r => Assert.Equal(GrabOutcome.Sent, r.Outcome));
        Assert.Empty(_searchedAgain);
    }

    [Fact]
    public async Task Stale_release_is_sent_as_found_in_the_new_search_results()
    {
        var stale = Release("a", Season1, "old");
        _newResults[Season1] = new SearchStatus(Season1, SearchState.Done, Now, [Release("a", Season1, "new")]);

        var results = await CreateSender().SendAsync([stale], Snapshot((Season1, 40)), Now);

        Assert.Equal([Season1], _searchedAgain);
        Assert.Equal("new", Assert.Single(_grabbed).Title);
        Assert.Equal(GrabOutcome.Sent, Assert.Single(results).Outcome);
    }

    [Fact]
    public async Task Stale_release_missing_from_the_new_results_is_not_found()
    {
        _newResults[Season1] = new SearchStatus(Season1, SearchState.Done, Now, [Release("other", Season1)]);

        var results = await CreateSender().SendAsync([Release("a", Season1)], Snapshot((Season1, 40)), Now);

        Assert.Empty(_grabbed);
        var result = Assert.Single(results);
        Assert.Equal(GrabOutcome.NotFound, result.Outcome);
        Assert.Null(result.Message);
    }

    [Fact]
    public async Task Failed_search_makes_its_releases_not_found_with_its_message()
    {
        _newResults[Season1] = new SearchStatus(Season1, SearchState.Failed, Now.AddMinutes(-40), [], "No indexers available");

        var result = Assert.Single(await CreateSender().SendAsync([Release("a", Season1)], Snapshot((Season1, 40)), Now));

        Assert.Equal(GrabOutcome.NotFound, result.Outcome);
        Assert.Equal("No indexers available", result.Message);
    }

    [Fact]
    public async Task Refused_release_is_skipped_and_the_rest_are_still_sent()
    {
        _refusals["a"] = "Couldn't find requested release in cache, try searching again";
        ReleaseRecord[] selected = [Release("a", Season1), Release("b", Season1)];

        var results = await CreateSender().SendAsync(selected, Snapshot((Season1, 5)), Now);

        Assert.Equal(["b"], _grabbed.Select(r => r.Guid));
        Assert.Equal(GrabOutcome.Refused, results[0].Outcome);
        Assert.Equal("Couldn't find requested release in cache, try searching again", results[0].Message);
        Assert.Equal(GrabOutcome.Sent, results[1].Outcome);
    }

    [Fact]
    public async Task Each_stale_search_runs_once_per_send()
    {
        _newResults[Season1] = new SearchStatus(Season1, SearchState.Done, Now, [Release("a", Season1), Release("b", Season1)]);
        ReleaseRecord[] selected = [Release("a", Season1), Release("b", Season1), Release("c", Season2)];

        var results = await CreateSender().SendAsync(selected, Snapshot((Season1, 40), (Season2, 5)), Now);

        Assert.Equal([Season1], _searchedAgain);
        Assert.Equal(["a", "b", "c"], results.Select(r => r.Release.Guid));
        Assert.All(results, r => Assert.Equal(GrabOutcome.Sent, r.Outcome));
    }

    [Fact]
    public async Task Progress_is_reported()
    {
        var reports = new List<string>();
        _newResults[Season1] = new SearchStatus(Season1, SearchState.Done, Now, [Release("a", Season1)]);

        await CreateSender().SendAsync([Release("a", Season1)], Snapshot((Season1, 40)), Now, new SynchronousProgress(reports.Add));

        Assert.Equal(["Searching again…", "Sending 1 of 1…"], reports);
    }

    private sealed class SynchronousProgress(Action<string> report) : IProgress<string>
    {
        public void Report(string value) => report(value);
    }
}
