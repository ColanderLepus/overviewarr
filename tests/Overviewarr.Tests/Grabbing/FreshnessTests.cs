using Overviewarr.Grabbing;
using Overviewarr.Releases;
using Overviewarr.Search;

namespace Overviewarr.Tests.Grabbing;

public class FreshnessTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 22, 12, 0, 0, TimeSpan.Zero);
    private static readonly SearchKey Season1 = SearchKey.Season(1, 1);
    private static readonly SearchKey Episode5 = SearchKey.Episode(1, 1, 105);

    private static SearchStatus Fetched(SearchKey key, int minutesAgo) =>
        new(key, SearchState.Done, Now.AddMinutes(-minutesAgo), []);

    private static SeriesSnapshot Snapshot(params SearchStatus[] searches) =>
        new(1, searches.ToDictionary(s => s.Key), []);

    private static ReleaseRecord Release(string guid, params SearchKey[] searches) =>
        TestReleases.Make(guid) with { Searches = searches };

    [Theory]
    [InlineData(1, true)]
    [InlineData(25, true)]
    [InlineData(26, false)]
    public void Fresh_within_25_minutes(int minutesAgo, bool fresh)
    {
        var release = Release("a", Season1);

        var plan = Freshness.Plan([release], Snapshot(Fetched(Season1, minutesAgo)), Now);

        Assert.Equal(fresh, plan.Fresh.Contains(release.Key));
        Assert.Equal(!fresh, plan.SearchAgain.ContainsKey(release.Key));
    }

    [Fact]
    public void Fresh_when_any_search_it_came_from_is_fresh()
    {
        var release = Release("a", Season1, Episode5);

        var plan = Freshness.Plan([release], Snapshot(Fetched(Season1, 60), Fetched(Episode5, 10)), Now);

        Assert.Contains(release.Key, plan.Fresh);
    }

    [Fact]
    public void The_newest_search_runs_again()
    {
        var release = Release("a", Season1, Episode5);

        var plan = Freshness.Plan([release], Snapshot(Fetched(Season1, 60), Fetched(Episode5, 40)), Now);

        Assert.Equal(Episode5, plan.SearchAgain[release.Key]);
    }

    [Fact]
    public void Each_search_runs_at_most_once()
    {
        var a = Release("a", Season1);
        var b = Release("b", Season1);

        var plan = Freshness.Plan([a, b], Snapshot(Fetched(Season1, 30)), Now);

        Assert.Equal([Season1], plan.Searches);
    }

    [Fact]
    public void Release_without_a_finished_search_has_none_to_run_again()
    {
        var release = Release("a", Season1);

        var plan = Freshness.Plan([release], Snapshot(), Now);

        Assert.Contains(release.Key, plan.NoSearch);
        Assert.Empty(plan.Searches);
    }
}
