using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using Microsoft.Extensions.Configuration;
using SkiaSharp;
using SolisDashboard.App.Diagnostics;
using SolisDashboard.App.Models;
using SolisDashboard.Configuration;
using SolisDashboard.Display;
using SolisDashboard.Models;
using SolisDashboard.Services;

namespace SolisDashboard.App.ViewModels;

public partial class MainViewModel : ViewModelBase, IDisposable
{
    private static readonly SKColor AccentColor = SKColor.Parse("#F5B942");
    private static readonly SKColor AxisTextColor = SKColor.Parse("#9AA6B5");
    private static readonly SKColor AxisLineColor = SKColor.Parse("#2A3442");

    private readonly AppOptions _options;
    private readonly DispatcherTimer _refreshTimer;
    private readonly DispatcherTimer _countdownTimer;
    private readonly DispatcherTimer _systemTimer;
    private readonly ObservableCollection<double> _generationValues = [];
    private readonly SystemMetricsService _systemMetrics = new();
    private readonly FfmpegRtspPlayer _primaryPlayer = new("Câmera principal");
    private readonly FfmpegRtspPlayer _secondaryPlayer = new("Câmera secundária");
    private SolisApiClient? _solis;
    private OpenMeteoClient? _weather;
    private string? _inverterSn;
    private DateTime _nextRefreshUtc = DateTime.UtcNow;
    private bool _disposed;
    private Bitmap? _primaryCameraFrameField;
    private Bitmap? _secondaryCameraFrameField;
    private byte[]? _pendingPrimaryJpeg;
    private byte[]? _pendingSecondaryJpeg;
    private int _primaryUiScheduled;
    private int _secondaryUiScheduled;
    private string? _lastCameraErrorKey;
    private DateTime _lastCameraErrorReportedUtc = DateTime.MinValue;

    public MainViewModel()
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .AddEnvironmentVariables(prefix: "SOLIS_")
            .Build();

        _options = configuration.Get<AppOptions>()
                   ?? throw new InvalidOperationException("Não foi possível carregar appsettings.json.");

        ForecastDays = [];
        CpuCores = [];
        CamerasVisible = _options.Cameras.Enabled;
        PrimaryCameraName = string.IsNullOrWhiteSpace(_options.Cameras.Primary.Name) ? "Câmera 1" : _options.Cameras.Primary.Name;
        SecondaryCameraName = string.IsNullOrWhiteSpace(_options.Cameras.Secondary.Name) ? "Câmera 2" : _options.Cameras.Secondary.Name;
        PrimaryCameraHeight = Math.Max(80, _options.Cameras.Primary.Height);
        SecondaryCameraHeight = Math.Max(80, _options.Cameras.Secondary.Height);

        var hasPrimary = !string.IsNullOrWhiteSpace(_options.Cameras.Primary.RtspUrl);
        var hasSecondary = !string.IsNullOrWhiteSpace(_options.Cameras.Secondary.RtspUrl);
        UseRtspCameras = _options.Cameras.Enabled && (hasPrimary || hasSecondary);

        ChartSeries =
        [
            new LineSeries<double>
            {
                Name = "Potência (W)",
                Values = _generationValues,
                Fill = new SolidColorPaint(AccentColor.WithAlpha(70)),
                Stroke = new SolidColorPaint(AccentColor, 3),
                GeometrySize = 4,
                GeometryStroke = new SolidColorPaint(AccentColor, 2),
                GeometryFill = new SolidColorPaint(AccentColor),
                LineSmoothness = 0.35
            }
        ];

        XAxes =
        [
            new Axis
            {
                Name = "Horário",
                Labels = [],
                LabelsPaint = new SolidColorPaint(AxisTextColor),
                NamePaint = new SolidColorPaint(AxisTextColor),
                SeparatorsPaint = new SolidColorPaint(AxisLineColor),
                LabelsRotation = 15,
                TextSize = 10
            }
        ];

