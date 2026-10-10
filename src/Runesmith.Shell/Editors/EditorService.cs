using System.Composition;
using Avalonia.Input;
using Avalonia.Threading;
using HammerUI;
using HammerUI.Services;
using Runesmith.Editor;
using Runesmith.Sdk.Commands;
using Runesmith.Sdk.Documents;
using Runesmith.Sdk.Languages;
using Runesmith.Sdk.Settings;
using Runesmith.Sdk.Shell;
using Runesmith.Sdk.Workspace;
using Runesmith.Shell.Appearance;
using Runesmith.Shell.Commands;
using Runesmith.Shell.Docking;
using Runesmith.Shell.Editors.Custom;
using Runesmith.Shell.Hub;
using Runesmith.Shell.Palette;
using Runesmith.Shell.Services;
using Runesmith.Shell.Symbols;
using Runesmith.Shell.ToolWindows;
using Runesmith.Shell.Views;
using Runesmith.Text;

namespace Runesmith.Shell.Editors;

/// <summary>Opens documents in editor tabs, saves them, asks before losing changes and remembers where the user has been.</summary>
[Export(typeof(IEditorService))]
[Export]
[Shared]
public sealed class EditorService : IEditorService
{
    private const int HistoryLimit = 50;

    private readonly IDocumentService documents;
    private readonly EditorServices editorServices;
    private readonly ShellLayout layout;
    private readonly ISettingsService settings;
    private readonly NotificationService notifications;
    private readonly ShellController shell;
    private readonly ILanguageRegistry languages;
    private readonly FileIcons fileIcons;
    private readonly IWorkspace workspace;
    private readonly Lazy<ExplorerToolWindow> explorer;
    private readonly ChangeBases changeBases;
    private readonly Lazy<IDiffService> diffs;
    private readonly Lazy<CommandService> commands;
    private readonly Lazy<CustomEditorService> customEditors;
    private readonly Lazy<PluginSuggestions> suggestions;
    private readonly Lazy<DocumentSymbols>? symbols;
    private readonly Dictionary<IDocument, DocumentPanel> panels = [];
    private readonly Dictionary<IDocument, DispatcherTimer> autoSaveTimers = [];
    private readonly Dictionary<IDocument, int> borrowed = [];
    private readonly Dictionary<TextEditor, Func<bool, Task<bool>>> attached = [];
    private readonly Stack<(string Path, int Offset)> back = new();
    private readonly Stack<(string Path, int Offset)> forward = new();
    private EditorOptions options;
    private TextEditor? active;

    [ImportingConstructor]
    public EditorService(
        IDocumentService documents,
        EditorServices editorServices,
        ShellLayout layout,
        ISettingsService settings,
        NotificationService notifications,
        ShellController shell,
        ILanguageRegistry languages,
        FileIcons fileIcons,
        IWorkspace workspace,
        Lazy<ExplorerToolWindow> explorer,
        ChangeBases changeBases,
        Lazy<IDiffService> diffs,
        Lazy<CommandService> commands,
        Lazy<CustomEditorService> customEditors,
        Lazy<PluginSuggestions> suggestions,
        [Import(AllowDefault = true)] Lazy<DocumentSymbols>? symbols = null)
    {
        this.symbols = symbols;
        this.documents = documents;
        this.editorServices = editorServices;
        this.layout = layout;
        this.settings = settings;
        this.notifications = notifications;
        this.shell = shell;
        this.languages = languages;
        this.fileIcons = fileIcons;
        this.workspace = workspace;
        this.explorer = explorer;
        this.changeBases = changeBases;
        this.diffs = diffs;
        this.commands = commands;
        this.customEditors = customEditors;
        this.suggestions = suggestions;
        options = ReadOptions();
        settings.Changed += OnSettingChanged;
        documents.ChangedOnDisk += OnChangedOnDisk;
        FollowLayout();
    }

    public IEditorView? ActiveEditor => active;

