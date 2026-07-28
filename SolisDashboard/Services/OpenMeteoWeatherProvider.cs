using System.Globalization;
using System.Text.Json;
using SolisDashboard.Configuration;
using SolisDashboard.Display;
using SolisDashboard.Models;

namespace SolisDashboard.Services;

public sealed class OpenMeteoWeatherProvider : IWeatherProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private readonly WeatherOptions _options;

    public OpenMeteoWeatherProvider(WeatherOptions options) => _options = options;

    public string DisplayName => "Open-Meteo";

    public async Task<WeatherSnapshot> GetSnapshotAsync(CancellationToken ct = default)
    {
        var url =
            "https://api.open-meteo.com/v1/forecast" +
            $"?latitude={_options.Latitude.ToString(CultureInfo.InvariantCulture)}" +
            $"&longitude={_options.Longitude.ToString(CultureInfo.InvariantCulture)}" +
            "&current=temperature_2m,relative_humidity_2m,apparent_temperature,weather_code,wind_speed_10m,precipitation,is_day" +
            "&daily=weather_code,temperature_2m_max,temperature_2m_min,precipitation_sum,precipitation_probability_max,wind_speed_10m_max" +
            "&forecast_days=5" +
            $"&timezone={Uri.EscapeDataString(_options.Timezone)}" +
            "&temperature_unit=celsius" +
            "&wind_speed_unit=kmh" +
            "&precipitation_unit=mm";

        using var response = await _http.GetAsync(url, ct);
        var raw = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Open-Meteo HTTP {(int)response.StatusCode}: {raw}");

        var forecast = JsonSerializer.Deserialize<OpenMeteoForecast>(raw, JsonOptions)
                       ?? throw new InvalidOperationException("Resposta vazia do Open-Meteo.");

        var current = forecast.Current;
        var days = new List<WeatherDayForecast>();
        if (forecast.Daily is { } daily)
        {
            var count = Math.Min(5, daily.Time.Count);
            for (var i = 0; i < count; i++)
            {
                var code = i < daily.WeatherCode.Count ? daily.WeatherCode[i] : -1;
                var date = DateOnly.TryParse(daily.Time[i], out var d) ? d : DateOnly.FromDateTime(DateTime.Today.AddDays(i));
                days.Add(new WeatherDayForecast
                {
                    Date = date,
                    Condition = WeatherCodes.Describe(code),
                    IconKey = WeatherCodes.IconKey(code),
                    TempMinC = i < daily.Temperature2mMin.Count ? daily.Temperature2mMin[i] : 0,
                    TempMaxC = i < daily.Temperature2mMax.Count ? daily.Temperature2mMax[i] : 0,
                    PrecipitationMm = i < daily.PrecipitationSum.Count ? daily.PrecipitationSum[i] : 0,
                    WindKmh = i < daily.WindSpeed10mMax.Count ? daily.WindSpeed10mMax[i] : 0
                });
            }
        }

        return new WeatherSnapshot
        {
            ProviderLabel = DisplayName,
            ObservedAt = current?.Time,
            TemperatureC = current?.Temperature2m ?? 0,
            FeelsLikeC = current?.ApparentTemperature ?? 0,
            Condition = WeatherCodes.Describe(current?.WeatherCode ?? -1),
            IconKey = WeatherCodes.IconKey(current?.WeatherCode ?? -1),
            HumidityPercent = current?.RelativeHumidity2m ?? 0,
            WindKmh = current?.WindSpeed10m ?? 0,
            PrecipitationMm = current?.Precipitation ?? 0,
            Days = days
        };
    }

    public void Dispose() => _http.Dispose();
}
