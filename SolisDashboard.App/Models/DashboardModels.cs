using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using SolisDashboard.App.Display;

namespace SolisDashboard.App.Models;

public sealed class GenerationPoint
{
    public string Time { get; init; } = "";
    public double PowerW { get; init; }
}

public sealed class MetricCard
{
    public string Title { get; init; } = "";
    public string Value { get; init; } = "";
    public string Unit { get; init; } = "";
    public string Subtitle { get; init; } = "";
}

public sealed class ForecastDayItem
{
    public string DateLabel { get; init; } = "";
    public string Condition { get; init; } = "";
    public string IconKey { get; init; } = "unknown";
    public string TempRange { get; init; } = "";
    public string Rain { get; init; } = "";
    public string Wind { get; init; } = "";
}

public partial class CpuCoreItem : ObservableObject
{
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private double _usagePercent;
    [ObservableProperty] private string _usageText = "0%";
    [ObservableProperty] private IBrush _barBrush = UsageBarPalette.ForPercent(0);
    [ObservableProperty] private bool _isCritical;

    partial void OnUsagePercentChanged(double value)
    {
        BarBrush = UsageBarPalette.ForPercent(value);
        IsCritical = UsageBarPalette.IsCritical(value);
    }
}

public partial class DiskItem : ObservableObject
{
    [ObservableProperty] private string _device = "";
    [ObservableProperty] private string _mountPoint = "";
    [ObservableProperty] private string _usageText = "-";
    [ObservableProperty] private double _usagePercent;
    [ObservableProperty] private IBrush _barBrush = UsageBarPalette.ForPercent(0);
    [ObservableProperty] private bool _isCritical;
    [ObservableProperty] private string _temperatureText = "N/D";
    [ObservableProperty] private string _smartStatus = "N/D";
    [ObservableProperty] private bool _smartOk;
    [ObservableProperty] private IBrush _smartBrush = UsageBarPalette.SwapRed;

    partial void OnUsagePercentChanged(double value)
    {
        BarBrush = UsageBarPalette.ForPercent(value);
        IsCritical = UsageBarPalette.IsCritical(value);
    }

    partial void OnSmartStatusChanged(string value)
    {
        SmartOk = string.Equals(value, "ok", StringComparison.OrdinalIgnoreCase);
        SmartBrush = SmartOk
            ? UsageBarPalette.GreenOk
            : string.Equals(value, "N/D", StringComparison.OrdinalIgnoreCase)
                ? Brushes.Gray
                : UsageBarPalette.SwapRed;
    }
}
