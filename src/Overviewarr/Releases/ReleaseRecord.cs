using Overviewarr.Search;

namespace Overviewarr.Releases;

/// <summary>A text value in a list property, as an object so Radzen's checkbox filter can list each value.</summary>
public sealed record Tag(string Name)
{
    public override string ToString() => Name;
}

/// <summary>A season number in the Season list property.</summary>
public sealed record SeasonTag(int Number)
{
    public override string ToString() => Number.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>Identifies a release: its <c>indexerId</c> and <c>guid</c>.</summary>
public readonly record struct ReleaseKey(int IndexerId, string Guid);

/// <summary>An episode Sonarr matched a release to (<c>mappedEpisodeInfo</c>).</summary>
public sealed record ReleaseEpisode(int EpisodeId, int SeasonNumber, int EpisodeNumber);

/// <summary>The newest failed download of a release (DESIGN.md section 10): Sonarr's message and the time.</summary>
public sealed record ReleaseFailure(string? Message, DateTimeOffset Date);

/// <summary>The values of the Status property (DESIGN.md section 8).</summary>
public static class ReleaseStatus
{
    public const string DownloadFailed = "Download failed";
    public const string RejectedBySonarr = "Rejected by Sonarr";
    public const string None = "None";

    /// <summary>The Status column's filter when the series page opens: every value except Download failed (DESIGN.md section 8).</summary>
    public static List<string> StartingFilter() => [RejectedBySonarr, None];
}

/// <summary>
/// A normalized release (DESIGN.md section 7). The text properties of the list values back the filter builder
/// and sorting; the <see cref="Tag"/> lists back the grid's checkbox filters.
/// </summary>
public sealed record ReleaseRecord
{
    public required string Guid { get; init; }

    public required int IndexerId { get; init; }

    public ReleaseKey Key => new(IndexerId, Guid);

    public required string Title { get; init; }

    public required string Indexer { get; init; }

    public required long Size { get; init; }

    public required int Age { get; init; }

    public required string Quality { get; init; }

    public required string Source { get; init; }

    public required int Resolution { get; init; }

    public required int RevisionVersion { get; init; }

    public string? ReleaseGroup { get; init; }

    public required IReadOnlyList<Tag> Languages { get; init; }

    public required bool SeasonPack { get; init; }

    /// <summary>The episodes Sonarr matched the release to, in season and episode order.</summary>
    public required IReadOnlyList<ReleaseEpisode> Episodes { get; init; }

    public required IReadOnlyList<Tag> CustomFormats { get; init; }

    public required int CustomFormatScore { get; init; }

    public required bool Rejected { get; init; }

    /// <summary>Set by the series page when the release has a failed download in the series' history (DESIGN.md section 10).</summary>
    public ReleaseFailure? Failure { get; init; }

    /// <summary>One status per release: a failed download comes before a rejection by Sonarr.</summary>
    public string Status => Failure is not null ? ReleaseStatus.DownloadFailed : Rejected ? ReleaseStatus.RejectedBySonarr : ReleaseStatus.None;

    public required IReadOnlyList<string> Rejections { get; init; }

    /// <summary>How Sonarr's rejection reason ends when the series' quality profile doesn't allow the release's quality.</summary>
    public const string QualityNotWantedReason = "is not wanted in profile";

    /// <summary>Sonarr rejected the release because the series' quality profile doesn't allow its quality (DESIGN.md section 8, Grid switches).</summary>
    public bool QualityNotWanted => Rejections.Any(r => r.EndsWith(QualityNotWantedReason, StringComparison.Ordinal));

    public required IReadOnlyList<Tag> Codec { get; init; }

    public required IReadOnlyList<Tag> Hdr { get; init; }

    public required IReadOnlyList<Tag> AudioFormat { get; init; }

    public required IReadOnlyList<Tag> AudioChannels { get; init; }

    public required IReadOnlyList<Tag> StreamingService { get; init; }

    /// <summary>The searches the release came from.</summary>
    public required IReadOnlyList<SearchKey> Searches { get; init; }

    /// <summary>The seasons in <see cref="Episodes"/>, or the seasons of <see cref="Searches"/> when it's empty.</summary>
    public required IReadOnlyList<SeasonTag> Seasons { get; init; }

    public bool ProperRepack => RevisionVersion > 1;

    public long? SizePerEpisode => Episodes.Count > 0 ? Size / Episodes.Count : null;

    public double SizeGb => Size / Gigabyte;

    public double? SizePerEpisodeGb => SizePerEpisode / Gigabyte;

    public string SeasonText => string.Join(", ", Seasons.Select(s => s.Number));

    public int SeasonSortKey => Seasons.Count > 0 ? Seasons[0].Number : int.MaxValue;

    public string EpisodesText => EpisodeRanges.Format(Episodes);

    public int EpisodesSortKey => Episodes.Count > 0 ? Episodes[0].SeasonNumber * 100_000 + Episodes[0].EpisodeNumber : int.MaxValue;

    public string LanguagesText => Join(Languages);

    public string CustomFormatsText => Join(CustomFormats);

    public string CodecText => Join(Codec);

    public string HdrText => Join(Hdr);

    public string AudioFormatText => Join(AudioFormat);

    public string AudioChannelsText => Join(AudioChannels);

    public string StreamingServiceText => Join(StreamingService);

    private const double Gigabyte = 1024d * 1024d * 1024d;

    private static string Join(IReadOnlyList<Tag> tags) => string.Join(", ", tags.Select(t => t.Name));

    /// <summary>The Season values: seasons of the matched episodes, or of the searches when none are matched.</summary>
    public static IReadOnlyList<SeasonTag> ComputeSeasons(IReadOnlyList<ReleaseEpisode> episodes, IEnumerable<SearchKey> searches) =>
        (episodes.Count > 0 ? episodes.Select(e => e.SeasonNumber) : searches.Select(s => s.SeasonNumber))
        .Distinct()
        .Order()
        .Select(n => new SeasonTag(n))
        .ToList();
}
