using Overviewarr.Coverage;
using Overviewarr.Filtering;
using Overviewarr.Releases;
using Overviewarr.Search;
using Overviewarr.Sonarr;
using Radzen;
using static Overviewarr.Tests.Coverage.CoverageTestData;

namespace Overviewarr.Tests.Coverage;

public class CombinationFinderTests
{
    // Season 1: episodes 1–3 aired. Season 2: episodes 1–2 aired, episode 3 airs next week. Season 3: no air date yet.
    private static readonly EpisodeDto[] Episodes =
    [
        Episode(1, 1), Episode(1, 2), Episode(1, 3),
        Episode(2, 1), Episode(2, 2), Episode(2, 3, airedDaysAgo: -7),
        Episode(3, 1, airedDaysAgo: null),
    ];

    /// <summary>A release covering episodes of one season, with the episode ids <c>Episode</c> gives them.</summary>
    private static ReleaseRecord Make(
        string guid, int season, int[] episodes, string? group = "GRP", string quality = "WEBDL-1080p",
        string[]? codec = null, string[]? hdr = null, string[]? audioFormat = null, string[]? audioChannels = null,
        string[]? streaming = null, bool seasonPack = false, bool rejected = false, int revision = 1,
        int customFormatScore = 0, int age = 10) =>
        TestReleases.Make(guid, releaseGroup: group, quality: quality, codec: codec, hdr: hdr, audioFormat: audioFormat,
            audioChannels: audioChannels, streaming: streaming, seasonPack: seasonPack, rejected: rejected, revision: revision,
            customFormatScore: customFormatScore, age: age, search: SearchKey.Season(SeriesId, season),
            episodes: episodes.Select(e => (season, e))) with
        {
            Episodes = episodes.Select(e => new ReleaseEpisode(season * 100 + e, season, e)).ToList(),
        };

    private static IReadOnlyList<Combination> Find(params ReleaseRecord[] releases) => CombinationFinder.Find(releases, Episodes, Now);

    private static IEnumerable<string> Guids(IEnumerable<ReleaseRecord> releases) => releases.Select(r => r.Guid);

    /// <summary>The release Select picks for S02E01 among the candidates, with S02E02 covered by another release.</summary>
    private static string PickForFirstEpisode(params ReleaseRecord[] candidates)
    {
        var combination = Assert.Single(Find([.. candidates, Make("s2e2", 2, [2])]));
        return combination.Picks.Single(r => r.Episodes.Any(e => e.EpisodeId == 201)).Guid;
    }

    [Fact]
    public void Releases_with_the_same_values_form_one_combination()
    {
        var combination = Assert.Single(Find(
            Make("e1", 1, [1], group: "NTb", hdr: ["DV Boost", "HDR"]),
            Make("e2", 1, [2], group: "ntb", hdr: ["HDR", "DV Boost"]),
            Make("e3", 1, [3], group: "NTb", hdr: ["DV Boost", "HDR"])));

        Assert.Equal(["e1", "e2", "e3"], Guids(combination.Releases));
        Assert.Equal("S1", combination.SeasonsText);
        Assert.Equal(
            ("NTb", "WEBDL-1080p", "x264", "DV Boost, HDR", "DD+", "5.1 Surround", "None"),
            (combination.ReleaseGroup, combination.Quality, combination.Codec, combination.Hdr, combination.AudioFormat,
                combination.AudioChannels, combination.StreamingService));
    }

