using MediaBrowser.Controller.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.OriginalLanguage.ScheduledTasks;

/// <summary>
/// Runs an action over a list of items with progress, per-item error isolation and a summary log.
/// </summary>
internal static class LibrarySweep
{
    /// <summary>
    /// Category shown for the plugin's tasks in the dashboard.
    /// </summary>
    public const string Category = "Original Language Metadata";

    public static async Task RunAsync(
        string taskName,
        IReadOnlyList<BaseItem> items,
        Func<BaseItem, CancellationToken, Task<string>> action,
        IProgress<double> progress,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);

        for (var i = 0; i < items.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string outcome;
            try
            {
                outcome = await action(items[i], cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "{Task}: failed on \"{Name}\" ({Id})", taskName, items[i].Name, items[i].Id);
                outcome = "Failed";
            }

            counts[outcome] = counts.GetValueOrDefault(outcome) + 1;
            progress.Report(100.0 * (i + 1) / items.Count);
        }

        progress.Report(100);
        logger.LogInformation(
            "{Task}: processed {Total} items ({Summary})",
            taskName,
            items.Count,
            string.Join(", ", counts.OrderBy(c => c.Key, StringComparer.Ordinal).Select(c => $"{c.Key}: {c.Value}")));
    }
}
