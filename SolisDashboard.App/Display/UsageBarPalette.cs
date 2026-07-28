using Avalonia.Media;

namespace SolisDashboard.App.Display;

public static class UsageBarPalette
{
    private static readonly IBrush Blue = new SolidColorBrush(Color.Parse("#42A5F5"));
    private static readonly IBrush Green = new SolidColorBrush(Color.Parse("#66BB6A"));
    private static readonly IBrush Yellow = new SolidColorBrush(Color.Parse("#FFEE58"));
    private static readonly IBrush Orange = new SolidColorBrush(Color.Parse("#FFA726"));
    private static readonly IBrush Red = new SolidColorBrush(Color.Parse("#EF5350"));
    private static readonly IBrush CriticalRed = new SolidColorBrush(Color.Parse("#FF1744"));
    public static readonly IBrush GreenOk = Green;
    public static readonly IBrush SwapRed = new SolidColorBrush(Color.Parse("#E53935"));

    public static IBrush ForPercent(double percent)
    {
        if (percent > 95)
            return CriticalRed;
        if (percent >= 86)
            return Red;
        if (percent >= 66)
            return Orange;
        if (percent >= 51)
            return Yellow;
        if (percent >= 31)
            return Green;
        return Blue;
    }

    public static bool IsCritical(double percent) => percent > 95;
}
