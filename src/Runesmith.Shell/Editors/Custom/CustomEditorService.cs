using System.Composition;
using Avalonia.Threading;
using HammerUI.Services;
using Runesmith.Composition;
using Runesmith.Sdk.Documents;
using Runesmith.Sdk.Editors;
using Runesmith.Sdk.Shell;
using Runesmith.Shell.Appearance;
using Runesmith.Shell.Docking;
using Runesmith.Shell.Services;
using Runesmith.Workspace.Files;

namespace Runesmith.Shell.Editors.Custom;

/// <summary>Opens files in custom editor tabs, chooses between them and the text editor, and keeps the user's default editor for each kind of
/// file.</summary>
[Export(typeof(ICustomEditorService))]
[Export]
[Shared]
public sealed class CustomEditorService : ICustomEditorService
{
    private readonly ShellLayout layout;
    private readonly NotificationService notifications;
    private readonly Lazy<EditorService> editors;
    private readonly FileIcons icons;
    private readonly Action<string> log;
    private readonly Lazy<EditorCatalog> catalog;
    private readonly Dictionary<string, CustomEditorPanel> panels = new(StringComparer.Ordinal);

    [ImportingConstructor]
    public CustomEditorService(
        [ImportMany] IEnumerable<Lazy<IEditorProvider>> providers,
        ShellLayout layout,
        NotificationService notifications,
        Lazy<EditorService> editors,
        FileIcons icons,
        IOutputService output)
        : this(providers, layout, notifications, editors, icons, line => output.GetChannel(PluginAccess.ChannelName).AppendLine(line), EditorAssociations.DefaultPath)
    {
    }

    internal CustomEditorService(
        IEnumerable<Lazy<IEditorProvider>> providers,
        ShellLayout layout,
        NotificationService notifications,
        Lazy<EditorService> editors,
        FileIcons icons,
        Action<string> log,
        string associationsPath)
    {
        this.layout = layout;
        this.notifications = notifications;
        this.editors = editors;
        this.icons = icons;
        this.log = log;
        catalog = new(() => new EditorCatalog(Load(providers), new EditorAssociations(associationsPath)));
        layout.Layout.Changed += (_, _) => FollowLayout();
    }

    /// <summary>Gets the custom editor whose tab is active in the focused group, or null while another kind of tab is.</summary>
    internal CustomEditorPanel? Active { get; private set; }

    internal IReadOnlyCollection<CustomEditorPanel> Panels => panels.Values;

    internal EditorCatalog Catalog => catalog.Value;

    /// <summary>Raised when another custom editor becomes active, or none is.</summary>
    internal event EventHandler? ActiveChanged;

    public IReadOnlyList<EditorChoice> GetChoices(string filePath) => Catalog.GetChoices(Path.GetFullPath(filePath));

    public async Task<bool> OpenWithAsync(string filePath, string editorId, bool activate = true)
    {
        ArgumentException.ThrowIfNullOrEmpty(filePath);
        ArgumentException.ThrowIfNullOrEmpty(editorId);
        var path = PathComparison.Normalize(filePath);
        if (editorId == EditorProviderDefinition.TextEditorId)
            return await OpenInTextEditorAsync(path, activate);

        var entry = Catalog.Matching(path).FirstOrDefault(p => p.Definition.Id == editorId)
            ?? throw new ArgumentException($"No editor {editorId} opens {Path.GetFileName(path)}.", nameof(editorId));
        return await OpenCustomAsync(path, entry, activate);
    }

    /// <summary>Switches to the custom editor a file is open in, or opens the file in its default editor when that is a custom one and
    /// <paramref name="toText"/> is not set; returns false when the text editor should open it.</summary>
    internal async Task<bool> TryOpenAsync(string filePath, bool activate, bool toText = false)
    {
        var path = PathComparison.Normalize(filePath);
        if (PanelOf(path) is { } existing)
        {
            Show(existing.Id, activate);
            return true;
        }

        if (toText || TextEditorOf(path) is not null || Catalog.DefaultFor(path) is not { } id || id == EditorProviderDefinition.TextEditorId)
            return false;

        await OpenCustomAsync(path, Catalog.Find(id)!, activate);
        return true;
    }

    /// <summary>Makes an editor the default for the kind of file a path is, by <see cref="FilePatterns.KeyOf"/>.</summary>
    internal void SetDefault(string filePath, string editorId) => Catalog.Associations.Set(FilePatterns.KeyOf(filePath), editorId);