        YAxes =
        [
            new Axis
            {
                Name = "W",
                Labeler = value => $"{value:0}",
                LabelsPaint = new SolidColorPaint(AxisTextColor),
                NamePaint = new SolidColorPaint(AxisTextColor),
                SeparatorsPaint = new SolidColorPaint(AxisLineColor),
                TextSize = 10
            }
        ];

        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(5) };
        _refreshTimer.Tick += async (_, _) => await RefreshAsync();

        _countdownTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _countdownTimer.Tick += (_, _) => UpdateCountdown();

        _systemTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _systemTimer.Tick += (_, _) => RefreshSystemMetrics();

        _primaryPlayer.FrameReceived += jpeg => OnFrame(isPrimary: true, jpeg);
        _secondaryPlayer.FrameReceived += jpeg => OnFrame(isPrimary: false, jpeg);
        _primaryPlayer.ErrorOccurred += OnCameraPlayerError;
        _secondaryPlayer.ErrorOccurred += OnCameraPlayerError;

        LogFilePath = AppLog.LogFilePath;
        _ = InitializeAsync();
    }

    public ObservableCollection<ForecastDayItem> ForecastDays { get; }
    public ObservableCollection<CpuCoreItem> CpuCores { get; }
    public ISeries[] ChartSeries { get; }
    public Axis[] XAxes { get; }
    public Axis[] YAxes { get; }

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusMessage = "Iniciando...";
    [ObservableProperty] private string _errorMessage = "";
    [ObservableProperty] private string _diagnosticLog = "";
    [ObservableProperty] private bool _hasDiagnosticLog;
    [ObservableProperty] private bool _isDiagnosticPanelOpen;
    [ObservableProperty] private string _logFilePath = "";
    [ObservableProperty] private string _copyDiagnosticStatus = "";
    [ObservableProperty] private string _inverterSnText = "-";
    [ObservableProperty] private string _modelText = "S6-GR1P6K-S";
    [ObservableProperty] private string _updatedText = "-";
    [ObservableProperty] private string _stateText = "-";
    [ObservableProperty] private string _energyTodayText = "-";
    [ObservableProperty] private string _energyTotalText = "-";
    [ObservableProperty] private string _countdownText = "";

    [ObservableProperty] private string _pv1Voltage = "-";
    [ObservableProperty] private string _pv1Current = "-";
    [ObservableProperty] private string _pv1Power = "-";
    [ObservableProperty] private string _pv2Voltage = "-";
    [ObservableProperty] private string _pv2Current = "-";
    [ObservableProperty] private string _pv2Power = "-";

    [ObservableProperty] private string _acPower = "-";
    [ObservableProperty] private string _acCurrent = "-";
    [ObservableProperty] private string _igbtTemp = "-";
    [ObservableProperty] private string _inverterTemp = "-";

    [ObservableProperty] private string _weatherTemp = "-";
    [ObservableProperty] private string _weatherFeelsLike = "-";
    [ObservableProperty] private string _weatherCondition = "-";
    [ObservableProperty] private string _weatherHumidity = "-";
    [ObservableProperty] private string _weatherWind = "-";
    [ObservableProperty] private string _weatherPrecip = "-";
    [ObservableProperty] private string _weatherIconKey = "unknown";
    [ObservableProperty] private string _weatherTime = "-";
    [ObservableProperty] private string _chartPeakText = "";

    [ObservableProperty] private bool _camerasVisible;
    [ObservableProperty] private bool _useRtspCameras;
    [ObservableProperty] private string _primaryCameraName = "Câmera 1";
    [ObservableProperty] private string _secondaryCameraName = "Câmera 2";
    [ObservableProperty] private double _primaryCameraHeight = 360;
    [ObservableProperty] private double _secondaryCameraHeight = 180;
    [ObservableProperty] private Bitmap? _primaryCameraFrame;
    [ObservableProperty] private Bitmap? _secondaryCameraFrame;
    [ObservableProperty] private string _cameraStatus = "";

    [ObservableProperty] private string _systemPlatform = "-";
    [ObservableProperty] private string _memoryText = "-";
    [ObservableProperty] private double _memoryPercent;
    [ObservableProperty] private string _swapText = "-";
    [ObservableProperty] private double _swapPercent;
    [ObservableProperty] private string _cpuTempText = "N/D";

    private async Task InitializeAsync()
    {
        try
        {
            ValidateOptions();
            _solis = new SolisApiClient(_options.Solis);
            _weather = new OpenMeteoClient(_options.Weather);

            RefreshSystemMetrics();
            _systemTimer.Start();

            await RefreshAsync();
            _refreshTimer.Start();
            _countdownTimer.Start();

            if (UseRtspCameras)
                StartCameras();
            else if (_options.Cameras.Enabled)
            {
                CameraStatus = "Configure Cameras.Primary/Secondary.RtspUrl no appsettings.json.";
                ReportError("Câmeras", CameraStatus);
            }
        }
        catch (Exception ex)
        {
            ReportError("Inicialização", ex);
            StatusMessage = "Falha na inicialização";
        }
    }

    private void StartCameras()
    {
        try
        {
            var ffmpeg = _options.Cameras.FfmpegPath;
            var fps = _options.Cameras.FrameRate;
            var maxWidth = _options.Cameras.MaxWidth;

            if (!string.IsNullOrWhiteSpace(_options.Cameras.Primary.RtspUrl))
                _primaryPlayer.Start(_options.Cameras.Primary.RtspUrl, ffmpeg, fps, maxWidth);

            if (!string.IsNullOrWhiteSpace(_options.Cameras.Secondary.RtspUrl))
                _secondaryPlayer.Start(_options.Cameras.Secondary.RtspUrl, ffmpeg, fps, maxWidth);

            CameraStatus = "";
        }
        catch (Exception ex)
        {
            UseRtspCameras = false;
            ReportError(
                "RTSP/FFmpeg",
                $"{ex}{Environment.NewLine}{Environment.NewLine}" +
                "Instale o FFmpeg e garanta que está no PATH:" + Environment.NewLine +
                "  sudo apt install -y ffmpeg" + Environment.NewLine +
                "  ffmpeg -version" + Environment.NewLine +
                "Ou defina Cameras.FfmpegPath no appsettings.json.");
        }
    }

    private void OnFrame(bool isPrimary, byte[] jpeg)
    {
        if (isPrimary)
        {
            Interlocked.Exchange(ref _pendingPrimaryJpeg, jpeg);
            SchedulePrimaryUi();
        }
        else
        {
            Interlocked.Exchange(ref _pendingSecondaryJpeg, jpeg);
            ScheduleSecondaryUi();
        }
    }

    private void SchedulePrimaryUi()
    {
        if (Interlocked.CompareExchange(ref _primaryUiScheduled, 1, 0) != 0)
            return;

        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                var data = Interlocked.Exchange(ref _pendingPrimaryJpeg, null);
                if (data is not null)
                    ApplyFrame(isPrimary: true, data);
            }
            finally
            {
                Interlocked.Exchange(ref _primaryUiScheduled, 0);
                if (_pendingPrimaryJpeg is not null)
                    SchedulePrimaryUi();
            }
        }, DispatcherPriority.Background);
    }

    private void ScheduleSecondaryUi()
    {
        if (Interlocked.CompareExchange(ref _secondaryUiScheduled, 1, 0) != 0)
            return;

        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                var data = Interlocked.Exchange(ref _pendingSecondaryJpeg, null);
                if (data is not null)
                    ApplyFrame(isPrimary: false, data);
            }
            finally
            {
                Interlocked.Exchange(ref _secondaryUiScheduled, 0);
                if (_pendingSecondaryJpeg is not null)
                    ScheduleSecondaryUi();
            }
        }, DispatcherPriority.Background);
    }

    private void ApplyFrame(bool isPrimary, byte[] jpeg)
    {
        if (_disposed || jpeg.Length == 0)
            return;

        try
        {
            using var ms = new MemoryStream(jpeg);
            var next = new Bitmap(ms);
            if (isPrimary)
            {
                var prev = _primaryCameraFrameField;
                _primaryCameraFrameField = next;
                PrimaryCameraFrame = next;
                prev?.Dispose();
            }
            else
            {
                var prev = _secondaryCameraFrameField;
                _secondaryCameraFrameField = next;
                SecondaryCameraFrame = next;
                prev?.Dispose();
            }

            if (!string.IsNullOrEmpty(CameraStatus))
                CameraStatus = "";
        }
        catch (Exception ex)
        {
            ReportCameraIssue($"Frame inválido: {ex.Message}");
        }
    }

    private void OnCameraPlayerError(string message) =>
        Dispatcher.UIThread.Post(() => ReportCameraIssue(message));

    private void ReportCameraIssue(string message)
    {
        CameraStatus = message;
        var key = message.Length <= 240 ? message : message[..240];
        if (key == _lastCameraErrorKey &&
            DateTime.UtcNow - _lastCameraErrorReportedUtc < TimeSpan.FromMinutes(2))
            return;

        _lastCameraErrorKey = key;
        _lastCameraErrorReportedUtc = DateTime.UtcNow;
        ReportError("RTSP/FFmpeg", message);
    }

    private void ReportError(string category, Exception ex)
    {
        var full = AppLog.Append(category, ex);
        AppendDiagnostic(full.Trim());
        ErrorMessage = ex.Message;
    }

    private void ReportError(string category, string message)
    {
        var full = AppLog.Append(category, message);
        AppendDiagnostic(full.Trim());
        ErrorMessage = message.Split('\n')[0].Trim();
    }

    private void AppendDiagnostic(string entry)
    {
        DiagnosticLog = string.IsNullOrWhiteSpace(DiagnosticLog)
            ? entry
            : $"{DiagnosticLog}{Environment.NewLine}{Environment.NewLine}---{Environment.NewLine}{Environment.NewLine}{entry}";
        HasDiagnosticLog = true;
        CopyDiagnosticStatus = "";
    }

    [RelayCommand]
    private void ToggleDiagnostic()
    {
        if (!HasDiagnosticLog)
            return;
        IsDiagnosticPanelOpen = !IsDiagnosticPanelOpen;
    }

    [RelayCommand]
    private void CloseDiagnostic() => IsDiagnosticPanelOpen = false;

    [RelayCommand]
    private async Task CopyDiagnosticAsync()
    {
        if (string.IsNullOrWhiteSpace(DiagnosticLog))
            return;

        try
        {
            var clipboard = Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
                ? desktop.MainWindow?.Clipboard
                : null;

            if (clipboard is null)
            {
                CopyDiagnosticStatus = "Clipboard indisponível";
                return;
            }

            await clipboard.SetTextAsync(DiagnosticLog);
            CopyDiagnosticStatus = "Copiado";
        }
        catch (Exception ex)
        {
            CopyDiagnosticStatus = $"Falha ao copiar: {ex.Message}";
        }
    }

    private void RefreshSystemMetrics()
    {
        try
        {
            var snap = _systemMetrics.Sample();
            SystemPlatform = snap.Platform;

            while (CpuCores.Count < snap.Cores.Count)
                CpuCores.Add(new CpuCoreItem());
            while (CpuCores.Count > snap.Cores.Count)
                CpuCores.RemoveAt(CpuCores.Count - 1);

            for (var i = 0; i < snap.Cores.Count; i++)
            {
                var core = snap.Cores[i];
                var item = CpuCores[i];
                item.Name = $"CPU {core.Index}";
                item.UsagePercent = core.UsagePercent;
                item.UsageText = $"{core.UsagePercent:0.0}%";
            }

            MemoryText = FormatMemPair(snap.MemoryUsedBytes, snap.MemoryTotalBytes);
            MemoryPercent = Percent(snap.MemoryUsedBytes, snap.MemoryTotalBytes);
            SwapText = snap.SwapTotalBytes <= 0
                ? "Sem swap"
                : FormatMemPair(snap.SwapUsedBytes, snap.SwapTotalBytes);
            SwapPercent = Percent(snap.SwapUsedBytes, snap.SwapTotalBytes);
            CpuTempText = snap.CpuTemperatureCelsius is null
                ? "N/D"
                : $"{snap.CpuTemperatureCelsius.Value:0.0} °C";
        }
        catch (Exception ex)
        {
            SystemPlatform = $"Erro: {ex.Message}";
        }
    }

    private static double Percent(double used, double total) =>
        total <= 0 ? 0 : Math.Clamp(100d * used / total, 0d, 100d);

    private static string FormatMemPair(double used, double total) =>
        $"{FormatBytes(used)} / {FormatBytes(total)}";

    private static string FormatBytes(double bytes)
    {
        if (bytes < 1024)
            return $"{bytes:0} B";
        if (bytes < 1024 * 1024)
            return $"{bytes / 1024:0.0} KiB";
        if (bytes < 1024d * 1024 * 1024)
            return $"{bytes / (1024 * 1024):0.0} MiB";
        return $"{bytes / (1024d * 1024 * 1024):0.00} GiB";
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (_solis is null || _weather is null || IsBusy)
            return;

        try
        {
            IsBusy = true;
            ErrorMessage = "";
            StatusMessage = "Consultando SolisCloud e Open-Meteo...";

            _inverterSn ??= await _solis.ResolveInverterSnAsync();
            InverterSnText = _inverterSn;

            var detail = await _solis.GetInverterDetailAsync(_inverterSn);
            var timeZone = (int)Math.Round(detail.TimeZone ?? _options.Solis.TimeZone);
            var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(timeZone));
            var dayPoints = await _solis.GetInverterDayAsync(_inverterSn, today, timeZone);
            var forecast = await _weather.GetForecastAsync();

            ModelText = detail.ProductModel ?? detail.Model ?? _options.Solis.InverterModel;
            UpdatedText = detail.TimeStr ?? "-";
            StateText = detail.State switch
            {
                1 => "Online",
                2 => "Offline",
                3 => "Alarme",
                _ => detail.State?.ToString() ?? "-"
            };
            EnergyTodayText = $"{Fmt(detail.EToday)} {detail.ETodayStr ?? "kWh"}";
            EnergyTotalText = $"{Fmt(detail.ETotal)} {detail.ETotalStr ?? "kWh"}";

            Pv1Voltage = FmtUnit(detail.UPv1, detail.UPv1Str ?? "V");
            Pv1Current = FmtUnit(detail.IPv1, detail.IPv1Str ?? "A");
            Pv1Power = FmtUnit(detail.Pow1, detail.Pow1Str ?? "W");
            Pv2Voltage = FmtUnit(detail.UPv2, detail.UPv2Str ?? "V");
            Pv2Current = FmtUnit(detail.IPv2, detail.IPv2Str ?? "A");
            Pv2Power = FmtUnit(detail.Pow2, detail.Pow2Str ?? "W");

            AcPower = FmtUnit(detail.Pac, detail.PacStr ?? "W");
            AcCurrent = FmtUnit(detail.IAc1, detail.IAc1Str ?? "A");
            IgbtTemp = ResolveIgbt(detail);
            InverterTemp = $"{Fmt(detail.InverterTemperature)} {detail.InverterTemperatureUnit ?? "°C"}";

            var points = dayPoints
                .Where(p => p.Pac is not null)
                .Select(p => new GenerationPoint
                {
                    Time = ExtractHour(p.TimeStr),
                    PowerW = Math.Max(0, p.Pac!.Value)
                })
                .ToList();

            _generationValues.Clear();
            foreach (var point in points)
                _generationValues.Add(point.PowerW);

            XAxes[0].Labels = points.Select(p => p.Time).ToArray();

            ChartPeakText = points.Count == 0
                ? "Sem dados de geração para hoje"
                : $"Pico {points.Max(p => p.PowerW):0.#} W · {points.Count} pontos";

            if (forecast.Current is { } current)
            {
                WeatherTime = current.Time ?? "-";
                WeatherTemp = $"{current.Temperature2m:0.0} °C";
                WeatherFeelsLike = $"{current.ApparentTemperature:0.0} °C";
                WeatherCondition = WeatherCodes.Describe(current.WeatherCode);
                WeatherIconKey = WeatherCodes.IconKey(current.WeatherCode);
                WeatherHumidity = $"{current.RelativeHumidity2m} %";
                WeatherWind = $"{current.WindSpeed10m:0.0} km/h";
                WeatherPrecip = $"{current.Precipitation:0.0} mm";
            }

            ForecastDays.Clear();
            if (forecast.Daily is { } daily)
            {
                var culture = new CultureInfo("pt-BR");
                var count = Math.Min(5, daily.Time.Count);
                for (var i = 0; i < count; i++)
                {
                    var code = i < daily.WeatherCode.Count ? daily.WeatherCode[i] : -1;
                    var min = i < daily.Temperature2mMin.Count ? daily.Temperature2mMin[i] : 0;
                    var max = i < daily.Temperature2mMax.Count ? daily.Temperature2mMax[i] : 0;
                    var rain = i < daily.PrecipitationSum.Count ? daily.PrecipitationSum[i] : 0;
                    var wind = i < daily.WindSpeed10mMax.Count ? daily.WindSpeed10mMax[i] : 0;
                    var dateLabel = DateOnly.TryParse(daily.Time[i], out var d)
                        ? d.ToString("ddd dd/MM", culture)
                        : daily.Time[i];

                    ForecastDays.Add(new ForecastDayItem
                    {
                        DateLabel = culture.TextInfo.ToTitleCase(dateLabel),
                        Condition = WeatherCodes.Describe(code),
                        IconKey = WeatherCodes.IconKey(code),
                        TempRange = $"{min:0.#}° / {max:0.#}°",
                        Rain = $"{rain:0.#} mm",
                        Wind = $"{wind:0.#} km/h"
                    });
                }
            }

            _nextRefreshUtc = DateTime.UtcNow.AddMinutes(5);
            StatusMessage = $"Atualizado às {DateTime.Now:HH:mm:ss}";
            UpdateCountdown();
        }
        catch (Exception ex)
        {
            ReportError("Atualização Solis/Weather", ex);
            StatusMessage = "Erro na atualização";
            _nextRefreshUtc = DateTime.UtcNow.AddMinutes(5);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void UpdateCountdown()
    {
        var remaining = _nextRefreshUtc - DateTime.UtcNow;
        if (remaining < TimeSpan.Zero)
            remaining = TimeSpan.Zero;
        CountdownText = $"Próxima atualização em {remaining.Minutes:00}:{remaining.Seconds:00}";
    }

    private void ValidateOptions()
    {
        if (string.IsNullOrWhiteSpace(_options.Solis.KeyId) ||
            string.IsNullOrWhiteSpace(_options.Solis.KeySecret) ||
            string.IsNullOrWhiteSpace(_options.Solis.ApiUrl))
        {
            throw new InvalidOperationException("Configure KeyId, KeySecret e ApiUrl em appsettings.json.");
        }
    }

    private static string ResolveIgbt(InverterDetail detail)
    {
        if (detail.Temp is not null)
        {
            var name = string.IsNullOrWhiteSpace(detail.TempName) ? "IGBT" : detail.TempName.Trim();
            if (detail.Temp > 100)
            {
                var celsius = (detail.Temp.Value - 32d) * 5d / 9d;
                return $"{celsius:0.0} °C ({name})";
            }

            return $"{detail.Temp:0.0} °C ({name})";
        }

        if (detail.InverterTemperature is not null)
            return $"{Fmt(detail.InverterTemperature)} {detail.InverterTemperatureUnit ?? "°C"}";

        return "N/D";
    }

    private static string Fmt(double? value) => value is null ? "-" : value.Value.ToString("0.##");

    private static string FmtUnit(double? value, string unit) =>
        value is null ? "-" : $"{value.Value:0.##} {unit}";

    private static string ExtractHour(string? timeStr)
    {
        if (string.IsNullOrWhiteSpace(timeStr))
            return "--:--";
        var parts = timeStr.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var timePart = parts.Length > 1 ? parts[^1] : parts[0];
        return timePart.Length >= 5 ? timePart[..5] : timePart;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _refreshTimer.Stop();
        _countdownTimer.Stop();
        _systemTimer.Stop();
        _systemMetrics.Dispose();

        _primaryPlayer.Dispose();
        _secondaryPlayer.Dispose();

        _solis?.Dispose();
        _weather?.Dispose();
        _primaryCameraFrameField?.Dispose();
        _secondaryCameraFrameField?.Dispose();
    }
}
