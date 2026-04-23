using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using WinUtilDashboard.Models;
using WinUtilDashboard.Services.Interfaces;

namespace WinUtilDashboard.Services;

/// <summary>Scans a folder's children and returns the largest subfolders.</summary>
public sealed class FolderScannerService : IFolderScannerService
{
    private readonly ILogger<FolderScannerService> _logger;

    public FolderScannerService(ILogger<FolderScannerService> logger) => _logger = logger;

    public async Task<IReadOnlyList<FolderSize>> ScanTopFoldersAsync(
        string path,
        int top = 15,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Path is required.", nameof(path));

        var root = new DirectoryInfo(path);
        if (!root.Exists)
            throw new DirectoryNotFoundException(path);

        return await Task.Run(() =>
        {
            var results = new List<FolderSize>();
            foreach (var dir in root.EnumerateDirectories())
            {
                ct.ThrowIfCancellationRequested();
                progress?.Report(dir.Name);

                long size = GetDirectorySizeSafe(dir, ct);
                results.Add(new FolderSize(dir.FullName, size));
            }

            return (IReadOnlyList<FolderSize>)results
                .OrderByDescending(x => x.SizeBytes)
                .Take(top)
                .ToList();
        }, ct).ConfigureAwait(false);
    }

    private long GetDirectorySizeSafe(DirectoryInfo dir, CancellationToken ct)
    {
        long size = 0;
        try
        {
            foreach (var file in dir.EnumerateFiles("*", SearchOption.AllDirectories))
            {
                ct.ThrowIfCancellationRequested();
                try { size += file.Length; }
                catch (IOException)           { /* file removed mid-scan */ }
                catch (UnauthorizedAccessException) { /* no read permission */ }
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogDebug(ex, "Access denied: {Folder}", dir.FullName);
        }
        catch (DirectoryNotFoundException)
        {
            // folder deleted while scanning
        }
        return size;
    }
}