    public IReadOnlyList<IEditorView> Editors => [.. panels.Values.Select(p => p.Editor)];

    /// <summary>Gets the active editor as the editor control.</summary>
    public TextEditor? Active => active;

    public bool CanGoBack => back.Count > 0;

    public bool CanGoForward => forward.Count > 0;

    public event EventHandler? ActiveEditorChanged;

    public async Task<IEditorView?> OpenAsync(string filePath, TextPosition? position = null, bool activate = true)
    {
        if (await customEditors.Value.TryOpenAsync(filePath, activate, toText: position is not null))
            return null;

        var editor = (TextEditor?)await OpenInTextEditorAsync(filePath, activate);
        if (editor is not null && position is { } at)
            editor.GoTo(at);
        return editor;
    }

    /// <summary>Opens a file in the text editor whatever its default editor is; with <paramref name="anyContent"/> a file that looks binary
    /// opens read-only. Returns null when it could not be opened, and the user has been told why.</summary>
    internal async Task<IEditorView?> OpenInTextEditorAsync(string filePath, bool activate = true, bool anyContent = false)
    {
        IDocument document;
        try
        {
            document = anyContent && documents is Workspace.Documents.DocumentService service
                ? await service.OpenAsTextAsync(filePath)
                : await documents.OpenAsync(filePath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            notifications.Notify(NotificationKind.Error, $"{Path.GetFileName(filePath)} could not be opened", exception.Message);
            return null;
        }

        return Open(document, activate);
    }

    public IEditorView Open(IDocument document, bool activate = true)
    {
        if (!panels.TryGetValue(document, out var panel))
        {
            var editor = CreateEditor(document);
            document.Buffer.Changed += (_, _) => ScheduleAutoSave(document);
            panel = CreatePanel(document, editor);
            panels[document] = panel;
            layout.Register(panel);
            UpdateDescriptions();
            if (suggestions.Value.For(document.FilePath) is { } plugin)
                panel.ShowNotice(PluginSuggestionBar.Create(plugin, suggestions.Value, panel.HideNotice));
        }

        layout.AddToEditors(panel.Id);
        if (activate)
        {
            SetActive(panel.Editor);
            Dispatcher.UIThread.Post(() => ((IEditorView)panel.Editor).Focus(), DispatcherPriority.Background);
        }

        return panel.Editor;
    }

    public async Task<bool> CloseAsync(IEditorView editor)
    {
        ArgumentNullException.ThrowIfNull(editor);
        if (editor is TextEditor textEditor && attached.TryGetValue(textEditor, out var close))
            return await close(true);

        if (!panels.TryGetValue(editor.Document, out var panel))
            return true;

        var isBorrowed = borrowed.ContainsKey(editor.Document);
        if (editor.Document.IsModified && !isBorrowed)
        {
            layout.Layout.EnsureVisible(panel.Id);
            switch (await notifications.Dialogs.AskToSaveChangesAsync(editor.Document.Name))
            {
                case UnsavedChangesChoice.Cancel:
                    return false;
                case UnsavedChangesChoice.Save when !await SaveAsync(editor.Document):
                    return false;
            }
        }

        layout.Layout.ClosePanel(panel.Id);
        layout.Unregister(panel.Id);
        panels.Remove(editor.Document);
        if (autoSaveTimers.Remove(editor.Document, out var timer))
            timer.Stop();
        if (!isBorrowed)
            documents.Close(editor.Document);
        DisposePanel(panel);
        UpdateDescriptions();
        if (ReferenceEquals(active, panel.Editor))
            SetActive(panels.Values.LastOrDefault()?.Editor);
        return true;
    }

    /// <summary>Closes the editor of a dock panel, asking first when it has unsaved changes.</summary>
    public Task<bool> ClosePanelAsync(string panelId) =>
        panels.Values.FirstOrDefault(p => p.Id == panelId) is { } panel ? CloseAsync(panel.Editor) : Task.FromResult(true);

    /// <summary>Closes every editor, asking once about all unsaved changes; returns whether they all closed.</summary>
    public async Task<bool> CloseAllAsync()
    {
        if (!await ConfirmLosingChangesAsync())
            return false;

        foreach (var close in attached.Values.ToList())
            await close(false);

        foreach (var panel in panels.Values.ToList())
        {
            layout.Layout.ClosePanel(panel.Id);
            layout.Unregister(panel.Id);
            documents.Close(panel.Editor.Document);
            DisposePanel(panel);
        }

        panels.Clear();
        await customEditors.Value.CloseAllAsync();
        SetActive(null);
        return true;
    }

    /// <summary>Asks what to do with unsaved changes before they would be lost, such as when the window closes; returns whether to go on.</summary>
    public async Task<bool> ConfirmLosingChangesAsync()
    {
        var modified = panels.Keys.Concat(borrowed.Keys).Distinct().Where(d => d.IsModified).ToList();
        var custom = customEditors.Value.Panels.Where(p => p.IsModified).ToList();
        if (modified.Count + custom.Count == 0)
            return true;

        var title = modified.Count + custom.Count == 1 ? modified.FirstOrDefault()?.Name ?? custom[0].Title : $"{modified.Count + custom.Count} files";
        switch (await notifications.Dialogs.AskToSaveChangesAsync(title))
        {
            case UnsavedChangesChoice.Cancel:
                return false;
            case UnsavedChangesChoice.Save:
                foreach (var document in modified)
                {
                    if (!await SaveAsync(document))
                        return false;
                }

                foreach (var panel in custom)
                {
                    if (!await customEditors.Value.SaveAsync(panel))
                        return false;
                }

                return true;
            default:
                return true;
        }
    }

    /// <summary>Opens a file's document for an editor outside the tabs, such as the working copy side of a diff, and keeps it open until it is
    /// returned, even when its own tab closes; null when it cannot be opened, and the user has been told why.</summary>
    public async Task<IDocument?> BorrowAsync(string filePath)
    {
        IDocument document;
        try
        {
            document = await documents.OpenAsync(filePath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            notifications.Notify(NotificationKind.Error, $"{Path.GetFileName(filePath)} could not be opened", exception.Message);
            return null;
        }

        borrowed[document] = borrowed.GetValueOrDefault(document) + 1;
        return document;
    }

    /// <summary>Gives back a borrowed document; the last one closes it unless a tab shows it, asking first about unsaved changes when
    /// <paramref name="ask"/> is set. Returns false when the user cancels.</summary>
    public async Task<bool> ReturnAsync(IDocument document, bool ask)
    {
        ArgumentNullException.ThrowIfNull(document);
        var count = borrowed.GetValueOrDefault(document);
        if (count > 1 || panels.ContainsKey(document))
        {
            Release(document, count);
            return true;
        }

        if (ask && document.IsModified)
        {
            switch (await notifications.Dialogs.AskToSaveChangesAsync(document.Name))
            {
                case UnsavedChangesChoice.Cancel:
                    return false;
                case UnsavedChangesChoice.Save when !await SaveAsync(document):
                    return false;
            }
        }

        Release(document, count);
        if (count > 0)
            documents.Close(document);
        return true;
    }

    /// <summary>Lets an editor outside the tabs become the active one, so Save and the status bar work on it, and closes it through
    /// <paramref name="close"/>, which gets whether to ask about unsaved changes.</summary>
    public void Attach(TextEditor editor, Func<bool, Task<bool>> close)
    {
        ArgumentNullException.ThrowIfNull(editor);
        attached[editor] = close;
        editor.GotFocus += OnAttachedEditorFocused;
    }

    public void Detach(TextEditor editor)
    {
        ArgumentNullException.ThrowIfNull(editor);
        attached.Remove(editor);
        editor.GotFocus -= OnAttachedEditorFocused;
        if (ReferenceEquals(active, editor))
            SetActive(panels.Values.LastOrDefault()?.Editor);
    }

    /// <summary>Gets the editors' options, as the settings set them.</summary>
    public EditorOptions Options => options;

    /// <summary>Raised when the settings change <see cref="Options"/>.</summary>
    public event EventHandler? OptionsChanged;

    private void OnAttachedEditorFocused(object? sender, FocusChangedEventArgs e) => SetActive((TextEditor)sender!);

    private void Release(IDocument document, int count)
    {
        if (count > 1)
            borrowed[document] = count - 1;
        else
            borrowed.Remove(document);
    }

    /// <summary>Saves a document, asking for a file when it has none; returns whether it was saved.</summary>
    /// <param name="format">Whether to format the document first when <see cref="SettingKeys.FormatOnSave"/> is on; auto save after a delay
    /// does not, so text does not move while the user types.</param>
    public async Task<bool> SaveAsync(IDocument document, bool format = true)
    {
        if (document.FilePath is null)
            return await SaveAsAsync(document);

        if (format)
            await FormatOnSave.RunAsync(settings, panels.GetValueOrDefault(document)?.Editor);

        try
        {
            await documents.SaveAsync(document);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            notifications.Notify(NotificationKind.Error, $"{document.Name} could not be saved", exception.Message);
            return false;
        }
    }

    public async Task<bool> SaveAsAsync(IDocument document)
    {
        if (await shell.PickFileToSaveAsync("Save As", document.FilePath is { } path ? Path.GetFileName(path) : document.Name) is not { } target)
            return false;

        var oldId = panels.TryGetValue(document, out var panel) ? panel.Id : null;
        try
        {
            await documents.SaveAsAsync(document, target);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            notifications.Notify(NotificationKind.Error, $"{Path.GetFileName(target)} could not be saved", exception.Message);
            return false;
        }

        if (panel is not null && oldId is not null)
            RenamePanel(document, panel, oldId);
        return true;
    }

    public async Task SaveAllAsync()
    {
        foreach (var document in panels.Keys.Where(d => d.IsModified).ToList())
            await SaveAsync(document);

        foreach (var panel in customEditors.Value.Panels.Where(p => p.IsModified).ToList())
            await customEditors.Value.SaveAsync(panel);
    }

    /// <summary>Opens a new, empty document.</summary>
    public void NewFile() => Open(documents.CreateUntitled());

    /// <summary>Goes back to where the caret was before the last jump.</summary>
    public Task GoBackAsync() => MoveThroughHistoryAsync(back, forward);

    public Task GoForwardAsync() => MoveThroughHistoryAsync(forward, back);

    /// <summary>Opens a file and remembers where the caret was, so Back returns there.</summary>
    public async Task JumpToAsync(string filePath)
    {
        RememberCaret();
        forward.Clear();
        await OpenAsync(filePath);
    }

    /// <summary>Opens a file at a position and remembers where the caret was, so Back returns there.</summary>
    public async Task JumpToAsync(string filePath, TextPosition position)
    {
        RememberCaret();
        forward.Clear();
        await OpenAsync(filePath, position);
    }

    /// <summary>Opens a file and selects a place in it. With <paramref name="focus"/> the editor gets the keyboard focus and Back returns to where
    /// the caret was; without it the file only shows, such as while the user moves through a list of results.</summary>
    public async Task<TextEditor?> RevealAsync(string filePath, TextPosition start, TextPosition? end, bool focus)
    {
        if (focus)
        {
            RememberCaret();
            forward.Clear();
        }

        if (await OpenInTextEditorAsync(filePath, activate: focus) is not TextEditor editor)
            return null;

        var snapshot = editor.Area.Snapshot;
        var from = snapshot.GetOffset(start);
        var to = end is { } last ? Math.Max(from, snapshot.GetOffset(last)) : from;
        editor.Area.Select(new EditorSelection(from, to), scrollIntoView: false);
        editor.Area.ScrollIntoView(from, center: true);
        if (focus)
            editor.Area.Focus();
        return editor;
    }

    /// <summary>Makes the editor text bigger or smaller by changing the font size setting.</summary>
    public void Zoom(int delta)
    {
        var size = settings.Get<double>(SettingKeys.FontSize);
        settings.Set(SettingKeys.FontSize, Math.Clamp(size + delta, 6, 72));
    }

    public void ResetZoom() => settings.Reset(SettingKeys.FontSize);

    private async Task NavigateAsync(TextEditor from, IReadOnlyList<Sdk.Languages.DocumentLocation> locations)
    {
        if (locations.Count == 0)
        {
            notifications.Notify(NotificationKind.Info, "No definition found", "The language does not know where this is defined.");
            return;
        }

        var target = locations[0];
        if (locations.Count > 1)
        {
            var rows = locations.Select(l => new QuickPickItem(Path.GetFileName(l.FilePath), l)
            {
                Subtitle = $"{l.FilePath}:{l.Start.Line + 1}",
                Icon = Icons.FileCode,
            }).ToList();
            if (await shell.PickAsync("Pick a definition", rows) is not { Value: Sdk.Languages.DocumentLocation picked })
                return;
            target = picked;
        }

        SetActive(from);
        await JumpToAsync(target.FilePath, target.Start);
    }

    private async Task MoveThroughHistoryAsync(Stack<(string Path, int Offset)> from, Stack<(string Path, int Offset)> to)
    {
        if (from.Count == 0)
            return;

        if (active?.Document.FilePath is { } current)
            to.Push((current, active.CaretOffset));

        var (path, offset) = from.Pop();
        if (await OpenAsync(path) is TextEditor editor)
        {
            editor.CaretOffset = Math.Min(offset, editor.Area.Snapshot.Length);
            editor.ScrollTo(editor.CaretOffset);
        }
    }

    private void RememberCaret()
    {
        if (active?.Document.FilePath is not { } path)
            return;

        back.Push((path, active.CaretOffset));
        if (back.Count > HistoryLimit)
        {
            var kept = back.Take(HistoryLimit).Reverse().ToList();
            back.Clear();
            foreach (var entry in kept)
                back.Push(entry);
        }
    }

    private void RenamePanel(IDocument document, DocumentPanel panel, string oldId)
    {
        var group = layout.Layout.FindPanel(oldId);
        var index = group is null ? -1 : group.Panels.ToList().IndexOf(oldId);
        layout.Layout.ClosePanel(oldId);
        layout.Unregister(oldId);
        DisposePanel(panel);

        var editor = CreateEditor(document);
        var renamed = CreatePanel(document, editor);
        panels[document] = renamed;
        layout.Register(renamed);
        UpdateDescriptions();
        if (group is not null && layout.Layout.FindNode(group.Id) is not null)
            layout.Layout.MovePanel(renamed.Id, group.Id, index);
        else
            layout.AddToEditors(renamed.Id);
        layout.Layout.ActivatePanel(renamed.Id);
        SetActive(editor);
    }

    private void FollowLayout() => layout.Layout.Changed += (_, _) =>
    {
        if (layout.Layout.FocusedGroup?.ActivePanel is { } id && panels.Values.FirstOrDefault(p => p.Id == id) is { } panel)
            SetActive(panel.Editor);
        else if (layout.Layout.FocusedGroup?.ActivePanel is { } other && CustomEditorPanel.IsCustomEditorPanel(other))
            SetActive(null);
    };

    private void SetActive(TextEditor? editor)
    {
        if (ReferenceEquals(active, editor))
            return;

        if (active is not null && settings.Get<string>(SettingKeys.AutoSave) == "onFocusChange" && active.Document is { IsModified: true, FilePath: not null } previous)
            _ = SaveAsync(previous);

        active = editor;
        ActiveEditorChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ScheduleAutoSave(IDocument document)
    {
        if (settings.Get<string>(SettingKeys.AutoSave) != "afterDelay" || document.FilePath is null)
            return;

        if (!autoSaveTimers.TryGetValue(document, out var timer))
        {
            timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                if (document.IsModified && panels.ContainsKey(document))
                    _ = SaveAsync(document, format: false);
            };
            autoSaveTimers[document] = timer;
        }

        timer.Stop();
        timer.Start();
    }

    /// <summary>Saves modified documents when the window loses focus, if auto save asks for it.</summary>
    public void OnWindowDeactivated()
    {
        if (settings.Get<string>(SettingKeys.AutoSave) != "onFocusChange")
            return;

        foreach (var document in panels.Keys.Where(d => d is { IsModified: true, FilePath: not null }).ToList())
            _ = SaveAsync(document);
    }

    private void OnChangedOnDisk(object? sender, DocumentEventArgs e)
    {
        if (!panels.ContainsKey(e.Document))
            return;

        var exists = e.Document.FilePath is { } path && File.Exists(path);
        if (exists && !e.Document.IsModified)
            return;

        notifications.Notify(
            NotificationKind.Warning,
            exists ? $"{e.Document.Name} changed on disk" : $"{e.Document.Name} was deleted",
            exists ? "Your unsaved changes are kept. Reload to take the version on disk instead." : "Its text stays open; save it to create the file again.",
            exists ? "Reload" : null,
            exists ? () => _ = documents.ReloadAsync(e.Document) : null);
    }

    private void OnSettingChanged(object? sender, SettingChangedEventArgs e)
    {
        if (!e.Key.StartsWith("editor.", StringComparison.Ordinal))
            return;

        if (e.Key == ShellSettings.Breadcrumbs)
        {
            foreach (var (document, panel) in panels)
                panel.ShowsBreadcrumbs = ShowsBreadcrumbs(document);
            return;
        }

        options = ReadOptions();
        foreach (var panel in panels.Values)
            panel.Editor.Options = options;
        OptionsChanged?.Invoke(this, EventArgs.Empty);
    }

    private EditorOptions ReadOptions() => new()
    {
        FontFamily = settings.Get<string>(SettingKeys.FontFamily),
        FontSize = settings.Get<double>(SettingKeys.FontSize),
        Ligatures = settings.Get<bool>(SettingKeys.Ligatures),
        TabSize = settings.Get<int>(SettingKeys.TabSize),
        InsertSpaces = settings.Get<bool>(SettingKeys.InsertSpaces),
        ShowLineNumbers = settings.Get<bool>(SettingKeys.LineNumbers),
        ShowIndentGuides = settings.Get<bool>(SettingKeys.IndentGuides),
        HighlightCurrentLine = settings.Get<bool>(SettingKeys.HighlightCurrentLine),
        AutoCloseBrackets = settings.Get<bool>(SettingKeys.AutoCloseBrackets),
        ShowWhitespace = settings.Get<bool>(SettingKeys.ShowWhitespace),
        CompletionOnType = settings.Get<bool>(SettingKeys.CompletionOnType),
        ShowInlayHints = settings.Get<bool>(SettingKeys.InlayHints),
        ShowCodeLens = settings.Get<bool>(SettingKeys.CodeLens),
    };

    private TextEditor CreateEditor(IDocument document)
    {
        var editor = new TextEditor(document, editorServices) { Options = options };
        editor.ZoomRequested += (_, delta) => Zoom(delta);
        editor.NavigationRequested += (_, locations) => _ = NavigateAsync(editor, locations);
        editor.GotFocus += (_, _) => SetActive(editor);
        editor.ShowChangesRequested += (_, _) => ShowChanges(editor);
        editor.Area.ContextRequested += (_, _) =>
            editor.Area.ContextMenu = MenuBuilder.ContextMenu(commands.Value, ContextMenuCommands(editor), ContextMenus.Editor, ContextTarget(editor));
        // Avalonia opens a context menu only from a control that had one when the request began, so each starts with an empty one.
        editor.Area.ContextMenu = new Avalonia.Controls.ContextMenu();
        changeBases.Track(editor.Area);
        return editor;
    }

    /// <summary>Gets what the editor's context menu gives plugins' commands: the file and the lines the selection covers, or null for a
    /// document that is not saved to a file.</summary>
    internal static ContextMenuTarget? ContextTarget(TextEditor editor)
    {
        if (editor.Document.FilePath is not { } path)
            return null;

        var snapshot = editor.Area.Snapshot;
        var selection = editor.Area.Selection;
        var start = snapshot.GetPosition(selection.Start);
        var end = snapshot.GetPosition(selection.End);
        var last = !selection.IsEmpty && end.Column == 0 && end.Line > start.Line ? end.Line - 1 : end.Line;
        return new ContextMenuTarget(path) { Lines = (start.Line + 1, last + 1) };
    }

    private void DisposePanel(DocumentPanel panel)
    {
        symbols?.Value.Release(panel.Editor);
        changeBases.Untrack(panel.Editor.Area);
        panel.Dispose();
    }

    private static string?[] ContextMenuCommands(TextEditor editor)
    {
        string?[] clipboard =
        [
            CommandIds.Cut, CommandIds.Copy, CommandIds.Paste, null, CommandIds.ShowCodeActions, CommandIds.GoToDefinition, CommandIds.GoToImplementation,
            CommandIds.FindReferences, CommandIds.ShowCallHierarchy, CommandIds.ShowTypeHierarchy, CommandIds.Rename, CommandIds.FormatDocument,
        ];
        return editor.Area.BaseText is null
            ? clipboard
            : [.. clipboard, null, CoreCommands.PreviousChange, CoreCommands.NextChange, CoreCommands.RollbackLines, CoreCommands.ShowChanges];
    }

    /// <summary>Opens the diff of an editor's document against the base its change markers compare with.</summary>
    public void ShowChanges(TextEditor editor)
    {
        ArgumentNullException.ThrowIfNull(editor);
        if (editor.Area.BaseText is not { } baseText || editor.Document.FilePath is not { } path)
            return;

        diffs.Value.Open(
            $"{editor.Document.Name} (changes)",
            new DiffSide("Base", baseText) { FilePath = path },
            new DiffSide("Working copy", editor.Area.Snapshot.GetText()) { FilePath = path, IsEditable = true });
    }

    private DocumentPanel CreatePanel(IDocument document, TextEditor editor)
    {
        var breadcrumbs = new PathBreadcrumbs(path => explorer.Value.Reveal(path));
        breadcrumbs.Show(document.FilePath, workspace.RootPath, Icons.Find(IconOf(document.LanguageId)));
        var symbolPath = symbols is null || document.FilePath is null ? null : new SymbolBreadcrumbs(editor, symbols.Value.For(editor));
        return new DocumentPanel(ShellLayout.DocumentPanelId(document.FilePath ?? document.Name), editor, fileIcons, breadcrumbs, symbolPath)
        {
            ShowsBreadcrumbs = ShowsBreadcrumbs(document),
        };
    }

    private bool ShowsBreadcrumbs(IDocument document) => document.FilePath is not null && settings.Get<bool>(ShellSettings.Breadcrumbs);

    // Tabs of files with the same name show their folders, so they can be told apart.
    private void UpdateDescriptions()
    {
        foreach (var group in panels.GroupBy(p => p.Key.Name, StringComparer.Ordinal))
        {
            var isShared = group.Count() > 1;
            foreach (var (document, panel) in group)
                panel.Description = isShared && document.FilePath is { } path ? Path.GetFileName(Path.GetDirectoryName(path)) : null;
        }
    }

    private string IconOf(string languageId) => languages.Find(languageId)?.Icon ?? "file";
}
