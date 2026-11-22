using Overviewarr.Downloads;
using Overviewarr.Releases;
using Overviewarr.Search;
using Overviewarr.Sonarr;

namespace Overviewarr.Coverage;

/// <summary>What a click on a coverage cell turned on.</summary>
public enum HighlightMode
{
    /// <summary>A covered cell: the grid shows only matching releases that cover the episode.</summary>
    EpisodeFilter,

    /// <summary>A not covered cell: every release that covers the episode, filters set aside.</summary>
    GapView,

    /// <summary>A missing cell: its episode search was started.</summary>
    EpisodeSearch,
}

/// <summary>The highlighted cell.</summary>
public sealed record CellHighlight(int EpisodeId, HighlightMode Mode);

/// <summary>
/// The series page's views of its releases (DESIGN.md sections 8 and 11): the coverage strip from the matching
/// set, the main grid's data from the filter builder's result, and the gap view's data. The highlighted cell
/// changes the grids, never the strip.
/// </summary>
public sealed class SeriesView
{
    private IReadOnlyList<EpisodeDto> _episodes = [];
    private Dictionary<int, EpisodeFileDto> _files = [];
    private Dictionary<int, EpisodeDto> _episodesById = [];

    public SeriesSnapshot Snapshot { get; private set; } = SeriesSnapshot.Empty(0);

    /// <summary>The filter builder's result.</summary>
    public IReadOnlyList<ReleaseRecord> BuilderResult { get; private set; } = [];

    /// <summary>The releases matching the filter builder and the column filters.</summary>
    public IReadOnlySet<ReleaseKey> Matching { get; private set; } = new HashSet<ReleaseKey>();

    public CoverageResult Coverage { get; private set; } = CoverageResult.Empty;

    public CellHighlight? Highlight { get; private set; }

    /// <summary>The main grid's data: the filter builder's result, narrowed by the episode filter.</summary>
    public IReadOnlyList<ReleaseRecord> GridData { get; private set; } = [];

    /// <summary>The gap view's data: every release that covers the episode.</summary>
    public IReadOnlyList<ReleaseRecord> GapData { get; private set; } = [];

    public bool IsGapView => Highlight?.Mode == HighlightMode.GapView;

    public void SetEpisodes(IReadOnlyList<EpisodeDto> episodes, IReadOnlyList<EpisodeFileDto> files)
    {
        _episodes = episodes;
        _episodesById = episodes.ToDictionary(e => e.Id);
        _files = files.ToDictionary(f => f.Id);
    }

    /// <summary>Recalculates everything from a snapshot, the builder's result, the matching set and the selection.</summary>
    public void Update(SeriesSnapshot snapshot, IReadOnlyList<ReleaseRecord> builderResult, IEnumerable<ReleaseRecord> matching,
        DateTimeOffset now, IReadOnlySet<ReleaseKey>? selected = null, DownloadTracker? downloads = null)
    {
        Snapshot = snapshot;
        BuilderResult = builderResult;
        Matching = matching.Select(r => r.Key).ToHashSet();
        Coverage = CoverageCalculator.Calculate(new CoverageInput(_episodes, _files, snapshot, Matching, now)
        {
            Selected = selected ?? new HashSet<ReleaseKey>(),
            QueueTitles = downloads?.QueueTitles ?? new Dictionary<int, string>(),
            Failures = downloads?.FailedEpisodes ?? new Dictionary<int, FailedDownload>(),
        });
        UpdateGrids();
    }

    /// <summary>An episode's season and episode number.</summary>
    public ReleaseEpisode? FindEpisode(int episodeId) =>
        _episodesById.TryGetValue(episodeId, out var episode) ? new ReleaseEpisode(episode.Id, episode.SeasonNumber, episode.EpisodeNumber) : null;

    /// <summary>The series' episodes, as loaded from Sonarr.</summary>
    public IReadOnlyList<EpisodeDto> Episodes => _episodes;

    /// <summary>Handles a click on a cell. Returns true when the cell's episode search should run.</summary>
    public bool Click(CoverageCell cell)
    {
        switch (cell.State)
        {
            case CoverageState.Covered:
                Toggle(cell.EpisodeId, HighlightMode.EpisodeFilter);
                return false;
            case CoverageState.NotCovered:
                Toggle(cell.EpisodeId, HighlightMode.GapView);
                return false;
            case CoverageState.Missing:
                Highlight = new CellHighlight(cell.EpisodeId, HighlightMode.EpisodeSearch);
                UpdateGrids();
                return true;
            default:
                return false;
        }
    }

    public void EndGapView()
    {
        if (IsGapView)
        {
            Highlight = null;
            UpdateGrids();
        }
    }

    public string EpisodeLabel(int episodeId) =>
        _episodesById.TryGetValue(episodeId, out var episode) ? EpisodeLabels.Format(episode.SeasonNumber, episode.EpisodeNumber) : string.Empty;

    private void Toggle(int episodeId, HighlightMode mode)
    {
        Highlight = Highlight == new CellHighlight(episodeId, mode) ? null : new CellHighlight(episodeId, mode);
        UpdateGrids();
    }

    private void UpdateGrids()
    {
        GridData = Highlight is { Mode: HighlightMode.EpisodeFilter } filter ? Covering(BuilderResult, filter.EpisodeId) : BuilderResult;
        GapData = Highlight is { Mode: HighlightMode.GapView } gap ? Covering(Snapshot.Releases, gap.EpisodeId) : [];
    }

    private static IReadOnlyList<ReleaseRecord> Covering(IEnumerable<ReleaseRecord> releases, int episodeId) =>
        releases.Where(r => r.Episodes.Any(e => e.EpisodeId == episodeId)).ToList();
}
