using System.Net;
using System.Text;
using Microsoft.Extensions.Time.Testing;
using Overviewarr.Settings;
using Overviewarr.Sonarr;

namespace Overviewarr.Tests.Sonarr;

public class SonarrClientTests
{
    private const string Url = "http://sonarr:8989";
    private const string ApiKey = "secret-key";

    private readonly FakeTimeProvider _time = new();
    private readonly ListLogger<SonarrClient> _logger = new();

    private SonarrClient CreateClient(FakeHandler handler)
    {
        using var directory = new TempDirectory();
        var store = new SettingsStore(new AppPaths(directory.Path, directory.Path), new ListLogger<SettingsStore>());
        store.SaveAsync(new SonarrConnection(Url, ApiKey)).GetAwaiter().GetResult();
        return new SonarrClient(store, _time, _logger, handler, SonarrTimeouts.Standard);
    }

    private static FakeHandler Respond(HttpStatusCode status, string? body = null, string? reason = null) =>
        new((_, _) => Task.FromResult(new HttpResponseMessage(status)
        {
            Content = new StringContent(body ?? string.Empty, Encoding.UTF8, "application/json"),
            ReasonPhrase = reason,
        }));

    [Fact]
    public async Task Requests_go_to_the_saved_url_with_the_api_key_header()
    {
        var handler = Respond(HttpStatusCode.OK, "[{\"id\":1,\"title\":\"Show\",\"sortTitle\":\"show\",\"year\":2015,\"qualityProfileId\":4,\"seasons\":[]}]");
        var client = CreateClient(handler);

        var series = await client.GetSeriesAsync();

        var request = Assert.Single(handler.Requests);
        Assert.Equal("http://sonarr:8989/api/v3/series", request.RequestUri!.ToString());
        Assert.Equal(ApiKey, Assert.Single(request.Headers.GetValues("X-Api-Key")));
        Assert.Equal("Show", Assert.Single(series).Title);
    }

    [Fact]
    public async Task Connection_test_uses_the_form_values_and_returns_the_version()
    {
        var handler = Respond(HttpStatusCode.OK, "{\"version\":\"4.0.20.3014\"}");
        var client = CreateClient(handler);

        var version = await client.TestConnectionAsync("http://other:8989", "form-key");

        var request = Assert.Single(handler.Requests);
        Assert.Equal("http://other:8989/api/v3/system/status", request.RequestUri!.ToString());
        Assert.Equal("form-key", Assert.Single(request.Headers.GetValues("X-Api-Key")));
        Assert.Equal("4.0.20.3014", version);
    }

    [Fact]
    public async Task Unauthorized_says_the_api_key_was_rejected()
    {
        var client = CreateClient(Respond(HttpStatusCode.Unauthorized, "{\"message\":\"Unauthorized\"}"));

        var exception = await Assert.ThrowsAsync<SonarrException>(() => client.GetSeriesAsync());

        Assert.Equal("Sonarr rejected the API key.", exception.Message);
        Assert.True(exception.IsUnauthorized);
    }

    [Fact]
    public async Task Error_with_a_message_shows_sonarrs_message()
    {
        var client = CreateClient(Respond(HttpStatusCode.BadRequest, "{\"message\":\"No indexers available\",\"description\":\"x\"}"));

        var exception = await Assert.ThrowsAsync<SonarrException>(() => client.GetSeriesAsync());

        Assert.Equal("No indexers available", exception.Message);
        Assert.False(exception.IsUnauthorized);
    }

    [Theory]
    [InlineData("")]
    [InlineData("<html>Bad gateway</html>")]
    [InlineData("[{\"errorMessage\":\"x\"}]")]
    public async Task Other_error_shows_the_status(string body)
    {
        var client = CreateClient(Respond(HttpStatusCode.BadGateway, body, reason: "Bad Gateway"));

        var exception = await Assert.ThrowsAsync<SonarrException>(() => client.GetSeriesAsync());

        Assert.Equal("HTTP 502 Bad Gateway", exception.Message);
    }

