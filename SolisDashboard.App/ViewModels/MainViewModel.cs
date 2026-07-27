using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LibVLCSharp.Shared;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using Microsoft.Extensions.Configuration;
using SkiaSharp;
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
    private readonly DispatcherTimer _cameraTimer;
    private readonly DispatcherTimer _systemTimer;
    private readonly ObservableCollection<double> _generationValues = [];
    private readonly SystemMetricsService _systemMetrics = new();
    private SolisApiClient? _solis;
    private OpenMeteoClient? _weather;
    private ShinobiClient? _shinobi;
    private LibVLC? _libVlc;
    private string? _inverterSn;
    private DateTime _nextRefreshUtc = DateTime.UtcNow;
    private bool _disposed;
    private bool _cameraBusy;
    private Bitmap? _primaryCameraFrameField;
    private Bitmap? _secondaryCameraFrameField;
    private readonly bool _primaryUsesRtsp;
    private readonly bool _secondaryUsesRtsp;

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
        CamerasVisible = _options.Shinobi.Enabled;
        PrimaryCameraName = string.IsNullOrWhiteSpace(_options.Shinobi.Primary.Name) ? "Câmera 1" : _options.Shinobi.Primary.Name;
        SecondaryCameraName = string.IsNullOrWhiteSpace(_options.Shinobi.Secondary.Name) ? "Câmera 2" : _options.Shinobi.Secondary.Name;
        PrimaryCameraHeight = Math.Max(80, _options.Shinobi.Primary.Height);
        SecondaryCameraHeight = Math.Max(80, _options.Shinobi.Secondary.Height);

        _primaryUsesRtsp = !string.IsNullOrWhiteSpace(_options.Shinobi.Primary.RtspUrl);
        _secondaryUsesRtsp = !string.IsNullOrWhiteSpace(_options.Shinobi.Secondary.RtspUrl);
        UseRtspCameras = _primaryUsesRtsp || _secondaryUsesRtsp;

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

        var cameraSeconds = Math.Clamp(_options.Shinobi.SnapshotRefreshSeconds, 0.5, 30);
        _cameraTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(cameraSeconds) };
        _cameraTimer.Tick += async (_, _) => await RefreshCamerasAsync();

        _systemTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _systemTimer.Tick += (_, _) => RefreshSystemMetrics();

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
    [ObservableProperty] private MediaPlayer? _primaryMediaPlayer;
    [ObservableProperty] private MediaPlayer? _secondaryMediaPlayer;

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
            _shinobi = new ShinobiClient(_options.Shinobi);

            RefreshSystemMetrics();
            _systemTimer.Start();

            await RefreshAsync();
            _refreshTimer.Start();
            _countdownTimer.Start();

            if (UseRtspCameras)
            {
                StartRtspPlayers();
            }
            else if (_shinobi.IsConfigured)
            {
                await RefreshCamerasAsync();
                _cameraTimer.Start();
            }
            else if (_options.Shinobi.Enabled)
            {
                CameraStatus = "Configure RtspUrl ou BaseUrl/ApiKey/GroupKey/MonitorId no appsettings.";
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            StatusMessage = "Falha na inicialização";
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

    private void StartRtspPlayers()
    {
        try
        {
            Core.Initialize();
            _libVlc = new LibVLC(
                "--network-caching=400",
                "--rtsp-tcp",
                "--no-audio",
                "--avcodec-hw=any");

            if (_primaryUsesRtsp)
            {
                PrimaryMediaPlayer = new MediaPlayer(_libVlc);
                PlayRtsp(PrimaryMediaPlayer, _options.Shinobi.Primary.RtspUrl);
            }

            if (_secondaryUsesRtsp)
            {
                SecondaryMediaPlayer = new MediaPlayer(_libVlc);
                PlayRtsp(SecondaryMediaPlayer, _options.Shinobi.Secondary.RtspUrl);
            }

            CameraStatus = "";
        }
        catch (Exception ex)
        {
            CameraStatus = $"RTSP: {ex.Message}";
        }
    }

    private void PlayRtsp(MediaPlayer player, string rtspUrl)
    {
        using var media = new Media(_libVlc!, rtspUrl, FromType.FromLocation);
        media.AddOption(":rtsp-tcp");
        media.AddOption(":network-caching=400");
        player.Play(media);
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
            ErrorMessage = ex.Message;
            StatusMessage = "Erro na atualização";
            _nextRefreshUtc = DateTime.UtcNow.AddMinutes(5);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RefreshCamerasAsync()
    {
        if (UseRtspCameras || _shinobi is null || !_shinobi.IsConfigured || _cameraBusy)
            return;

        try
        {
            _cameraBusy = true;
            var primaryId = _options.Shinobi.Primary.MonitorId;
            var secondaryId = _options.Shinobi.Secondary.MonitorId;

            var primaryTask = string.IsNullOrWhiteSpace(primaryId)
                ? Task.FromResult<byte[]?>(null)
                : _shinobi.GetJpegAsync(primaryId);
            var secondaryTask = string.IsNullOrWhiteSpace(secondaryId)
                ? Task.FromResult<byte[]?>(null)
                : _shinobi.GetJpegAsync(secondaryId);

            await Task.WhenAll(primaryTask, secondaryTask);

            var primaryBytes = await primaryTask;
            var secondaryBytes = await secondaryTask;

            if (primaryBytes is { Length: > 0 })
                SetPrimaryFrame(primaryBytes);

            if (secondaryBytes is { Length: > 0 })
                SetSecondaryFrame(secondaryBytes);

            CameraStatus = primaryBytes is null && secondaryBytes is null
                ? "Shinobi JPEG: sem frame (habilite Snapshot ou use RtspUrl)."
                : "";
        }
        catch (Exception ex)
        {
            CameraStatus = $"Shinobi: {ex.Message}";
        }
        finally
        {
            _cameraBusy = false;
        }
    }

    private void SetPrimaryFrame(byte[] jpegBytes)
    {
        using var ms = new MemoryStream(jpegBytes);
        var next = new Bitmap(ms);
        var previous = _primaryCameraFrameField;
        _primaryCameraFrameField = next;
        PrimaryCameraFrame = next;
        previous?.Dispose();
    }

    private void SetSecondaryFrame(byte[] jpegBytes)
    {
        using var ms = new MemoryStream(jpegBytes);
        var next = new Bitmap(ms);
        var previous = _secondaryCameraFrameField;
        _secondaryCameraFrameField = next;
        SecondaryCameraFrame = next;
        previous?.Dispose();
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
        _cameraTimer.Stop();
        _systemTimer.Stop();
        _systemMetrics.Dispose();

        PrimaryMediaPlayer?.Stop();
        SecondaryMediaPlayer?.Stop();
        PrimaryMediaPlayer?.Dispose();
        SecondaryMediaPlayer?.Dispose();
        _libVlc?.Dispose();

        _solis?.Dispose();
        _weather?.Dispose();
        _shinobi?.Dispose();
        _primaryCameraFrameField?.Dispose();
        _secondaryCameraFrameField?.Dispose();
    }
}
