using System.Composition;
using Avalonia.Controls;
using Runesmith.Composition;
using Runesmith.Sdk.Shell;
using Runesmith.Sdk.ToolWindows;
using Runesmith.Shell.Appearance;
using Runesmith.Shell.Commands;
using Runesmith.Shell.Services;

namespace Runesmith.Shell.TreeViews;

/// <summary>Keeps the tree views plugins export, each in a tool window of its own, and gives each plugin its own tree views.</summary>
[Export(typeof(ITreeViewService))]
[Export]
[Shared]
public sealed class TreeViewService : ITreeViewService, IDisposable
{
    private readonly Dictionary<string, (ITreeViewProvider Provider, TreeViewDefinition Definition, PluginInfo? Owner)> providers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TreeViewModel> models = new(StringComparer.Ordinal);
    private readonly Lazy<CommandService>? commands;
    private readonly Lazy<FileIcons>? icons;
    private readonly Lazy<IToolWindowManager>? toolWindows;
    private readonly Func<PluginInfo?> caller;
    private readonly Action<string> log;

    [ImportingConstructor]
    public TreeViewService([ImportMany] IEnumerable<ITreeViewProvider> providers, Lazy<CommandService> commands, Lazy<FileIcons> icons,
        Lazy<IToolWindowManager> toolWindows, IOutputService output)
        : this(providers, PluginCallers.Current, provider => PluginCallers.Of(provider.GetType().Assembly),
            line => output.GetChannel(PluginAccess.ChannelName).AppendLine(line), commands, icons, toolWindows)
    {
    }

    internal TreeViewService(IEnumerable<ITreeViewProvider> providers, Func<PluginInfo?> caller, Func<ITreeViewProvider, PluginInfo?> ownerOf, Action<string> log,
        Lazy<CommandService>? commands = null, Lazy<FileIcons>? icons = null, Lazy<IToolWindowManager>? toolWindows = null)
    {
        this.caller = caller;
        this.log = log;
        this.commands = commands;
        this.icons = icons;
        this.toolWindows = toolWindows;
        foreach (var provider in providers)
        {
            var owner = ownerOf(provider);
            try
            {
                var definition = provider.Definition;
                if (!this.providers.TryAdd(definition.Id, (provider, definition, owner)))
                    log($"{Name(owner)}: the tree view {definition.Id} was left out, because another one has the same id.");
            }
            catch (Exception exception)
            {
                log($"{Name(owner)}: a tree view was left out, because its definition could not be read: {exception.Message}");
            }
        }
    }

    /// <summary>Gets the tree views' definitions.</summary>
    public IReadOnlyList<TreeViewDefinition> Definitions => [.. providers.Values.Select(p => p.Definition)];

    /// <exception cref="KeyNotFoundException">There is no tree view with this id.</exception>
    /// <exception cref="UnauthorizedAccessException">The tree view belongs to another plugin.</exception>
    public ITreeView GetTreeView(string id)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        if (!providers.TryGetValue(id, out var entry))
            throw new KeyNotFoundException($"There is no tree view with the id {id}.");

        if (caller() is { } plugin && plugin.Manifest.Id != entry.Owner?.Manifest.Id)
            throw new UnauthorizedAccessException($"{plugin.Manifest.Name} ({plugin.Manifest.Id}) cannot use the tree view {id}: it belongs to {Name(entry.Owner)}.");

        return Model(id);
    }

    public void Dispose()
    {
        lock (models)
        {
            foreach (var model in models.Values)
                model.Dispose();
        }
    }

    /// <summary>Gets the state of a tree view, created the first time.</summary>
    internal TreeViewModel Model(string id)
    {
        lock (models)
        {
            if (models.TryGetValue(id, out var model))
                return model;

            var (provider, definition, owner) = providers[id];
            model = new TreeViewModel(provider, definition, line => log($"{Name(owner)}: {line}"), shown => toolWindows?.Value.Show(shown));
            models[id] = model;
            return model;
        }
    }

    /// <summary>Creates the content of a tree view's tool window.</summary>
    internal Control CreateView(string id) => new TreeViewControl(Model(id), commands?.Value, icons?.Value);

    /// <summary>Gets the tool windows the tree views show in.</summary>
    internal IEnumerable<IToolWindowProvider> ToolWindows() => providers.Values.Select(p => new TreeToolWindow(this, p.Definition));

    private static string Name(PluginInfo? plugin) => plugin is null ? "Runesmith" : $"{plugin.Manifest.Name} ({plugin.Manifest.Id})";

    private sealed class TreeToolWindow(TreeViewService service, TreeViewDefinition definition) : IToolWindowProvider
    {
        public ToolWindowDefinition Definition => definition.ToolWindow;

        public Control CreateContent() => service.CreateView(definition.Id);
    }
}
