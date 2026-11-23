using System.Linq.Dynamic.Core;
using Overviewarr.Filtering;
using Overviewarr.Releases;
using Radzen;

namespace Overviewarr.Tests.Releases;

public class ReleaseStatusTests
{
    private static readonly ReleaseFailure Failure = new("Download failed", DateTimeOffset.UnixEpoch);

    private static readonly ReleaseRecord[] Releases =
    [
        TestReleases.Make("failed") with { Failure = Failure },
        TestReleases.Make("failed-and-rejected", rejected: true) with { Failure = Failure },
        TestReleases.Make("rejected", rejected: true),
        TestReleases.Make("ok"),
    ];

    [Fact]
    public void Download_failed_comes_before_rejected_by_sonarr() =>
        Assert.Equal(
            [ReleaseStatus.DownloadFailed, ReleaseStatus.DownloadFailed, ReleaseStatus.RejectedBySonarr, ReleaseStatus.None],
            Releases.Select(r => r.Status));

    [Fact]
    public void Unwanted_quality_is_rejected_by_sonarr()
    {
        var release = TestReleases.Make("unwanted", rejected: true) with { Rejections = ["HDTV-720p is not wanted in profile"] };

        Assert.Equal(ReleaseStatus.RejectedBySonarr, release.Status);
    }

    /// <summary>The Status column's starting filter, applied the way the grid applies a checkbox filter on a text column.</summary>
    [Fact]
    public void Starting_filter_hides_failed_releases_and_keeps_the_others()
    {
        var filter = new FilterDescriptor
        {
            Property = nameof(ReleaseRecord.Status),
            Type = typeof(IEnumerable<string>),
            FilterValue = ReleaseStatus.StartingFilter(),
            FilterOperator = FilterOperator.Contains,
        };

        var shown = Releases.AsQueryable().Where([filter], LogicalFilterOperator.And, FilterCaseSensitivity.Default).Select(r => r.Guid).ToList();

        Assert.Equal(["rejected", "ok"], shown);
    }

    [Fact]
    public void Starting_filter_is_a_new_list_each_time() =>
        Assert.NotSame(ReleaseStatus.StartingFilter(), ReleaseStatus.StartingFilter());

    [Fact]
    public void Filter_builder_can_leave_out_failed_releases()
    {
        CompositeFilterDescriptor[] filters =
        [
            new() { Property = nameof(ReleaseRecord.Status), FilterOperator = FilterOperator.NotEquals, FilterValue = "download failed" },
        ];

        Assert.Equal(["rejected", "ok"], FilterEvaluator.Apply(Releases, filters, LogicalFilterOperator.And).Select(r => r.Guid));
    }
}
