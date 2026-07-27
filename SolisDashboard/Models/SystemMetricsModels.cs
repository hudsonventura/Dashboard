namespace SolisDashboard.Models;

public sealed class CpuCoreUsage
{
    public int Index { get; init; }
    public double UsagePercent { get; init; }
}

public sealed class SystemMetricsSnapshot
{
    public IReadOnlyList<CpuCoreUsage> Cores { get; init; } = [];
    public double MemoryUsedBytes { get; init; }
    public double MemoryTotalBytes { get; init; }
    public double SwapUsedBytes { get; init; }
    public double SwapTotalBytes { get; init; }
    public double? CpuTemperatureCelsius { get; init; }
    public string Platform { get; init; } = "";
}
