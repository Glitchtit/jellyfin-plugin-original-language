using System.Net;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.OriginalLanguage.Tmdb;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Jellyfin.Plugin.OriginalLanguage.Tests;

public class TmdbOriginalLanguageClientTests
{
    private sealed class FakeHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
        }
    }

    private static (TmdbOriginalLanguageClient Client, FakeHandler Handler) Create(
        HttpStatusCode status, string body, string? apiKey = "k")
    {
        var handler = new FakeHandler(status, body);
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(() => new HttpClient(handler, false));
        var keys = new Mock<ITmdbApiKeyProvider>();
        keys.Setup(k => k.GetApiKey()).Returns(apiKey);
        return (new TmdbOriginalLanguageClient(factory.Object, keys.Object, NullLogger<TmdbOriginalLanguageClient>.Instance), handler);
    }

    [Fact]
    public async Task Movie_RequestsMovieEndpointWithKey_ReturnsOriginalLanguage()
    {
        var (client, handler) = Create(HttpStatusCode.OK, """{"id":550,"original_language":"sv"}""");

        var result = await client.GetOriginalLanguageAsync(BaseItemKind.Movie, "550", CancellationToken.None);

        Assert.Equal("sv", result);
        var uri = Assert.Single(handler.Requests);
        Assert.Equal("api.themoviedb.org", uri.Host);
        Assert.Equal("/3/movie/550", uri.AbsolutePath);
        Assert.Contains("api_key=k", uri.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Series_RequestsTvEndpoint()
    {
        var (client, handler) = Create(HttpStatusCode.OK, """{"original_language":"fi"}""");

        var result = await client.GetOriginalLanguageAsync(BaseItemKind.Series, "77", CancellationToken.None);

        Assert.Equal("fi", result);
        Assert.Equal("/3/tv/77", Assert.Single(handler.Requests).AbsolutePath);
    }

    [Fact]
    public async Task NotFound_ReturnsNull()
    {
        var (client, _) = Create(HttpStatusCode.NotFound, """{"status_code":34}""");

        Assert.Null(await client.GetOriginalLanguageAsync(BaseItemKind.Movie, "1", CancellationToken.None));
    }

    [Fact]
    public async Task MalformedJson_ReturnsNull()
    {
        var (client, _) = Create(HttpStatusCode.OK, "not json");

        Assert.Null(await client.GetOriginalLanguageAsync(BaseItemKind.Movie, "1", CancellationToken.None));
    }

    [Fact]
    public async Task MissingField_ReturnsNull()
    {
        var (client, _) = Create(HttpStatusCode.OK, """{"id":1}""");

        Assert.Null(await client.GetOriginalLanguageAsync(BaseItemKind.Movie, "1", CancellationToken.None));
    }

    [Fact]
    public async Task NoApiKey_ReturnsNullWithoutRequest()
    {
        var (client, handler) = Create(HttpStatusCode.OK, """{"original_language":"sv"}""", apiKey: null);

        Assert.Null(await client.GetOriginalLanguageAsync(BaseItemKind.Movie, "1", CancellationToken.None));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public void ApiKeyProvider_WithoutJellyfinProvidersAssembly_ReturnsNull()
    {
        var provider = new TmdbApiKeyProvider(NullLogger<TmdbApiKeyProvider>.Instance);

        Assert.Null(provider.GetApiKey());
    }
}
