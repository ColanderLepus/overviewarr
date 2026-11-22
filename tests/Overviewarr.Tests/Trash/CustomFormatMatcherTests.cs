using Overviewarr.Trash;

namespace Overviewarr.Tests.Trash;

/// <summary>Sonarr's custom format rules, with small hand-written custom formats.</summary>
public class CustomFormatMatcherTests
{
    private static CustomFormatSpec TitleSpec(string pattern, bool negate = false, bool required = false) =>
        new("title", CustomFormatSpec.ReleaseTitle, negate, required, CustomFormatSpec.CreateRegex(pattern), null);

    private static CustomFormatSpec GroupSpec(string pattern, bool negate = false, bool required = false) =>
        new("group", CustomFormatSpec.ReleaseGroup, negate, required, CustomFormatSpec.CreateRegex(pattern), null);

    private static CustomFormatSpec SourceSpec(int source, bool negate = false, bool required = false) =>
        new("source", CustomFormatSpec.Source, negate, required, null, source);

    private static CustomFormat Format(params CustomFormatSpec[] specifications) => new("id", "Format", "format", specifications);

    private static readonly ReleaseInput WebRelease = new("Show.S01E01.1080p.WEB-DL.DDP5.1.H.264-GRP", "GRP", "web");

    [Fact]
    public void One_matching_specification_in_a_group_is_enough() =>
        Assert.True(CustomFormatMatcher.IsMatch(Format(TitleSpec(@"\bx265\b"), TitleSpec(@"\bH\.?264\b")), WebRelease));

    [Fact]
    public void A_group_where_nothing_matches_fails() =>
        Assert.False(CustomFormatMatcher.IsMatch(Format(TitleSpec(@"\bx265\b"), TitleSpec(@"\bAV1\b")), WebRelease));

    [Fact]
    public void A_failing_required_specification_fails_its_group() =>
        Assert.False(CustomFormatMatcher.IsMatch(Format(TitleSpec(@"\bH\.?264\b"), TitleSpec(@"\b2160p\b", required: true)), WebRelease));

    [Fact]
    public void Negate_inverts_a_specification()
    {
        Assert.True(CustomFormatMatcher.IsMatch(Format(TitleSpec(@"\bREMUX\b", negate: true)), WebRelease));
        Assert.False(CustomFormatMatcher.IsMatch(Format(TitleSpec(@"\bWEB-DL\b", negate: true)), WebRelease));
    }

    [Fact]
    public void Every_group_must_match()
    {
        Assert.True(CustomFormatMatcher.IsMatch(Format(TitleSpec(@"\bH\.?264\b"), SourceSpec(3)), WebRelease));
        Assert.False(CustomFormatMatcher.IsMatch(Format(TitleSpec(@"\bH\.?264\b"), SourceSpec(6)), WebRelease));
    }

    [Fact]
    public void Release_group_specification_matches_the_release_group()
    {
        Assert.True(CustomFormatMatcher.IsMatch(Format(GroupSpec("^grp$")), WebRelease));
        Assert.False(CustomFormatMatcher.IsMatch(Format(GroupSpec("^grp$")), WebRelease with { ReleaseGroup = null }));
    }

    [Theory]
    [InlineData(0, "unknown")]
    [InlineData(1, "television")]
    [InlineData(2, "televisionRaw")]
    [InlineData(3, "web")]
    [InlineData(4, "webRip")]
    [InlineData(5, "dvd")]
    [InlineData(6, "bluray")]
    [InlineData(7, "blurayRaw")]
    public void Source_numbers_map_to_sonarrs_source_names(int number, string source) =>
        Assert.True(CustomFormatMatcher.IsMatch(Format(SourceSpec(number)), WebRelease with { Source = source }));

    [Fact]
    public void Unknown_implementation_does_not_match() =>
        Assert.False(CustomFormatMatcher.IsMatch(
            Format(new CustomFormatSpec("resolution", "ResolutionSpecification", false, false, null, 1080)), WebRelease));
}
