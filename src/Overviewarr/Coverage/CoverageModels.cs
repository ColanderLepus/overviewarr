using Overviewarr.Downloads;
using Overviewarr.Search;
using Overviewarr.Sonarr;

namespace Overviewarr.Coverage;

/// <summary>The state of an episode in the coverage strip (DESIGN.md sections 6 and 8).</summary>
public enum CoverageState
{
    /// <summary>At least one matching release covers the episode.</summary>
    Covered,

    /// <summary>Releases cover the episode, but none of them matches.</summary>
    NotCovered,

    /// <summary>No release covers the episode, and it has aired.</summary>
    Missing,

    /// <summary>No release covers the episode, and it hasn't aired.</summary>
    NotAired,
}

/// <summary>One episode in the coverage strip.</summary>
public sealed record CoverageCell(
    EpisodeDto Episode,
    CoverageState State,
    int MatchingCount,
    int ReleaseCount,
    string? FileSummary,
    bool Selected,
    string? QueueTitle,
    FailedDownload? Failure,
    SearchStatus? EpisodeSearch)
{
    public int EpisodeId => Episode.Id;

    public bool HasFile => FileSummary is not null;

    public bool InQueue => QueueTitle is not null;

    public bool Failed => Failure is not null;

    public bool IsClickable => State != CoverageState.NotAired;

    public string Label => EpisodeLabels.Format(Episode.SeasonNumber, Episode.EpisodeNumber);
}

/// <summary>One searched season in the coverage strip.</summary>
public sealed record CoverageRow(int SeasonNumber, IReadOnlyList<CoverageCell> Cells);

public sealed record CoverageResult(IReadOnlyList<CoverageRow> Rows)
{
    public static CoverageResult Empty { get; } = new([]);

    public IReadOnlyList<CoverageCell> Missing => Rows.SelectMany(r => r.Cells).Where(c => c.State == CoverageState.Missing).ToList();
}

public static class EpisodeLabels
{
    public static string Format(int seasonNumber, int episodeNumber) =>
        $"S{seasonNumber.ToString("00", System.Globalization.CultureInfo.InvariantCulture)}" +
        $"E{episodeNumber.ToString("00", System.Globalization.CultureInfo.InvariantCulture)}";
}
