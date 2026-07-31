using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Dashboard.Models;

namespace Dashboard.Services;

/// <summary>Obtém métricas locais sem depender de ferramentas externas.</summary>
public sealed class SystemMonitorService
{
    private readonly Dictionary<string, (long Total, long Idle)> _previousCpuTimes = new();

    public SystemMetricsSnapshot GetSnapshot()
    {
        var memory = ReadMemoryInfo();

        return new SystemMetricsSnapshot
        {
            CpuUsagePercentages = ReadCpuUsage(),
            MemoryUsedBytes = memory.MemoryUsed,
            MemoryTotalBytes = memory.MemoryTotal,
            SwapUsedBytes = memory.SwapUsed,
            SwapTotalBytes = memory.SwapTotal,
            CpuTemperatureCelsius = ReadCpuTemperature(),
            Disks = ReadDisks()
        };
    }

    private List<double> ReadCpuUsage()
    {
        const string statPath = "/proc/stat";
        if (!File.Exists(statPath)) return new List<double>();

        var result = new List<double>();
        foreach (string line in File.ReadLines(statPath).Where(line => line.StartsWith("cpu", StringComparison.Ordinal)))
        {
            string[] fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length < 5 || fields[0] == "cpu") continue;

            long[] values = fields.Skip(1)
                .Select(value => long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed) ? parsed : 0)
                .ToArray();
            long total = values.Sum();
            long idle = values[3] + (values.Length > 4 ? values[4] : 0);

            if (_previousCpuTimes.TryGetValue(fields[0], out var previous))
            {
                long totalDelta = total - previous.Total;
                long idleDelta = idle - previous.Idle;
                result.Add(totalDelta > 0 ? Math.Clamp((totalDelta - idleDelta) * 100d / totalDelta, 0, 100) : 0);
            }
            else
            {
                result.Add(0);
            }

            _previousCpuTimes[fields[0]] = (total, idle);
        }

        return result;
    }

    private static (long MemoryUsed, long MemoryTotal, long SwapUsed, long SwapTotal) ReadMemoryInfo()
    {
        const string memInfoPath = "/proc/meminfo";
        if (!File.Exists(memInfoPath)) return default;

        var values = File.ReadLines(memInfoPath)
            .Select(line => line.Split(':', 2))
            .Where(parts => parts.Length == 2)
            .ToDictionary(
                parts => parts[0],
                parts => long.TryParse(parts[1].Trim().Split(' ')[0], out long value) ? value * 1024 : 0,
                StringComparer.OrdinalIgnoreCase);

        long total = values.GetValueOrDefault("MemTotal");
        long available = values.GetValueOrDefault("MemAvailable");
        long swapTotal = values.GetValueOrDefault("SwapTotal");
        long swapFree = values.GetValueOrDefault("SwapFree");

        return (Math.Max(0, total - available), total, Math.Max(0, swapTotal - swapFree), swapTotal);
    }

    private static double? ReadCpuTemperature()
    {
        const string thermalPath = "/sys/class/thermal";
        if (!Directory.Exists(thermalPath)) return null;

        foreach (string file in Directory.EnumerateFiles(thermalPath, "temp", SearchOption.AllDirectories))
        {
            if (double.TryParse(File.ReadAllText(file).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
            {
                double celsius = value > 200 ? value / 1000d : value;
                if (celsius is > 0 and < 150) return celsius;
            }
        }

        return null;
    }

    private static List<DiskUsage> ReadDisks() => DriveInfo.GetDrives()
        .Where(drive => drive.IsReady && drive.DriveType == DriveType.Fixed && drive.TotalSize > 0)
        .Where(drive => !IsSysMount(drive.RootDirectory.FullName))
        .OrderBy(drive => drive.Name)
        .Take(3)
        .Select(drive => new DiskUsage
        {
            Name = drive.Name.TrimEnd(Path.DirectorySeparatorChar),
            MountPoint = drive.RootDirectory.FullName,
            UsedBytes = drive.TotalSize - drive.AvailableFreeSpace,
            TotalBytes = drive.TotalSize
        })
        .ToList();

    private static bool IsSysMount(string mountPoint) =>
        mountPoint.Equals("/sys", StringComparison.Ordinal) ||
        mountPoint.StartsWith("/sys/", StringComparison.Ordinal);
}
