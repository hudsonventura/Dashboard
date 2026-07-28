using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LibVLCSharp.Shared;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using Microsoft.Extensions.Configuration;
using SkiaSharp;
using SolisDashboard.App.Cameras;
using SolisDashboard.App.Diagnostics;
using SolisDashboard.App.Display;
using SolisDashboard.App.Models;
using SolisDashboard.Configuration;
using SolisDashboard.Display;
using SolisDashboard.Models;
using SolisDashboard.Services;

namespace SolisDashboard.App.ViewModels;

public partial class MainViewModel : ViewModelBase, IDisposable
{
    private static readonly SKColor AccentColor = SKColor.Parse("#F5B942");
    private static readonly SKColor AxisLineColor = SKColor.Parse("#2A3442");
    /// <summary>Janela fixa do gráfico em horário local UTC-4.</summary>
    private const double ChartStartHour = 6;
    private const double ChartEndHour = 18;
    /// <summary>Atualização Solis só entre 10:00 e 22:00 UTC.</summary>
    private static readonly TimeSpan SolisWindowStartUtc = TimeSpan.FromHours(10);
    private static readonly TimeSpan SolisWindowEndUtc = TimeSpan.FromHours(22);

    private readonly AppOptions _options;
    private readonly DispatcherTimer _refreshTimer;
    private readonly DispatcherTimer _weatherTimer;
    private readonly DispatcherTimer _countdownTimer;
    private readonly DispatcherTimer _systemTimer;
    private readonly ObservableCollection<ObservablePoint> _generationValues = [];
    private readonly SystemMetricsService _systemMetrics = new();
    private readonly LibVlcCameraHost _cameras = new();
    private SolisApiClient? _solis;
    private IWeatherProvider? _weather;
    private string? _inverterSn;
    private DateTime _nextRefreshUtc = DateTime.UtcNow;
    private DateTime _nextWeatherUtc = DateTime.UtcNow;
    private bool _weatherBusy;
    private bool _disposed;
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
        Disks = [];
        DisksVisible = OperatingSystem.IsLinux();
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
            new LineSeries<ObservablePoint>
            {
                Name = "Potência (W)",
                Values = _generationValues,
                Fill = new SolidColorPaint(AccentColor.WithAlpha(70)),
                Stroke = new SolidColorPaint(AccentColor, 3),
                GeometrySize = 0,
                GeometryStroke = null,
                GeometryFill = null,
                LineSmoothness = 0.35
            }
        ];

        XAxes =
        [
            new Axis
            {
                MinLimit = ChartStartHour,
                MaxLimit = ChartEndHour,
                MinStep = 1,
                LabelsPaint = null,
                NamePaint = null,
                SeparatorsPaint = new SolidColorPaint(AxisLineColor),
                ShowSeparatorLines = true
            }
        ];

        YAxes =
        [
            new Axis
            {
                LabelsPaint = null,
                NamePaint = null,
                SeparatorsPaint = new SolidColorPaint(AxisLineColor),
                ShowSeparatorLines = true
            }
        ];

        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(5) };
        _refreshTimer.Tick += async (_, _) => await RefreshSolisAsync();

        var weatherMinutes = Math.Clamp(_options.Weather.RefreshMinutes, 5, 60);
        _weatherTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(weatherMinutes) };
        _weatherTimer.Tick += async (_, _) => await RefreshWeatherAsync();

        _countdownTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _countdownTimer.Tick += (_, _) => UpdateCountdown();

        _systemTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _systemTimer.Tick += (_, _) => RefreshSystemMetrics();

        UpdateCountdown();

        _cameras.ErrorOccurred += OnCameraPlayerError;

        LogFilePath = AppLog.LogFilePath;
        _ = InitializeAsync();
    }

    public ObservableCollection<ForecastDayItem> ForecastDays { get; }
    public ObservableCollection<CpuCoreItem> CpuCores { get; }
    public ObservableCollection<DiskItem> Disks { get; }
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
    [ObservableProperty] private string _clockDateText = "";
    [ObservableProperty] private string _clockTimeText = "";

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

    [ObservableProperty] private string _weatherProviderLabel = "Weather";
    [ObservableProperty] private string _weatherTemp = "-";
    [ObservableProperty] private string _weatherTodayRange = "-";
    [ObservableProperty] private string _weatherFeelsLike = "-";
    [ObservableProperty] private string _weatherCondition = "-";
    [ObservableProperty] private string _weatherHumidity = "-";
    [ObservableProperty] private string _weatherWind = "-";
    [ObservableProperty] private string _weatherPrecip = "-";
    [ObservableProperty] private string _weatherIconKey = "unknown";
    [ObservableProperty] private string _weatherTime = "-";
    [ObservableProperty] private string _chartPeakText = "";
    [ObservableProperty] private string _power6kFirstText = "-";
    [ObservableProperty] private string _power6kLastText = "-";

    [ObservableProperty] private bool _camerasVisible;
    [ObservableProperty] private bool _useRtspCameras;
    [ObservableProperty] private string _primaryCameraName = "Câmera 1";
    [ObservableProperty] private string _secondaryCameraName = "Câmera 2";
    [ObservableProperty] private double _primaryCameraHeight = 360;
    [ObservableProperty] private double _secondaryCameraHeight = 180;
    [ObservableProperty] private MediaPlayer? _primaryMediaPlayer;
    [ObservableProperty] private MediaPlayer? _secondaryMediaPlayer;
    [ObservableProperty] private string _cameraStatus = "";

    [ObservableProperty] private string _systemPlatform = "-";
    [ObservableProperty] private string _memoryText = "-";
    [ObservableProperty] private double _memoryPercent;
    [ObservableProperty] private IBrush _memoryBarBrush = UsageBarPalette.ForPercent(0);
    [ObservableProperty] private bool _memoryIsCritical;
    [ObservableProperty] private string _swapText = "-";
    [ObservableProperty] private double _swapPercent;
    [ObservableProperty] private string _cpuTempText = "N/D";
    [ObservableProperty] private bool _disksVisible;

    partial void OnMemoryPercentChanged(double value)
    {
        MemoryBarBrush = UsageBarPalette.ForPercent(value);
        MemoryIsCritical = UsageBarPalette.IsCritical(value);
    }

    private async Task InitializeAsync()
    {
        // Libera a UI thread imediatamente — senão a janela não abre no Linux
        // enquanto SMART/discos/Solis rodam no trecho síncrono inicial.
        await Task.Yield();

        try
        {
            ValidateOptions();
            _solis = new SolisApiClient(_options.Solis);
            _weather = CreateWeatherProvider(_options.Weather);
            WeatherProviderLabel = $"Weather · {_weather.DisplayName}";

            RefreshSystemMetrics();
            _systemTimer.Start();

            await RefreshSolisAsync();
            await RefreshWeatherAsync();
            _refreshTimer.Start();
            _weatherTimer.Start();
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
            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    // Garante que nenhum VideoView exista até o LibVLC subir.
                    PrimaryMediaPlayer = null;
                    SecondaryMediaPlayer = null;

                    _cameras.Start(
                        UseRtspCameras ? _options.Cameras.Primary.RtspUrl : null,
                        UseRtspCameras ? _options.Cameras.Secondary.RtspUrl : null,
                        _options.Cameras.LibVlcPath);

                    PrimaryMediaPlayer = _cameras.PrimaryPlayer;
                    SecondaryMediaPlayer = _cameras.SecondaryPlayer;
                    CameraStatus = string.IsNullOrWhiteSpace(_cameras.ResolvedLibVlcDirectory)
                        ? ""
                        : $"LibVLC: {_cameras.ResolvedLibVlcDirectory}";
                }
                catch (Exception ex)
                {
                    UseRtspCameras = false;
                    PrimaryMediaPlayer = null;
                    SecondaryMediaPlayer = null;
                    _cameras.Stop();
                    ReportError(
                        "RTSP/LibVLC",
                        $"{ex}{Environment.NewLine}{Environment.NewLine}" +
                        "NÃO use: export LD_LIBRARY_PATH=/snap/vlc/current/usr/lib" + Environment.NewLine +
                        "(isso quebra libidn e outras libs)." + Environment.NewLine + Environment.NewLine +
                        "No Linux, ache o diretório correto:" + Environment.NewLine +
                        "  find /snap/vlc/current -name 'libvlc.so*' 2>/dev/null" + Environment.NewLine +
                        "E no appsettings.json:" + Environment.NewLine +
                        "  \"Cameras\": { \"LibVlcPath\": \"/snap/vlc/current/usr/lib/x86_64-linux-gnu\" }" + Environment.NewLine +
                        "Ou: export LIBVLC_PATH=/snap/vlc/current/usr/lib/x86_64-linux-gnu" + Environment.NewLine +
                        "Teste: ls \"$LIBVLC_PATH\"/libvlc.so*");
                }
            });
        }
        catch (Exception ex)
        {
            UseRtspCameras = false;
            PrimaryMediaPlayer = null;
            SecondaryMediaPlayer = null;
            ReportError("RTSP/LibVLC", ex);
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
        ReportError("RTSP/LibVLC", message);
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

            if (DisksVisible)
                SyncDisks(snap.Disks);
            else if (Disks.Count > 0)
                Disks.Clear();
        }
        catch (Exception ex)
        {
            SystemPlatform = $"Erro: {ex.Message}";
        }
    }

    private void SyncDisks(IReadOnlyList<DiskMetrics> disks)
    {
        while (Disks.Count < disks.Count)
            Disks.Add(new DiskItem());
        while (Disks.Count > disks.Count)
            Disks.RemoveAt(Disks.Count - 1);

        for (var i = 0; i < disks.Count; i++)
        {
            var src = disks[i];
            var item = Disks[i];
            item.Device = src.Device;
            item.MountPoint = src.MountPoint;
            item.UsagePercent = src.UsagePercent;
            item.UsageText = $"{FormatBytes(src.UsedBytes)} / {FormatBytes(src.TotalBytes)} ({src.UsagePercent:0}%)";
            item.TemperatureText = src.TemperatureCelsius is null
                ? "N/D"
                : $"{src.TemperatureCelsius.Value:0} °C";
            item.SmartStatus = string.IsNullOrWhiteSpace(src.SmartStatus) ? "N/D" : src.SmartStatus;
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
        await RefreshSolisAsync();
        await RefreshWeatherAsync();
    }

    private async Task RefreshSolisAsync()
    {
        if (_solis is null || IsBusy)
            return;

        if (!IsSolisRefreshWindow(DateTime.UtcNow))
        {
            _nextRefreshUtc = NextSolisRefreshUtc(DateTime.UtcNow);
            StatusMessage = "Solis em pausa noturna (10–22 UTC)";
            UpdateCountdown();
            return;
        }

        try
        {
            IsBusy = true;
            StatusMessage = "Consultando SolisCloud...";

            _inverterSn ??= await _solis.ResolveInverterSnAsync();
            InverterSnText = _inverterSn;

            var detail = await _solis.GetInverterDetailAsync(_inverterSn);
            var timeZone = (int)Math.Round(detail.TimeZone ?? _options.Solis.TimeZone);
            var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(timeZone));
            var dayPoints = await _solis.GetInverterDayAsync(_inverterSn, today, timeZone);

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
                .Where(p => TryParseHourDecimal(p.Time, out var hour) &&
                            hour >= ChartStartHour && hour <= ChartEndHour)
                .ToList();

            _generationValues.Clear();
            foreach (var point in points)
            {
                if (!TryParseHourDecimal(point.Time, out var hour))
                    continue;
                _generationValues.Add(new ObservablePoint(hour, point.PowerW));
            }

            // Mantém o eixo sempre 06h–18h (UTC-4 / horário local do gráfico).
            XAxes[0].MinLimit = ChartStartHour;
            XAxes[0].MaxLimit = ChartEndHour;
            XAxes[0].Labels = null;

            ChartPeakText = points.Count == 0
                ? "Sem dados de geração para hoje"
                : $"Pico {points.Max(p => p.PowerW):0.#} W · {points.Count} pts · 06–18h";

            const double thresholdW = 6000;
            var atOrAbove6k = points.Where(p => p.PowerW >= thresholdW).ToList();
            Power6kFirstText = atOrAbove6k.Count == 0 ? "—" : atOrAbove6k[0].Time;
            Power6kLastText = atOrAbove6k.Count == 0 ? "—" : atOrAbove6k[^1].Time;

            _nextRefreshUtc = NextSolisRefreshUtc(DateTime.UtcNow);
            StatusMessage = $"Solis atualizado às {DateTime.Now:HH:mm:ss}";
            UpdateCountdown();
        }
        catch (Exception ex)
        {
            ReportError("Atualização Solis", ex);
            StatusMessage = "Erro na atualização Solis";
            _nextRefreshUtc = NextSolisRefreshUtc(DateTime.UtcNow);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RefreshWeatherAsync()
    {
        if (_weather is null || _weatherBusy)
            return;

        try
        {
            _weatherBusy = true;
            StatusMessage = $"Consultando {_weather.DisplayName}...";

            var snapshot = await _weather.GetSnapshotAsync();
            ApplyWeatherSnapshot(snapshot);

            var weatherMinutes = Math.Clamp(_options.Weather.RefreshMinutes, 5, 60);
            _nextWeatherUtc = DateTime.UtcNow.AddMinutes(weatherMinutes);
            StatusMessage = $"Clima atualizado às {DateTime.Now:HH:mm:ss}";
            UpdateCountdown();
        }
        catch (Exception ex)
        {
            ReportError("Atualização Weather", ex);
            StatusMessage = "Erro na atualização do clima";
            _nextWeatherUtc = DateTime.UtcNow.AddMinutes(Math.Clamp(_options.Weather.RefreshMinutes, 5, 60));
        }
        finally
        {
            _weatherBusy = false;
        }
    }

    private void ApplyWeatherSnapshot(WeatherSnapshot snapshot)
    {
        WeatherProviderLabel = $"Weather · {snapshot.ProviderLabel}";
        WeatherTime = snapshot.ObservedAt ?? "-";
        WeatherTemp = $"{Math.Round(snapshot.TemperatureC):0} °C";
        WeatherFeelsLike = $"{Math.Round(snapshot.FeelsLikeC):0} °C";
        WeatherCondition = snapshot.Condition;
        WeatherIconKey = snapshot.IconKey;
        WeatherHumidity = $"{snapshot.HumidityPercent} %";
        WeatherWind = $"{Math.Round(snapshot.WindKmh):0} km/h";
        WeatherPrecip = $"{Math.Round(snapshot.PrecipitationMm):0} mm";

        ForecastDays.Clear();
        WeatherTodayRange = "-";
        var culture = new CultureInfo("pt-BR");
        for (var i = 0; i < snapshot.Days.Count; i++)
        {
            var day = snapshot.Days[i];
            var max = Math.Round(day.TempMaxC);
            var min = Math.Round(day.TempMinC);
            if (i == 0)
            {
                WeatherTodayRange = $"{max:0}° / {min:0}°";
                continue; // previsão: pula o dia de hoje
            }

            var dayLabel = day.Date.ToString("ddd", culture).TrimEnd('.');
            dayLabel = culture.TextInfo.ToTitleCase(dayLabel.ToLowerInvariant());

            ForecastDays.Add(new ForecastDayItem
            {
                DateLabel = dayLabel,
                Condition = day.Condition,
                IconKey = day.IconKey,
                TempRange = $"{max:0}°/{min:0}°",
                Rain = $"{Math.Round(day.PrecipitationMm):0} mm",
                Wind = $"{Math.Round(day.WindKmh):0} km/h"
            });
        }
    }

    private static IWeatherProvider CreateWeatherProvider(WeatherOptions weather)
    {
        var name = weather.Provider?.Trim() ?? "OpenMeteo";
        if (name.Equals("OpenWeather", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("OpenWeatherMap", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("OneCall", StringComparison.OrdinalIgnoreCase))
        {
            return new OpenWeatherMapProvider(weather);
        }

        return new OpenMeteoWeatherProvider(weather);
    }

    private void UpdateCountdown()
    {
        var remaining = _nextRefreshUtc - DateTime.UtcNow;
        if (remaining < TimeSpan.Zero)
            remaining = TimeSpan.Zero;
        var weatherRemaining = _nextWeatherUtc - DateTime.UtcNow;
        if (weatherRemaining < TimeSpan.Zero)
            weatherRemaining = TimeSpan.Zero;
        CountdownText =
            $"Solis {FormatCountdown(remaining)} · Clima {FormatCountdown(weatherRemaining)}";

        var now = DateTime.Now;
        ClockDateText = now.ToString("yyyy-MM-dd");
        ClockTimeText = now.ToString("HH:mm:ss");
    }

    private static string FormatCountdown(TimeSpan remaining) =>
        remaining.TotalHours >= 1
            ? $"{(int)remaining.TotalHours:00}:{remaining.Minutes:00}:{remaining.Seconds:00}"
            : $"{remaining.Minutes:00}:{remaining.Seconds:00}";

    private void ValidateOptions()
    {
        if (string.IsNullOrWhiteSpace(_options.Solis.KeyId) ||
            string.IsNullOrWhiteSpace(_options.Solis.KeySecret) ||
            string.IsNullOrWhiteSpace(_options.Solis.ApiUrl))
        {
            throw new InvalidOperationException("Configure KeyId, KeySecret e ApiUrl em appsettings.json.");
        }

        var provider = _options.Weather.Provider?.Trim() ?? "OpenMeteo";
        if ((provider.Equals("OpenWeather", StringComparison.OrdinalIgnoreCase) ||
             provider.Equals("OpenWeatherMap", StringComparison.OrdinalIgnoreCase) ||
             provider.Equals("OneCall", StringComparison.OrdinalIgnoreCase)) &&
            string.IsNullOrWhiteSpace(_options.Weather.OpenWeather.ApiKey))
        {
            throw new InvalidOperationException("Configure Weather:OpenWeather:ApiKey no appsettings.json.");
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

    private static bool TryParseHourDecimal(string? hhmm, out double hour)
    {
        hour = 0;
        if (string.IsNullOrWhiteSpace(hhmm) || hhmm.Length < 4)
            return false;
        if (!TimeOnly.TryParseExact(hhmm, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t) &&
            !TimeOnly.TryParse(hhmm, CultureInfo.InvariantCulture, DateTimeStyles.None, out t))
            return false;
        hour = t.Hour + t.Minute / 60d + t.Second / 3600d;
        return true;
    }

    private static bool IsSolisRefreshWindow(DateTime utcNow)
    {
        var tod = utcNow.TimeOfDay;
        return tod >= SolisWindowStartUtc && tod < SolisWindowEndUtc;
    }

    private DateTime NextSolisRefreshUtc(DateTime utcNow)
    {
        if (IsSolisRefreshWindow(utcNow))
            return utcNow.Add(_refreshTimer.Interval);

        var todayStart = utcNow.Date + SolisWindowStartUtc;
        return utcNow < todayStart ? todayStart : todayStart.AddDays(1);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _refreshTimer.Stop();
        _weatherTimer.Stop();
        _countdownTimer.Stop();
        _systemTimer.Stop();
        _systemMetrics.Dispose();
        _cameras.Dispose();
        PrimaryMediaPlayer = null;
        SecondaryMediaPlayer = null;

        _solis?.Dispose();
        _weather?.Dispose();
    }
}
