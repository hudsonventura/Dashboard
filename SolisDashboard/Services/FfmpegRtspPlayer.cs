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
    /// <summary>Último JPEG; trocado atomicamente. Consumidor descarta o anterior.</summary>
    private byte[]? _latestJpeg;
    private int _frameNotifyScheduled;

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
        // No Linux, FPS alto + decode Skia estoura RAM; limita na origem.
        fps = Math.Clamp(fps, 1, 12);
        maxWidth = Math.Clamp(maxWidth, 160, 1280);

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
        var minFrameGap = TimeSpan.FromSeconds(1d / fps);
        _ = Task.Run(() => ReadMjpegLoop(process.StandardOutput.BaseStream, minFrameGap, token), token);
        _ = Task.Run(() => ReadStderrLoop(process.StandardError, token), token);
    }

    private static IEnumerable<string> BuildArgs(string rtspUrl, double fps, int maxWidth)
    {
        yield return "-hide_banner";
        yield return "-loglevel";
        yield return "error";
        yield return "-nostdin";
        yield return "-rtsp_transport";
        yield return "tcp";
        yield return "-fflags";
        yield return "nobuffer";
        yield return "-flags";
        yield return "low_delay";
        yield return "-threads";
        yield return "1";
        yield return "-i";
        yield return rtspUrl;
        yield return "-an";
        yield return "-vf";
        yield return $"fps={fps:0.##},scale='min({maxWidth},iw)':-2";
        yield return "-f";
        yield return "mjpeg";
        yield return "-q:v";
        yield return "7";
        yield return "pipe:1";
    }

    private void ReadMjpegLoop(Stream stream, TimeSpan minFrameGap, CancellationToken ct)
    {
        var buffer = new byte[64 * 1024];
        // Buffer reutilizável do frame atual (evita List<byte> + ToArray a cada frame sem necessidade).
        var frameBuffer = new byte[512 * 1024];
        var frameLength = 0;
        var prev = (byte)0;
        var inFrame = false;
        var lastEmitted = Stopwatch.StartNew();

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
                            frameLength = 0;
                            EnsureCapacity(ref frameBuffer, 2);
                            frameBuffer[0] = 0xFF;
                            frameBuffer[1] = 0xD8;
                            frameLength = 2;
                        }

                        prev = b;
                        continue;
                    }

                    EnsureCapacity(ref frameBuffer, frameLength + 1);
                    frameBuffer[frameLength++] = b;

                    if (prev == 0xFF && b == 0xD9 && frameLength >= 4)
                    {
                        // Backpressure: só emite se passou o intervalo E o consumidor já pegou o anterior
                        // (ou substitui o latest sem notificar em excesso).
                        if (lastEmitted.Elapsed >= minFrameGap)
                        {
                            var copy = new byte[frameLength];
                            Buffer.BlockCopy(frameBuffer, 0, copy, 0, frameLength);
                            var dropped = Interlocked.Exchange(ref _latestJpeg, copy);
                            // JPEG substituído e nunca decodificado → elegível ao GC imediatamente.
                            _ = dropped;
                            lastEmitted.Restart();
                            NotifyFrameIfNeeded();
                        }

                        inFrame = false;
                        frameLength = 0;
                        prev = 0;
                        continue;
                    }

                    if (frameLength > 2 * 1024 * 1024)
                    {
                        inFrame = false;
                        frameLength = 0;
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

    private void NotifyFrameIfNeeded()
    {
        if (Interlocked.CompareExchange(ref _frameNotifyScheduled, 1, 0) != 0)
            return;

        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                var jpeg = Interlocked.Exchange(ref _latestJpeg, null);
                if (jpeg is { Length: > 0 })
                    FrameReceived?.Invoke(jpeg);
            }
            finally
            {
                Interlocked.Exchange(ref _frameNotifyScheduled, 0);
                // Se chegou frame novo enquanto notificávamos, agenda de novo.
                if (Volatile.Read(ref _latestJpeg) is not null)
                    NotifyFrameIfNeeded();
            }
        });
    }

    private static void EnsureCapacity(ref byte[] buffer, int needed)
    {
        if (needed <= buffer.Length)
            return;
        var newSize = Math.Max(needed, buffer.Length * 2);
        Array.Resize(ref buffer, newSize);
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
            Interlocked.Exchange(ref _latestJpeg, null);
            Interlocked.Exchange(ref _frameNotifyScheduled, 0);
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
