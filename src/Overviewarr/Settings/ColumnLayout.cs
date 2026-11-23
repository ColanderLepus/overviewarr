using System.Text.RegularExpressions;

namespace Overviewarr.Settings;

/// <summary>One column of the release grid's layout (DESIGN.md section 8): its title, whether it's shown, and its width.</summary>
public sealed record ColumnLayout(string Title, bool Visible, string? Width);

public static partial class ColumnLayouts
{
    /// <summary>
    /// Applies a saved layout to the grid's columns. Saved columns come first, in their saved order; the other columns
    /// follow in their default order with their defaults. Unknown titles, repeated titles and invalid widths are ignored.
    /// </summary>
    public static IReadOnlyList<ColumnLayout> Resolve(IReadOnlyList<ColumnLayout> defaults, IReadOnlyList<ColumnLayout>? saved)
    {
        if (saved is null)
        {
            return defaults;
        }

        var byTitle = defaults.ToDictionary(c => c.Title, StringComparer.Ordinal);
        var used = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<ColumnLayout>(defaults.Count);

        foreach (var column in saved)
        {
            if (!byTitle.TryGetValue(column.Title, out var fallback) || !used.Add(column.Title))
            {
                continue;
            }

            result.Add(new ColumnLayout(column.Title, column.Visible, IsPixelWidth(column.Width) ? column.Width : fallback.Width));
        }

        result.AddRange(defaults.Where(c => !used.Contains(c.Title)));
        return result;
    }

    /// <summary>A width such as <c>120px</c> or <c>120.5px</c>.</summary>
    public static bool IsPixelWidth(string? width) => width is not null && PixelWidth().IsMatch(width);

    [GeneratedRegex(@"^\d+(\.\d+)?px$", RegexOptions.CultureInvariant)]
    private static partial Regex PixelWidth();
}
