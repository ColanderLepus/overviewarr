using System.Globalization;

namespace Overviewarr.Releases;

/// <summary>
/// The display formats from DESIGN.md section 8. Used as a Radzen column <c>FormatProvider</c> with the format
/// strings <c>{0:GB}</c> (bytes), <c>{0:days}</c> and <c>{0:yesno}</c>, so cells and checkbox filters agree.
/// </summary>
public sealed class DisplayFormat : IFormatProvider, ICustomFormatter
{
    public static DisplayFormat Instance { get; } = new();

    public object? GetFormat(Type? formatType) => formatType == typeof(ICustomFormatter) ? this : null;

    public string Format(string? format, object? arg, IFormatProvider? formatProvider) => (format, arg) switch
    {
        (_, null) => string.Empty,
        ("GB", long bytes) => Gigabytes(bytes),
        ("days", int days) => Days(days),
        ("yesno", bool value) => value ? "Yes" : "No",
        (_, IFormattable formattable) => formattable.ToString(format, CultureInfo.CurrentCulture),
        _ => arg.ToString() ?? string.Empty,
    };

    /// <summary>Gigabytes with 2 decimals, where 1 GB is 1024³ bytes.</summary>
    public static string Gigabytes(long bytes) =>
        (bytes / (1024d * 1024d * 1024d)).ToString("0.00", CultureInfo.CurrentCulture) + " GB";

    public static string Days(int days) => days == 1 ? "1 day" : $"{days} days";

    /// <summary><c>HH:mm</c> for today, otherwise <c>yyyy-MM-dd HH:mm</c>, in the local time zone.</summary>
    public static string Time(DateTimeOffset time, DateTimeOffset now, TimeZoneInfo? timeZone = null)
    {
        timeZone ??= TimeZoneInfo.Local;
        var local = TimeZoneInfo.ConvertTime(time, timeZone);
        var today = TimeZoneInfo.ConvertTime(now, timeZone);

        return local.Date == today.Date
            ? local.ToString("HH:mm", CultureInfo.InvariantCulture)
            : local.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
    }
}