    /// <summary>Gets the id of the editor the file is open in, or null when it is not open.</summary>
    internal string? OpenEditorOf(string filePath)
    {
        var path = PathComparison.Normalize(filePath);
        return PanelOf(path) is { } panel ? panel.Entry.Definition.Id
            : TextEditorOf(path) is not null ? EditorProviderDefinition.TextEditorId
            : null;
    }

    internal async Task<bool> SaveAsync(CustomEditorPanel panel)
    {
        try
        {
            await panel.Editor.SaveAsync(CancellationToken.None);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or NotSupportedException)
        {
            notifications.Notify(NotificationKind.Error, $"{panel.Title} could not be saved", exception.Message);
            return false;
        }
    }

    internal async Task RevertAsync(CustomEditorPanel panel)
    {
        try
        {
            await panel.Editor.RevertAsync(CancellationToken.None);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException)
        {
            notifications.Notify(NotificationKind.Error, $"{panel.Title} could not be read again", exception.Message);
        }
    }

    /// <summary>Closes a custom editor's tab, asking first about unsaved changes when <paramref name="ask"/> is set; returns false when the user
    /// keeps it open.</summary>
    internal async Task<bool> CloseAsync(string panelId, bool ask = true)
    {
        if (!panels.TryGetValue(panelId, out var panel))
            return true;

        if (ask && panel.IsModified)
        {
            layout.Layout.EnsureVisible(panelId);
            switch (await notifications.Dialogs.AskToSaveChangesAsync(panel.Title))
            {
                case UnsavedChangesChoice.Cancel:
                    return false;
                case UnsavedChangesChoice.Save when !await SaveAsync(panel):
                    return false;
            }
        }

        layout.Layout.ClosePanel(panelId);
        layout.Unregister(panelId);
        panels.Remove(panelId);
        try
        {
            panel.Dispose();
        }
        catch (Exception exception) when (!panel.Entry.IsBuiltIn)
        {
            log($"{panel.Entry.Definition.Name} failed to close {panel.Title}: {exception.Message}");
        }

        FollowLayout();
        return true;
    }

    /// <summary>Closes every custom editor without asking; the caller has asked about unsaved changes.</summary>
    internal async Task CloseAllAsync()
    {
        foreach (var id in panels.Keys.ToList())
            await CloseAsync(id, ask: false);
    }

    private async Task<bool> OpenInTextEditorAsync(string path, bool activate)
    {
        var existing = PanelOf(path);
        var place = PlaceOf(existing?.Id);
        if (existing is not null && !await CloseAsync(existing.Id))
            return false;

        if (await editors.Value.OpenInTextEditorAsync(path, activate, anyContent: true) is null)
            return false;

        MoveTo(ShellLayout.DocumentPanelId(TextEditorOf(path)?.Document.FilePath ?? path), place, activate);
        return true;
    }

    private async Task<bool> OpenCustomAsync(string path, EditorEntry entry, bool activate)
    {
        var open = PanelOf(path);
        if (open is not null && open.Entry == entry)
        {
            Show(open.Id, activate);
            return true;
        }

        if (await CreateAsync(path, entry) is not { } editor)
            return false;

        var text = TextEditorOf(path);
        var place = PlaceOf(open?.Id ?? (text is null ? null : ShellLayout.DocumentPanelId(text.Document.FilePath!)));
        if ((open is not null && !await CloseAsync(open.Id)) || (text is not null && !await editors.Value.CloseAsync(text)))
        {
            editor.Dispose();
            return false;
        }

        var id = CustomEditorPanel.NewId(path);
        var panel = new CustomEditorPanel(id, path, entry, editor, icons) { Watch = WatchFile(path, id) };
        panels[id] = panel;
        layout.Register(panel);
        if (place is null)
            layout.AddToEditors(id);
        MoveTo(id, place, activate);
        return true;
    }

    private async Task<CustomEditor?> CreateAsync(string path, EditorEntry entry)
    {
        var name = Path.GetFileName(path);
        try
        {
            return await entry.Provider.CreateEditorAsync(new CustomEditorContext(path, IsReadOnly(path)), CancellationToken.None);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException)
        {
            notifications.Notify(NotificationKind.Error, $"{name} could not be opened", exception.Message);
        }
        catch (Exception exception) when (!entry.IsBuiltIn)
        {
            log($"{entry.Definition.Name} failed to open {name}: {exception}");
            notifications.Notify(NotificationKind.Error, $"{name} could not be opened", $"{entry.Definition.Name} failed: {exception.Message}");
        }

        return null;
    }

