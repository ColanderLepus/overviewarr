using Overviewarr.Releases;
using Overviewarr.Search;
using Overviewarr.Sonarr;

namespace Overviewarr.Tests.Coverage;

internal static class CoverageTestData
{
    public const int SeriesId = 1;

    public static readonly DateTimeOffset Now = new(2026, 11, 22, 12, 0, 0, TimeSpan.Zero);

    /// <summary>An episode with id <c>season * 100 + number</c>. <paramref name="airedDaysAgo"/> null means no air date.</summary>
    public static EpisodeDto Episode(int season, int number, int? airedDaysAgo = 30, int fileId = 0) =>
        new(season * 100 + number, season, number, $"Episode {number}",
            airedDaysAgo is int days ? Now.AddDays(-days) : null, fileId > 0, fileId);

    public static ReleaseRecord Release(string guid, int season, params int[] episodes) =>
        TestReleases.Make(guid, search: SearchKey.Season(SeriesId, season), episodes: episodes.Select(e => (season, e))) with
        {
            Episodes = episodes.Select(e => new ReleaseEpisode(season * 100 + e, season, e)).ToList(),
        };

    public static SearchStatus SeasonSearched(int season) =>
        new(SearchKey.Season(SeriesId, season), SearchState.Done, Now.AddMinutes(-5), []);

    public static SeriesSnapshot Snapshot(IEnumerable<SearchStatus> searches, params ReleaseRecord[] releases) =>
        new(SeriesId, searches.ToDictionary(s => s.Key), releases);
}
