using Jellyfin.Plugin.OriginalLanguage.Configuration;

namespace Jellyfin.Plugin.OriginalLanguage;

/// <summary>
/// The per-item metadata language and country to apply.
/// </summary>
/// <param name="Language">Two-letter metadata language, e.g. "sv".</param>
/// <param name="Country">Two-letter metadata country, e.g. "SE".</param>
public record LanguageTarget(string Language, string Country);

/// <summary>
/// Maps an item's original language to the metadata language it should use.
/// </summary>
public static class LanguageRules
{
    private static readonly LanguageTarget Swedish = new("sv", "SE");
    private static readonly LanguageTarget Finnish = new("fi", "FI");

    /// <summary>
    /// Gets the metadata target for an original language, or null when no enabled rule matches.
    /// </summary>
    /// <param name="originalLanguage">Original language as two- or three-letter code.</param>
    /// <param name="config">Plugin configuration.</param>
    /// <returns>The target, or null.</returns>
    public static LanguageTarget? GetTarget(string? originalLanguage, PluginConfiguration config)
    {
        switch (originalLanguage?.Trim().ToLowerInvariant())
        {
            case "sv" or "swe" when config.EnableSwedish:
                return Swedish;
            case "fi" or "fin" when config.EnableFinnish:
                return Finnish;
            default:
                return null;
        }
    }
}