    private void Show(string id, bool activate)
    {
        layout.AddToEditors(id);
        if (activate)
            Activate(id);
    }

    private void Activate(string id)
    {
        layout.Layout.ActivatePanel(id);
        FollowLayout();
        if (panels.TryGetValue(id, out var panel))
            Dispatcher.UIThread.Post(panel.Editor.Focus, DispatcherPriority.Background);
    }

    private (string Group, int Index)? PlaceOf(string? panelId)
    {
        if (panelId is null || layout.Layout.FindPanel(panelId) is not { } group)
            return null;
        return (group.Id, group.Panels.ToList().IndexOf(panelId));
    }

    private void MoveTo(string panelId, (string Group, int Index)? place, bool activate)
    {
        if (place is { } at && layout.Layout.FindNode(at.Group) is not null)
            layout.Layout.MovePanel(panelId, at.Group, at.Index);
        else
            layout.AddToEditors(panelId);

        if (activate || place is not null)
            Activate(panelId);
    }

    private CustomEditorPanel? PanelOf(string path) => panels.Values.FirstOrDefault(p => PathComparison.Comparer.Equals(p.FilePath, path));

    private IEditorView? TextEditorOf(string path) =>
        editors.Value.Editors.FirstOrDefault(e => e.Document.FilePath is { } open && PathComparison.Comparer.Equals(open, path));

    private void FollowLayout()
    {
        var group = layout.Layout.FocusedGroup;
        var active = group?.ActivePanel is { } id && panels.TryGetValue(id, out var panel) ? panel : null;
        if (ReferenceEquals(active, Active))
            return;

        Active = active;
        ActiveChanged?.Invoke(this, EventArgs.Empty);
    }

    private FileWatch? WatchFile(string path, string panelId)
    {
        try
        {
            var watcher = new FileSystemWatcher(Path.GetDirectoryName(path)!, Path.GetFileName(path))
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName | NotifyFilters.CreationTime,
            };
            var quiet = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            quiet.Tick += (_, _) =>
            {
                quiet.Stop();
                OnChangedOnDisk(panelId);
            };
            void Changed(object? sender, FileSystemEventArgs e) => Dispatcher.UIThread.Post(() =>
            {
                quiet.Stop();
                quiet.Start();
            });
            watcher.Changed += Changed;
            watcher.Created += Changed;
            watcher.Deleted += Changed;
            watcher.Renamed += (sender, e) => Changed(sender, e);
            watcher.EnableRaisingEvents = true;
            return new FileWatch(watcher, quiet);
        }
        catch (Exception exception) when (exception is IOException or ArgumentException or PlatformNotSupportedException)
        {
            return null;
        }
    }

    private void OnChangedOnDisk(string panelId)
    {
        if (!panels.TryGetValue(panelId, out var panel))
            return;

        var exists = File.Exists(panel.FilePath);
        if (exists && !panel.IsModified)
        {
            _ = RevertAsync(panel);
            return;
        }

        notifications.Notify(
            NotificationKind.Warning,
            exists ? $"{panel.Title} changed on disk" : $"{panel.Title} was deleted",
            exists ? "Your unsaved changes are kept. Reload to take the version on disk instead." : "Its editor stays open; save it to create the file again.",
            exists ? "Reload" : null,
            exists ? () => _ = RevertAsync(panel) : null);
    }

    private List<EditorEntry> Load(IEnumerable<Lazy<IEditorProvider>> providers)
    {
        var entries = new List<EditorEntry>();
        foreach (var lazy in providers)
        {
            try
            {
                var provider = lazy.Value;
                var definition = provider.Definition;
                var isBuiltIn = PluginCallers.Of(provider.GetType().Assembly) is null;
                if (definition.Id == EditorProviderDefinition.TextEditorId || entries.Any(e => e.Definition.Id == definition.Id))
                {
                    log($"The editor {definition.Name} is left out: another editor has the id {definition.Id}.");
                    continue;
                }

                entries.Add(new EditorEntry(definition, isBuiltIn, provider));
            }
            catch (Exception exception)
            {
                log($"An editor provider could not be created: {exception.Message}");
            }
        }

        return entries;
    }

    private static bool IsReadOnly(string path)
    {
        try
        {
            return new FileInfo(path).IsReadOnly;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    private sealed class FileWatch(FileSystemWatcher watcher, DispatcherTimer quiet) : IDisposable
    {
        public void Dispose()
        {
            quiet.Stop();
            watcher.Dispose();
        }
    }
}
