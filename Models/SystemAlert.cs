using System;

namespace WinUtilDashboard.Models;

public enum AlertLevel
{
    Info,
    Warning,
    Critical
}

public class SystemAlert
{
    public string Message { get; init; } = "";
    public AlertLevel Level { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.Now;

    public string Icon => Level switch
    {
        AlertLevel.Info     => "ℹ️",
        AlertLevel.Warning  => "⚠️",
        AlertLevel.Critical => "🔴",
        _                   => "📌"
    };
}
