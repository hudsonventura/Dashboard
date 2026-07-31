using System;
using System.Collections.Generic;
using Avalonia.Media.Imaging;

namespace Dashboard.Models;

public class CurrentWeather
{
    public double Temperature { get; set; }
    public double FeelsLike { get; set; }
    public double WindSpeedKmH { get; set; }
    public int Humidity { get; set; }
    public string ConditionText { get; set; } = "Ensolarado";
    public Bitmap Icon { get; set; } = MeteoconAssets.GetBitmap("clear-day");
}

public class DailyForecast
{
    public DateTime Date { get; set; }
    public string DayOfWeek { get; set; } = string.Empty;
    public double TempMax { get; set; }
    public double TempMin { get; set; }
    public string ConditionText { get; set; } = "Ensolarado";
    public Bitmap Icon { get; set; } = MeteoconAssets.GetBitmap("clear-day");
}

public class WeatherAlert
{
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string Severity { get; set; } = "Warning"; // "Warning" or "Danger"
    public Bitmap Icon { get; set; } = MeteoconAssets.GetBitmap("weather-alert");
}

public class WeatherData
{
    public string LocationName { get; set; } = "Cuiabá";
    public CurrentWeather Current { get; set; } = new();
    public double TodayTempMax { get; set; }
    public double TodayTempMin { get; set; }
    public int MaxPrecipitationProbabilityNext24Hours { get; set; }
    public List<DailyForecast> Forecast { get; set; } = new();
    public List<WeatherAlert> Alerts { get; set; } = new();
    public DateTime LastUpdated { get; set; } = DateTime.Now;
    public string ProviderName { get; set; } = "OpenMeteo";
}

public static class WeatherAlertEvaluator
{
    public static void EvaluateAlerts(WeatherData weatherData)
    {
        weatherData.Alerts.Clear();

        // 1. High Wind Speed / Gusts Warning (wind-alert.png)
        if (weatherData.Current.WindSpeedKmH >= 25)
        {
            weatherData.Alerts.Add(new WeatherAlert
            {
                Title = "High Wind Warning",
                Message = $"Wind speed reaching {weatherData.Current.WindSpeedKmH:F0} km/h",
                Severity = weatherData.Current.WindSpeedKmH >= 45 ? "Danger" : "Warning",
                Icon = MeteoconAssets.GetBitmap("wind-alert")
            });
        }

        // 2. High Precipitation / Heavy Rain Warning (weather-alert.png)
        if (weatherData.MaxPrecipitationProbabilityNext24Hours >= 60 || 
            weatherData.Current.ConditionText.Contains("Thunderstorm", StringComparison.OrdinalIgnoreCase) ||
            weatherData.Current.ConditionText.Contains("Rain", StringComparison.OrdinalIgnoreCase))
        {
            weatherData.Alerts.Add(new WeatherAlert
            {
                Title = "Rain / Storm Warning",
                Message = $"{weatherData.MaxPrecipitationProbabilityNext24Hours}% chance of precipitation in 24h",
                Severity = weatherData.MaxPrecipitationProbabilityNext24Hours >= 80 ? "Danger" : "Warning",
                Icon = MeteoconAssets.GetBitmap("weather-alert")
            });
        }

        // 3. Extreme Temperature Alert (thermometer-alert.png)
        // Alert only if temperature is outside the 20°C to 40°C range
        if (weatherData.TodayTempMax > 40 || weatherData.TodayTempMin < 20)
        {
            bool isHigh = weatherData.TodayTempMax > 40;
            string msg = isHigh
                ? $"High temperature reaching {weatherData.TodayTempMax:F0}°C"
                : $"Low temperature dropping to {weatherData.TodayTempMin:F0}°C";

            weatherData.Alerts.Add(new WeatherAlert
            {
                Title = isHigh ? "Extreme Heat Warning" : "Low Temperature Warning",
                Message = msg,
                Severity = isHigh || weatherData.TodayTempMin <= 10 ? "Danger" : "Warning",
                Icon = MeteoconAssets.GetBitmap("thermometer-alert")
            });
        }

        // 4. Low Humidity Alert (code-orange.png)
        if (weatherData.Current.Humidity <= 25)
        {
            weatherData.Alerts.Add(new WeatherAlert
            {
                Title = "Low Humidity Warning",
                Message = $"Critical relative humidity at {weatherData.Current.Humidity}%",
                Severity = weatherData.Current.Humidity <= 15 ? "Danger" : "Warning",
                Icon = MeteoconAssets.GetBitmap("code-orange")
            });
        }
    }
}
