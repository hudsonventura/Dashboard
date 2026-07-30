using CommunityToolkit.Mvvm.ComponentModel;

namespace Dashboard.ViewModels;

public partial class CpuUsageViewModel : ObservableObject
{
    public string Label { get; }

    [ObservableProperty] private double _usage;
    [ObservableProperty] private string _usageText = "0.0%";
    [ObservableProperty] private string _barColor = "#38BDF8";

    public CpuUsageViewModel(string label) => Label = label;

    public void Update(double usage)
    {
        Usage = usage;
        UsageText = $"{usage:F1}%";
        BarColor = usage >= 90 ? "#BE123C" : "#38BDF8";
    }
}

public partial class DiskUsageViewModel : ObservableObject
{
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _mountPoint = string.Empty;
    [ObservableProperty] private double _usage;
    [ObservableProperty] private string _usageText = "--";
}
