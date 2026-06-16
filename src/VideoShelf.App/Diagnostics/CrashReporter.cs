using System;
using System.IO;
using System.Text;

namespace VideoShelf.App.Diagnostics;

/// <summary>
/// Pure crash-report formatting + best-effort persistence. The WPF handlers
/// (App.xaml.cs) call FormatReport for the dialog text and WriteToDisk for a log.
/// </summary>
public static class CrashReporter
{
    public static string FormatReport(string source, Exception? ex)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"VideoShelf unexpected error ({source}).");
        if (ex is null)
        {
            sb.AppendLine("Unknown error (no exception object).");
            return sb.ToString();
        }
        sb.AppendLine($"{ex.GetType().Name}: {ex.Message}");
        sb.AppendLine(ex.StackTrace ?? "(no stack trace)");
        return sb.ToString();
    }

    /// <summary>Best-effort: write the report under &lt;dataDir&gt;\logs\. Never throws.</summary>
    public static void WriteToDisk(string dataDir, string report)
    {
        try
        {
            var dir = Path.Combine(dataDir, "logs");
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, $"crash-{Guid.NewGuid():N}.log");
            File.WriteAllText(file, report);
        }
        catch { /* logging must never crash the crash handler */ }
    }
}
