using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dashboard.Models;
using Dashboard.Services;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using Microsoft.Extensions.Configuration;
using SkiaSharp;

namespace Dashboard.ViewModels;

public partial class MainWindowViewModel : ObservableObject, IDisposable
{
    private const double ChartStartHour = 6;
    private const double ChartEndHour = 18;
    private const double ChartMaxPowerWatts = 6000;
    private const int SolarGenerationTargetKwh = 33;
    private const int SolarGenerationDisplayMaxKwh = 40;
    private static readonly SKColor SolarAccent = SKColor.Parse("#F59E0B");
    private static readonly SKColor ChartGrid = SKColor.Parse("#334155");
    [ObservableProperty]
    private string _title = "Monitor de Câmeras RTSP & Dashboard";

    [ObservableProperty]
    private CameraViewModel? _primaryCamera;

    [ObservableProperty]
    private CameraViewModel? _secondaryCamera;

    [ObservableProperty]
    private CameraViewModel? _focusedCamera;

    [ObservableProperty]
    private bool _isDualView = true;

    [ObservableProperty]
    private string _currentTimeText = string.Empty;

    [ObservableProperty]
    private bool _isCamerasEnabled = true;

    [ObservableProperty]
    private string _configStatusMessage = string.Empty;

    // SOLIS PANEL PROPERTIES (70% Left Field)
    [ObservableProperty]
    private string _solisApiUrlText = "https://www.soliscloud.com:13333";

    [ObservableProperty]
    private string _solisKeyIdText = "1300386381677723412";

    [ObservableProperty]
    private string _solisInverterModelText = "S6-GR1P6K-S";

    [ObservableProperty]
    private string _solisCurrencyText = "BRL (R$)";

    [ObservableProperty]
    private string _solisTimeZoneText = "GMT -3";

    [ObservableProperty]
    private string _solisInverterSnText = "-";

    [ObservableProperty]
    private string _solisStateText = "Não consultado";

    [ObservableProperty]
    private string _solisEnergyTodayText = "-";

    [ObservableProperty]
    private string _solisGenerationProgressText = "Meta: 33 kWh (100%)";

    [ObservableProperty]
    private string _solisPowerText = "-";

    [ObservableProperty]
    private string _solisTemperatureText = "-";

    [ObservableProperty]
    private string _solisPv1PowerText = "-";

    [ObservableProperty]
    private string _solisPv1DetailText = "-";

    [ObservableProperty]
    private string _solisPv2PowerText = "-";

    [ObservableProperty]
    private string _solisPv2DetailText = "-";

    [ObservableProperty]
    private string _solisAcCurrentText = "-";

    [ObservableProperty]
    private string _solisIgbtTemperatureText = "-";

    [ObservableProperty]
    private string _solisLastUpdatedText = "-";

    [ObservableProperty]
    private string _solisErrorMessage = string.Empty;

    [ObservableProperty]
    private bool _isSolisLoading;



    [ObservableProperty]
    private string _solisFirst6KwText = "—";

    [ObservableProperty]
    private string _solisLast6KwText = "—";

    [ObservableProperty]
    private string _solisLocalDateText = "-";

    [ObservableProperty]
    private string _solisLocalTimeText = "-";

    // WEATHER PANEL PROPERTIES (30% Right Field - Middle)
    [ObservableProperty]
    private string _weatherProviderText = "OpenMeteo";

    [ObservableProperty]
    private string _weatherLocationText = "Cuiabá - MT";

    [ObservableProperty]
    private string _weatherCurrentTempText = "-- °C";

    [ObservableProperty]
    private string _weatherCurrentHumidityText = "-- %";

    [ObservableProperty]
    private string _weatherConditionText = "Carregando...";

    [ObservableProperty]
    private string _weatherIconEmoji = "☀️";

    [ObservableProperty]
    private string _weatherLastUpdatedText = "Atualizando...";

