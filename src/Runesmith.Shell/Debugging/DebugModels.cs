using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using Runesmith.Dap;
using Runesmith.Dap.Protocol;
using Runesmith.Shell.Running;

namespace Runesmith.Shell.Debugging;

/// <summary>A base for the debugger's models that tell views when a property changes.</summary>
public abstract class DebugModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }
}

/// <summary>A row of the Variables or Watch tree: a scope, a variable, a watch expression or its children. Children load when the row first
/// expands.</summary>
public sealed class VariableNode : DebugModel
{
    private readonly VariableTree? tree;
    private bool isExpanded;
    private Task? loading;
    private string value;
    private string? type;
    private int reference;
    private bool isError;

    internal VariableNode(VariableTree? tree, string path, string name, string value, string? type, int reference, bool isScope = false, bool isPlaceholder = false)
    {
        this.tree = tree;
        Path = path;
        Name = name;
        this.value = value;
        this.type = type;
        this.reference = reference;
        IsScope = isScope;
        IsPlaceholder = isPlaceholder;
        if (reference > 0)
            Children.Add(Placeholder(tree, path));
    }

    /// <summary>Gets where the row is in its tree, such as <c>Locals/person/Name</c>, so expansion can be kept from one pause to the next.</summary>
    public string Path { get; }

    public string Name { get; }

    public string Value
    {
        get => value;
        internal set => Set(ref this.value, value);
    }

    public string? Type
    {
        get => type;
        internal set => Set(ref type, value);
    }

    /// <summary>Gets the reference to ask for the children with, or 0 when there are none.</summary>
    public int Reference => reference;

    public bool HasChildren => reference > 0;

    public bool IsScope { get; }

    /// <summary>Gets whether this is the stand-in child that shows a row can expand before its children load.</summary>
    public bool IsPlaceholder { get; }

    /// <summary>Gets whether <see cref="Value"/> is an error message, such as for a watch that cannot be evaluated.</summary>
    public bool IsError
    {
        get => isError;
        internal set => Set(ref isError, value);
    }

    public ObservableCollection<VariableNode> Children { get; } = [];

    /// <summary>Gets or sets whether the row is open; opening it the first time loads its children.</summary>
    public bool IsExpanded
    {
        get => isExpanded;
        set
        {
            if (!Set(ref isExpanded, value))
                return;

            tree?.OnExpanded(this, value);
            if (value && HasChildren)
                _ = LoadChildrenAsync();
        }
    }

    /// <summary>Loads the children once, then opens those that were open before.</summary>
    public Task LoadChildrenAsync() => tree is null || !HasChildren ? Task.CompletedTask : loading ??= LoadAsync(tree);

    private async Task LoadAsync(VariableTree tree)
    {
        IReadOnlyList<Variable> variables;
        try
        {
            variables = await tree.FetchAsync(reference);
        }
        catch (Exception exception) when (exception is DebugAdapterException or IOException)
        {
            Children.Clear();
            Children.Add(new VariableNode(tree, Path + "/!", exception.Message, "", null, 0) { IsError = true });
            return;
        }

        Children.Clear();
        foreach (var variable in variables)
            Children.Add(tree.Create(Path, variable.Name, variable.Value, variable.Type, variable.VariablesReference));
        await tree.RestoreAsync(Children);
    }

    internal void Replace(string newValue, string? newType, int newReference, bool error)
    {
        Value = newValue;
        Type = newType;
        IsError = error;
        if (newReference == reference && !HasChildren)
            return;

        reference = newReference;
        loading = null;
        Children.Clear();
        if (reference > 0)
            Children.Add(Placeholder(tree, Path));
        if (isExpanded && reference > 0)
            _ = LoadChildrenAsync();
    }

    private static VariableNode Placeholder(VariableTree? tree, string path) => new(tree, path + "/…", "Loading…", "", null, 0, isPlaceholder: true);
}

/// <summary>What the rows of one tree share: how to get children, and which rows were open, so a step keeps them open.</summary>
internal sealed class VariableTree(Func<int, Task<IReadOnlyList<Variable>>> fetch)
{
    private readonly HashSet<string> expanded = new(StringComparer.Ordinal);

    public Func<int, Task<IReadOnlyList<Variable>>> Fetch { get; set; } = fetch;

    public Task<IReadOnlyList<Variable>> FetchAsync(int reference) => Fetch(reference);

    public VariableNode Create(string parent, string name, string value, string? type, int reference, bool isScope = false) =>
        new(this, parent.Length == 0 ? name : parent + "/" + name, name, value, type, reference, isScope);

    public bool WasExpanded(string path) => expanded.Contains(path);

