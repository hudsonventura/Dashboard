using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dashboard.Services;

namespace Dashboard.ViewModels;

public partial class CameraViewModel : ObservableObject, IDisposable
{
    private CameraStreamService? _streamService;

    [ObservableProperty]
    private string _cameraName = "RTSP Camera";

    [ObservableProperty]
    private string _rtspUrl = string.Empty;

    [ObservableProperty]
    private int _targetWidth = 640;

    [ObservableProperty]
    private int _targetHeight = 360;

    [ObservableProperty]
    private double? _viewHeight;

    [ObservableProperty]
    private double? _viewWidth;

    [ObservableProperty]
    private WriteableBitmap? _currentFrame;

    [ObservableProperty]
    private string _statusText = "Inactive";

    [ObservableProperty]
    private string _statusColor = "#6B7280";

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private string _fpsText = "0 FPS";

    [ObservableProperty]
    private bool _isLive;

    [ObservableProperty]
    private bool _isPlaying = true;

    [ObservableProperty]
    private bool _isConnecting = true;

    [ObservableProperty]
    private bool _isExpanded;

    public event Action<CameraViewModel>? OnToggleExpandRequested;

    public CameraViewModel(string name, string rtspUrl, int targetHeight = 360)
    {
        _cameraName = name;
        _rtspUrl = rtspUrl;

        _targetHeight = targetHeight > 0 ? targetHeight : 360;
        if (_targetHeight % 2 != 0) _targetHeight++;

        _targetWidth = Math.Max(320, (int)(_targetHeight * 16.0 / 9.0));
        if (_targetWidth % 2 != 0) _targetWidth++;

        _viewHeight = _targetHeight;
        _viewWidth = _targetWidth;
    }

    public void UpdateViewHeight(bool isDualView)
    {
        if (!isDualView)
        {
            ViewHeight = null;
            ViewWidth = null;
            return;
        }

        ViewHeight = TargetHeight;
        UpdateViewWidthFromAspect(TargetWidth, TargetHeight);
    }

    public void StartStream()
    {
        if (string.IsNullOrWhiteSpace(RtspUrl))
        {
            StatusText = "No RTSP URL";
            StatusColor = "#EF4444";
            IsConnecting = false;
            IsLive = false;
            return;
        }

        StopStreamInternal();

        _streamService = new CameraStreamService(RtspUrl, TargetWidth, TargetHeight);
        _streamService.OnFrameReceived += HandleFrameReceived;
        _streamService.OnStatusChanged += HandleStatusChanged;
        _streamService.OnFpsUpdated += HandleFpsUpdated;

        IsPlaying = true;
        _streamService.Start();
    }

    private void HandleFrameReceived(WriteableBitmap bitmap)
    {
        CurrentFrame = bitmap;
        UpdateViewWidthFromAspect(bitmap.PixelSize.Width, bitmap.PixelSize.Height);
    }

    private void UpdateViewWidthFromAspect(int width, int height)
    {
        if (ViewHeight is null || width <= 0 || height <= 0)
            return;

        ViewWidth = Math.Round(ViewHeight.Value * width / (double)height, 2);
    }

    private void HandleStatusChanged(StreamStatus status, string error)
    {
        Dispatcher.UIThread.Post(() =>
        {
            ErrorMessage = error;

            switch (status)
            {
                case StreamStatus.Streaming:
                    StatusText = "LIVE";
                    StatusColor = "#10B981"; // Emerald green
                    IsLive = true;
                    IsConnecting = false;
                    break;
                case StreamStatus.Connecting:
                    StatusText = "CONNECTING...";
                    StatusColor = "#F59E0B"; // Amber
                    IsLive = false;
                    IsConnecting = true;
                    break;
                case StreamStatus.Reconnecting:
                    StatusText = "RECONNECTING...";
                    StatusColor = "#F97316"; // Orange
                    IsLive = false;
                    IsConnecting = true;
                    break;
                case StreamStatus.Error:
                    StatusText = "ERROR";
                    StatusColor = "#EF4444"; // Red
                    IsLive = false;
                    IsConnecting = false;
                    break;
                case StreamStatus.Stopped:
                default:
                    StatusText = "PAUSED";
                    StatusColor = "#6B7280"; // Gray
                    IsLive = false;
                    IsConnecting = false;
                    break;
            }
        });
    }

    private void HandleFpsUpdated(double fps)
    {
        Dispatcher.UIThread.Post(() =>
        {
            FpsText = $"{fps:F0} FPS";
        });
    }

    [RelayCommand]
    public async Task TogglePlayAsync()
    {
        if (IsPlaying)
        {
            IsPlaying = false;
            StopStreamInternal();
            StatusText = "PAUSED";
            StatusColor = "#6B7280";
            IsLive = false;
            IsConnecting = false;
        }
        else
        {
            StartStream();
        }
    }

    [RelayCommand]
    public void Reconnect()
    {
        StartStream();
    }

    [RelayCommand]
    public void ToggleExpand()
    {
        IsExpanded = !IsExpanded;
        OnToggleExpandRequested?.Invoke(this);
    }

    [RelayCommand]
    public async Task SaveSnapshotAsync()
    {
        if (CurrentFrame == null) return;

        try
        {
            string fileName = $"snapshot_{SanitizeFileName(CameraName)}_{DateTime.Now:yyyyMMdd_HHmmss}.png";
            string filePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), fileName);
            
            using (var stream = File.Create(filePath))
            {
#pragma warning disable CS0618
                CurrentFrame.Save(stream);
#pragma warning restore CS0618
            }
            StatusText = "SNAPSHOT SAVED!";
            
            await Task.Delay(2000);
            if (IsLive) StatusText = "LIVE";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to save snapshot: {ex.Message}";
        }
    }

    private string SanitizeFileName(string name)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(c, '_');
        }
        return name.Replace(' ', '_');
    }

    private void StopStreamInternal()
    {
        if (_streamService != null)
        {
            _streamService.OnFrameReceived -= HandleFrameReceived;
            _streamService.OnStatusChanged -= HandleStatusChanged;
            _streamService.OnFpsUpdated -= HandleFpsUpdated;
            _streamService.Dispose();
            _streamService = null;
        }
    }

    public void Dispose()
    {
        StopStreamInternal();
    }
}
