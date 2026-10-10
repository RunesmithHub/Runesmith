using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using HammerUI;
using HammerUI.Controls;
using Runesmith.Sdk.Web;

namespace Runesmith.Shell.Web;

/// <summary>A plugin's web view: a native browser control held to the plugin's rules, or a message when the system has no browser
/// engine.</summary>
internal sealed class PluginWebView : IWebView
{
    private readonly WebViewPolicy policy;
    private readonly Action<string> log;
    private readonly Queue<string> outbox = new();
    private readonly NativeWebView? view;
    private Uri? committed;
    private Uri? pending;
    private bool isReady;
    private bool isGtk;
    private bool isGuarded;
    private Uri? lastOwn;
    private bool isDisposed;

    public PluginWebView(WebViewPolicy policy, string? unavailableReason, Action<string> log)
    {
        this.policy = policy;
        this.log = log;
        UnavailableReason = unavailableReason;
        if (unavailableReason is not null)
        {
            Control = new EmptyState { Icon = Icons.Link, Title = "Web content cannot be shown", Hint = unavailableReason };
            return;
        }

        view = new NativeWebView();
        view.EnvironmentRequested += OnEnvironmentRequested;
        view.AdapterCreated += OnAdapterCreated;
        view.NavigationStarted += OnNavigationStarted;
        view.NavigationCompleted += OnNavigationCompleted;
        view.NewWindowRequested += OnNewWindowRequested;
        view.WebMessageReceived += OnWebMessageReceived;
        Control = view;
    }

    public Control Control { get; }

    public bool IsAvailable => view is not null;

    public string? UnavailableReason { get; }

    public Uri? Address => committed ?? pending;

    public event EventHandler<WebViewMessageEventArgs>? MessageReceived;

    public event EventHandler<WebViewNavigationBlockedEventArgs>? NavigationBlocked;

    public event EventHandler? PageLoaded;

    /// <summary>Loads the first page, which the caller has checked.</summary>
    internal void Start(Uri page)
    {
        if (view is not null)
            view.Source = page;
    }

    public void Navigate(string address)
    {
        ArgumentException.ThrowIfNullOrEmpty(address);
        var target = Resolve(address);
        var (kind, reason) = policy.Check(target);
        if (kind == WebAddressKind.Blocked)
        {
            var message = $"The web view of {policy.PluginId} cannot open {target}: {reason}";
            log(message);
            if (!policy.IsOwnOrigin(target) && target.Scheme == Uri.UriSchemeHttps)
                throw new UnauthorizedAccessException(message);
            throw new ArgumentException(message, nameof(address));
        }

        view?.Navigate(target);
    }

    public void PostMessage(string json)
    {
        if (!WebMessages.IsJson(json))
            throw new ArgumentException("A message must be one JSON value.", nameof(json));
        if (isDisposed || view is null)
            return;

        outbox.Enqueue(json);
        Flush();
    }

    public void Dispose()
    {
        if (isDisposed)
            return;

        isDisposed = true;
        outbox.Clear();
        if (view is null)
            return;

        view.EnvironmentRequested -= OnEnvironmentRequested;
        view.AdapterCreated -= OnAdapterCreated;
        view.NavigationStarted -= OnNavigationStarted;
        view.NavigationCompleted -= OnNavigationCompleted;
        view.NewWindowRequested -= OnNewWindowRequested;
        view.WebMessageReceived -= OnWebMessageReceived;
        if (view.Parent is ContentControl parent && ReferenceEquals(parent.Content, view))
            parent.Content = null;
        else if (view.Parent is Panel panel)
            panel.Children.Remove(view);
    }

    /// <summary>Gets the address a plugin names: a path in its web folder, or an absolute address.</summary>
    /// <exception cref="ArgumentException">The path leaves the web folder.</exception>
    internal Uri Resolve(string address)
    {
        if (Uri.TryCreate(address, UriKind.Absolute, out var absolute) && !address.StartsWith('/'))
            return absolute;

        var path = "/" + address.TrimStart('/');
        if (!BundledFiles.IsSafePath(Uri.UnescapeDataString(path.Split(['?', '#'], 2)[0])))
            throw new ArgumentException($"{address} is not a path in the plugin's web folder.", nameof(address));
        return new Uri(policy.Origin, path);
    }

