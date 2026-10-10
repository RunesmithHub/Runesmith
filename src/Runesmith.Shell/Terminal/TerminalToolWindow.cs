using System.Collections.Specialized;
using System.Composition;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Threading;
using HammerUI;
using HammerUI.Controls;
using Runesmith.Sdk.Settings;
using Runesmith.Sdk.Shell;
using Runesmith.Sdk.Terminals;
using Runesmith.Sdk.ToolWindows;
using Runesmith.Sdk.Workspace;
using Runesmith.Shell.Commands;
using Runesmith.Shell.Editors;
using Runesmith.Shell.Running;
using Runesmith.Shell.Services;
using Runesmith.Shell.ToolWindows;
using Runesmith.Text;

namespace Runesmith.Shell.Terminal;

/// <summary>The Terminal tool window: a tab per terminal, each with the user's shell or a program, starting a shell when it first shows.</summary>
[Export(typeof(IToolWindowProvider))]
[Export]
[Shared]
public sealed class TerminalToolWindow : IToolWindowProvider
{
    /// <summary>The tool window's id.</summary>
    public const string Id = "terminal";

    private readonly TerminalService terminals;
    private readonly Lazy<IToolWindowManager> toolWindows;
    private readonly Lazy<EditorService> editors;
    private readonly IWorkspace workspace;
    private readonly ISettingsService settings;
    private readonly ThemeService theme;
    private readonly Lazy<CommandService> commands;
    private readonly INotificationService notifications;
    private TerminalPanel? panel;

    [ImportingConstructor]
    public TerminalToolWindow(TerminalService terminals, Lazy<IToolWindowManager> toolWindows, Lazy<EditorService> editors, IWorkspace workspace,
        ISettingsService settings, ThemeService theme, Lazy<CommandService> commands, INotificationService notifications)
    {
        TerminalIcons.Register();
        this.terminals = terminals;
        this.toolWindows = toolWindows;
        this.editors = editors;
        this.workspace = workspace;
        this.settings = settings;
        this.theme = theme;
        this.commands = commands;
        this.notifications = notifications;
        terminals.ShowRequested += (_, e) =>
        {
            toolWindows.Value.Show(Id);
            panel?.Select(e.Session, e.Focus);
        };
    }

    public ToolWindowDefinition Definition { get; } = new(Id, "Terminal", TerminalIcons.Terminal, DockSide.Bottom) { Order = 2, KeyBinding = "Ctrl+`" };

    /// <summary>Gets the terminal whose tab is selected, or null.</summary>
    internal TerminalSession? Active => panel?.Active;

    public Control CreateContent()
    {
        panel = new TerminalPanel(this);
        return panel;
    }

    /// <summary>Clears the selected terminal.</summary>
    internal void ClearActive() => panel?.ClearActive();

    /// <summary>Starts the user's shell in a new tab, telling the user when it cannot start.</summary>
    internal TerminalSession? NewShell(string? folder = null)
    {
        try
        {
            return terminals.CreateShell(folder);
        }
        catch (InvalidOperationException exception)
        {
            notifications.Notify(NotificationKind.Error, "The terminal could not start", exception.Message);
            return null;
        }
    }

    private TerminalPalette Palette()
    {
        var colors = theme.Current.Palette;
        var input = new TerminalPalette.ThemeInput(colors.IsDark, colors.Surface, colors.TextPrimary, colors.TextSecondary, colors.TextMuted, colors.Accent,
            colors.Success, colors.Warning, colors.Danger, colors.Info);
        IReadOnlyList<Color>? ansi = null;
        if (theme.Current.Theme.Colors.TerminalColors is { Count: 16 } given)
        {
            var parsed = given.Select(c => Color.TryParse(c, out var color) ? color : (Color?)null).ToList();
            if (parsed.All(c => c is not null))
                ansi = [.. parsed.Select(c => c!.Value)];
        }

        return new TerminalPalette(input, ansi);
    }

    private string FontFamily() => settings.Get<string>(TerminalSettings.FontFamily) is { Length: > 0 } family ? family : settings.Get<string>(SettingKeys.FontFamily);

    private double FontSize() => settings.Get<int>(TerminalSettings.FontSize) is > 0 and var size ? size : settings.Get<double>(SettingKeys.FontSize);

