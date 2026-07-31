using CommunityToolkit.Mvvm.ComponentModel;

namespace Dashboard.ViewModels;

public partial class CpuUsageViewModel : ObservableObject
{
    public string Label { get; }

    [ObservableProperty] private double _usage;
    [ObservableProperty] private string _usageText = "0.0%";
    [ObservableProperty] private string _barColor = UsageBarPalette.Blue;
    [ObservableProperty] private bool _isCritical;

    public CpuUsageViewModel(string label) => Label = label;

    public void Update(double usage)
    {
        Usage = usage;
        UsageText = $"{usage:F1}%";
        BarColor = UsageBarPalette.ForPercent(usage);
        IsCritical = UsageBarPalette.IsCritical(usage);
    }
}

public partial class DiskUsageViewModel : ObservableObject
{
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _mountPoint = string.Empty;
    [ObservableProperty] private double _usage;
    [ObservableProperty] private string _usageText = "--";
    [ObservableProperty] private string _barColor = UsageBarPalette.Blue;
    [ObservableProperty] private bool _isCritical;
    [ObservableProperty] private string _temperatureText = "Temp. N/D";
    [ObservableProperty] private string _smartStatusText = "S.M.A.R.T. indisponível";
    [ObservableProperty] private string _smartStatusColor = "#94A3B8";
}
