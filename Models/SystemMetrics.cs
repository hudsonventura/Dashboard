using System.Collections.Generic;

namespace Dashboard.Models;

public sealed class SystemMetricsSnapshot
{
    public List<double> CpuUsagePercentages { get; init; } = new();
    public long MemoryUsedBytes { get; init; }
    public long MemoryTotalBytes { get; init; }
    public long SwapUsedBytes { get; init; }
    public long SwapTotalBytes { get; init; }
    public double? CpuTemperatureCelsius { get; init; }
    public List<DiskUsage> Disks { get; init; } = new();
}

public sealed class DiskUsage
{
    public string Name { get; init; } = string.Empty;
    public string MountPoint { get; init; } = string.Empty;
    public long UsedBytes { get; init; }
    public long TotalBytes { get; init; }
    public double? TemperatureCelsius { get; init; }
    public string SmartStatus { get; init; } = "S.M.A.R.T. indisponível";
    public bool HasSmartErrors { get; init; }
}
