using Overviewarr.Search;
using Overviewarr.Sonarr;
using Overviewarr.Trash;

namespace Overviewarr.Releases;

/// <summary>Turns a Sonarr release into a release record (DESIGN.md section 7).</summary>
public sealed class ReleaseNormalizer(TrashDefinitions trash)
{
    public ReleaseRecord Normalize(ReleaseDto release, SearchKey search)
    {
        var quality = release.Quality?.Quality;
        var releaseGroup = string.IsNullOrWhiteSpace(release.ReleaseGroup) ? null : release.ReleaseGroup;
        var input = new ReleaseInput(release.Title, releaseGroup, quality?.Source);
        var episodes = (release.MappedEpisodeInfo ?? [])
            .Select(e => new ReleaseEpisode(e.Id, e.SeasonNumber, e.EpisodeNumber))
            .DistinctBy(e => e.EpisodeId)
            .OrderBy(e => e.SeasonNumber)
            .ThenBy(e => e.EpisodeNumber)
            .ToList();

        return new ReleaseRecord
        {
            Guid = release.Guid,
            IndexerId = release.IndexerId,
            Title = release.Title,
            Indexer = release.Indexer ?? string.Empty,
            Size = release.Size,
            Age = release.Age,
            Quality = quality?.Name ?? "Unknown",
            Source = quality?.Source ?? "unknown",
            Resolution = quality?.Resolution ?? 0,
            RevisionVersion = release.Quality?.Revision?.Version ?? 1,
            ReleaseGroup = releaseGroup,
            Languages = Tags(release.Languages),
            SeasonPack = release.FullSeason,
            Episodes = episodes,
            CustomFormats = Tags(release.CustomFormats),
            CustomFormatScore = release.CustomFormatScore,
            Rejected = release.Rejected,
            Rejections = release.Rejections ?? [],
            Codec = Attribute(TrashAttribute.Codec, input, "Unknown"),
            Hdr = Attribute(TrashAttribute.Hdr, input, "SDR"),
            AudioFormat = Attribute(TrashAttribute.AudioFormat, input, "Unknown"),
            AudioChannels = Attribute(TrashAttribute.AudioChannels, input, "Unknown"),
            StreamingService = Attribute(TrashAttribute.StreamingService, input, "None"),
            Searches = [search],
            Seasons = ReleaseRecord.ComputeSeasons(episodes, [search]),
        };
    }

    private IReadOnlyList<Tag> Attribute(TrashAttribute attribute, ReleaseInput input, string noMatch)
    {
        var names = trash.Match(attribute, input);
        return names.Count > 0 ? names.Select(n => new Tag(n)).ToList() : [new Tag(noMatch)];
    }

    private static IReadOnlyList<Tag> Tags(IReadOnlyList<NamedDto>? values) =>
        (values ?? [])
        .Select(v => v.Name)
        .Where(n => !string.IsNullOrWhiteSpace(n))
        .Distinct()
        .Select(n => new Tag(n!))
        .ToList();
}
