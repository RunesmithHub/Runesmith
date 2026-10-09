using System.Composition;
using Avalonia.Threading;
using Runesmith.Sdk.Build;

namespace Runesmith.Languages.Features;

/// <summary>Keeps the problems each source reports per file, and tells listeners which files changed, once per batch.</summary>
[Export(typeof(IDiagnosticService))]
[Shared]
public sealed class DiagnosticService : IDiagnosticService
{
    private static readonly StringComparer PathComparer = OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;

    private readonly Lock gate = new();
    private readonly Dictionary<string, Dictionary<string, IReadOnlyList<Diagnostic>>> bySource = new(StringComparer.Ordinal);
    private readonly HashSet<string> changedPaths = new(PathComparer);
    private bool notifyScheduled;

    public event EventHandler<IReadOnlyCollection<string>>? Changed;

    public IReadOnlyList<Diagnostic> All
    {
        get
        {
            lock (gate)
                return [.. bySource.Values.SelectMany(files => files.Values).SelectMany(list => list)];
        }
    }

    public void Set(string source, string filePath, IReadOnlyList<Diagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(diagnostics);
        var path = Path.GetFullPath(filePath);
        lock (gate)
        {
            if (!bySource.TryGetValue(source, out var files))
                bySource[source] = files = new Dictionary<string, IReadOnlyList<Diagnostic>>(PathComparer);

            var had = files.Remove(path, out var previous) && previous.Count > 0;
            if (diagnostics.Count > 0)
                files[path] = [.. diagnostics];
            else if (!had)
                return;

            changedPaths.Add(path);
        }

        Schedule();
    }

    public void Clear(string source)
    {
        lock (gate)
        {
            if (!bySource.Remove(source, out var files) || files.Count == 0)
                return;
            changedPaths.UnionWith(files.Keys);
        }

        Schedule();
    }

    public IReadOnlyList<Diagnostic> Get(string filePath)
    {
        var path = Path.GetFullPath(filePath);
        lock (gate)
            return [.. bySource.Values.SelectMany(files => files.TryGetValue(path, out var list) ? list : [])];
    }

    private void Schedule()
    {
        lock (gate)
        {
            if (notifyScheduled)
                return;
            notifyScheduled = true;
        }

        Dispatcher.UIThread.Post(Notify, DispatcherPriority.Background);
    }

    private void Notify()
    {
        string[] paths;
        lock (gate)
        {
            notifyScheduled = false;
            paths = [.. changedPaths];
            changedPaths.Clear();
        }

        if (paths.Length > 0)
            Changed?.Invoke(this, paths);
    }
}
