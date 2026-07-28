namespace SolisDashboard.Models;

public sealed class CpuCoreUsage
{
    public int Index { get; init; }
    public double UsagePercent { get; init; }
}

public sealed class DiskMetrics
{
    public string Device { get; init; } = "";
    public string MountPoint { get; init; } = "";
    public double UsedBytes { get; init; }
    public double TotalBytes { get; init; }
    public double UsagePercent { get; init; }
    public double? TemperatureCelsius { get; init; }
    /// <summary>"ok", mensagem de erro SMART, ou "N/D".</summary>
    public string SmartStatus { get; init; } = "N/D";
}

public sealed class SystemMetricsSnapshot
{
    public IReadOnlyList<CpuCoreUsage> Cores { get; init; } = [];
    public IReadOnlyList<DiskMetrics> Disks { get; init; } = [];
    public double MemoryUsedBytes { get; init; }
    public double MemoryTotalBytes { get; init; }
    public double SwapUsedBytes { get; init; }
    public double SwapTotalBytes { get; init; }
    public double? CpuTemperatureCelsius { get; init; }
    public string Platform { get; init; } = "";
}
