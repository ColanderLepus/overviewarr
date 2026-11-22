namespace Overviewarr.Sonarr;

// Only the Sonarr v3 fields Overviewarr uses (DESIGN.md section 5). Enums arrive as camelCase strings.

public sealed record SystemStatusDto(string Version);

public sealed record SeriesDto(
    int Id,
    string Title,
    string? SortTitle,
    int Year,
    int QualityProfileId,
    IReadOnlyList<SeasonDto>? Seasons);

public sealed record SeasonDto(int SeasonNumber, bool Monitored);

public sealed record EpisodeDto(
    int Id,
    int SeasonNumber,
    int EpisodeNumber,
    string? Title,
    DateTimeOffset? AirDateUtc,
    bool HasFile,
    int EpisodeFileId);

public sealed record EpisodeFileDto(
    int Id,
    string? ReleaseGroup,
    QualityModelDto? Quality,
    MediaInfoDto? MediaInfo);

public sealed record MediaInfoDto(
    string? VideoCodec,
    string? VideoDynamicRangeType,
    string? AudioCodec,
    double? AudioChannels);

public sealed record QualityModelDto(QualityDto? Quality, RevisionDto? Revision);

public sealed record QualityDto(int Id, string? Name, string? Source, int Resolution);

public sealed record RevisionDto(int Version, int Real);

public sealed record ReleaseDto(
    string Guid,
    int IndexerId,
    string Title,
    string? Indexer,
    long Size,
    int Age,
    QualityModelDto? Quality,
    string? ReleaseGroup,
    IReadOnlyList<NamedDto>? Languages,
    bool FullSeason,
    IReadOnlyList<ReleaseEpisodeDto>? MappedEpisodeInfo,
    IReadOnlyList<NamedDto>? CustomFormats,
    int CustomFormatScore,
    bool Rejected,
    IReadOnlyList<string>? Rejections);

public sealed record NamedDto(string? Name);

public sealed record DownloadClientConfigDto(bool AutoRedownloadFailed, bool AutoRedownloadFailedFromInteractiveSearch)
{
    /// <summary>Sonarr grabs a replacement by itself when a download from an interactive search fails.</summary>
    public bool RedownloadsFailedInteractiveGrabs => AutoRedownloadFailed && AutoRedownloadFailedFromInteractiveSearch;
}

public sealed record QualityProfileDto(int Id, string Name, bool UpgradeAllowed);

/// <summary>A page of a paged Sonarr list (queue, history).</summary>
public sealed record PagedDto<T>(int Page, int PageSize, int TotalRecords, IReadOnlyList<T>? Records);

public sealed record QueueRecordDto(int? SeriesId, int? EpisodeId, string? Title, string? DownloadId);

public sealed record HistoryDto(
    int Id,
    DateTimeOffset Date,
    string EventType,
    int SeriesId,
    int EpisodeId,
    string? DownloadId,
    string? SourceTitle,
    IReadOnlyDictionary<string, string?>? Data)
{
    public const string Grabbed = "grabbed";
    public const string DownloadFolderImported = "downloadFolderImported";
    public const string DownloadFailed = "downloadFailed";
    public const string DownloadIgnored = "downloadIgnored";

    public bool Is(string eventType) => string.Equals(EventType, eventType, StringComparison.OrdinalIgnoreCase);

    /// <summary>A value of <c>data</c>, by key, ignoring case.</summary>
    public string? GetData(string key) =>
        Data?.FirstOrDefault(pair => string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase)).Value;
}

public sealed record ReleaseEpisodeDto(int Id, int SeasonNumber, int EpisodeNumber);
