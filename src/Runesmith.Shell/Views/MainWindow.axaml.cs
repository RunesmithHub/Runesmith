using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HammerUI.Docking;
using Microsoft.VisualStudio.Composition;
using Runesmith.Sdk.Build;
using Runesmith.Sdk.Commands;
using Runesmith.Sdk.Languages;
using Runesmith.Sdk.Settings;
using Runesmith.Sdk.Workspace;
using Runesmith.Shell.Commands;
using Runesmith.Shell.Diffs;
using Runesmith.Shell.Docking;
using Runesmith.Shell.Editors;
using Runesmith.Shell.Pages;
using Runesmith.Shell.Services;
using Runesmith.Shell.Session;
using Runesmith.Shell.ToolWindows;
using Runesmith.Workspace.Files;

namespace Runesmith.Shell.Views;

/// <summary>The main window: the toolbar, the tool window stripes and areas around the editor area, the welcome screen while nothing is open,
/// and the status bar. It routes key bindings to commands, also in floating panel windows, and asks before closing with unsaved changes.</summary>
public partial class MainWindow : Window
{
    // These keys edit text, so text boxes and the editor handle them themselves; the window never takes them.
    private static readonly HashSet<string> TextCommands = new(StringComparer.Ordinal)
    {
        CommandIds.Undo, CommandIds.Redo, CommandIds.Cut, CommandIds.Copy, CommandIds.Paste, CommandIds.SelectAll,
        CoreCommands.MoveLinesUp, CoreCommands.MoveLinesDown, CoreCommands.DuplicateLines,
    };

    private readonly CommandService? commands;
    private readonly EditorService? editors;
    private readonly DiffService? diffs;
    private readonly IWorkspace? workspace;
    private readonly ShellLayout? layout;
    private readonly Workbench? workbench;
    private readonly ILanguageRegistry? languages;
    private readonly NotificationService? notifications;
    private readonly ISettingsService? settings;
    private readonly ToolWindowManager? toolWindows;
    private readonly BackgroundTaskService? tasks;
    private readonly RecentFolders? recent;
    private readonly PageService? pages;
    private readonly ShellController? controller;
    private readonly Hub.HubService? hub;
    private readonly OutputService? output;
    private readonly ThemeService? themes;
    private readonly WorkbenchView? workbenchView;
    private readonly DispatcherTimer menuRefresh = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private WelcomePage? welcome;
    private bool welcomeRequested;
    private bool closeApproved;

    /// <summary>Parameterless constructor for the XAML previewer.</summary>
    public MainWindow() => InitializeComponent();

    public MainWindow(ExportProvider exports)
    {
        ArgumentNullException.ThrowIfNull(exports);
        InitializeComponent();
        commands = exports.GetExportedValue<CommandService>();
        editors = exports.GetExportedValue<EditorService>();
        diffs = exports.GetExportedValue<DiffService>();
        workspace = exports.GetExportedValue<IWorkspace>();
        layout = exports.GetExportedValue<ShellLayout>();
        workbench = exports.GetExportedValue<Workbench>();
        languages = exports.GetExportedValue<ILanguageRegistry>();
        notifications = exports.GetExportedValue<NotificationService>();
        settings = exports.GetExportedValue<ISettingsService>();
        toolWindows = exports.GetExportedValue<ToolWindowManager>();
        tasks = exports.GetExportedValue<BackgroundTaskService>();
        recent = exports.GetExportedValue<RecentFolders>();
        pages = exports.GetExportedValue<PageService>();
        var explorer = exports.GetExportedValue<ExplorerToolWindow>();
        var statusBar = exports.GetExportedValue<StatusBarService>();
        controller = exports.GetExportedValue<ShellController>();
        controller.Window = this;
        hub = exports.GetExportedValue<Hub.HubService>();
        output = exports.GetExportedValue<OutputService>();
        themes = exports.GetExportedValue<ThemeService>();
        hub.ShowRequested += (_, _) => ShowPlugins();

        notifications.Host.Attach(this, Dialogs, Toasts);
        commands.Initialize();
        toolWindows.SetGestures(commands.GetKeyBinding);

        Workspace.ContentProvider = layout;
        Workspace.Layout = layout.Layout;
        Workspace.PanelClosing += OnPanelClosing;
        Workspace.WindowOpened += OnDockWindowOpened;
        Body.Children.Remove(EditorArea);
        workbenchView = new WorkbenchView(toolWindows, EditorArea);
        Body.Children.Insert(0, workbenchView);
        layout.Layout.Changed += (_, _) => UpdateBody();
        pages.WelcomeRequested += (_, _) =>
        {
            welcomeRequested = true;
            UpdateBody();
        };

        statusPath = new PathBreadcrumbs(explorer.Reveal);
        StatusPathHost.Content = statusPath;
        LeftItems.ItemsSource = statusBar.Left;
        RightItems.ItemsSource = statusBar.Right;

        BuildMenus();
        menuRefresh.Tick += (_, _) =>
        {
            menuRefresh.Stop();
            BuildMenus();
            toolWindows.SetGestures(commands.GetKeyBinding);
            UpdateSearchKeys();
        };
        commands.Changed += (_, _) =>
        {
            menuRefresh.Stop();
            menuRefresh.Start();
        };

        WireToolbar();
        RunWidgetHost.Content = Running.RunWidget.Create(exports);
        AddToolbarWidgets(exports.GetExportedValue<ToolbarWidgets>());
        WireStatusBar();
        editors.ActiveEditorChanged += (_, _) => FollowActiveEditor();
        workspace.Changed += (_, _) => UiThread.Run(() =>
        {
            welcomeRequested = false;
            UpdateProject();
            UpdateBody();
            UpdatePath();
        });
        settings.Changed += (_, e) =>
        {
            if (e.Key is SettingKeys.TabSize or SettingKeys.InsertSpaces)
                UpdateEditorInfo();
            else if (e.Key == ShellSettings.MenuBar)
                ApplyMenuBarMode();
            else if (e.Key == ShellSettings.Breadcrumbs)
                UpdatePath();
        };

        AddHandler(KeyDownEvent, OnShortcutKeyDown, RoutingStrategies.Bubble);
        AddHandler(KeyDownEvent, OnAnyKeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(KeyUpEvent, OnAnyKeyUp, RoutingStrategies.Tunnel, handledEventsToo: true);
        Deactivated += (_, _) => editors.OnWindowDeactivated();
        SetUpTitleBar();
        ApplyMenuBarMode();
        RestoreBounds();
        UpdateProject();
        UpdateBody();
        FollowActiveEditor();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (editors is null || e.Cancel || closeApproved)
            return;

        e.Cancel = true;
        _ = CloseAsync();
    }

    /// <summary>Closes the window after asking about unsaved changes; returns false when the user kept it open.</summary>
    /// <param name="closing">Runs once the user agreed, just before the window closes.</param>
    public async Task<bool> CloseAsync(Action? closing = null)
    {
        if (!closeApproved && editors is not null)
        {
            if (!await editors.ConfirmLosingChangesAsync())
                return false;

            workbench!.SaveSession();
            workbenchView!.StoreSizes();
            toolWindows!.Save();
            SaveBounds();
        }

        closeApproved = true;
        closing?.Invoke();
        Close();
        return true;
    }

    private void OnShortcutKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled || commands is null || Dialogs.IsOpen)
            return;

