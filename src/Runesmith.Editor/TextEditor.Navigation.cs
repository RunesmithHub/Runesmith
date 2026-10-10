using Runesmith.Editor.Folding;
using Runesmith.Editor.Navigation;
using Runesmith.Sdk.Languages;
using Runesmith.Text;

namespace Runesmith.Editor;

public sealed partial class TextEditor
{
    private FoldingController folding = null!;
    private OccurrenceController occurrences = null!;
    private SelectionExpander selectionExpander = null!;
    private CancellationTokenSource? navigationRequest;

    /// <summary>Finds every use of the symbol at the caret, with its declaration; null when the request was cancelled by a newer one.</summary>
    public Task<IReadOnlyList<DocumentLocation>?> FindReferencesAsync() =>
        NavigateAsync((snapshot, offset, token) => services.Navigation.GetReferencesAsync(Document, snapshot, offset, includeDeclaration: true, token));

    /// <summary>Finds the implementations of the symbol at the caret; null when the request was cancelled by a newer one.</summary>
    public Task<IReadOnlyList<DocumentLocation>?> FindImplementationsAsync() =>
        NavigateAsync((snapshot, offset, token) => services.Navigation.GetImplementationsAsync(Document, snapshot, offset, token));

    /// <summary>Gets the word at the caret, such as the name a references search is about, or null.</summary>
    public string? WordAtCaret()
    {
        var word = WordBoundaries.GetWordAt(Area.Snapshot, Area.Selection.Caret);
        return word.IsEmpty ? null : Area.Snapshot.GetText(word);
    }

    /// <summary>Grows the selection to the next larger element around it.</summary>
    public Task ExpandSelectionAsync() => selectionExpander.ExpandAsync();

    /// <summary>Shrinks the selection back to what it was before the last expand.</summary>
    public void ShrinkSelection() => selectionExpander.Shrink();

    /// <summary>Folds the innermost unfolded region around the caret; returns whether there was one.</summary>
    public bool Fold()
    {
        var line = Area.Snapshot.GetLineFromPosition(Area.Selection.Caret).LineNumber;
        if (Area.Folding.Innermost(line, collapsed: false) is not { } region)
            return false;

        Area.SetFolded(region, true);
        return true;
    }

    /// <summary>Unfolds the folded region on the caret's line; returns whether there was one.</summary>
    public bool Unfold()
    {
        var line = Area.Snapshot.GetLineFromPosition(Area.Selection.Caret).LineNumber;
        if ((Area.Folding.RegionAt(line) is { IsCollapsed: true } at ? at : Area.Folding.Innermost(line, collapsed: true)) is not { } region)
            return false;

        Area.SetFolded(region, false);
        return true;
    }

    /// <summary>Folds every region.</summary>
    public void FoldAll()
    {
        Area.Folding.CollapseAll();
        Area.KeepSelectionVisible();
    }

    /// <summary>Unfolds every region.</summary>
    public void UnfoldAll() => Area.Folding.ExpandAll();

    private void InitializeNavigation()
    {
        folding = new FoldingController(Area, services.Syntax);
        occurrences = new OccurrenceController(Area, services.Navigation);
        selectionExpander = new SelectionExpander(Area, services.Syntax);
        Area.ShowsFolding = true;
    }

    private void EnableNavigation(bool enabled)
    {
        folding.IsEnabled = enabled;
        occurrences.IsEnabled = enabled;
        Area.ShowsFolding = enabled;
    }

    private void DisposeNavigation()
    {
        Cancellation.Cancel(ref navigationRequest);
        folding.Dispose();
        occurrences.Dispose();
    }

    private async Task<IReadOnlyList<DocumentLocation>?> NavigateAsync(Func<TextSnapshot, int, CancellationToken, Task<IReadOnlyList<DocumentLocation>>> find)
    {
        var token = Cancellation.Renew(ref navigationRequest);
        var snapshot = Area.Snapshot;
        var offset = Area.Selection.Caret;
        try
        {
            var found = await Task.Run(() => find(snapshot, offset, token), token);
            return token.IsCancellationRequested ? null : found;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (LanguageFeatureException exception)
        {
            ShowMessage(exception.Message);
            return null;
        }
    }
}
