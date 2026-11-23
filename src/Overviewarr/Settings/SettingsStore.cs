using System.Text.Json;
using System.Text.Json.Serialization;

namespace Overviewarr.Settings;

/// <summary>Reads and writes <c>settings.json</c>, and holds the current Sonarr connection and the release grid's column layout.</summary>
public sealed class SettingsStore(AppPaths paths, ILogger<SettingsStore> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private readonly SemaphoreSlim _writeLock = new(1, 1);

    /// <summary>The saved connection, or null while unconfigured.</summary>
    public SonarrConnection? Connection { get; private set; }

    /// <summary>The saved column layout of the release grid, or null while the default layout is in use.</summary>
    public IReadOnlyList<ColumnLayout>? ColumnLayout { get; private set; }

    /// <summary>Why <c>settings.json</c> couldn't be read at startup, or null.</summary>
    public string? LoadError { get; private set; }

    public bool IsConfigured => Connection is not null;

    /// <summary>Raised after the connection is saved.</summary>
    public event Action<SettingsChange>? Changed;

    /// <summary>Raised after the column layout is saved or reset.</summary>
    public event Action? ColumnLayoutChanged;

    /// <summary>Loads <c>settings.json</c>. A file that can't be read leaves the app unconfigured.</summary>
    public void Load()
    {
        Connection = null;
        ColumnLayout = null;
        LoadError = null;

        if (!File.Exists(paths.SettingsFile))
        {
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
            ColumnLayout = model?.ReleaseGrid?.Columns?
                .Where(c => !string.IsNullOrEmpty(c?.Title))
                .Select(c => new ColumnLayout(c!.Title!, c.Visible ?? true, c.Width))
                .ToList();
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException or IOException or UnauthorizedAccessException)
        {
            LoadError = exception.Message;
            logger.LogError("settings.json couldn't be read: {Error}", exception.Message);
        }
    }

    /// <summary>Saves the connection atomically, keeping the column layout, and raises <see cref="Changed"/>.</summary>
    public async Task SaveAsync(SonarrConnection connection, CancellationToken cancellationToken = default)
    {
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            await WriteAsync(connection, ColumnLayout, cancellationToken);

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

    /// <summary>
    /// Saves the release grid's column layout atomically, keeping the connection, and raises <see cref="ColumnLayoutChanged"/>.
    /// A layout equal to the saved one isn't written again.
    /// </summary>
    public async Task SaveColumnLayoutAsync(IReadOnlyList<ColumnLayout> layout, CancellationToken cancellationToken = default)
    {
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            if (ColumnLayout is not null && ColumnLayout.SequenceEqual(layout))
            {
                return;
            }

            var connection = Connection ?? throw new InvalidOperationException("The column layout can't be saved before a Sonarr connection.");
            var copy = layout.ToList();
            await WriteAsync(connection, copy, cancellationToken);

            ColumnLayout = copy;
            ColumnLayoutChanged?.Invoke();
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>Removes the saved column layout, keeping the connection, and raises <see cref="ColumnLayoutChanged"/>.</summary>
    public async Task ResetColumnLayoutAsync(CancellationToken cancellationToken = default)
    {
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            if (ColumnLayout is null || Connection is null)
            {
                return;
            }

            await WriteAsync(Connection, null, cancellationToken);

            ColumnLayout = null;
            ColumnLayoutChanged?.Invoke();
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private Task WriteAsync(SonarrConnection connection, IReadOnlyList<ColumnLayout>? layout, CancellationToken cancellationToken)
    {
        var model = new SettingsFileModel
        {
            Sonarr = new SettingsFileModel.SonarrSection { Url = connection.Url, ApiKey = connection.ApiKey },
            ReleaseGrid = layout is null
                ? null
                : new SettingsFileModel.ReleaseGridSection
                {
                    Columns = layout.Select(c => (SettingsFileModel.ColumnSection?)new SettingsFileModel.ColumnSection { Title = c.Title, Visible = c.Visible, Width = c.Width }).ToList(),
                },
        };

        return AtomicFile.WriteAllTextAsync(paths.SettingsFile, JsonSerializer.Serialize(model, JsonOptions), cancellationToken);
    }

    private sealed class SettingsFileModel
    {
        public SonarrSection? Sonarr { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public ReleaseGridSection? ReleaseGrid { get; set; }

        public sealed class SonarrSection
        {
            public string? Url { get; set; }

            public string? ApiKey { get; set; }
        }

        public sealed class ReleaseGridSection
        {
            public List<ColumnSection?>? Columns { get; set; }
        }

        public sealed class ColumnSection
        {
            public string? Title { get; set; }

            public bool? Visible { get; set; }

            public string? Width { get; set; }
        }
    }
}
