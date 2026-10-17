using System.Globalization;
using System.Text.Json;
using Jellyfin.Data.Enums;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.OriginalLanguage.Tmdb;

/// <summary>
/// Looks up a title's original language on TMDB.
/// </summary>
public interface ITmdbOriginalLanguageClient
{
    /// <summary>
    /// Gets the original language of a TMDB movie or TV show.
    /// </summary>
    /// <param name="kind"><see cref="BaseItemKind.Movie"/> or <see cref="BaseItemKind.Series"/>.</param>
    /// <param name="tmdbId">TMDB ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The two-letter original language, or null when unavailable.</returns>
    Task<string?> GetOriginalLanguageAsync(BaseItemKind kind, string tmdbId, CancellationToken cancellationToken);
}

/// <summary>
/// TMDB v3 API client for the <c>original_language</c> field.
/// </summary>
public class TmdbOriginalLanguageClient : ITmdbOriginalLanguageClient
{
    private const string BaseUrl = "https://api.themoviedb.org/3/";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ITmdbApiKeyProvider _apiKeyProvider;
    private readonly ILogger<TmdbOriginalLanguageClient> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="TmdbOriginalLanguageClient"/> class.
    /// </summary>
    /// <param name="httpClientFactory">HTTP client factory.</param>
    /// <param name="apiKeyProvider">API key provider.</param>
    /// <param name="logger">Logger.</param>
    public TmdbOriginalLanguageClient(
        IHttpClientFactory httpClientFactory,
        ITmdbApiKeyProvider apiKeyProvider,
        ILogger<TmdbOriginalLanguageClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _apiKeyProvider = apiKeyProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<string?> GetOriginalLanguageAsync(BaseItemKind kind, string tmdbId, CancellationToken cancellationToken)
    {
        var apiKey = _apiKeyProvider.GetApiKey();
        if (string.IsNullOrEmpty(apiKey))
        {
            return null;
        }

        var path = kind == BaseItemKind.Series ? "tv" : "movie";
        var url = string.Format(
            CultureInfo.InvariantCulture,
            "{0}{1}/{2}?api_key={3}",
            BaseUrl,
            path,
            Uri.EscapeDataString(tmdbId),
            Uri.EscapeDataString(apiKey));

        try
        {
            using var client = _httpClientFactory.CreateClient(nameof(TmdbOriginalLanguageClient));
            using var response = await client.GetAsync(url, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("TMDB lookup for {Kind} {TmdbId} failed with HTTP {Status}", path, tmdbId, (int)response.StatusCode);
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("original_language", out var language)
                && language.ValueKind == JsonValueKind.String)
            {
                return language.GetString();
            }

            _logger.LogWarning("TMDB response for {Kind} {TmdbId} has no original_language", path, tmdbId);
            return null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "TMDB lookup for {Kind} {TmdbId} failed", path, tmdbId);
            return null;
        }
    }
}
