using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dashboard.Models;
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

    // WEATHER PANEL PROPERTIES (30% Right Field - Middle)
    [ObservableProperty]
    private string _weatherProviderText = "OpenMeteo";

    [ObservableProperty]
    private string _weatherLocationText = "Cuiabá - MT";

    [ObservableProperty]
    private string _weatherTimezoneText = "America/Cuiaba";

    [ObservableProperty]
    private string _weatherCoordsText = "-15.5989, -56.0949";

    // SYSTEM MONITOR PROPERTIES (30% Right Field - Bottom)
    [ObservableProperty]
    private string _sysMemoryText = "0 MB";

    [ObservableProperty]
    private string _sysUptimeText = "00:00:00";

    [ObservableProperty]
    private string _sysThreadsText = "0 Threads";

    [ObservableProperty]
    private string _sysOsText = "Linux x86_64";

    private readonly DispatcherTimer _clockTimer;
    private readonly DateTime _startTime = DateTime.Now;

    public MainWindowViewModel()
    {
        SysOsText = $"{RuntimeInformation.OSDescription} ({RuntimeInformation.ProcessArchitecture})";

        _clockTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _clockTimer.Tick += OnClockTick;
        _clockTimer.Start();

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
    }

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

            // Load Solis configuration details
            SolisApiUrlText = string.IsNullOrEmpty(appConfig.Solis.ApiUrl) ? "https://www.soliscloud.com:13333" : appConfig.Solis.ApiUrl;
            SolisKeyIdText = string.IsNullOrEmpty(appConfig.Solis.KeyId) ? "1300386381677723412" : appConfig.Solis.KeyId;
            SolisInverterModelText = string.IsNullOrEmpty(appConfig.Solis.InverterModel) ? "S6-GR1P6K-S" : appConfig.Solis.InverterModel;
            SolisCurrencyText = string.IsNullOrEmpty(appConfig.Solis.Currency) ? "BRL" : appConfig.Solis.Currency;
            SolisTimeZoneText = $"GMT {appConfig.Solis.TimeZone}";

            // Load Weather configuration details
            WeatherProviderText = string.IsNullOrEmpty(appConfig.Weather.Provider) ? "OpenMeteo" : appConfig.Weather.Provider;
            WeatherTimezoneText = string.IsNullOrEmpty(appConfig.Weather.Timezone) ? "America/Cuiaba" : appConfig.Weather.Timezone;
            WeatherCoordsText = $"{appConfig.Weather.Latitude:F4}, {appConfig.Weather.Longitude:F4}";

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

            ConfigStatusMessage = "Configurações do appsettings.json carregadas com sucesso.";

            if (IsCamerasEnabled)
            {
                PrimaryCamera.StartStream();
                SecondaryCamera.StartStream();
            }
        }
        catch (Exception ex)
        {
            ConfigStatusMessage = $"Erro ao carregar appsettings.json: {ex.Message}";
        }
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
        PrimaryCamera?.Dispose();
        SecondaryCamera?.Dispose();
    }
}
