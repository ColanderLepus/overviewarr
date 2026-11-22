using System.Text.Json;

namespace Overviewarr.Trash;

/// <summary>The attributes taken from TRaSH Guides custom formats (DESIGN.md section 7).</summary>
public enum TrashAttribute
{
    Codec,
    Hdr,
    AudioFormat,
    AudioChannels,
    StreamingService,
}

/// <summary>The TRaSH Guides definitions, loaded once at startup.</summary>
public sealed class TrashDefinitions
{
    private static readonly (TrashAttribute Attribute, string[] Files, string[] Groups)[] Sources =
    [
        (TrashAttribute.Codec, ["x264", "x265", "x266", "av1", "vp9", "vc-1", "mpeg2"], []),
        (TrashAttribute.Hdr, ["dv-boost", "hdr10plus-boost", "hlg", "hdr"], []),
        (TrashAttribute.AudioFormat, [], ["audio-formats"]),
        (TrashAttribute.AudioChannels, [], ["audio-channels"]),
        (TrashAttribute.StreamingService, [],
        [
            "streaming-services-general", "streaming-services-uk", "streaming-services-dutch", "streaming-services-french",
            "streaming-services-asian", "streaming-services-anime", "streaming-services-miscellaneous",
        ]),
    ];

    private readonly IReadOnlyDictionary<TrashAttribute, IReadOnlyList<CustomFormat>> _formats;

    private TrashDefinitions(IReadOnlyDictionary<TrashAttribute, IReadOnlyList<CustomFormat>> formats) => _formats = formats;

    /// <summary>Definitions from custom formats built in code. Attributes without formats match nothing.</summary>
    internal static TrashDefinitions FromFormats(IReadOnlyDictionary<TrashAttribute, IReadOnlyList<CustomFormat>> formats) =>
        new(Enum.GetValues<TrashAttribute>().ToDictionary(a => a, a => formats.GetValueOrDefault(a) ?? []));

    /// <summary>The custom formats behind an attribute, in the order their names are listed.</summary>
    public IReadOnlyList<CustomFormat> FormatsFor(TrashAttribute attribute) => _formats[attribute];

    /// <summary>The names of every matching custom format of an attribute.</summary>
    public IReadOnlyList<string> Match(TrashAttribute attribute, ReleaseInput input) =>
        _formats[attribute].Where(f => CustomFormatMatcher.IsMatch(f, input)).Select(f => f.Name).ToList();

    /// <summary>Loads the definitions from a directory holding <c>cf/</c> and <c>cf-groups/</c>.</summary>
    /// <exception cref="InvalidDataException">The definitions are missing or can't be read.</exception>
    public static TrashDefinitions Load(string directory, ILogger? logger = null)
    {
        var cfDirectory = Path.Combine(directory, "cf");
        var groupDirectory = Path.Combine(directory, "cf-groups");

        if (!Directory.Exists(cfDirectory) || !Directory.Exists(groupDirectory))
        {
            throw new InvalidDataException($"The TRaSH Guides definitions weren't found in {directory}.");
        }

        // Every custom format file is read, so groups can refer to them by trash_id. A file that can't be read
        // only matters when an attribute needs it.
        var byFile = new Dictionary<string, (string FileName, JsonElement Json)>(StringComparer.OrdinalIgnoreCase);
        var byTrashId = new Dictionary<string, (string FileName, JsonElement Json)>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in Directory.GetFiles(cfDirectory, "*.json"))
        {
            var fileName = Path.GetFileNameWithoutExtension(path);
            try
            {
                var document = (fileName, ReadJson(path));
                byFile[fileName] = document;
                if (document.Item2.ValueKind == JsonValueKind.Object &&
                    document.Item2.TryGetProperty("trash_id", out var trashId) && trashId.ValueKind == JsonValueKind.String)
                {
                    byTrashId.TryAdd(trashId.GetString()!, document);
                }
            }
            catch (InvalidDataException exception)
            {
                logger?.LogWarning("{Error}", exception.Message);
            }
        }

