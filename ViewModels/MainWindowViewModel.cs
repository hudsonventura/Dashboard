using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dashboard.Models;
using Dashboard.Services;
using Microsoft.Extensions.Configuration;

namespace Dashboard.ViewModels;

public partial class MainWindowViewModel : ObservableObject, IDisposable
{
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
    private string _solisPowerText = "-";

    [ObservableProperty]
    private string _solisTemperatureText = "-";

    [ObservableProperty]
    private string _solisLastUpdatedText = "-";

    [ObservableProperty]
    private string _solisErrorMessage = string.Empty;

    [ObservableProperty]
    private bool _isSolisLoading;

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
        _solisTimer.Tick += async (s, e) => await RefreshSolisAsync();

        LoadConfiguration();
    }

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
                _ = RefreshSolisAsync();
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
    public async Task RefreshSolisAsync()
    {
        if (_solisClient is null || IsSolisLoading)
            return;

        IsSolisLoading = true;
        SolisErrorMessage = string.Empty;
        SolisStateText = "Consultando SolisCloud...";

        try
        {
            _inverterSn ??= await _solisClient.ResolveInverterSnAsync();
            var detail = await _solisClient.GetInverterDetailAsync(_inverterSn);

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
            SolisPowerText = FormatUnit(detail.Pac, detail.PacStr ?? "W");
            SolisTemperatureText = FormatUnit(detail.InverterTemperature, detail.InverterTemperatureUnit ?? "°C");
            SolisLastUpdatedText = detail.TimeStr ?? DateTime.Now.ToString("HH:mm:ss");
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
