using Overviewarr.Coverage;
using Overviewarr.Releases;
using static Overviewarr.Tests.Coverage.CoverageTestData;

namespace Overviewarr.Tests.Coverage;

public class SeriesViewTests
{
    // Episode 1 is covered by "match", episode 2 only by "other", episode 3 by nothing, episode 4 hasn't aired.
    private static readonly ReleaseRecord Match = Release("match", 1, 1);
    private static readonly ReleaseRecord Other = Release("other", 1, 2);

    private static SeriesView CreateView()
    {
        var view = new SeriesView();
        view.SetEpisodes([Episode(1, 1), Episode(1, 2), Episode(1, 3), Episode(1, 4, airedDaysAgo: -1)], []);

        // The builder keeps both releases; the column filters keep only "match".
        view.Update(Snapshot([SeasonSearched(1)], Match, Other), [Match, Other], [Match], Now);
        return view;
    }

    private static CoverageCell Cell(SeriesView view, int episodeId) =>
        view.Coverage.Rows.SelectMany(r => r.Cells).Single(c => c.EpisodeId == episodeId);

    [Fact]
    public void Covered_cell_toggles_the_episode_filter()
    {
        var view = CreateView();

        view.Click(Cell(view, 101));
        Assert.Equal(new CellHighlight(101, HighlightMode.EpisodeFilter), view.Highlight);
        Assert.Equal(["match"], view.GridData.Select(r => r.Guid));

        view.Click(Cell(view, 101));
        Assert.Null(view.Highlight);
        Assert.Equal(["match", "other"], view.GridData.Select(r => r.Guid));
    }

    [Fact]
    public void Not_covered_cell_toggles_the_gap_view_with_every_covering_release()
    {
        var view = CreateView();

        view.Click(Cell(view, 102));
        Assert.True(view.IsGapView);
        Assert.Equal(["other"], view.GapData.Select(r => r.Guid));
        Assert.Equal(["match", "other"], view.GridData.Select(r => r.Guid));

        view.Click(Cell(view, 102));
        Assert.False(view.IsGapView);
        Assert.Empty(view.GapData);
    }

    [Fact]
    public void Ending_the_gap_view_brings_the_filters_back()
    {
        var view = CreateView();
        view.Click(Cell(view, 102));

        view.EndGapView();

        Assert.Null(view.Highlight);
        Assert.Equal(["match", "other"], view.GridData.Select(r => r.Guid));
    }

    [Fact]
    public void Missing_cell_asks_for_its_episode_search()
    {
        var view = CreateView();

        Assert.True(view.Click(Cell(view, 103)));
        Assert.Equal(new CellHighlight(103, HighlightMode.EpisodeSearch), view.Highlight);
        Assert.Equal(["match", "other"], view.GridData.Select(r => r.Guid));
    }

    [Fact]
    public void Not_aired_cell_does_nothing()
    {
        var view = CreateView();

        Assert.False(view.Click(Cell(view, 104)));
        Assert.Null(view.Highlight);
    }

    [Fact]
    public void Clicking_another_cell_moves_the_highlight()
    {
        var view = CreateView();
        view.Click(Cell(view, 101));

        view.Click(Cell(view, 102));

        Assert.Equal(new CellHighlight(102, HighlightMode.GapView), view.Highlight);
        Assert.Equal(["match", "other"], view.GridData.Select(r => r.Guid));
    }

    [Fact]
    public void Strip_stays_the_same_with_an_episode_filter_or_the_gap_view()
    {
        var view = CreateView();
        var states = view.Coverage.Rows.SelectMany(r => r.Cells).Select(c => (c.EpisodeId, c.State, c.MatchingCount)).ToList();

        view.Click(Cell(view, 101));
        Assert.Equal(states, view.Coverage.Rows.SelectMany(r => r.Cells).Select(c => (c.EpisodeId, c.State, c.MatchingCount)));

        view.Click(Cell(view, 102));
        Assert.Equal(states, view.Coverage.Rows.SelectMany(r => r.Cells).Select(c => (c.EpisodeId, c.State, c.MatchingCount)));
    }

    [Fact]
    public void Episode_filter_keeps_following_new_results()
    {
        var view = CreateView();
        view.Click(Cell(view, 101));
        var newer = Release("newer", 1, 1);

        view.Update(Snapshot([SeasonSearched(1)], Match, Other, newer), [Match, Other, newer], [Match, newer], Now);

        Assert.Equal(["match", "newer"], view.GridData.Select(r => r.Guid));
    }
}
