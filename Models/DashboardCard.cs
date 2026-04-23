namespace WinUtilDashboard.Models;

/// <summary>Model for dashboard cards (display only).</summary>
public class DashboardCard
{
    public string Title { get; set; } = "";
    public string Value { get; set; } = "";
    public string Icon { get; set; } = "";
    public string Color { get; set; } = "#0078D4";
    public string SubText { get; set; } = "";
    public double Percentage { get; set; }
}
