using Overviewarr.Releases;
using Overviewarr.Search;
using Overviewarr.Sonarr;
using Overviewarr.Trash;

namespace Overviewarr.Tests.Releases;

public class ReleaseNormalizerTests
{
    private static readonly ReleaseNormalizer Normalizer = new(TrashDefinitions.FromFormats(new Dictionary<TrashAttribute, IReadOnlyList<CustomFormat>>
    {
        [TrashAttribute.Codec] = [new CustomFormat("x265", "x265", "x265",
            [new CustomFormatSpec("x265", CustomFormatSpec.ReleaseTitle, false, false, CustomFormatSpec.CreateRegex(@"[xh][ ._-]?265"), null)])],
    }));

    private static ReleaseDto Release(string title = "Show.S01.1080p.WEB-DL.x265-GRP", long size = 10L * 1024 * 1024 * 1024,
        IReadOnlyList<ReleaseEpisodeDto>? episodes = null, int revision = 1, string? releaseGroup = "GRP") =>
        new("guid", 7, title, "Indexer", size, 3,
            new QualityModelDto(new QualityDto(3, "WEBDL-1080p", "web", 1080), new RevisionDto(revision, 0)),
            releaseGroup, [new NamedDto("English")], true, episodes ?? [], [new NamedDto("Repack")], 50, true, ["Not wanted"]);

    [Fact]
    public void Season_comes_from_the_matched_episodes()
    {
        var record = Normalizer.Normalize(Release(episodes: [new(12, 2, 1), new(11, 1, 10)]), SearchKey.Season(1, 1));

        Assert.Equal([1, 2], record.Seasons.Select(s => s.Number));
        Assert.Equal("S01E10, S02E01", record.EpisodesText);
    }

    [Fact]
    public void Season_comes_from_the_search_when_nothing_is_matched()
    {
        var record = Normalizer.Normalize(Release(episodes: []), SearchKey.Season(1, 3));

        Assert.Equal([3], record.Seasons.Select(s => s.Number));
    }

    [Fact]
    public void Size_per_episode_divides_by_the_matched_episodes()
    {
        var episodes = Enumerable.Range(1, 10).Select(n => new ReleaseEpisodeDto(n, 1, n)).ToList();

        var record = Normalizer.Normalize(Release(episodes: episodes), SearchKey.Season(1, 1));

        Assert.Equal(1024L * 1024 * 1024, record.SizePerEpisode);
        Assert.Equal(1.0, record.SizePerEpisodeGb);
    }

    [Fact]
    public void Size_per_episode_is_empty_without_matched_episodes()
    {
        var record = Normalizer.Normalize(Release(episodes: []), SearchKey.Season(1, 1));

        Assert.Null(record.SizePerEpisode);
        Assert.Null(record.SizePerEpisodeGb);
    }

    [Fact]
    public void Attributes_without_a_match_get_their_no_match_value()
    {
        var record = Normalizer.Normalize(Release(), SearchKey.Season(1, 1));

        Assert.Equal("x265", record.CodecText);
        Assert.Equal("SDR", record.HdrText);
        Assert.Equal("Unknown", record.AudioFormatText);
        Assert.Equal("Unknown", record.AudioChannelsText);
        Assert.Equal("None", record.StreamingServiceText);
    }

    [Fact]
    public void Sonarr_fields_are_kept()
    {
        var record = Normalizer.Normalize(Release(revision: 2), SearchKey.Season(1, 1));

        Assert.Equal(new ReleaseKey(7, "guid"), record.Key);
        Assert.True(record.SeasonPack);
        Assert.True(record.ProperRepack);
        Assert.True(record.Rejected);
        Assert.Equal(["Not wanted"], record.Rejections);
        Assert.Equal("English", record.LanguagesText);
        Assert.Equal("Repack", record.CustomFormatsText);
        Assert.Equal(50, record.CustomFormatScore);
        Assert.Equal("WEBDL-1080p", record.Quality);
        Assert.Equal("web", record.Source);
        Assert.Equal(1080, record.Resolution);
    }

    [Fact]
    public void Missing_release_group_is_empty() =>
        Assert.Null(Normalizer.Normalize(Release(releaseGroup: " "), SearchKey.Season(1, 1)).ReleaseGroup);
}
