using System.Composition;
using Runesmith.Editor;
using Runesmith.Sdk.Documents;
using Runesmith.Sdk.Languages;
using Runesmith.Shell.Docking;
using Runesmith.Shell.Editors;

namespace Runesmith.Shell.Diffs;

/// <summary>Opens diffs as editor tabs; a working copy side edits the file's own document, so saving it works as in its editor.</summary>
[Export(typeof(IDiffService))]
[Export]
[Shared]
[method: ImportingConstructor]
public sealed class DiffService(ShellLayout layout, EditorServices editorServices, ILanguageRegistry languages, Lazy<EditorService> editors) : IDiffService
{
    private readonly Dictionary<string, DiffPanel> panels = new(StringComparer.Ordinal);
    private bool followsOptions;

    public void Open(string title, DiffSide left, DiffSide right)
    {
        ArgumentException.ThrowIfNullOrEmpty(title);
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        UiThread.Run(() => _ = OpenAsync(title, left, right));
    }

    /// <summary>Gets whether a dock panel is a diff's.</summary>
    public static bool IsDiffPanel(string panelId) => DiffPanel.IsDiffPanel(panelId);

    /// <summary>Closes a diff's tab, giving its working copy back; returns false when the user keeps unsaved changes open.</summary>
    public async Task<bool> CloseAsync(string panelId, bool ask = true)
    {
        if (!panels.TryGetValue(panelId, out var panel))
            return true;

        if (panel.WorkingCopy is { } document && !await editors.Value.ReturnAsync(document, ask))
            return false;

        editors.Value.Detach(panel.View.Right);
        layout.Layout.ClosePanel(panelId);
        layout.Unregister(panelId);
        panels.Remove(panelId);
        panel.Dispose();
        return true;
    }

    private async Task OpenAsync(string title, DiffSide left, DiffSide right)
    {
        var id = DiffPanel.IdOf(title);
        if (panels.ContainsKey(id))
        {
            layout.AddToEditors(id);
            layout.Layout.ActivatePanel(id);
            return;
        }

        var workingCopy = right is { IsEditable: true, FilePath: { } path } ? await editors.Value.BorrowAsync(path) : null;
        if (panels.ContainsKey(id))
        {
            if (workingCopy is not null)
                await editors.Value.ReturnAsync(workingCopy, ask: false);
            layout.Layout.ActivatePanel(id);
            return;
        }

        if (!followsOptions)
        {
            followsOptions = true;
            editors.Value.OptionsChanged += (_, _) =>
            {
                foreach (var open in panels.Values)
                    open.View.ApplyOptions(editors.Value.Options);
            };
        }

        var languageId = languages.GetLanguageForFile(left.FilePath ?? right.FilePath ?? title).Id;
        var view = new DiffView(
            new ReadOnlyDocument(left.Title, left.Text, languageId),
            left.Title,
            workingCopy ?? new ReadOnlyDocument(right.Title, right.Text, languageId),
            right.Title,
            workingCopy is not null,
            editorServices,
            editors.Value.Options);
        var panel = new DiffPanel(title, view, workingCopy, right.FilePath ?? left.FilePath);
        panels[id] = panel;
        layout.Register(panel);
        layout.AddToEditors(id);
        layout.Layout.ActivatePanel(id);
        if (workingCopy is not null)
            editors.Value.Attach(view.Right, ask => CloseAsync(id, ask));
    }
}
