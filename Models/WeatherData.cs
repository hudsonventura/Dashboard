using System;
using System.Collections.Generic;

namespace Dashboard.Models;

public class CurrentWeather
{
    public double Temperature { get; set; }
    public int Humidity { get; set; }
    public string ConditionText { get; set; } = "Ensolarado";
    public string IconEmoji { get; set; } = "☀️";
}

public class DailyForecast
{
    public DateTime Date { get; set; }
    public string DayOfWeek { get; set; } = string.Empty;
    public double TempMax { get; set; }
    public double TempMin { get; set; }
    public string ConditionText { get; set; } = "Ensolarado";
    public string IconEmoji { get; set; } = "☀️";
}

public class WeatherData
{
    public string LocationName { get; set; } = "Cuiabá";
    public CurrentWeather Current { get; set; } = new();
    public List<DailyForecast> Forecast { get; set; } = new();
    public DateTime LastUpdated { get; set; } = DateTime.Now;
    public string ProviderName { get; set; } = "OpenMeteo";
}
