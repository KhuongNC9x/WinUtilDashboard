using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Extensions.Logging;

namespace WinUtilDashboard.Infrastructure;

/// <summary>
/// Centralized error surface: logs and shows a user-friendly dialog.
/// Keeps the MessageBox text free of stack traces.
/// </summary>
public static class UserErrorReporter
{
    public static void Report(ILogger logger, string friendlyMessage, Exception ex)
    {
        logger.LogError(ex, "{Message}", friendlyMessage);

        var body = $"{friendlyMessage}\n\n{ex.Message}\n\n(See log file for details.)";
        Application.Current?.Dispatcher.Invoke(() =>
            MessageBox.Show(body, "Error", MessageBoxButton.OK, MessageBoxImage.Error));
    }

    /// <summary>
    /// Helper: run an async action and show a friendly error if it fails.
    /// Swallows <see cref="OperationCanceledException"/> (user-requested cancel).
    /// </summary>
    public static async Task TryRunAsync(
        ILogger logger,
        string friendlyMessage,
        Func<CancellationToken, Task> action,
        CancellationToken ct = default)
    {
        try
        {
            await action(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("Operation cancelled: {What}", friendlyMessage);
        }
        catch (Exception ex)
        {
            Report(logger, friendlyMessage, ex);
        }
    }
}