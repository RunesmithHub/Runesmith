using System.Collections.Specialized;
using System.Composition;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using HammerUI;
using HammerUI.Controls;
using Runesmith.Sdk.Documents;
using Runesmith.Sdk.ToolWindows;
using Runesmith.Sdk.Workspace;
using Runesmith.Text;

namespace Runesmith.Shell.Running;

/// <summary>The Run tool window: a tab per run with its console, shown when a run starts.</summary>
[Export(typeof(IToolWindowProvider))]
[Export]
[Shared]
public sealed class RunToolWindow : IToolWindowProvider
{
    /// <summary>The tool window's id.</summary>
    public const string Id = "run";

    private readonly RunService runs;
    private readonly Lazy<IToolWindowManager> toolWindows;
    private readonly Lazy<IEditorService> editors;
    private readonly IWorkspace workspace;
    private readonly Dictionary<RunSession, RunConsoleView> views = [];
    private TabControl? tabs;
    private IReadOnlyList<string>? files;

    /// <summary>Creates the tool window, which shows itself when a run starts.</summary>
    [ImportingConstructor]
    public RunToolWindow(RunService runs, Lazy<IToolWindowManager> toolWindows, Lazy<IEditorService> editors, IWorkspace workspace)
    {
        this.runs = runs;
        this.toolWindows = toolWindows;
        this.editors = editors;
        this.workspace = workspace;
        workspace.Changed += (_, _) => files = null;
        runs.SessionStarted += (_, session) => UiThread.Run(() =>
        {
            toolWindows.Value.Show(Id);
            if (tabs is not null)
                tabs.SelectedItem = session;
        });
        runs.StateChanged += (_, _) => UiThread.Run(UpdateBadge);
    }

    public ToolWindowDefinition Definition { get; } = new(Id, "Run", "play", DockSide.Bottom) { Order = -1 };

    public Control CreateContent()
    {
        tabs = new TabControl
        {
            ItemsSource = runs.Sessions,
            Padding = new Thickness(0),
            ItemTemplate = new FuncDataTemplate<RunSession>((session, _) => session is null ? new Panel() : Header(session)),
            ContentTemplate = new FuncDataTemplate<RunSession>((session, _) => session is null ? new Panel() : View(session)),
        };
        if (runs.Sessions.Count > 0)
            tabs.SelectedIndex = runs.Sessions.Count - 1;

        var empty = new EmptyState
        {
            Icon = Icons.Play,
            Title = "Nothing has run yet",
            Hint = "Choose a run configuration in the toolbar and press Run, or Shift+F10.",
        };
        void Update() => empty.IsVisible = runs.Sessions.Count == 0;
        runs.Sessions.CollectionChanged += (_, e) =>
        {
            if (e.Action is NotifyCollectionChangedAction.Remove or NotifyCollectionChangedAction.Replace or NotifyCollectionChangedAction.Reset)
            {
                foreach (var gone in views.Keys.Where(s => !runs.Sessions.Contains(s)).ToList())
                    views.Remove(gone);
            }

            if (e.Action == NotifyCollectionChangedAction.Replace && e.NewItems?[0] is RunSession replaced)
                tabs.SelectedItem = replaced;
            Update();
        };
        Update();
        return new Panel { Children = { tabs, empty } };
    }

    private void UpdateBadge()
    {
        var active = runs.Sessions.Count(s => s.State != RunState.Finished);
        toolWindows.Value.SetBadge(Id, active == 0 ? null : active.ToString(System.Globalization.CultureInfo.CurrentCulture));
    }

    private RunConsoleView View(RunSession session)
    {
        if (views.TryGetValue(session, out var view))
            return view;

        var resolver = new ConsoleLinkResolver(() => [.. new[] { session.WorkingDirectory, workspace.RootPath }.OfType<string>()], Files);
        view = new RunConsoleView(session, resolver, Open, s => _ = runs.RunAsync(s.Configuration, s.Mode));
        views[session] = view;
        return view;
    }

    private StackPanel Header(RunSession session)
    {
        var icon = new SymbolIcon { Data = Icons.Find(session.Type?.Icon) ?? Icons.Play, Size = 14, VerticalAlignment = VerticalAlignment.Center };
        var name = new TextBlock { Text = session.Configuration.Name, VerticalAlignment = VerticalAlignment.Center };
        var dot = new Border { Width = 6, Height = 6, CornerRadius = new CornerRadius(3), VerticalAlignment = VerticalAlignment.Center };
        var close = new Button { Classes = { "icon", "small" }, Content = new SymbolIcon { Data = Icons.X, Size = 12 }, Padding = new Thickness(2), VerticalAlignment = VerticalAlignment.Center };
        ToolTip.SetTip(close, "Close; stops the run when it still runs");
        close.Click += (_, e) =>
        {
            e.Handled = true;
            runs.Remove(session);
        };

        void Update()
        {
            var key = session.State != RunState.Finished ? "SuccessBrush" : session.Succeeded || session.WasStopped ? null : "DangerBrush";
            dot.IsVisible = key is not null;
            if (key is not null && dot.TryFindResource(key, dot.ActualThemeVariant, out var brush) && brush is IBrush color)
                dot.Background = color;
        }

        session.StateChanged += (_, _) => UiThread.Run(Update);
        dot.AttachedToVisualTree += (_, _) => Update();
        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { icon, name, dot, close } };
        ToolTip.SetTip(header, session.Type is { } type ? $"{session.Configuration.Name} ({type.Name})" : session.Configuration.Name);
        return header;
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
            files = await workspace.GetFilesAsync().ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or OperationCanceledException)
        {
        }
    }

    private void Open(string path, int line, int column) =>
        _ = editors.Value.OpenAsync(path, new TextPosition(Math.Max(0, line - 1), Math.Max(0, column - 1)));
}
