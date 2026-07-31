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

public class WeatherData
{
    public string LocationName { get; set; } = "Cuiabá";
    public CurrentWeather Current { get; set; } = new();
    public double TodayTempMax { get; set; }
    public double TodayTempMin { get; set; }
    public int MaxPrecipitationProbabilityNext24Hours { get; set; }
    public List<DailyForecast> Forecast { get; set; } = new();
    public DateTime LastUpdated { get; set; } = DateTime.Now;
    public string ProviderName { get; set; } = "OpenMeteo";
}
