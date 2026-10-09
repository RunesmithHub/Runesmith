using System.Composition;
using Runesmith.Sdk;
using Runesmith.Sdk.Options;
using Runesmith.Sdk.Running;
using Runesmith.Sdk.Tasks;
using Runesmith.Sdk.Workspace;

namespace Runesmith.Shell.Running;

/// <summary>Keeps the open folder's run configurations: when a folder opens it reads the saved ones, then asks every configuration type to
/// detect its own and merges them.</summary>
[Export(typeof(IRunConfigurationService))]
[Export]
[Shared]
public sealed class RunConfigurationService : IRunConfigurationService, IDisposable
{
    private const int RecentLimit = 10;

    private readonly IEnumerable<Lazy<IRunConfigurationType>> typeExports;
    private readonly IWorkspace workspace;
    private readonly IBackgroundTasks? tasks;
    private readonly string stateFolder;
    private readonly Lock gate = new();
    private IReadOnlyList<IRunConfigurationType>? types;
    private string? root;
    private IReadOnlyList<RunConfiguration> configurations = [];
    private RunLocalState local = RunLocalState.Empty;
    private CancellationTokenSource? detecting;

    /// <summary>Creates the service, which follows the workspace's open folder.</summary>
    [ImportingConstructor]
    public RunConfigurationService([ImportMany] IEnumerable<Lazy<IRunConfigurationType>> types, IWorkspace workspace,
        [Import(AllowDefault = true)] IBackgroundTasks? tasks)
        : this(types, workspace, tasks, RunesmithPaths.State)
    {
    }

    internal RunConfigurationService(IEnumerable<Lazy<IRunConfigurationType>> types, IWorkspace workspace, IBackgroundTasks? tasks, string stateFolder)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        typeExports = types;
        this.workspace = workspace;
        this.tasks = tasks;
        this.stateFolder = stateFolder;
        workspace.Changed += OnWorkspaceChanged;
        if (workspace.RootPath is { } path)
            _ = LoadAsync(path);
    }

    public IReadOnlyList<IRunConfigurationType> Types => types ??= LoadTypes();

    public IReadOnlyList<RunConfiguration> Configurations => configurations;

    public RunConfiguration? Selected { get; private set; }

    public IReadOnlyList<string> Recent => local.Recent;

    public bool IsDetecting { get; private set; }

    public event EventHandler? Changed;

    public async Task<IReadOnlyDictionary<string, OptionSet>> GetOptionsAsync(CancellationToken cancellationToken = default)
    {
        var options = new Dictionary<string, OptionSet>(StringComparer.Ordinal);
        if (root is not { } rootPath)
            return options;

        var found = await Task.WhenAll(Types.Select(type => OptionsOfAsync(type, rootPath, cancellationToken)));
        foreach (var (id, set) in found)
            options.TryAdd(id, set);
        return options;
    }

    public IRunConfigurationType? FindType(string typeId) => Types.FirstOrDefault(t => t.Id == typeId);

    public RunConfiguration? Find(string name) => configurations.FirstOrDefault(c => c.Name == name);

    public void Select(RunConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        Selected = Find(configuration.Name) ?? configuration;
        local = local with { Selected = Selected.Name };
        SaveLocal();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void MarkRun(RunConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        local = local with { Recent = [configuration.Name, .. local.Recent.Where(n => n != configuration.Name).Take(RecentLimit - 1)] };
        SaveLocal();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <exception cref="IOException">The shared or local configurations could not be written.</exception>
    public void Save(IReadOnlyList<RunConfiguration> configurations, string? selected = null)
    {
        ArgumentNullException.ThrowIfNull(configurations);
        if (root is null)
            return;

        this.configurations = [.. configurations];
        RunConfigurationFile.WriteShared(root, [.. configurations.Where(c => !c.IsDetected && !c.IsLocal)]);
        local = local with { Configurations = [.. configurations.Where(c => !c.IsDetected && c.IsLocal)] };
        Selected = (selected is null ? null : Find(selected)) ?? (Selected is { } current ? Find(current.Name) : null) ?? (this.configurations.Count > 0 ? this.configurations[0] : null);
        local = local with { Selected = Selected?.Name };
        SaveLocal();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Reads a folder's saved configurations and detects the others; finishes when detection has.</summary>
    internal async Task LoadAsync(string rootPath)
    {
        CancellationTokenSource cancel;
        lock (gate)
        {
            detecting?.Cancel();
            detecting?.Dispose();
            detecting = cancel = new CancellationTokenSource();
        }

        var token = cancel.Token;
        root = rootPath;
        local = RunConfigurationFile.ReadLocal(LocalPath(rootPath));
        var saved = (IReadOnlyList<RunConfiguration>)[.. RunConfigurationFile.ReadShared(rootPath), .. local.Configurations];
        Apply(RunConfigurationMerger.Merge([], saved));
        IsDetecting = true;
        Changed?.Invoke(this, EventArgs.Empty);

        using var task = tasks?.Start("Finding run configurations", cancel.Cancel);
        var found = await Task.WhenAll(Types.Select(type => DetectAsync(type, rootPath, token)));
        if (token.IsCancellationRequested)
            return;

        IsDetecting = false;
        Apply(RunConfigurationMerger.Merge([.. found.SelectMany(f => f)], saved));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        workspace.Changed -= OnWorkspaceChanged;
        lock (gate)
        {
            detecting?.Cancel();
            detecting?.Dispose();
            detecting = null;
        }
    }

    private static async Task<IReadOnlyList<RunConfiguration>> DetectAsync(IRunConfigurationType type, string rootPath, CancellationToken token)
    {
        try
        {
            var found = await Task.Run(() => type.DetectAsync(rootPath, token), token).ConfigureAwait(false);
            return [.. found.Select(c => c with { TypeId = type.Id, IsDetected = true })];
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return [];
        }
    }

    private static async Task<(string Id, OptionSet Options)> OptionsOfAsync(IRunConfigurationType type, string rootPath, CancellationToken token)
    {
        try
        {
            return (type.Id, await Task.Run(() => type.GetOptionsAsync(rootPath, token), token).ConfigureAwait(false));
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return (type.Id, OptionSet.Empty);
        }
    }

    private void Apply(IReadOnlyList<RunConfiguration> merged)
    {
        configurations = merged;
        Selected = (local.Selected is { } name ? Find(name) : null) ?? (merged.Count > 0 ? merged[0] : null);
    }

    private void OnWorkspaceChanged(object? sender, EventArgs e)
    {
        if (workspace.RootPath == root)
            return;

        if (workspace.RootPath is { } path)
        {
            _ = LoadAsync(path);
            return;
        }

        lock (gate)
            detecting?.Cancel();
        root = null;
        configurations = [];
        Selected = null;
        local = RunLocalState.Empty;
        IsDetecting = false;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void SaveLocal()
    {
        if (root is null)
            return;

        try
        {
            RunConfigurationFile.WriteLocal(LocalPath(root), local);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private string LocalPath(string rootPath) => RunConfigurationFile.LocalPath(stateFolder, rootPath);

    private List<IRunConfigurationType> LoadTypes()
    {
        var loaded = new List<IRunConfigurationType>();
        foreach (var export in typeExports)
        {
            try
            {
                loaded.Add(export.Value);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
            }
        }

        return loaded;
    }
}
