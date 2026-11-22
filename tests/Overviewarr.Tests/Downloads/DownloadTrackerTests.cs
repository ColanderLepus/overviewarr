using Overviewarr.Downloads;
using Overviewarr.Sonarr;

namespace Overviewarr.Tests.Downloads;

public class DownloadTrackerTests
{
    private const int SeriesId = 1;
    private static readonly DateTimeOffset Opened = new(2026, 11, 22, 12, 0, 0, TimeSpan.Zero);

    private static HistoryDto Entry(int id, string eventType, int episodeId, string downloadId = "dl-1", int seriesId = SeriesId,
        int minutesAfterOpen = 0, string? guid = null, string? message = null, string title = "Show.S01E01-GRP")
    {
        var data = new Dictionary<string, string?>();
        if (guid is not null)
        {
            data["guid"] = guid;
        }

        if (message is not null)
        {
            data["message"] = message;
        }

        return new HistoryDto(id, Opened.AddMinutes(minutesAfterOpen), eventType, seriesId, episodeId, downloadId, title, data);
    }

    [Fact]
    public void Last_download_is_the_entry_with_the_highest_id()
    {
        var tracker = new DownloadTracker(SeriesId, Opened);

        tracker.AddSeriesHistory(
        [
            Entry(3, HistoryDto.DownloadFailed, 101, message: "Download failed"),
            Entry(1, HistoryDto.Grabbed, 101),
            Entry(2, HistoryDto.DownloadFailed, 102, "dl-2"),
            Entry(4, HistoryDto.DownloadFolderImported, 102, "dl-2"),
        ]);

        Assert.Equal([101], tracker.FailedEpisodes.Keys);
        var failure = Assert.Single(tracker.FailedDownloads);
        Assert.Equal("dl-1", failure.DownloadId);
        Assert.Equal("Download failed", failure.Message);
        Assert.Equal("Show.S01E01-GRP", failure.Title);
    }

    [Fact]
    public void A_newer_grabbed_entry_clears_a_failure()
    {
        var tracker = new DownloadTracker(SeriesId, Opened);
        tracker.AddSeriesHistory([Entry(1, HistoryDto.Grabbed, 101), Entry(2, HistoryDto.DownloadFailed, 101)]);
        Assert.Single(tracker.FailedDownloads);

        tracker.AddHistorySince([Entry(3, HistoryDto.Grabbed, 101, "dl-2", minutesAfterOpen: 1)]);

        Assert.Empty(tracker.FailedDownloads);
        Assert.Empty(tracker.FailedEpisodes);
    }

    [Fact]
    public void A_failed_pack_is_one_row_with_its_episodes()
    {
        var tracker = new DownloadTracker(SeriesId, Opened);

        tracker.AddSeriesHistory(
        [
            Entry(1, HistoryDto.DownloadFailed, 101, "pack"),
            Entry(2, HistoryDto.DownloadFailed, 102, "pack"),
            Entry(3, HistoryDto.DownloadFailed, 103, "pack"),
            Entry(4, HistoryDto.Grabbed, 103, "single"),
        ]);

        var failure = Assert.Single(tracker.FailedDownloads);
        Assert.Equal([101, 102], failure.EpisodeIds.Order());
    }

    [Fact]
    public void History_since_skips_entries_already_seen_other_series_and_other_events()
    {
        var tracker = new DownloadTracker(SeriesId, Opened);
        tracker.AddSeriesHistory([Entry(1, HistoryDto.Grabbed, 101)]);

        var imported = tracker.AddHistorySince(
        [
            Entry(1, HistoryDto.Grabbed, 101),
            Entry(2, HistoryDto.DownloadFailed, 201, seriesId: 2),
            Entry(3, "episodeFileRenamed", 101),
        ]);

        Assert.False(imported);
        Assert.Empty(tracker.FailedDownloads);
    }

    [Fact]
    public void History_since_starts_before_the_page_opened_and_moves_to_the_newest_date()
    {
        var tracker = new DownloadTracker(SeriesId, Opened);
        Assert.Equal(Opened - DownloadTracker.SinceMargin, tracker.SinceStart);

        tracker.AddHistorySince([Entry(5, "episodeFileRenamed", 999, seriesId: 2, minutesAfterOpen: 3), Entry(6, HistoryDto.Grabbed, 101, minutesAfterOpen: 1)]);

        Assert.Equal(Opened.AddMinutes(3), tracker.SinceStart);
    }

    [Fact]
    public void A_new_import_of_the_series_is_reported_once()
    {
        var tracker = new DownloadTracker(SeriesId, Opened);

        Assert.True(tracker.AddHistorySince([Entry(1, HistoryDto.DownloadFolderImported, 101)]));
        Assert.False(tracker.AddHistorySince([Entry(1, HistoryDto.DownloadFolderImported, 101)]));
        Assert.False(tracker.AddHistorySince([Entry(2, HistoryDto.DownloadFolderImported, 201, seriesId: 2)]));
    }

    [Fact]
    public void Failed_release_is_found_through_its_grabbed_entry()
    {
        var tracker = new DownloadTracker(SeriesId, Opened);

        tracker.AddSeriesHistory(
        [
            Entry(1, HistoryDto.Grabbed, 101, "dl-1", guid: "release-guid"),
            Entry(2, HistoryDto.DownloadFailed, 101, "dl-1", message: "Download failed"),
            Entry(3, HistoryDto.Grabbed, 102, "dl-2", guid: "other-guid"),
        ]);

        var failure = Assert.Single(tracker.FailedReleases);
        Assert.Equal("release-guid", failure.Key);
        Assert.Equal("Download failed", failure.Value.Message);
    }

    [Fact]
    public void Data_keys_are_read_ignoring_case()
    {
        var entry = new HistoryDto(1, Opened, HistoryDto.DownloadFailed, SeriesId, 101, "dl", "title",
            new Dictionary<string, string?> { ["Message"] = "Upper case key" });

        Assert.Equal("Upper case key", entry.GetData("message"));
    }

    [Fact]
    public void Queue_titles_are_kept_by_episode()
    {
        var tracker = new DownloadTracker(SeriesId, Opened);

        tracker.SetQueue(
        [
            new QueueRecordDto(SeriesId, 101, "Show.S01-GRP", "dl-1"),
            new QueueRecordDto(SeriesId, 102, "Show.S01-GRP", "dl-1"),
            new QueueRecordDto(2, 201, "Other.S01-GRP", "dl-9"),
            new QueueRecordDto(SeriesId, null, "Unknown", "dl-3"),
        ]);

        Assert.Equal([101, 102], tracker.QueueTitles.Keys.Order());
        Assert.Equal("Show.S01-GRP", tracker.QueueTitles[101]);

        tracker.SetQueue([]);
        Assert.Empty(tracker.QueueTitles);
    }
}
