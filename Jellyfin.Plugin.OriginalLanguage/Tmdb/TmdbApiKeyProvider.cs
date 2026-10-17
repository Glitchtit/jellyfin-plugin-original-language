using System.Reflection;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.OriginalLanguage.Tmdb;

/// <summary>
/// Supplies the TMDB API key.
/// </summary>
public interface ITmdbApiKeyProvider
{
    /// <summary>
    /// Gets the API key, or null when none is available.
    /// </summary>
    /// <returns>The key or null.</returns>
    string? GetApiKey();
}

/// <summary>
/// Reuses the key of Jellyfin's built-in TMDb plugin (its configured key, else its bundled default).
/// The built-in plugin lives in MediaBrowser.Providers, which is not published as a package,
/// so it is reached by reflection.
/// </summary>
public class TmdbApiKeyProvider : ITmdbApiKeyProvider
{
    private const string ProvidersAssembly = "MediaBrowser.Providers";
    private const string TmdbPluginType = "MediaBrowser.Providers.Plugins.Tmdb.Plugin";
    private const string TmdbUtilsType = "MediaBrowser.Providers.Plugins.Tmdb.TmdbUtils";

    private readonly ILogger<TmdbApiKeyProvider> _logger;
    private string? _defaultKey;
    private bool _warned;

    /// <summary>
    /// Initializes a new instance of the <see cref="TmdbApiKeyProvider"/> class.
    /// </summary>
    /// <param name="logger">Logger.</param>
    public TmdbApiKeyProvider(ILogger<TmdbApiKeyProvider> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public string? GetApiKey()
    {
        try
        {
            var assembly = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == ProvidersAssembly);

            if (assembly is not null)
            {
                // Read the configured key every time so changes in the TMDb plugin settings apply.
                var configured = GetConfiguredKey(assembly);
                if (!string.IsNullOrEmpty(configured))
                {
                    return configured;
                }

                _defaultKey ??= assembly.GetType(TmdbUtilsType)
                    ?.GetField("ApiKey", BindingFlags.Public | BindingFlags.Static)
                    ?.GetValue(null) as string;

                if (!string.IsNullOrEmpty(_defaultKey))
                {
                    return _defaultKey;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Reading the TMDb API key from Jellyfin failed");
        }

        if (!_warned)
        {
            _warned = true;
            _logger.LogWarning("Could not find Jellyfin's TMDb API key; TMDB lookups for items without a stored original language are disabled");
        }

        return null;
    }

    private static string? GetConfiguredKey(Assembly assembly)
    {
        var pluginType = assembly.GetType(TmdbPluginType);
        var instance = pluginType?.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
        var configuration = instance?.GetType().GetProperty("Configuration")?.GetValue(instance);
        return configuration?.GetType().GetProperty("TmdbApiKey")?.GetValue(configuration) as string;
    }
}
