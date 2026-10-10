using System.Composition;
using Avalonia.Controls;
using Runesmith.Dap.Protocol;
using Runesmith.Sdk.Commands;
using Runesmith.Sdk.Shell;
using Runesmith.Sdk.ToolWindows;
using Runesmith.Sdk.Workspace;
using Runesmith.Shell.Editors;
using Runesmith.Shell.Services;
using Runesmith.Text;

namespace Runesmith.Shell.Debugging;

/// <summary>The Debug tool window, with the call stack, variables, watches, the debug console and the breakpoints; it shows when a session
/// starts, follows the paused line in the editor and keeps the status bar's debugging item.</summary>
[Export(typeof(IToolWindowProvider))]
[Export]
[Shared]
public sealed class DebugToolWindow : IToolWindowProvider
{
    /// <summary>The tool window's id.</summary>
    public const string Id = "debug";

    private readonly DebugService debug;
    private readonly BreakpointService breakpoints;
    private readonly Lazy<IToolWindowManager> toolWindows;
    private readonly Lazy<EditorService> editors;
    private readonly IWorkspace workspace;
    private readonly Lazy<ICommandService> commands;
    private readonly NotificationService notifications;
    private readonly IStatusBar? statusBar;
    private IStatusBarItem? statusItem;
    private DebugView? view;
    private StoppedEvent? followedStop;
    private int pending;

    [ImportingConstructor]
    public DebugToolWindow(DebugService debug, BreakpointService breakpoints, Lazy<IToolWindowManager> toolWindows, Lazy<EditorService> editors, IWorkspace workspace,
        Lazy<ICommandService> commands, NotificationService notifications, [Import(AllowDefault = true)] IStatusBar? statusBar)
    {
        DebugIcons.Register();
        this.debug = debug;
        this.breakpoints = breakpoints;
        this.toolWindows = toolWindows;
        this.editors = editors;
        this.workspace = workspace;
        this.commands = commands;
        this.notifications = notifications;
        this.statusBar = statusBar;
        debug.SessionStarted += (_, _) => UiThread.Run(() => toolWindows.Value.Show(Id));
        debug.Changed += (_, _) =>
        {
            // Sessions change on every step; one update per UI frame is enough.
            if (Interlocked.Exchange(ref pending, 1) == 0)
                Avalonia.Threading.Dispatcher.UIThread.Post(Update, Avalonia.Threading.DispatcherPriority.Input);
        };
    }

    public ToolWindowDefinition Definition { get; } = new(Id, "Debug", "bug", DockSide.Bottom) { Order = 0, KeyBinding = "Ctrl+Shift+D" };

    public Control CreateContent()
    {
        view = new DebugView(debug, breakpoints, commands.Value, notifications, () => workspace.RootPath, Navigate);
        return view;
    }

    /// <summary>Opens a frame's file at its line.</summary>
    internal void Navigate(StackFrame frame)
    {
        if (frame.Source?.Path is not { Length: > 0 } path || !File.Exists(path))
            return;

        _ = editors.Value.OpenAsync(path, new TextPosition(Math.Max(0, frame.Line - 1), Math.Max(0, frame.Column - 1)));
    }

    private void Update()
    {
        Interlocked.Exchange(ref pending, 0);
        var session = debug.Current;
        if (session is { State: DebugState.Paused, Stop: { } stop } && stop != followedStop)
        {
            followedStop = stop;
            if (session.CurrentFrame is { } frame)
                Navigate(frame);
        }

        UpdateStatus(session);
        view?.Refresh();
    }

    private void UpdateStatus(DebugSession? session)
    {
        if (statusBar is null)
            return;

        if (session is null)
        {
            if (statusItem is not null)
                statusItem.IsVisible = false;
            return;
        }

        statusItem ??= statusBar.AddItem("debug.state", StatusBarAlignment.Left, 50);
        statusItem.Icon = "bug";
        statusItem.CommandId = "view." + Id;
        statusItem.IsVisible = true;
        switch (session.State)
        {
            case DebugState.Paused:
                var where = session.CurrentFrame is { Source.Path: { Length: > 0 } path } frame ? $" at {Path.GetFileName(path)}:{frame.Line}" : "";
                statusItem.Text = $"Paused{where}";
                statusItem.State = StatusBarState.Warning;
                statusItem.ToolTip = $"{session.Name} is paused{(session.Stop?.Reason is { } reason ? $" ({Reason(reason)})" : "")}. Press F5 to continue.";
                break;
            case DebugState.Starting:
                statusItem.Text = $"Starting {session.DebuggerName}";
                statusItem.State = StatusBarState.Busy;
                statusItem.ToolTip = $"{session.DebuggerName} is starting {session.Name}.";
                break;
            default:
                statusItem.Text = $"Debugging {session.Name}";
                statusItem.State = StatusBarState.Normal;
                statusItem.ToolTip = $"{session.Name} runs under {session.DebuggerName}.";
                break;
        }
    }

    internal static string Reason(string reason) => reason switch
    {
        "breakpoint" => "at a breakpoint",
        "step" => "after a step",
        "exception" => "on an exception",
        "pause" => "on request",
        "entry" => "at the start",
        _ => reason,
    };
}
