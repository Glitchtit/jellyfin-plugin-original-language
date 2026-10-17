using Jellyfin.Plugin.OriginalLanguage.Configuration;
using Jellyfin.Plugin.OriginalLanguage.Tmdb;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.IO;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.OriginalLanguage;

/// <summary>
/// Registers the plugin's services. Scheduled tasks are discovered by Jellyfin automatically.
/// </summary>
public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddSingleton<ITmdbApiKeyProvider, TmdbApiKeyProvider>();
        serviceCollection.AddSingleton<ITmdbOriginalLanguageClient, TmdbOriginalLanguageClient>();
        serviceCollection.AddSingleton<IOriginalLanguageService>(sp => new OriginalLanguageService(
            sp.GetRequiredService<ILibraryManager>(),
            sp.GetRequiredService<IProviderManager>(),
            sp.GetRequiredService<IFileSystem>(),
            sp.GetRequiredService<ITmdbOriginalLanguageClient>(),
            () => Plugin.Instance?.Configuration ?? new PluginConfiguration(),
            sp.GetRequiredService<ILogger<OriginalLanguageService>>()));
        serviceCollection.AddHostedService<NewItemWatcher>();
    }
}
