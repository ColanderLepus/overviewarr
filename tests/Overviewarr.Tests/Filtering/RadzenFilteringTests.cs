using System.Collections;
using System.Linq.Dynamic.Core;
using Overviewarr.Releases;
using Radzen;

namespace Overviewarr.Tests.Filtering;

/// <summary>
/// Checks the Radzen 13 behaviour the release grid relies on: checkbox filters on list columns through
/// <c>FilterProperty</c>, their option lists, and the filter builder's own pass over our property types.
/// The filters are built the way the grid builds them from a column.
/// </summary>
public class RadzenFilteringTests
{
    private static readonly IQueryable<ReleaseRecord> Releases = new[]
    {
        TestReleases.Make("a", quality: "WEBDL-1080p", hdr: ["SDR"]),
        TestReleases.Make("b", quality: "WEBDL-2160p", hdr: ["DV Boost", "HDR"]),
        TestReleases.Make("c", quality: "WEBDL-2160p", hdr: ["HDR"]),
        TestReleases.Make("d", quality: "HDTV-720p", hdr: ["SDR"], episodes: []),
    }.AsQueryable();

    /// <summary>The checkbox list's ticked values, converted as <c>RadzenDataGridHeaderCell.ListBoxChange</c> does.</summary>
    private static IList Ticked(Type elementType, params object[] values) =>
        QueryableExtension.ToList(values.AsQueryable().Cast(elementType));

    private static IEnumerable<string> Filter(FilterDescriptor filter) =>
        Releases.Where([filter], LogicalFilterOperator.And, FilterCaseSensitivity.Default).Select(r => r.Guid).ToList();

    [Fact]
    public void Checkbox_filter_on_a_list_column_matches_any_ticked_value()
    {
        var filter = new FilterDescriptor
        {
            Property = nameof(ReleaseRecord.Hdr),
            FilterProperty = nameof(Tag.Name),
            Type = typeof(IEnumerable<Tag>),
            FilterValue = Ticked(typeof(string), "HDR"),
            FilterOperator = FilterOperator.In,
        };

        Assert.Equal(["b", "c"], Filter(filter));
    }

    [Fact]
    public void Checkbox_filter_on_a_list_column_with_several_ticked_values()
    {
        var filter = new FilterDescriptor
        {
            Property = nameof(ReleaseRecord.Hdr),
            FilterProperty = nameof(Tag.Name),
            Type = typeof(IEnumerable<Tag>),
            FilterValue = Ticked(typeof(string), "DV Boost", "SDR"),
            FilterOperator = FilterOperator.In,
        };

        Assert.Equal(["a", "b", "d"], Filter(filter));
    }

    [Fact]
    public void Checkbox_options_for_a_list_column_are_the_single_values()
    {
        var options = QueryableExtension.ToList(Releases.SelectMany(nameof(ReleaseRecord.Hdr)).Select(nameof(Tag.Name)).Distinct())
            .Cast<object>()
            .Select(o => o.ToString());

        Assert.Equal(["DV Boost", "HDR", "SDR"], options.Order());
    }

    [Fact]
    public void Checkbox_filter_on_a_text_column_matches_ticked_values()
    {
        var filter = new FilterDescriptor
        {
            Property = nameof(ReleaseRecord.Quality),
            Type = typeof(IEnumerable<string>),
            FilterValue = Ticked(typeof(string), "WEBDL-1080p", "HDTV-720p"),
            FilterOperator = FilterOperator.Contains,
        };

        Assert.Equal(["a", "d"], Filter(filter));
    }

    [Fact]
    public void Checkbox_filter_on_a_nullable_number_column_matches_ticked_values()
    {
        var filter = new FilterDescriptor
        {
            Property = nameof(ReleaseRecord.SizePerEpisode),
            Type = typeof(IEnumerable<long?>),
            FilterValue = Ticked(typeof(long?), 1024L * 1024 * 1024),
            FilterOperator = FilterOperator.Contains,
        };

        Assert.Equal(["a", "b", "c"], Filter(filter));
    }

    [Fact]
    public void Builder_pass_over_our_property_types_does_not_throw()
    {
        CompositeFilterDescriptor[] filters =
        [
            new() { Property = nameof(ReleaseRecord.HdrText), FilterValue = "HDR", FilterOperator = FilterOperator.DoesNotContain },
            new() { Property = nameof(ReleaseRecord.SizePerEpisodeGb), FilterValue = 2.0, FilterOperator = FilterOperator.LessThan },
            new() { Property = nameof(ReleaseRecord.Resolution), FilterValue = 1080, FilterOperator = FilterOperator.Equals },
            new() { Property = nameof(ReleaseRecord.ProperRepack), FilterValue = false, FilterOperator = FilterOperator.Equals },
            new() { Property = nameof(ReleaseRecord.SeasonText), FilterValue = "1", FilterOperator = FilterOperator.Contains },
        ];

        var exception = Record.Exception(() =>
            Releases.Where(filters, LogicalFilterOperator.And, FilterCaseSensitivity.CaseInsensitive).ToList());

        Assert.Null(exception);
    }
}
