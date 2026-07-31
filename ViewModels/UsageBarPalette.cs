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
