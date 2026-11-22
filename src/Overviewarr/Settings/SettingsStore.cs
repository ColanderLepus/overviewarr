using System.Text.Json;

namespace Overviewarr.Settings;

/// <summary>Reads and writes <c>settings.json</c>, and holds the current Sonarr connection.</summary>
public sealed class SettingsStore(AppPaths paths, ILogger<SettingsStore> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private readonly SemaphoreSlim _writeLock = new(1, 1);

    /// <summary>The saved connection, or null while unconfigured.</summary>
    public SonarrConnection? Connection { get; private set; }

    /// <summary>Why <c>settings.json</c> couldn't be read at startup, or null.</summary>
    public string? LoadError { get; private set; }

    public bool IsConfigured => Connection is not null;

    public event Action<SettingsChange>? Changed;

    /// <summary>Loads <c>settings.json</c>. A file that can't be read leaves the app unconfigured.</summary>
    public void Load()
    {
        if (!File.Exists(paths.SettingsFile))
        {
            Connection = null;
            LoadError = null;
            return;
        }

        try
        {
            var model = JsonSerializer.Deserialize<SettingsFileModel>(File.ReadAllText(paths.SettingsFile), JsonOptions);
            var url = model?.Sonarr?.Url;
            var apiKey = model?.Sonarr?.ApiKey?.Trim();

            if (!SettingsRules.TryNormalizeUrl(url, out var normalizedUrl, out _) || string.IsNullOrEmpty(apiKey))
            {
                throw new InvalidDataException("sonarr.url or sonarr.apiKey is missing or invalid");
            }

            Connection = new SonarrConnection(normalizedUrl, apiKey);
            LoadError = null;
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException or IOException or UnauthorizedAccessException)
        {
            Connection = null;
            LoadError = exception.Message;
            logger.LogError("settings.json couldn't be read: {Error}", exception.Message);
        }
    }

    /// <summary>Saves the connection atomically and raises <see cref="Changed"/>.</summary>
    public async Task SaveAsync(SonarrConnection connection, CancellationToken cancellationToken = default)
    {
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            var model = new SettingsFileModel
            {
                Sonarr = new SettingsFileModel.SonarrSection { Url = connection.Url, ApiKey = connection.ApiKey },
            };

            await AtomicFile.WriteAllTextAsync(paths.SettingsFile, JsonSerializer.Serialize(model, JsonOptions), cancellationToken);

            var urlChanged = !string.Equals(Connection?.Url, connection.Url, StringComparison.Ordinal);
            Connection = connection;
            LoadError = null;
            Changed?.Invoke(new SettingsChange(urlChanged));
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private sealed class SettingsFileModel
    {
        public SonarrSection? Sonarr { get; set; }

        public sealed class SonarrSection
        {
            public string? Url { get; set; }

            public string? ApiKey { get; set; }
        }
    }
}
