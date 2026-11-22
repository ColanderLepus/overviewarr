using Overviewarr.Settings;

namespace Overviewarr.Tests.Settings;

public class SettingsRulesTests
{
    private static readonly SonarrConnection Saved = new("http://sonarr:8989", "saved-key");

    [Theory]
    [InlineData("http://sonarr:8989/", "http://sonarr:8989")]
    [InlineData("  https://sonarr.example.com/sonarr//  ", "https://sonarr.example.com/sonarr")]
    [InlineData("http://192.168.1.10:8989", "http://192.168.1.10:8989")]
    public void Trailing_slashes_are_removed(string input, string expected)
    {
        Assert.True(SettingsRules.TryNormalizeUrl(input, out var url, out var error));
        Assert.Equal(expected, url);
        Assert.Null(error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("sonarr:8989")]
    [InlineData("ftp://sonarr:8989")]
    [InlineData("/api/v3")]
    public void Invalid_urls_are_rejected(string input)
    {
        Assert.False(SettingsRules.TryNormalizeUrl(input, out _, out var error));
        Assert.NotNull(error);
    }

    [Fact]
    public void Entered_key_is_used()
    {
        Assert.True(SettingsRules.TryResolveApiKey("http://other:8989", "new-key", Saved, out var key, out _));
        Assert.Equal("new-key", key);
    }

    [Fact]
    public void Empty_key_keeps_the_saved_key_for_the_saved_url()
    {
        Assert.True(SettingsRules.TryResolveApiKey("http://sonarr:8989", "", Saved, out var key, out _));
        Assert.Equal("saved-key", key);
    }

    [Fact]
    public void Saved_key_is_never_sent_to_another_url()
    {
        Assert.False(SettingsRules.TryResolveApiKey("http://attacker:8989", "", Saved, out var key, out var error));
        Assert.Equal(string.Empty, key);
        Assert.Contains("entered again", error);
    }

    [Fact]
    public void Key_is_required_on_first_run()
    {
        Assert.True(SettingsRules.IsApiKeyRequired("http://sonarr:8989", saved: null));
        Assert.False(SettingsRules.TryResolveApiKey("http://sonarr:8989", "  ", saved: null, out _, out var error));
        Assert.Equal("The API key is required.", error);
    }

    [Fact]
    public void Key_is_required_when_the_url_changes()
    {
        Assert.False(SettingsRules.IsApiKeyRequired("http://sonarr:8989", Saved));
        Assert.True(SettingsRules.IsApiKeyRequired("http://sonarr:9999", Saved));
    }
}
