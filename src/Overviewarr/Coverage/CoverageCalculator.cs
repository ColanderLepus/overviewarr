using System.Globalization;
using Overviewarr.Downloads;
using Overviewarr.Releases;
using Overviewarr.Search;
using Overviewarr.Sonarr;

namespace Overviewarr.Coverage;

/// <summary>What the coverage strip is calculated from.</summary>
public sealed record CoverageInput(
    IReadOnlyList<EpisodeDto> Episodes,
    IReadOnlyDictionary<int, EpisodeFileDto> Files,
    SeriesSnapshot Snapshot,
    IReadOnlySet<ReleaseKey> Matching,
    DateTimeOffset Now)
{
    /// <summary>The selected releases (section 9).</summary>
    public IReadOnlySet<ReleaseKey> Selected { get; init; } = new HashSet<ReleaseKey>();

    /// <summary>The titles of queue records, by episode (section 10).</summary>
    public IReadOnlyDictionary<int, string> QueueTitles { get; init; } = new Dictionary<int, string>();

    /// <summary>Episodes whose last download failed (section 10).</summary>
    public IReadOnlyDictionary<int, FailedDownload> Failures { get; init; } = new Dictionary<int, FailedDownload>();
}

/// <summary>Calculates the coverage strip: one row per searched season, one cell per episode.</summary>
public static class CoverageCalculator
{
    public static CoverageResult Calculate(CoverageInput input)
    {
        var searchedSeasons = input.Snapshot.Searches.Values
            .Where(s => s.Key.IsSeasonSearch && s.FetchedAt is not null)
            .Select(s => s.Key.SeasonNumber)
            .ToHashSet();

        if (searchedSeasons.Count == 0)
        {
            return CoverageResult.Empty;
        }

        var releaseCounts = new Dictionary<int, int>();
        var matchingCounts = new Dictionary<int, int>();
        var selectedEpisodes = new HashSet<int>();

        foreach (var release in input.Snapshot.Releases)
        {
            var matches = input.Matching.Contains(release.Key);
            var selected = input.Selected.Contains(release.Key);

            foreach (var episode in release.Episodes)
            {
                releaseCounts[episode.EpisodeId] = releaseCounts.GetValueOrDefault(episode.EpisodeId) + 1;
                if (matches)
                {
                    matchingCounts[episode.EpisodeId] = matchingCounts.GetValueOrDefault(episode.EpisodeId) + 1;
                }

                if (selected)
                {
                    selectedEpisodes.Add(episode.EpisodeId);
                }
            }
        }

        var episodeSearches = input.Snapshot.Searches.Values
            .Where(s => s.Key.EpisodeId is not null)
            .ToDictionary(s => s.Key.EpisodeId!.Value);

        var rows = input.Episodes
            .Where(e => searchedSeasons.Contains(e.SeasonNumber))
            .GroupBy(e => e.SeasonNumber)
            .OrderBy(g => g.Key)
            .Select(season => new CoverageRow(season.Key, season
                .OrderBy(e => e.EpisodeNumber)
                .Select(episode =>
                {
                    var releaseCount = releaseCounts.GetValueOrDefault(episode.Id);
                    var matchingCount = matchingCounts.GetValueOrDefault(episode.Id);

                    return new CoverageCell(
                        episode,
                        State(matchingCount, releaseCount, episode.AirDateUtc, input.Now),
                        matchingCount,
                        releaseCount,
                        episode.HasFile && input.Files.TryGetValue(episode.EpisodeFileId, out var file) ? FileSummary(file) : null,
                        selectedEpisodes.Contains(episode.Id),
                        input.QueueTitles.GetValueOrDefault(episode.Id),
                        input.Failures.GetValueOrDefault(episode.Id),
                        episodeSearches.GetValueOrDefault(episode.Id));
                })
                .ToList()))
            .ToList();

        return new CoverageResult(rows);
    }

    /// <summary>Whether an episode has aired: its air date is set and not in the future.</summary>
    public static bool HasAired(DateTimeOffset? airDateUtc, DateTimeOffset now) => airDateUtc is { } airDate && airDate <= now;

    private static CoverageState State(int matchingCount, int releaseCount, DateTimeOffset? airDateUtc, DateTimeOffset now) =>
        matchingCount > 0 ? CoverageState.Covered
        : releaseCount > 0 ? CoverageState.NotCovered
        : HasAired(airDateUtc, now) ? CoverageState.Missing
        : CoverageState.NotAired;

    /// <summary>The file's release group, quality, codec, HDR and audio.</summary>
    public static string FileSummary(EpisodeFileDto file)
    {
        var media = file.MediaInfo;
        var audio = string.Join(" ", new[]
        {
            media?.AudioCodec,
            media?.AudioChannels is double channels ? channels.ToString("0.0", CultureInfo.InvariantCulture) : null,
        }.Where(p => !string.IsNullOrWhiteSpace(p)));

        var parts = new[]
        {
            file.ReleaseGroup,
            file.Quality?.Quality?.Name,
            media?.VideoCodec,
            media is null ? null : string.IsNullOrWhiteSpace(media.VideoDynamicRangeType) ? "SDR" : media.VideoDynamicRangeType,
            audio,
        };

        return string.Join(" · ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
    }
}
