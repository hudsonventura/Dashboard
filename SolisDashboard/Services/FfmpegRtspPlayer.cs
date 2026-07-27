using System.Diagnostics;
using System.Text;

namespace SolisDashboard.Services;

/// <summary>
/// Captura frames RTSP via processo FFmpeg (MJPEG em stdout).
/// Funciona no Ubuntu sem Live555 (diferente do LibVLC do apt).
/// </summary>
public sealed class FfmpegRtspPlayer : IDisposable
{
    private readonly object _gate = new();
    private Process? _process;
    private CancellationTokenSource? _cts;
    private bool _disposed;

    public string Name { get; }
    public string RtspUrl { get; private set; } = "";
    public bool IsRunning => _process is { HasExited: false };

    public event Action<byte[]>? FrameReceived;
    public event Action<string>? ErrorOccurred;

    public FfmpegRtspPlayer(string name) => Name = name;

    public void Start(string rtspUrl, string ffmpegPath, double fps, int maxWidth)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Stop();

        if (string.IsNullOrWhiteSpace(rtspUrl))
            throw new ArgumentException("RtspUrl vazia.", nameof(rtspUrl));

        RtspUrl = rtspUrl.Trim();
        fps = Math.Clamp(fps, 1, 30);
        maxWidth = Math.Clamp(maxWidth, 160, 1920);

        _cts = new CancellationTokenSource();
        var psi = new ProcessStartInfo
        {
            FileName = string.IsNullOrWhiteSpace(ffmpegPath) ? "ffmpeg" : ffmpegPath.Trim(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardErrorEncoding = Encoding.UTF8
        };

        foreach (var arg in BuildArgs(RtspUrl, fps, maxWidth))
            psi.ArgumentList.Add(arg);

        var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        process.Exited += (_, _) =>
        {
            if (_disposed || _cts is null || _cts.IsCancellationRequested)
                return;
            ErrorOccurred?.Invoke($"{Name}: ffmpeg encerrou (exit={SafeExitCode(process)}). Verifique a URL RTSP / rede.");
        };

        if (!process.Start())
            throw new InvalidOperationException($"Não foi possível iniciar o ffmpeg ({psi.FileName}). Está no PATH?");

        _process = process;
        var token = _cts.Token;
        _ = Task.Run(() => ReadMjpegLoop(process.StandardOutput.BaseStream, token), token);
        _ = Task.Run(() => ReadStderrLoop(process.StandardError, token), token);
    }

    private static IEnumerable<string> BuildArgs(string rtspUrl, double fps, int maxWidth)
    {
        yield return "-hide_banner";
        yield return "-loglevel";
        yield return "warning";
        yield return "-rtsp_transport";
        yield return "tcp";
        yield return "-i";
        yield return rtspUrl;
        yield return "-an";
        yield return "-vf";
        yield return $"fps={fps:0.##},scale='min({maxWidth},iw)':-2";
        yield return "-f";
        yield return "mjpeg";
        yield return "-q:v";
        yield return "6";
        yield return "pipe:1";
    }

    private void ReadMjpegLoop(Stream stream, CancellationToken ct)
    {
        var buffer = new byte[64 * 1024];
        var frame = new List<byte>(128 * 1024);
        var prev = (byte)0;
        var inFrame = false;

        try
        {
            while (!ct.IsCancellationRequested)
            {
                var read = stream.Read(buffer, 0, buffer.Length);
                if (read <= 0)
                    break;

                for (var i = 0; i < read; i++)
                {
                    var b = buffer[i];

                    if (!inFrame)
                    {
                        if (prev == 0xFF && b == 0xD8)
                        {
                            inFrame = true;
                            frame.Clear();
                            frame.Add(0xFF);
                            frame.Add(0xD8);
                        }

                        prev = b;
                        continue;
                    }

                    frame.Add(b);
                    if (prev == 0xFF && b == 0xD9 && frame.Count >= 4)
                    {
                        FrameReceived?.Invoke(frame.ToArray());
                        inFrame = false;
                        frame.Clear();
                        prev = 0;
                        continue;
                    }

                    if (frame.Count > 2 * 1024 * 1024)
                    {
                        inFrame = false;
                        frame.Clear();
                        prev = 0;
                        continue;
                    }

                    prev = b;
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (!ct.IsCancellationRequested)
                ErrorOccurred?.Invoke($"{Name}: leitura MJPEG: {ex.Message}");
        }
    }

    private void ReadStderrLoop(StreamReader reader, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var line = reader.ReadLine();
                if (line is null)
                    break;

                if (line.Contains("error", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("failed", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("401", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("404", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("Connection refused", StringComparison.OrdinalIgnoreCase))
                {
                    ErrorOccurred?.Invoke($"{Name}: {line.Trim()}");
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (!ct.IsCancellationRequested)
                ErrorOccurred?.Invoke($"{Name}: stderr: {ex.Message}");
        }
    }

    private static int SafeExitCode(Process process)
    {
        try { return process.ExitCode; }
        catch { return -1; }
    }

    public void Stop()
    {
        lock (_gate)
        {
            try { _cts?.Cancel(); } catch { /* ignore */ }

            if (_process is not null)
            {
                try
                {
                    if (!_process.HasExited)
                    {
                        _process.Kill(entireProcessTree: true);
                        _process.WaitForExit(2000);
                    }
                }
                catch { /* ignore */ }

                try { _process.Dispose(); } catch { /* ignore */ }
                _process = null;
            }

            try { _cts?.Dispose(); } catch { /* ignore */ }
            _cts = null;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        Stop();
    }
}