    [Theory]
    [InlineData("release group")]
    [InlineData("quality")]
    [InlineData("codec")]
    [InlineData("HDR")]
    [InlineData("audio format")]
    [InlineData("audio channels")]
    [InlineData("streaming service")]
    public void A_different_value_makes_a_different_combination(string attribute)
    {
        var different = attribute switch
        {
            "release group" => Make("e3", 1, [3], group: "OTHER"),
            "quality" => Make("e3", 1, [3], quality: "WEBDL-720p"),
            "codec" => Make("e3", 1, [3], codec: ["x265"]),
            "HDR" => Make("e3", 1, [3], hdr: ["DV Boost", "HDR"]),
            "audio format" => Make("e3", 1, [3], audioFormat: ["AAC"]),
            "audio channels" => Make("e3", 1, [3], audioChannels: ["2.0 Stereo"]),
            _ => Make("e3", 1, [3], streaming: ["AMZN"]),
        };

        Assert.Empty(Find(Make("e1", 1, [1]), Make("e2", 1, [2]), different));
    }

    [Fact]
    public void Only_releases_with_a_release_group_form_combinations() =>
        Assert.Empty(Find(Make("none", 1, [1, 2, 3], group: null), Make("blank", 1, [1, 2, 3], group: " ")));

    [Fact]
    public void A_complete_season_needs_every_aired_episode_with_or_without_a_file()
    {
        EpisodeDto[] episodes =
        [
            Episode(1, 1, fileId: 7), Episode(1, 2),
            Episode(2, 1), Episode(2, 2), Episode(2, 3, airedDaysAgo: -7),
        ];

        var combination = Assert.Single(CombinationFinder.Find([Make("s1e2", 1, [2]), Make("s2", 2, [1, 2])], episodes, Now));

        Assert.Equal("S2 (S1 1/2)", combination.SeasonsText);
    }

    [Fact]
    public void Partly_covered_seasons_follow_the_complete_ones_with_their_counts()
    {
        EpisodeDto[] episodes = [Episode(0, 1), Episode(0, 2), .. Episodes];

        var combination = Assert.Single(CombinationFinder.Find(
            [Make("s0e1", 0, [1]), Make("s1", 1, [1, 2, 3]), Make("s2e1", 2, [1]), Make("s3e1", 3, [1])], episodes, Now));

        Assert.Equal("S1 (S0 1/2) (S2 1/2)", combination.SeasonsText);
        Assert.Equal([(0, 1, 2), (1, 3, 3), (2, 1, 2)], combination.Seasons.Select(s => (s.SeasonNumber, s.Covered, s.Aired)));
    }

    [Fact]
    public void Only_combinations_with_a_complete_season_are_listed_in_order()
    {
        var combinations = Find(
            Make("partial", 1, [1, 2], group: "PARTIAL"),
            Make("beta", 2, [1, 2], group: "BETA"),
            Make("alpha-2160", 2, [1, 2], group: "ALPHA", quality: "WEBDL-2160p"),
            Make("alpha-1080", 2, [1, 2], group: "ALPHA"),
            Make("other-s1", 1, [1, 2, 3], group: "OTHER"),
            Make("other-s2", 2, [1], group: "OTHER"),
            Make("grp-s1", 1, [1, 2, 3]),
            Make("grp-s2", 2, [1, 2]));

        Assert.Equal(
            ["GRP WEBDL-1080p", "OTHER WEBDL-1080p", "ALPHA WEBDL-1080p", "ALPHA WEBDL-2160p", "BETA WEBDL-1080p"],
            combinations.Select(c => $"{c.ReleaseGroup} {c.Quality}"));
    }

    [Fact]
    public void Season_packs_and_single_episodes_form_one_combination()
    {
        var combination = Assert.Single(Find(
            Make("pack-s1", 1, [1, 2, 3], seasonPack: true),
            Make("s2e1", 2, [1]),
            Make("s2e2", 2, [2])));

        Assert.Equal("S1 S2", combination.SeasonsText);
        Assert.Equal(["pack-s1", "s2e1", "s2e2"], Guids(combination.Picks));
    }

    [Fact]
    public void Select_takes_the_season_pack_for_its_whole_season()
    {
        var combination = Assert.Single(Find(
            Make("s1e1", 1, [1], customFormatScore: 100),
            Make("s1e2", 1, [2]),
            Make("s1e3", 1, [3]),
            Make("pack", 1, [1, 2, 3], seasonPack: true)));

        Assert.Equal(["pack"], Guids(combination.Picks));
    }

