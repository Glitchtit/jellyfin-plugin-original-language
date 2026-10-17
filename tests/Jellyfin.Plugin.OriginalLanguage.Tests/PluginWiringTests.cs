using Jellyfin.Plugin.OriginalLanguage.Tmdb;
using MediaBrowser.Controller;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;

namespace Jellyfin.Plugin.OriginalLanguage.Tests;

public class PluginWiringTests
{
    [Fact]
    public void Registrator_RegistersServicesAndWatcher()
    {
        var services = new ServiceCollection();

        new PluginServiceRegistrator().RegisterServices(services, Mock.Of<IServerApplicationHost>());

        Assert.Contains(services, d => d.ServiceType == typeof(ITmdbApiKeyProvider) && d.Lifetime == ServiceLifetime.Singleton);
        Assert.Contains(services, d => d.ServiceType == typeof(ITmdbOriginalLanguageClient) && d.Lifetime == ServiceLifetime.Singleton);
        Assert.Contains(services, d => d.ServiceType == typeof(IOriginalLanguageService) && d.Lifetime == ServiceLifetime.Singleton);
        Assert.Contains(services, d => d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(NewItemWatcher));
    }

    [Fact]
    public void ConfigPage_IsEmbedded()
    {
        var assembly = typeof(OriginalLanguage.Plugin).Assembly;

        Assert.Contains("Jellyfin.Plugin.OriginalLanguage.Configuration.configPage.html", assembly.GetManifestResourceNames());
    }
}
