namespace SolisDashboard.Configuration;

public sealed class AppOptions
{
    public SolisOptions Solis { get; set; } = new();
    public WeatherOptions Weather { get; set; } = new();
    public CamerasOptions Cameras { get; set; } = new();
}

public sealed class SolisOptions
{
    public string ApiUrl { get; set; } = "https://www.soliscloud.com:13333";
    public string KeyId { get; set; } = "";
    public string KeySecret { get; set; } = "";
    public string InverterSn { get; set; } = "";
    public string InverterModel { get; set; } = "S6-GR1P6K-S";
    public int TimeZone { get; set; } = -3;
    public string Currency { get; set; } = "BRL";
}

public sealed class WeatherOptions
{
    /// <summary>Provider ativo: OpenMeteo | OpenWeather (free: Current + Forecast 2.5)</summary>
    public string Provider { get; set; } = "OpenMeteo";
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string Timezone { get; set; } = "America/Sao_Paulo";
    /// <summary>Intervalo de atualização do clima em minutos.</summary>
    public int RefreshMinutes { get; set; } = 10;
    public OpenWeatherOptions OpenWeather { get; set; } = new();
}

/// <summary>OpenWeather Map free tier (não usa One Call 3.0/4.0).</summary>
public sealed class OpenWeatherOptions
{
    public string ApiKey { get; set; } = "";
    public string Units { get; set; } = "metric";
    public string Lang { get; set; } = "pt_br";
}

/// <summary>Câmeras RTSP via LibVLC (VideoView).</summary>
public sealed class CamerasOptions
{
    public bool Enabled { get; set; } = true;
    /// <summary>
    /// Linux: pasta que contém libvlc.so (ex. /snap/vlc/current/usr/lib/x86_64-linux-gnu).
    /// Não use /snap/vlc/current/usr/lib genérico com LD_LIBRARY_PATH — quebra o sistema.
    /// </summary>
    public string LibVlcPath { get; set; } = "";
    public CameraOptions Primary { get; set; } = new();
    public CameraOptions Secondary { get; set; } = new();
}

public sealed class CameraOptions
{
    public string Name { get; set; } = "Câmera";
    /// <summary>URL RTSP completa da câmera.</summary>
    public string RtspUrl { get; set; } = "";
    /// <summary>Altura da área de vídeo em pixels (layout Auto; weather/Solis ocupam o restante).</summary>
    public double Height { get; set; } = 280;
}
