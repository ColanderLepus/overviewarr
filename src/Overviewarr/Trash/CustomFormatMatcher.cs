using System.Text.RegularExpressions;

namespace Overviewarr.Trash;

/// <summary>
/// Sonarr's custom format rules (<c>CustomFormatCalculationService.cs</c>): specifications are grouped by
/// implementation and inverted when negated; a group matches when every required specification matches and at
/// least one matches; the format matches when every group matches.
/// </summary>
public static class CustomFormatMatcher
{
    /// <summary>Sonarr's <c>QualitySource</c> numbers, as the API names them.</summary>
    private static readonly string[] QualitySources =
        ["unknown", "television", "televisionRaw", "web", "webRip", "dvd", "bluray", "blurayRaw"];

    public static bool IsMatch(CustomFormat format, ReleaseInput input)
    {
        foreach (var group in format.Specifications.GroupBy(s => s.Implementation, StringComparer.Ordinal))
        {
            var anyMatched = false;

            foreach (var specification in group)
            {
                var matched = Evaluate(specification, input);
                if (specification.Negate)
                {
                    matched = !matched;
                }

                if (specification.Required && !matched)
                {
                    return false;
                }

                anyMatched |= matched;
            }

            if (!anyMatched)
            {
                return false;
            }
        }

        return true;
    }

    internal static string? SourceName(int number) =>
        number >= 0 && number < QualitySources.Length ? QualitySources[number] : null;

    private static bool Evaluate(CustomFormatSpec specification, ReleaseInput input) => specification.Implementation switch
    {
        CustomFormatSpec.ReleaseTitle => IsRegexMatch(specification.Regex, input.Title),
        CustomFormatSpec.ReleaseGroup => IsRegexMatch(specification.Regex, input.ReleaseGroup),
        CustomFormatSpec.Source => specification.Number is int number &&
                                   string.Equals(SourceName(number), input.Source, StringComparison.OrdinalIgnoreCase),
        _ => false,
    };

    private static bool IsRegexMatch(Regex? regex, string? value)
    {
        if (regex is null || value is null)
        {
            return false;
        }

        try
        {
            return regex.IsMatch(value);
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }
}
