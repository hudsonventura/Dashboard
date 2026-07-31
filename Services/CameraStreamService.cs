using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;

namespace Dashboard.Services;

public enum StreamStatus
{
    Stopped,
    Connecting,
    Streaming,
    Reconnecting,
    Error
}

public class CameraStreamService : IDisposable
{
    private readonly string _rtspUrl;
    private readonly int _width;
    private readonly int _height;

    private Process? _ffmpegProcess;
    private CancellationTokenSource? _cts;
    private Task? _streamTask;

    private StreamStatus _status = StreamStatus.Stopped;
    private string _errorMessage = string.Empty;
    private double _currentFps;

    public event Action<WriteableBitmap>? OnFrameReceived;
    public event Action<StreamStatus, string>? OnStatusChanged;
    public event Action<double>? OnFpsUpdated;

    public StreamStatus Status
    {
        get => _status;
        private set
        {
            if (_status != value)
            {
                _status = value;
                OnStatusChanged?.Invoke(_status, _errorMessage);
            }
        }
    }

    public string ErrorMessage => _errorMessage;

    public CameraStreamService(string rtspUrl, int width = 640, int height = 360)
    {
        _rtspUrl = rtspUrl;
        _width = width;
        _height = height;
    }

    public void Start()
    {
        if (_streamTask != null && !_streamTask.IsCompleted)
            return;

        if (string.IsNullOrWhiteSpace(_rtspUrl))
        {
            _errorMessage = "URL RTSP não configurada";
            Status = StreamStatus.Error;
            return;
        }

        _cts = new CancellationTokenSource();
        _streamTask = Task.Run(() => StreamLoopAsync(_cts.Token));
    }

    public async Task StopAsync()
    {
        if (_cts != null)
        {
            _cts.Cancel();
            _cts.Dispose();
            _cts = null;
        }

        KillProcess();

        if (_streamTask != null)
        {
            try
            {
                await _streamTask;
            }
            catch (OperationCanceledException) { }
            _streamTask = null;
        }

        Status = StreamStatus.Stopped;
    }

    private async Task StreamLoopAsync(CancellationToken ct)
    {
        int frameSize = _width * _height * 4; // BGRA = 4 bytes per pixel
        byte[] frameBuffer = new byte[frameSize];

        WriteableBitmap[] bitmaps = new WriteableBitmap[2];
        int activeBufferIndex = 0;

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var pixelSize = new PixelSize(_width, _height);
            var dpi = new Vector(96, 96);
            bitmaps[0] = new WriteableBitmap(pixelSize, dpi, PixelFormat.Bgra8888, AlphaFormat.Premul);
            bitmaps[1] = new WriteableBitmap(pixelSize, dpi, PixelFormat.Bgra8888, AlphaFormat.Premul);
        });

        int reconnectAttempts = 0;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                Status = reconnectAttempts == 0 ? StreamStatus.Connecting : StreamStatus.Reconnecting;
                _errorMessage = string.Empty;

                var psi = new ProcessStartInfo
                {
                    FileName = "ffmpeg",
                    Arguments = $"-loglevel warning -rtsp_transport tcp -fflags nobuffer -flags low_delay -i \"{_rtspUrl}\" -an -vf scale={_width}:{_height} -f rawvideo -pix_fmt bgra -",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                _ffmpegProcess = Process.Start(psi);

                if (_ffmpegProcess == null)
                {
                    _errorMessage = "Não foi possível iniciar o processo FFmpeg.";
                    Status = StreamStatus.Error;
                    await Task.Delay(3000, ct);
                    reconnectAttempts++;
                    continue;
                }

                using Stream stdout = _ffmpegProcess.StandardOutput.BaseStream;
                
                int frameCount = 0;
                var stopwatch = Stopwatch.StartNew();
                bool hasReceivedFirstFrame = false;

                while (!ct.IsCancellationRequested && !_ffmpegProcess.HasExited)
                {
                    int bytesRead = 0;
                    while (bytesRead < frameSize && !ct.IsCancellationRequested)
                    {
                        int read = await stdout.ReadAsync(frameBuffer.AsMemory(bytesRead, frameSize - bytesRead), ct);
                        if (read == 0) break; // End of stream
                        bytesRead += read;
                    }

                    if (bytesRead < frameSize)
                    {
                        break;
                    }

                    if (!hasReceivedFirstFrame)
                    {
                        hasReceivedFirstFrame = true;
                        reconnectAttempts = 0;
                        Status = StreamStatus.Streaming;
                    }

                    int nextIndex = 1 - activeBufferIndex;
                    var targetBitmap = bitmaps[nextIndex];

                    if (targetBitmap != null)
                    {
                        byte[] localBuffer = (byte[])frameBuffer.Clone();
                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            using (var lockedBuffer = targetBitmap.Lock())
                            {
                                Marshal.Copy(localBuffer, 0, lockedBuffer.Address, localBuffer.Length);
                            }
                            OnFrameReceived?.Invoke(targetBitmap);
                        }, DispatcherPriority.Render);

                        activeBufferIndex = nextIndex;
                    }

                    frameCount++;
                    if (stopwatch.ElapsedMilliseconds >= 1000)
                    {
                        _currentFps = frameCount * 1000.0 / stopwatch.ElapsedMilliseconds;
                        OnFpsUpdated?.Invoke(_currentFps);
                        frameCount = 0;
                        stopwatch.Restart();
                    }
                }

                KillProcess();

                if (!ct.IsCancellationRequested)
                {
                    reconnectAttempts++;
                    _errorMessage = "Conexão perdida com a câmera RTSP.";
                    Status = StreamStatus.Reconnecting;
                    await Task.Delay(2000, ct);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                KillProcess();
                reconnectAttempts++;
                _errorMessage = $"Erro no streaming: {ex.Message}";
                Status = StreamStatus.Error;

                if (!ct.IsCancellationRequested)
                {
                    await Task.Delay(3000, ct);
                }
            }
        }
    }

    private void KillProcess()
    {
        if (_ffmpegProcess != null)
        {
            try
            {
                if (!_ffmpegProcess.HasExited)
                {
                    _ffmpegProcess.Kill(true);
                }
            }
            catch { }
            finally
            {
                _ffmpegProcess.Dispose();
                _ffmpegProcess = null;
            }
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        KillProcess();
    }
}
