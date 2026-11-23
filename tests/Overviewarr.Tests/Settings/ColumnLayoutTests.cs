using System.Text.Json;
using Overviewarr.Settings;

namespace Overviewarr.Tests.Settings;

public class ColumnLayoutTests
{
    private static readonly SonarrConnection Connection = new("http://sonarr:8989", "key");

    private static readonly ColumnLayout[] Defaults =
    [
        new("Season", true, "90px"),
        new("Episodes", true, "210px"),
        new("Quality", true, "130px"),
        new("Title", false, "420px"),
    ];

    private static SettingsStore CreateStore(string directory) =>
        new(new AppPaths(directory, directory), new ListLogger<SettingsStore>());

    [Fact]
    public void No_saved_layout_gives_the_defaults() =>
        Assert.Equal(Defaults, ColumnLayouts.Resolve(Defaults, null));

    [Fact]
    public void Saved_order_visibility_and_widths_are_applied()
    {
        ColumnLayout[] saved =
        [
            new("Title", true, "300px"),
            new("Season", true, "90px"),
            new("Quality", false, "150.5px"),
            new("Episodes", true, "200px"),
        ];

        Assert.Equal(saved, ColumnLayouts.Resolve(Defaults, saved));
    }

    [Fact]
    public void Column_missing_from_the_saved_layout_comes_after_the_saved_ones_with_its_defaults()
    {
        ColumnLayout[] saved = [new("Quality", false, "150px"), new("Season", true, "80px")];

        Assert.Equal(
        [
            new ColumnLayout("Quality", false, "150px"),
            new ColumnLayout("Season", true, "80px"),
            new ColumnLayout("Episodes", true, "210px"),
            new ColumnLayout("Title", false, "420px"),
        ], ColumnLayouts.Resolve(Defaults, saved));
    }

    [Fact]
    public void Unknown_titles_repeated_titles_and_invalid_widths_are_ignored()
    {
        ColumnLayout[] saved =
        [
            new("Removed column", true, "100px"),
            new("Quality", true, "12em"),
            new("Quality", false, "99px"),
            new("Season", false, null),
            new("Episodes", true, "-5px"),
        ];

        var resolved = ColumnLayouts.Resolve(Defaults, saved);

        Assert.Equal(
        [
            new ColumnLayout("Quality", true, "130px"),
            new ColumnLayout("Season", false, "90px"),
            new ColumnLayout("Episodes", true, "210px"),
            new ColumnLayout("Title", false, "420px"),
        ], resolved);
    }

    [Fact]
    public async Task Saved_layout_loads_again_and_keeps_the_connection()
    {
        using var directory = new TempDirectory();
        var store = CreateStore(directory.Path);
        await store.SaveAsync(Connection);
        ColumnLayout[] layout = [new("Title", true, "300px"), new("Season", false, "90px")];

        await store.SaveColumnLayoutAsync(layout);

        var loaded = CreateStore(directory.Path);
        loaded.Load();
        Assert.Equal(Connection, loaded.Connection);
        Assert.Equal(layout, loaded.ColumnLayout);
    }

    [Fact]
    public async Task Saving_the_connection_keeps_the_layout()
    {
        using var directory = new TempDirectory();
        var store = CreateStore(directory.Path);
        await store.SaveAsync(Connection);
        ColumnLayout[] layout = [new("Title", true, "300px")];
        await store.SaveColumnLayoutAsync(layout);

        await store.SaveAsync(new SonarrConnection("http://other:8989", "other-key"));

        var loaded = CreateStore(directory.Path);
        loaded.Load();
        Assert.Equal(new SonarrConnection("http://other:8989", "other-key"), loaded.Connection);
        Assert.Equal(layout, loaded.ColumnLayout);
    }

    [Fact]
    public async Task Reset_removes_the_layout_and_keeps_the_connection()
    {
        using var directory = new TempDirectory();
        var store = CreateStore(directory.Path);
        await store.SaveAsync(Connection);
        await store.SaveColumnLayoutAsync([new ColumnLayout("Title", true, "300px")]);

        await store.ResetColumnLayoutAsync();

        Assert.Null(store.ColumnLayout);
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(directory.Path, "settings.json")));
        Assert.False(json.RootElement.TryGetProperty("releaseGrid", out _));

        var loaded = CreateStore(directory.Path);
        loaded.Load();
        Assert.Equal(Connection, loaded.Connection);
        Assert.Null(loaded.ColumnLayout);
    }

    [Fact]
    public async Task File_has_the_documented_shape()
    {
        using var directory = new TempDirectory();
        var store = CreateStore(directory.Path);
        await store.SaveAsync(Connection);

        await store.SaveColumnLayoutAsync([new ColumnLayout("Season", true, "90px")]);

        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(directory.Path, "settings.json")));
        var column = Assert.Single(json.RootElement.GetProperty("releaseGrid").GetProperty("columns").EnumerateArray());
        Assert.Equal("Season", column.GetProperty("title").GetString());
        Assert.True(column.GetProperty("visible").GetBoolean());
        Assert.Equal("90px", column.GetProperty("width").GetString());
        Assert.Equal("key", json.RootElement.GetProperty("sonarr").GetProperty("apiKey").GetString());
    }

    [Fact]
    public async Task Saves_and_resets_raise_the_layout_event_but_an_unchanged_layout_is_not_written_again()
    {
        using var directory = new TempDirectory();
        var store = CreateStore(directory.Path);
        await store.SaveAsync(Connection);
        var events = 0;
        store.ColumnLayoutChanged += () => events++;

        await store.SaveColumnLayoutAsync([new ColumnLayout("Season", true, "90px")]);
        await store.SaveColumnLayoutAsync([new ColumnLayout("Season", true, "90px")]);
        await store.ResetColumnLayoutAsync();
        await store.ResetColumnLayoutAsync();

        Assert.Equal(2, events);
    }

    [Fact]
    public async Task Saving_the_connection_does_not_raise_the_layout_event()
    {
        using var directory = new TempDirectory();
        var store = CreateStore(directory.Path);
        var events = 0;
        store.ColumnLayoutChanged += () => events++;

        await store.SaveAsync(Connection);

        Assert.Equal(0, events);
    }

    [Fact]
    public async Task Hand_edited_entries_without_a_title_are_skipped_and_a_missing_visible_means_shown()
    {
        using var directory = new TempDirectory();
        await File.WriteAllTextAsync(Path.Combine(directory.Path, "settings.json"), """
            {
              "sonarr": { "url": "http://sonarr:8989", "apiKey": "key" },
              "releaseGrid": { "columns": [ { "visible": false }, null, { "title": "Season", "width": "80px" } ] }
            }
            """);
        var store = CreateStore(directory.Path);

        store.Load();

        Assert.Equal([new ColumnLayout("Season", true, "80px")], store.ColumnLayout);
    }
}
