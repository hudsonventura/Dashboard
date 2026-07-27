using System.Runtime.InteropServices;
using System.Text;
using LibVLCSharp.Shared;

namespace SolisDashboard.App.Native;

/// <summary>
/// Bootstrap LibVLC on Linux/Windows. On Linux, Core.Initialize(path) is NOT supported —
/// use system libvlc + VLC_PLUGIN_PATH / --plugin-path.
/// </summary>
internal static class LibVlcHost
{
    public static string LastDiagnostics { get; private set; } = "";

    public static void Initialize()
    {
        var diag = new StringBuilder();
        diag.AppendLine($"OS: {RuntimeInformation.OSDescription}");
        diag.AppendLine($"Arch: {RuntimeInformation.OSArchitecture}");

        if (OperatingSystem.IsLinux())
        {
            var pluginPath = ResolveLinuxPluginPath(diag);
            if (!string.IsNullOrWhiteSpace(pluginPath))
            {
                Environment.SetEnvironmentVariable("VLC_PLUGIN_PATH", pluginPath);
                diag.AppendLine($"VLC_PLUGIN_PATH={pluginPath}");
            }
            else
            {
                diag.AppendLine("VLC_PLUGIN_PATH: NÃO encontrado");
            }

            AppendLinuxLibCheck(diag);

            // LibVLCSharp docs: libvlcDirectoryPath is NOT supported on Linux.
            Core.Initialize();
            diag.AppendLine("Core.Initialize() sem path (Linux).");
        }
        else
        {
            Core.Initialize();
            diag.AppendLine("Core.Initialize() (Windows NuGet LibVLC).");
        }

        LastDiagnostics = diag.ToString().Trim();
    }

    public static string[] BuildOptions(params string[] extra)
    {
        var list = new List<string>
        {
            "--verbose=2",
            "--network-caching=400",
            "--rtsp-tcp",
            "--no-audio",
            "--aout=dummy"
        };

        if (OperatingSystem.IsLinux())
        {
            var pluginPath = Environment.GetEnvironmentVariable("VLC_PLUGIN_PATH");
            if (string.IsNullOrWhiteSpace(pluginPath))
                pluginPath = ResolveLinuxPluginPath(null);

            if (!string.IsNullOrWhiteSpace(pluginPath))
                list.Add($"--plugin-path={pluginPath}");
        }

        list.AddRange(extra);
        return list.ToArray();
    }

    private static string? ResolveLinuxPluginPath(StringBuilder? diag)
    {
        string[] candidates =
        [
            "/usr/lib/x86_64-linux-gnu/vlc/plugins",
            "/usr/lib/aarch64-linux-gnu/vlc/plugins",
            "/usr/lib/vlc/plugins",
            "/usr/lib64/vlc/plugins",
            "/usr/local/lib/vlc/plugins"
        ];

        foreach (var path in candidates)
        {
            var exists = Directory.Exists(path);
            diag?.AppendLine($"plugin dir {path}: {(exists ? "OK" : "ausente")}");
            if (!exists)
                continue;

            var count = Directory.EnumerateFiles(path, "*.so", SearchOption.AllDirectories).Take(3).Count();
            diag?.AppendLine($"  sample plugins: {count}+");
            return path;
        }

        return null;
    }

    private static void AppendLinuxLibCheck(StringBuilder diag)
    {
        string[] libs =
        [
            "/usr/lib/x86_64-linux-gnu/libvlc.so",
            "/usr/lib/x86_64-linux-gnu/libvlc.so.5",
            "/usr/lib/x86_64-linux-gnu/libvlccore.so",
            "/usr/lib/x86_64-linux-gnu/libvlccore.so.9",
            "/usr/lib/libvlc.so",
            "/usr/lib/libvlc.so.5"
        ];

        foreach (var lib in libs)
            diag.AppendLine($"{lib}: {(File.Exists(lib) ? "OK" : "ausente")}");

        diag.AppendLine($"LD_LIBRARY_PATH={Environment.GetEnvironmentVariable("LD_LIBRARY_PATH") ?? "(vazio)"}");
    }
}
