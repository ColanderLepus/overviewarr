using Overviewarr.Releases;
using Overviewarr.Search;

namespace Overviewarr.Grabbing;

/// <summary>Which selected releases can be sent as they are, and which searches run again first.</summary>
public sealed record FreshnessPlan(
    IReadOnlySet<ReleaseKey> Fresh,
    IReadOnlyDictionary<ReleaseKey, SearchKey> SearchAgain,
    IReadOnlySet<ReleaseKey> NoSearch)
{
    /// <summary>The searches to run again, each once.</summary>
    public IReadOnlyList<SearchKey> Searches => SearchAgain.Values.Distinct().ToList();
}

/// <summary>
/// DESIGN.md section 9: a selected release is fresh when a search it came from was fetched within the last
/// 25 minutes. Otherwise the newest search it came from runs again, each search at most once per send.
/// </summary>
public static class Freshness
{
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(25);

    public static FreshnessPlan Plan(IEnumerable<ReleaseRecord> selected, SeriesSnapshot snapshot, DateTimeOffset now)
    {
        var fresh = new HashSet<ReleaseKey>();
        var searchAgain = new Dictionary<ReleaseKey, SearchKey>();
        var noSearch = new HashSet<ReleaseKey>();

        foreach (var release in selected)
        {
            var fetched = release.Searches
                .Select(key => snapshot.Searches.GetValueOrDefault(key))
                .Where(status => status?.FetchedAt is not null)
                .Select(status => status!)
                .ToList();

            if (fetched.Any(status => now - status.FetchedAt!.Value <= Window))
            {
                fresh.Add(release.Key);
            }
            else if (fetched.MaxBy(status => status.FetchedAt) is { } newest)
            {
                searchAgain[release.Key] = newest.Key;
            }
            else
            {
                noSearch.Add(release.Key);
            }
        }

        return new FreshnessPlan(fresh, searchAgain, noSearch);
    }
}
