using Avalonia;
using System;

namespace SolisDashboard.App;

sealed class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        Console.WriteLine($"SolisDashboard starting… ({Environment.OSVersion})");
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
