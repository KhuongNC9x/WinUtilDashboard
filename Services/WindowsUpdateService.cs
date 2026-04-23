using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.ServiceProcess;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using WinUtilDashboard.Models;
using WinUtilDashboard.Services.Interfaces;

namespace WinUtilDashboard.Services;

/// <summary>
/// Manages Windows Update related services. Uses <see cref="ServiceController"/>
/// for read operations (language-independent) and shells out to <c>sc.exe</c>
/// only to change the startup type (which ServiceController cannot do).
/// </summary>
public sealed class WindowsUpdateService : IWindowsUpdateService
{
    private readonly ILogger<WindowsUpdateService> _logger;

    private static readonly IReadOnlyDictionary<string, string> Services = new Dictionary<string, string>
    {
        ["wuauserv"] = "Windows Update",
        ["bits"]     = "Background Intelligent Transfer",
        ["dosvc"]    = "Delivery Optimization",
    };

    private static readonly TimeSpan ServiceOpTimeout = TimeSpan.FromSeconds(30);

    public WindowsUpdateService(ILogger<WindowsUpdateService> logger) => _logger = logger;

    public async Task<IReadOnlyList<ServiceStatus>> CheckStatusAsync(CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            var list = new List<ServiceStatus>();
            foreach (var (name, display) in Services)
            {
                ct.ThrowIfCancellationRequested();
                list.Add(QueryStatus(name, display));
            }
            return (IReadOnlyList<ServiceStatus>)list;
        }, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<string>> DisableAsync(CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            var results = new List<string>();
            foreach (var (name, display) in Services)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    StopService(name);
                    SetStartMode(name, "disabled");
                    results.Add($"Stopped and disabled: {display} ({name})");
                    _logger.LogInformation("Disabled service {Service}", name);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to disable {Service}", name);
                    results.Add($"FAILED {display}: {ex.Message}");
                }
            }
            return (IReadOnlyList<string>)results;
        }, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<string>> EnableAsync(CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            var results = new List<string>();
            foreach (var (name, display) in Services)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    // Default start modes - wuauserv/bits = manual, dosvc = delayed-auto
                    var mode = name == "dosvc" ? "delayed-auto" : "demand";
                    SetStartMode(name, mode);
                    StartService(name);
                    results.Add($"Re-enabled: {display} ({name})");
                    _logger.LogInformation("Enabled service {Service} as {Mode}", name, mode);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to enable {Service}", name);
                    results.Add($"FAILED {display}: {ex.Message}");
                }
            }
            return (IReadOnlyList<string>)results;
        }, ct).ConfigureAwait(false);
    }

    // ---------- internals ----------

    private ServiceStatus QueryStatus(string name, string display)
    {
        try
        {
            using var sc = new ServiceController(name);
            return new ServiceStatus(
                ServiceName: name,
                DisplayName: display,
                State:       sc.Status.ToString(),       // Running, Stopped...
                StartMode:   sc.StartType.ToString());   // Automatic, Manual, Disabled
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cannot query service {Service}", name);
            return new ServiceStatus(name, display, "Unknown", "Unknown");
        }
    }

    private static void StopService(string name)
    {
        using var sc = new ServiceController(name);
        if (sc.Status == ServiceControllerStatus.Running || sc.Status == ServiceControllerStatus.StartPending)
        {
            sc.Stop();
            sc.WaitForStatus(ServiceControllerStatus.Stopped, ServiceOpTimeout);
        }
    }

    private static void StartService(string name)
    {
        using var sc = new ServiceController(name);
        if (sc.Status == ServiceControllerStatus.Stopped)
        {
            sc.Start();
            sc.WaitForStatus(ServiceControllerStatus.Running, ServiceOpTimeout);
        }
    }

    /// <summary>
    /// Changes service start type. .NET's <see cref="ServiceController"/> cannot
    /// set this on .NET (Framework) and the <c>ChangeServiceConfig</c> P/Invoke is
    /// verbose, so we shell out to <c>sc.exe</c> — but only for this one narrow case.
    /// </summary>
    private static void SetStartMode(string name, string mode)
    {
        var psi = new ProcessStartInfo("sc.exe", $"config {name} start= {mode}")
        {
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
            CreateNoWindow         = true
        };

        using var p = Process.Start(psi)
            ?? throw new InvalidOperationException("Could not start sc.exe");

        p.WaitForExit(10_000);
        if (p.ExitCode != 0)
        {
            var stderr = p.StandardError.ReadToEnd();
            var stdout = p.StandardOutput.ReadToEnd();
            throw new InvalidOperationException($"sc config failed ({p.ExitCode}): {stderr} {stdout}");
        }
    }
}