        var formats = new Dictionary<TrashAttribute, IReadOnlyList<CustomFormat>>();
        foreach (var (attribute, files, groups) in Sources)
        {
            var selected = new List<(string FileName, JsonElement Json)>();

            foreach (var file in files)
            {
                selected.Add(byFile.TryGetValue(file, out var document)
                    ? document
                    : throw new InvalidDataException($"The TRaSH Guides custom format cf/{file}.json is missing."));
            }

            foreach (var group in groups)
            {
                var groupPath = Path.Combine(groupDirectory, group + ".json");
                if (!File.Exists(groupPath))
                {
                    throw new InvalidDataException($"The TRaSH Guides group cf-groups/{group}.json is missing.");
                }

                foreach (var member in GetArray(ReadJson(groupPath), "custom_formats", group))
                {
                    var trashId = GetString(member, "trash_id", group);
                    selected.Add(byTrashId.TryGetValue(trashId, out var document)
                        ? document
                        : throw new InvalidDataException($"Group {group} lists the custom format {trashId}, which is missing."));
                }
            }

            formats[attribute] = selected
                .DistinctBy(d => d.FileName, StringComparer.OrdinalIgnoreCase)
                .Select(d => ParseFormat(d.FileName, d.Json, logger))
                .ToList();
        }

        return new TrashDefinitions(formats);
    }

    private static CustomFormat ParseFormat(string fileName, JsonElement json, ILogger? logger)
    {
        var name = GetString(json, "name", fileName);
        var specifications = new List<CustomFormatSpec>();

        foreach (var spec in GetArray(json, "specifications", fileName))
        {
            var implementation = GetString(spec, "implementation", fileName);
            var negate = spec.TryGetProperty("negate", out var n) && n.ValueKind == JsonValueKind.True;
            var required = spec.TryGetProperty("required", out var r) && r.ValueKind == JsonValueKind.True;
            var value = GetFieldValue(spec);

            switch (implementation)
            {
                case CustomFormatSpec.ReleaseTitle or CustomFormatSpec.ReleaseGroup:
                    var pattern = value?.ValueKind == JsonValueKind.String ? value.Value.GetString() : null;
                    if (pattern is null)
                    {
                        throw new InvalidDataException($"cf/{fileName}.json has a {implementation} without a value.");
                    }

                    try
                    {
                        specifications.Add(new CustomFormatSpec(GetString(spec, "name", fileName), implementation, negate, required,
                            CustomFormatSpec.CreateRegex(pattern), null));
                    }
                    catch (ArgumentException exception)
                    {
                        throw new InvalidDataException($"cf/{fileName}.json has an invalid regular expression: {exception.Message}");
                    }

                    break;

                case CustomFormatSpec.Source:
                    int? number = value?.ValueKind == JsonValueKind.Number ? value.Value.GetInt32()
                        : int.TryParse(value?.ToString(), out var parsed) ? parsed : null;
                    specifications.Add(new CustomFormatSpec(GetString(spec, "name", fileName), implementation, negate, required, null, number));
                    break;

                default:
                    logger?.LogWarning("Custom format {Name} uses {Implementation}, which Overviewarr evaluates as not matching", name, implementation);
                    specifications.Add(new CustomFormatSpec(GetString(spec, "name", fileName), implementation, negate, required, null, null));
                    break;
            }
        }

        return new CustomFormat(GetString(json, "trash_id", fileName), name, fileName, specifications);
    }

    /// <summary><c>fields</c> is <c>{ "value": … }</c> in TRaSH files, or a list of name/value pairs in Sonarr exports.</summary>
    private static JsonElement? GetFieldValue(JsonElement spec)
    {
        if (!spec.TryGetProperty("fields", out var fields))
        {
            return null;
        }

        if (fields.ValueKind == JsonValueKind.Object && fields.TryGetProperty("value", out var value))
        {
            return value;
        }

        if (fields.ValueKind == JsonValueKind.Array)
        {
            foreach (var field in fields.EnumerateArray())
            {
                if (field.TryGetProperty("name", out var fieldName) && fieldName.GetString() == "value" &&
                    field.TryGetProperty("value", out var fieldValue))
                {
                    return fieldValue;
                }
            }
        }

        return null;
    }

    private static JsonElement ReadJson(string path)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            return document.RootElement.Clone();
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            throw new InvalidDataException($"{path} couldn't be read: {exception.Message}");
        }
    }

    private static string GetString(JsonElement json, string property, string source) =>
        json.ValueKind == JsonValueKind.Object && json.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()!
            : throw new InvalidDataException($"{source} has no \"{property}\".");

    private static JsonElement.ArrayEnumerator GetArray(JsonElement json, string property, string source) =>
        json.ValueKind == JsonValueKind.Object && json.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray()
            : throw new InvalidDataException($"{source} has no \"{property}\" list.");
}
