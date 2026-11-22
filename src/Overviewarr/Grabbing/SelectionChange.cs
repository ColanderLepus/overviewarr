using Overviewarr.Releases;

namespace Overviewarr.Grabbing;

/// <summary>Releases ticked (<paramref name="Selected"/> true) or unticked in a grid.</summary>
public sealed record SelectionChange(IReadOnlyList<ReleaseKey> Keys, bool Selected);