        if (commands.FindByGesture(e) is not { } id || TextCommands.Contains(id))
            return;

        // In a text box, plain typing belongs to the box; only gestures with a modifier or a function key run commands.
        var focused = (sender as TopLevel)?.FocusManager?.GetFocusedElement();
        var isTextBox = focused is Visual visual && (visual is TextBox || visual.FindAncestorOfType<TextBox>() is not null);
        if (isTextBox && (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta | KeyModifiers.Alt)) == 0 && e.Key is not (>= Key.F1 and <= Key.F24))
            return;

        _ = commands.ExecuteAsync(id);
        e.Handled = true;
    }

    private void OnDockWindowOpened(object? sender, DockWindowOpenedEventArgs e)
    {
        e.Window.Icon = Icon;
        e.Window.AddHandler(KeyDownEvent, OnShortcutKeyDown, RoutingStrategies.Bubble);
    }

    private void OnPanelClosing(object? sender, DockPanelClosingEventArgs e)
    {
        if (DiffService.IsDiffPanel(e.PanelId))
        {
            e.Cancel = true;
            _ = diffs!.CloseAsync(e.PanelId);
        }
        else if (ShellLayout.IsDocumentPanel(e.PanelId))
        {
            e.Cancel = true;
            _ = editors!.ClosePanelAsync(e.PanelId);
        }
    }

    // The welcome screen takes the window while no folder and nothing in the editor area is open, or when asked for until something opens.
    private void UpdateBody()
    {
        var hasPanels = layout!.Layout.Panels.Any();
        if (hasPanels && workspace!.RootPath is null)
            welcomeRequested = false;

        var showWelcome = welcomeRequested || (workspace!.RootPath is null && !hasPanels);
        if (showWelcome && welcome is null)
        {
            welcome = new WelcomePage(commands!, recent!, workbench!, settings!, themes!, () => new Hub.PluginManagerView(hub!, notifications!, output!, compact: false, () => pages!.ShowSettings(Hub.HubSettings.Category)), () =>
            {
                welcomeRequested = false;
                UpdateBody();
            });
            WelcomeHost.Content = welcome;
        }

        welcome?.CanClose = workspace!.RootPath is not null || hasPanels;
        WelcomeHost.IsVisible = showWelcome;
        workbenchView!.IsVisible = !showWelcome;
        EmptyEditor.IsVisible = !hasPanels;
        Workspace.IsVisible = hasPanels;
        UpdateEmptyEditorHints();
    }

    // The welcome screen's Plugins page shows the plugin manager while the welcome screen is up; otherwise the Plugins tool window does.
    private void ShowPlugins()
    {
        if (WelcomeHost.IsVisible && welcome is not null)
            welcome.ShowPlugins();
        else
            toolWindows!.Show(Hub.HubService.ToolWindowId);
        Activate();
    }

    private void RestoreBounds()
    {
        if (SessionStore.LoadWindow() is not { } session || session.Width < MinWidth || session.Height < MinHeight)
            return;

        var position = new PixelPoint((int)session.X, (int)session.Y);
        var onScreen = Screens.All.Any(s => s.WorkingArea.Contains(position + new PixelPoint(40, 40)));
        Width = session.Width;
        Height = session.Height;
        if (onScreen)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Position = position;
        }

        if (session.IsMaximized)
            WindowState = WindowState.Maximized;
    }

    private void SaveBounds()
    {
        var maximized = WindowState == WindowState.Maximized;
        var size = maximized ? new Size(Width, Height) : ClientSize;
        SessionStore.SaveWindow(new WindowSession(Position.X, Position.Y, size.Width, size.Height, maximized, workspace?.RootPath));
    }
}
