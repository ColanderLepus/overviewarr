using Overviewarr.Releases;

namespace Overviewarr.Tests.Releases;

public class FormattingTests
{
    private static IEnumerable<ReleaseEpisode> Episodes(params (int Season, int Episode)[] episodes) =>
        episodes.Select(e => new ReleaseEpisode(e.Season * 100 + e.Episode, e.Season, e.Episode));

    [Fact]
    public void Season_pack_is_one_range() =>
        Assert.Equal("S01E01–E10", EpisodeRanges.Format(Episodes(Enumerable.Range(1, 10).Select(n => (1, n)).ToArray())));

    [Fact]
    public void Single_episode_has_no_range() =>
        Assert.Equal("S01E03", EpisodeRanges.Format(Episodes((1, 3))));

    [Fact]
    public void Multi_season_pack_has_a_range_per_season() =>
        Assert.Equal("S01E01–E03, S02E01–E02", EpisodeRanges.Format(Episodes((2, 2), (1, 1), (1, 2), (2, 1), (1, 3))));

    [Fact]
    public void Gaps_split_ranges() =>
        Assert.Equal("S01E01–E02, S01E05", EpisodeRanges.Format(Episodes((1, 1), (1, 2), (1, 5))));

    [Fact]
    public void Gigabytes_use_1024_cubed() =>
        Assert.Equal(1.5.ToString("0.00") + " GB", DisplayFormat.Gigabytes(1024L * 1024 * 1024 * 3 / 2));

    [Theory]
    [InlineData(1, "1 day")]
    [InlineData(0, "0 days")]
    [InlineData(12, "12 days")]
    public void Days_are_whole_days(int days, string expected) => Assert.Equal(expected, DisplayFormat.Days(days));

    [Fact]
    public void Format_provider_formats_column_values()
    {
        Assert.Equal("Yes", string.Format(DisplayFormat.Instance, "{0:yesno}", true));
        Assert.Equal("3 days", string.Format(DisplayFormat.Instance, "{0:days}", 3));
        Assert.Equal(string.Empty, string.Format(DisplayFormat.Instance, "{0:GB}", (long?)null));
    }

    [Fact]
    public void Time_shows_the_date_only_when_it_is_not_today()
    {
        var utc = TimeZoneInfo.Utc;
        var now = new DateTimeOffset(2026, 11, 22, 18, 0, 0, TimeSpan.Zero);

        Assert.Equal("14:02", DisplayFormat.Time(new DateTimeOffset(2026, 11, 22, 14, 2, 0, TimeSpan.Zero), now, utc));
        Assert.Equal("2026-11-21 14:02", DisplayFormat.Time(new DateTimeOffset(2026, 11, 21, 14, 2, 0, TimeSpan.Zero), now, utc));
    }
}
