namespace Dashboard.Models;

public class AppConfig
{
    public SolisSettings Solis { get; set; } = new();
    public WeatherSettings Weather { get; set; } = new();
    public CameraSettings Cameras { get; set; } = new();
}

public class SolisSettings
{
    public string ApiUrl { get; set; } = string.Empty;
    public string KeyId { get; set; } = string.Empty;
    public string KeySecret { get; set; } = string.Empty;
    public string InverterSn { get; set; } = string.Empty;
    public string InverterModel { get; set; } = string.Empty;
    public int TimeZone { get; set; } = -3;
    public string Currency { get; set; } = "BRL";
}

public class WeatherSettings
{
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string Timezone { get; set; } = string.Empty;
    public string Provider { get; set; } = "OpenMeteo";
    public OpenWeatherSettings OpenWeather { get; set; } = new();
}

public class OpenWeatherSettings
{
    public string ApiKey { get; set; } = string.Empty;
    public string Units { get; set; } = "metric";
    public string Lang { get; set; } = "pt_br";
}

public class CameraSettings
{
    public bool Enabled { get; set; } = true;
    public CameraItem Primary { get; set; } = new() { Name = "Câmera Principal", Height = 410 };
    public CameraItem Secondary { get; set; } = new() { Name = "Câmera Secundária", Height = 180 };
}

public class CameraItem
{
    public string Name { get; set; } = string.Empty;
    public int Height { get; set; } = 360;
    public string RtspUrl { get; set; } = string.Empty;
}
