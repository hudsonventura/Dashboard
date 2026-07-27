using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace SolisDashboard.App.Converters;

/// <summary>Converte a chave do clima (ex.: clear) no DrawingImage correspondente em Application.Resources.</summary>
public sealed class WeatherIconConverter : IValueConverter
{
    public static readonly WeatherIconConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = value as string ?? "unknown";
        var resourceKey = $"Weather.{key}";

        if (Avalonia.Application.Current?.TryGetResource(resourceKey, Avalonia.Application.Current.ActualThemeVariant, out var resource) == true
            && resource is IImage image)
        {
            return image;
        }

        if (Avalonia.Application.Current?.TryGetResource("Weather.unknown", Avalonia.Application.Current.ActualThemeVariant, out var fallback) == true
            && fallback is IImage fallbackImage)
        {
            return fallbackImage;
        }

        return null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
