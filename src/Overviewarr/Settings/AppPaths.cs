namespace Overviewarr.Settings;

/// <summary>Where the app keeps its settings and finds the TRaSH Guides definitions.</summary>
public sealed record AppPaths(string ConfigDirectory, string TrashDirectory)
{
    public string SettingsFile => Path.Combine(ConfigDirectory, "settings.json");

    /// <summary>
    /// Reads <c>ConfigDirectory</c> (default <c>/config</c>) and <c>TrashDirectory</c>
    /// (default <c>trash</c> next to the app's files). Relative paths are resolved against the content root.
    /// </summary>
    public static AppPaths FromConfiguration(IConfiguration configuration, string contentRoot)
    {
        var configDirectory = configuration["ConfigDirectory"] ?? "/config";
        var trashDirectory = configuration["TrashDirectory"] ?? Path.Combine(AppContext.BaseDirectory, "trash");

        return new AppPaths(
            Path.GetFullPath(configDirectory, contentRoot),
            Path.GetFullPath(trashDirectory, contentRoot));
    }
}
