using Jellyfin.Data.Enums;
using Jellyfin.Plugin.OriginalLanguage.Configuration;
using Jellyfin.Plugin.OriginalLanguage.Tmdb;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.OriginalLanguage;

/// <summary>
/// Outcome of applying the rules to one item.
/// </summary>
public enum ApplyResult
{
    /// <summary>The per-item language was changed and a refresh queued.</summary>
    Changed,

    /// <summary>The item already has the target language and country.</summary>
    AlreadySet,

    /// <summary>The original language is known but no enabled rule matches.</summary>
    NotMatching,

    /// <summary>The original language could not be determined.</summary>
    Unknown
}

/// <summary>
/// Sets or clears per-item metadata languages based on original language.
/// </summary>
public interface IOriginalLanguageService
{
    /// <summary>
    /// Gets the item's original language: the stored value, else a TMDB lookup.
    /// </summary>
    /// <param name="item">Movie or series.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The original language, or null.</returns>
    Task<string?> ResolveOriginalLanguageAsync(BaseItem item, CancellationToken cancellationToken);

    /// <summary>
    /// Applies the enabled rules to an item.
    /// </summary>
    /// <param name="item">Movie or series.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>What happened.</returns>
    Task<ApplyResult> ApplyAsync(BaseItem item, CancellationToken cancellationToken);

    /// <summary>
    /// Clears the item's per-item metadata language and country so the library default applies.
    /// </summary>
    /// <param name="item">Movie or series.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True when something was cleared.</returns>
    Task<bool> ResetAsync(BaseItem item, CancellationToken cancellationToken);

    /// <summary>
    /// Gets every non-virtual movie and series on the server.
    /// </summary>
    /// <returns>The items.</returns>
    IReadOnlyList<BaseItem> GetAllMoviesAndSeries();
}

/// <inheritdoc />
public class OriginalLanguageService : IOriginalLanguageService
{
    private readonly ILibraryManager _libraryManager;
    private readonly IProviderManager _providerManager;
    private readonly IFileSystem _fileSystem;
    private readonly ITmdbOriginalLanguageClient _tmdbClient;
    private readonly Func<PluginConfiguration> _getConfiguration;
    private readonly ILogger<OriginalLanguageService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="OriginalLanguageService"/> class.
    /// </summary>
    /// <param name="libraryManager">Library manager.</param>
    /// <param name="providerManager">Provider manager.</param>
    /// <param name="fileSystem">File system.</param>
    /// <param name="tmdbClient">TMDB client.</param>
    /// <param name="getConfiguration">Returns the current plugin configuration.</param>
    /// <param name="logger">Logger.</param>
    public OriginalLanguageService(
        ILibraryManager libraryManager,
        IProviderManager providerManager,
        IFileSystem fileSystem,
        ITmdbOriginalLanguageClient tmdbClient,
        Func<PluginConfiguration> getConfiguration,
        ILogger<OriginalLanguageService> logger)
    {
        _libraryManager = libraryManager;
        _providerManager = providerManager;
        _fileSystem = fileSystem;
        _tmdbClient = tmdbClient;
        _getConfiguration = getConfiguration;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<string?> ResolveOriginalLanguageAsync(BaseItem item, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(item.OriginalLanguage))
        {
            return item.OriginalLanguage;
        }

        var tmdbId = item.GetProviderId(MetadataProvider.Tmdb);
        if (string.IsNullOrEmpty(tmdbId))
        {
            return null;
        }

        return await _tmdbClient.GetOriginalLanguageAsync(item.GetBaseItemKind(), tmdbId, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<ApplyResult> ApplyAsync(BaseItem item, CancellationToken cancellationToken)
    {
        var original = await ResolveOriginalLanguageAsync(item, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(original))
        {
            return ApplyResult.Unknown;
        }

        var target = LanguageRules.GetTarget(original, _getConfiguration());
        if (target is null)
        {
            return ApplyResult.NotMatching;
        }

        if (string.Equals(item.PreferredMetadataLanguage, target.Language, StringComparison.OrdinalIgnoreCase)
            && string.Equals(item.PreferredMetadataCountryCode, target.Country, StringComparison.OrdinalIgnoreCase))
        {
            return ApplyResult.AlreadySet;
        }

        _logger.LogInformation(
            "Setting metadata language {Language}-{Country} on \"{Name}\" (original language {Original})",
            target.Language,
            target.Country,
            item.Name,
            original);

        item.PreferredMetadataLanguage = target.Language;
        item.PreferredMetadataCountryCode = target.Country;
        await SaveAndRefreshAsync(item, cancellationToken).ConfigureAwait(false);
        return ApplyResult.Changed;
    }

    /// <inheritdoc />
    public async Task<bool> ResetAsync(BaseItem item, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(item.PreferredMetadataLanguage) && string.IsNullOrEmpty(item.PreferredMetadataCountryCode))
        {
            return false;
        }

        _logger.LogInformation("Clearing per-item metadata language on \"{Name}\"", item.Name);

        item.PreferredMetadataLanguage = null;
        item.PreferredMetadataCountryCode = null;
        await SaveAndRefreshAsync(item, cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <inheritdoc />
    public IReadOnlyList<BaseItem> GetAllMoviesAndSeries()
    {
        return _libraryManager.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = [BaseItemKind.Movie, BaseItemKind.Series],
            Recursive = true,
            IsVirtualItem = false
        });
    }

    private async Task SaveAndRefreshAsync(BaseItem item, CancellationToken cancellationToken)
    {
        await _libraryManager.UpdateItemAsync(item, item.GetParent(), ItemUpdateType.MetadataEdit, cancellationToken).ConfigureAwait(false);

        // Same options as "Refresh metadata → Replace all metadata" in the UI, minus images.
        var options = new MetadataRefreshOptions(new DirectoryService(_fileSystem))
        {
            MetadataRefreshMode = MetadataRefreshMode.FullRefresh,
            ImageRefreshMode = MetadataRefreshMode.Default,
            ReplaceAllMetadata = true,
            ReplaceAllImages = false,
            ForceSave = true,
            RemoveOldMetadata = true,
            IsAutomated = false
        };

        _providerManager.QueueRefresh(item.Id, options, RefreshPriority.Normal);
    }
}
