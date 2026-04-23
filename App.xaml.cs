using System;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using WinUtilDashboard.Services;
using WinUtilDashboard.Services.Interfaces;
using WinUtilDashboard.ViewModels;

namespace WinUtilDashboard;

/// <summary>
/// Composition root. Configures logging (Serilog), DI, and global exception
/// handlers before showing the main window.
/// </summary>
public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        ConfigureLogging();
        Services = ConfigureServices();

        // Global exception handlers - log everything, nothing gets swallowed silently.
        DispatcherUnhandledException += (_, args) =>
        {
            Log.Fatal(args.Exception, "Unhandled dispatcher exception");
            MessageBox.Show(
                $"An unexpected error occurred:\n\n{args.Exception.Message}\n\nSee log file for details.",
                "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
                Log.Fatal(ex, "Unhandled AppDomain exception (IsTerminating={Terminating})", args.IsTerminating);
        };

        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error(args.Exception, "Unobserved task exception");
            args.SetObserved();
        };

        var mainWindow = Services.GetRequiredService<MainWindow>();
        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.Information("Application exiting");
        Log.CloseAndFlush();

        if (Services is IDisposable disposable)
            disposable.Dispose();

        base.OnExit(e);
    }

    private static void ConfigureLogging()
    {
        var logFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
        Directory.CreateDirectory(logFolder);

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.File(
                path: Path.Combine(logFolder, "log-.txt"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 30,
                outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
            .WriteTo.Debug()
            .CreateLogger();
    }

    private static IServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        services.AddLogging(b => b.AddSerilog(dispose: true));

        // Services - singleton because they're stateless and cheap to share.
        services.AddSingleton<ISystemMonitorService, SystemMonitorService>();
        services.AddSingleton<ICleanupService, CleanupService>();
        services.AddSingleton<IFolderScannerService, FolderScannerService>();
        services.AddSingleton<IProcessService, ProcessService>();
        services.AddSingleton<IHardwareInfoService, HardwareInfoService>();
        services.AddSingleton<IStartupManagerService, StartupManagerService>();
        services.AddSingleton<IWindowsUpdateService, WindowsUpdateService>();
        services.AddSingleton<IExportService, ExportService>();
        services.AddSingleton<INotificationService, NotificationService>();

        // ViewModels + Views
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();

        return services.BuildServiceProvider();
    }
}
