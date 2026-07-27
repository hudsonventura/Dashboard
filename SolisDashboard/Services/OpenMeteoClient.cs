using System.Globalization;
using System.Text.Json;
using SolisDashboard.Configuration;
using SolisDashboard.Models;

namespace SolisDashboard.Services;

public sealed class OpenMeteoClient : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private readonly WeatherOptions _options;

    public OpenMeteoClient(WeatherOptions options)
    {
        _options = options;
    }

    public async Task<OpenMeteoForecast> GetForecastAsync(CancellationToken ct = default)
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

        return JsonSerializer.Deserialize<OpenMeteoForecast>(raw, JsonOptions)
               ?? throw new InvalidOperationException("Resposta vazia do Open-Meteo.");
    }

    public void Dispose() => _http.Dispose();
}
