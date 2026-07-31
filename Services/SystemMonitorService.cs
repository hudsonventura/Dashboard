using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using Dashboard.Models;

namespace Dashboard.Services;

/// <summary>Obtém métricas locais sem depender de ferramentas externas.</summary>
public sealed class SystemMonitorService
{
    private static readonly TimeSpan SmartCacheDuration = TimeSpan.FromMinutes(5);
    private readonly Dictionary<string, (long Total, long Idle)> _previousCpuTimes = new();
    private readonly Dictionary<string, (DateTime ReadAt, SmartDiskInfo Info)> _smartCache = new(StringComparer.Ordinal);

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

    private List<DiskUsage> ReadDisks() => DriveInfo.GetDrives()
        .Where(drive => drive.IsReady && drive.DriveType == DriveType.Fixed && drive.TotalSize > 0)
        .Where(drive => !IsSysMount(drive.RootDirectory.FullName))
        .OrderBy(drive => drive.Name)
        .Take(3)
        .Select(drive =>
        {
            SmartDiskInfo smart = GetSmartInfo(drive.RootDirectory.FullName);
            return new DiskUsage
            {
                Name = drive.Name.TrimEnd(Path.DirectorySeparatorChar),
                MountPoint = drive.RootDirectory.FullName,
                UsedBytes = drive.TotalSize - drive.AvailableFreeSpace,
                TotalBytes = drive.TotalSize,
                TemperatureCelsius = smart.TemperatureCelsius,
                SmartStatus = smart.StatusText,
                HasSmartErrors = smart.HasErrors
            };
        })
        .ToList();

    private SmartDiskInfo GetSmartInfo(string mountPoint)
    {
        string? devicePath = ResolvePhysicalDevice(mountPoint);
        if (string.IsNullOrWhiteSpace(devicePath))
            return SmartDiskInfo.Unavailable;

        if (_smartCache.TryGetValue(devicePath, out var cached) && DateTime.UtcNow - cached.ReadAt < SmartCacheDuration)
            return cached.Info;

        SmartDiskInfo info = ReadSmartInfo(devicePath);
        _smartCache[devicePath] = (DateTime.UtcNow, info);
        return info;
    }

    private static string? ResolvePhysicalDevice(string mountPoint)
    {
        const string mountsPath = "/proc/mounts";
        if (!File.Exists(mountsPath)) return null;

        string normalizedMountPoint = mountPoint.TrimEnd(Path.DirectorySeparatorChar);
        if (normalizedMountPoint.Length == 0) normalizedMountPoint = Path.DirectorySeparatorChar.ToString();

        foreach (string line in File.ReadLines(mountsPath))
        {
            string[] fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length < 2 || !fields[0].StartsWith("/dev/", StringComparison.Ordinal)) continue;

            string mountedAt = fields[1].Replace("\\040", " ").TrimEnd(Path.DirectorySeparatorChar);
            if (mountedAt.Length == 0) mountedAt = Path.DirectorySeparatorChar.ToString();
            if (!string.Equals(mountedAt, normalizedMountPoint, StringComparison.Ordinal)) continue;

            return ToPhysicalDevice(fields[0]);
        }

        return null;
    }

    private static string? ToPhysicalDevice(string devicePath)
    {
        string name = Path.GetFileName(devicePath);
        string physicalName = name;

        int partitionIndex = name.LastIndexOf('p');
        if (name.StartsWith("nvme", StringComparison.Ordinal) && partitionIndex > 0 &&
            int.TryParse(name[(partitionIndex + 1)..], out _))
        {
            physicalName = name[..partitionIndex];
        }
        else if ((name.StartsWith("sd", StringComparison.Ordinal) || name.StartsWith("vd", StringComparison.Ordinal) ||
                  name.StartsWith("xvd", StringComparison.Ordinal) || name.StartsWith("hd", StringComparison.Ordinal)))
        {
            int digitIndex = name.Length;
            while (digitIndex > 0 && char.IsDigit(name[digitIndex - 1])) digitIndex--;
            physicalName = name[..digitIndex];
        }

        return string.IsNullOrWhiteSpace(physicalName) ? null : $"/dev/{physicalName}";
    }

    private static SmartDiskInfo ReadSmartInfo(string devicePath)
    {
        try
        {
            var startInfo = new ProcessStartInfo("smartctl")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("-j");
            startInfo.ArgumentList.Add("-A");
            startInfo.ArgumentList.Add("-H");
            startInfo.ArgumentList.Add(devicePath);

            using Process? process = Process.Start(startInfo);
            if (process is null || !process.WaitForExit(3000))
            {
                if (process is not null && !process.HasExited) process.Kill(true);
                return SmartDiskInfo.Unavailable;
            }

            string json = process.StandardOutput.ReadToEnd();
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;

            if (HasSmartCtlError(root) || !root.TryGetProperty("smart_status", out JsonElement smartStatus) ||
                !smartStatus.TryGetProperty("passed", out JsonElement passedElement))
            {
                return SmartDiskInfo.Unavailable;
            }

            bool passed = passedElement.GetBoolean();

            return new SmartDiskInfo(
                FindTemperature(root),
                passed ? "S.M.A.R.T. OK" : "S.M.A.R.T. FALHA",
                !passed);
        }
        catch
        {
            return SmartDiskInfo.Unavailable;
        }
    }

    private static bool HasSmartCtlError(JsonElement root) =>
        HasErrorMessage(root) ||
        (root.TryGetProperty("smartctl", out JsonElement smartCtl) && HasErrorMessage(smartCtl));

    private static bool HasErrorMessage(JsonElement element) =>
        element.TryGetProperty("messages", out JsonElement messages) &&
        messages.ValueKind == JsonValueKind.Array &&
        messages.EnumerateArray().Any(message =>
            message.TryGetProperty("severity", out JsonElement severity) &&
            string.Equals(severity.GetString(), "error", StringComparison.OrdinalIgnoreCase));

    private static double? FindTemperature(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("temperature", out JsonElement temperature))
            {
                if (temperature.ValueKind == JsonValueKind.Number && temperature.TryGetDouble(out double value) && value is > -50 and < 150)
                    return value;

                if (temperature.ValueKind == JsonValueKind.Object && temperature.TryGetProperty("current", out JsonElement current) &&
                    current.TryGetDouble(out value) && value is > -50 and < 150)
                    return value;
            }

            foreach (JsonProperty property in element.EnumerateObject())
            {
                double? nestedTemperature = FindTemperature(property.Value);
                if (nestedTemperature is not null) return nestedTemperature;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement child in element.EnumerateArray())
            {
                double? nestedTemperature = FindTemperature(child);
                if (nestedTemperature is not null) return nestedTemperature;
            }
        }

        return null;
    }

    private readonly record struct SmartDiskInfo(double? TemperatureCelsius, string StatusText, bool HasErrors)
    {
        public static SmartDiskInfo Unavailable => new(null, "S.M.A.R.T. indisponível", false);
    }

    private static bool IsSysMount(string mountPoint) =>
        mountPoint.Equals("/sys", StringComparison.Ordinal) ||
        mountPoint.StartsWith("/sys/", StringComparison.Ordinal);
}
