using Overviewarr.Grabbing;
using Overviewarr.Sonarr;
using static Overviewarr.Tests.Coverage.CoverageTestData;

namespace Overviewarr.Tests.Grabbing;

public class ConfirmationWarningsTests
{
    [Fact]
    public void Episode_in_more_than_one_selected_release_is_an_overlap()
    {
        var pack = Release("pack", 1, 1, 2, 3);
        var single = Release("single", 1, 2);

        var warnings = ConfirmationWarnings.Calculate([pack, single], [Episode(1, 1), Episode(1, 2), Episode(1, 3)],
            Snapshot([SeasonSearched(1)]), new HashSet<int>(), Now);

        var overlap = Assert.Single(warnings.Overlaps);
        Assert.Equal("S01E02", overlap.Label);
        Assert.Equal(["pack", "single"], overlap.Releases.Select(r => r.Guid));
    }

    [Fact]
    public void Gap_is_an_aired_episode_without_a_file_a_queue_record_or_a_selected_release()
    {
        EpisodeDto[] episodes =
        [
            Episode(1, 1),                    // selected
            Episode(1, 2, fileId: 9),         // has a file
            Episode(1, 3),                    // in the queue
            Episode(1, 4, airedDaysAgo: -3),  // not aired
            Episode(1, 5),                    // a gap
            Episode(2, 1),                    // season not searched
        ];

        var warnings = ConfirmationWarnings.Calculate([Release("a", 1, 1)], episodes, Snapshot([SeasonSearched(1)]),
            new HashSet<int> { 103 }, Now);

        Assert.Equal([105], warnings.Gaps.Select(e => e.Id));
        Assert.Empty(warnings.Overlaps);
    }
}
