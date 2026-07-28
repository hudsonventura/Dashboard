using System.Runtime.InteropServices;
using LibVLCSharp.Shared;

namespace SolisDashboard.App.Cameras;

/// <summary>
/// Host LibVLC para duas câmeras RTSP.
/// Windows: NuGet VideoLAN.LibVLC.Windows.
/// Linux: procura libvlc em snap (arch dir) ou /usr — sem LD_LIBRARY_PATH global quebrado.
/// </summary>
public sealed class LibVlcCameraHost : IDisposable
{
    private LibVLC? _libVlc;
    private Media? _primaryMedia;
    private Media? _secondaryMedia;
    private bool _disposed;

    public MediaPlayer? PrimaryPlayer { get; private set; }
    public MediaPlayer? SecondaryPlayer { get; private set; }
    public string? ResolvedLibVlcDirectory { get; private set; }

    public event Action<string>? ErrorOccurred;

    public void Start(string? primaryRtspUrl, string? secondaryRtspUrl, string? configuredLibVlcPath = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Stop();

        var libDir = ResolveLibVlcDirectory(configuredLibVlcPath);
        ResolvedLibVlcDirectory = libDir;

        if (OperatingSystem.IsWindows())
        {
            Core.Initialize();
        }
        else
        {
            if (libDir is null)
            {
                throw new InvalidOperationException(
                    "libvlc.so não encontrado. No Xubuntu 24.04 use o snap e configure Cameras:LibVlcPath, ex.:\n" +
                    "  /snap/vlc/current/usr/lib/x86_64-linux-gnu\n" +
                    "Comando para achar: find /snap/vlc/current -name 'libvlc.so*' 2>/dev/null");
            }

            Core.Initialize(libDir);
        }

        var options = new List<string>
        {
            "--rtsp-tcp",
            "--network-caching=400",
            "--live-caching=400",
            "--sout-mux-caching=400",
            "--no-audio",
            "--no-video-title-show",
            "--quiet"
        };

        var pluginPath = FindPluginPath(libDir);
        if (pluginPath is not null)
            options.Add($"--plugin-path={pluginPath}");

        _libVlc = new LibVLC(enableDebugLogs: false, options.ToArray());

        if (!string.IsNullOrWhiteSpace(primaryRtspUrl))
            (PrimaryPlayer, _primaryMedia) = CreateAndPlay(primaryRtspUrl.Trim(), "Câmera principal");

        if (!string.IsNullOrWhiteSpace(secondaryRtspUrl))
            (SecondaryPlayer, _secondaryMedia) = CreateAndPlay(secondaryRtspUrl.Trim(), "Câmera secundária");
    }

    private (MediaPlayer Player, Media Media) CreateAndPlay(string rtspUrl, string label)
    {
        var player = new MediaPlayer(_libVlc!);
        player.EncounteredError += (_, _) =>
            ErrorOccurred?.Invoke($"{label}: erro LibVLC ao reproduzir RTSP.");

        var media = new Media(_libVlc!, rtspUrl, FromType.FromLocation);
        media.AddOption(":rtsp-tcp");
        media.AddOption(":network-caching=400");
        media.AddOption(":no-audio");

        if (!player.Play(media))
            ErrorOccurred?.Invoke($"{label}: Play() retornou false. Verifique URL RTSP / live555.");

        return (player, media);
    }

    public void Stop()
    {
        StopPlayer(PrimaryPlayer);
        PrimaryPlayer = null;
        StopPlayer(SecondaryPlayer);
        SecondaryPlayer = null;

        _primaryMedia?.Dispose();
        _primaryMedia = null;
        _secondaryMedia?.Dispose();
        _secondaryMedia = null;

        _libVlc?.Dispose();
        _libVlc = null;
    }

    private static void StopPlayer(MediaPlayer? player)
    {
        if (player is null)
            return;
        try { player.Stop(); } catch { /* ignore */ }
        player.Dispose();
    }

    /// <summary>
    /// Não use LD_LIBRARY_PATH=/snap/vlc/current/usr/lib — isso quebra libidn e outras libs do sistema.
    /// Use o diretório arch-specific que contém libvlc.so.
    /// </summary>
    public static string? ResolveLibVlcDirectory(string? configuredPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath) && Directory.Exists(configuredPath))
        {
            if (HasLibVlc(configuredPath))
                return configuredPath;
        }

        var env = Environment.GetEnvironmentVariable("LIBVLC_PATH");
        if (!string.IsNullOrWhiteSpace(env) && HasLibVlc(env))
            return env;

        foreach (var candidate in EnumerateCandidateDirs())
        {
            if (HasLibVlc(candidate))
                return candidate;
        }

        return null;
    }

    private static IEnumerable<string> EnumerateCandidateDirs()
    {
        var arch = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "x86_64-linux-gnu",
            Architecture.Arm64 => "aarch64-linux-gnu",
            Architecture.Arm => "arm-linux-gnueabihf",
            _ => "x86_64-linux-gnu"
        };

        yield return $"/snap/vlc/current/usr/lib/{arch}";
        yield return "/snap/vlc/current/usr/lib";
        yield return $"/usr/lib/{arch}";
        yield return "/usr/lib";
        yield return "/usr/local/lib";
        yield return $"/usr/local/lib/{arch}";
    }

    private static bool HasLibVlc(string dir)
    {
        try
        {
            return File.Exists(Path.Combine(dir, "libvlc.so")) ||
                   File.Exists(Path.Combine(dir, "libvlc.so.5")) ||
                   Directory.GetFiles(dir, "libvlc.so*").Length > 0;
        }
        catch
        {
            return false;
        }
    }

    private static string? FindPluginPath(string? libDir)
    {
        if (libDir is null)
            return null;

        var candidates = new[]
        {
            Path.Combine(libDir, "vlc", "plugins"),
            Path.Combine(libDir, "vlc"),
            "/snap/vlc/current/usr/lib/x86_64-linux-gnu/vlc/plugins",
            "/snap/vlc/current/usr/lib/aarch64-linux-gnu/vlc/plugins",
            "/usr/lib/x86_64-linux-gnu/vlc/plugins",
            "/usr/lib/vlc/plugins"
        };

        return candidates.FirstOrDefault(Directory.Exists);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        Stop();
    }
}

/// <summary>X11 multithread init exigido pelo LibVLC/Avalonia no Linux.</summary>
internal static class X11Threads
{
    [DllImport("libX11", CallingConvention = CallingConvention.Cdecl)]
    private static extern int XInitThreads();

    public static void EnsureInitialized()
    {
        if (!OperatingSystem.IsLinux())
            return;
        try { _ = XInitThreads(); }
        catch { /* ignore */ }
    }
}
