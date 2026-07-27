using Avalonia;
using System;
using SolisDashboard.App.Native;

namespace SolisDashboard.App;

sealed class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // LibVLCSharp + X11 (XFCE): must init X threads before any LibVLC/UI work.
        LinuxX11.EnsureThreads();

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
