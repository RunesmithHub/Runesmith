using System.Runtime.InteropServices;

namespace Runesmith.Shell.Web;

/// <summary>Blocks navigations in a WebKitGTK web view itself, by answering its <c>decide-policy</c> signal.</summary>
/// <remarks>The WebKitGTK adapter of Avalonia.Controls.WebView 12.1 marks a cancelled navigation as handled without ignoring it, so the page
/// still loads. This handler runs after the adapter's while the adapter leaves the decision open, and ignores the navigation.</remarks>
internal sealed class GtkNavigationGuard
{
    private const string GObject = "libgobject-2.0.so.0";
    private const string WebKit = "libwebkit2gtk-4.1.so.0";
    private const int NavigationAction = 0;

    private static readonly DecidePolicy Decide = OnDecidePolicy;
    private static readonly DestroyNotify Release = OnRelease;

    private readonly Func<Uri, bool> isBlocked;

    private GtkNavigationGuard(Func<Uri, bool> isBlocked) => this.isBlocked = isBlocked;

    private delegate int DecidePolicy(nint webView, nint decision, int type, nint data);

    private delegate void DestroyNotify(nint data, nint closure);

    /// <summary>Connects the guard to a WebKitWebView; returns false when WebKitGTK 4.1 cannot be reached, so the guard is not in place.</summary>
    public static bool TryAttach(nint webView, Func<Uri, bool> isBlocked)
    {
        if (webView == 0 || !OperatingSystem.IsLinux())
            return false;

        var handle = GCHandle.Alloc(new GtkNavigationGuard(isBlocked));
        try
        {
            var id = g_signal_connect_data(webView, "decide-policy\0"u8.ToArray(), Marshal.GetFunctionPointerForDelegate(Decide), GCHandle.ToIntPtr(handle),
                Marshal.GetFunctionPointerForDelegate(Release), 0);
            if (id != 0)
                return true;
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
        {
        }

        handle.Free();
        return false;
    }

    private static int OnDecidePolicy(nint webView, nint decision, int type, nint data)
    {
        if (type != NavigationAction || GCHandle.FromIntPtr(data).Target is not GtkNavigationGuard guard)
            return 0;

        var action = webkit_navigation_policy_decision_get_navigation_action(decision);
        var request = action == 0 ? 0 : webkit_navigation_action_get_request(action);
        var text = request == 0 ? null : Marshal.PtrToStringUTF8(webkit_uri_request_get_uri(request));
        if (text is null || !Uri.TryCreate(text, UriKind.Absolute, out var address) || !guard.isBlocked(address))
            return 0;

        webkit_policy_decision_ignore(decision);
        return 1;
    }

    private static void OnRelease(nint data, nint closure) => GCHandle.FromIntPtr(data).Free();

    [DllImport(GObject)]
    private static extern nuint g_signal_connect_data(nint instance, byte[] signal, nint handler, nint data, nint destroyData, int flags);

    [DllImport(WebKit)]
    private static extern nint webkit_navigation_policy_decision_get_navigation_action(nint decision);

    [DllImport(WebKit)]
    private static extern nint webkit_navigation_action_get_request(nint action);

    [DllImport(WebKit)]
    private static extern nint webkit_uri_request_get_uri(nint request);

    [DllImport(WebKit)]
    private static extern void webkit_policy_decision_ignore(nint decision);
}
