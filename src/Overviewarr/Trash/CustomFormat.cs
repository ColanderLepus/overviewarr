using System.Text.RegularExpressions;

namespace Overviewarr.Trash;

/// <summary>A TRaSH Guides custom format (<c>docs/json/sonarr/cf/*.json</c>).</summary>
public sealed record CustomFormat(string TrashId, string Name, string FileName, IReadOnlyList<CustomFormatSpec> Specifications);

/// <summary>One specification of a custom format. <see cref="Regex"/> is set for the regular expression kinds.</summary>
public sealed record CustomFormatSpec(string Name, string Implementation, bool Negate, bool Required, Regex? Regex, int? Number)
{
    public const string ReleaseTitle = "ReleaseTitleSpecification";
    public const string ReleaseGroup = "ReleaseGroupSpecification";
    public const string Source = "SourceSpecification";

    public static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(250);

    /// <summary>Case-insensitive .NET regular expression, as Sonarr uses.</summary>
    public static Regex CreateRegex(string pattern) => new(pattern, RegexOptions.IgnoreCase, RegexTimeout);
}

/// <summary>What a custom format is matched against.</summary>
public sealed record ReleaseInput(string Title, string? ReleaseGroup, string? Source);
