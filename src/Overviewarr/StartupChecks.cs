using Overviewarr.Settings;
using Overviewarr.Trash;

namespace Overviewarr;

/// <summary>The startup steps before requests are served (DESIGN.md section 11).</summary>
internal static class StartupChecks
{
    public static bool Run(WebApplication app)
    {
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Overviewarr.Startup");
        var paths = app.Services.GetRequiredService<AppPaths>();

        try
        {
            EnsureWritable(paths.ConfigDirectory);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogCritical("The config directory {Directory} isn't writable: {Error}", paths.ConfigDirectory, exception.Message);
            return false;
        }

        logger.LogInformation("Config directory: {Directory}", paths.ConfigDirectory);
        app.Services.GetRequiredService<SettingsStore>().Load();

        try
        {
            app.Services.GetRequiredService<TrashDefinitions>();
            logger.LogInformation("TRaSH Guides definitions loaded from {Directory}", paths.TrashDirectory);
        }
        catch (InvalidDataException exception)
        {
            logger.LogCritical("The TRaSH Guides definitions couldn't be read: {Error}", exception.Message);
            return false;
        }

        return true;
    }

    private static void EnsureWritable(string directory)
    {
        Directory.CreateDirectory(directory);
        var probe = Path.Combine(directory, $".write-test-{Guid.NewGuid():N}");
        File.WriteAllText(probe, string.Empty);
        File.Delete(probe);
    }
}
