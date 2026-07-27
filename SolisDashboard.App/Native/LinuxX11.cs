using System.Runtime.InteropServices;

namespace SolisDashboard.App.Native;

internal static class LinuxX11
{
    [DllImport("libX11", CallingConvention = CallingConvention.Cdecl)]
    private static extern int XInitThreads();

    /// <summary>
    /// Required on Linux/X11 (XFCE) before LibVLC creates players.
    /// Safe to call once; ignore failures if libX11 is unavailable.
    /// </summary>
    public static void EnsureThreads()
    {
        if (!OperatingSystem.IsLinux())
            return;

        try
        {
            _ = XInitThreads();
        }
        catch (DllNotFoundException)
        {
            // libx11-dev / libx11-6 missing — surfaced later via LibVLC errors.
        }
        catch (EntryPointNotFoundException)
        {
            // Ignore.
        }
    }
}
