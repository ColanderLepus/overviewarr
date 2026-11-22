using Overviewarr.Sonarr;

namespace Overviewarr.Downloads;

/// <summary>A failed download: its release title, the episodes whose last download it was, and Sonarr's message.</summary>
public sealed record FailedDownload(string DownloadId, string Title, IReadOnlyList<int> EpisodeIds, string? Message, DateTimeOffset Date);

/// <summary>
/// The series page's queue and history state (DESIGN.md section 10). Each episode's last download is its entry with
/// the highest <c>id</c> among grabbed, imported, failed and ignored; it failed when that entry is a failure.
/// </summary>
public sealed class DownloadTracker
{
    /// <summary>The first <c>history/since</c> call starts this long before the page opened.</summary>
    public static readonly TimeSpan SinceMargin = TimeSpan.FromMinutes(5);

    private readonly int _seriesId;
    private readonly HashSet<int> _seen = [];
    private readonly Dictionary<int, HistoryDto> _lastDownload = [];
    private readonly Dictionary<string, HistoryDto> _grabs = new(StringComparer.Ordinal);

    public DownloadTracker(int seriesId, DateTimeOffset pageOpened)
    {
        _seriesId = seriesId;
        SinceStart = pageOpened - SinceMargin;
    }

    /// <summary>Where the next <c>history/since</c> call starts: the newest <c>date</c> returned so far.</summary>
    public DateTimeOffset SinceStart { get; private set; }

    /// <summary>The queue records' titles, by episode.</summary>
    public IReadOnlyDictionary<int, string> QueueTitles { get; private set; } = new Dictionary<int, string>();

    /// <summary>Failed downloads, newest first.</summary>
    public IReadOnlyList<FailedDownload> FailedDownloads { get; private set; } = [];

    /// <summary>Episodes whose last download failed.</summary>
    public IReadOnlyDictionary<int, FailedDownload> FailedEpisodes { get; private set; } = new Dictionary<int, FailedDownload>();

    /// <summary>Failed downloads by the <c>data.guid</c> of their <c>grabbed</c> entry.</summary>
    public IReadOnlyDictionary<string, FailedDownload> FailedReleases { get; private set; } = new Dictionary<string, FailedDownload>();

    /// <summary>Adds the series' download history, read when the page opens.</summary>
    public void AddSeriesHistory(IEnumerable<HistoryDto> entries)
    {
        foreach (var entry in entries.Where(e => e.SeriesId == _seriesId))
        {
            Process(entry);
        }

        Rebuild();
    }

    /// <summary>Adds the entries of a <c>history/since</c> call. Returns true when a new import of this series arrived.</summary>
    public bool AddHistorySince(IEnumerable<HistoryDto> entries)
    {
        var imported = false;

        foreach (var entry in entries)
        {
            if (entry.Date > SinceStart)
            {
                SinceStart = entry.Date;
            }

            if (entry.SeriesId == _seriesId && Process(entry) && entry.Is(HistoryDto.DownloadFolderImported))
            {
                imported = true;
            }
        }

        Rebuild();
        return imported;
    }

    /// <summary>Replaces the queue with the latest queue records.</summary>
    public void SetQueue(IEnumerable<QueueRecordDto> records) =>
        QueueTitles = records
            .Where(r => r.EpisodeId is not null && (r.SeriesId is null || r.SeriesId == _seriesId))
            .GroupBy(r => r.EpisodeId!.Value)
            .ToDictionary(g => g.Key, g => string.Join(", ", g.Select(r => r.Title).Where(t => !string.IsNullOrEmpty(t)).Distinct()));

    /// <summary>Records a download entry. Returns false for other event types and entries already seen.</summary>
    private bool Process(HistoryDto entry)
    {
        var isDownloadEvent = entry.Is(HistoryDto.Grabbed) || entry.Is(HistoryDto.DownloadFolderImported) ||
                              entry.Is(HistoryDto.DownloadFailed) || entry.Is(HistoryDto.DownloadIgnored);

        if (!isDownloadEvent || !_seen.Add(entry.Id))
        {
            return false;
        }

        if (!_lastDownload.TryGetValue(entry.EpisodeId, out var last) || entry.Id > last.Id)
        {
            _lastDownload[entry.EpisodeId] = entry;
        }

        if (entry.Is(HistoryDto.Grabbed) && !string.IsNullOrEmpty(entry.DownloadId) &&
            (!_grabs.TryGetValue(entry.DownloadId, out var grab) || entry.Id > grab.Id))
        {
            _grabs[entry.DownloadId] = entry;
        }

        return true;
    }

    private void Rebuild()
    {
        FailedDownloads = _lastDownload.Values
            .Where(e => e.Is(HistoryDto.DownloadFailed))
            .GroupBy(e => string.IsNullOrEmpty(e.DownloadId) ? $"history-{e.Id}" : e.DownloadId)
            .Select(g =>
            {
                var newest = g.MaxBy(e => e.Id)!;
                return new FailedDownload(g.Key, newest.SourceTitle ?? string.Empty, g.Select(e => e.EpisodeId).Distinct().ToList(),
                    newest.GetData("message"), newest.Date);
            })
            .OrderByDescending(f => f.Date)
            .ToList();

        FailedEpisodes = FailedDownloads
            .SelectMany(f => f.EpisodeIds.Select(id => (EpisodeId: id, Failure: f)))
            .ToDictionary(x => x.EpisodeId, x => x.Failure);

        var failedReleases = new Dictionary<string, FailedDownload>(StringComparer.Ordinal);
        foreach (var failure in FailedDownloads)
        {
            if (_grabs.TryGetValue(failure.DownloadId, out var grab) && grab.GetData("guid") is { Length: > 0 } guid)
            {
                failedReleases.TryAdd(guid, failure);
            }
        }

        FailedReleases = failedReleases;
    }
}
