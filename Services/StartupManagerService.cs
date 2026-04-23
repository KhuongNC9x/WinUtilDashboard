using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using WinUtilDashboard.Models;
using WinUtilDashboard.Services.Interfaces;

namespace WinUtilDashboard.Services;

/// <summary>Manages Windows startup entries (Registry Run keys + Startup folder).</summary>
public sealed class StartupManagerService : IStartupManagerService
{
    private readonly ILogger<StartupManagerService> _logger;

    private static readonly (RegistryHive Hive, string Path, string Source)[] RunKeys =
    {
        (RegistryHive.CurrentUser,  @"Software\Microsoft\Windows\CurrentVersion\Run",                  "HKCU\\Run"),
        (RegistryHive.LocalMachine, @"Software\Microsoft\Windows\CurrentVersion\Run",                  "HKLM\\Run"),
        (RegistryHive.LocalMachine, @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run",      "HKLM\\Run (x86)"),
    };

    public StartupManagerService(ILogger<StartupManagerService> logger) => _logger = logger;

    public async Task<IReadOnlyList<StartupEntry>> GetStartupAppsAsync(CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            var list = new List<StartupEntry>();

            foreach (var (hive, path, source) in RunKeys)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Default);
                    using var key = baseKey.OpenSubKey(path);
                    if (key is null) continue;

                    foreach (var name in key.GetValueNames())
                    {
                        var value = key.GetValue(name)?.ToString() ?? "";
                        list.Add(new StartupEntry(name, value, source));
                    }
                }
                catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException)
                {
                    _logger.LogDebug(ex, "Skipping registry path (no access): {Path}", path);
                }
            }

            var folders = new[]
            {
                (Environment.GetFolderPath(Environment.SpecialFolder.Startup),       "Startup (User)"),
                (Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), "Startup (All Users)")
            };

            foreach (var (folder, label) in folders)
            {
                if (!Directory.Exists(folder)) continue;
                foreach (var file in Directory.EnumerateFiles(folder))
                    list.Add(new StartupEntry(Path.GetFileName(file), file, label));
            }

            return (IReadOnlyList<StartupEntry>)list;
        }, ct).ConfigureAwait(false);
    }

    public async Task<bool> DisableStartupAsync(string name, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;

        return await Task.Run(() =>
        {
            bool found = false;

            foreach (var (hive, path, _) in RunKeys)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Default);
                    using var key = baseKey.OpenSubKey(path, writable: true);
                    if (key is null) continue;

                    foreach (var valueName in key.GetValueNames())
                    {
                        if (!valueName.Equals(name, StringComparison.OrdinalIgnoreCase)) continue;
                        try
                        {
                            key.DeleteValue(valueName);
                            _logger.LogInformation("Removed startup registry: {Name} from {Path}", valueName, path);
                            found = true;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to delete registry value {Name}", valueName);
                        }
                    }
                }
                catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException)
                {
                    _logger.LogDebug(ex, "Skipping registry path: {Path}", path);
                }
            }

            foreach (var folder in new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.Startup),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup)
            })
            {
                if (!Directory.Exists(folder)) continue;
                foreach (var file in Directory.EnumerateFiles(folder))
                {
                    if (!Path.GetFileName(file).Equals(name, StringComparison.OrdinalIgnoreCase)) continue;
                    try
                    {
                        File.Delete(file);
                        _logger.LogInformation("Removed startup file: {File}", file);
                        found = true;
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        _logger.LogWarning(ex, "Failed to delete startup file {File}", file);
                    }
                }
            }

            return found;
        }, ct).ConfigureAwait(false);
    }
}
