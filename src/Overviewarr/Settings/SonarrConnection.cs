namespace Overviewarr.Settings;

/// <summary>A saved Sonarr connection. <see cref="Url"/> is normalized (no trailing slash).</summary>
public sealed record SonarrConnection(string Url, string ApiKey);

/// <summary>Raised after the settings are saved.</summary>
public sealed record SettingsChange(bool UrlChanged);
