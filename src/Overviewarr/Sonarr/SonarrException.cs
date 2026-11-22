namespace Overviewarr.Sonarr;

/// <summary>A failed Sonarr call, with a message meant for the user (DESIGN.md section 12).</summary>
public sealed class SonarrException(string message, bool isUnauthorized = false, Exception? innerException = null)
    : Exception(message, innerException)
{
    /// <summary>Sonarr answered 401: the page links to the settings.</summary>
    public bool IsUnauthorized { get; } = isUnauthorized;
}
