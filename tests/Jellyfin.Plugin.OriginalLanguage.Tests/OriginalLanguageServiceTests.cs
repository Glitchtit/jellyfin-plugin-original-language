using Jellyfin.Data.Enums;
using Jellyfin.Plugin.OriginalLanguage.Configuration;
using Jellyfin.Plugin.OriginalLanguage.Tmdb;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Jellyfin.Plugin.OriginalLanguage.Tests;

public class OriginalLanguageServiceTests
{
    private readonly Mock<ILibraryManager> _libraryManager = new();
    private readonly Mock<IProviderManager> _providerManager = new();
    private readonly Mock<ITmdbOriginalLanguageClient> _tmdb = new();
    private readonly PluginConfiguration _config = new();
    private readonly OriginalLanguageService _service;

    public OriginalLanguageServiceTests()
    {
        _service = new OriginalLanguageService(
            _libraryManager.Object,
            _providerManager.Object,
            Mock.Of<IFileSystem>(),
            _tmdb.Object,
            () => _config,
            NullLogger<OriginalLanguageService>.Instance);
    }

    private void VerifyChangedOnce(BaseItem item)
    {
        _libraryManager.Verify(
            l => l.UpdateItemAsync(item, It.IsAny<BaseItem>(), ItemUpdateType.MetadataEdit, It.IsAny<CancellationToken>()),
            Times.Once);
        _providerManager.Verify(
            p => p.QueueRefresh(
                item.Id,
                It.Is<MetadataRefreshOptions>(o =>
                    o.MetadataRefreshMode == MetadataRefreshMode.FullRefresh
                    && o.ReplaceAllMetadata
                    && !o.ReplaceAllImages
                    && o.ForceSave),
                RefreshPriority.Normal),
            Times.Once);
    }

