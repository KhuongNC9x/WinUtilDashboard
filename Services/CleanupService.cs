using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using WinUtilDashboard.Models;
using WinUtilDashboard.Services.Interfaces;

namespace WinUtilDashboard.Services;

/// <summary>
/// Cleans temp folders and browser caches using streaming enumeration
/// (no upfront file list allocation) with cancellation and progress support.
/// </summary>
public sealed class CleanupService : ICleanupService
{
    private readonly ILogger<CleanupService> _logger;

    public CleanupService(ILogger<CleanupService> logger) => _logger = logger;

    public Task<CleanupResult> CleanUserTempAsync(IProgress<int>? progress, CancellationToken ct)
        => CleanFolderAsync(Path.GetTempPath(), progress, ct);

    public Task<CleanupResult> CleanWindowsTempAsync(IProgress<int>? progress, CancellationToken ct)
        => CleanFolderAsync(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp"), progress, ct);

    public Task<CleanupResult> CleanWindowsUpdateCacheAsync(IProgress<int>? progress, CancellationToken ct)
        => CleanFolderAsync(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "SoftwareDistribution", "Download"),
            progress, ct);

    public async Task<CleanupResult> CleanBrowserCacheAsync(IProgress<int>? progress, CancellationToken ct)
    {
        var result = CleanupResult.Empty;
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        foreach (var path in GetBrowserCachePaths(userProfile))
        {
            ct.ThrowIfCancellationRequested();
            result = result.Combine(await CleanFolderAsync(path, progress, ct).ConfigureAwait(false));
        }
        return result;
    }

    private static IEnumerable<string> GetBrowserCachePaths(string userProfile)
    {
        // Chrome (modern layout places files in Cache_Data)
        yield return Path.Combine(userProfile, @"AppData\Local\Google\Chrome\User Data\Default\Cache\Cache_Data");
        yield return Path.Combine(userProfile, @"AppData\Local\Google\Chrome\User Data\Default\Cache");

        // Edge
        yield return Path.Combine(userProfile, @"AppData\Local\Microsoft\Edge\User Data\Default\Cache\Cache_Data");
        yield return Path.Combine(userProfile, @"AppData\Local\Microsoft\Edge\User Data\Default\Cache");

        // Firefox - enumerate profile dirs
        var firefoxBase = Path.Combine(userProfile, @"AppData\Local\Mozilla\Firefox\Profiles");
        if (Directory.Exists(firefoxBase))
        {
            foreach (var profileDir in Directory.EnumerateDirectories(firefoxBase))
                yield return Path.Combine(profileDir, "cache2");
        }
    }

    private async Task<CleanupResult> CleanFolderAsync(
        string folderPath,
        IProgress<int>? progress,
        CancellationToken ct)
    {
        if (!Directory.Exists(folderPath))
        {
            _logger.LogDebug("Skip cleanup - folder not found: {Folder}", folderPath);
            return CleanupResult.Empty;
        }

        return await Task.Run(() =>
        {
            int deleted = 0;
            long freedBytes = 0;

            // EnumerateFiles streams results - does not allocate the full list upfront.
            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(folderPath, "*", SearchOption.AllDirectories);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                _logger.LogWarning(ex, "Cannot enumerate {Folder}", folderPath);
                return CleanupResult.Empty;
            }

            foreach (var file in files)
            {
                ct.ThrowIfCancellationRequested();

                try
                {
                    var fi = new FileInfo(file);
                    long size = fi.Length;

                    // Some cache files are read-only
                    if ((fi.Attributes & FileAttributes.ReadOnly) != 0)
                        fi.Attributes &= ~FileAttributes.ReadOnly;

                    fi.Delete();
                    freedBytes += size;
                    deleted++;

                    if (deleted % 100 == 0) progress?.Report(deleted);
                }
                catch (IOException)
                {
                    // File in use - normal for live caches
                }
                catch (UnauthorizedAccessException)
                {
                    // No permission - skip silently
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to delete {File}", file);
                }
            }

            progress?.Report(deleted);
            return new CleanupResult(deleted, freedBytes / 1024.0 / 1024.0);
        }, ct).ConfigureAwait(false);
    }
}
