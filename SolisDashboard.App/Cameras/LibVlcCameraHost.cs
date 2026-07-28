using System.Runtime.InteropServices;
using LibVLCSharp.Shared;

namespace SolisDashboard.App.Cameras;

/// <summary>
/// Host LibVLC para duas câmeras RTSP (sem decode JPEG na UI).
/// Linux: libvlc do sistema / snap (LD_LIBRARY_PATH). Windows: NuGet VideoLAN.LibVLC.Windows.
/// </summary>
public sealed class LibVlcCameraHost : IDisposable
{
    private LibVLC? _libVlc;
    private Media? _primaryMedia;
    private Media? _secondaryMedia;
    private bool _disposed;

    public MediaPlayer? PrimaryPlayer { get; private set; }
    public MediaPlayer? SecondaryPlayer { get; private set; }

    public event Action<string>? ErrorOccurred;

    public void Start(string? primaryRtspUrl, string? secondaryRtspUrl)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Stop();

        Core.Initialize();
        _libVlc = new LibVLC(
            enableDebugLogs: false,
            "--rtsp-tcp",
            "--network-caching=400",
            "--live-caching=400",
            "--sout-mux-caching=400",
            "--no-audio",
            "--no-video-title-show",
            "--quiet");

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
        catch { /* libX11 ausente — LibVLC pode falhar depois */ }
    }
}
