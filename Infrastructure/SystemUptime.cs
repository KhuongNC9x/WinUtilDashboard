using System;

namespace WinUtilDashboard.Infrastructure;

/// <summary>
/// Returns the real Windows boot-time uptime.
/// The original code used <c>DateTime.Now</c> at app startup, which is actually
/// "how long the app has been running" — not system uptime.
/// </summary>
public static class SystemUptime
{
    /// <summary>Time since the OS booted.</summary>
    public static TimeSpan GetUptime() => TimeSpan.FromMilliseconds(Environment.TickCount64);

    /// <summary>Human-readable uptime, e.g. "3d 14h 22m" or "14h 22m".</summary>
    public static string Format()
    {
        var uptime = GetUptime();
        return uptime.TotalDays >= 1
            ? $"{(int)uptime.TotalDays}d {uptime.Hours}h {uptime.Minutes}m"
            : $"{(int)uptime.TotalHours}h {uptime.Minutes}m";
    }
}
