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
            "https://api.openweathermap.org/data/2.5/weather?lat={0}&lon={1}&appid={2}&units=metric&lang=en",
            latitude, longitude, _apiKey);

        var currentResponse = await HttpClient.GetAsync(currentUrl, ct);
        currentResponse.EnsureSuccessStatusCode();

        string currentJson = await currentResponse.Content.ReadAsStringAsync(ct);
        using var currentDoc = JsonDocument.Parse(currentJson);
        var currentRoot = currentDoc.RootElement;

        double currentTemp = currentRoot.GetProperty("main").GetProperty("temp").GetDouble();
        int currentHumidity = currentRoot.GetProperty("main").GetProperty("humidity").GetInt32();
        double feelsLike = currentRoot.GetProperty("main").TryGetProperty("feels_like", out var feelsProp) ? feelsProp.GetDouble() : currentTemp;
        double windSpeed = currentRoot.TryGetProperty("wind", out var windObj) && windObj.TryGetProperty("speed", out var speedProp) ? speedProp.GetDouble() * 3.6 : 0;
        string condition = currentRoot.GetProperty("weather")[0].GetProperty("description").GetString() ?? "Clear";
        string iconCode = currentRoot.GetProperty("weather")[0].GetProperty("icon").GetString() ?? "01d";

        var weatherData = new WeatherData
        {
            LocationName = currentRoot.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? "Cuiabá" : "Cuiabá",
            ProviderName = "OpenWeather",
            LastUpdated = DateTime.Now,
            Current = new CurrentWeather
            {
                Temperature = currentTemp,
                FeelsLike = feelsLike,
                WindSpeedKmH = windSpeed,
                Humidity = currentHumidity,
                ConditionText = CultureInfo.CurrentCulture.TextInfo.ToTitleCase(condition),
                Icon = MeteoconAssets.GetBitmap(MapOpenWeatherIcon(iconCode))
            },
            TodayTempMax = currentRoot.GetProperty("main").GetProperty("temp_max").GetDouble(),
            TodayTempMin = currentRoot.GetProperty("main").GetProperty("temp_min").GetDouble()
        };

        // Fetch 5-day forecast
        string forecastUrl = string.Format(CultureInfo.InvariantCulture,
            "https://api.openweathermap.org/data/2.5/forecast?lat={0}&lon={1}&appid={2}&units=metric&lang=en",
            latitude, longitude, _apiKey);

        var forecastResponse = await HttpClient.GetAsync(forecastUrl, ct);
        if (forecastResponse.IsSuccessStatusCode)
        {
            string forecastJson = await forecastResponse.Content.ReadAsStringAsync(ct);
            using var forecastDoc = JsonDocument.Parse(forecastJson);
            var list = forecastDoc.RootElement.GetProperty("list");

            var dailyMap = new Dictionary<string, (double Max, double Min, string Icon, string Cond)>();
            var culture = CultureInfo.GetCultureInfo("en-US");

            foreach (var item in list.EnumerateArray())
            {
                string dtTxt = item.GetProperty("dt_txt").GetString() ?? string.Empty;
                if (DateTime.TryParse(dtTxt, out DateTime dt))
                {
                    string dateKey = dt.ToString("yyyy-MM-dd");
                    double mainMax = item.GetProperty("main").GetProperty("temp_max").GetDouble();
                    double mainMin = item.GetProperty("main").GetProperty("temp_min").GetDouble();
                    string itemIcon = item.GetProperty("weather")[0].GetProperty("icon").GetString() ?? "01d";
                    string itemCond = item.GetProperty("weather")[0].GetProperty("description").GetString() ?? "Limpo";

                    if (dateKey == DateTime.Now.ToString("yyyy-MM-dd"))
                    {
                        weatherData.TodayTempMax = Math.Max(weatherData.TodayTempMax, mainMax);
                        weatherData.TodayTempMin = Math.Min(weatherData.TodayTempMin, mainMin);
                    }
                    else
                    {
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

                    if (dt >= DateTime.Now && dt <= DateTime.Now.AddHours(24) &&
                        item.TryGetProperty("pop", out var precipitationProbability))
                    {
                        weatherData.MaxPrecipitationProbabilityNext24Hours = Math.Max(
                            weatherData.MaxPrecipitationProbabilityNext24Hours,
                            (int)Math.Round(precipitationProbability.GetDouble() * 100));
                    }
                }
            }

            int count = 0;
            foreach (var kvp in dailyMap)
            {
                if (count >= 3) break;

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
                        Icon = MeteoconAssets.GetBitmap(MapOpenWeatherIcon(kvp.Value.Icon))
                    });

                    count++;
                }
            }
        }

        WeatherAlertEvaluator.EvaluateAlerts(weatherData);
        return weatherData;
    }

    private static string MapOpenWeatherIcon(string iconCode)
    {
        return iconCode switch
        {
            "01d" => "clear-day",
            "01n" => "clear-night",
            "02d" => "partly-cloudy-day",
            "02n" => "partly-cloudy-night",
            "03d" or "03n" or "04d" or "04n" => "overcast",
            "09d" or "09n" => "drizzle",
            "10d" or "10n" => "rain",
            "11d" or "11n" => "thunderstorms",
            "13d" or "13n" => "snow",
            "50d" or "50n" => "fog",
            _ => "clear-day"
        };
    }
}
