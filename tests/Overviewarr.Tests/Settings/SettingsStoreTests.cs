using Overviewarr.Settings;

namespace Overviewarr.Tests.Settings;

public class SettingsStoreTests
{
    private static SettingsStore CreateStore(string directory, ListLogger<SettingsStore>? logger = null) =>
        new(new AppPaths(directory, directory), logger ?? new ListLogger<SettingsStore>());

    [Fact]
    public void Missing_file_means_unconfigured_without_an_error()
    {
        using var directory = new TempDirectory();
        var store = CreateStore(directory.Path);

        store.Load();

        Assert.False(store.IsConfigured);
        Assert.Null(store.LoadError);
    }

    [Fact]
    public async Task Saved_connection_loads_again()
    {
        using var directory = new TempDirectory();
        await CreateStore(directory.Path).SaveAsync(new SonarrConnection("http://sonarr:8989", "key"));

        var store = CreateStore(directory.Path);
        store.Load();

        Assert.Equal(new SonarrConnection("http://sonarr:8989", "key"), store.Connection);
        Assert.Equal(["settings.json"], Directory.GetFiles(directory.Path).Select(Path.GetFileName));
    }

    [Fact]
    public async Task File_has_the_documented_shape()
    {
        using var directory = new TempDirectory();
        await CreateStore(directory.Path).SaveAsync(new SonarrConnection("http://sonarr:8989", "key"));

        var json = await File.ReadAllTextAsync(Path.Combine(directory.Path, "settings.json"));

        Assert.Contains("\"sonarr\"", json);
        Assert.Contains("\"url\": \"http://sonarr:8989\"", json);
        Assert.Contains("\"apiKey\": \"key\"", json);
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("{ \"sonarr\": { \"url\": \"http://sonarr:8989\" } }")]
    [InlineData("{ \"sonarr\": { \"url\": \"not a url\", \"apiKey\": \"key\" } }")]
    public void Unreadable_file_leaves_the_app_unconfigured_with_an_error(string contents)
    {
        using var directory = new TempDirectory();
        File.WriteAllText(Path.Combine(directory.Path, "settings.json"), contents);
        var logger = new ListLogger<SettingsStore>();
        var store = CreateStore(directory.Path, logger);

        store.Load();

        Assert.False(store.IsConfigured);
        Assert.NotNull(store.LoadError);
        Assert.Contains(logger.Messages, m => m.StartsWith("Error: settings.json couldn't be read"));
    }

    [Fact]
    public async Task Saving_reports_whether_the_url_changed()
    {
        using var directory = new TempDirectory();
        var store = CreateStore(directory.Path);
        var changes = new List<SettingsChange>();
        store.Changed += changes.Add;

        await store.SaveAsync(new SonarrConnection("http://sonarr:8989", "key"));
        await store.SaveAsync(new SonarrConnection("http://sonarr:8989", "other-key"));
        await store.SaveAsync(new SonarrConnection("http://sonarr2:8989", "other-key"));

        Assert.Equal([true, false, true], changes.Select(c => c.UrlChanged));
    }

    [Fact]
    public async Task Saving_clears_a_load_error()
    {
        using var directory = new TempDirectory();
        File.WriteAllText(Path.Combine(directory.Path, "settings.json"), "{ not json");
        var store = CreateStore(directory.Path);
        store.Load();

        await store.SaveAsync(new SonarrConnection("http://sonarr:8989", "key"));

        Assert.Null(store.LoadError);
        Assert.True(store.IsConfigured);
    }
}
