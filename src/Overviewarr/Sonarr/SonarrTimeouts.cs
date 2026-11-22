namespace Overviewarr.Sonarr;

/// <summary>Timeouts for Sonarr calls (DESIGN.md sections 4 and 5).</summary>
public sealed record SonarrTimeouts(TimeSpan Default, TimeSpan ReleaseSearch, TimeSpan ConnectionTest)
{
    public static SonarrTimeouts Standard { get; } =
        new(TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(3), TimeSpan.FromSeconds(10));
}
