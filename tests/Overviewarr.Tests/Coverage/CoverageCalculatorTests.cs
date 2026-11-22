using Overviewarr.Coverage;
using Overviewarr.Downloads;
using Overviewarr.Releases;
using Overviewarr.Search;
using Overviewarr.Sonarr;
using static Overviewarr.Tests.Coverage.CoverageTestData;

namespace Overviewarr.Tests.Coverage;

public class CoverageCalculatorTests
{
    private static CoverageResult Calculate(IReadOnlyList<EpisodeDto> episodes, SeriesSnapshot snapshot, IEnumerable<ReleaseRecord> matching,
        IReadOnlyDictionary<int, EpisodeFileDto>? files = null, Func<CoverageInput, CoverageInput>? adjust = null)
    {
        var input = new CoverageInput(episodes, files ?? new Dictionary<int, EpisodeFileDto>(), snapshot,
            matching.Select(r => r.Key).ToHashSet(), Now);
        return CoverageCalculator.Calculate(adjust is null ? input : adjust(input));
    }

    private static Dictionary<int, CoverageCell> Cells(CoverageResult result) =>
        result.Rows.SelectMany(r => r.Cells).ToDictionary(c => c.EpisodeId);

    [Fact]
    public void Each_state()
    {
        EpisodeDto[] episodes =
        [
            Episode(1, 1), Episode(1, 2), Episode(1, 3), Episode(1, 4, airedDaysAgo: -7), Episode(1, 5, airedDaysAgo: null),
        ];
        var matching = Release("matching", 1, 1);
        var other = Release("other", 1, 1, 2);

        var cells = Cells(Calculate(episodes, Snapshot([SeasonSearched(1)], matching, other), [matching]));

        Assert.Equal(CoverageState.Covered, cells[101].State);
        Assert.Equal(CoverageState.NotCovered, cells[102].State);
        Assert.Equal(CoverageState.Missing, cells[103].State);
        Assert.Equal(CoverageState.NotAired, cells[104].State);
        Assert.Equal(CoverageState.NotAired, cells[105].State);
        Assert.False(cells[104].IsClickable);
    }

    [Fact]
    public void Missing_and_not_aired_by_air_date()
    {
        Assert.True(CoverageCalculator.HasAired(Now.AddMinutes(-1), Now));
        Assert.True(CoverageCalculator.HasAired(Now, Now));
        Assert.False(CoverageCalculator.HasAired(Now.AddMinutes(1), Now));
        Assert.False(CoverageCalculator.HasAired(null, Now));
    }

    [Fact]
    public void Counts_matching_and_all_covering_releases()
    {
        var a = Release("a", 1, 1);
        var b = Release("b", 1, 1);
        var c = Release("c", 1, 1);

        var cell = Cells(Calculate([Episode(1, 1)], Snapshot([SeasonSearched(1)], a, b, c), [a, b]))[101];

        Assert.Equal(2, cell.MatchingCount);
        Assert.Equal(3, cell.ReleaseCount);
    }

    [Fact]
    public void Only_seasons_with_a_finished_season_search_get_a_row()
    {
        EpisodeDto[] episodes = [Episode(1, 1), Episode(2, 1), Episode(3, 1)];
        SearchStatus[] searches =
        [
            SeasonSearched(2),
            new(SearchKey.Season(SeriesId, 1), SearchState.Running, null, []),
            new(SearchKey.Episode(SeriesId, 3, 301), SearchState.Done, Now, []),
        ];

        var result = Calculate(episodes, Snapshot(searches), []);

        Assert.Equal([2], result.Rows.Select(r => r.SeasonNumber));
    }

    [Fact]
    public void Rows_and_cells_are_in_order()
    {
        EpisodeDto[] episodes = [Episode(2, 2), Episode(1, 3), Episode(2, 1), Episode(1, 1)];

        var result = Calculate(episodes, Snapshot([SeasonSearched(2), SeasonSearched(1)]), []);

        Assert.Equal([1, 2], result.Rows.Select(r => r.SeasonNumber));
        Assert.Equal([1, 3], result.Rows[0].Cells.Select(c => c.Episode.EpisodeNumber));
        Assert.Equal([1, 2], result.Rows[1].Cells.Select(c => c.Episode.EpisodeNumber));
    }

    [Fact]
    public void Episode_with_a_file_has_its_summary()
    {
        var file = new EpisodeFileDto(9, "GRP", new QualityModelDto(new QualityDto(3, "WEBDL-1080p", "web", 1080), null),
            new MediaInfoDto("x265", "", "EAC3", 5.1));
        var hdrFile = file with { Id = 10, MediaInfo = file.MediaInfo! with { VideoDynamicRangeType = "DV HDR10" } };
        var files = new Dictionary<int, EpisodeFileDto> { [9] = file, [10] = hdrFile };

        var cells = Cells(Calculate([Episode(1, 1, fileId: 9), Episode(1, 2, fileId: 10), Episode(1, 3)], Snapshot([SeasonSearched(1)]), [], files));

        Assert.Equal("GRP · WEBDL-1080p · x265 · SDR · EAC3 5.1", cells[101].FileSummary);
        Assert.Equal("GRP · WEBDL-1080p · x265 · DV HDR10 · EAC3 5.1", cells[102].FileSummary);
        Assert.False(cells[103].HasFile);
    }

    [Fact]
    public void Markers_come_from_selected_releases_the_queue_and_failures()
    {
        var pack = Release("pack", 1, 1, 2);
        EpisodeDto[] episodes = [Episode(1, 1), Episode(1, 2), Episode(1, 3)];
        var failure = new FailedDownload("dl-1", "Show.S01E02-GRP", [102], "Download failed", Now);

        var cells = Cells(Calculate(episodes, Snapshot([SeasonSearched(1)], pack), [], adjust: input => input with
        {
            Selected = new HashSet<ReleaseKey> { pack.Key },
            QueueTitles = new Dictionary<int, string> { [103] = "Show.S01E03-GRP" },
            Failures = new Dictionary<int, FailedDownload> { [102] = failure },
        }));

        Assert.True(cells[101].Selected);
        Assert.True(cells[102].Selected);
        Assert.False(cells[103].Selected);
        Assert.True(cells[103].InQueue);
        Assert.Equal("Show.S01E03-GRP", cells[103].QueueTitle);
        Assert.False(cells[101].InQueue);
        Assert.Same(failure, cells[102].Failure);
        Assert.False(cells[101].Failed);
    }

    [Fact]
    public void Failed_episode_search_stays_missing_and_is_attached()
    {
        var failed = new SearchStatus(SearchKey.Episode(SeriesId, 1, 101), SearchState.Failed, null, [], "No indexers available");

        var result = Calculate([Episode(1, 1)], Snapshot([SeasonSearched(1), failed]), []);
        var cell = Cells(result)[101];

        Assert.Equal(CoverageState.Missing, cell.State);
        Assert.Equal("No indexers available", cell.EpisodeSearch?.Error);
        Assert.Equal([101], result.Missing.Select(c => c.EpisodeId));
    }

    [Fact]
    public void Nothing_searched_gives_an_empty_strip() =>
        Assert.Empty(Calculate([Episode(1, 1)], Snapshot([]), []).Rows);
}
