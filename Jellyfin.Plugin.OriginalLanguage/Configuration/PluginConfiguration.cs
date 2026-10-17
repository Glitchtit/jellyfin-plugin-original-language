using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.OriginalLanguage.Configuration;

/// <summary>
/// Plugin settings: which original languages get their own metadata language.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Gets or sets a value indicating whether Swedish-original titles get Swedish metadata.
    /// </summary>
    public bool EnableSwedish { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether Finnish-original titles get Finnish metadata.
    /// </summary>
    public bool EnableFinnish { get; set; } = true;
}