    [ObservableProperty]
    private string _weatherErrorMessage = string.Empty;

    [ObservableProperty]
    private bool _isWeatherLoading;

    public ObservableCollection<DailyForecast> ForecastDays { get; } = new();

    // SYSTEM MONITOR PROPERTIES (30% Right Field - Bottom)
    [ObservableProperty]
    private string _sysMemoryText = "0 MB";

    [ObservableProperty]
    private string _sysUptimeText = "00:00:00";

    [ObservableProperty]
    private string _sysThreadsText = "0 Threads";

    [ObservableProperty]
    private string _sysOsText = "Linux x86_64";

    [ObservableProperty]
    private string _sysMemoryUsageText = "--";

    [ObservableProperty]
    private double _sysMemoryUsage;

    [ObservableProperty]
    private string _sysSwapUsageText = "--";

    [ObservableProperty]
    private double _sysSwapUsage;

    [ObservableProperty]
    private string _sysCpuTemperatureText = "N/D";

    public ObservableCollection<CpuUsageViewModel> CpuCores { get; } = new();
    public ObservableCollection<DiskUsageViewModel> SystemDisks { get; } = new();

    private readonly DispatcherTimer _clockTimer;
    private readonly DispatcherTimer _weatherTimer;
    private readonly DispatcherTimer _solisTimer;
    private readonly DateTime _startTime = DateTime.Now;

    private WeatherSettings _weatherSettings = new();
    private readonly SystemMonitorService _systemMonitorService = new();
    private SolisSettings _solisSettings = new();
    private SolisApiClient? _solisClient;
    private string? _inverterSn;
    private readonly ObservableCollection<ObservablePoint> _solarGenerationValues = new();

    public MainWindowViewModel()
    {
        SysOsText = RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
            ? "Linux"
            : $"{RuntimeInformation.OSDescription} ({RuntimeInformation.ProcessArchitecture})";

        // Clock timer (updates time & system metrics every 1s)
        _clockTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _clockTimer.Tick += OnClockTick;
        _clockTimer.Start();

        // Weather timer (updates weather every 30 minutes)
        _weatherTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMinutes(30)
        };
        _weatherTimer.Tick += async (s, e) => await FetchWeatherAsync();

