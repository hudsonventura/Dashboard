using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using SolisDashboard.Models;

namespace SolisDashboard.Services;

public sealed class SystemMetricsService : IDisposable
{
    private readonly object _gate = new();
    private long[]? _prevIdle;
    private long[]? _prevTotal;
    private PerformanceCounter[]? _windowsCoreCounters;
    private bool _windowsPrimed;
    private bool _disposed;
    private DateTime _diskExtrasUtc = DateTime.MinValue;
    private readonly Dictionary<string, (double? Temp, string Smart)> _diskExtrasCache = new(StringComparer.Ordinal);
    private static readonly TimeSpan DiskExtrasTtl = TimeSpan.FromSeconds(30);

    private static readonly HashSet<string> IgnoredFsTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "tmpfs", "devtmpfs", "devfs", "proc", "sysfs", "cgroup", "cgroup2", "pstore", "bpf",
        "debugfs", "tracefs", "securityfs", "fusectl", "configfs", "mqueue", "hugetlbfs",
        "overlay", "aufs", "squashfs", "iso9660", "udf", "ramfs", "rpc_pipefs", "binfmt_misc",
        "autofs", "efivarfs", "nsfs", "fuse.gvfsd-fuse", "fuse.portal", "tracefs"
    };

    public SystemMetricsSnapshot Sample()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (OperatingSystem.IsLinux())
            return SampleLinux();
        if (OperatingSystem.IsWindows())
            return SampleWindows();

        return new SystemMetricsSnapshot
        {
            Platform = RuntimeInformation.OSDescription,
            Cores = Array.Empty<CpuCoreUsage>()
        };
    }

    private SystemMetricsSnapshot SampleLinux()
    {
        var cores = ReadLinuxCpu();
        var (memUsed, memTotal, swapUsed, swapTotal) = ReadLinuxMemory();
        var temp = ReadLinuxCpuTemperature();

        return new SystemMetricsSnapshot
        {
            Platform = "Linux",
            Cores = cores,
            Disks = ReadLinuxDisks(),
            MemoryUsedBytes = memUsed,
            MemoryTotalBytes = memTotal,
            SwapUsedBytes = swapUsed,
            SwapTotalBytes = swapTotal,
            CpuTemperatureCelsius = temp
        };
    }

    [SupportedOSPlatform("windows")]
    private SystemMetricsSnapshot SampleWindows()
    {
        var cores = ReadWindowsCpu();
        var (memUsed, memTotal, swapUsed, swapTotal) = ReadWindowsMemory();
        var temp = ReadWindowsCpuTemperature();

        return new SystemMetricsSnapshot
        {
            Platform = "Windows",
            Cores = cores,
            MemoryUsedBytes = memUsed,
            MemoryTotalBytes = memTotal,
            SwapUsedBytes = swapUsed,
            SwapTotalBytes = swapTotal,
            CpuTemperatureCelsius = temp
        };
    }

    private List<CpuCoreUsage> ReadLinuxCpu()
    {
        var idle = new List<long>();
        var total = new List<long>();

        foreach (var line in File.ReadLines("/proc/stat"))
        {
            if (!line.StartsWith("cpu", StringComparison.Ordinal) || line.StartsWith("cpu ", StringComparison.Ordinal))
                continue;

            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 5 || !parts[0].StartsWith("cpu", StringComparison.Ordinal))
                continue;
            if (!char.IsDigit(parts[0][^1]))
                continue;

            long sum = 0;
            for (var i = 1; i < parts.Length; i++)
            {
                if (long.TryParse(parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out var v))
                    sum += v;
            }

            var idleVal = 0L;
            if (parts.Length > 4 &&
                long.TryParse(parts[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out var idleParsed))
            {
                idleVal = idleParsed;
                if (parts.Length > 5 &&
                    long.TryParse(parts[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out var iowait))
                {
                    idleVal += iowait;
                }
            }

            idle.Add(idleVal);
            total.Add(sum);
        }

        return DiffCpu(idle.ToArray(), total.ToArray());
    }

    [SupportedOSPlatform("windows")]
    private List<CpuCoreUsage> ReadWindowsCpu()
    {
        EnsureWindowsCounters();
        if (_windowsCoreCounters is null || _windowsCoreCounters.Length == 0)
            return [];

        if (!_windowsPrimed)
        {
            foreach (var c in _windowsCoreCounters)
                _ = c.NextValue();
            _windowsPrimed = true;
            return Enumerable.Range(0, _windowsCoreCounters.Length)
                .Select(i => new CpuCoreUsage { Index = i, UsagePercent = 0 })
                .ToList();
        }

        var result = new List<CpuCoreUsage>(_windowsCoreCounters.Length);
        for (var i = 0; i < _windowsCoreCounters.Length; i++)
        {
            var usage = Math.Clamp(_windowsCoreCounters[i].NextValue(), 0f, 100f);
            result.Add(new CpuCoreUsage { Index = i, UsagePercent = usage });
        }

        return result;
    }

    [SupportedOSPlatform("windows")]
    private void EnsureWindowsCounters()
    {
        if (_windowsCoreCounters is not null)
            return;

        try
        {
            var category = new PerformanceCounterCategory("Processor");
            var names = category.GetInstanceNames()
                .Where(n => n != "_Total" && int.TryParse(n, out _))
                .OrderBy(n => int.Parse(n, CultureInfo.InvariantCulture))
                .ToArray();

            _windowsCoreCounters = names
                .Select(n => new PerformanceCounter("Processor", "% Processor Time", n, true))
                .ToArray();
        }
        catch
        {
            _windowsCoreCounters = [];
        }
    }

    private List<CpuCoreUsage> DiffCpu(long[] idle, long[] total)
    {
        lock (_gate)
        {
            var result = new List<CpuCoreUsage>(idle.Length);
            if (_prevIdle is null || _prevTotal is null ||
                _prevIdle.Length != idle.Length || _prevTotal.Length != total.Length)
            {
                _prevIdle = idle;
                _prevTotal = total;
                for (var i = 0; i < idle.Length; i++)
                    result.Add(new CpuCoreUsage { Index = i, UsagePercent = 0 });
                return result;
            }

            for (var i = 0; i < idle.Length; i++)
            {
                var idleDelta = idle[i] - _prevIdle[i];
                var totalDelta = total[i] - _prevTotal[i];
                var usage = totalDelta <= 0
                    ? 0d
                    : Math.Clamp(100d * (1d - (double)idleDelta / totalDelta), 0d, 100d);
                result.Add(new CpuCoreUsage { Index = i, UsagePercent = usage });
            }

            _prevIdle = idle;
            _prevTotal = total;
            return result;
        }
    }

    private static (double memUsed, double memTotal, double swapUsed, double swapTotal) ReadLinuxMemory()
    {
        double memTotal = 0, memAvailable = 0, swapTotal = 0, swapFree = 0;
        foreach (var line in File.ReadLines("/proc/meminfo"))
        {
            if (line.StartsWith("MemTotal:", StringComparison.Ordinal))
                memTotal = ParseMemKb(line) * 1024d;
            else if (line.StartsWith("MemAvailable:", StringComparison.Ordinal))
                memAvailable = ParseMemKb(line) * 1024d;
            else if (line.StartsWith("SwapTotal:", StringComparison.Ordinal))
                swapTotal = ParseMemKb(line) * 1024d;
            else if (line.StartsWith("SwapFree:", StringComparison.Ordinal))
                swapFree = ParseMemKb(line) * 1024d;
        }

        var memUsed = Math.Max(0, memTotal - memAvailable);
        var swapUsed = Math.Max(0, swapTotal - swapFree);
        return (memUsed, memTotal, swapUsed, swapTotal);
    }

    private static double ParseMemKb(string line)
    {
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 &&
               double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
            ? v
            : 0;
    }

    [SupportedOSPlatform("windows")]
    private static (double memUsed, double memTotal, double swapUsed, double swapTotal) ReadWindowsMemory()
    {
        var status = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        if (!GlobalMemoryStatusEx(ref status))
            return (0, 0, 0, 0);

        var memTotal = (double)status.ullTotalPhys;
        var memUsed = memTotal - status.ullAvailPhys;
        // Page file (commit) includes physical + pagefile; approximate swap as pagefile alone.
        var swapTotal = Math.Max(0, (double)status.ullTotalPageFile - status.ullTotalPhys);
        var swapUsed = Math.Max(0, swapTotal - Math.Max(0, (double)status.ullAvailPageFile - status.ullAvailPhys));
        return (memUsed, memTotal, swapUsed, swapTotal);
    }

    private static double? ReadLinuxCpuTemperature()
    {
        try
        {
            // Prefer package / CPU thermal zones.
            var zones = Directory.Exists("/sys/class/thermal")
                ? Directory.GetDirectories("/sys/class/thermal", "thermal_zone*")
                : [];

            double? preferred = null;
            double? fallback = null;

            foreach (var zone in zones)
            {
                var typePath = Path.Combine(zone, "type");
                var tempPath = Path.Combine(zone, "temp");
                if (!File.Exists(tempPath))
                    continue;

                var type = File.Exists(typePath)
                    ? File.ReadAllText(typePath).Trim()
                    : "";
                if (!TryReadMilliCelsius(tempPath, out var celsius))
                    continue;

                if (type.Contains("x86_pkg", StringComparison.OrdinalIgnoreCase) ||
                    type.Contains("cpu-thermal", StringComparison.OrdinalIgnoreCase) ||
                    type.Contains("soc", StringComparison.OrdinalIgnoreCase) ||
                    type.Equals("CPU", StringComparison.OrdinalIgnoreCase))
                {
                    preferred = celsius;
                    break;
                }

                fallback ??= celsius;
            }

            if (preferred is not null)
                return preferred;

            if (Directory.Exists("/sys/class/hwmon"))
            {
                foreach (var hwmon in Directory.GetDirectories("/sys/class/hwmon"))
                {
                    var namePath = Path.Combine(hwmon, "name");
                    var name = File.Exists(namePath) ? File.ReadAllText(namePath).Trim() : "";
                    var isCpu = name.Contains("coretemp", StringComparison.OrdinalIgnoreCase) ||
                                name.Contains("k10temp", StringComparison.OrdinalIgnoreCase) ||
                                name.Contains("cpu", StringComparison.OrdinalIgnoreCase);

                    foreach (var input in Directory.GetFiles(hwmon, "temp*_input"))
                    {
                        if (!TryReadMilliCelsius(input, out var celsius))
                            continue;
                        if (isCpu)
                            return celsius;
                        fallback ??= celsius;
                    }
                }
            }

            return fallback;
        }
        catch
        {
            return null;
        }
    }

    private static bool TryReadMilliCelsius(string path, out double celsius)
    {
        celsius = 0;
        try
        {
            var raw = File.ReadAllText(path).Trim();
            if (!long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var milli))
                return false;
            // Some sensors report already in °C (rare); milli-°C is standard.
            celsius = milli > 1000 ? milli / 1000d : milli;
            return celsius is > -40 and < 150;
        }
        catch
        {
            return false;
        }
    }

    [SupportedOSPlatform("windows")]
    private static double? ReadWindowsCpuTemperature()
    {
        try
        {
            // WMI ACPI thermal zone — often unavailable without admin / OEM support.
            using var searcher = new System.Management.ManagementObjectSearcher(
                @"root\WMI",
                "SELECT * FROM MSAcpi_ThermalZoneTemperature");
            foreach (var obj in searcher.Get())
            {
                var kelvinTenths = Convert.ToDouble(obj["CurrentTemperature"], CultureInfo.InvariantCulture);
                var celsius = kelvinTenths / 10d - 273.15d;
                if (celsius is > -40 and < 150)
                    return celsius;
            }
        }
        catch
        {
            // Ignore — temperature is best-effort on Windows.
        }

        try
        {
            using var searcher = new System.Management.ManagementObjectSearcher(
                "SELECT * FROM Win32_TemperatureProbe");
            foreach (var obj in searcher.Get())
            {
                if (obj["CurrentReading"] is null)
                    continue;
                var reading = Convert.ToDouble(obj["CurrentReading"], CultureInfo.InvariantCulture);
                // Tenths of Kelvin per CIM.
                var celsius = reading / 10d - 273.15d;
                if (celsius is > -40 and < 150)
                    return celsius;
            }
        }
        catch
        {
            // Ignore.
        }

        return null;
    }

    private List<DiskMetrics> ReadLinuxDisks()
    {
        var mounts = new List<(string Source, string Mount, string FsType)>();
        try
        {
            foreach (var line in File.ReadLines("/proc/mounts"))
            {
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 3)
                    continue;

                var source = parts[0];
                var mount = UnescapeMount(parts[1]);
                var fsType = parts[2];
                if (IgnoredFsTypes.Contains(fsType))
                    continue;
                if (!source.StartsWith("/dev/", StringComparison.Ordinal))
                    continue;
                if (source.StartsWith("/dev/loop", StringComparison.Ordinal) ||
                    source.StartsWith("/dev/ram", StringComparison.Ordinal) ||
                    source.StartsWith("/dev/fd", StringComparison.Ordinal))
                    continue;

                mounts.Add((source, mount, fsType));
            }
        }
        catch
        {
            return [];
        }

        // Prefer shorter/root mounts; dedupe by mount point.
        mounts = mounts
            .GroupBy(m => m.Mount, StringComparer.Ordinal)
            .Select(g => g.First())
            .OrderBy(m => m.Mount, StringComparer.Ordinal)
            .ToList();

        RefreshDiskExtrasIfNeeded(mounts.Select(m => m.Source).Distinct(StringComparer.Ordinal));

        var disks = new List<DiskMetrics>();
        foreach (var (source, mount, _) in mounts)
        {
            try
            {
                var info = new DriveInfo(mount);
                if (!info.IsReady || info.TotalSize <= 0)
                    continue;

                var total = (double)info.TotalSize;
                var used = total - info.AvailableFreeSpace;
                var pct = Math.Clamp(100d * used / total, 0d, 100d);

                _diskExtrasCache.TryGetValue(source, out var extras);
                disks.Add(new DiskMetrics
                {
                    Device = source,
                    MountPoint = mount,
                    UsedBytes = used,
                    TotalBytes = total,
                    UsagePercent = pct,
                    TemperatureCelsius = extras.Temp,
                    SmartStatus = string.IsNullOrWhiteSpace(extras.Smart) ? "N/D" : extras.Smart
                });
            }
            catch
            {
                // Mount pode sumir entre leitura e DriveInfo.
            }
        }

        return disks;
    }

    private void RefreshDiskExtrasIfNeeded(IEnumerable<string> sources)
    {
        var now = DateTime.UtcNow;
        if (now - _diskExtrasUtc < DiskExtrasTtl && _diskExtrasCache.Count > 0)
            return;

        _diskExtrasCache.Clear();
        foreach (var source in sources)
        {
            var block = ResolveBlockDeviceName(source);
            var temp = ReadBlockTemperature(block);
            var smart = ReadSmartStatus(source, block);
            _diskExtrasCache[source] = (temp, smart);
        }

        _diskExtrasUtc = now;
    }

    private static string UnescapeMount(string mount) =>
        mount.Replace("\\040", " ", StringComparison.Ordinal)
             .Replace("\\011", "\t", StringComparison.Ordinal)
             .Replace("\\012", "\n", StringComparison.Ordinal)
             .Replace("\\134", "\\", StringComparison.Ordinal);

    /// <summary>Ex.: /dev/sda1 → sda ; /dev/nvme0n1p2 → nvme0n1</summary>
    private static string? ResolveBlockDeviceName(string source)
    {
        var name = Path.GetFileName(source);
        if (string.IsNullOrWhiteSpace(name))
            return null;

        if (Directory.Exists(Path.Combine("/sys/block", name)))
            return name;

        var classDir = Path.Combine("/sys/class/block", name);
        if (!Directory.Exists(classDir))
            return name;

        try
        {
            var classInfo = new DirectoryInfo(classDir);
            var resolved = classInfo.ResolveLinkTarget(true) as DirectoryInfo ?? classInfo;
            var parent = resolved.Parent;
            if (parent is not null &&
                parent.Parent is not null &&
                parent.Parent.Name.Equals("block", StringComparison.OrdinalIgnoreCase))
            {
                return parent.Name;
            }

            // Fallback: strip partition suffix.
            if (name.StartsWith("nvme", StringComparison.OrdinalIgnoreCase))
            {
                var p = name.LastIndexOf('p');
                if (p > 0 && p < name.Length - 1 && name[(p + 1)..].All(char.IsDigit))
                    return name[..p];
            }
            else
            {
                var i = name.Length - 1;
                while (i > 0 && char.IsDigit(name[i]))
                    i--;
                if (i < name.Length - 1)
                    return name[..(i + 1)];
            }
        }
        catch
        {
            // ignore
        }

        return name;
    }

    private static double? ReadBlockTemperature(string? blockName)
    {
        if (string.IsNullOrWhiteSpace(blockName))
            return null;

        try
        {
            // /sys/block/sda/device/hwmon/hwmonX/temp1_input  (drivetemp / nvme)
            var deviceDir = Path.Combine("/sys/block", blockName, "device");
            if (!Directory.Exists(deviceDir))
                return null;

            foreach (var hwmon in Directory.GetDirectories(deviceDir, "hwmon*", SearchOption.AllDirectories))
            {
                foreach (var input in Directory.GetFiles(hwmon, "temp*_input"))
                {
                    if (TryReadMilliCelsius(input, out var celsius))
                        return celsius;
                }
            }

            // Alguns NVMe expõem hwmon em /sys/class/nvme/nvme0/device → ../../hwmon
            var nvmeMatch = System.Text.RegularExpressions.Regex.Match(blockName, @"^(nvme\d+)");
            if (nvmeMatch.Success)
            {
                var nvmeDir = Path.Combine("/sys/class/nvme", nvmeMatch.Groups[1].Value);
                if (Directory.Exists(nvmeDir))
                {
                    foreach (var input in Directory.GetFiles(nvmeDir, "temp*_input", SearchOption.AllDirectories))
                    {
                        if (TryReadMilliCelsius(input, out var celsius))
                            return celsius;
                    }
                }
            }
        }
        catch
        {
            // ignore
        }

        return null;
    }

    private static string ReadSmartStatus(string source, string? blockName)
    {
        var fromSysfs = ReadNvmeCriticalWarning(blockName);
        if (fromSysfs is not null)
            return fromSysfs;

        return ReadSmartctlHealth(source, blockName);
    }

    private static string? ReadNvmeCriticalWarning(string? blockName)
    {
        if (string.IsNullOrWhiteSpace(blockName) ||
            !blockName.StartsWith("nvme", StringComparison.OrdinalIgnoreCase))
            return null;

        try
        {
            var match = System.Text.RegularExpressions.Regex.Match(blockName, @"^(nvme\d+)");
            if (!match.Success)
                return null;

            var warnPath = Path.Combine("/sys/class/nvme", match.Groups[1].Value, "device", "critical_warning");
            if (!File.Exists(warnPath))
                warnPath = Path.Combine("/sys/class/nvme", match.Groups[1].Value, "critical_warning");
            if (!File.Exists(warnPath))
                return null;

            var raw = File.ReadAllText(warnPath).Trim();
            if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var warn))
                return null;

            return warn == 0 ? "ok" : $"erro SMART (critical_warning={warn})";
        }
        catch
        {
            return null;
        }
    }

    private static string ReadSmartctlHealth(string source, string? blockName)
    {
        var targets = new List<string> { source };
        if (!string.IsNullOrWhiteSpace(blockName))
        {
            var parent = "/dev/" + blockName;
            if (!targets.Contains(parent, StringComparer.Ordinal))
                targets.Insert(0, parent);
        }

        foreach (var target in targets)
        {
            try
            {
                using var proc = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "smartctl",
                        Arguments = $"-H {target}",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };
                if (!proc.Start())
                    continue;

                if (!proc.WaitForExit(2500))
                {
                    try { proc.Kill(entireProcessTree: true); } catch { /* ignore */ }
                    continue;
                }

                var output = proc.StandardOutput.ReadToEnd() + proc.StandardError.ReadToEnd();
                if (output.Contains("PASSED", StringComparison.OrdinalIgnoreCase) ||
                    (output.Contains("OK", StringComparison.OrdinalIgnoreCase) &&
                     output.Contains("result", StringComparison.OrdinalIgnoreCase)))
                {
                    return "ok";
                }

                if (output.Contains("FAILED", StringComparison.OrdinalIgnoreCase))
                    return "FAILED";

                // Exit 0 often means healthy even without PASSED line.
                if (proc.ExitCode == 0 &&
                    !output.Contains("Permission denied", StringComparison.OrdinalIgnoreCase) &&
                    !output.Contains("Unable to", StringComparison.OrdinalIgnoreCase))
                {
                    return "ok";
                }
            }
            catch
            {
                // smartctl ausente ou sem permissão
            }
        }

        return "N/D";
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        if (_windowsCoreCounters is not null)
        {
            foreach (var c in _windowsCoreCounters)
                c.Dispose();
            _windowsCoreCounters = null;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);
}
