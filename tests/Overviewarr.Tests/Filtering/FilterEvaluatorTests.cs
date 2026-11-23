using Overviewarr.Filtering;
using Overviewarr.Releases;
using Radzen;

namespace Overviewarr.Tests.Filtering;

public class FilterEvaluatorTests
{
    private static readonly ReleaseRecord[] Releases =
    [
        TestReleases.Make("a", releaseGroup: "NTb", resolution: 1080, size: 2L * 1024 * 1024 * 1024, codec: ["x264"], hdr: ["SDR"], episodes: [(1, 1), (1, 2)]),
        TestReleases.Make("b", releaseGroup: "FLUX", resolution: 2160, codec: ["x265"], hdr: ["DV Boost", "HDR"], episodes: [(2, 1)]),
        TestReleases.Make("c", releaseGroup: null, resolution: 720, codec: ["x265"], hdr: ["HDR"], episodes: []),
    ];

    private static IEnumerable<string> Apply(LogicalFilterOperator op, params CompositeFilterDescriptor[] filters) =>
        FilterEvaluator.Apply(Releases, filters, op).Select(r => r.Guid);

    private static CompositeFilterDescriptor Condition(string property, FilterOperator op, object? value) =>
        new() { Property = property, FilterOperator = op, FilterValue = value };

    [Fact]
    public void No_conditions_keep_everything() =>
        Assert.Equal(["a", "b", "c"], Apply(LogicalFilterOperator.And));

    [Theory]
    [InlineData(FilterOperator.Equals, "ntb", new[] { "a" })]
    [InlineData(FilterOperator.NotEquals, "NTb", new[] { "b", "c" })]
    [InlineData(FilterOperator.Contains, "lu", new[] { "b" })]
    [InlineData(FilterOperator.DoesNotContain, "lu", new[] { "a", "c" })]
    [InlineData(FilterOperator.StartsWith, "fl", new[] { "b" })]
    [InlineData(FilterOperator.EndsWith, "TB", new[] { "a" })]
    public void Text_conditions_ignore_case(FilterOperator op, string value, string[] expected) =>
        Assert.Equal(expected, Apply(LogicalFilterOperator.And, Condition(nameof(ReleaseRecord.ReleaseGroup), op, value)));

    [Theory]
    [InlineData(FilterOperator.Equals, 1080, new[] { "a" })]
    [InlineData(FilterOperator.NotEquals, 1080, new[] { "b", "c" })]
    [InlineData(FilterOperator.LessThan, 1080, new[] { "c" })]
    [InlineData(FilterOperator.LessThanOrEquals, 1080, new[] { "a", "c" })]
    [InlineData(FilterOperator.GreaterThan, 1080, new[] { "b" })]
    [InlineData(FilterOperator.GreaterThanOrEquals, 1080, new[] { "a", "b" })]
    public void Number_conditions_compare(FilterOperator op, int value, string[] expected) =>
        Assert.Equal(expected, Apply(LogicalFilterOperator.And, Condition(nameof(ReleaseRecord.Resolution), op, value)));

    [Fact]
    public void Empty_size_per_episode_never_matches_a_number_condition()
    {
        // a: 2 GB over 2 episodes, b: 1 GB over 1 episode, c: no matched episodes.
        Assert.Equal(["a", "b"], Apply(LogicalFilterOperator.And, Condition(nameof(ReleaseRecord.SizePerEpisodeGb), FilterOperator.LessThan, 1.5)));
        Assert.Equal(["a", "b"], Apply(LogicalFilterOperator.And, Condition(nameof(ReleaseRecord.SizePerEpisodeGb), FilterOperator.NotEquals, 5.0)));
    }

    [Fact]
    public void Yes_no_condition_compares()
    {
        ReleaseRecord[] releases = [TestReleases.Make("repack", revision: 2), TestReleases.Make("original", revision: 1)];

        var repacks = FilterEvaluator.Apply(releases, [Condition(nameof(ReleaseRecord.ProperRepack), FilterOperator.Equals, true)], LogicalFilterOperator.And);
        var originals = FilterEvaluator.Apply(releases, [Condition(nameof(ReleaseRecord.ProperRepack), FilterOperator.Equals, false)], LogicalFilterOperator.And);

        Assert.Equal(["repack"], repacks.Select(r => r.Guid));
        Assert.Equal(["original"], originals.Select(r => r.Guid));
    }

    [Fact]
    public void List_contains_when_one_value_equals()
    {
        Assert.Equal(["b", "c"], Apply(LogicalFilterOperator.And, Condition(nameof(ReleaseRecord.HdrText), FilterOperator.Contains, "hdr")));
        Assert.Equal(["b"], Apply(LogicalFilterOperator.And, Condition(nameof(ReleaseRecord.HdrText), FilterOperator.Contains, "DV Boost")));
    }

    [Fact]
    public void List_does_not_contain_when_no_value_equals() =>
        Assert.Equal(["a"], Apply(LogicalFilterOperator.And, Condition(nameof(ReleaseRecord.HdrText), FilterOperator.DoesNotContain, "HDR")));

    [Fact]
    public void List_values_are_whole_values() =>
        Assert.Empty(Apply(LogicalFilterOperator.And, Condition(nameof(ReleaseRecord.CodecText), FilterOperator.Contains, "x26")));

    [Fact]
    public void Season_list_compares_numbers()
    {
        Assert.Equal(["b"], Apply(LogicalFilterOperator.And, Condition(nameof(ReleaseRecord.SeasonText), FilterOperator.Contains, "2")));
        Assert.Equal(["a", "c"], Apply(LogicalFilterOperator.And, Condition(nameof(ReleaseRecord.SeasonText), FilterOperator.DoesNotContain, "2")));
    }

    [Fact]
    public void Nested_groups_combine_with_and_and_or()
    {
        var group = new CompositeFilterDescriptor
        {
            LogicalFilterOperator = LogicalFilterOperator.Or,
            Filters =
            [
                Condition(nameof(ReleaseRecord.ReleaseGroup), FilterOperator.Equals, "NTb"),
                Condition(nameof(ReleaseRecord.Resolution), FilterOperator.Equals, 720),
            ],
        };

        Assert.Equal(["a", "c"], Apply(LogicalFilterOperator.And, group));
        Assert.Equal(["c"], Apply(LogicalFilterOperator.And, group, Condition(nameof(ReleaseRecord.CodecText), FilterOperator.Contains, "x265")));
        Assert.Equal(["a", "b", "c"], Apply(LogicalFilterOperator.Or, group, Condition(nameof(ReleaseRecord.CodecText), FilterOperator.Contains, "x265")));
    }

    [Fact]
    public void Conditions_without_a_value_and_empty_groups_are_ignored() =>
        Assert.Equal(["a", "b", "c"], Apply(LogicalFilterOperator.And,
            Condition(nameof(ReleaseRecord.ReleaseGroup), FilterOperator.Equals, ""),
            Condition(nameof(ReleaseRecord.Resolution), FilterOperator.Equals, null),
            new CompositeFilterDescriptor { LogicalFilterOperator = LogicalFilterOperator.Or, Filters = [] }));
}