    public void OnExpanded(VariableNode node, bool isExpanded)
    {
        if (node.IsPlaceholder)
            return;
        if (isExpanded)
            expanded.Add(node.Path);
        else
            expanded.Remove(node.Path);
    }

    /// <summary>Opens the rows that were open before.</summary>
    public async Task RestoreAsync(IEnumerable<VariableNode> nodes)
    {
        foreach (var node in nodes.Where(n => n.HasChildren && WasExpanded(n.Path)).ToList())
        {
            node.IsExpanded = true;
            await node.LoadChildrenAsync();
        }
    }
}

/// <summary>The Variables pane: the current frame's scopes and their variables.</summary>
public sealed class VariablesModel : DebugModel
{
    private readonly DebugService debug;
    private readonly VariableTree tree;
    private StackFrame? shown;
    private DebugSession? session;
    private string? message = "Variables show while the program is paused.";

    public VariablesModel(DebugService debug)
    {
        this.debug = debug;
        tree = new VariableTree(_ => Task.FromResult<IReadOnlyList<Variable>>([]));
    }

    public ObservableCollection<VariableNode> Roots { get; } = [];

    /// <summary>Gets what the pane says instead of variables, or null while it shows them.</summary>
    public string? Message
    {
        get => message;
        private set => Set(ref message, value);
    }

    /// <summary>Shows the current frame's scopes, when the frame changed since the last time.</summary>
    public async Task RefreshAsync()
    {
        var current = debug.Current;
        var frame = current is { State: DebugState.Paused } ? current.CurrentFrame : null;
        if (frame == shown && current == session)
            return;

        shown = frame;
        session = current;
        if (current is null || frame is null)
        {
            Roots.Clear();
            Message = current is { State: DebugState.Running or DebugState.Starting } ? "The program is running." : "Variables show while the program is paused.";
            return;
        }

        tree.Fetch = reference => current.GetVariablesAsync(reference);
        IReadOnlyList<Scope> scopes;
        try
        {
            scopes = await current.GetScopesAsync(frame);
        }
        catch (Exception exception) when (exception is DebugAdapterException or IOException)
        {
            Roots.Clear();
            Message = exception.Message;
            return;
        }

        if (frame != shown)
            return;

        Roots.Clear();
        var first = true;
        foreach (var scope in scopes)
        {
            var node = tree.Create("", scope.Name, "", null, scope.VariablesReference, isScope: true);
            Roots.Add(node);
            if (tree.WasExpanded(node.Path) || first && !scope.Expensive)
                node.IsExpanded = true;
            first = false;
        }

        Message = Roots.Count == 0 ? "This frame has no variables." : null;
        await Task.WhenAll(Roots.Where(r => r.IsExpanded).Select(r => r.LoadChildrenAsync()));
    }
}

/// <summary>The Watch pane: the user's expressions, evaluated in the current frame each time the program pauses.</summary>
public sealed class WatchesModel : DebugModel
{
    private const string NotPaused = "Available while paused";

    private readonly DebugService debug;
    private readonly BreakpointService store;
    private readonly VariableTree tree;
    private StackFrame? evaluatedIn;

    public WatchesModel(DebugService debug, BreakpointService store)
    {
        this.debug = debug;
        this.store = store;
        tree = new VariableTree(_ => Task.FromResult<IReadOnlyList<Variable>>([]));
        Rebuild();
    }

    public ObservableCollection<VariableNode> Items { get; } = [];

    public void Add(string expression) => store.AddWatch(expression);

    public void Edit(int index, string expression) => store.ReplaceWatch(index, expression);

    public void Remove(int index) => store.RemoveWatch(index);

    /// <summary>Evaluates the watches again when the frame changed, or always with <paramref name="force"/>, as after the list changed.</summary>
    public async Task RefreshAsync(bool force = false)
    {
        if (force || Items.Count != store.Watches.Count)
            Rebuild();

        var current = debug.Current;
        var frame = current is { State: DebugState.Paused } ? current.CurrentFrame : null;
        if (!force && frame == evaluatedIn)
            return;

        evaluatedIn = frame;
        if (current is null || frame is null)
        {
            foreach (var item in Items)
                item.Replace(NotPaused, null, 0, error: false);
            return;
        }

        tree.Fetch = reference => current.GetVariablesAsync(reference);
        foreach (var item in Items.ToList())
        {
            try
            {
                var result = await current.EvaluateAsync(item.Name, "watch");
                if (frame != evaluatedIn)
                    return;
                item.Replace(result.Result, result.Type, result.VariablesReference, error: false);
            }
            catch (Exception exception) when (exception is DebugAdapterException or IOException)
            {
                item.Replace(exception.Message, null, 0, error: true);
            }
        }
    }

