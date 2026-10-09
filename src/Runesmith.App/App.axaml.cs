using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Runesmith.Shell;
using Runesmith.Shell.Hub;
using Runesmith.Shell.Services;
using Runesmith.Shell.Views;

namespace Runesmith.App;

public sealed class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            Dispatcher.UIThread.UnhandledException += (_, e) =>
            {
                var log = CrashLog.Write(e.Exception, "An error in the user interface");
                e.Handled = true;
                Shell?.Notify(e.Exception, log);
            };

            // The composition started on a thread-pool thread before Avalonia, so waiting for it here cannot deadlock.
            var composition = Program.Composition.GetAwaiter().GetResult();
            var options = new StartupOptions(Program.Arguments.Paths, Environment.CurrentDirectory, Program.Arguments.IsDiagnosticsEnabled)
            {
                IsSafeMode = Program.IsSafeMode,
                HubStartup = Program.HubReport,
                Link = Program.Arguments.Link,
            };
            var window = ShellStartup.CreateMainWindow(composition, options, Program.Timings);
            window.Icon = new WindowIcon(new Bitmap(AssetLoader.Open(new Uri("avares://Runesmith.Shell/Assets/runesmith.png"))));
            var notifications = composition.Exports.GetExportedValue<NotificationService>();
            Shell = new ShellHandle(notifications);
            desktop.MainWindow = window;
            desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;

            var restart = composition.Exports.GetExportedValue<AppRestart>();
            restart.Handler = safeMode => _ = window.CloseAsync(() => Program.RestartArguments = safeMode ? ["--safe-mode"] : []);
            window.Opened += async (_, _) =>
            {
                Program.Guard.Shown();
                var stable = new DispatcherTimer { Interval = StartupGuard.StableAfter };
                stable.Tick += (_, _) =>
                {
                    stable.Stop();
                    Program.Guard.Stable();
                };
                stable.Start();
                await ShellStartup.OnShownAsync(composition.Exports, options);
                if (Program.Guard.Offer is { } offer)
                    await SafeModeDialog.ShowAsync(notifications.Dialogs, offer.Reason, [.. offer.Plugins.Select(p => (p.Name, p.Version))], () => restart.Restart(safeMode: false));
            };

            if (Program.Instance is { } instance)
            {
                var workbench = composition.Exports.GetExportedValue<Workbench>();
                instance.Received += (paths, folder) => Dispatcher.UIThread.Post(async () =>
                {
                    Bring(window);
                    await workbench.OpenArgumentsAsync(paths, folder);
                });
                var hub = composition.Exports.GetExportedValue<HubService>();
                instance.LinkReceived += link => Dispatcher.UIThread.Post(() =>
                {
                    Bring(window);
                    hub.OpenLink(link);
                });
                instance.Listen();
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    private ShellHandle? Shell { get; set; }

    private static void Bring(MainWindow window)
    {
        window.Activate();
        if (window.WindowState == WindowState.Minimized)
            window.WindowState = WindowState.Normal;
    }

    private sealed class ShellHandle(NotificationService notifications)
    {
        public void Notify(Exception exception, string? log) =>
            notifications.Notify(Sdk.Shell.NotificationKind.Error, "Something went wrong", exception.Message + (log is null ? "" : $" Details are in {log}."));
    }
}
