using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using WinUtilDashboard.Models;
using WinUtilDashboard.Services.Interfaces;

namespace WinUtilDashboard.Services;

/// <summary>
/// Aggregates hardware info via WMI. Returns strongly-typed records
/// (so callers can render/export them properly, not just a string dump).
/// </summary>
public sealed class HardwareInfoService : IHardwareInfoService
{
    private readonly ILogger<HardwareInfoService> _logger;

    public HardwareInfoService(ILogger<HardwareInfoService> logger) => _logger = logger;

    public async Task<HardwareReport> GetFullReportAsync(CancellationToken ct = default)
    {
        return await Task.Run(() => new HardwareReport(
            Cpu:             GetCpu(),
            RamModules:      GetRamModules(),
            Mainboard:       GetMainboard(),
            Bios:            GetBios(),
            ComputerSystem:  GetComputerSystem(),
            Gpus:            GetGpus(),
            PhysicalDisks:   GetPhysicalDisks(),
            LogicalDisks:    GetLogicalDisks()
        ), ct).ConfigureAwait(false);
    }

    private CpuInfo? GetCpu()
    {
        return QuerySingle<CpuInfo>(
            "SELECT Name, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed FROM Win32_Processor",
            mo => new CpuInfo(
                Name:              AsString(mo["Name"]),
                Cores:             AsInt(mo["NumberOfCores"]),
                LogicalProcessors: AsInt(mo["NumberOfLogicalProcessors"]),
                MaxClockMhz:       AsInt(mo["MaxClockSpeed"])));
    }

    private IReadOnlyList<RamModule> GetRamModules()
    {
        return QueryAll(
            "SELECT Capacity, Speed, Manufacturer, PartNumber FROM Win32_PhysicalMemory",
            mo => new RamModule(
                SizeGb:       AsDouble(mo["Capacity"]) / 1024.0 / 1024.0 / 1024.0,
                SpeedMhz:     AsInt(mo["Speed"]),
                Manufacturer: AsString(mo["Manufacturer"]).Trim(),
                PartNumber:   AsString(mo["PartNumber"]).Trim()));
    }

    private MainboardInfo? GetMainboard() => QuerySingle(
        "SELECT Manufacturer, Product, Version FROM Win32_BaseBoard",
        mo => new MainboardInfo(AsString(mo["Manufacturer"]), AsString(mo["Product"]), AsString(mo["Version"])));

    private BiosInfo? GetBios() => QuerySingle(
        "SELECT Manufacturer, Name, Version, ReleaseDate FROM Win32_BIOS",
        mo => new BiosInfo(AsString(mo["Manufacturer"]), AsString(mo["Name"]), AsString(mo["Version"]), AsString(mo["ReleaseDate"])));

    private ComputerSystemInfo? GetComputerSystem() => QuerySingle(
        "SELECT Manufacturer, Model, SystemType FROM Win32_ComputerSystem",
        mo => new ComputerSystemInfo(AsString(mo["Manufacturer"]), AsString(mo["Model"]), AsString(mo["SystemType"])));

    /// <summary>
    /// Reads GPU info. <c>Win32_VideoController.AdapterRAM</c> is uint32 and caps
    /// at ~4GB, which is wrong for modern GPUs. We prefer the QWORD registry value
    /// <c>HardwareInformation.qwMemorySize</c> and fall back to WMI.
    /// </summary>
    private IReadOnlyList<GpuInfo> GetGpus()
    {
        var list = new List<GpuInfo>();

        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name, AdapterRAM, DriverVersion, PNPDeviceID FROM Win32_VideoController");
            using var collection = searcher.Get();

            foreach (var o in collection)
            {
                using var mo = (ManagementObject)o;
                string name = AsString(mo["Name"]);
                string driver = AsString(mo["DriverVersion"]);
                string pnpId = AsString(mo["PNPDeviceID"]);

                double vramGb = GetGpuVramGbFromRegistry(pnpId) ??
                                AsDouble(mo["AdapterRAM"]) / 1024.0 / 1024.0 / 1024.0;

                list.Add(new GpuInfo(name, vramGb, driver));
            }
        }
        catch (ManagementException ex)
        {
            _logger.LogWarning(ex, "Failed to enumerate GPUs");
        }
        return list;
    }

    private double? GetGpuVramGbFromRegistry(string pnpDeviceId)
    {
        if (string.IsNullOrWhiteSpace(pnpDeviceId)) return null;

        try
        {
            // Scan display adapter class registry for a matching MatchingDeviceId
            using var classKey = Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");
            if (classKey is null) return null;

            foreach (var subKeyName in classKey.GetSubKeyNames())
            {
                if (!int.TryParse(subKeyName, out _)) continue; // only numeric subkeys (0000, 0001...)

                using var subKey = classKey.OpenSubKey(subKeyName);
                if (subKey?.GetValue("HardwareInformation.qwMemorySize") is long bytes && bytes > 0)
                    return bytes / 1024.0 / 1024.0 / 1024.0;
            }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException)
        {
            _logger.LogDebug(ex, "Cannot read GPU VRAM from registry");
        }
        return null;
    }

    private IReadOnlyList<PhysicalDiskInfo> GetPhysicalDisks()
    {
        return QueryAll(
            "SELECT Model, Size, MediaType FROM Win32_DiskDrive",
            mo => new PhysicalDiskInfo(
                Model:     AsString(mo["Model"]),
                SizeGb:    AsDouble(mo["Size"]) / 1024.0 / 1024.0 / 1024.0,
                MediaType: AsString(mo["MediaType"])));
    }

    private IReadOnlyList<LogicalDiskInfo> GetLogicalDisks()
    {
        return QueryAll(
            "SELECT DeviceID, Size, FreeSpace, FileSystem FROM Win32_LogicalDisk WHERE DriveType=3",
            mo => new LogicalDiskInfo(
                DeviceId:   AsString(mo["DeviceID"]),
                FileSystem: AsString(mo["FileSystem"]),
                SizeGb:     AsDouble(mo["Size"]) / 1024.0 / 1024.0 / 1024.0,
                FreeGb:     AsDouble(mo["FreeSpace"]) / 1024.0 / 1024.0 / 1024.0));
    }

    // ---------- WMI helpers ----------

    private T? QuerySingle<T>(string wql, Func<ManagementObject, T> map) where T : class
        => QueryAll(wql, map).FirstOrDefault();

    private IReadOnlyList<T> QueryAll<T>(string wql, Func<ManagementObject, T> map)
    {
        var list = new List<T>();
        try
        {
            using var searcher = new ManagementObjectSearcher(wql);
            using var collection = searcher.Get();

            foreach (var o in collection)
            {
                using var mo = (ManagementObject)o;
                try { list.Add(map(mo)); }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to map WMI row for: {Query}", wql);
                }
            }
        }
        catch (ManagementException ex)
        {
            _logger.LogWarning(ex, "WMI query failed: {Query}", wql);
        }
        return list;
    }

    private static string AsString(object? v) => v?.ToString() ?? "";
    private static int    AsInt(object? v)    { try { return Convert.ToInt32(v ?? 0); }   catch { return 0; } }
    private static double AsDouble(object? v) { try { return Convert.ToDouble(v ?? 0); } catch { return 0; } }
}