    private void Rebuild()
    {
        Items.Clear();
        var index = 0;
        foreach (var expression in store.Watches)
            Items.Add(new VariableNode(tree, string.Create(CultureInfo.InvariantCulture, $"{index++}:{expression}"), expression, NotPaused, null, 0));
        evaluatedIn = null;
    }
}

/// <summary>A thread in the Call Stack pane.</summary>
public sealed record ThreadItem(int Id, string Name, bool IsStopped)
{
    public string Text => IsStopped ? $"{Name} (paused)" : Name;
}

/// <summary>A frame in the Call Stack pane.</summary>
public sealed record FrameItem(StackFrame Frame, bool IsCurrent)
{
    public string Name => Frame.Name;

    /// <summary>Gets the file and line, such as <c>Program.cs:12</c>, or null for a frame without source.</summary>
    public string? Location => Frame.Source?.Path is { Length: > 0 } path ? string.Create(CultureInfo.CurrentCulture, $"{System.IO.Path.GetFileName(path)}:{Frame.Line}") : null;

    public bool HasSource => Frame.Source?.Path is { Length: > 0 };
}

/// <summary>The Call Stack pane: the program's threads, and the frames of the one that paused or was picked.</summary>
public sealed class CallStackModel : DebugModel
{
    private readonly DebugService debug;
    private readonly Action<StackFrame> navigate;
    private ThreadItem? selectedThread;
    private FrameItem? selectedFrame;
    private string? message = "The call stack shows while the program is paused.";
    private bool updating;

    /// <param name="navigate">Shows a frame's line in an editor.</param>
    public CallStackModel(DebugService debug, Action<StackFrame> navigate)
    {
        this.debug = debug;
        this.navigate = navigate;
    }

    public ObservableCollection<ThreadItem> Threads { get; } = [];

    public ObservableCollection<FrameItem> Frames { get; } = [];

    public string? Message
    {
        get => message;
        private set => Set(ref message, value);
    }

    /// <summary>Gets or sets the thread whose frames show; picking one asks for its call stack.</summary>
    public ThreadItem? SelectedThread
    {
        get => selectedThread;
        set
        {
            if (!Set(ref selectedThread, value) || updating || value is null || debug.Current is not { } session || session.ThreadId == value.Id)
                return;

            _ = session.SelectThreadAsync(value.Id);
        }
    }

    /// <summary>Gets or sets the frame variables and watches are read in; picking one with source shows its line.</summary>
    public FrameItem? SelectedFrame
    {
        get => selectedFrame;
        set
        {
            if (!Set(ref selectedFrame, value) || updating || value is null || debug.Current is not { } session)
                return;

            session.SelectFrame(value.Frame);
            if (value.HasSource)
                navigate(value.Frame);
        }
    }

    /// <summary>Shows the session's threads and frames as they are now.</summary>
    public void Refresh()
    {
        var session = debug.Current;
        updating = true;
        try
        {
            if (session is not { State: DebugState.Paused })
            {
                Threads.Clear();
                Frames.Clear();
                SelectedThread = null;
                SelectedFrame = null;
                Message = session is null ? "The call stack shows while the program is paused." : "The program is running.";
                return;
            }

            Sync(Threads, [.. session.Threads.Select(t => new ThreadItem(t.Id, t.Name, t.Id == session.ThreadId))]);
            Sync(Frames, [.. session.Frames.Select(f => new FrameItem(f, f == session.CurrentFrame))]);
            SelectedThread = Threads.FirstOrDefault(t => t.Id == session.ThreadId);
            SelectedFrame = Frames.FirstOrDefault(f => f.IsCurrent);
            Message = Frames.Count == 0 ? "The thread has no frames to show." : null;
        }
        finally
        {
            updating = false;
        }
    }

    private static void Sync<T>(ObservableCollection<T> target, IReadOnlyList<T> items)
    {
        if (target.SequenceEqual(items))
            return;

        target.Clear();
        foreach (var item in items)
            target.Add(item);
    }
}

/// <summary>A breakpoint in the Breakpoints pane, which can be turned off and on.</summary>
public sealed class BreakpointItem(LineBreakpoint breakpoint, BreakpointStatus? status, string folder, Action<LineBreakpoint> set) : DebugModel
{
    public LineBreakpoint Breakpoint { get; } = breakpoint;

    public string FileName => System.IO.Path.GetFileName(Breakpoint.Path);

    /// <summary>Gets the file's folder, relative to the open folder.</summary>
    public string Folder { get; } = folder;

    public string Line => string.Create(CultureInfo.CurrentCulture, $"{Breakpoint.Line + 1}");

