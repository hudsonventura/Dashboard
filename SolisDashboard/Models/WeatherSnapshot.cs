namespace SolisDashboard.Models;

/// <summary>Snapshot unificado de clima (Open-Meteo ou OpenWeather).</summary>
public sealed class WeatherSnapshot
{
    public string ProviderLabel { get; init; } = "Weather";
    public string? ObservedAt { get; init; }
    public double TemperatureC { get; init; }
    public double FeelsLikeC { get; init; }
    public string Condition { get; init; } = "-";
    public string IconKey { get; init; } = "unknown";
    public int HumidityPercent { get; init; }
    public double WindKmh { get; init; }
    public double PrecipitationMm { get; init; }
    public IReadOnlyList<WeatherDayForecast> Days { get; init; } = [];
}

public sealed class WeatherDayForecast
{
    public DateOnly Date { get; init; }
    public string Condition { get; init; } = "-";
    public string IconKey { get; init; } = "unknown";
    public double TempMaxC { get; init; }
    public double TempMinC { get; init; }
    public double PrecipitationMm { get; init; }
    public double WindKmh { get; init; }
}
