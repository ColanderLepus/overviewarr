using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Overviewarr.Settings;

namespace Overviewarr.Sonarr;

/// <summary>
/// Every Sonarr call, over one <see cref="HttpClient"/>. Each request builds its absolute URL from the
/// current settings and has its own timeout. Failures become a <see cref="SonarrException"/>.
/// </summary>
public sealed class SonarrClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly SettingsStore _settings;
    private readonly TimeProvider _time;
    private readonly SonarrTimeouts _timeouts;
    private readonly ILogger<SonarrClient> _logger;

    public SonarrClient(SettingsStore settings, TimeProvider time, ILogger<SonarrClient> logger)
        : this(settings, time, logger,
            new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(2) },
            SonarrTimeouts.Standard)
    {
    }

    internal SonarrClient(SettingsStore settings, TimeProvider time, ILogger<SonarrClient> logger,
        HttpMessageHandler handler, SonarrTimeouts timeouts)
    {
        _http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        _settings = settings;
        _time = time;
        _timeouts = timeouts;
        _logger = logger;
    }

    /// <summary>Tests the URL and key from the settings form. Returns Sonarr's version.</summary>
    public async Task<string> TestConnectionAsync(string url, string apiKey, CancellationToken cancellationToken = default)
    {
        var status = await SendAsync<SystemStatusDto>(HttpMethod.Get, url, apiKey, "/api/v3/system/status", null,
            _timeouts.ConnectionTest, cancellationToken);
        return status.Version;
    }

    public Task<List<SeriesDto>> GetSeriesAsync(CancellationToken cancellationToken = default) =>
        GetAsync<List<SeriesDto>>("/api/v3/series", _timeouts.Default, cancellationToken);

    public Task<SeriesDto> GetSeriesByIdAsync(int seriesId, CancellationToken cancellationToken = default) =>
        GetAsync<SeriesDto>($"/api/v3/series/{seriesId}", _timeouts.Default, cancellationToken);

    public Task<List<EpisodeDto>> GetEpisodesAsync(int seriesId, CancellationToken cancellationToken = default) =>
        GetAsync<List<EpisodeDto>>($"/api/v3/episode?seriesId={seriesId}", _timeouts.Default, cancellationToken);

    public Task<List<EpisodeFileDto>> GetEpisodeFilesAsync(int seriesId, CancellationToken cancellationToken = default) =>
        GetAsync<List<EpisodeFileDto>>($"/api/v3/episodefile?seriesId={seriesId}", _timeouts.Default, cancellationToken);

    public Task<List<ReleaseDto>> SearchSeasonAsync(int seriesId, int seasonNumber, CancellationToken cancellationToken = default) =>
        GetAsync<List<ReleaseDto>>($"/api/v3/release?seriesId={seriesId}&seasonNumber={seasonNumber}", _timeouts.ReleaseSearch, cancellationToken);

    public Task<List<ReleaseDto>> SearchEpisodeAsync(int episodeId, CancellationToken cancellationToken = default) =>
        GetAsync<List<ReleaseDto>>($"/api/v3/release?episodeId={episodeId}", _timeouts.ReleaseSearch, cancellationToken);

    public Task<DownloadClientConfigDto> GetDownloadClientConfigAsync(CancellationToken cancellationToken = default) =>
        GetAsync<DownloadClientConfigDto>("/api/v3/config/downloadclient", _timeouts.Default, cancellationToken);

    public Task<QualityProfileDto> GetQualityProfileAsync(int qualityProfileId, CancellationToken cancellationToken = default) =>
        GetAsync<QualityProfileDto>($"/api/v3/qualityprofile/{qualityProfileId}", _timeouts.Default, cancellationToken);

    public Task<PagedDto<QueueRecordDto>> GetQueueAsync(int seriesId, CancellationToken cancellationToken = default) =>
        GetAsync<PagedDto<QueueRecordDto>>($"/api/v3/queue?seriesIds={seriesId}&pageSize=1000", _timeouts.Default, cancellationToken);

    /// <summary>The series' grabbed, imported, failed and ignored history entries, newest first.</summary>
    public Task<PagedDto<HistoryDto>> GetSeriesHistoryAsync(int seriesId, CancellationToken cancellationToken = default) =>
        GetAsync<PagedDto<HistoryDto>>(
            $"/api/v3/history?seriesIds={seriesId}&eventType=1&eventType=3&eventType=4&eventType=7&pageSize=1000",
            _timeouts.Default, cancellationToken);

    /// <summary>Every history entry since a time, for all series.</summary>
    public Task<List<HistoryDto>> GetHistorySinceAsync(DateTimeOffset since, CancellationToken cancellationToken = default) =>
        GetAsync<List<HistoryDto>>(
            $"/api/v3/history/since?date={Uri.EscapeDataString(since.UtcDateTime.ToString("o", System.Globalization.CultureInfo.InvariantCulture))}",
            _timeouts.Default, cancellationToken);

    /// <summary>Sends a release to Sonarr for download (<c>POST /api/v3/release</c>).</summary>
    public async Task GrabReleaseAsync(string guid, int indexerId, CancellationToken cancellationToken = default)
    {
        var connection = _settings.Connection ?? throw new SonarrException("No Sonarr connection is saved.");
        await SendAsync<NoContent>(HttpMethod.Post, connection.Url, connection.ApiKey, "/api/v3/release",
            new { guid, indexerId }, _timeouts.Default, cancellationToken);
    }

    private Task<T> GetAsync<T>(string pathAndQuery, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var connection = _settings.Connection ?? throw new SonarrException("No Sonarr connection is saved.");
        return SendAsync<T>(HttpMethod.Get, connection.Url, connection.ApiKey, pathAndQuery, null, timeout, cancellationToken);
    }

    /// <summary>A call whose response body isn't read.</summary>
    private sealed record NoContent;

    private async Task<T> SendAsync<T>(HttpMethod method, string baseUrl, string apiKey, string pathAndQuery,
        object? body, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var timeoutSource = new CancellationTokenSource(timeout, _time);
        using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);

        try
        {
            using var request = new HttpRequestMessage(method, baseUrl + pathAndQuery);
            request.Headers.Add("X-Api-Key", apiKey);
            if (body is not null)
            {
                request.Content = JsonContent.Create(body, options: Json);
            }

            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, linkedSource.Token);
            if (!response.IsSuccessStatusCode)
            {
                throw await CreateErrorAsync(response, linkedSource.Token);
            }

            if (typeof(T) == typeof(NoContent))
            {
                return (T)(object)new NoContent();
            }

            var result = await response.Content.ReadFromJsonAsync<T>(Json, linkedSource.Token);
            return result ?? throw new SonarrException("Sonarr returned an empty response.");
        }
        catch (SonarrException exception)
        {
            throw Logged(method, pathAndQuery, exception);
        }
        catch (OperationCanceledException exception) when (timeoutSource.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw Logged(method, pathAndQuery,
                new SonarrException($"Sonarr didn't answer within {timeout.TotalSeconds:0} seconds.", innerException: exception));
        }
        catch (HttpRequestException exception)
        {
            throw Logged(method, pathAndQuery,
                new SonarrException($"Can't reach Sonarr at {baseUrl}: {exception.Message}", innerException: exception));
        }
        catch (JsonException exception)
        {
            throw Logged(method, pathAndQuery,
                new SonarrException($"Sonarr's response couldn't be read: {exception.Message}", innerException: exception));
        }
    }

    private static async Task<SonarrException> CreateErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            return new SonarrException("Sonarr rejected the API key.", isUnauthorized: true);
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var message = TryReadMessage(body);
        if (!string.IsNullOrWhiteSpace(message))
        {
            return new SonarrException(message);
        }

        var status = (int)response.StatusCode;
        var reason = string.IsNullOrWhiteSpace(response.ReasonPhrase) ? response.StatusCode.ToString() : response.ReasonPhrase;
        return new SonarrException($"HTTP {status} {reason}");
    }

    private static string? TryReadMessage(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (string.Equals(property.Name, "message", StringComparison.OrdinalIgnoreCase) &&
                    property.Value.ValueKind == JsonValueKind.String)
                {
                    return property.Value.GetString();
                }
            }

            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private SonarrException Logged(HttpMethod method, string pathAndQuery, SonarrException exception)
    {
        _logger.LogWarning("Sonarr call {Method} {Path} failed: {Message}", method, pathAndQuery, exception.Message);
        return exception;
    }
}
