using Overviewarr.Releases;

namespace Overviewarr.Grabbing;

/// <summary>
/// Releases added to the selection (<paramref name="Selected"/> true) or removed from it: ticked or unticked in a grid,
/// or added by a combination's Select button.
/// </summary>
public sealed record SelectionChange(IReadOnlyList<ReleaseKey> Keys, bool Selected)
{
    /// <summary>Adds the keys to the selection, or removes them from it. The other selected keys stay.</summary>
    public void ApplyTo(ISet<ReleaseKey> selection)
    {
        if (Selected)
        {
            selection.UnionWith(Keys);
        }
        else
        {
            selection.ExceptWith(Keys);
        }
    }
}
