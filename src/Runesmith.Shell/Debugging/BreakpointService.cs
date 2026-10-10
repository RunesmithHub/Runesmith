using System.Collections.Immutable;
using System.Composition;
using System.Text.Json;
using System.Text.Json.Serialization;
using Runesmith.Sdk;
using Runesmith.Sdk.Documents;
using Runesmith.Sdk.Workspace;
using Runesmith.Text;

namespace Runesmith.Shell.Debugging;

/// <summary>A breakpoint on a line of a file, with what it does when the line runs.</summary>
/// <param name="Path">The file's full path.</param>
/// <param name="Line">The line, counted from 0.</param>
public sealed record LineBreakpoint(string Path, int Line)
{
    /// <summary>Gets an expression that must be true for the breakpoint to stop, or null.</summary>
    public string? Condition { get; init; }

    /// <summary>Gets how many hits to let pass, as the debugger understands it, such as <c>5</c>, or null.</summary>
    public string? HitCondition { get; init; }

    /// <summary>Gets a message to write to the debug console instead of stopping, with expressions in braces, or null for a breakpoint that
    /// stops.</summary>
    public string? LogMessage { get; init; }

    public bool IsEnabled { get; init; } = true;

    /// <summary>Gets whether the breakpoint writes a message instead of stopping.</summary>
    public bool IsLogPoint => !string.IsNullOrEmpty(LogMessage);

    /// <summary>Gets whether it has a condition or a hit count.</summary>
    public bool IsConditional => !string.IsNullOrEmpty(Condition) || !string.IsNullOrEmpty(HitCondition);
}

/// <summary>What the debugger of the running session made of a breakpoint.</summary>
/// <param name="Verified">Whether the debugger set it, such as on a line with code.</param>
/// <param name="Message">Why it is not set, or other news about it.</param>
public sealed record BreakpointStatus(bool Verified, string? Message = null);

/// <summary>The open folder's breakpoints, exception breakpoint choices and watch expressions, saved for the next time the folder opens.
/// Breakpoints in open documents move with their lines as the text is edited.</summary>
/// <remarks>Its members can be called from any thread; <see cref="Changed"/> is raised on the thread that made the change.</remarks>
[Export]
[Shared]
public sealed class BreakpointService
{
    private readonly IWorkspace workspace;
    private readonly string stateFolder;
    private readonly Lock gate = new();
    private ImmutableList<LineBreakpoint> breakpoints = [];
    private ImmutableList<string> watches = [];
    private ImmutableHashSet<string>? exceptionFilters;
    private ImmutableDictionary<LineBreakpoint, BreakpointStatus> statuses = ImmutableDictionary<LineBreakpoint, BreakpointStatus>.Empty;
    private string? root;

    [ImportingConstructor]
    public BreakpointService(IWorkspace workspace, IDocumentService documents)
        : this(workspace, documents, RunesmithPaths.State)
    {
    }

    internal BreakpointService(IWorkspace workspace, IDocumentService? documents, string stateFolder)
    {
        this.workspace = workspace;
        this.stateFolder = stateFolder;
        workspace.Changed += (_, _) => Load();
        if (documents is not null)
        {
            documents.Opened += (_, e) => Follow(e.Document);
            foreach (var document in documents.Documents)
                Follow(document);
        }

        Load();
    }

    /// <summary>Gets every breakpoint, by file and line.</summary>
    public IReadOnlyList<LineBreakpoint> All => breakpoints;

    /// <summary>Gets the watch expressions, in the order the user added them.</summary>
    public IReadOnlyList<string> Watches => watches;

    /// <summary>Gets the exception breakpoint filters the user turned on, or null while the user has not chosen, so each debugger's defaults
    /// apply.</summary>
    public IReadOnlySet<string>? ExceptionFilters => exceptionFilters;

    /// <summary>Raised when breakpoints are added, removed, moved or changed, with their file, or null when several files changed.</summary>
    public event EventHandler<string?>? Changed;

    /// <summary>Raised when the debugger says something new about the breakpoints.</summary>
    public event EventHandler? StatusesChanged;

    /// <summary>Raised when the watch expressions change.</summary>
    public event EventHandler? WatchesChanged;

    /// <summary>Raised when the exception breakpoint choices change.</summary>
    public event EventHandler? ExceptionFiltersChanged;

    /// <summary>Gets the breakpoints of a file, in line order.</summary>
    public IReadOnlyList<LineBreakpoint> In(string path) => [.. breakpoints.Where(b => PathKey.Equals(b.Path, path)).OrderBy(b => b.Line)];

    public LineBreakpoint? Find(string path, int line) => breakpoints.FirstOrDefault(b => b.Line == line && PathKey.Equals(b.Path, path));