    /// <summary>Gets the condition, hit count or log message, for a second line, or null.</summary>
    public string? Detail => string.Join("  ", new[]
    {
        Breakpoint.Condition is { Length: > 0 } condition ? $"when {condition}" : null,
        Breakpoint.HitCondition is { Length: > 0 } hits ? $"hit count {hits}" : null,
        Breakpoint.IsLogPoint ? $"logs {Breakpoint.LogMessage}" : null,
    }.OfType<string>()) is { Length: > 0 } text ? text : null;

    public BreakpointStatus? Status { get; } = status;

    /// <summary>Gets whether the running debugger could not set it.</summary>
    public bool IsUnverified => Breakpoint.IsEnabled && Status is { Verified: false };

    public bool IsEnabled
    {
        get => Breakpoint.IsEnabled;
        set
        {
            if (value != Breakpoint.IsEnabled)
                set(Breakpoint with { IsEnabled = value });
        }
    }
}

/// <summary>A kind of exception the debugger can stop on, in the Breakpoints pane.</summary>
public sealed class ExceptionFilterItem(ExceptionBreakpointsFilter filter, bool isEnabled, Action<string, bool> set) : DebugModel
{
    public string Filter => filter.Filter;

    public string Label => filter.Label;

    public string? Description => filter.Description;

    public bool IsEnabled
    {
        get => isEnabled;
        set
        {
            if (value != isEnabled)
            {
                isEnabled = value;
                set(filter.Filter, value);
            }
        }
    }
}

/// <summary>The Breakpoints pane: every breakpoint in the folder, and the exception breakpoints the last debugger offered.</summary>
public sealed class BreakpointsModel(BreakpointService breakpoints, DebugService debug, Func<string?> root) : DebugModel
{
    public ObservableCollection<BreakpointItem> Items { get; } = [];

    public ObservableCollection<ExceptionFilterItem> ExceptionFilters { get; } = [];

    public void Refresh()
    {
        var folder = root();
        Items.Clear();
        foreach (var breakpoint in breakpoints.All.OrderBy(b => b.Path, PathKey.Comparer).ThenBy(b => b.Line))
        {
            var relative = folder is null ? System.IO.Path.GetDirectoryName(breakpoint.Path) ?? "" : System.IO.Path.GetDirectoryName(System.IO.Path.GetRelativePath(folder, breakpoint.Path)) ?? "";
            Items.Add(new BreakpointItem(breakpoint, breakpoints.StatusOf(breakpoint), relative, breakpoints.Set));
        }

        ExceptionFilters.Clear();
        var offered = debug.ExceptionFilters;
        var chosen = breakpoints.ExceptionFilters;
        foreach (var filter in offered)
        {
            ExceptionFilters.Add(new ExceptionFilterItem(filter, chosen?.Contains(filter.Filter) ?? filter.Default,
                (id, on) => breakpoints.SetExceptionFilter(id, on, offered.Where(f => f.Default).Select(f => f.Filter))));
        }
    }

    public void Remove(BreakpointItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        breakpoints.Remove(item.Breakpoint);
    }
}

/// <summary>The debug console: the latest session's output, and expressions evaluated in the paused frame.</summary>
public sealed class DebugConsoleModel(DebugService debug) : DebugModel
{
    private readonly List<string> history = [];
    private int historyIndex;

    /// <summary>Gets the console of the latest session, or null before the first.</summary>
    public ConsoleBuffer? Console => debug.Latest?.Console;

    /// <summary>Gets whether expressions can be evaluated now.</summary>
    public bool CanEvaluate => debug.Current is { State: DebugState.Paused or DebugState.Running };

    /// <summary>Evaluates an expression in the current frame and writes it and its result to the console.</summary>
    public async Task EvaluateAsync(string expression)
    {
        if (string.IsNullOrWhiteSpace(expression) || debug.Current is not { } session)
            return;

        expression = expression.Trim();
        history.Remove(expression);
        history.Add(expression);
        historyIndex = history.Count;
        session.Console.AppendLine("> " + expression, ConsoleSource.Input);
        try
        {
            var result = await session.EvaluateAsync(expression, "repl");
            session.Console.AppendLine(result.Type is { Length: > 0 } type && result.VariablesReference > 0 ? $"{result.Result} ({type})" : result.Result, ConsoleSource.Output);
        }
        catch (Exception exception) when (exception is DebugAdapterException or IOException)
        {
            session.Console.AppendLine(exception.Message, ConsoleSource.Error);
        }
    }

    /// <summary>Gets the expression before the one shown from the history, or null at its start.</summary>
    public string? Previous()
    {
        if (history.Count == 0 || historyIndex == 0)
            return null;
        return history[--historyIndex];
    }

    /// <summary>Gets the expression after the one shown from the history, or an empty text past its end.</summary>
    public string Next()
    {
        if (historyIndex >= history.Count - 1)
        {
            historyIndex = history.Count;
            return "";
        }

        return history[++historyIndex];
    }
}