    [Fact]
    public async Task Timeout_says_how_long_was_waited()
    {
        var started = new TaskCompletionSource();
        var handler = new FakeHandler(async (_, cancellationToken) =>
        {
            started.SetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var client = CreateClient(handler);

        var call = client.GetSeriesAsync();
        await started.Task;
        _time.Advance(TimeSpan.FromSeconds(31));

        var exception = await Assert.ThrowsAsync<SonarrException>(() => call);
        Assert.Equal("Sonarr didn't answer within 30 seconds.", exception.Message);
    }

    [Fact]
    public async Task Connection_error_names_the_url()
    {
        var client = CreateClient(new FakeHandler((_, _) => throw new HttpRequestException("No such host is known.")));

        var exception = await Assert.ThrowsAsync<SonarrException>(() => client.GetSeriesAsync());

        Assert.Equal("Can't reach Sonarr at http://sonarr:8989: No such host is known.", exception.Message);
    }

    [Fact]
    public async Task Grab_posts_the_guid_and_indexer_and_ignores_the_response_body()
    {
        string? body = null;
        var handler = new FakeHandler(async (request, cancellationToken) =>
        {
            body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(string.Empty) };
        });
        var client = CreateClient(handler);

        await client.GrabReleaseAsync("abc-123", 3);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("http://sonarr:8989/api/v3/release", request.RequestUri!.ToString());
        Assert.Equal("{\"guid\":\"abc-123\",\"indexerId\":3}", body);
    }

    [Fact]
    public async Task Refused_grab_shows_sonarrs_message()
    {
        var client = CreateClient(Respond(HttpStatusCode.NotFound, "{\"message\":\"Couldn't find requested release in cache, try searching again\"}"));

        var exception = await Assert.ThrowsAsync<SonarrException>(() => client.GrabReleaseAsync("abc", 3));

        Assert.Equal("Couldn't find requested release in cache, try searching again", exception.Message);
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    public async Task Redownload_setting_is_on_when_both_options_are(bool failed, bool fromInteractiveSearch, bool on)
    {
        var json = $"{{\"id\":1,\"autoRedownloadFailed\":{failed.ToString().ToLowerInvariant()},\"autoRedownloadFailedFromInteractiveSearch\":{fromInteractiveSearch.ToString().ToLowerInvariant()},\"enableCompletedDownloadHandling\":true}}";
        var client = CreateClient(Respond(HttpStatusCode.OK, json));

        var config = await client.GetDownloadClientConfigAsync();

        Assert.Equal(on, config.RedownloadsFailedInteractiveGrabs);
    }

    [Fact]
    public async Task Quality_profile_is_read()
    {
        var handler = Respond(HttpStatusCode.OK, "{\"id\":4,\"name\":\"HD-1080p\",\"upgradeAllowed\":true,\"items\":[]}");
        var client = CreateClient(handler);

        var profile = await client.GetQualityProfileAsync(4);

        Assert.Equal("http://sonarr:8989/api/v3/qualityprofile/4", Assert.Single(handler.Requests).RequestUri!.ToString());
        Assert.Equal(new QualityProfileDto(4, "HD-1080p", true), profile);
    }

    [Fact]
    public async Task Queue_and_history_calls_use_the_documented_queries()
    {
        var handler = new FakeHandler((request, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(request.RequestUri!.AbsolutePath.EndsWith("/since", StringComparison.Ordinal)
                ? "[]"
                : "{\"page\":1,\"pageSize\":1000,\"totalRecords\":0,\"records\":[]}", Encoding.UTF8, "application/json"),
        }));
        var client = CreateClient(handler);

        await client.GetQueueAsync(12);
        await client.GetSeriesHistoryAsync(12);
        await client.GetHistorySinceAsync(new DateTimeOffset(2026, 11, 22, 14, 3, 11, TimeSpan.FromHours(1)));

        Assert.Equal(
        [
            "http://sonarr:8989/api/v3/queue?seriesIds=12&pageSize=1000",
            "http://sonarr:8989/api/v3/history?seriesIds=12&eventType=1&eventType=3&eventType=4&eventType=7&pageSize=1000",
            "http://sonarr:8989/api/v3/history/since?date=2026-11-22T13%3A03%3A11.0000000Z",
        ], handler.Requests.Select(r => r.RequestUri!.OriginalString));
    }

    [Fact]
    public async Task History_entries_are_read()
    {
        var json = "[{\"id\":7,\"date\":\"2026-11-22T13:03:11Z\",\"eventType\":\"downloadFailed\",\"seriesId\":12,\"episodeId\":101," +
                   "\"downloadId\":\"SABnzbd_nzo_1\",\"sourceTitle\":\"Show.S01E01-GRP\",\"data\":{\"message\":\"Download failed\",\"guid\":null}}]";
        var client = CreateClient(Respond(HttpStatusCode.OK, json));

        var entry = Assert.Single(await client.GetHistorySinceAsync(DateTimeOffset.UnixEpoch));

        Assert.True(entry.Is(HistoryDto.DownloadFailed));
        Assert.Equal(new DateTimeOffset(2026, 11, 22, 13, 3, 11, TimeSpan.Zero), entry.Date);
        Assert.Equal("Download failed", entry.GetData("message"));
        Assert.Null(entry.GetData("guid"));
    }

    [Fact]
    public async Task Failures_are_logged_without_the_api_key()
    {
        var client = CreateClient(Respond(HttpStatusCode.InternalServerError, "{\"message\":\"Boom\"}"));

        await Assert.ThrowsAsync<SonarrException>(() => client.GetSeriesAsync());

        var message = Assert.Single(_logger.Messages);
        Assert.Equal("Warning: Sonarr call GET /api/v3/series failed: Boom", message);
        Assert.DoesNotContain(ApiKey, message);
    }
}
