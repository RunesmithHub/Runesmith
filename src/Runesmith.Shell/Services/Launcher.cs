using System.Diagnostics;

namespace Runesmith.Shell.Services;

/// <summary>Opens addresses and folders with the system's own applications.</summary>
public static class Launcher
{
    /// <summary>Opens a web address in the default browser.</summary>
    public static void OpenUrl(string url) => Start(url);

    /// <summary>Shows a file or folder in the system's file manager.</summary>
    public static void Reveal(string path)
    {
        if (OperatingSystem.IsWindows())
            Start("explorer.exe", File.Exists(path) ? $"/select,\"{path}\"" : $"\"{path}\"");
        else if (OperatingSystem.IsMacOS())
            Start("open", File.Exists(path) ? $"-R \"{path}\"" : $"\"{path}\"");
        else
            Start("xdg-open", $"\"{(File.Exists(path) ? Path.GetDirectoryName(path) : path)}\"");
    }

    private static void Start(string target, string? arguments = null)
    {
        try
        {
            using var process = arguments is null
                ? Process.Start(new ProcessStartInfo(target) { UseShellExecute = true })
                : Process.Start(new ProcessStartInfo(target, arguments) { UseShellExecute = false });
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
        }
    }
}
