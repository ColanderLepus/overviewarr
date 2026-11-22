using Overviewarr.Releases;

namespace Overviewarr.Search;

/// <summary>
/// Merges the result sets of a series' searches by <c>guid</c> and <c>indexerId</c> (DESIGN.md section 6).
/// A release that comes from several searches uses the most recently fetched copy.
/// </summary>
public static class ReleaseMerger
{
    public static IReadOnlyList<ReleaseRecord> Merge(IEnumerable<SearchStatus> searches)
    {
        var newest = new Dictionary<ReleaseKey, (ReleaseRecord Release, DateTimeOffset FetchedAt)>();
        var sources = new Dictionary<ReleaseKey, List<SearchKey>>();

        foreach (var search in searches.Where(s => s.FetchedAt is not null))
        {
            var fetchedAt = search.FetchedAt!.Value;

            foreach (var release in search.Results)
            {
                if (!newest.TryGetValue(release.Key, out var current) || fetchedAt > current.FetchedAt)
                {
                    newest[release.Key] = (release, fetchedAt);
                }

                if (!sources.TryGetValue(release.Key, out var keys))
                {
                    sources[release.Key] = keys = [];
                }

                if (!keys.Contains(search.Key))
                {
                    keys.Add(search.Key);
                }
            }
        }

        return newest.Values
            .Select(v =>
            {
                var keys = sources[v.Release.Key];
                return v.Release with
                {
                    Searches = keys,
                    Seasons = ReleaseRecord.ComputeSeasons(v.Release.Episodes, keys),
                };
            })
            .OrderBy(r => r.EpisodesSortKey)
            .ThenBy(r => r.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