    private void Open(string path, int line, int column) =>
        _ = editors.Value.OpenAsync(path, new TextPosition(Math.Max(0, line - 1), Math.Max(0, column - 1)));

    /// <summary>The panel's content: the tabs, the terminal of the selected one and the header's buttons.</summary>
    private sealed class TerminalPanel : Panel, IToolWindowHeader
    {
        private readonly TerminalToolWindow owner;
        private readonly TabControl tabs;
        private readonly EmptyState empty;
        private readonly StackPanel actions;
        private readonly Dictionary<TerminalSession, TerminalHost> hosts = [];
        private IReadOnlyList<string>? files;
        private bool started;

        public TerminalPanel(TerminalToolWindow owner)
        {
            this.owner = owner;
            tabs = new TabControl
            {
                ItemsSource = owner.terminals.Tabs,
                Padding = new Thickness(0),
                ItemTemplate = new FuncDataTemplate<TerminalSession>((session, _) => session is null ? new Panel() : Header(session)),
                ContentTemplate = new FuncDataTemplate<TerminalSession>((session, _) => session is null ? new Panel() : Host(session)),
            };
            tabs.SelectionChanged += (_, _) =>
            {
                if (Active is { } session)
                    FocusLater(session);
            };
            empty = new EmptyState
            {
                Icon = Icons.Find(TerminalIcons.Terminal),
                Title = "No terminal is open",
                Hint = "Start your shell in the open folder.",
                ActionText = "New Terminal",
                ActionCommand = new Views.RelayCommand(() => owner.NewShell()),
            };
            actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
            actions.Children.Add(Action(Icons.Plus, "terminal.new", "New Terminal", () => owner.NewShell()));
            actions.Children.Add(Action(Icons.Eraser, "terminal.clear", "Clear", ClearActive));
            actions.Children.Add(Action(Icons.Trash, "terminal.kill", "Kill Terminal", () => Active?.Close()));
            Children.Add(tabs);
            Children.Add(empty);
            owner.terminals.Tabs.CollectionChanged += OnTabsChanged;
            owner.theme.Changed += (_, _) => ApplyAppearance();
            owner.settings.Changed += (_, e) =>
            {
                if (e.Key.StartsWith("terminal.", StringComparison.Ordinal) || e.Key is SettingKeys.FontFamily or SettingKeys.FontSize)
                    ApplyAppearance();
            };
            owner.workspace.Changed += (_, _) => files = null;
            AttachedToVisualTree += (_, _) =>
            {
                if (!started && owner.terminals.Tabs.Count == 0)
                    owner.NewShell();
                started = true;
                if (Active is { } session)
                    FocusLater(session);
            };
            Update();
        }

        public event EventHandler? HeaderChanged
        {
            add { }
            remove { }
        }

        public string? HeaderTitle => null;

        public Control HeaderActions => actions;

        public TerminalSession? Active => tabs.SelectedItem as TerminalSession;

        public void ClearActive() => Current?.View.Clear();

        private TerminalHost? Current => Active is { } session && hosts.TryGetValue(session, out var host) ? host : null;

        /// <summary>Selects a terminal's tab and, when asked, gives its terminal the keyboard focus.</summary>
        public void Select(TerminalSession session, bool focus)
        {
            tabs.SelectedItem = session;
            if (focus)
                FocusLater(session);
        }

        private void FocusLater(TerminalSession session) =>
            Dispatcher.UIThread.Post(() =>
            {
                if (hosts.TryGetValue(session, out var host) && host.IsEffectivelyVisible)
                    host.View.Focus();
            }, DispatcherPriority.Input);

