namespace WinUtilDashboard.Models;

/// <summary>Real-time system snapshot used for dashboard updates.</summary>
public record QuickSystemInfo(
    double CpuPercent,
    double RamPercent,
    double RamUsedGb,
    double RamTotalGb,
    double DiskFreeGb,
    double DiskTotalGb,
    double? TempCelsius)
{
    public double DiskUsedPercent =>
        DiskTotalGb > 0 ? (1 - DiskFreeGb / DiskTotalGb) * 100 : 0;

    public double DiskFreePercent =>
        DiskTotalGb > 0 ? DiskFreeGb / DiskTotalGb * 100 : 0;
}

public record CleanupResult(int DeletedCount, double FreedMb)
{
    public static CleanupResult Empty { get; } = new(0, 0);

    public CleanupResult Combine(CleanupResult other) =>
        new(DeletedCount + other.DeletedCount, FreedMb + other.FreedMb);
}

public record FolderSize(string FullPath, long SizeBytes)
{
    public double SizeMb => SizeBytes / 1024.0 / 1024.0;
    public double SizeGb => SizeBytes / 1024.0 / 1024.0 / 1024.0;
    public string Name => System.IO.Path.GetFileName(FullPath);
}

public record ProcessInfo(
    int Pid,
    string Name,
    double MemoryMb,
    int Threads);

public record StartupEntry(
    string Name,
    string Path,
    string Source);

public record ServiceStatus(
    string ServiceName,
    string DisplayName,
    string State,
    string StartMode);
