using System.Globalization;
using System.Text;

namespace Overviewarr.Releases;

/// <summary>Formats covered episodes as ranges per season, for example <c>S01E01–E10, S02E01–E08</c>.</summary>
public static class EpisodeRanges
{
    public static string Format(IEnumerable<ReleaseEpisode> episodes)
    {
        var parts = new List<string>();

        foreach (var season in episodes.GroupBy(e => e.SeasonNumber).OrderBy(g => g.Key))
        {
            var numbers = season.Select(e => e.EpisodeNumber).Distinct().Order().ToList();
            var start = 0;

            for (var i = 1; i <= numbers.Count; i++)
            {
                if (i < numbers.Count && numbers[i] == numbers[i - 1] + 1)
                {
                    continue;
                }

                parts.Add(Range(season.Key, numbers[start], numbers[i - 1]));
                start = i;
            }
        }

        return string.Join(", ", parts);
    }

    private static string Range(int season, int first, int last)
    {
        var text = new StringBuilder();
        text.Append('S').Append(Pad(season)).Append('E').Append(Pad(first));
        if (last != first)
        {
            text.Append("–E").Append(Pad(last));
        }

        return text.ToString();
    }

    private static string Pad(int number) => number.ToString("00", CultureInfo.InvariantCulture);
}
