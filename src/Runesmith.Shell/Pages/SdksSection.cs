using System.Collections.Concurrent;
using System.Composition;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using HammerUI;
using HammerUI.Controls;
using Runesmith.Sdk.Sdks;
using Runesmith.Sdk.Shell;
using Runesmith.Sdk.Tasks;
using Runesmith.Shell.Services;

namespace Runesmith.Shell.Pages;

/// <summary>The SDKs category of the settings: per kind, the SDKs found or installed, their updates, and downloads.</summary>
[Export]
[Shared]
public sealed class SdksSection
{
    /// <summary>The settings category it fills.</summary>
    public const string Category = SdkSettings.Category;

    private readonly SdkService sdks;
    private readonly NotificationService notifications;
    private readonly IBackgroundTasks? tasks;
    private readonly ConcurrentDictionary<string, Task<IReadOnlyList<SdkRelease>>> releases = new(StringComparer.Ordinal);

    [ImportingConstructor]
    public SdksSection(SdkService sdks, NotificationService notifications, [Import(AllowDefault = true)] IBackgroundTasks? tasks)
    {
        this.sdks = sdks;
        this.notifications = notifications;
        this.tasks = tasks;
    }

    /// <summary>Creates the section's content; it reaches the vendors' services to look for updates.</summary>
    public Control Create()
    {
        var panel = new StackPanel { Spacing = 24 };
        panel.Children.Add(new TextBlock { Text = Category, FontSize = 16, FontWeight = FontWeight.SemiBold });
        if (sdks.Providers.Count == 0)
            panel.Children.Add(new TextBlock { Text = "No plugin supplies SDKs.", Classes = { "muted" } });
        foreach (var provider in sdks.Providers.OrderBy(p => p.Name, StringComparer.CurrentCulture))
            panel.Children.Add(new KindView(this, provider));
        return panel;
    }

    /// <summary>Gets the releases of a kind, downloaded once per run; a failed download is tried again next time.</summary>
    internal Task<IReadOnlyList<SdkRelease>> ReleasesAsync(ISdkProvider provider)
    {
        var task = releases.GetOrAdd(provider.Kind, _ => provider.GetReleasesAsync(CancellationToken.None));
        if (task.IsFaulted || task.IsCanceled)
        {
            releases.TryRemove(new KeyValuePair<string, Task<IReadOnlyList<SdkRelease>>>(provider.Kind, task));
            task = releases.GetOrAdd(provider.Kind, _ => provider.GetReleasesAsync(CancellationToken.None));
        }

        return task;
    }

    /// <summary>Installs a release, showing it in the status bar and reporting the result; the error, if any, is returned too.</summary>
    internal async Task<(bool Succeeded, string? Error)> InstallAsync(ISdkProvider provider, SdkRelease release, Action<SdkInstallProgress>? report, CancellationTokenSource cancel)
    {
        var name = Describe(provider, release);
        using var task = tasks?.Start($"Installing {name}", cancel.Cancel);
        var progress = new Progress<SdkInstallProgress>(p =>
        {
            report?.Invoke(p);
            task?.Report(p.Fraction is { } f ? $"{p.Stage} {f:P0}" : p.Stage, p.Fraction);
        });
        try
        {
            var sdk = await provider.InstallAsync(release, progress, cancel.Token);
            await sdks.RefreshAsync();
            notifications.Notify(NotificationKind.Success, $"{name} is installed", sdk.Path);
            return (true, null);
        }
        catch (OperationCanceledException)
        {
            return (false, null);
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or InvalidDataException or UnauthorizedAccessException or NotSupportedException
            or InvalidOperationException)
        {
            notifications.Notify(NotificationKind.Error, $"Could not install {name}", exception.Message);
            return (false, exception.Message);
        }
    }

    /// <summary>Gets the name of a release in messages, such as ".NET SDK 10.0.401" or "Temurin 25.0.1".</summary>
    internal static string Describe(ISdkProvider provider, SdkRelease release) =>
        release.Kind == "dotnet" ? $"{provider.Name} {release.Version}" : $"{release.Vendor} {release.Version}";

    /// <summary>Gets a size in bytes as people read it, such as "212 MB".</summary>
    internal static string FormatSize(long bytes) => bytes switch
    {
        >= 1L << 30 => (bytes / (double)(1L << 30)).ToString("0.0 GB", CultureInfo.CurrentCulture),
        >= 1L << 20 => (bytes / (double)(1L << 20)).ToString("0 MB", CultureInfo.CurrentCulture),
        _ => (bytes / 1024.0).ToString("0 KB", CultureInfo.CurrentCulture),
    };

    internal static Badge Badge(string text, string? kind = null)
    {
        var badge = new Badge { Content = text, VerticalAlignment = VerticalAlignment.Center };
        if (kind is not null)
            badge.Classes.Add(kind);
        return badge;
    }

    /// <summary>One kind's SDKs, kept in step with the SDK service while it is shown.</summary>
    private sealed class KindView : StackPanel
    {
        private readonly SdksSection owner;
        private readonly ISdkProvider provider;
        private readonly StackPanel rows = new() { Spacing = 4 };
        private readonly TextBlock status = new() { Classes = { "caption", "muted" }, TextWrapping = TextWrapping.Wrap, IsVisible = false };
        private int generation;

