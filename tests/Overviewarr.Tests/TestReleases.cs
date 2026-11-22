using Overviewarr.Releases;
using Overviewarr.Search;

namespace Overviewarr.Tests;

/// <summary>Builds release records for tests, with defaults for everything not given.</summary>
internal static class TestReleases
{
    public static ReleaseRecord Make(
        string guid = "guid",
        int indexerId = 1,
        string? title = null,
        string? releaseGroup = "GRP",
        string quality = "WEBDL-1080p",
        string source = "web",
        int resolution = 1080,
        long size = 1024L * 1024 * 1024,
        int age = 10,
        int revision = 1,
        bool seasonPack = false,
        bool rejected = false,
        int customFormatScore = 0,
        string indexer = "Indexer",
        IEnumerable<(int Season, int Episode)>? episodes = null,
        IEnumerable<string>? codec = null,
        IEnumerable<string>? hdr = null,
        IEnumerable<string>? audioFormat = null,
        IEnumerable<string>? audioChannels = null,
        IEnumerable<string>? streaming = null,
        IEnumerable<string>? languages = null,
        IEnumerable<string>? customFormats = null,
        SearchKey? search = null)
    {
        var mapped = (episodes ?? [(1, 1)])
            .Select(e => new ReleaseEpisode(e.Season * 1000 + e.Episode, e.Season, e.Episode))
            .ToList();
        var searchKey = search ?? SearchKey.Season(1, mapped.Count > 0 ? mapped[0].SeasonNumber : 1);

        return new ReleaseRecord
        {
            Guid = guid,
            IndexerId = indexerId,
            Title = title ?? $"Show.{guid}",
            Indexer = indexer,
            Size = size,
            Age = age,
            Quality = quality,
            Source = source,
            Resolution = resolution,
            RevisionVersion = revision,
            ReleaseGroup = releaseGroup,
            Languages = Tags(languages ?? ["English"]),
            SeasonPack = seasonPack,
            Episodes = mapped,
            CustomFormats = Tags(customFormats ?? []),
            CustomFormatScore = customFormatScore,
            Rejected = rejected,
            Rejections = rejected ? ["Rejected for a test"] : [],
            Codec = Tags(codec ?? ["x264"]),
            Hdr = Tags(hdr ?? ["SDR"]),
            AudioFormat = Tags(audioFormat ?? ["DD+"]),
            AudioChannels = Tags(audioChannels ?? ["5.1 Surround"]),
            StreamingService = Tags(streaming ?? ["None"]),
            Searches = [searchKey],
            Seasons = ReleaseRecord.ComputeSeasons(mapped, [searchKey]),
        };
    }

    private static IReadOnlyList<Tag> Tags(IEnumerable<string> names) => names.Select(n => new Tag(n)).ToList();
}
