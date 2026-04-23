using System;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using WinUtilDashboard.Models;
using WinUtilDashboard.Services.Interfaces;

namespace WinUtilDashboard.Services;

/// <summary>
/// Monitors CPU, RAM, temperature, and disk metrics.
/// Disposes native PerformanceCounter to avoid handle leaks.
/// </summary>
public sealed class SystemMonitorService : ISystemMonitorService
{
    private readonly ILogger<SystemMonitorService> _logger;
    private readonly PerformanceCounter _cpuCounter;
    private readonly SemaphoreSlim _cpuLock = new(1, 1);
    private bool _disposed;

    public SystemMonitorService(ILogger<SystemMonitorService> logger)
    {
        _logger = logger;
        _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
        // First NextValue() always returns 0 - prime it
        _ = _cpuCounter.NextValue();
    }

    public async Task<QuickSystemInfo> GetQuickInfoAsync(CancellationToken ct = default)
    {
        ThrowIfDisposed();

        return await Task.Run(async () =>
        {
            ct.ThrowIfCancellationRequested();

            double cpu = await ReadCpuAsync(ct).ConfigureAwait(false);
            var (ramPct, ramUsed, ramTotal) = ReadRam();
            var (diskFree, diskTotal) = ReadDisk("C:\\");
            double? temp = ReadCpuTemperature();

            return new QuickSystemInfo(cpu, ramPct, ramUsed, ramTotal, diskFree, diskTotal, temp);
        }, ct).ConfigureAwait(false);
    }

    public async Task<double?> GetCpuTemperatureAsync(CancellationToken ct = default)
    {
        ThrowIfDisposed();
        return await Task.Run(ReadCpuTemperature, ct).ConfigureAwait(false);
    }

    private async Task<double> ReadCpuAsync(CancellationToken ct)
    {
        // PerformanceCounter itself isn't thread-safe - serialize access.
        await _cpuLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return _cpuCounter.NextValue();
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "CPU counter unavailable");
            return 0;
        }
        finally
        {
            _cpuLock.Release();
        }
    }

    private (double pct, double usedGb, double totalGb) ReadRam()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT TotalVisibleMemorySize, FreePhysicalMemory FROM Win32_OperatingSystem");
            using var collection = searcher.Get();

            foreach (var o in collection)
            {
                using var mo = (ManagementObject)o;
                // WMI returns KB
                double totalKb = Convert.ToDouble(mo["TotalVisibleMemorySize"]);
                double freeKb = Convert.ToDouble(mo["FreePhysicalMemory"]);
                if (totalKb <= 0) return (0, 0, 0);

                double totalGb = totalKb / 1024.0 / 1024.0;
                double usedGb = (totalKb - freeKb) / 1024.0 / 1024.0;
                double pct = (totalKb - freeKb) / totalKb * 100.0;
                return (pct, usedGb, totalGb);
            }
        }
        catch (ManagementException ex)
        {
            _logger.LogWarning(ex, "Failed to read RAM from WMI");
        }
        return (0, 0, 0);
    }

    private (double freeGb, double totalGb) ReadDisk(string drive)
    {
        try
        {
            var di = new DriveInfo(drive);
            if (!di.IsReady) return (0, 0);

            double free = di.AvailableFreeSpace / 1024.0 / 1024.0 / 1024.0;
            double total = di.TotalSize / 1024.0 / 1024.0 / 1024.0;
            return (free, total);
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Failed to read disk {Drive}", drive);
            return (0, 0);
        }
    }

    /// <summary>
    /// Reads CPU temperature via WMI (MSAcpi_ThermalZoneTemperature).
    /// Returns null if sensor is not exposed - on many laptops this requires
    /// LibreHardwareMonitorLib as a fallback.
    /// </summary>
    private double? ReadCpuTemperature()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                @"root\WMI",
                "SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature");
            using var collection = searcher.Get();

            foreach (var o in collection)
            {
                using var mo = (ManagementObject)o;
                double kelvin10 = Convert.ToDouble(mo["CurrentTemperature"]);
                return (kelvin10 / 10.0) - 273.15;
            }
        }
        catch (ManagementException)
        {
            // Sensor not exposed - expected on many systems
        }
        return null;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(SystemMonitorService));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _cpuCounter.Dispose();
        _cpuLock.Dispose();
        _disposed = true;
    }
}