        private void OnTabsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            foreach (var gone in hosts.Keys.Where(s => !owner.terminals.Tabs.Contains(s)).ToList())
                hosts.Remove(gone);
            if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems?[0] is TerminalSession added)
                tabs.SelectedItem = added;
            else if (tabs.SelectedItem is null && owner.terminals.Tabs.Count > 0)
                tabs.SelectedIndex = owner.terminals.Tabs.Count - 1;
            Update();
        }

        private void Update() => empty.IsVisible = owner.terminals.Tabs.Count == 0;

        private void ApplyAppearance()
        {
            var palette = owner.Palette();
            foreach (var host in hosts.Values)
                host.Apply(palette, owner.FontFamily(), owner.FontSize(), owner.settings.Get<bool>(TerminalSettings.CursorBlink));
        }

        private TerminalHost Host(TerminalSession session)
        {
            if (hosts.TryGetValue(session, out var host))
                return host;

            var resolver = new ConsoleLinkResolver(() => [.. new[] { session.CurrentDirectory, owner.workspace.RootPath }.OfType<string>()], Files);
            var view = new TerminalView(session, owner.Palette(), e => owner.commands.Value.FindByGesture(e) is not null, resolver, owner.Open, Launcher.OpenUrl);
            host = new TerminalHost(view);
            host.Apply(owner.Palette(), owner.FontFamily(), owner.FontSize(), owner.settings.Get<bool>(TerminalSettings.CursorBlink));
            hosts[session] = host;
            return host;
        }

        private IReadOnlyList<string>? Files()
        {
            if (files is { } known)
                return known;

            _ = LoadFilesAsync();
            return null;
        }

        private async Task LoadFilesAsync()
        {
            try
            {
                files = await owner.workspace.GetFilesAsync().ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or OperationCanceledException)
            {
            }
        }

        private StackPanel Header(TerminalSession session)
        {
            var icon = new SymbolIcon { Data = Icons.Find(TerminalIcons.Terminal), Size = 14, VerticalAlignment = VerticalAlignment.Center };
            var title = new TextBlock { Text = session.Title, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 220, TextTrimming = TextTrimming.CharacterEllipsis };
            var close = new Button { Classes = { "icon", "small" }, Content = new SymbolIcon { Data = Icons.X, Size = 12 }, Padding = new Thickness(2), VerticalAlignment = VerticalAlignment.Center };
            ToolTip.SetTip(close, "Close; ends the terminal's program");
            close.Click += (_, e) =>
            {
                e.Handled = true;
                session.Close();
            };

            var pending = 0;
            void Update()
            {
                Interlocked.Exchange(ref pending, 0);
                title.Text = session.Title;
                ToolTip.SetTip(title, session.HasExited ? $"{session.Title} (ended)" : $"{session.Title}\n{session.CurrentDirectory}");
                if (session.HasExited)
                    title[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextMutedBrush");
            }

            session.Changed += (_, _) =>
            {
                if (Interlocked.Exchange(ref pending, 1) == 0)
                    Dispatcher.UIThread.Post(Update, DispatcherPriority.Background);
            };
            session.Exited += (_, _) => Dispatcher.UIThread.Post(Update);
            Update();
            return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { icon, title, close } };
        }

        private Button Action(Geometry icon, string commandId, string title, Action action)
        {
            var button = new Button { Classes = { "icon", "small" }, Content = new SymbolIcon { Data = icon, Size = 14 } };
            ToolTip.SetTip(button, owner.commands.Value.GetKeyBinding(commandId) is { } keys ? $"{title} ({keys})" : title);
            button.Click += (_, _) => action();
            return button;
        }
    }

    /// <summary>A terminal and its scroll bar.</summary>
    private sealed class TerminalHost : DockPanel
    {
        private readonly ScrollBar scrollBar = new() { Orientation = Orientation.Vertical, Width = 12, AllowAutoHide = true, SmallChange = 1 };
        private bool updating;

        public TerminalHost(TerminalView view)
        {
            View = view;
            DockPanel.SetDock(scrollBar, Dock.Right);
            Children.Add(scrollBar);
            Children.Add(view);
            view.ScrollChanged += (_, _) => UpdateScrollBar();
            scrollBar.ValueChanged += (_, _) =>
            {
                if (!updating)
                    view.ScrollTo((int)Math.Round(scrollBar.Value));
            };
            UpdateScrollBar();
        }

        public TerminalView View { get; }

        public void Apply(TerminalPalette palette, string family, double size, bool blink)
        {
            View.SetPalette(palette);
            View.SetFont(family, size);
            View.SetCursorBlink(blink);
            Background = new SolidColorBrush(palette.Background);
        }

        private void UpdateScrollBar()
        {
            var (total, first, rows) = View.ScrollState;
            updating = true;
            scrollBar.Maximum = Math.Max(0, total - rows);
            scrollBar.ViewportSize = rows;
            scrollBar.LargeChange = Math.Max(1, rows - 1);
            scrollBar.Value = first;
            scrollBar.IsVisible = total > rows;
            updating = false;
        }
    }
}