        public KindView(SdksSection owner, ISdkProvider provider)
        {
            this.owner = owner;
            this.provider = provider;
            Spacing = 10;

            var download = new Button { Classes = { "small" }, Content = "Download...", VerticalAlignment = VerticalAlignment.Center };
            download.Click += (_, _) => _ = owner.notifications.Dialogs.ShowAsync(new SdkDownloadDialog(owner, provider).Dialog);
            var refresh = new Button { Classes = { "subtle", "small" }, Content = "Refresh", VerticalAlignment = VerticalAlignment.Center };
            ToolTip.SetTip(refresh, $"Look for installed {provider.Name}s again.");
            refresh.Click += (_, _) => _ = owner.sdks.RefreshAsync();
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { refresh, download } };
            var header = new DockPanel();
            DockPanel.SetDock(buttons, Dock.Right);
            header.Children.Add(buttons);
            header.Children.Add(new TextBlock { Text = provider.Name, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center });
            Children.Add(header);
            Children.Add(new Border { Classes = { "card" }, Child = rows });
            Children.Add(status);
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            owner.sdks.Changed += OnChanged;
            _ = FillAsync();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            owner.sdks.Changed -= OnChanged;
            base.OnDetachedFromVisualTree(e);
        }

        private void OnChanged(object? sender, EventArgs e) => UiThread.Run(() => _ = FillAsync());

        private async Task FillAsync()
        {
            var current = ++generation;
            var installed = await owner.sdks.GetInstalledAsync(provider.Kind);
            var chosen = await owner.sdks.GetDefaultAsync(provider.Kind);
            if (current != generation)
                return;

            rows.Children.Clear();
            var updates = new List<(InstalledSdk Sdk, Panel Actions)>();
            foreach (var sdk in installed)
            {
                var (row, actions) = Row(sdk, sdk == chosen);
                rows.Children.Add(row);
                updates.Add((sdk, actions));
            }

            if (installed.Count == 0)
                rows.Children.Add(new TextBlock { Text = $"No {provider.Name} was found.", Classes = { "muted" }, Margin = new Thickness(12) });

            try
            {
                var available = await owner.ReleasesAsync(provider);
                if (current != generation)
                    return;

                status.IsVisible = false;
                foreach (var (sdk, actions) in updates)
                {
                    if (provider.FindUpdate(sdk, available) is { } update)
                        actions.Children.Insert(0, UpdateButton(update));
                }
            }
            catch (Exception exception) when (exception is HttpRequestException or IOException or System.Text.Json.JsonException or TaskCanceledException)
            {
                if (current != generation)
                    return;

                status.Text = $"Could not look for updates: {exception.Message}";
                status.IsVisible = true;
            }
        }

        private (SettingRow Row, Panel Actions) Row(InstalledSdk sdk, bool isDefault)
        {
            var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
            actions.Children.Add(sdk.Source == SdkSource.Managed ? Badge("Runesmith", "accent") : Badge("System"));

            var makeDefault = new RadioButton { Content = "Default", GroupName = $"sdk-default-{provider.Kind}", IsChecked = isDefault, VerticalAlignment = VerticalAlignment.Center };
            ToolTip.SetTip(makeDefault, $"Builds and new projects use this {provider.Name}.");
            makeDefault.IsCheckedChanged += (_, _) =>
            {
                if (makeDefault.IsChecked == true && !owner.sdks.IsChosenDefault(sdk))
                    owner.sdks.SetDefault(sdk);
            };
            actions.Children.Add(makeDefault);

            if (sdk.Source == SdkSource.Managed)
            {
                var remove = new Button { Classes = { "icon", "small" }, Content = new SymbolIcon { Data = Icons.Trash, Size = 14 }, VerticalAlignment = VerticalAlignment.Center };
                ToolTip.SetTip(remove, "Remove");
                remove.Click += (_, _) => _ = RemoveAsync(sdk);
                actions.Children.Add(remove);
            }

            var title = sdk.Kind == "dotnet" ? sdk.Version : sdk.DisplayName;
            var description = sdk.Architecture is null ? sdk.Path : $"{sdk.Path}  ({sdk.Architecture})";
            return (new SettingRow { Icon = Icons.Package, Title = title, Description = description, Content = actions }, actions);
        }

        private Button UpdateButton(SdkRelease update)
        {
            var button = new Button { Classes = { "small", "accent" }, Content = $"Update to {update.Version}", VerticalAlignment = VerticalAlignment.Center };
            ToolTip.SetTip(button, "Installs the newer version next to this one; remove the older one when nothing needs it.");
            button.Click += async (_, _) =>
            {
                button.IsEnabled = false;
                using var cancel = new CancellationTokenSource();
                await owner.InstallAsync(provider, update, p => button.Content = p.Fraction is { } f ? $"{p.Stage} {f:P0}" : p.Stage, cancel);
                button.Content = $"Update to {update.Version}";
                button.IsEnabled = true;
            };
            return button;
        }

        private async Task RemoveAsync(InstalledSdk sdk)
        {
            var name = sdk.Kind == "dotnet" ? $"{provider.Name} {sdk.Version}" : sdk.DisplayName;
            if (!await owner.notifications.ConfirmAsync($"Remove {name}?", $"Runesmith deletes it from {sdk.Path}.", "Remove", isDestructive: true))
                return;

            try
            {
                await provider.UninstallAsync(sdk, CancellationToken.None);
                await owner.sdks.RefreshAsync();
                owner.notifications.Notify(NotificationKind.Success, $"{name} is removed");
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                owner.notifications.Notify(NotificationKind.Error, $"Could not remove {name}", exception.Message);
            }
        }
    }
}
