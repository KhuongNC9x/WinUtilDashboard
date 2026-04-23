using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using WinUtilDashboard.Models;

namespace WinUtilDashboard.Services.Interfaces;

public interface ISystemMonitorService : IDisposable
{
    Task<QuickSystemInfo> GetQuickInfoAsync(CancellationToken ct = default);
    Task<double?> GetCpuTemperatureAsync(CancellationToken ct = default);
}

public interface ICleanupService
{
    Task<CleanupResult> CleanUserTempAsync(IProgress<int>? progress = null, CancellationToken ct = default);
    Task<CleanupResult> CleanWindowsTempAsync(IProgress<int>? progress = null, CancellationToken ct = default);
    Task<CleanupResult> CleanWindowsUpdateCacheAsync(IProgress<int>? progress = null, CancellationToken ct = default);
    Task<CleanupResult> CleanBrowserCacheAsync(IProgress<int>? progress = null, CancellationToken ct = default);
}

public interface IFolderScannerService
{
    Task<IReadOnlyList<FolderSize>> ScanTopFoldersAsync(
        string path,
        int top = 15,
        IProgress<string>? progress = null,
        CancellationToken ct = default);
}

public interface IProcessService
{
    Task<IReadOnlyList<ProcessInfo>> GetProcessesAsync(string? filter = null, CancellationToken ct = default);
    bool KillProcess(int pid);
}

public interface IHardwareInfoService
{
    Task<HardwareReport> GetFullReportAsync(CancellationToken ct = default);
}

public interface IStartupManagerService
{
    Task<IReadOnlyList<StartupEntry>> GetStartupAppsAsync(CancellationToken ct = default);
    Task<bool> DisableStartupAsync(string name, CancellationToken ct = default);
}

public interface IWindowsUpdateService
{
    Task<IReadOnlyList<ServiceStatus>> CheckStatusAsync(CancellationToken ct = default);
    Task<IReadOnlyList<string>> DisableAsync(CancellationToken ct = default);
    Task<IReadOnlyList<string>> EnableAsync(CancellationToken ct = default);
}

public interface IExportService
{
    Task ExportToTextAsync(string filePath, string content, CancellationToken ct = default);
    Task ExportToCsvAsync(string filePath, IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyList<string>> rows, CancellationToken ct = default);
    Task ExportToHtmlAsync(string filePath, string title, string content, CancellationToken ct = default);
}

public interface INotificationService
{
    ObservableCollection<SystemAlert> Alerts { get; }
    void AddAlert(string message, AlertLevel level);
    void CheckSystemStatus(QuickSystemInfo info);
    void ClearAlerts();
}
