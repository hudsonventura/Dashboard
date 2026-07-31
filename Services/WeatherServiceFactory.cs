using System;
using Dashboard.Models;

namespace Dashboard.Services;

public static class WeatherServiceFactory
{
    public static IWeatherService Create(WeatherSettings settings)
    {
        if (settings.Provider?.Equals("OpenWeather", StringComparison.OrdinalIgnoreCase) == true)
        {
            return new OpenWeatherService(settings.OpenWeather.ApiKey);
        }

        return new OpenMeteoService();
    }
}
