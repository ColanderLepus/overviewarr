using Overviewarr.Releases;

namespace Overviewarr.Search;

/// <summary>
/// A season search (<see cref="EpisodeId"/> null) or an episode search. An episode search carries its episode's
/// season number.
/// </summary>
public sealed record SearchKey(int SeriesId, int SeasonNumber, int? EpisodeId = null)
{
    public static SearchKey Season(int seriesId, int seasonNumber) => new(seriesId, seasonNumber);

    public static SearchKey Episode(int seriesId, int seasonNumber, int episodeId) => new(seriesId, seasonNumber, episodeId);

    public bool IsSeasonSearch => EpisodeId is null;
}

public enum SearchState
{
    Waiting,
    Running,
    Done,
    Failed,
}

/// <summary>
/// The state of one search. <see cref="Results"/> and <see cref="FetchedAt"/> are the last successful results,
/// which a failed or repeated search keeps.
/// </summary>
public sealed record SearchStatus(
    SearchKey Key,
    SearchState State,
    DateTimeOffset? FetchedAt,
    IReadOnlyList<ReleaseRecord> Results,
    string? Error = null,
    bool Unauthorized = false)
{
    public bool IsActive => State is SearchState.Waiting or SearchState.Running;
}

/// <summary>An immutable view of one series in the search cache.</summary>
public sealed record SeriesSnapshot(
    int SeriesId,
    IReadOnlyDictionary<SearchKey, SearchStatus> Searches,
    IReadOnlyList<ReleaseRecord> Releases)
{
    public static SeriesSnapshot Empty(int seriesId) => new(seriesId, new Dictionary<SearchKey, SearchStatus>(), []);

    public SearchStatus? SeasonSearch(int seasonNumber) =>
        Searches.TryGetValue(SearchKey.Season(SeriesId, seasonNumber), out var status) ? status : null;
}
