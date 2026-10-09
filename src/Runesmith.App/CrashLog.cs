using System.Globalization;
using Runesmith.Sdk;

namespace Runesmith.App;

/// <summary>Writes errors nothing else handled to a log file, so a crash can be reported.</summary>
internal static class CrashLog
{
    /// <summary>Writes an error to a new file in the logs folder and returns its path, or null when it cannot be written.</summary>
    public static string? Write(Exception exception, string context)
    {
        try
        {
            Directory.CreateDirectory(RunesmithPaths.Logs);
            var path = Path.Combine(RunesmithPaths.Logs, $"error-{DateTime.Now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture)}.log");
            File.WriteAllText(path, $"""
                Runesmith {typeof(CrashLog).Assembly.GetName().Version} on {System.Runtime.InteropServices.RuntimeInformation.OSDescription}
                {context} at {DateTime.Now:O}

                {exception}
                """);
            return path;
        }
        catch (Exception writeFailure) when (writeFailure is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
