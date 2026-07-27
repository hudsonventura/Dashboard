using System.Text.Json.Serialization;

namespace SolisDashboard.Models;

public sealed class OpenMeteoForecast
{
    [JsonPropertyName("latitude")]
    public double Latitude { get; set; }

    [JsonPropertyName("longitude")]
    public double Longitude { get; set; }

    [JsonPropertyName("timezone")]
    public string? Timezone { get; set; }

    [JsonPropertyName("current")]
    public CurrentWeather? Current { get; set; }

    [JsonPropertyName("daily")]
    public DailyWeather? Daily { get; set; }
}

public sealed class CurrentWeather
{
    [JsonPropertyName("time")]
    public string? Time { get; set; }

    [JsonPropertyName("temperature_2m")]
    public double Temperature2m { get; set; }

    [JsonPropertyName("relative_humidity_2m")]
    public int RelativeHumidity2m { get; set; }

    [JsonPropertyName("apparent_temperature")]
    public double ApparentTemperature { get; set; }

    [JsonPropertyName("weather_code")]
    public int WeatherCode { get; set; }

    [JsonPropertyName("wind_speed_10m")]
    public double WindSpeed10m { get; set; }

    [JsonPropertyName("precipitation")]
    public double Precipitation { get; set; }

    [JsonPropertyName("is_day")]
    public int IsDay { get; set; }
}

public sealed class DailyWeather
{
    [JsonPropertyName("time")]
    public List<string> Time { get; set; } = [];

    [JsonPropertyName("weather_code")]
    public List<int> WeatherCode { get; set; } = [];

    [JsonPropertyName("temperature_2m_max")]
    public List<double> Temperature2mMax { get; set; } = [];

    [JsonPropertyName("temperature_2m_min")]
    public List<double> Temperature2mMin { get; set; } = [];

    [JsonPropertyName("precipitation_sum")]
    public List<double> PrecipitationSum { get; set; } = [];

    [JsonPropertyName("precipitation_probability_max")]
    public List<int> PrecipitationProbabilityMax { get; set; } = [];

    [JsonPropertyName("wind_speed_10m_max")]
    public List<double> WindSpeed10mMax { get; set; } = [];
}
