using Avalonia;
using System;
using System.Runtime.InteropServices;
using SolisDashboard.App.Cameras;

namespace SolisDashboard.App;

sealed class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        Console.WriteLine($"SolisDashboard starting… ({RuntimeInformation.OSDescription})");
        X11Threads.EnsureInitialized();
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
