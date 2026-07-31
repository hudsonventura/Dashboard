using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Dashboard.Models;

namespace Dashboard.Services;

public class OpenMeteoService : IWeatherService
{
    private static readonly HttpClient HttpClient = new HttpClient();

    public string ProviderName => "OpenMeteo";

    public async Task<WeatherData> GetWeatherAsync(double latitude, double longitude, string timezone, CancellationToken ct = default)
    {
        string encodedTimezone = Uri.EscapeDataString(string.IsNullOrWhiteSpace(timezone) ? "America/Cuiaba" : timezone);
        string url = string.Format(CultureInfo.InvariantCulture,
            "https://api.open-meteo.com/v1/forecast?latitude={0}&longitude={1}&current=temperature_2m,relative_humidity_2m,weather_code&daily=weather_code,temperature_2m_max,temperature_2m_min&timezone={2}",
            latitude, longitude, encodedTimezone);

        var response = await HttpClient.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        string json = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var weatherData = new WeatherData
        {
            LocationName = "Cuiabá",
            ProviderName = "OpenMeteo",
            LastUpdated = DateTime.Now
        };

        if (root.TryGetProperty("current", out var currentEl))
        {
            double temp = currentEl.GetProperty("temperature_2m").GetDouble();
            int humidity = currentEl.GetProperty("relative_humidity_2m").GetInt32();
            int code = currentEl.GetProperty("weather_code").GetInt32();

            var (condText, iconName) = MapWmoCode(code);
            var localHour = DateTime.Now.Hour;
            var isNight = localHour < 6 || localHour >= 18;
            weatherData.Current = new CurrentWeather
            {
                Temperature = temp,
                Humidity = humidity,
                ConditionText = condText,
                Icon = MeteoconAssets.GetBitmap(MeteoconAssets.ResolveWeatherIconName(iconName, isNight))
            };
        }

        if (root.TryGetProperty("daily", out var dailyEl))
        {
            var times = dailyEl.GetProperty("time");
            var maxTemps = dailyEl.GetProperty("temperature_2m_max");
            var minTemps = dailyEl.GetProperty("temperature_2m_min");
            var codes = dailyEl.GetProperty("weather_code");

            int count = times.GetArrayLength();
            // Get next 4 days (start from index 1 for tomorrow, or index 1..4)
            int forecastCount = 0;
            var culture = CultureInfo.GetCultureInfo("en-US");

            for (int i = 1; i < count && forecastCount < 4; i++)
            {
                string dateStr = times[i].GetString() ?? string.Empty;
                if (DateTime.TryParse(dateStr, out DateTime date))
                {
                    double max = maxTemps[i].GetDouble();
                    double min = minTemps[i].GetDouble();
                    int code = codes[i].GetInt32();
                    var (condText, iconName) = MapWmoCode(code);

                    string dayOfWeek = culture.DateTimeFormat.GetAbbreviatedDayName(date.DayOfWeek);
                    dayOfWeek = char.ToUpper(dayOfWeek[0]) + dayOfWeek.Substring(1);

                    weatherData.Forecast.Add(new DailyForecast
                    {
                        Date = date,
                        DayOfWeek = dayOfWeek,
                        TempMax = max,
                        TempMin = min,
                        ConditionText = condText,
                        Icon = MeteoconAssets.GetBitmap(iconName)
                    });

                    forecastCount++;
                }
            }
        }

        return weatherData;
    }

    private static (string ConditionText, string IconName) MapWmoCode(int code)
    {
        return code switch
        {
            0 => ("Ensolarado", "clear-day"),
            1 or 2 => ("Parcialmente Nublado", "partly-cloudy-day"),
            3 => ("Encoberto", "overcast"),
            45 or 48 => ("Nevoeiro", "fog"),
            51 or 53 or 55 => ("Garoa", "drizzle"),
            61 or 63 or 65 or 80 or 81 or 82 => ("Chuva", "rain"),
            71 or 73 or 75 => ("Neve", "snow"),
            95 or 96 or 99 => ("Tempestade", "thunderstorms"),
            _ => ("Limpo", "clear-day")
        };
    }
}
