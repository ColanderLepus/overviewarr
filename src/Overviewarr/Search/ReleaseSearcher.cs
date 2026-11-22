using Overviewarr.Releases;
using Overviewarr.Sonarr;

namespace Overviewarr.Search;

/// <summary>Runs one search against Sonarr and normalizes the results.</summary>
public interface IReleaseSearcher
{
    Task<IReadOnlyList<ReleaseRecord>> SearchAsync(SearchKey key, CancellationToken cancellationToken);
}

public sealed class SonarrReleaseSearcher(SonarrClient sonarr, ReleaseNormalizer normalizer) : IReleaseSearcher
{
    public async Task<IReadOnlyList<ReleaseRecord>> SearchAsync(SearchKey key, CancellationToken cancellationToken)
    {
        var releases = key.EpisodeId is int episodeId
            ? await sonarr.SearchEpisodeAsync(episodeId, cancellationToken)
            : await sonarr.SearchSeasonAsync(key.SeriesId, key.SeasonNumber, cancellationToken);

        return releases.Select(r => normalizer.Normalize(r, key)).ToList();
    }
}