        _solisTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMinutes(5)
        };
        _solisTimer.Tick += async (s, e) =>
        {
            if (IsSolisRefreshWindow())
                await RefreshSolisCoreAsync();
        };

        SolarChartSeries =
        [
            new LineSeries<ObservablePoint>
            {
                Name = "Potência",
                Values = _solarGenerationValues,
                Fill = new SolidColorPaint(SolarAccent.WithAlpha(72)),
                Stroke = new SolidColorPaint(SolarAccent, 3),
                GeometrySize = 0,
                GeometryStroke = null,
                GeometryFill = null,
                LineSmoothness = 0.25
            }
        ];
        SolarChartXAxes =
        [
            new Axis
            {
                MinLimit = ChartStartHour,
                MaxLimit = ChartEndHour,
                MinStep = 1,
                Labeler = value => $"{value:0}h",
                LabelsPaint = new SolidColorPaint(SKColor.Parse("#94A3B8")),
                SeparatorsPaint = new SolidColorPaint(ChartGrid),
                TextSize = 10
            }
        ];
        SolarChartYAxes =
        [
            new Axis
            {
                MinLimit = 0,
                MaxLimit = ChartMaxPowerWatts,
                MinStep = 1000,
                Labeler = value => value == 0 ? "0 W" : $"{value / 1000d:0} kW",
                LabelsPaint = new SolidColorPaint(SKColor.Parse("#94A3B8")),
                SeparatorsPaint = new SolidColorPaint(ChartGrid),
                TextSize = 10
            }
        ];
        ApplySolarGenerationProgress(0);

        LoadConfiguration();
    }

    public ISeries[] SolarChartSeries { get; }
    public Axis[] SolarChartXAxes { get; }
    public Axis[] SolarChartYAxes { get; }
    public ObservableCollection<SolarProgressSegment> SolarGenerationProgressSegments { get; } = new();

    private void OnClockTick(object? sender, EventArgs e)
    {
        CurrentTimeText = DateTime.Now.ToString("HH:mm:ss - dd/MM/yyyy");

        // Update real system monitor metrics
        TimeSpan uptime = DateTime.Now - _startTime;
        SysUptimeText = $"{uptime.Hours:D2}:{uptime.Minutes:D2}:{uptime.Seconds:D2}";

        long memoryBytes = GC.GetTotalMemory(false);
        SysMemoryText = $"{memoryBytes / (1024.0 * 1024.0):F1} MB";

        using (var process = Process.GetCurrentProcess())
        {
            SysThreadsText = $"{process.Threads.Count} Threads";
        }

        UpdateSystemMetrics();
        UpdateSolisClock();
    }

    private void UpdateSystemMetrics()
    {
        try
        {
            SystemMetricsSnapshot snapshot = _systemMonitorService.GetSnapshot();
            UpdateCpuCores(snapshot);

            SysMemoryUsage = ToPercentage(snapshot.MemoryUsedBytes, snapshot.MemoryTotalBytes);
            SysMemoryUsageText = FormatUsage(snapshot.MemoryUsedBytes, snapshot.MemoryTotalBytes);
            SysSwapUsage = ToPercentage(snapshot.SwapUsedBytes, snapshot.SwapTotalBytes);
            SysSwapUsageText = FormatUsage(snapshot.SwapUsedBytes, snapshot.SwapTotalBytes);
            SysCpuTemperatureText = snapshot.CpuTemperatureCelsius is double temperature ? $"{temperature:F1} °C" : "N/D";

            SystemDisks.Clear();
            foreach (DiskUsage disk in snapshot.Disks)
            {
                SystemDisks.Add(new DiskUsageViewModel
                {
                    Name = string.IsNullOrWhiteSpace(disk.Name) ? disk.MountPoint : disk.Name,
                    MountPoint = disk.MountPoint,
                    Usage = ToPercentage(disk.UsedBytes, disk.TotalBytes),
                    UsageText = FormatUsage(disk.UsedBytes, disk.TotalBytes)
                });
            }
        }
        catch
        {
            // Métricas do host não devem interromper o dashboard.
        }
    }

    private void UpdateCpuCores(SystemMetricsSnapshot snapshot)
    {
        while (CpuCores.Count < snapshot.CpuUsagePercentages.Count)
            CpuCores.Add(new CpuUsageViewModel($"CPU {CpuCores.Count}"));

        while (CpuCores.Count > snapshot.CpuUsagePercentages.Count)
            CpuCores.RemoveAt(CpuCores.Count - 1);

        for (int index = 0; index < snapshot.CpuUsagePercentages.Count; index++)
            CpuCores[index].Update(snapshot.CpuUsagePercentages[index]);
    }

    private static double ToPercentage(long used, long total) => total > 0 ? Math.Clamp(used * 100d / total, 0, 100) : 0;

    private static string FormatUsage(long used, long total) => total > 0
        ? $"{FormatBytes(used)} / {FormatBytes(total)} ({ToPercentage(used, total):F0}%)"
        : "N/D";

    private static string FormatBytes(long bytes) => bytes >= 1024L * 1024 * 1024
        ? $"{bytes / (1024d * 1024 * 1024):F2} GiB"
        : $"{bytes / (1024d * 1024):F0} MiB";

    [RelayCommand]
    public void LoadConfiguration()
    {
        try
        {
            string basePath = AppContext.BaseDirectory;
            string configPath = Path.Combine(basePath, "appsettings.json");

            if (!File.Exists(configPath))
            {
                configPath = Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json");
            }

            if (!File.Exists(configPath))
            {
                ConfigStatusMessage = "Arquivo appsettings.json não encontrado!";
                return;
            }

            var builder = new ConfigurationBuilder()
                .SetBasePath(Path.GetDirectoryName(configPath)!)
                .AddJsonFile(Path.GetFileName(configPath), optional: false, reloadOnChange: true);

            var configRoot = builder.Build();
            var appConfig = new AppConfig();
            configRoot.Bind(appConfig);

            IsCamerasEnabled = appConfig.Cameras.Enabled;
            _weatherSettings = appConfig.Weather;
            _solisSettings = appConfig.Solis;
            UpdateSolisClock();

            // Load Solis details
            SolisApiUrlText = string.IsNullOrEmpty(appConfig.Solis.ApiUrl) ? "https://www.soliscloud.com:13333" : appConfig.Solis.ApiUrl;
            SolisKeyIdText = string.IsNullOrEmpty(appConfig.Solis.KeyId) ? "1300386381677723412" : appConfig.Solis.KeyId;
            SolisInverterModelText = string.IsNullOrEmpty(appConfig.Solis.InverterModel) ? "S6-GR1P6K-S" : appConfig.Solis.InverterModel;
            SolisCurrencyText = string.IsNullOrEmpty(appConfig.Solis.Currency) ? "BRL" : appConfig.Solis.Currency;
            SolisTimeZoneText = $"GMT {appConfig.Solis.TimeZone}";

            _solisTimer.Stop();
            _solisClient?.Dispose();
            _solisClient = null;
            _inverterSn = null;
            SolisErrorMessage = string.Empty;
            if (string.IsNullOrWhiteSpace(_solisSettings.ApiUrl) ||
                string.IsNullOrWhiteSpace(_solisSettings.KeyId) ||
                string.IsNullOrWhiteSpace(_solisSettings.KeySecret))
            {
                SolisStateText = "Configuração incompleta";
            }
            else
            {
                _solisClient = new SolisApiClient(_solisSettings);
                _solisTimer.Start();
                _ = RefreshSolisCoreAsync(); // Uma consulta inicial, inclusive fora da janela de geração.
            }

            // Load Weather settings
            WeatherProviderText = string.IsNullOrEmpty(appConfig.Weather.Provider) ? "OpenMeteo" : appConfig.Weather.Provider;
            WeatherLocationText = string.IsNullOrEmpty(appConfig.Weather.Timezone) ? "America/Cuiaba" : appConfig.Weather.Timezone;

            // Dispose existing camera stream services if reloading
            PrimaryCamera?.Dispose();
            SecondaryCamera?.Dispose();

            // Initialize Primary Camera (70% width left column, height 410px)
            var primaryItem = appConfig.Cameras.Primary;
            PrimaryCamera = new CameraViewModel(
                string.IsNullOrEmpty(primaryItem.Name) ? "Câmera Principal" : primaryItem.Name,
                primaryItem.RtspUrl,
                primaryItem.Height > 0 ? primaryItem.Height : 410);

            PrimaryCamera.OnToggleExpandRequested += HandleCameraExpandRequested;

            // Initialize Secondary Camera (30% width right column, height 180px)
            var secondaryItem = appConfig.Cameras.Secondary;
            SecondaryCamera = new CameraViewModel(
                string.IsNullOrEmpty(secondaryItem.Name) ? "Câmera Secundária" : secondaryItem.Name,
                secondaryItem.RtspUrl,
                secondaryItem.Height > 0 ? secondaryItem.Height : 180);

            SecondaryCamera.OnToggleExpandRequested += HandleCameraExpandRequested;

            ConfigStatusMessage = "Configurações carregadas com sucesso.";

            if (IsCamerasEnabled)
            {
                PrimaryCamera.StartStream();
                SecondaryCamera.StartStream();
            }

            // Fetch initial weather data & start 30-minute timer
            _weatherTimer.Stop();
            _weatherTimer.Start();
            _ = FetchWeatherAsync();
        }
        catch (Exception ex)
        {
            ConfigStatusMessage = $"Erro ao carregar appsettings.json: {ex.Message}";
        }
    }

    [RelayCommand]
    public async Task FetchWeatherAsync()
    {
        IsWeatherLoading = true;
        WeatherErrorMessage = string.Empty;

        try
        {
            IWeatherService weatherService = WeatherServiceFactory.Create(_weatherSettings);
            WeatherData data = await weatherService.GetWeatherAsync(
                _weatherSettings.Latitude != 0 ? _weatherSettings.Latitude : -15.5989,
                _weatherSettings.Longitude != 0 ? _weatherSettings.Longitude : -56.0949,
                _weatherSettings.Timezone);

            WeatherProviderText = data.ProviderName;
            WeatherLocationText = data.LocationName;

            WeatherCurrentTempText = $"{data.Current.Temperature:F1} °C";
            WeatherCurrentHumidityText = $"{data.Current.Humidity}%";
            WeatherConditionText = data.Current.ConditionText;
            WeatherIconEmoji = data.Current.IconEmoji;
            WeatherLastUpdatedText = $"Atualizado às {data.LastUpdated:HH:mm} (30 min auto)";

            ForecastDays.Clear();
            foreach (var day in data.Forecast)
            {
                ForecastDays.Add(day);
            }
        }
        catch (Exception ex)
        {
            // If primary provider (e.g. OpenWeather without key) fails, attempt fallback to OpenMeteo
            if (!_weatherSettings.Provider.Equals("OpenMeteo", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var fallbackService = new OpenMeteoService();
                    WeatherData fallbackData = await fallbackService.GetWeatherAsync(
                        _weatherSettings.Latitude != 0 ? _weatherSettings.Latitude : -15.5989,
                        _weatherSettings.Longitude != 0 ? _weatherSettings.Longitude : -56.0949,
                        _weatherSettings.Timezone);

                    WeatherProviderText = "OpenMeteo (Fallback)";
                    WeatherLocationText = fallbackData.LocationName;
                    WeatherCurrentTempText = $"{fallbackData.Current.Temperature:F1} °C";
                    WeatherCurrentHumidityText = $"{fallbackData.Current.Humidity}%";
                    WeatherConditionText = fallbackData.Current.ConditionText;
                    WeatherIconEmoji = fallbackData.Current.IconEmoji;
                    WeatherLastUpdatedText = $"Atualizado às {fallbackData.LastUpdated:HH:mm} (Fallback)";

                    ForecastDays.Clear();
                    foreach (var day in fallbackData.Forecast)
                    {
                        ForecastDays.Add(day);
                    }
                    return;
                }
                catch { }
            }

            WeatherErrorMessage = $"Erro ao obter clima: {ex.Message}";
        }
        finally
        {
            IsWeatherLoading = false;
        }
    }

    [RelayCommand]
    public Task RefreshSolisAsync() => RefreshSolisCoreAsync();

    private async Task RefreshSolisCoreAsync()
    {
        if (_solisClient is null || IsSolisLoading)
            return;

        IsSolisLoading = true;
        SolisErrorMessage = string.Empty;
        SolisStateText = "Consultando SolisCloud...";

        try
        {
            _inverterSn ??= await _solisClient.ResolveInverterSnAsync();
            var localNow = GetSolisLocalNow();
            var detailTask = _solisClient.GetInverterDetailAsync(_inverterSn);
            var dayTask = _solisClient.GetInverterDayAsync(
                _inverterSn,
                DateOnly.FromDateTime(localNow),
                _solisSettings.TimeZone);
            await Task.WhenAll(detailTask, dayTask);
            var detail = await detailTask;
            var dayPoints = await dayTask;

            SolisInverterSnText = _inverterSn;
            SolisInverterModelText = detail.ProductModel ?? detail.Model ?? SolisInverterModelText;
            SolisStateText = detail.State switch
            {
                1 => "Online",
                2 => "Offline",
                3 => "Alarme",
                _ => detail.State?.ToString() ?? "Sem estado"
            };
            SolisEnergyTodayText = FormatUnit(detail.EToday, detail.ETodayStr ?? "kWh");
            ApplySolarGenerationProgress(detail.EToday);
            SolisPowerText = FormatUnit(detail.Pac, detail.PacStr ?? "W");
            SolisAcCurrentText = FormatUnit(detail.IAc1, detail.IAc1Str ?? "A");
            SolisTemperatureText = FormatUnit(detail.InverterTemperature, detail.InverterTemperatureUnit ?? "°C");
            SolisIgbtTemperatureText = ResolveIgbtTemperature(detail);
            SolisPv1PowerText = FormatUnit(detail.Pow1, detail.Pow1Str ?? "W");
            SolisPv1DetailText = $"{FormatUnit(detail.UPv1, detail.UPv1Str ?? "V")} · {FormatUnit(detail.IPv1, detail.IPv1Str ?? "A")}";
            SolisPv2PowerText = FormatUnit(detail.Pow2, detail.Pow2Str ?? "W");
            SolisPv2DetailText = $"{FormatUnit(detail.UPv2, detail.UPv2Str ?? "V")} · {FormatUnit(detail.IPv2, detail.IPv2Str ?? "A")}";
            SolisLastUpdatedText = detail.TimeStr ?? DateTime.Now.ToString("HH:mm:ss");
            ApplySolarDayPoints(dayPoints);
        }
        catch (Exception ex)
        {
            SolisStateText = "Erro na consulta";
            SolisErrorMessage = $"Solis: {ex.Message}";
        }
        finally
        {
            IsSolisLoading = false;
        }
    }

    private static string FormatUnit(double? value, string unit) =>
        value is null ? "-" : $"{value.Value:0.##} {unit}";

    private void ApplySolarGenerationProgress(double? energyToday)
    {
        var generatedKwh = Math.Max(0, energyToday ?? 0);
        var targetPercent = generatedKwh / SolarGenerationTargetKwh * 100d;
        SolisGenerationProgressText =
            $"{generatedKwh:0.##} kWh · {targetPercent:0}% da meta de {SolarGenerationTargetKwh} kWh";

        SolarGenerationProgressSegments.Clear();
        for (var kwh = 1; kwh <= SolarGenerationDisplayMaxKwh; kwh++)
        {
            var isFilled = generatedKwh >= kwh;
            var isAboveTarget = kwh > SolarGenerationTargetKwh;
            SolarGenerationProgressSegments.Add(new SolarProgressSegment(
                isFilled
                    ? isAboveTarget ? "#F59E0B" : "#4ADE80"
                    : "#334155",
                kwh == SolarGenerationTargetKwh));
        }
    }

    private void ApplySolarDayPoints(IEnumerable<InverterDayPoint> dayPoints)
    {
        var points = dayPoints
            .Where(point => point.Pac is not null && TryParseHourDecimal(ExtractHour(point.TimeStr), out var hour) &&
                            hour >= ChartStartHour && hour <= ChartEndHour)
            .Select(point => new
            {
                Time = ExtractHour(point.TimeStr),
                Hour = ParseHourDecimal(ExtractHour(point.TimeStr)),
                PowerWatts = Math.Max(0, point.Pac!.Value)
            })
            .OrderBy(point => point.Hour)
            .ToList();

        _solarGenerationValues.Clear();
        foreach (var point in points)
            _solarGenerationValues.Add(new ObservablePoint(point.Hour, Math.Min(ChartMaxPowerWatts, point.PowerWatts)));


        var atOrAbove6Kw = points.Where(point => point.PowerWatts >= ChartMaxPowerWatts).ToList();
        SolisFirst6KwText = atOrAbove6Kw.Count == 0 ? "—" : atOrAbove6Kw[0].Time;
        SolisLast6KwText = atOrAbove6Kw.Count == 0 ? "—" : atOrAbove6Kw[^1].Time;
    }

    private bool IsSolisRefreshWindow()
    {
        var currentTime = GetSolisLocalNow().TimeOfDay;
        return currentTime >= TimeSpan.FromHours(ChartStartHour) && currentTime < TimeSpan.FromHours(ChartEndHour);
    }

    private DateTime GetSolisLocalNow() => DateTime.UtcNow.AddHours(_solisSettings.TimeZone);

    private void UpdateSolisClock()
    {
        var localNow = GetSolisLocalNow();
        SolisLocalDateText = localNow.ToString("yyyy-MM-dd");
        SolisLocalTimeText = localNow.ToString("HH:mm:ss");
    }

    private static string ResolveIgbtTemperature(InverterDetail detail)
    {
        if (detail.Temp is null)
            return "N/D";

        var value = detail.Temp.Value > 100 ? (detail.Temp.Value - 32d) * 5d / 9d : detail.Temp.Value;
        var name = string.IsNullOrWhiteSpace(detail.TempName) ? "IGBT" : detail.TempName;
        return $"{value:0.0} °C ({name})";
    }

    private static string ExtractHour(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var time = parts.Length > 1 ? parts[^1] : parts[0];
        return time.Length >= 5 ? time[..5] : time;
    }

    private static bool TryParseHourDecimal(string value, out double hour)
    {
        hour = 0;
        if (!TimeOnly.TryParse(value, out var time))
            return false;

        hour = time.Hour + time.Minute / 60d + time.Second / 3600d;
        return true;
    }

    private static double ParseHourDecimal(string value)
    {
        return TryParseHourDecimal(value, out var hour) ? hour : 0;
    }

    private void HandleCameraExpandRequested(CameraViewModel camera)
    {
        if (camera.IsExpanded)
        {
            FocusedCamera = camera;
            IsDualView = false;

            PrimaryCamera?.UpdateViewHeight(false);
            SecondaryCamera?.UpdateViewHeight(false);

            if (camera == PrimaryCamera && SecondaryCamera != null)
                SecondaryCamera.IsExpanded = false;
            else if (camera == SecondaryCamera && PrimaryCamera != null)
                PrimaryCamera.IsExpanded = false;
        }
        else
        {
            FocusedCamera = null;
            IsDualView = true;

            PrimaryCamera?.UpdateViewHeight(true);
            SecondaryCamera?.UpdateViewHeight(true);
        }
    }

    [RelayCommand]
    public void RestoreDualView()
    {
        if (PrimaryCamera != null) PrimaryCamera.IsExpanded = false;
        if (SecondaryCamera != null) SecondaryCamera.IsExpanded = false;

        PrimaryCamera?.UpdateViewHeight(true);
        SecondaryCamera?.UpdateViewHeight(true);

        FocusedCamera = null;
        IsDualView = true;
    }

    public void Dispose()
    {
        _clockTimer.Stop();
        _weatherTimer.Stop();
        _solisTimer.Stop();
        _solisClient?.Dispose();
        PrimaryCamera?.Dispose();
        SecondaryCamera?.Dispose();
    }
}

public sealed class SolarProgressSegment
{
    public SolarProgressSegment(string color, bool isTargetMarker)
    {
        Fill = new SolidColorBrush(Color.Parse(color));
        Border = isTargetMarker
            ? new SolidColorBrush(Color.Parse("#F59E0B"))
            : Brushes.Transparent;
        BorderThickness = isTargetMarker ? new Thickness(1.5) : new Thickness(0);
    }

    public IBrush Fill { get; }
    public IBrush Border { get; }
    public Thickness BorderThickness { get; }
}
