using Jellyfin.Plugin.OriginalLanguage.Configuration;

namespace Jellyfin.Plugin.OriginalLanguage.Tests;

public class LanguageRulesTests
{
    private static readonly PluginConfiguration AllOn = new();

    [Theory]
    [InlineData("sv")]
    [InlineData("SV")]
    [InlineData("swe")]
    [InlineData(" sv ")]
    public void Swedish_MapsToSwedishSweden(string original)
    {
        Assert.Equal(new LanguageTarget("sv", "SE"), LanguageRules.GetTarget(original, AllOn));
    }

    [Theory]
    [InlineData("fi")]
    [InlineData("FI")]
    [InlineData("fin")]
    public void Finnish_MapsToFinnishFinland(string original)
    {
        Assert.Equal(new LanguageTarget("fi", "FI"), LanguageRules.GetTarget(original, AllOn));
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    [InlineData("")]
    [InlineData(null)]
    public void OtherOrMissing_ReturnsNull(string? original)
    {
        Assert.Null(LanguageRules.GetTarget(original, AllOn));
    }

    [Fact]
    public void SwedishDisabled_SkipsSwedishButKeepsFinnish()
    {
        var config = new PluginConfiguration { EnableSwedish = false };

        Assert.Null(LanguageRules.GetTarget("sv", config));
        Assert.Equal(new LanguageTarget("fi", "FI"), LanguageRules.GetTarget("fi", config));
    }

    [Fact]
    public void FinnishDisabled_SkipsFinnishButKeepsSwedish()
    {
        var config = new PluginConfiguration { EnableFinnish = false };

        Assert.Null(LanguageRules.GetTarget("fi", config));
        Assert.Equal(new LanguageTarget("sv", "SE"), LanguageRules.GetTarget("sv", config));
    }

    [Fact]
    public void Defaults_BothEnabled()
    {
        Assert.True(AllOn.EnableSwedish);
        Assert.True(AllOn.EnableFinnish);
    }
}