    // Every document that may still send messages, the shown one and the one loading, is the plugin's own.
    private bool IsOwnPage => committed is { } shown && policy.IsOwnOrigin(shown) && (pending is null || policy.IsOwnOrigin(pending));

    private void Flush()
    {
        if (!isReady || view is null || !IsOwnPage)
            return;

        while (outbox.TryDequeue(out var json))
            _ = RunAsync(WebMessages.DeliveryScript(json));
    }

    private async Task RunAsync(string script)
    {
        try
        {
            await view!.InvokeScript(script);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            log($"A script in the web view of {policy.PluginId} did not run: {exception.Message}");
        }
    }

    private void OnEnvironmentRequested(object? sender, WebViewEnvironmentRequestedEventArgs e)
    {
        e.EnableDevTools = false;
        switch (e)
        {
            case GtkWebViewEnvironmentRequestedEventArgs gtk:
                gtk.EphemeralDataManager = true;
                break;
            case WindowsWebView2EnvironmentRequestedEventArgs edge:
                edge.IsInPrivateModeEnabled = true;
                edge.ProfileName = policy.PluginId;
                break;
            case AppleWKWebViewEnvironmentRequestedEventArgs apple:
                apple.NonPersistentDataStore = true;
                break;
        }
    }

    private void OnAdapterCreated(object? sender, WebViewAdapterEventArgs e)
    {
        if (e.TryGetPlatformHandle() is not IGtkWebViewPlatformHandle gtk)
            return;

        isGtk = true;
        isGuarded = GtkNavigationGuard.TryAttach(gtk.WebKitWebView, address => policy.Check(address).Kind == WebAddressKind.Blocked);
        if (!isGuarded)
            log($"The web view of {policy.PluginId} cannot stop pages from leaving the allowed addresses before they load, so it goes back when one does.");
    }

    private void OnNavigationStarted(object? sender, WebViewNavigationStartingEventArgs e)
    {
        if (e.Request is not { } address)
            return;

        var (kind, reason) = policy.Check(address);
        if (kind == WebAddressKind.Blocked)
        {
            if (!isGtk)
                e.Cancel = true;
            else if (!isGuarded)
                pending = address;
            log($"The web view of {policy.PluginId} was kept from opening {address}: {reason}");
            Dispatcher.UIThread.Post(() => NavigationBlocked?.Invoke(this, new WebViewNavigationBlockedEventArgs(address, reason!)));
            return;
        }

        if (committed is not null && committed.GetLeftPart(UriPartial.Query) == address.GetLeftPart(UriPartial.Query) && address.Fragment.Length > 0)
            return;

        isReady = false;
        pending = address;
    }

    private void OnNavigationCompleted(object? sender, WebViewNavigationCompletedEventArgs e)
    {
        if (!e.IsSuccess && e.Request is { } stopped && policy.Check(stopped).Kind == WebAddressKind.Blocked)
            return;

        pending = null;
        if (e.Request is { } address)
        {
            committed = address;
            if (policy.IsOwnOrigin(address))
                lastOwn = address;
            else if (policy.Check(address).Kind == WebAddressKind.Blocked)
                view!.Navigate(lastOwn ?? policy.Origin);
        }

        if (!isReady && IsOwnPage)
            _ = RunAsync(WebMessages.ReadyScript);
        PageLoaded?.Invoke(this, EventArgs.Empty);
    }

    private void OnNewWindowRequested(object? sender, WebViewNewWindowRequestedEventArgs e)
    {
        e.Handled = true;
        if (e.Request is { } address)
            log($"The web view of {policy.PluginId} was kept from opening a window on {address}.");
    }

    private void OnWebMessageReceived(object? sender, WebMessageReceivedEventArgs e)
    {
        if (isDisposed || !IsOwnPage)
            return;

        if (e.Body == WebMessages.Ready)
        {
            isReady = true;
            Flush();
        }
        else if (WebMessages.IsJson(e.Body))
        {
            MessageReceived?.Invoke(this, new WebViewMessageEventArgs(e.Body!));
        }
    }
}
