using Overviewarr.Releases;
using Overviewarr.Search;
using Overviewarr.Sonarr;
using Overviewarr.Trash;

namespace Overviewarr.Tests.Trash;

/// <summary>The attribute examples from DESIGN.md section 13, against the TRaSH Guides definitions.</summary>
public class TrashAttributeTests
{
    private static readonly Lazy<TrashDefinitions> Definitions = new(() => TrashDefinitions.Load(FindTrashDirectory()));

    /// <summary><c>TRASH_DIR</c>, or the development clone in <c>.dev/trash</c>.</summary>
    internal static string FindTrashDirectory()
    {
        var configured = Environment.GetEnvironmentVariable("TRASH_DIR");
        if (!string.IsNullOrEmpty(configured))
        {
            return configured;
        }

        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Overviewarr.slnx")))
        {
            directory = directory.Parent;
        }

        var development = directory is null ? null : Path.Combine(directory.FullName, ".dev", "trash", "docs", "json", "sonarr");
        return development is not null && Directory.Exists(development)
            ? development
            : throw new InvalidOperationException("Set TRASH_DIR to the TRaSH Guides docs/json/sonarr directory.");
    }

    [Theory]
    [InlineData("Show.Name.S01E01.1080p.AMZN.WEB-DL.DDP5.1.H.264-GRP", "web", "x264", "SDR", "DD+", "5.1 Surround", "AMZN")]
    [InlineData("Show.Name.S02.2160p.NF.WEB-DL.DDP5.1.Atmos.DV.HDR.H.265-GRP", "web", "x265", "DV Boost, HDR", "DD+ ATMOS", "5.1 Surround", "NF")]
    [InlineData("Show.Name.S01E02.720p.HDTV.x264-GRP", "television", "x264", "SDR", "Unknown", "Unknown", "None")]
    public void Attributes_come_from_the_trash_definitions(string title, string source, string codec, string hdr,
        string audioFormat, string audioChannels, string streaming)
    {
        var normalizer = new ReleaseNormalizer(Definitions.Value);
        var release = new ReleaseDto("guid", 1, title, "Indexer", 0, 1,
            new QualityModelDto(new QualityDto(3, "WEBDL-1080p", source, 1080), new RevisionDto(1, 0)),
            "GRP", [], false, [], [], 0, false, []);

        var record = normalizer.Normalize(release, SearchKey.Season(1, 1));

        Assert.Equal(codec, record.CodecText);
        Assert.Equal(hdr, record.HdrText);
        Assert.Equal(audioFormat, record.AudioFormatText);
        Assert.Equal(audioChannels, record.AudioChannelsText);
        Assert.Equal(streaming, record.StreamingServiceText);
    }

    [Fact]
    public void Every_attribute_has_custom_formats()
    {
        foreach (var attribute in Enum.GetValues<TrashAttribute>())
        {
            Assert.NotEmpty(Definitions.Value.FormatsFor(attribute));
        }
    }

    [Fact]
    public void Missing_directory_is_reported()
    {
        var exception = Assert.Throws<InvalidDataException>(() => TrashDefinitions.Load(Path.Combine(Path.GetTempPath(), "no-such-trash-dir")));
        Assert.Contains("weren't found", exception.Message);
    }
}
