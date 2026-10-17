using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.OriginalLanguage.ScheduledTasks;

/// <summary>
/// Manual task: clears the per-item metadata language on every movie and series so the
/// library default (English) applies again.
/// </summary>
public class ResetToEnglishTask : IScheduledTask
{
    private readonly IOriginalLanguageService _service;
    private readonly ILogger<ResetToEnglishTask> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ResetToEnglishTask"/> class.
    /// </summary>
    /// <param name="service">Original language service.</param>
    /// <param name="logger">Logger.</param>
    public ResetToEnglishTask(IOriginalLanguageService service, ILogger<ResetToEnglishTask> logger)
    {
        _service = service;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "Reset all media to English";

    /// <inheritdoc />
    public string Key => "OriginalLanguageResetToEnglish";

    /// <inheritdoc />
    public string Description => "Clears the per-item metadata language on every movie and series so the library's language (English) applies again, and refreshes the changed items.";

    /// <inheritdoc />
    public string Category => LibrarySweep.Category;

    /// <inheritdoc />
    public Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        return LibrarySweep.RunAsync(
            Name,
            _service.GetAllMoviesAndSeries(),
            async (item, ct) => await _service.ResetAsync(item, ct).ConfigureAwait(false) ? "Cleared" : "Unchanged",
            progress,
            _logger,
            cancellationToken);
    }

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() => [];
}
