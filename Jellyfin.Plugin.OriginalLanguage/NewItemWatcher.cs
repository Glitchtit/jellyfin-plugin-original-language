using System.Collections.Concurrent;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.OriginalLanguage;

/// <summary>
/// Applies the rules to newly added movies and series once their first metadata download is saved
/// (only then are the TMDB ID and original language known). Existing items are never touched here.
/// </summary>
public sealed class NewItemWatcher : IHostedService, IDisposable
{
    private readonly ILibraryManager _libraryManager;
    private readonly IOriginalLanguageService _service;
    private readonly ILogger<NewItemWatcher> _logger;
    private readonly ConcurrentDictionary<Guid, byte> _pending = new();
    private readonly CancellationTokenSource _stopping = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="NewItemWatcher"/> class.
    /// </summary>
    /// <param name="libraryManager">Library manager.</param>
    /// <param name="service">Original language service.</param>
    /// <param name="logger">Logger.</param>
    public NewItemWatcher(ILibraryManager libraryManager, IOriginalLanguageService service, ILogger<NewItemWatcher> logger)
    {
        _libraryManager = libraryManager;
        _service = service;
        _logger = logger;
    }

    /// <summary>
    /// Gets or sets how often to check whether a library scan is still running.
    /// </summary>
    internal TimeSpan ScanPollInterval { get; set; } = TimeSpan.FromSeconds(10);

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _libraryManager.ItemAdded += OnItemAdded;
        _libraryManager.ItemUpdated += OnItemUpdated;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _libraryManager.ItemAdded -= OnItemAdded;
        _libraryManager.ItemUpdated -= OnItemUpdated;
        _stopping.Cancel();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public void Dispose() => _stopping.Dispose();

    internal void OnItemAdded(object? sender, ItemChangeEventArgs e)
    {
        if (e.Item is (Movie or Series) and { IsVirtualItem: false })
        {
            _pending.TryAdd(e.Item.Id, 0);
        }
    }

    internal async Task HandleItemUpdatedAsync(ItemChangeEventArgs e)
    {
        // Our own save uses MetadataEdit and the id is removed below, so this cannot loop.
        if (!e.UpdateReason.HasFlag(ItemUpdateType.MetadataDownload) || !_pending.TryRemove(e.Item.Id, out _))
        {
            return;
        }

        try
        {
            // A new series' seasons and episodes are refreshed by the scan after this event;
            // wait so our replace-all refresh doesn't race them.
            while (_libraryManager.IsScanRunning)
            {
                await Task.Delay(ScanPollInterval, _stopping.Token).ConfigureAwait(false);
            }

            var result = await _service.ApplyAsync(e.Item, _stopping.Token).ConfigureAwait(false);
            _logger.LogDebug("New item \"{Name}\": {Result}", e.Item.Name, result);
        }
        catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Applying original-language rules to new item \"{Name}\" failed", e.Item.Name);
        }
    }

    private void OnItemUpdated(object? sender, ItemChangeEventArgs e)
    {
        if (e.Item is null || !_pending.ContainsKey(e.Item.Id))
        {
            return;
        }

        // Don't block the refresh that raised the event.
        _ = Task.Run(() => HandleItemUpdatedAsync(e));
    }
}
