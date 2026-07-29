using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Dashboard.Models;

namespace Dashboard.Services;

public class OpenWeatherService : IWeatherService
{
    private static readonly HttpClient HttpClient = new HttpClient();
    private readonly string _apiKey;

    public string ProviderName => "OpenWeather";

    public OpenWeatherService(string apiKey)
    {
        _apiKey = apiKey;
    }

    public async Task<WeatherData> GetWeatherAsync(double latitude, double longitude, string timezone, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_apiKey) || _apiKey.Contains("SUA_API_KEY"))
        {
            throw new InvalidOperationException("API Key do OpenWeather não configurada no appsettings.json.");
        }

        string currentUrl = string.Format(CultureInfo.InvariantCulture,
            "https://api.openweathermap.org/data/2.5/weather?lat={0}&lon={1}&appid={2}&units=metric&lang=pt_br",
            latitude, longitude, _apiKey);

        var currentResponse = await HttpClient.GetAsync(currentUrl, ct);
        currentResponse.EnsureSuccessStatusCode();

        string currentJson = await currentResponse.Content.ReadAsStringAsync(ct);
        using var currentDoc = JsonDocument.Parse(currentJson);
        var currentRoot = currentDoc.RootElement;

        double currentTemp = currentRoot.GetProperty("main").GetProperty("temp").GetDouble();
        int currentHumidity = currentRoot.GetProperty("main").GetProperty("humidity").GetInt32();
        string condition = currentRoot.GetProperty("weather")[0].GetProperty("description").GetString() ?? "Limpo";
        string iconCode = currentRoot.GetProperty("weather")[0].GetProperty("icon").GetString() ?? "01d";

        var weatherData = new WeatherData
        {
            LocationName = currentRoot.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? "Cuiabá" : "Cuiabá",
            ProviderName = "OpenWeather",
            LastUpdated = DateTime.Now,
            Current = new CurrentWeather
            {
                Temperature = currentTemp,
                Humidity = currentHumidity,
                ConditionText = CultureInfo.CurrentCulture.TextInfo.ToTitleCase(condition),
                IconEmoji = MapOpenWeatherIcon(iconCode)
            }
        };

        // Fetch 5-day forecast
        string forecastUrl = string.Format(CultureInfo.InvariantCulture,
            "https://api.openweathermap.org/data/2.5/forecast?lat={0}&lon={1}&appid={2}&units=metric&lang=pt_br",
            latitude, longitude, _apiKey);

        var forecastResponse = await HttpClient.GetAsync(forecastUrl, ct);
        if (forecastResponse.IsSuccessStatusCode)
        {
            string forecastJson = await forecastResponse.Content.ReadAsStringAsync(ct);
            using var forecastDoc = JsonDocument.Parse(forecastJson);
            var list = forecastDoc.RootElement.GetProperty("list");

            var dailyMap = new Dictionary<string, (double Max, double Min, string Icon, string Cond)>();
            var culture = new CultureInfo("pt-BR");

            foreach (var item in list.EnumerateArray())
            {
                string dtTxt = item.GetProperty("dt_txt").GetString() ?? string.Empty;
                if (DateTime.TryParse(dtTxt, out DateTime dt))
                {
                    string dateKey = dt.ToString("yyyy-MM-dd");
                    if (dateKey == DateTime.Now.ToString("yyyy-MM-dd")) continue; // Skip today

                    double mainMax = item.GetProperty("main").GetProperty("temp_max").GetDouble();
                    double mainMin = item.GetProperty("main").GetProperty("temp_min").GetDouble();
                    string itemIcon = item.GetProperty("weather")[0].GetProperty("icon").GetString() ?? "01d";
                    string itemCond = item.GetProperty("weather")[0].GetProperty("description").GetString() ?? "Limpo";

                    if (!dailyMap.ContainsKey(dateKey))
                    {
                        dailyMap[dateKey] = (mainMax, mainMin, itemIcon, itemCond);
                    }
                    else
                    {
                        var cur = dailyMap[dateKey];
                        dailyMap[dateKey] = (Math.Max(cur.Max, mainMax), Math.Min(cur.Min, mainMin), cur.Icon, cur.Cond);
                    }
                }
            }

            int count = 0;
            foreach (var kvp in dailyMap)
            {
                if (count >= 4) break;

                if (DateTime.TryParse(kvp.Key, out DateTime date))
                {
                    string dayOfWeek = culture.DateTimeFormat.GetAbbreviatedDayName(date.DayOfWeek);
                    dayOfWeek = char.ToUpper(dayOfWeek[0]) + dayOfWeek.Substring(1);

                    weatherData.Forecast.Add(new DailyForecast
                    {
                        Date = date,
                        DayOfWeek = dayOfWeek,
                        TempMax = kvp.Value.Max,
                        TempMin = kvp.Value.Min,
                        ConditionText = CultureInfo.CurrentCulture.TextInfo.ToTitleCase(kvp.Value.Cond),
                        IconEmoji = MapOpenWeatherIcon(kvp.Value.Icon)
                    });

                    count++;
                }
            }
        }

        return weatherData;
    }

    private static string MapOpenWeatherIcon(string iconCode)
    {
        return iconCode switch
        {
            "01d" or "01n" => "☀️",
            "02d" or "02n" => "🌤️",
            "03d" or "03n" or "04d" or "04n" => "☁️",
            "09d" or "09n" or "10d" or "10n" => "🌧️",
            "11d" or "11n" => "⛈️",
            "13d" or "13n" => "❄️",
            "50d" or "50n" => "🌫️",
            _ => "☀️"
        };
    }
}
