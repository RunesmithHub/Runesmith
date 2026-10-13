using System.Globalization;
using Runesmith.Sdk;

namespace Runesmith.App;

/// <summary>Writes errors nothing else handled, and starts that hang, to log files, so they can be reported.</summary>
internal static class CrashLog
{
    /// <summary>Writes an error to a new file in the logs folder and returns its path, or null when it cannot be written.</summary>
    public static string? Write(Exception exception, string context) => Write("error", context, exception.ToString());

    /// <summary>Writes a report to a new file in the logs folder and returns its path, or null when it cannot be written.</summary>
    /// <param name="kind">The start of the file's name, such as <c>error</c>.</param>
    public static string? Write(string kind, string context, string details)
    {
        try
        {
            Directory.CreateDirectory(RunesmithPaths.Logs);
            var path = Path.Combine(RunesmithPaths.Logs, $"{kind}-{DateTime.Now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture)}.log");
            File.WriteAllText(path, $"""
                Runesmith {typeof(CrashLog).Assembly.GetName().Version} on {System.Runtime.InteropServices.RuntimeInformation.OSDescription}
                {context} at {DateTime.Now:O}

                {details}
                """);
            return path;
        }
        catch (Exception writeFailure) when (writeFailure is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
