using Overviewarr.Coverage;
using Overviewarr.Releases;
using Overviewarr.Search;
using Overviewarr.Sonarr;

namespace Overviewarr.Grabbing;

/// <summary>An episode covered by more than one selected release.</summary>
public sealed record OverlapWarning(ReleaseEpisode Episode, IReadOnlyList<ReleaseRecord> Releases)
{
    public string Label => EpisodeLabels.Format(Episode.SeasonNumber, Episode.EpisodeNumber);
}

/// <summary>The confirmation dialog's warnings (DESIGN.md section 9).</summary>
public sealed record ConfirmationWarnings(IReadOnlyList<OverlapWarning> Overlaps, IReadOnlyList<EpisodeDto> Gaps)
{
    /// <summary>
    /// Overlaps: each episode covered by more than one selected release. Gaps: each aired episode in the searched
    /// seasons that has no file, no queue record and no selected release covering it.
    /// </summary>
    public static ConfirmationWarnings Calculate(IReadOnlyList<ReleaseRecord> selected, IReadOnlyList<EpisodeDto> episodes,
        SeriesSnapshot snapshot, IReadOnlySet<int> queuedEpisodes, DateTimeOffset now)
    {
        var covering = new Dictionary<int, (ReleaseEpisode Episode, List<ReleaseRecord> Releases)>();
        foreach (var release in selected)
        {
            foreach (var episode in release.Episodes)
            {
                if (!covering.TryGetValue(episode.EpisodeId, out var entry))
                {
                    covering[episode.EpisodeId] = entry = (episode, []);
                }

                entry.Releases.Add(release);
            }
        }

        var overlaps = covering.Values
            .Where(c => c.Releases.Count > 1)
            .OrderBy(c => c.Episode.SeasonNumber)
            .ThenBy(c => c.Episode.EpisodeNumber)
            .Select(c => new OverlapWarning(c.Episode, c.Releases))
            .ToList();

        var searchedSeasons = snapshot.Searches.Values
            .Where(s => s.Key.IsSeasonSearch && s.FetchedAt is not null)
            .Select(s => s.Key.SeasonNumber)
            .ToHashSet();

        var gaps = episodes
            .Where(e => searchedSeasons.Contains(e.SeasonNumber)
                        && CoverageCalculator.HasAired(e.AirDateUtc, now)
                        && !e.HasFile
                        && !queuedEpisodes.Contains(e.Id)
                        && !covering.ContainsKey(e.Id))
            .OrderBy(e => e.SeasonNumber)
            .ThenBy(e => e.EpisodeNumber)
            .ToList();

        return new ConfirmationWarnings(overlaps, gaps);
    }
}
