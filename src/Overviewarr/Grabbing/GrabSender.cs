using Overviewarr.Releases;
using Overviewarr.Search;
using Overviewarr.Sonarr;

namespace Overviewarr.Grabbing;

public enum GrabOutcome
{
    /// <summary>Sonarr accepted it.</summary>
    Sent,

    /// <summary>Missing from the new search results, or the search failed.</summary>
    NotFound,

    /// <summary>Sonarr returned an error.</summary>
    Refused,
}

public sealed record GrabResult(ReleaseRecord Release, GrabOutcome Outcome, string? Message = null);

/// <summary>
/// Sends selected releases to Sonarr (DESIGN.md section 9, steps 2–4): stale releases are looked up again in their
/// newest search's new results, then every release is sent one at a time. A release that isn't found again, or that
/// Sonarr refuses, is skipped; the rest are still sent.
/// </summary>
public sealed class GrabSender(Func<SearchKey, Task<SearchStatus>> searchAgain, Func<ReleaseRecord, Task> grab)
{
    public async Task<IReadOnlyList<GrabResult>> SendAsync(IReadOnlyList<ReleaseRecord> selected, SeriesSnapshot snapshot,
        DateTimeOffset now, IProgress<string>? progress = null)
    {
        var plan = Freshness.Plan(selected, snapshot, now);
        var results = new Dictionary<ReleaseKey, GrabResult>();
        var toSend = new List<ReleaseRecord>();

        var searches = new Dictionary<SearchKey, SearchStatus>();
        if (plan.Searches.Count > 0)
        {
            progress?.Report("Searching again…");
            var running = plan.Searches.ToDictionary(key => key, searchAgain);
            await Task.WhenAll(running.Values);
            foreach (var (key, task) in running)
            {
                searches[key] = task.Result;
            }
        }

        foreach (var release in selected)
        {
            if (plan.Fresh.Contains(release.Key))
            {
                toSend.Add(release);
            }
            else if (plan.SearchAgain.TryGetValue(release.Key, out var searchKey))
            {
                var search = searches[searchKey];
                var found = search.State == SearchState.Done ? search.Results.FirstOrDefault(r => r.Key == release.Key) : null;

                if (found is not null)
                {
                    toSend.Add(found);
                }
                else
                {
                    results[release.Key] = new GrabResult(release, GrabOutcome.NotFound, search.State == SearchState.Failed ? search.Error : null);
                }
            }
            else
            {
                results[release.Key] = new GrabResult(release, GrabOutcome.NotFound);
            }
        }

        for (var i = 0; i < toSend.Count; i++)
        {
            progress?.Report($"Sending {i + 1} of {toSend.Count}…");
            var release = toSend[i];

            try
            {
                await grab(release);
                results[release.Key] = new GrabResult(release, GrabOutcome.Sent);
            }
            catch (SonarrException exception)
            {
                results[release.Key] = new GrabResult(release, GrabOutcome.Refused, exception.Message);
            }
        }

        return selected.Select(r => results[r.Key]).ToList();
    }
}