    /// <summary>Gets what the running session's debugger made of a breakpoint, or null outside a session.</summary>
    public BreakpointStatus? StatusOf(LineBreakpoint breakpoint) => statuses.GetValueOrDefault(breakpoint);

    /// <summary>Adds a breakpoint to a line, or removes the one it has.</summary>
    /// <returns>The breakpoint added, or null when one was removed.</returns>
    public LineBreakpoint? Toggle(string path, int line)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        LineBreakpoint? added = null;
        Update(path, list =>
        {
            if (list.FirstOrDefault(b => b.Line == line && PathKey.Equals(b.Path, path)) is { } existing)
                return list.Remove(existing);

            added = new LineBreakpoint(Path.GetFullPath(path), Math.Max(0, line));
            return list.Add(added);
        });
        return added;
    }

    /// <summary>Puts a breakpoint in place of the one on its line, or adds it.</summary>
    public void Set(LineBreakpoint breakpoint)
    {
        ArgumentNullException.ThrowIfNull(breakpoint);
        Update(breakpoint.Path, list =>
        {
            var existing = list.FirstOrDefault(b => b.Line == breakpoint.Line && PathKey.Equals(b.Path, breakpoint.Path));
            return existing is null ? list.Add(breakpoint) : list.Replace(existing, breakpoint);
        });
    }

    public void Remove(LineBreakpoint breakpoint)
    {
        ArgumentNullException.ThrowIfNull(breakpoint);
        Update(breakpoint.Path, list => list.Remove(breakpoint));
    }

    public void RemoveAll() => Update(null, _ => []);

    /// <summary>Turns every breakpoint on or off.</summary>
    public void EnableAll(bool enabled) => Update(null, list => [.. list.Select(b => b with { IsEnabled = enabled })]);

    /// <summary>Records what the debugger made of a file's breakpoints, replacing what it said before about them.</summary>
    public void SetStatuses(IReadOnlyDictionary<LineBreakpoint, BreakpointStatus> found)
    {
        ArgumentNullException.ThrowIfNull(found);
        lock (gate)
            statuses = statuses.SetItems(found);
        StatusesChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Forgets what the debugger said about the breakpoints, as when a session ends.</summary>
    public void ClearStatuses()
    {
        lock (gate)
            statuses = statuses.Clear();
        StatusesChanged?.Invoke(this, EventArgs.Empty);
    }

    public void AddWatch(string expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
            return;

        lock (gate)
            watches = watches.Add(expression.Trim());
        SaveAndRaise(WatchesChanged);
    }

    public void ReplaceWatch(int index, string expression)
    {
        lock (gate)
        {
            if (index < 0 || index >= watches.Count)
                return;
            watches = string.IsNullOrWhiteSpace(expression) ? watches.RemoveAt(index) : watches.SetItem(index, expression.Trim());
        }

        SaveAndRaise(WatchesChanged);
    }

    public void RemoveWatch(int index)
    {
        lock (gate)
        {
            if (index < 0 || index >= watches.Count)
                return;
            watches = watches.RemoveAt(index);
        }

        SaveAndRaise(WatchesChanged);
    }

    /// <summary>Turns an exception breakpoint filter on or off, starting from the defaults the debugger offers.</summary>
    public void SetExceptionFilter(string filter, bool enabled, IEnumerable<string> defaults)
    {
        lock (gate)
        {
            var current = exceptionFilters ?? [.. defaults];
            exceptionFilters = enabled ? current.Add(filter) : current.Remove(filter);
        }

        SaveAndRaise(ExceptionFiltersChanged);
    }

    /// <summary>Moves the breakpoints of a file through an edit, so each stays on the text it was on.</summary>
    internal void MapEdit(string path, TextChangeSet changeSet)
    {
        if (changeSet.IsEmpty || !breakpoints.Any(b => PathKey.Equals(b.Path, path)))
            return;

        Update(path, list =>
        {
            var moved = new List<LineBreakpoint>();
            var taken = new HashSet<int>();
            foreach (var breakpoint in list)
            {
                if (!PathKey.Equals(breakpoint.Path, path))
                {
                    moved.Add(breakpoint);
                    continue;
                }

                var line = MapLine(changeSet, breakpoint.Line);
                if (taken.Add(line))
                    moved.Add(breakpoint with { Line = line });
            }

            return moved.SequenceEqual(list) ? list : [.. moved];
        }, keepStatuses: true);
    }

    /// <summary>Maps a line through an edit by its first character: text inserted or deleted before it moves it, and a deleted line lands
    /// where the deletion started.</summary>
    internal static int MapLine(TextChangeSet changeSet, int line)
    {
        var before = changeSet.Before;
        if (line >= before.LineCount)
            return line + changeSet.After.LineCount - before.LineCount;

        var offset = before.GetLine(line).Start;
        var delta = 0;
        foreach (var change in changeSet.Changes)
        {
            if (change.Span.End <= offset)
            {
                delta += change.NewText.Length - change.Span.Length;
                continue;
            }

            if (change.Span.Start < offset)
                offset = change.Span.Start;
            break;
        }

        var after = changeSet.After;
        return after.GetLineFromPosition(Math.Clamp(offset + delta, 0, after.Length)).LineNumber;
    }

    private void Follow(IDocument document) =>
        document.Buffer.Changed += (_, e) =>
        {
            if (document.FilePath is { } path)
                MapEdit(path, e.ChangeSet);
        };

    private void Update(string? path, Func<ImmutableList<LineBreakpoint>, ImmutableList<LineBreakpoint>> change, bool keepStatuses = false)
    {
        lock (gate)
        {
            var next = change(breakpoints);
            if (next == breakpoints)
                return;
            breakpoints = next;
            if (!keepStatuses)
                statuses = statuses.RemoveRange(statuses.Keys.Where(k => !next.Contains(k)));
        }

        Save();
        Changed?.Invoke(this, path);
    }

    private void SaveAndRaise(EventHandler? handler)
    {
        Save();
        handler?.Invoke(this, EventArgs.Empty);
    }

    private void Load()
    {
        var path = workspace.RootPath;
        lock (gate)
        {
            if (root == path)
                return;

            root = path;
            var state = path is null ? null : Read(StatePath(path));
            breakpoints = state is null ? [] : [.. (state.Breakpoints ?? []).OfType<BreakpointModel>().Select(b => FromModel(path!, b))];
            watches = [.. state?.Watches ?? []];
            exceptionFilters = state?.ExceptionFilters is { } filters ? [.. filters] : null;
            statuses = statuses.Clear();
        }

        Changed?.Invoke(this, null);
        WatchesChanged?.Invoke(this, EventArgs.Empty);
        ExceptionFiltersChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Save()
    {
        DebugStateModel state;
        string folder;
        lock (gate)
        {
            if (root is null)
                return;
            folder = root;
            state = new DebugStateModel(
                [.. breakpoints.Select(b => ToModel(folder, b))],
                [.. watches],
                exceptionFilters is null ? null : [.. exceptionFilters.Order(StringComparer.Ordinal)]);
        }

        try
        {
            var file = StatePath(folder);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            var temporary = file + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(state, DebugJson.Default.DebugStateModel) + Environment.NewLine);
            File.Move(temporary, file, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private string StatePath(string rootPath)
    {
        var key = OperatingSystem.IsLinux() ? rootPath : rootPath.ToUpperInvariant();
        var hash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(key)))[..16];
        return Path.Combine(stateFolder, "debug", hash + ".json");
    }

    private static DebugStateModel? Read(string path)
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize(File.ReadAllText(path), DebugJson.Default.DebugStateModel) : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private static BreakpointModel ToModel(string root, LineBreakpoint breakpoint)
    {
        var relative = Path.GetRelativePath(root, breakpoint.Path);
        var path = relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative) ? breakpoint.Path : relative.Replace('\\', '/');
        return new BreakpointModel(path, breakpoint.Line + 1, breakpoint.Condition, breakpoint.HitCondition, breakpoint.LogMessage, breakpoint.IsEnabled ? null : false);
    }

    private static LineBreakpoint FromModel(string root, BreakpointModel model) =>
        new(Path.GetFullPath(Path.Combine(root, model.Path)), Math.Max(0, model.Line - 1))
        {
            Condition = model.Condition,
            HitCondition = model.HitCondition,
            LogMessage = model.LogMessage,
            IsEnabled = model.Enabled ?? true,
        };
}

/// <summary>Compares file paths as the file system does: ignoring case on Windows and macOS.</summary>
internal static class PathKey
{
    public static StringComparer Comparer { get; } = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    public static bool Equals(string? a, string? b) => a is not null && b is not null && Comparer.Equals(Normalize(a), Normalize(b));

    private static string Normalize(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return path;
        }
    }
}

internal sealed record DebugStateModel(IReadOnlyList<BreakpointModel?>? Breakpoints, IReadOnlyList<string>? Watches, IReadOnlyList<string>? ExceptionFilters);

/// <param name="Path">The file, relative to the folder with <c>/</c> when it is inside it.</param>
/// <param name="Line">The line, counted from 1 as editors show it.</param>
internal sealed record BreakpointModel(
    string Path,
    int Line,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Condition,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? HitCondition,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? LogMessage,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? Enabled);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true)]
[JsonSerializable(typeof(DebugStateModel))]
internal sealed partial class DebugJson : JsonSerializerContext;
