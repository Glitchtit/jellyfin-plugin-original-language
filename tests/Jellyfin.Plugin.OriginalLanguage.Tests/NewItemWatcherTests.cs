using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Jellyfin.Plugin.OriginalLanguage.Tests;

public class NewItemWatcherTests
{
    private readonly Mock<IOriginalLanguageService> _service = new();
    private readonly Mock<ILibraryManager> _libraryManager = new();
    private readonly NewItemWatcher _watcher;

    public NewItemWatcherTests()
    {
        _watcher = new NewItemWatcher(_libraryManager.Object, _service.Object, NullLogger<NewItemWatcher>.Instance)
        {
            ScanPollInterval = TimeSpan.FromMilliseconds(1)
        };
    }

    private static ItemChangeEventArgs Args(BaseItem item, ItemUpdateType reason = ItemUpdateType.None)
        => new() { Item = item, UpdateReason = reason };

    private void VerifyApplied(BaseItem item, Times times)
        => _service.Verify(s => s.ApplyAsync(item, It.IsAny<CancellationToken>()), times);

    [Fact]
    public async Task AddedThenMetadataDownload_AppliesOnce()
    {
        var movie = new Movie { Id = Guid.NewGuid() };

        _watcher.OnItemAdded(null, Args(movie));
        await _watcher.HandleItemUpdatedAsync(Args(movie, ItemUpdateType.MetadataImport | ItemUpdateType.MetadataDownload));
        await _watcher.HandleItemUpdatedAsync(Args(movie, ItemUpdateType.MetadataDownload));

        VerifyApplied(movie, Times.Once());
    }

    [Fact]
    public async Task UpdateWithoutAdd_NeverApplies()
    {
        var movie = new Movie { Id = Guid.NewGuid() };

        await _watcher.HandleItemUpdatedAsync(Args(movie, ItemUpdateType.MetadataDownload));

        VerifyApplied(movie, Times.Never());
    }

    [Fact]
    public async Task AddedThenImportOnly_WaitsForDownload()
    {
        var series = new Series { Id = Guid.NewGuid() };

        _watcher.OnItemAdded(null, Args(series));
        await _watcher.HandleItemUpdatedAsync(Args(series, ItemUpdateType.MetadataImport));
        VerifyApplied(series, Times.Never());

        await _watcher.HandleItemUpdatedAsync(Args(series, ItemUpdateType.MetadataDownload));
        VerifyApplied(series, Times.Once());
    }

    [Fact]
    public async Task OwnMetadataEdit_DoesNotApply()
    {
        var movie = new Movie { Id = Guid.NewGuid() };

        _watcher.OnItemAdded(null, Args(movie));
        await _watcher.HandleItemUpdatedAsync(Args(movie, ItemUpdateType.MetadataEdit));

        VerifyApplied(movie, Times.Never());
    }

    [Fact]
    public async Task EpisodeAdded_Ignored()
    {
        var episode = new Episode { Id = Guid.NewGuid() };

        _watcher.OnItemAdded(null, Args(episode));
        await _watcher.HandleItemUpdatedAsync(Args(episode, ItemUpdateType.MetadataDownload));

        VerifyApplied(episode, Times.Never());
    }

    [Fact]
    public async Task VirtualSeriesAdded_Ignored()
    {
        var series = new Series { Id = Guid.NewGuid(), IsVirtualItem = true };

        _watcher.OnItemAdded(null, Args(series));
        await _watcher.HandleItemUpdatedAsync(Args(series, ItemUpdateType.MetadataDownload));

        VerifyApplied(series, Times.Never());
    }

    [Fact]
    public async Task ApplyThrows_IsSwallowed()
    {
        var movie = new Movie { Id = Guid.NewGuid() };
        _service.Setup(s => s.ApplyAsync(movie, It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("boom"));

        _watcher.OnItemAdded(null, Args(movie));
        await _watcher.HandleItemUpdatedAsync(Args(movie, ItemUpdateType.MetadataDownload));

        VerifyApplied(movie, Times.Once());
    }

    [Fact]
    public async Task WaitsForLibraryScanToFinish_BeforeApplying()
    {
        var series = new Series { Id = Guid.NewGuid() };
        _libraryManager.SetupSequence(l => l.IsScanRunning).Returns(true).Returns(true).Returns(false);

        _watcher.OnItemAdded(null, Args(series));
        await _watcher.HandleItemUpdatedAsync(Args(series, ItemUpdateType.MetadataDownload));

        _libraryManager.VerifyGet(l => l.IsScanRunning, Times.Exactly(3));
        VerifyApplied(series, Times.Once());
    }
}
