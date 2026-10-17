using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.OriginalLanguage.ScheduledTasks;

/// <summary>
/// Manual task: applies the enabled original-language rules to every movie and series.
/// </summary>
public class ApplyRulesTask : IScheduledTask
{
    private readonly IOriginalLanguageService _service;
    private readonly ILogger<ApplyRulesTask> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ApplyRulesTask"/> class.
    /// </summary>
    /// <param name="service">Original language service.</param>
    /// <param name="logger">Logger.</param>
    public ApplyRulesTask(IOriginalLanguageService service, ILogger<ApplyRulesTask> logger)
    {
        _service = service;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "Apply original-language rules to all media";

    /// <inheritdoc />
    public string Key => "OriginalLanguageApplyRules";

    /// <inheritdoc />
    public string Description => "Gives every movie and series whose original language is an enabled language (Swedish, Finnish) metadata in that language, and refreshes it.";

    /// <inheritdoc />
    public string Category => LibrarySweep.Category;

    /// <summary>
    /// Gets or sets the pause after each TMDB lookup, to stay under TMDB's rate limit.
    /// </summary>
    internal TimeSpan TmdbLookupDelay { get; set; } = TimeSpan.FromMilliseconds(50);

    /// <inheritdoc />
    public Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        return LibrarySweep.RunAsync(
            Name,
            _service.GetAllMoviesAndSeries(),
            async (item, ct) =>
            {
                var lookup = NeedsTmdbLookup(item);
                var result = await _service.ApplyAsync(item, ct).ConfigureAwait(false);
                if (lookup && TmdbLookupDelay > TimeSpan.Zero)
                {
                    await Task.Delay(TmdbLookupDelay, ct).ConfigureAwait(false);
                }

                return result.ToString();
            },
            progress,
            _logger,
            cancellationToken);
    }

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() => [];

    /// <summary>
    /// Gets whether applying the rules to this item will call TMDB.
    /// </summary>
    /// <param name="item">The item.</param>
    /// <returns>True when the original language is not stored but a TMDB ID exists.</returns>
    internal static bool NeedsTmdbLookup(BaseItem item)
        => string.IsNullOrWhiteSpace(item.OriginalLanguage)
           && !string.IsNullOrEmpty(item.GetProviderId(MetadataProvider.Tmdb));
}
