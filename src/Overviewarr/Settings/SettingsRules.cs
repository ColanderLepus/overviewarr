namespace Overviewarr.Settings;

/// <summary>The rules for the Sonarr fields on the settings page (DESIGN.md section 4).</summary>
public static class SettingsRules
{
    public const string DefaultUrl = "http://sonarr:8989";

    /// <summary>Validates the URL field and removes trailing slashes.</summary>
    public static bool TryNormalizeUrl(string? input, out string url, out string? error)
    {
        url = (input ?? string.Empty).Trim().TrimEnd('/');

        if (url.Length == 0)
        {
            error = "The URL is required.";
            return false;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            error = "The URL must be an absolute http or https URL.";
            return false;
        }

        error = null;
        return true;
    }

    /// <summary>Whether the API key field must be filled in for this (normalized) URL.</summary>
    public static bool IsApiKeyRequired(string normalizedUrl, SonarrConnection? saved) =>
        saved is null || !string.Equals(saved.Url, normalizedUrl, StringComparison.Ordinal);

    /// <summary>
    /// The API key to use with the form's values. An empty field means "keep the current key",
    /// which is only allowed while the URL is the saved one: the saved key is sent only to the saved URL.
    /// </summary>
    public static bool TryResolveApiKey(string normalizedUrl, string? enteredKey, SonarrConnection? saved,
        out string apiKey, out string? error)
    {
        var key = (enteredKey ?? string.Empty).Trim();

        if (key.Length > 0)
        {
            apiKey = key;
            error = null;
            return true;
        }

        if (!IsApiKeyRequired(normalizedUrl, saved))
        {
            apiKey = saved!.ApiKey;
            error = null;
            return true;
        }

        apiKey = string.Empty;
        error = saved is null
            ? "The API key is required."
            : "The URL has changed, so the API key must be entered again.";
        return false;
    }
}