    [Fact]
    public void Select_picks_one_release_per_episode_of_the_complete_seasons_only()
    {
        var combination = Assert.Single(Find(
            Make("s1e1", 1, [1]), Make("s1e1-again", 1, [1]), Make("s1e2", 1, [2]), Make("s1e3", 1, [3]),
            Make("s2e1", 2, [1])));

        Assert.Equal("S1 (S2 1/2)", combination.SeasonsText);
        Assert.Equal(["s1e1", "s1e2", "s1e3"], Guids(combination.Picks));
    }

    [Fact]
    public void Select_prefers_a_release_that_covers_no_picked_episode()
    {
        var combination = Assert.Single(Find(
            Make("s2e1", 2, [1], customFormatScore: 20),
            Make("double", 2, [1, 2], customFormatScore: 10),
            Make("s2e2", 2, [2])));

        Assert.Equal(["s2e1", "s2e2"], Guids(combination.Picks));
    }

    [Fact]
    public void Select_prefers_a_release_sonarr_did_not_reject() =>
        Assert.Equal("accepted", PickForFirstEpisode(
            Make("rejected", 2, [1], rejected: true, revision: 2, customFormatScore: 100, age: 1),
            Make("accepted", 2, [1])));

    [Fact]
    public void Select_then_prefers_a_proper_or_repack() =>
        Assert.Equal("proper", PickForFirstEpisode(
            Make("original", 2, [1], customFormatScore: 100, age: 1),
            Make("proper", 2, [1], revision: 2)));

    [Fact]
    public void Select_then_prefers_the_highest_custom_format_score() =>
        Assert.Equal("higher", PickForFirstEpisode(
            Make("lower", 2, [1], customFormatScore: 10, age: 1),
            Make("higher", 2, [1], customFormatScore: 20)));

    [Fact]
    public void Select_then_prefers_the_newest() =>
        Assert.Equal("newer", PickForFirstEpisode(
            Make("older", 2, [1], age: 30),
            Make("newer", 2, [1], age: 2)));

    [Fact]
    public void A_release_covering_several_seasons_is_picked_once()
    {
        var multi = Make("multi", 1, [1, 2, 3], seasonPack: true) with
        {
            Episodes = [new(101, 1, 1), new(102, 1, 2), new(103, 1, 3), new(201, 2, 1), new(202, 2, 2)],
        };

        var combination = Assert.Single(Find(multi));

        Assert.Equal("S1 S2", combination.SeasonsText);
        Assert.Equal(["multi"], Guids(combination.Picks));
    }

    [Fact]
    public void Release_group_is_shown_as_most_releases_spell_it()
    {
        Assert.Equal("NTb", Assert.Single(Find(
            Make("a", 1, [1], group: "NTb"), Make("b", 1, [2], group: "NTB"), Make("c", 1, [3], group: "NTb"))).ReleaseGroup);

        // On a tie, the spelling that sorts first.
        Assert.Equal("NTB", Assert.Single(Find(Make("a", 1, [1, 2], group: "NTb"), Make("b", 1, [3], group: "NTB"))).ReleaseGroup);
    }

    [Fact]
    public void The_filter_builder_narrows_the_releases_the_combinations_use()
    {
        ReleaseRecord[] releases = [Make("hd", 1, [1, 2, 3]), Make("sd", 1, [1, 2, 3], quality: "WEBDL-720p")];
        CompositeFilterDescriptor[] filters =
        [
            new() { Property = nameof(ReleaseRecord.Quality), FilterOperator = FilterOperator.Equals, FilterValue = "WEBDL-1080p" },
        ];

        var combination = Assert.Single(CombinationFinder.Find(FilterEvaluator.Apply(releases, filters, LogicalFilterOperator.And), Episodes, Now));

        Assert.Equal(["hd"], Guids(combination.Releases));
    }
}
