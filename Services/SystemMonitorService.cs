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
        var cpuTemps = new List<double>();
        var fallbackTemps = new List<double>();
        var anyTemps = new List<double>();

        // 1. Check /sys/class/hwmon (coretemp, k10temp, zenpower, etc.)
        const string hwmonPath = "/sys/class/hwmon";
        if (Directory.Exists(hwmonPath))
        {
            try
            {
                foreach (string hwmonDir in Directory.EnumerateDirectories(hwmonPath, "hwmon*"))
                {
                    string? driverName = TryReadFirstLine(Path.Combine(hwmonDir, "name")) ??
                                         TryReadFirstLine(Path.Combine(hwmonDir, "device", "name"));
                    bool isCpuDriver = IsCpuDriver(driverName);
                    bool isIgnoredDriver = IsIgnoredHwmonDriver(driverName);

                    string searchDir = Directory.Exists(Path.Combine(hwmonDir, "device")) &&
                                       !Directory.EnumerateFiles(hwmonDir, "temp*_input").Any()
                        ? Path.Combine(hwmonDir, "device")
                        : hwmonDir;

                    foreach (string inputFile in Directory.EnumerateFiles(searchDir, "temp*_input"))
                    {
                        if (TryReadTemperature(inputFile) is not double temp) continue;

                        anyTemps.Add(temp);

                        string labelFile = inputFile.EndsWith("_input", StringComparison.Ordinal)
                            ? inputFile[..^5] + "label"
                            : inputFile;
                        string? label = TryReadFirstLine(labelFile);

                        if (isCpuDriver || IsCpuLabel(label))
                        {
                            cpuTemps.Add(temp);
                        }
                        else if (!isIgnoredDriver)
                        {
                            fallbackTemps.Add(temp);
                        }
                    }
                }
            }
            catch
            {
                // Ignore sysfs navigation errors
            }
        }

        // 2. Check /sys/class/thermal (x86_pkg_temp, cpu-thermal, etc.)
        const string thermalPath = "/sys/class/thermal";
        if (Directory.Exists(thermalPath))
        {
            try
            {
                foreach (string zoneDir in Directory.EnumerateDirectories(thermalPath, "thermal_zone*"))
                {
                    if (TryReadTemperature(Path.Combine(zoneDir, "temp")) is not double temp) continue;

                    anyTemps.Add(temp);

                    string? type = TryReadFirstLine(Path.Combine(zoneDir, "type"));
                    if (IsCpuThermalType(type))
                    {
                        cpuTemps.Add(temp);
                    }
                    else if (!IsIgnoredThermalType(type))
                    {
                        fallbackTemps.Add(temp);
                    }
                }
            }
            catch
            {
                // Ignore sysfs navigation errors
            }
        }

        if (cpuTemps.Count > 0) return cpuTemps.Max();
        if (fallbackTemps.Count > 0) return fallbackTemps.Max();
        if (anyTemps.Count > 0) return anyTemps.Max();

        return null;
    }

    private static double? TryReadTemperature(string filePath)
    {
        try
        {
            if (!File.Exists(filePath)) return null;
            string text = File.ReadAllText(filePath).Trim();
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
            {
                double celsius = value > 200 ? value / 1000d : value;
                if (celsius is > 0 and < 150) return celsius;
            }
        }
        catch
        {
            // Ignore sysfs file read errors
        }

        return null;
    }

    private static string? TryReadFirstLine(string filePath)
    {
        try
        {
            if (!File.Exists(filePath)) return null;
            using var reader = new StreamReader(filePath);
            return reader.ReadLine()?.Trim();
        }
        catch
        {
            return null;
        }
    }

    private static bool IsCpuDriver(string? driverName)
    {
        if (string.IsNullOrWhiteSpace(driverName)) return false;
        string name = driverName.ToLowerInvariant();
        return name is "coretemp" or "k10temp" or "zenpower" or "via_cputemp" ||
               name.Contains("cputemp", StringComparison.Ordinal) ||
               name.Contains("cpu_thermal", StringComparison.Ordinal) ||
               name.Contains("soc_thermal", StringComparison.Ordinal);
    }

    private static bool IsCpuLabel(string? label)
    {
        if (string.IsNullOrWhiteSpace(label)) return false;
        string l = label.ToLowerInvariant();
        return l.Contains("package", StringComparison.Ordinal) ||
               l.Contains("core", StringComparison.Ordinal) ||
               l.Contains("tctl", StringComparison.Ordinal) ||
               l.Contains("tdie", StringComparison.Ordinal) ||
               l.Contains("tccd", StringComparison.Ordinal) ||
               l == "cpu" ||
               l.StartsWith("cpu ", StringComparison.Ordinal) ||
               l.EndsWith(" cpu", StringComparison.Ordinal) ||
               l.Contains("cputin", StringComparison.Ordinal) ||
               l.Contains("t_cpu", StringComparison.Ordinal);
    }

    private static bool IsIgnoredHwmonDriver(string? driverName)
    {
        if (string.IsNullOrWhiteSpace(driverName)) return false;
        string name = driverName.ToLowerInvariant();
        return name.Contains("nvme", StringComparison.Ordinal) ||
               name.Contains("amdgpu", StringComparison.Ordinal) ||
               name.Contains("nouveau", StringComparison.Ordinal) ||
               name.Contains("radeon", StringComparison.Ordinal) ||
               name.Contains("iwlwifi", StringComparison.Ordinal) ||
               name.Contains("wireless", StringComparison.Ordinal) ||
               name.Contains("battery", StringComparison.Ordinal) ||
               name.Contains("hid", StringComparison.Ordinal) ||
               name.Contains("acpitz", StringComparison.Ordinal) ||
               name.Contains("pch", StringComparison.Ordinal) ||
               name.Contains("jc42", StringComparison.Ordinal);
    }

    private static bool IsCpuThermalType(string? type)
    {
        if (string.IsNullOrWhiteSpace(type)) return false;
        string t = type.ToLowerInvariant();
        if (t.Contains("acpitz", StringComparison.Ordinal) || t.Contains("pch", StringComparison.Ordinal))
            return false;

        return t is "x86_pkg_temp" ||
               t.Contains("cpu", StringComparison.Ordinal) ||
               t.Contains("core", StringComparison.Ordinal) ||
               t.Contains("pkg", StringComparison.Ordinal) ||
               t.Contains("package", StringComparison.Ordinal) ||
               t.Contains("k10temp", StringComparison.Ordinal) ||
               t.Contains("zenpower", StringComparison.Ordinal) ||
               t.Contains("soc", StringComparison.Ordinal);
    }

    private static bool IsIgnoredThermalType(string? type)
    {
        if (string.IsNullOrWhiteSpace(type)) return false;
        string t = type.ToLowerInvariant();
        return t.Contains("acpitz", StringComparison.Ordinal) ||
               t.Contains("pch", StringComparison.Ordinal) ||
               t.Contains("nvme", StringComparison.Ordinal) ||
               t.Contains("gpu", StringComparison.Ordinal) ||
               t.Contains("battery", StringComparison.Ordinal);
    }

    private List<DiskUsage> ReadDisks() => DriveInfo.GetDrives()
        .Where(drive => drive.IsReady && drive.DriveType == DriveType.Fixed && drive.TotalSize > 0)
        .Where(drive => !IsIgnoredMountPoint(drive.RootDirectory.FullName))
        .OrderBy(drive => drive.Name)
        .Select(drive =>
        {
            SmartDiskInfo smart = GetSmartInfo(drive.RootDirectory.FullName, out string? physicalDevicePath);
            string deviceName = !string.IsNullOrWhiteSpace(physicalDevicePath)
                ? Path.GetFileName(physicalDevicePath)
                : string.Empty;

            return new DiskUsage
            {
                Name = drive.Name.TrimEnd(Path.DirectorySeparatorChar),
                MountPoint = drive.RootDirectory.FullName,
                Device = deviceName,
                UsedBytes = drive.TotalSize - drive.AvailableFreeSpace,
                TotalBytes = drive.TotalSize,
                TemperatureCelsius = smart.TemperatureCelsius,
                SmartStatus = smart.StatusText,
                HasSmartErrors = smart.HasErrors
            };
        })
        .ToList();

    private SmartDiskInfo GetSmartInfo(string mountPoint, out string? devicePath)
    {
        devicePath = ResolvePhysicalDevice(mountPoint);
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
        string? rawDevicePath = ResolveDeviceForMountPoint(mountPoint);
        if (string.IsNullOrWhiteSpace(rawDevicePath)) return null;

        return ResolvePhysicalDriveFromDevice(rawDevicePath);
    }

    private static string? ResolveDeviceForMountPoint(string mountPoint)
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
            if (string.Equals(mountedAt, normalizedMountPoint, StringComparison.Ordinal))
            {
                return fields[0];
            }
        }

        return null;
    }

    private static string? ResolvePhysicalDriveFromDevice(string devicePath, int maxDepth = 5)
    {
        if (maxDepth <= 0 || string.IsNullOrWhiteSpace(devicePath)) return null;

        try
        {
            if (File.Exists(devicePath))
            {
                var linkInfo = new FileInfo(devicePath);
                if (linkInfo.LinkTarget != null)
                {
                    string? target = linkInfo.ResolveLinkTarget(true)?.FullName;
                    if (!string.IsNullOrEmpty(target) && target.StartsWith("/dev/", StringComparison.Ordinal))
                    {
                        devicePath = target;
                    }
                }
            }
        }
        catch { }

        string name = Path.GetFileName(devicePath);

        // 1. Check if device is a loop device e.g. loop0, loop1 (common with VeraCrypt file/device mounts)
        if (name.StartsWith("loop", StringComparison.OrdinalIgnoreCase))
        {
            string backingFilePath = $"/sys/block/{name}/loop/backing_file";
            if (File.Exists(backingFilePath))
            {
                try
                {
                    string backingFile = File.ReadAllText(backingFilePath).Trim();
                    if (backingFile.StartsWith("/dev/", StringComparison.OrdinalIgnoreCase))
                    {
                        return ResolvePhysicalDriveFromDevice(backingFile, maxDepth - 1);
                    }
                    else if (File.Exists(backingFile))
                    {
                        string? parentMountPoint = FindMountPointForFilePath(backingFile);
                        if (!string.IsNullOrEmpty(parentMountPoint))
                        {
                            string? parentDevice = ResolveDeviceForMountPoint(parentMountPoint);
                            if (!string.IsNullOrEmpty(parentDevice) && !parentDevice.Equals(devicePath, StringComparison.OrdinalIgnoreCase))
                            {
                                return ResolvePhysicalDriveFromDevice(parentDevice, maxDepth - 1);
                            }
                        }
                    }
                }
                catch { }
            }
        }

        // 2. Check if device is a device-mapper / veracrypt device e.g. dm-0, mapper/veracrypt1
        if (name.StartsWith("dm-", StringComparison.OrdinalIgnoreCase) || devicePath.Contains("/mapper/", StringComparison.OrdinalIgnoreCase))
        {
            string realDmName = name;
            if (devicePath.Contains("/mapper/", StringComparison.OrdinalIgnoreCase))
            {
                realDmName = FindDmDeviceName(name) ?? name;
            }

            string slavesDir = $"/sys/class/block/{realDmName}/slaves";
            if (!Directory.Exists(slavesDir))
            {
                slavesDir = $"/sys/block/{realDmName}/slaves";
            }

            if (Directory.Exists(slavesDir))
            {
                try
                {
                    var slaves = Directory.GetDirectories(slavesDir)
                        .Concat(Directory.GetFiles(slavesDir))
                        .Select(Path.GetFileName)
                        .Where(n => !string.IsNullOrEmpty(n))
                        .ToList();

                    foreach (var slave in slaves)
                    {
                        string slaveDevicePath = $"/dev/{slave}";
                        string? resolved = ResolvePhysicalDriveFromDevice(slaveDevicePath, maxDepth - 1);
                        if (!string.IsNullOrEmpty(resolved)) return resolved;
                    }
                }
                catch { }
            }
        }

        // 3. Fallback / Standard physical device mapping (e.g. sdb1 -> /dev/sdb, nvme0n1p2 -> /dev/nvme0n1)
        return ToPhysicalDevice(devicePath);
    }

    private static string? FindMountPointForFilePath(string filePath)
    {
        const string mountsPath = "/proc/mounts";
        if (!File.Exists(mountsPath)) return null;

        string bestMatch = string.Empty;

        foreach (string line in File.ReadLines(mountsPath))
        {
            string[] fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length < 2 || !fields[0].StartsWith("/dev/", StringComparison.Ordinal)) continue;

            string mountedAt = fields[1].Replace("\\040", " ");
            if (filePath.StartsWith(mountedAt, StringComparison.Ordinal) && mountedAt.Length > bestMatch.Length)
            {
                bestMatch = mountedAt;
            }
        }

        return string.IsNullOrEmpty(bestMatch) ? null : bestMatch;
    }

    private static string? FindDmDeviceName(string mapperName)
    {
        const string sysClassBlock = "/sys/class/block";
        if (!Directory.Exists(sysClassBlock)) return null;

        try
        {
            foreach (var dir in Directory.GetDirectories(sysClassBlock, "dm-*"))
            {
                string nameFile = Path.Combine(dir, "dm", "name");
                if (File.Exists(nameFile))
                {
                    string content = File.ReadAllText(nameFile).Trim();
                    if (string.Equals(content, mapperName, StringComparison.OrdinalIgnoreCase))
                    {
                        return Path.GetFileName(dir);
                    }
                }
            }
        }
        catch { }

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
                passed ? "S.M.A.R.T. OK" : "S.M.A.R.T. FAILED",
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
        public static SmartDiskInfo Unavailable => new(null, "S.M.A.R.T. N/A", false);
    }

    private static bool IsIgnoredMountPoint(string mountPoint) =>
        mountPoint.Equals("/sys", StringComparison.OrdinalIgnoreCase) ||
        mountPoint.StartsWith("/sys/", StringComparison.OrdinalIgnoreCase) ||
        mountPoint.Equals("/snap", StringComparison.OrdinalIgnoreCase) ||
        mountPoint.StartsWith("/snap/", StringComparison.OrdinalIgnoreCase) ||
        mountPoint.StartsWith("/var/lib/snapd", StringComparison.OrdinalIgnoreCase) ||
        mountPoint.StartsWith("/dev", StringComparison.OrdinalIgnoreCase) ||
        mountPoint.StartsWith("/run", StringComparison.OrdinalIgnoreCase) ||
        mountPoint.StartsWith("/proc", StringComparison.OrdinalIgnoreCase);
}
