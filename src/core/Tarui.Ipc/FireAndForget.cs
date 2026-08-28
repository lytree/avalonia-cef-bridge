namespace Tarui.Ipc;

/// <summary>
/// Shared helper that drives fire-and-forget notification tasks. Replaces the ad-hoc
/// <c>async void FireAndForget</c> pattern that previously lived in each shell service and silently
/// swallowed exceptions: with this helper every swallowed exception is logged with a stable category
/// so a downstream log sink (or unit test) can observe bridge failures without crashing the dispatch
/// loop.
/// </summary>
public static class FireAndForget
{
    /// <summary>Runs <paramref name="task"/> on the thread pool, logging any exception it raises.</summary>
    public static void Run(ValueTask task, ILogger? logger = null)
    {
        if (task.IsCompletedSuccessfully)
        {
            return;
        }

        _ = AwaitAndLogAsync(task, logger ?? NullLogger.Instance);
    }

    /// <summary>Runs <paramref name="task"/> on the thread pool, logging any exception it raises.</summary>
    public static void Run(Task task, ILogger? logger = null)
    {
        if (task.IsCompletedSuccessfully)
        {
            return;
        }

        _ = AwaitAndLogAsync(task, logger ?? NullLogger.Instance);
    }

    private static async Task AwaitAndLogAsync(ValueTask task, ILogger logger)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Best-effort notifications may be cancelled by the dispatcher shutting down; not an error.
        }
        catch (Exception exception)
        {
            logger.NotificationFailed(exception);
        }
    }

    private static async Task AwaitAndLogAsync(Task task, ILogger logger)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Best-effort notifications may be cancelled by the dispatcher shutting down; not an error.
        }
        catch (Exception exception)
        {
            logger.NotificationFailed(exception);
        }
    }
}
