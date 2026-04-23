using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows;
using Microsoft.Extensions.Logging;
using WinUtilDashboard.Models;
using WinUtilDashboard.Services.Interfaces;

namespace WinUtilDashboard.Services;

/// <summary>
/// Manages the user-facing alerts collection. Adds are marshalled to the UI
/// dispatcher (safe from background threads) and repeated same-category alerts
/// are throttled so a sustained problem doesn't spam the list.
/// </summary>
public sealed class NotificationService : INotificationService
{
    private readonly ILogger<NotificationService> _logger;

    private const double CpuWarningPercent = 80.0;
    private const double RamWarningPercent = 85.0;
    private const double DiskWarningFreeGb = 10.0;
    private const int    MaxAlerts         = 50;

    private static readonly TimeSpan AlertCooldown = TimeSpan.FromMinutes(1);

    private readonly Dictionary<string, DateTime> _lastAlertByKey = new();
    private readonly object _syncRoot = new();

    public ObservableCollection<SystemAlert> Alerts { get; } = new();

    public NotificationService(ILogger<NotificationService> logger) => _logger = logger;

    public void CheckSystemStatus(QuickSystemInfo info)
    {
        if (info.CpuPercent > CpuWarningPercent)
            AddThrottled("cpu", $"High CPU usage: {info.CpuPercent:F0}%", AlertLevel.Warning);

        if (info.RamPercent > RamWarningPercent)
            AddThrottled("ram", $"High RAM usage: {info.RamPercent:F0}%", AlertLevel.Warning);

        if (info.DiskFreeGb < DiskWarningFreeGb)
            AddThrottled("disk", $"Disk C: low free space ({info.DiskFreeGb:F1} GB)", AlertLevel.Critical);
    }

    public void AddAlert(string message, AlertLevel level)
    {
        var alert = new SystemAlert
        {
            Message   = message,
            Level     = level,
            Timestamp = DateTime.Now
        };

        DispatchToUi(() =>
        {
            Alerts.Insert(0, alert);
            while (Alerts.Count > MaxAlerts)
                Alerts.RemoveAt(Alerts.Count - 1);
        });

        _logger.LogInformation("Alert [{Level}] {Message}", level, message);
    }

    public void ClearAlerts() => DispatchToUi(Alerts.Clear);

    // ---------- internals ----------

    private void AddThrottled(string key, string message, AlertLevel level)
    {
        lock (_syncRoot)
        {
            if (_lastAlertByKey.TryGetValue(key, out var last) &&
                DateTime.Now - last < AlertCooldown)
            {
                return;
            }
            _lastAlertByKey[key] = DateTime.Now;
        }

        AddAlert(message, level);
    }

    private static void DispatchToUi(Action action)
    {
        var app = Application.Current;
        if (app is null)
        {
            action();
            return;
        }

        if (app.Dispatcher.CheckAccess())
            action();
        else
            app.Dispatcher.Invoke(action);
    }
}
