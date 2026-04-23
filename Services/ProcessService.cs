using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using WinUtilDashboard.Models;
using WinUtilDashboard.Services.Interfaces;

namespace WinUtilDashboard.Services;

/// <summary>
/// Lists and kills processes. Disposes Process objects to avoid handle leaks,
/// and catches the exceptions that Process members can actually throw.
/// </summary>
public sealed class ProcessService : IProcessService
{
    private readonly ILogger<ProcessService> _logger;

    public ProcessService(ILogger<ProcessService> logger) => _logger = logger;

    public async Task<IReadOnlyList<ProcessInfo>> GetProcessesAsync(
        string? filter = null,
        CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            var list = new List<ProcessInfo>();
            Process[] processes = Process.GetProcesses();
            try
            {
                foreach (var p in processes)
                {
                    ct.ThrowIfCancellationRequested();

                    try
                    {
                        if (!string.IsNullOrEmpty(filter) &&
                            !p.ProcessName.Contains(filter, StringComparison.OrdinalIgnoreCase))
                            continue;

                        list.Add(new ProcessInfo(
                            Pid:      p.Id,
                            Name:     p.ProcessName,
                            MemoryMb: Math.Round(p.WorkingSet64 / 1024.0 / 1024.0, 1),
                            Threads:  p.Threads.Count));
                    }
                    catch (InvalidOperationException) { /* process exited */ }
                    catch (System.ComponentModel.Win32Exception) { /* access denied (System/Idle) */ }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Skip process {Pid}", SafeId(p));
                    }
                }
            }
            finally
            {
                foreach (var p in processes) p.Dispose();
            }

            return (IReadOnlyList<ProcessInfo>)list.OrderByDescending(x => x.MemoryMb).ToList();
        }, ct).ConfigureAwait(false);
    }

    public bool KillProcess(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            p.Kill(entireProcessTree: true);
            p.WaitForExit(3000);
            return true;
        }
        catch (ArgumentException)
        {
            _logger.LogWarning("Kill failed - process {Pid} not found", pid);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to kill process {Pid}", pid);
            return false;
        }
    }

    private static string SafeId(Process p)
    {
        try { return p.Id.ToString(); } catch { return "?"; }
    }
}
