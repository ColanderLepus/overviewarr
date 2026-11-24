using Overviewarr.Filtering;
using Overviewarr.Releases;

namespace Overviewarr.Tests.Filtering;

public class GridSwitchesTests
{
    /// <summary>Sonarr's rejection when the series' quality profile doesn't allow the release's quality.</summary>
    private const string UnwantedQuality = "HDTV-720p is not wanted in profile";

    private const string ExistingFile = "Existing file on disk is of equal or higher preference: Bluray-1080p";

    private static ReleaseRecord Single(string guid, params string[] rejections) =>
        TestReleases.Make(guid, rejected: rejections.Length > 0) with { Rejections = rejections };

    private static ReleaseRecord Pack(string guid, params string[] rejections) =>
        TestReleases.Make(guid, seasonPack: true, rejected: rejections.Length > 0, episodes: [(1, 1), (1, 2)]) with { Rejections = rejections };

    private static readonly ReleaseRecord[] Releases =
    [
        Single("single"),
        Single("single-rejected", ExistingFile),
        Single("single-unwanted", ExistingFile, UnwantedQuality),
        Pack("pack"),
        Pack("pack-unwanted", UnwantedQuality),
    ];

    private static IEnumerable<string> Guids(GridSwitchResult result) => result.Releases.Select(r => r.Guid);

    [Fact]
    public void Starting_switches_show_single_episodes_without_unwanted_qualities()
    {
        var result = GridSwitches.Starting.Apply(Releases);

        Assert.Equal(new GridSwitches(SeasonPacks: false, ShowUnwantedQualities: false), result.Switches);
        Assert.Equal(["single", "single-rejected"], Guids(result));
    }

    [Fact]
    public void Season_packs_show_only_season_packs()
    {
        var result = new GridSwitches(SeasonPacks: true).Apply(Releases);

        Assert.True(result.Switches.SeasonPacks);
        Assert.True(result.SeasonPacksAvailable);
        Assert.Equal(["pack"], Guids(result));
    }

    [Fact]
    public void Show_unwanted_qualities_lets_them_through()
    {
        Assert.Equal(["single", "single-rejected", "single-unwanted"], Guids(new GridSwitches(ShowUnwantedQualities: true).Apply(Releases)));
        Assert.Equal(["pack", "pack-unwanted"], Guids(new GridSwitches(SeasonPacks: true, ShowUnwantedQualities: true).Apply(Releases)));
    }

    [Fact]
    public void Only_a_rejection_ending_with_the_profile_reason_is_an_unwanted_quality()
    {
        Assert.True(Single("unwanted", UnwantedQuality).QualityNotWanted);
        Assert.False(Single("mentioned", "is not wanted in profile, says the test").QualityNotWanted);
        Assert.False(Single("rejected", ExistingFile).QualityNotWanted);
    }

    [Fact]
    public void Season_packs_are_unavailable_without_a_season_pack()
    {
        ReleaseRecord[] singles = [Single("single"), Single("single-rejected", ExistingFile)];

        var result = new GridSwitches(SeasonPacks: true).Apply(singles);

        Assert.False(result.SeasonPacksAvailable);
        Assert.False(result.Switches.SeasonPacks);
        Assert.Equal(["single", "single-rejected"], Guids(result));
    }

    [Fact]
    public void A_season_pack_with_an_unwanted_quality_counts_only_while_unwanted_qualities_are_shown()
    {
        ReleaseRecord[] releases = [Single("single"), Pack("pack-unwanted", UnwantedQuality)];

        var shown = new GridSwitches(SeasonPacks: true, ShowUnwantedQualities: true).Apply(releases);
        Assert.True(shown.SeasonPacksAvailable);
        Assert.Equal(["pack-unwanted"], Guids(shown));

        var hidden = (shown.Switches with { ShowUnwantedQualities = false }).Apply(releases);
        Assert.False(hidden.SeasonPacksAvailable);
        Assert.Equal(new GridSwitches(SeasonPacks: false, ShowUnwantedQualities: false), hidden.Switches);
        Assert.Equal(["single"], Guids(hidden));
    }

    [Fact]
    public void Season_packs_stay_chosen_while_a_season_pack_is_left()
    {
        var result = new GridSwitches(SeasonPacks: true).Apply([Single("single"), Pack("pack")]);

        Assert.True(result.Switches.SeasonPacks);
    }

    [Fact]
    public void All_kinds_has_season_packs_and_single_episodes_whichever_the_select_bar_shows()
    {
        Assert.Equal(["single", "single-rejected", "pack"], GridSwitches.Starting.Apply(Releases).AllKinds.Select(r => r.Guid));
        Assert.Equal(["single", "single-rejected", "pack"], new GridSwitches(SeasonPacks: true).Apply(Releases).AllKinds.Select(r => r.Guid));
        Assert.Equal(
            ["single", "single-rejected", "single-unwanted", "pack", "pack-unwanted"],
            new GridSwitches(ShowUnwantedQualities: true).Apply(Releases).AllKinds.Select(r => r.Guid));
    }
}
