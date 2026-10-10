using Avalonia.Controls;

namespace Runesmith.Sdk.Web;

/// <summary>Creates web views that show the calling plugin's own web pages, for tool windows and custom editors.</summary>
/// <remarks>A web view loads the files in the <c>web</c> folder of the plugin's package. It may also go to the hosts the plugin's manifest
/// lists in <c>networkHosts</c>, over https, when the plugin declares the <c>network</c> capability; every other address is blocked. Pages talk
/// to the plugin through messages, never through .NET objects.</remarks>
public interface IWebViewService
{
    /// <summary>Gets the name of the web folder in a plugin's package.</summary>
    public const string WebFolder = "web";

    /// <summary>Creates a web view that shows a page of the calling plugin's web folder, such as <c>index.html</c>; call it on the UI
    /// thread.</summary>
    /// <exception cref="InvalidOperationException">The caller is not a plugin.</exception>
    /// <exception cref="ArgumentException">The page is not a path inside the web folder.</exception>
    IWebView Create(string page = "index.html");
}

/// <summary>A web page in a plugin's interface. Put <see cref="Control"/> in a tool window or a custom editor, and dispose the web view
/// with it.</summary>
public interface IWebView : IDisposable
{
    /// <summary>Gets the control that shows the page, or a message saying why web pages cannot show on this computer.</summary>
    Control Control { get; }

    /// <summary>Gets whether this computer can show web pages; when it cannot, <see cref="UnavailableReason"/> says why.</summary>
    bool IsAvailable { get; }

    /// <summary>Gets why web pages cannot show, such as a missing system library, or null when they can.</summary>
    string? UnavailableReason { get; }

    /// <summary>Gets the address of the page shown, or null before the first one loads.</summary>
    Uri? Address { get; }

    /// <summary>Shows another page: a path in the plugin's web folder, such as <c>settings.html</c>, or an https address on one of the
    /// plugin's declared hosts.</summary>
    /// <exception cref="ArgumentException">The path leaves the web folder, or the address is not https.</exception>
    /// <exception cref="UnauthorizedAccessException">The address is on the network, and the plugin did not declare the <c>network</c>
    /// capability or the host.</exception>
    void Navigate(string address);

    /// <summary>Sends a message to the page, as JSON; the page receives the parsed value. Messages sent before the page is ready wait for
    /// it.</summary>
    /// <exception cref="ArgumentException">The text is not valid JSON.</exception>
    void PostMessage(string json);

    /// <summary>Raised when the plugin's own page sends a message; pages from the network cannot send any.</summary>
    event EventHandler<WebViewMessageEventArgs>? MessageReceived;

    /// <summary>Raised when the page tried to go to an address the rules block, such as a host the plugin did not declare.</summary>
    event EventHandler<WebViewNavigationBlockedEventArgs>? NavigationBlocked;

    /// <summary>Raised when a page has loaded.</summary>
    event EventHandler? PageLoaded;
}

/// <summary>A message from the page.</summary>
/// <param name="json">The message as JSON text.</param>
public sealed class WebViewMessageEventArgs(string json) : EventArgs
{
    public string Json { get; } = json;
}

/// <summary>An address the page was not allowed to go to.</summary>
public sealed class WebViewNavigationBlockedEventArgs(Uri address, string reason) : EventArgs
{
    public Uri Address { get; } = address;

    /// <summary>Gets why it was blocked, for the plugin's log.</summary>
    public string Reason { get; } = reason;
}
