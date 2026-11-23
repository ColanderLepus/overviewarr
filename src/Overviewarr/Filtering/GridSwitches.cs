using Overviewarr.Releases;

namespace Overviewarr.Filtering;

/// <summary>
/// The switches in the release grid's header bar (DESIGN.md section 8, Grid switches): season packs or single
/// episodes, never both, and whether releases with a quality the profile doesn't want are shown.
/// </summary>
public sealed record GridSwitches(bool SeasonPacks = false, bool ShowUnwantedQualities = false)
{
    /// <summary>The switches when a series page opens: Single episodes, and unwanted qualities hidden.</summary>
    public static GridSwitches Starting { get; } = new();

    /// <summary>
    /// The releases the switches let through. Season packs can be chosen only while one of the releases that
    /// <see cref="ShowUnwantedQualities"/> lets through is a season pack; without one, the switches go back to Single episodes.
    /// </summary>
    public GridSwitchResult Apply(IReadOnlyList<ReleaseRecord> releases)
    {
        var allowed = ShowUnwantedQualities ? releases : releases.Where(r => !r.QualityNotWanted).ToList();
        var seasonPacksAvailable = allowed.Any(r => r.SeasonPack);
        var switches = SeasonPacks && !seasonPacksAvailable ? this with { SeasonPacks = false } : this;

        return new GridSwitchResult(switches, seasonPacksAvailable, allowed.Where(r => r.SeasonPack == switches.SeasonPacks).ToList());
    }
}

/// <summary>The switches in effect, whether Season packs can be chosen, and the releases the switches let through.</summary>
public sealed record GridSwitchResult(GridSwitches Switches, bool SeasonPacksAvailable, IReadOnlyList<ReleaseRecord> Releases);
