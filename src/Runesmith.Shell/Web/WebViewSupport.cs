using Avalonia.Platform;

namespace Runesmith.Shell.Web;

/// <summary>Tells whether this computer has a browser engine web views can use, such as WebView2 on Windows or WebKitGTK on Linux.</summary>
internal static class WebViewSupport
{
    private static readonly Lazy<string?> Reason = new(Check);

    /// <summary>Gets why web views cannot show pages, or null when they can.</summary>
    public static string? UnavailableReason => Reason.Value;

    private static string? Check()
    {
        foreach (var type in Enum.GetValues<WebViewAdapterType>())
        {
            if (type is WebViewAdapterType.Unknown or WebViewAdapterType.Headless or WebViewAdapterType.BrowserIFrame or WebViewAdapterType.AndroidWebView)
                continue;

            try
            {
                if (WebViewAdapterInfo.GetAdapterInfo(type) is { IsSupported: true, IsInstalled: true })
                    return null;
            }
            catch (Exception exception) when (exception is TypeInitializationException or DllNotFoundException or EntryPointNotFoundException or InvalidOperationException or PlatformNotSupportedException)
            {
            }
        }

        return OperatingSystem.IsLinux() ? "Install WebKitGTK 4.1 (libwebkit2gtk-4.1) and restart Runesmith."
            : OperatingSystem.IsWindows() ? "Install the Microsoft Edge WebView2 Runtime and restart Runesmith."
            : "This system has no browser engine Runesmith can use.";
    }
}
