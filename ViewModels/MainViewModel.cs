using System.Collections.ObjectModel;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using WinUtilDashboard.Infrastructure;
using WinUtilDashboard.Models;
using WinUtilDashboard.Services.Interfaces;
using WinUtilDashboard.Views;

namespace WinUtilDashboard.ViewModels;

/// <summary>
/// Main window view-model. Owns all dashboard state, commands, and timers.
/// Uses [ObservableProperty] / [RelayCommand] source-gen from CommunityToolkit.Mvvm
/// to avoid property/command boilerplate.
/// </summary>
public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly ISystemMonitorService _monitor;
    private readonly ICleanupService _cleanup;
    private readonly IFolderScannerService _scanner;
    private readonly IProcessService _process;
    private readonly IHardwareInfoService _hardware;
    private readonly IStartupManagerService _startup;
    private readonly IWindowsUpdateService _wuService;
    private readonly IExportService _export;
    private readonly INotificationService _notification;
    private readonly ILogger<MainViewModel> _logger;

    private readonly DispatcherTimer _monitorTimer;
    private readonly DispatcherTimer _alertTimer;
    private readonly DispatcherTimer _uptimeTimer;

    private CancellationTokenSource? _currentOperationCts;

    // ---------- bindable state ----------

    [ObservableProperty] private double cpuPercent;
    [ObservableProperty] private double ramPercent;
    [ObservableProperty] private double ramUsedGb;
    [ObservableProperty] private double ramTotalGb;
    [ObservableProperty] private double diskFreeGb;
    [ObservableProperty] private double diskTotalGb;
    [ObservableProperty] private double diskFreePercent;
    [ObservableProperty] private double? tempCelsius;

    [ObservableProperty] private string cpuDisplay = "0%";
    [ObservableProperty] private string ramDisplay = "0%";
    [ObservableProperty] private string tempDisplay = "N/A";
    [ObservableProperty] private string diskDisplay = "0 GB";
    [ObservableProperty] private string ramDetails = "0.0 / 0.0 GB";
    [ObservableProperty] private string diskPercentDisplay = "0% free";
    [ObservableProperty] private string uptimeDisplay = "0h 0m";
    [ObservableProperty] private string totalRamDisplay = "0.0 GB";

    [ObservableProperty] private string logText = "";
    [ObservableProperty] private int alertCount;

    [ObservableProperty] private bool isBusy;

    // Dark mode toggle state. Changing this property automatically applies the
    // matching theme via the partial OnIsDarkModeChanged hook below.
    [ObservableProperty] private bool isDarkMode;

    public string DarkModeLabel => IsDarkMode ? "☀️ Light" : "🌙 Dark";

    partial void OnIsDarkModeChanged(bool value)
    {
        ThemeManager.ApplyTheme(value ? "Themes/DarkTheme.xaml" : "Themes/LightTheme.xaml");
        OnPropertyChanged(nameof(DarkModeLabel));
        AppendLog(value ? "🌙 Switched to Dark Mode" : "☀️ Switched to Light Mode");
    }

    public ObservableCollection<SystemAlert> Alerts => _notification.Alerts;

    // ---------- ctor ----------

    public MainViewModel(
        ISystemMonitorService monitor,
        ICleanupService cleanup,
        IFolderScannerService scanner,
        IProcessService process,
        IHardwareInfoService hardware,
        IStartupManagerService startup,
        IWindowsUpdateService wuService,
        IExportService export,
        INotificationService notification,
        ILogger<MainViewModel> logger)
    {
        _monitor = monitor;
        _cleanup = cleanup;
        _scanner = scanner;
        _process = process;
        _hardware = hardware;
        _startup = startup;
        _wuService = wuService;
        _export = export;
        _notification = notification;
        _logger = logger;

        _notification.Alerts.CollectionChanged += (_, _) => AlertCount = _notification.Alerts.Count;

        _monitorTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _monitorTimer.Tick += async (_, _) => await RefreshMetricsAsync();

        _alertTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        _alertTimer.Tick += (_, _) => CheckAlertsSync();

        _uptimeTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _uptimeTimer.Tick += (_, _) => UptimeDisplay = SystemUptime.Format();
    }

    public void Start()
    {
        _logger.LogInformation("========================================");
        _logger.LogInformation("Windows Utility Dashboard starting (v1.0.0)");
        _logger.LogInformation("========================================");

        UptimeDisplay = SystemUptime.Format();
        _monitorTimer.Start();
        _alertTimer.Start();
        _uptimeTimer.Start();

        AppendLog("✅ Application started");
        _notification.AddAlert("Dashboard is ready!", AlertLevel.Info);
    }

    // ---------- polling ----------

    private async Task RefreshMetricsAsync()
    {
        try
        {
            var info = await _monitor.GetQuickInfoAsync().ConfigureAwait(true);
            ApplyMetrics(info);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Metric refresh failed");
        }
    }

    private void ApplyMetrics(QuickSystemInfo info)
    {
        CpuPercent = info.CpuPercent;
        RamPercent = info.RamPercent;
        RamUsedGb = info.RamUsedGb;
        RamTotalGb = info.RamTotalGb;
        DiskFreeGb = info.DiskFreeGb;
        DiskTotalGb = info.DiskTotalGb;
        DiskFreePercent = info.DiskFreePercent;
        TempCelsius = info.TempCelsius;

        CpuDisplay = $"{info.CpuPercent:F0}%";
        RamDisplay = $"{info.RamPercent:F0}%";
        RamDetails = $"{info.RamUsedGb:F1} / {info.RamTotalGb:F1} GB";
        TempDisplay = info.TempCelsius.HasValue ? $"{info.TempCelsius:F0}°C" : "N/A";
        DiskDisplay = $"{info.DiskFreeGb:F0} GB";
        DiskPercentDisplay = $"{info.DiskFreePercent:F0}% free";
        TotalRamDisplay = $"{info.RamTotalGb:F1} GB";
    }

    private void CheckAlertsSync()
    {
        try
        {
            // Cheap read path - we already have the latest metrics in bindable state,
            // but we need the full struct for the notification service, so rebuild it.
            var snapshot = new QuickSystemInfo(
                CpuPercent, RamPercent, RamUsedGb, RamTotalGb,
                DiskFreeGb, DiskTotalGb, TempCelsius);
            _notification.CheckSystemStatus(snapshot);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Alert check failed");
        }
    }

    // ---------- commands ----------

    [RelayCommand]
    private async Task QuickCleanAsync()
    {
        await RunBusyAsync("Quick Clean", async ct =>
        {
            AppendLog("🧹 Starting Quick Clean...");
            _notification.AddAlert("Starting cleanup...", AlertLevel.Info);

            var progress = new Progress<int>(n => AppendLog($"  ... {n} files so far"));

            var r1 = await _cleanup.CleanUserTempAsync(progress, ct).ConfigureAwait(true);
            AppendLog($"  ✓ User Temp: {r1.DeletedCount} files, {r1.FreedMb:F1} MB");

            var r2 = await _cleanup.CleanBrowserCacheAsync(progress, ct).ConfigureAwait(true);
            AppendLog($"  ✓ Browser Cache: {r2.DeletedCount} files, {r2.FreedMb:F1} MB");

            var total = r1.Combine(r2);
            AppendLog($"🎉 Complete! Freed {total.FreedMb:F1} MB ({total.DeletedCount} files)");
            _notification.AddAlert($"Cleanup complete! Freed {total.FreedMb / 1024.0:F2} GB", AlertLevel.Info);
        });
    }

    [RelayCommand]
    private async Task CheckWindowsUpdateAsync()
    {
        await RunBusyAsync("Check Windows Update", async ct =>
        {
            AppendLog("🔍 Checking Windows Update status...");
            var statuses = await _wuService.CheckStatusAsync(ct).ConfigureAwait(true);
            foreach (var s in statuses)
                AppendLog($"  {s.DisplayName} ({s.ServiceName}): {s.State}, StartMode={s.StartMode}");
            _notification.AddAlert("Windows Update status checked", AlertLevel.Info);
        });
    }

    [RelayCommand]
    private async Task DisableWindowsUpdateAsync()
    {
        var confirm = MessageBox.Show(
            "Disabling Windows Update will stop 3 services and prevent security patches.\n\nAre you sure?",
            "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes) return;

        await RunBusyAsync("Disable Windows Update", async ct =>
        {
            AppendLog("⏸️ Disabling Windows Update...");
            var results = await _wuService.DisableAsync(ct).ConfigureAwait(true);
            foreach (var line in results) AppendLog("  " + line);
            _notification.AddAlert("Windows Update disabled", AlertLevel.Warning);
        });
    }

    [RelayCommand]
    private async Task EnableWindowsUpdateAsync()
    {
        await RunBusyAsync("Enable Windows Update", async ct =>
        {
            AppendLog("▶️ Re-enabling Windows Update...");
            var results = await _wuService.EnableAsync(ct).ConfigureAwait(true);
            foreach (var line in results) AppendLog("  " + line);
            _notification.AddAlert("Windows Update re-enabled", AlertLevel.Info);
        });
    }

    [RelayCommand]
    private async Task ScanFoldersAsync()
    {
        var dlg = new OpenFolderDialog { Title = "Select folder to scan" };
        if (dlg.ShowDialog() != true) return;

        await RunBusyAsync($"Scan {dlg.FolderName}", async ct =>
        {
            AppendLog($"📁 Scanning '{dlg.FolderName}'...");
            var progress = new Progress<string>(sub => AppendLog($"  scanning: {sub}"));

            var result = await _scanner.ScanTopFoldersAsync(dlg.FolderName, 10, progress, ct).ConfigureAwait(true);
            AppendLog($"--- Top {result.Count} largest folders ---");
            foreach (var f in result)
                AppendLog($"  {f.SizeGb,8:F2} GB  |  {f.FullPath}");
            _notification.AddAlert($"Scanned {result.Count} folders", AlertLevel.Info);
        });
    }

    [RelayCommand]
    private async Task ShowProcessesAsync()
    {
        await RunBusyAsync("List processes", async ct =>
        {
            AppendLog("⚙️ Loading process list...");
            var list = await _process.GetProcessesAsync(null, ct).ConfigureAwait(true);

            var sb = new StringBuilder();
            sb.AppendLine($"Total processes: {list.Count}").AppendLine();
            sb.AppendLine("Top 10 by RAM:");
            foreach (var p in list.Take(10))
                sb.AppendLine($"  {p.Name,-30} {p.MemoryMb,8:F1} MB  (PID {p.Pid})");

            MessageBox.Show(sb.ToString(), "Process Manager", MessageBoxButton.OK, MessageBoxImage.Information);
            AppendLog($"  Loaded {list.Count} processes");
        });
    }

    [RelayCommand]
    private async Task ShowHardwareInfoAsync()
    {
        await RunBusyAsync("Hardware info", async ct =>
        {
            AppendLog("💻 Reading hardware information...");
            var report = await _hardware.GetFullReportAsync(ct).ConfigureAwait(true);

            if (report.Cpu is { } cpu)
                AppendLog($"  CPU: {cpu.Name} ({cpu.Cores}C/{cpu.LogicalProcessors}T @ {cpu.MaxClockMhz} MHz)");

            foreach (var ram in report.RamModules)
                AppendLog($"  RAM: {ram.SizeGb:F1} GB @ {ram.SpeedMhz} MHz ({ram.Manufacturer} {ram.PartNumber})");

            foreach (var gpu in report.Gpus)
                AppendLog($"  GPU: {gpu.Name} - VRAM {gpu.VramGb:F1} GB - Driver {gpu.DriverVersion}");

            foreach (var disk in report.PhysicalDisks)
                AppendLog($"  DISK: {disk.Model} - {disk.SizeGb:F0} GB - {disk.MediaType}");

            foreach (var ld in report.LogicalDisks)
                AppendLog($"  {ld.DeviceId} ({ld.FileSystem}): {ld.FreeGb:F0}/{ld.SizeGb:F0} GB free");

            _notification.AddAlert("Hardware information read", AlertLevel.Info);
        });
    }

    [RelayCommand]
    private async Task ExportReportAsync()
    {
        var dlg = new SaveFileDialog
        {
            Filter = "HTML Report (*.html)|*.html|Text Report (*.txt)|*.txt|CSV Report (*.csv)|*.csv",
            FileName = $"SystemReport_{DateTime.Now:yyyyMMdd_HHmmss}"
        };
        if (dlg.ShowDialog() != true) return;

        await RunBusyAsync("Export report", async ct =>
        {
            AppendLog("📄 Creating report...");
            var info = await _monitor.GetQuickInfoAsync(ct).ConfigureAwait(true);
            var hardware = await _hardware.GetFullReportAsync(ct).ConfigureAwait(true);

            var content = BuildReportContent(info, hardware);

            if (dlg.FileName.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
            {
                await _export.ExportToHtmlAsync(dlg.FileName, "System Report", content, ct).ConfigureAwait(true);
            }
            else if (dlg.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
            {
                var headers = new[] { "Metric", "Value" };
                var rows = new[]
                {
                    new[] { "CPU Usage",   $"{info.CpuPercent:F1}%" },
                    new[] { "RAM Usage",   $"{info.RamPercent:F1}%" },
                    new[] { "RAM Used",    $"{info.RamUsedGb:F1} GB" },
                    new[] { "RAM Total",   $"{info.RamTotalGb:F1} GB" },
                    new[] { "Temperature", info.TempCelsius is { } t ? $"{t:F1}°C" : "N/A" },
                    new[] { "Disk Free",   $"{info.DiskFreeGb:F1} GB" },
                    new[] { "Disk Total",  $"{info.DiskTotalGb:F1} GB" },
                };
                await _export.ExportToCsvAsync(dlg.FileName, headers, rows, ct).ConfigureAwait(true);
            }
            else
            {
                await _export.ExportToTextAsync(dlg.FileName, content, ct).ConfigureAwait(true);
            }

            AppendLog($"✅ Report saved: {dlg.FileName}");
            _notification.AddAlert("Report exported", AlertLevel.Info);

            var open = MessageBox.Show(
                "Report saved successfully!\nOpen it now?",
                "Success", MessageBoxButton.YesNo, MessageBoxImage.Information);
            if (open == MessageBoxResult.Yes)
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = dlg.FileName,
                    UseShellExecute = true
                });
            }
        });
    }

    [RelayCommand]
    private async Task ShowStartupManagerAsync()
    {
        await RunBusyAsync("Startup apps", async ct =>
        {
            AppendLog("🚀 Reading startup apps...");
            var apps = await _startup.GetStartupAppsAsync(ct).ConfigureAwait(true);

            var sb = new StringBuilder();
            sb.AppendLine($"STARTUP APPS ({apps.Count})").AppendLine();
            int i = 1;
            foreach (var app in apps)
            {
                sb.AppendLine($"{i++}. {app.Name}");
                sb.AppendLine($"   Source: {app.Source}");
                sb.AppendLine($"   Path:   {app.Path}").AppendLine();
            }
            sb.AppendLine("Press OK to disable one.");

            var result = MessageBox.Show(sb.ToString(), "Startup Apps", MessageBoxButton.OKCancel, MessageBoxImage.Information);
            AppendLog($"  Found {apps.Count} startup apps");
            if (result != MessageBoxResult.OK) return;

            var appName = InputDialog.Prompt(
                Application.Current.MainWindow,
                "Enter EXACT name of the app to disable (not case-sensitive):",
                "Disable Startup App");

            if (string.IsNullOrWhiteSpace(appName)) return;

            var success = await _startup.DisableStartupAsync(appName.Trim(), ct).ConfigureAwait(true);
            if (success)
            {
                AppendLog($"  ✅ Disabled '{appName}'");
                _notification.AddAlert($"Disabled startup: {appName}", AlertLevel.Info);
                MessageBox.Show($"Disabled '{appName}'.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                AppendLog($"  ❌ Not found: '{appName}'");
                MessageBox.Show($"Startup app not found:\n{appName}", "Not Found", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        });
    }

    [RelayCommand]
    private async Task RefreshProcessAsync()
    {
        await RunBusyAsync("Refresh processes", async ct =>
        {
            AppendLog("🔄 Refreshing process list...");
            var list = await _process.GetProcessesAsync(null, ct).ConfigureAwait(true);
            AppendLog($"  ✓ Loaded {list.Count} processes");
        });
    }

    [RelayCommand]
    private void OpenLogsFolder()
    {
        try
        {
            var logFolder = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
            if (!System.IO.Directory.Exists(logFolder))
                System.IO.Directory.CreateDirectory(logFolder);

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = logFolder,
                UseShellExecute = true
            });
            AppendLog("📂 Opened Logs folder");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to open logs folder");
            AppendLog($"❌ Cannot open logs folder: {ex.Message}");
        }
    }

    [RelayCommand]
    private void OpenTaskManager()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "taskmgr.exe",
                UseShellExecute = true
            });
            AppendLog("⚙️ Opened Task Manager");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to open Task Manager");
            AppendLog($"❌ Cannot open Task Manager: {ex.Message}");
        }
    }

    [RelayCommand]
    private void CancelCurrentOperation()
    {
        _currentOperationCts?.Cancel();
        AppendLog("⏹️ Cancellation requested...");
    }

    [RelayCommand]
    private void ClearLog() => LogText = "";

    [RelayCommand]
    private void ClearAlerts() => _notification.ClearAlerts();

    // ---------- helpers ----------

    private async Task RunBusyAsync(string opName, Func<CancellationToken, Task> operation)
    {
        if (IsBusy)
        {
            MessageBox.Show("Another operation is in progress.", "Busy",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        IsBusy = true;
        _currentOperationCts = new CancellationTokenSource();
        try
        {
            await UserErrorReporter.TryRunAsync(_logger, opName, operation, _currentOperationCts.Token)
                .ConfigureAwait(true);
        }
        finally
        {
            _currentOperationCts.Dispose();
            _currentOperationCts = null;
            IsBusy = false;
        }
    }

    private void AppendLog(string message)
    {
        LogText += $"[{DateTime.Now:HH:mm:ss}] {message}\n";
    }

    private static string BuildReportContent(QuickSystemInfo info, HardwareReport hw)
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== SYSTEM REPORT ===");
        sb.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine();
        sb.AppendLine($"CPU Usage:   {info.CpuPercent:F1}%");
        sb.AppendLine($"RAM Usage:   {info.RamPercent:F1}% ({info.RamUsedGb:F1}/{info.RamTotalGb:F1} GB)");
        sb.AppendLine($"Temperature: {(info.TempCelsius is { } t ? $"{t:F1}°C" : "N/A")}");
        sb.AppendLine($"Disk C:      {info.DiskFreeGb:F1}/{info.DiskTotalGb:F1} GB free");
        sb.AppendLine();

        if (hw.Cpu is { } cpu)
            sb.AppendLine($"CPU: {cpu.Name} ({cpu.Cores}C/{cpu.LogicalProcessors}T @ {cpu.MaxClockMhz} MHz)");

        foreach (var r in hw.RamModules)
            sb.AppendLine($"RAM: {r.SizeGb:F1} GB @ {r.SpeedMhz} MHz - {r.Manufacturer} {r.PartNumber}");

        if (hw.Mainboard is { } mb)
            sb.AppendLine($"Mainboard: {mb.Manufacturer} {mb.Product} (v{mb.Version})");

        if (hw.Bios is { } bios)
            sb.AppendLine($"BIOS: {bios.Manufacturer} {bios.Name} v{bios.Version}");

        foreach (var g in hw.Gpus)
            sb.AppendLine($"GPU: {g.Name} - VRAM {g.VramGb:F1} GB - Driver {g.DriverVersion}");

        foreach (var d in hw.PhysicalDisks)
            sb.AppendLine($"Disk: {d.Model} - {d.SizeGb:F0} GB - {d.MediaType}");

        return sb.ToString();
    }

    public void Dispose()
    {
        _monitorTimer.Stop();
        _alertTimer.Stop();
        _uptimeTimer.Stop();
        _currentOperationCts?.Cancel();
        _currentOperationCts?.Dispose();
    }
}