namespace Dashboard.ViewModels;

internal static class UsageBarPalette
{
    public const string Blue = "#38BDF8";
    public const string Green = "#4ADE80";
    public const string Yellow = "#FACC15";
    public const string Orange = "#F97316";
    public const string Red = "#EF4444";

    public static string ForPercent(double percent) => percent switch
    {
        <= 20 => Blue,
        <= 40 => Green,
        <= 60 => Yellow,
        <= 80 => Orange,
        _ => Red
    };

    public static bool IsCritical(double percent) => percent >= 91;
}

public static class TemperaturePalette
{
    public const string Blue = "#38BDF8";      // <= 30 °C
    public const string Green = "#4ADE80";     // 31 - 39 °C
    public const string Yellow = "#FACC15";    // 40 - 45 °C
    public const string Orange = "#FB923C";    // 46 - 52 °C
    public const string Red = "#EF4444";       // 53 - 59 °C & > 59 °C (flashing)

    public static string ForCelsius(double? tempCelsius)
    {
        if (tempCelsius is null) return "#94A3B8";

        double temp = tempCelsius.Value;
        if (temp <= 30) return Blue;
        if (temp <= 39) return Green;
        if (temp <= 45) return Yellow;
        if (temp <= 52) return Orange;
        return Red;
    }

    public static bool IsFlashing(double? tempCelsius)
    {
        return tempCelsius is not null && tempCelsius.Value > 59;
    }

    public static string ForIgbtCelsius(double? tempCelsius)
    {
        if (tempCelsius is null) return "#94A3B8";

        double temp = tempCelsius.Value;
        if (temp <= 50) return Green;
        if (temp <= 60) return Yellow;
        return Red;
    }

    public static bool IsIgbtFlashing(double? tempCelsius)
    {
        return tempCelsius is not null && tempCelsius.Value > 80;
    }
}
