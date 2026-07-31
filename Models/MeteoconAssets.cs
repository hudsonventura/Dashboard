using System;
using System.Collections.Concurrent;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Dashboard.Models;

public static class MeteoconAssets
{
    private static readonly ConcurrentDictionary<string, Bitmap> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static Bitmap GetBitmap(string iconName)
    {
        return Cache.GetOrAdd(iconName, static name =>
        {
            var uri = new Uri($"avares://Dashboard/Assets/Meteocons/png/{name}.png");
            using var stream = AssetLoader.Open(uri);
            return new Bitmap(stream);
        });
    }

    public static string ResolveWeatherIconName(string baseIconName, bool isNight)
    {
        return baseIconName switch
        {
            "clear-day" => isNight ? "clear-night" : "clear-day",
            "partly-cloudy-day" => isNight ? "partly-cloudy-night" : "partly-cloudy-day",
            _ => baseIconName
        };
    }

    public static string ResolveTimeIconName(int hour)
    {
        return hour switch
        {
            < 3 => "time-night",
            < 6 => "time-late-night",
            < 9 => "time-morning",
            < 12 => "time-late-morning",
            < 15 => "time-afternoon",
            < 18 => "time-late-afternoon",
            < 21 => "time-evening",
            _ => "time-late-evening"
        };
    }
}
