using System.Globalization;
using Overviewarr.Releases;
using Overviewarr.Sonarr;

namespace Overviewarr.Coverage;

/// <summary>How many of a season's aired episodes a combination covers. <see cref="AiredEpisodeIds"/> are in episode order.</summary>
public sealed record SeasonCoverage(int SeasonNumber, int Covered, IReadOnlyList<int> AiredEpisodeIds)
{
    public int Aired => AiredEpisodeIds.Count;

    public bool IsComplete => Covered == Aired;

    /// <summary>The season as the coverage strip labels it, for example <c>S1</c>.</summary>
    public string Label => "S" + SeasonNumber.ToString(CultureInfo.InvariantCulture);
}

/// <summary>
/// Releases with the same release group, quality, codec, HDR, audio format, audio channels and streaming service
/// (DESIGN.md section 8, Combinations). <see cref="Seasons"/> are the seasons it covers completely or partly, in season
/// order, and <see cref="Picks"/> the releases its Select button adds to the selection.
/// </summary>
public sealed record Combination(
    string ReleaseGroup,
    string Quality,
    string Codec,
    string Hdr,
    string AudioFormat,
    string AudioChannels,
    string StreamingService,
    IReadOnlyList<SeasonCoverage> Seasons,
    IReadOnlyList<ReleaseRecord> Releases,
    IReadOnlyList<ReleaseRecord> Picks)
{
    public int CompleteSeasonCount => Seasons.Count(s => s.IsComplete);

    public int AiredEpisodesCovered => Seasons.Sum(s => s.Covered);

    /// <summary>The Complete seasons column, for example <c>S1 S2 S3 (S4 9/10)</c>.</summary>
    public string SeasonsText => string.Join(" ",
        Seasons.Where(s => s.IsComplete).Select(s => s.Label)
            .Concat(Seasons.Where(s => !s.IsComplete).Select(s => $"({s.Label} {s.Covered}/{s.Aired})")));
}

/// <summary>Finds the combinations of releases that cover whole seasons (DESIGN.md section 8, Combinations).</summary>
public static class CombinationFinder
{
    /// <summary>
    /// The combinations with at least one complete season, in the panel's order. <paramref name="releases"/> are the
    /// releases the page uses for the combinations; each one with a release group belongs to one combination.
    /// </summary>
    public static IReadOnlyList<Combination> Find(IEnumerable<ReleaseRecord> releases, IEnumerable<EpisodeDto> episodes, DateTimeOffset now)
    {
        var airedBySeason = episodes
            .Where(e => CoverageCalculator.HasAired(e.AirDateUtc, now))
            .GroupBy(e => e.SeasonNumber)
            .OrderBy(g => g.Key)
            .Select(g => (SeasonNumber: g.Key, EpisodeIds: g.OrderBy(e => e.EpisodeNumber).Select(e => e.Id).ToList()))
            .ToList();

        var combinations = new List<Combination>();

        foreach (var group in releases.Where(r => !string.IsNullOrWhiteSpace(r.ReleaseGroup)).GroupBy(CombinationKey.Of))
        {
            var members = group.ToList();
            var covered = members.SelectMany(r => r.Episodes).Select(e => e.EpisodeId).ToHashSet();
            var seasons = airedBySeason
                .Select(s => new SeasonCoverage(s.SeasonNumber, s.EpisodeIds.Count(covered.Contains), s.EpisodeIds))
                .Where(s => s.Covered > 0)
                .ToList();

            if (!seasons.Any(s => s.IsComplete))
            {
                continue;
            }

            var first = members[0];
            combinations.Add(new Combination(
                Spelling(members), first.Quality, first.CodecText, first.HdrText, first.AudioFormatText,
                first.AudioChannelsText, first.StreamingServiceText, seasons, members, Pick(members, seasons)));
        }

        return combinations
            .OrderByDescending(c => c.CompleteSeasonCount)
            .ThenByDescending(c => c.AiredEpisodesCovered)
            .ThenBy(c => c.ReleaseGroup, StringComparer.OrdinalIgnoreCase)
            .ThenBy(c => c.Quality, StringComparer.Ordinal)
            .ThenBy(c => c.Codec, StringComparer.Ordinal)
            .ThenBy(c => c.Hdr, StringComparer.Ordinal)
            .ThenBy(c => c.AudioFormat, StringComparer.Ordinal)
            .ThenBy(c => c.AudioChannels, StringComparer.Ordinal)
            .ThenBy(c => c.StreamingService, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>The release group as most of the releases spell it. On a tie, the spelling that sorts first.</summary>
    private static string Spelling(IEnumerable<ReleaseRecord> releases) => releases
        .GroupBy(r => r.ReleaseGroup!, StringComparer.Ordinal)
        .OrderByDescending(g => g.Count())
        .ThenBy(g => g.Key, StringComparer.Ordinal)
        .First()
        .Key;

    /// <summary>
    /// The releases Select adds: for each complete season, in episode order, each aired episode that no release picked
    /// so far covers gets one of the releases that cover it, preferring one that covers no picked episode, then a season
    /// pack, then one Sonarr didn't reject, then a proper/repack, then the highest custom format score, then the newest.
    /// </summary>
    private static List<ReleaseRecord> Pick(IReadOnlyList<ReleaseRecord> releases, IEnumerable<SeasonCoverage> seasons)
    {
        var picked = new List<ReleaseRecord>();
        var covered = new HashSet<int>();

        foreach (var episodeId in seasons.Where(s => s.IsComplete).SelectMany(s => s.AiredEpisodeIds))
        {
            if (covered.Contains(episodeId))
            {
                continue;
            }

            var release = releases
                .Where(r => r.Episodes.Any(e => e.EpisodeId == episodeId))
                .OrderBy(r => r.Episodes.Any(e => covered.Contains(e.EpisodeId)))
                .ThenByDescending(r => r.SeasonPack)
                .ThenBy(r => r.Rejected)
                .ThenByDescending(r => r.ProperRepack)
                .ThenByDescending(r => r.CustomFormatScore)
                .ThenBy(r => r.Age)
                .First();

            picked.Add(release);
            covered.UnionWith(release.Episodes.Select(e => e.EpisodeId));
        }

        return picked;
    }

    /// <summary>What a combination's releases share: the release group ignoring case, the quality, and the list values in any order.</summary>
    private sealed record CombinationKey(
        string ReleaseGroup, string Quality, string Codec, string Hdr, string AudioFormat, string AudioChannels, string StreamingService)
    {
        public static CombinationKey Of(ReleaseRecord release) => new(
            release.ReleaseGroup!.ToUpperInvariant(),
            release.Quality,
            Values(release.Codec),
            Values(release.Hdr),
            Values(release.AudioFormat),
            Values(release.AudioChannels),
            Values(release.StreamingService));

        private static string Values(IEnumerable<Tag> tags) =>
            string.Join("\n", tags.Select(t => t.Name).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));
    }
}
