using Jellyfin.Plugin.OriginalLanguage.ScheduledTasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Jellyfin.Plugin.OriginalLanguage.Tests;

public class ScheduledTaskTests
{
    private sealed class RecordingProgress : IProgress<double>
    {
        public List<double> Values { get; } = [];

        public void Report(double value) => Values.Add(value);
    }

    private readonly Mock<IOriginalLanguageService> _service = new();
    private readonly List<BaseItem> _items =
    [
        new Movie { Id = Guid.NewGuid(), Name = "A", OriginalLanguage = "sv" },
        new Movie { Id = Guid.NewGuid(), Name = "B", OriginalLanguage = "en" },
        new Movie { Id = Guid.NewGuid(), Name = "C", OriginalLanguage = "fi" }
    ];

    public ScheduledTaskTests()
    {
        _service.Setup(s => s.GetAllMoviesAndSeries()).Returns(_items);
    }

    private ApplyRulesTask CreateApply() =>
        new(_service.Object, NullLogger<ApplyRulesTask>.Instance) { TmdbLookupDelay = TimeSpan.Zero };

    private ResetToEnglishTask CreateReset() =>
        new(_service.Object, NullLogger<ResetToEnglishTask>.Instance);

    [Fact]
    public async Task Apply_AppliesEveryItem_AndEndsAt100()
    {
        var progress = new RecordingProgress();

        await CreateApply().ExecuteAsync(progress, CancellationToken.None);

        foreach (var item in _items)
        {
            _service.Verify(s => s.ApplyAsync(item, It.IsAny<CancellationToken>()), Times.Once);
        }

        Assert.Equal(100, progress.Values[^1]);
    }

    [Fact]
    public async Task Apply_ItemThrows_ContinuesWithRest()
    {
        _service.Setup(s => s.ApplyAsync(_items[0], It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("boom"));

        await CreateApply().ExecuteAsync(new RecordingProgress(), CancellationToken.None);

        _service.Verify(s => s.ApplyAsync(_items[1], It.IsAny<CancellationToken>()), Times.Once);
        _service.Verify(s => s.ApplyAsync(_items[2], It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Apply_Cancelled_Throws()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => CreateApply().ExecuteAsync(new RecordingProgress(), cts.Token));
        _service.Verify(s => s.ApplyAsync(It.IsAny<BaseItem>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Reset_ResetsEveryItem()
    {
        var progress = new RecordingProgress();

        await CreateReset().ExecuteAsync(progress, CancellationToken.None);

        foreach (var item in _items)
        {
            _service.Verify(s => s.ResetAsync(item, It.IsAny<CancellationToken>()), Times.Once);
        }

        Assert.Equal(100, progress.Values[^1]);
    }

    [Fact]
    public async Task Reset_ItemThrows_ContinuesWithRest()
    {
        _service.Setup(s => s.ResetAsync(_items[1], It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("boom"));

        await CreateReset().ExecuteAsync(new RecordingProgress(), CancellationToken.None);

        _service.Verify(s => s.ResetAsync(_items[2], It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void Tasks_HaveSpecNamesKeysAndNoDefaultTriggers()
    {
        var apply = CreateApply();
        var reset = CreateReset();

        Assert.Equal("Apply original-language rules to all media", apply.Name);
        Assert.Equal("OriginalLanguageApplyRules", apply.Key);
        Assert.Equal("Reset all media to English", reset.Name);
        Assert.Equal("OriginalLanguageResetToEnglish", reset.Key);
        Assert.Equal("Original Language Metadata", apply.Category);
        Assert.Equal("Original Language Metadata", reset.Category);
        Assert.Empty(apply.GetDefaultTriggers());
        Assert.Empty(reset.GetDefaultTriggers());
    }

    [Fact]
    public void Apply_NeedsTmdbDelay_OnlyWhenNoStoredLanguageButTmdbId()
    {
        var stored = new Movie { OriginalLanguage = "sv" };
        stored.SetProviderId(MetadataProvider.Tmdb, "1");
        var lookup = new Movie();
        lookup.SetProviderId(MetadataProvider.Tmdb, "2");
        var none = new Movie();

        Assert.False(ApplyRulesTask.NeedsTmdbLookup(stored));
        Assert.True(ApplyRulesTask.NeedsTmdbLookup(lookup));
        Assert.False(ApplyRulesTask.NeedsTmdbLookup(none));
    }
}
