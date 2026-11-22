using Overviewarr.Releases;
using Radzen;

namespace Overviewarr.Filtering;

public enum FilterKind
{
    Text,
    Number,
    YesNo,
    TextList,
    NumberList,
}

/// <summary>
/// A filterable property in the filter builder (DESIGN.md section 8). <see cref="Property"/> names the
/// <see cref="ReleaseRecord"/> property Radzen's builder edits; <see cref="Type"/> is that property's type.
/// List properties are edited through their text form and evaluated as lists.
/// </summary>
public sealed record FilterField(string Title, string Property, FilterKind Kind, Type Type, Func<ReleaseRecord, object?> Value)
{
    private static readonly FilterOperator[] TextOperators =
    [
        FilterOperator.Equals, FilterOperator.NotEquals, FilterOperator.Contains, FilterOperator.DoesNotContain,
        FilterOperator.StartsWith, FilterOperator.EndsWith,
    ];

    private static readonly FilterOperator[] NumberOperators =
    [
        FilterOperator.Equals, FilterOperator.NotEquals, FilterOperator.LessThan, FilterOperator.LessThanOrEquals,
        FilterOperator.GreaterThan, FilterOperator.GreaterThanOrEquals,
    ];

    private static readonly FilterOperator[] YesNoOperators = [FilterOperator.Equals];

    private static readonly FilterOperator[] ListOperators = [FilterOperator.Contains, FilterOperator.DoesNotContain];

    public IReadOnlyList<FilterOperator> Operators => Kind switch
    {
        FilterKind.Text => TextOperators,
        FilterKind.Number => NumberOperators,
        FilterKind.YesNo => YesNoOperators,
        _ => ListOperators,
    };
}

public static class FilterFields
{
    public static IReadOnlyList<FilterField> All { get; } =
    [
        new("Season", nameof(ReleaseRecord.SeasonText), FilterKind.NumberList, typeof(string), r => r.Seasons.Select(s => s.Number)),
        new("Season pack", nameof(ReleaseRecord.SeasonPack), FilterKind.YesNo, typeof(bool), r => r.SeasonPack),
        new("Release group", nameof(ReleaseRecord.ReleaseGroup), FilterKind.Text, typeof(string), r => r.ReleaseGroup),
        new("Quality", nameof(ReleaseRecord.Quality), FilterKind.Text, typeof(string), r => r.Quality),
        new("Source", nameof(ReleaseRecord.Source), FilterKind.Text, typeof(string), r => r.Source),
        new("Resolution", nameof(ReleaseRecord.Resolution), FilterKind.Number, typeof(int), r => r.Resolution),
        new("Codec", nameof(ReleaseRecord.CodecText), FilterKind.TextList, typeof(string), r => Names(r.Codec)),
        new("HDR", nameof(ReleaseRecord.HdrText), FilterKind.TextList, typeof(string), r => Names(r.Hdr)),
        new("Audio format", nameof(ReleaseRecord.AudioFormatText), FilterKind.TextList, typeof(string), r => Names(r.AudioFormat)),
        new("Audio channels", nameof(ReleaseRecord.AudioChannelsText), FilterKind.TextList, typeof(string), r => Names(r.AudioChannels)),
        new("Streaming service", nameof(ReleaseRecord.StreamingServiceText), FilterKind.TextList, typeof(string), r => Names(r.StreamingService)),
        new("Languages", nameof(ReleaseRecord.LanguagesText), FilterKind.TextList, typeof(string), r => Names(r.Languages)),
        new("Custom formats", nameof(ReleaseRecord.CustomFormatsText), FilterKind.TextList, typeof(string), r => Names(r.CustomFormats)),
        new("Custom format score", nameof(ReleaseRecord.CustomFormatScore), FilterKind.Number, typeof(int), r => r.CustomFormatScore),
        new("Indexer", nameof(ReleaseRecord.Indexer), FilterKind.Text, typeof(string), r => r.Indexer),
        new("Size (GB)", nameof(ReleaseRecord.SizeGb), FilterKind.Number, typeof(double), r => r.SizeGb),
        new("Size per episode (GB)", nameof(ReleaseRecord.SizePerEpisodeGb), FilterKind.Number, typeof(double?), r => r.SizePerEpisodeGb),
        new("Age (days)", nameof(ReleaseRecord.Age), FilterKind.Number, typeof(int), r => r.Age),
        new("Proper/repack", nameof(ReleaseRecord.ProperRepack), FilterKind.YesNo, typeof(bool), r => r.ProperRepack),
        new("Rejected by Sonarr", nameof(ReleaseRecord.Rejected), FilterKind.YesNo, typeof(bool), r => r.Rejected),
        new("Title", nameof(ReleaseRecord.Title), FilterKind.Text, typeof(string), r => r.Title),
    ];

    private static readonly Dictionary<string, FilterField> ByProperty = All.ToDictionary(f => f.Property, StringComparer.Ordinal);

    public static FilterField? Find(string? property) =>
        property is not null && ByProperty.TryGetValue(property, out var field) ? field : null;

    private static IEnumerable<string> Names(IReadOnlyList<Tag> tags) => tags.Select(t => t.Name);
}
