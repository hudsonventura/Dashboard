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