    private void VerifyUntouched()
    {
        _libraryManager.Verify(
            l => l.UpdateItemAsync(It.IsAny<BaseItem>(), It.IsAny<BaseItem>(), It.IsAny<ItemUpdateType>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _providerManager.Verify(
            p => p.QueueRefresh(It.IsAny<Guid>(), It.IsAny<MetadataRefreshOptions>(), It.IsAny<RefreshPriority>()),
            Times.Never);
    }

    [Fact]
    public async Task Apply_StoredSwedish_SetsSwedishAndRefreshes()
    {
        var movie = new Movie { Id = Guid.NewGuid(), OriginalLanguage = "sv" };

        var result = await _service.ApplyAsync(movie, CancellationToken.None);

        Assert.Equal(ApplyResult.Changed, result);
        Assert.Equal("sv", movie.PreferredMetadataLanguage);
        Assert.Equal("SE", movie.PreferredMetadataCountryCode);
        VerifyChangedOnce(movie);
        _tmdb.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Apply_AlreadyTarget_DoesNothing()
    {
        var movie = new Movie
        {
            Id = Guid.NewGuid(),
            OriginalLanguage = "sv",
            PreferredMetadataLanguage = "sv",
            PreferredMetadataCountryCode = "SE"
        };

        Assert.Equal(ApplyResult.AlreadySet, await _service.ApplyAsync(movie, CancellationToken.None));
        VerifyUntouched();
    }

    [Fact]
    public async Task Apply_OverwritesOtherPerItemLanguage()
    {
        var movie = new Movie { Id = Guid.NewGuid(), OriginalLanguage = "sv", PreferredMetadataLanguage = "en" };

        Assert.Equal(ApplyResult.Changed, await _service.ApplyAsync(movie, CancellationToken.None));
        Assert.Equal("sv", movie.PreferredMetadataLanguage);
    }

    [Fact]
    public async Task Apply_NoStoredLanguage_UsesTmdbForSeries()
    {
        var series = new Series { Id = Guid.NewGuid() };
        series.SetProviderId(MetadataProvider.Tmdb, "123");
        _tmdb.Setup(t => t.GetOriginalLanguageAsync(BaseItemKind.Series, "123", It.IsAny<CancellationToken>()))
            .ReturnsAsync("fi");

        var result = await _service.ApplyAsync(series, CancellationToken.None);

        Assert.Equal(ApplyResult.Changed, result);
        Assert.Equal("fi", series.PreferredMetadataLanguage);
        Assert.Equal("FI", series.PreferredMetadataCountryCode);
        VerifyChangedOnce(series);
    }

    [Fact]
    public async Task Apply_NoLanguageAndNoTmdbId_IsUnknown()
    {
        var movie = new Movie { Id = Guid.NewGuid() };

        Assert.Equal(ApplyResult.Unknown, await _service.ApplyAsync(movie, CancellationToken.None));
        _tmdb.VerifyNoOtherCalls();
        VerifyUntouched();
    }

    [Fact]
    public async Task Apply_TmdbReturnsNothing_IsUnknown()
    {
        var movie = new Movie { Id = Guid.NewGuid() };
        movie.SetProviderId(MetadataProvider.Tmdb, "9");
        _tmdb.Setup(t => t.GetOriginalLanguageAsync(BaseItemKind.Movie, "9", It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        Assert.Equal(ApplyResult.Unknown, await _service.ApplyAsync(movie, CancellationToken.None));
        VerifyUntouched();
    }

    [Fact]
    public async Task Apply_English_IsNotMatching()
    {
        var movie = new Movie { Id = Guid.NewGuid(), OriginalLanguage = "en" };

        Assert.Equal(ApplyResult.NotMatching, await _service.ApplyAsync(movie, CancellationToken.None));
        Assert.Null(movie.PreferredMetadataLanguage);
        VerifyUntouched();
    }

    [Fact]
    public async Task Apply_SwedishDisabled_IsNotMatching()
    {
        _config.EnableSwedish = false;
        var movie = new Movie { Id = Guid.NewGuid(), OriginalLanguage = "sv" };

        Assert.Equal(ApplyResult.NotMatching, await _service.ApplyAsync(movie, CancellationToken.None));
        VerifyUntouched();
    }

    [Fact]
    public async Task Reset_SetItem_ClearsAndRefreshes()
    {
        var movie = new Movie
        {
            Id = Guid.NewGuid(),
            PreferredMetadataLanguage = "sv",
            PreferredMetadataCountryCode = "SE"
        };

        Assert.True(await _service.ResetAsync(movie, CancellationToken.None));
        Assert.Null(movie.PreferredMetadataLanguage);
        Assert.Null(movie.PreferredMetadataCountryCode);
        VerifyChangedOnce(movie);
    }

    [Fact]
    public async Task Reset_OnlyCountrySet_StillClears()
    {
        var movie = new Movie { Id = Guid.NewGuid(), PreferredMetadataCountryCode = "FI" };

        Assert.True(await _service.ResetAsync(movie, CancellationToken.None));
        Assert.Null(movie.PreferredMetadataCountryCode);
    }

    [Fact]
    public async Task Reset_NothingSet_DoesNothing()
    {
        var movie = new Movie { Id = Guid.NewGuid() };

        Assert.False(await _service.ResetAsync(movie, CancellationToken.None));
        VerifyUntouched();
    }

    [Fact]
    public async Task Apply_MutatesAndSavesCachedInstance_NotTheQueriedCopy()
    {
        var id = Guid.NewGuid();
        var queried = new Movie { Id = id, OriginalLanguage = "sv" };
        var cached = new Movie { Id = id, OriginalLanguage = "sv" };
        _libraryManager.Setup(l => l.GetItemById(id)).Returns(cached);

        Assert.Equal(ApplyResult.Changed, await _service.ApplyAsync(queried, CancellationToken.None));

        Assert.Equal("sv", cached.PreferredMetadataLanguage);
        Assert.Equal("SE", cached.PreferredMetadataCountryCode);
        VerifyChangedOnce(cached);
    }

    [Fact]
    public async Task Apply_CachedInstanceAlreadySet_IsAlreadySet()
    {
        var id = Guid.NewGuid();
        var queried = new Movie { Id = id, OriginalLanguage = "sv" };
        var cached = new Movie { Id = id, OriginalLanguage = "sv", PreferredMetadataLanguage = "sv", PreferredMetadataCountryCode = "SE" };
        _libraryManager.Setup(l => l.GetItemById(id)).Returns(cached);

        Assert.Equal(ApplyResult.AlreadySet, await _service.ApplyAsync(queried, CancellationToken.None));
        VerifyUntouched();
    }

    [Fact]
    public async Task Reset_ClearsAndSavesCachedInstance()
    {
        var id = Guid.NewGuid();
        var queried = new Movie { Id = id, PreferredMetadataLanguage = "sv", PreferredMetadataCountryCode = "SE" };
        var cached = new Movie { Id = id, PreferredMetadataLanguage = "sv", PreferredMetadataCountryCode = "SE" };
        _libraryManager.Setup(l => l.GetItemById(id)).Returns(cached);

        Assert.True(await _service.ResetAsync(queried, CancellationToken.None));

        Assert.Null(cached.PreferredMetadataLanguage);
        Assert.Null(cached.PreferredMetadataCountryCode);
        VerifyChangedOnce(cached);
    }

    [Fact]
    public void GetAllMoviesAndSeries_QueriesNonVirtualMoviesAndSeriesRecursively()
    {
        var items = new List<BaseItem> { new Movie(), new Series() };
        _libraryManager.Setup(l => l.GetItemList(It.Is<InternalItemsQuery>(q =>
                q.Recursive
                && q.IsVirtualItem == false
                && q.IncludeItemTypes.Length == 2
                && q.IncludeItemTypes.Contains(BaseItemKind.Movie)
                && q.IncludeItemTypes.Contains(BaseItemKind.Series))))
            .Returns(items);

        Assert.Same(items, _service.GetAllMoviesAndSeries());
    }
}
