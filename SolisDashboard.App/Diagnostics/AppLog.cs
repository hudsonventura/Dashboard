using System.Text;

namespace SolisDashboard.App.Diagnostics;

internal static class AppLog
{
    private static readonly object Gate = new();
    private static readonly string LogDirectory =
        Path.Combine(AppContext.BaseDirectory, "logs");
    private static readonly string LogFile =
        Path.Combine(LogDirectory, "dashboard-errors.log");

    public static string Append(string category, Exception ex)
    {
        var text = new StringBuilder()
            .AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {category}")
            .AppendLine(ex.ToString())
            .ToString();

        AppendRaw(text);
        return text;
    }

    public static string Append(string category, string message)
    {
        var text = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {category}{Environment.NewLine}{message}{Environment.NewLine}";
        AppendRaw(text);
        return text;
    }

    private static void AppendRaw(string text)
    {
        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(LogDirectory);
                File.AppendAllText(LogFile, text + Environment.NewLine);
            }
            catch
            {
                // Never crash the app because logging failed.
            }

            try
            {
                Console.Error.WriteLine(text);
            }
            catch
            {
                // Ignore.
            }
        }
    }

    public static string LogFilePath => LogFile;
}
