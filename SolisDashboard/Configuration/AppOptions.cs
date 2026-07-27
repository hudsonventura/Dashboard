namespace SolisDashboard.Configuration;

public sealed class AppOptions
{
    public SolisOptions Solis { get; set; } = new();
    public WeatherOptions Weather { get; set; } = new();
    public ShinobiOptions Shinobi { get; set; } = new();
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
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string Timezone { get; set; } = "America/Sao_Paulo";
}

/// <summary>
/// Câmeras Shinobi. URLs conforme docs: https://docs.shinobi.video/api/get-streams
/// JPEG: {BaseUrl}/{ApiKey}/jpeg/{GroupKey}/{MonitorId}/s.jpg
/// </summary>
public sealed class ShinobiOptions
{
    public bool Enabled { get; set; } = true;
    public string BaseUrl { get; set; } = "http://127.0.0.1:8080";
    public string ApiKey { get; set; } = "";
    public string GroupKey { get; set; } = "";
    /// <summary>Intervalo de atualização dos frames (segundos).</summary>
    public double SnapshotRefreshSeconds { get; set; } = 1.5;
    public ShinobiCameraOptions Primary { get; set; } = new();
    public ShinobiCameraOptions Secondary { get; set; } = new();
}

public sealed class ShinobiCameraOptions
{
    public string MonitorId { get; set; } = "";
    public string Name { get; set; } = "Câmera";
    /// <summary>URL RTSP completa. Se preenchida, tem prioridade sobre JPEG/Shinobi API.</summary>
    public string RtspUrl { get; set; } = "";
    /// <summary>Altura da visualização em pixels. Ajuste aqui para deixar grande ou pequena.</summary>
    public double Height { get; set; } = 280;
}
