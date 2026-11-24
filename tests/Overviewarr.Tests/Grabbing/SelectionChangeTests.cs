using Overviewarr.Grabbing;
using Overviewarr.Releases;

namespace Overviewarr.Tests.Grabbing;

public class SelectionChangeTests
{
    private static readonly ReleaseKey A = new(1, "a");
    private static readonly ReleaseKey B = new(1, "b");
    private static readonly ReleaseKey C = new(2, "c");

    [Fact]
    public void Adding_keeps_the_releases_already_selected()
    {
        var selection = new HashSet<ReleaseKey> { A };

        new SelectionChange([B, C], Selected: true).ApplyTo(selection);

        Assert.Equal(new HashSet<ReleaseKey> { A, B, C }, selection);
    }

    [Fact]
    public void Removing_keeps_the_other_releases()
    {
        var selection = new HashSet<ReleaseKey> { A, B, C };

        new SelectionChange([B], Selected: false).ApplyTo(selection);

        Assert.Equal(new HashSet<ReleaseKey> { A, C }, selection);
    }
}
