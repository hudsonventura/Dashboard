using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using SolisDashboard.Configuration;
using SolisDashboard.Display;
using SolisDashboard.Models;

namespace SolisDashboard.Services;

/// <summary>
/// OpenWeather free tier: Current Weather + 5 day / 3 hour Forecast (API 2.5).
/// Docs: https://openweathermap.org/current · https://openweathermap.org/forecast5
/// </summary>
public sealed class OpenWeatherMapProvider : IWeatherProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private readonly WeatherOptions _options;

    public OpenWeatherMapProvider(WeatherOptions options)
    {
        _options = options;
        if (string.IsNullOrWhiteSpace(options.OpenWeather.ApiKey))
            throw new InvalidOperationException("Configure Weather:OpenWeather:ApiKey no appsettings.json.");
    }

    public string DisplayName => "OpenWeather";

    public async Task<WeatherSnapshot> GetSnapshotAsync(CancellationToken ct = default)
    {
        var ow = _options.OpenWeather;
        var lat = _options.Latitude.ToString(CultureInfo.InvariantCulture);
        var lon = _options.Longitude.ToString(CultureInfo.InvariantCulture);
        var key = Uri.EscapeDataString(ow.ApiKey.Trim());
        var units = string.IsNullOrWhiteSpace(ow.Units) ? "metric" : ow.Units.Trim();
        var lang = string.IsNullOrWhiteSpace(ow.Lang) ? "pt_br" : ow.Lang.Trim();
        var common =
            $"?lat={lat}&lon={lon}&units={Uri.EscapeDataString(units)}" +
            $"&lang={Uri.EscapeDataString(lang)}&appid={key}";

        var currentUrl = "https://api.openweathermap.org/data/2.5/weather" + common;
        var forecastUrl = "https://api.openweathermap.org/data/2.5/forecast" + common;

        var currentTask = FetchAsync<OwmCurrentResponse>(currentUrl, ct);
        var forecastTask = FetchAsync<OwmForecastResponse>(forecastUrl, ct);
        await Task.WhenAll(currentTask, forecastTask);

        var current = await currentTask;
        var forecast = await forecastTask;

        var weather = current.Weather.FirstOrDefault();
        var condition = string.IsNullOrWhiteSpace(weather?.Description)
            ? WeatherCodes.DescribeOpenWeather(weather?.Id ?? -1)
            : CultureInfo.CurrentCulture.TextInfo.ToTitleCase(weather!.Description!);

        var tzOffset = TimeSpan.FromSeconds(current.Timezone);
        var days = AggregateDaily(forecast.List, tzOffset, units);

        var observedLocal = DateTimeOffset.FromUnixTimeSeconds(current.Dt).ToOffset(tzOffset);

        return new WeatherSnapshot
        {
            ProviderLabel = DisplayName,
            ObservedAt = observedLocal.ToString("yyyy-MM-dd HH:mm"),
            TemperatureC = current.Main?.Temp ?? 0,
            FeelsLikeC = current.Main?.FeelsLike ?? 0,
            Condition = condition,
            IconKey = WeatherCodes.IconKeyFromOpenWeather(weather?.Id ?? -1, weather?.Icon),
            HumidityPercent = current.Main?.Humidity ?? 0,
            WindKmh = ToKmh(current.Wind?.Speed ?? 0, units),
            PrecipitationMm = current.Rain?.OneHour ?? current.Snow?.OneHour ?? 0,
            Days = days
        };
    }

    private static List<WeatherDayForecast> AggregateDaily(
        IReadOnlyList<OwmForecastItem> items,
        TimeSpan tzOffset,
        string units)
    {
        var groups = items
            .GroupBy(i => DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeSeconds(i.Dt).ToOffset(tzOffset).DateTime))
            .OrderBy(g => g.Key)
            .Take(5);

        var days = new List<WeatherDayForecast>();
        foreach (var group in groups)
        {
            var slots = group.ToList();
            // Preferência: horário próximo do meio-dia (representativo do dia).
            var pick = slots
                .OrderBy(s => Math.Abs(DateTimeOffset.FromUnixTimeSeconds(s.Dt).ToOffset(tzOffset).Hour - 12))
                .First();
            var w = pick.Weather.FirstOrDefault();
            var dayCondition = string.IsNullOrWhiteSpace(w?.Description)
                ? WeatherCodes.DescribeOpenWeather(w?.Id ?? -1)
                : CultureInfo.CurrentCulture.TextInfo.ToTitleCase(w!.Description!);

            days.Add(new WeatherDayForecast
            {
                Date = group.Key,
                Condition = dayCondition,
                IconKey = WeatherCodes.IconKeyFromOpenWeather(w?.Id ?? -1, w?.Icon),
                TempMaxC = slots.Max(s => s.Main?.TempMax ?? s.Main?.Temp ?? 0),
                TempMinC = slots.Min(s => s.Main?.TempMin ?? s.Main?.Temp ?? 0),
                PrecipitationMm = slots.Sum(s => s.Rain?.ThreeHour ?? s.Snow?.ThreeHour ?? 0),
                WindKmh = ToKmh(slots.Max(s => s.Wind?.Speed ?? 0), units)
            });
        }

        return days;
    }

    private async Task<T> FetchAsync<T>(string url, CancellationToken ct)
    {
        using var response = await _http.GetAsync(url, ct);
        var raw = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"OpenWeather HTTP {(int)response.StatusCode}: {raw}");

        return JsonSerializer.Deserialize<T>(raw, JsonOptions)
               ?? throw new InvalidOperationException("Resposta vazia do OpenWeather.");
    }

    private static double ToKmh(double windSpeed, string units) =>
        units.Equals("imperial", StringComparison.OrdinalIgnoreCase)
            ? windSpeed * 1.60934
            : windSpeed * 3.6; // metric / standard: m/s → km/h

    public void Dispose() => _http.Dispose();

    private sealed class OwmCurrentResponse
    {
        [JsonPropertyName("dt")]
        public long Dt { get; set; }

        [JsonPropertyName("timezone")]
        public int Timezone { get; set; }

        [JsonPropertyName("main")]
        public OwmMain? Main { get; set; }

        [JsonPropertyName("wind")]
        public OwmWind? Wind { get; set; }

        [JsonPropertyName("rain")]
        public OwmPrecip1h? Rain { get; set; }

        [JsonPropertyName("snow")]
        public OwmPrecip1h? Snow { get; set; }

        [JsonPropertyName("weather")]
        public List<OwmWeatherInfo> Weather { get; set; } = [];
    }

    private sealed class OwmForecastResponse
    {
        [JsonPropertyName("list")]
        public List<OwmForecastItem> List { get; set; } = [];
    }

    private sealed class OwmForecastItem
    {
        [JsonPropertyName("dt")]
        public long Dt { get; set; }

        [JsonPropertyName("main")]
        public OwmMain? Main { get; set; }

        [JsonPropertyName("wind")]
        public OwmWind? Wind { get; set; }

        [JsonPropertyName("rain")]
        public OwmPrecip3h? Rain { get; set; }

        [JsonPropertyName("snow")]
        public OwmPrecip3h? Snow { get; set; }

        [JsonPropertyName("weather")]
        public List<OwmWeatherInfo> Weather { get; set; } = [];
    }

    private sealed class OwmMain
    {
        [JsonPropertyName("temp")]
        public double Temp { get; set; }

        [JsonPropertyName("feels_like")]
        public double FeelsLike { get; set; }

        [JsonPropertyName("temp_min")]
        public double TempMin { get; set; }

        [JsonPropertyName("temp_max")]
        public double TempMax { get; set; }

        [JsonPropertyName("humidity")]
        public int Humidity { get; set; }
    }

    private sealed class OwmWind
    {
        [JsonPropertyName("speed")]
        public double Speed { get; set; }
    }

    private sealed class OwmPrecip1h
    {
        [JsonPropertyName("1h")]
        public double? OneHour { get; set; }
    }

    private sealed class OwmPrecip3h
    {
        [JsonPropertyName("3h")]
        public double? ThreeHour { get; set; }
    }

    private sealed class OwmWeatherInfo
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("icon")]
        public string? Icon { get; set; }
    }
}
