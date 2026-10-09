using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Runesmith.Editor.CodeActions;
using Runesmith.Editor.Decorations;
using Runesmith.Editor.Editing;
using Runesmith.Editor.Popups;
using Runesmith.Sdk.Documents;
using Runesmith.Sdk.Languages;
using Runesmith.Text;

namespace Runesmith.Editor;

/// <summary>How a format request ended.</summary>
public enum FormatResult
{
    /// <summary>The text was formatted, or was formatted already.</summary>
    Formatted,

    /// <summary>No provider formats the document's language.</summary>
    NoFormatter,

    /// <summary>The text changed while the formatter ran, or the request was cancelled, so nothing was changed.</summary>
    Cancelled,
}

public sealed partial class TextEditor
{
    private static readonly TimeSpan MessageTime = TimeSpan.FromSeconds(4);

    private DecorationController decorationController = null!;
    private CodeActionController codeActions = null!;
    private RenameController rename = null!;
    private EditorPopup messagePopup = null!;
    private DispatcherTimer messageTimer = null!;
    private CancellationTokenSource? formatRequest;

    /// <summary>Gets or sets whether the editor asks the decoration providers for gutter icons, inlay hints, code lenses and highlights; diffs
    /// turn it off so their sides stay aligned.</summary>
    public bool IsDecorated
    {
        get => decorationController.IsEnabled;
        set => decorationController.IsEnabled = value;
    }

    /// <summary>Gets or sets how the caret is drawn.</summary>
    public EditorCaretStyle CaretStyle
    {
        get => Area.CaretStyle;
        set => Area.CaretStyle = value;
    }

    internal CodeActionController CodeActions => codeActions;

    internal RenameController Renaming => rename;

    /// <summary>Gets whether the inline rename box is open.</summary>
    public bool IsRenaming => rename.IsOpen;

    /// <summary>Shows the quick fixes and refactorings at the caret or selection in a menu.</summary>
    public Task ShowCodeActionsAsync() => codeActions.ShowMenuAsync();

    /// <summary>Starts renaming the symbol at the caret with the inline rename box.</summary>
    public Task RenameAsync() => rename.StartAsync();

    /// <summary>Formats the document, or with <paramref name="selectionOnly"/> the selected lines, as one undo step.</summary>
    /// <param name="quiet">Whether to say nothing when no formatter serves the document, as for format on save.</param>
    public async Task<FormatResult> FormatAsync(bool selectionOnly = false, bool quiet = false, CancellationToken cancellationToken = default)
    {
        var token = Cancellation.Renew(ref formatRequest);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, cancellationToken);
        var snapshot = Area.Snapshot;
        var selection = Area.Selection;
        var options = new FormattingOptions(Options.TabSize, Options.InsertSpaces);
        TextSpan? span = selectionOnly && !selection.IsEmpty ? LinesOf(snapshot, selection.Span) : null;
        IReadOnlyList<TextChange>? changes;
        try
        {
            var features = services.EditorFeatures;
            var formatToken = linked.Token;
            changes = await Task.Run(() => span is { } range
                ? features.FormatRangeAsync(Document, snapshot, range, options, formatToken)
                : features.FormatDocumentAsync(Document, snapshot, options, formatToken), formatToken);
        }
        catch (OperationCanceledException)
        {
            return FormatResult.Cancelled;
        }
        catch (LanguageFeatureException exception)
        {
            if (!quiet)
                ShowMessage(exception.Message);
            return FormatResult.Cancelled;
        }

        if (changes is null)
        {
            if (!quiet)
                ShowMessage($"No formatter for {services.Languages.Find(Document.LanguageId)?.Name ?? Document.LanguageId} files is installed.");
            return FormatResult.NoFormatter;
        }

        if (linked.IsCancellationRequested || !ReferenceEquals(snapshot, Area.Snapshot))
            return FormatResult.Cancelled;

        var effective = changes.Where(change => change.Span.End <= snapshot.Length && snapshot.GetText(change.Span) != change.NewText).ToList();
        if (effective.Count > 0)
        {
            var anchor = OffsetMapping.Map(effective, selection.Anchor);
            var caret = OffsetMapping.Map(effective, selection.Caret);
            Area.ApplyChanges(effective, new EditorSelection(anchor, caret));
        }

        return FormatResult.Formatted;
    }

    /// <summary>Shows a short message beside the caret, such as why a rename cannot be done.</summary>
    public void ShowMessage(string message)
    {
        var text = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, FontSize = 12.5 };
        messagePopup.Show(text, Area.GetCaretRect());
        messageTimer.Stop();
        messageTimer.Start();
    }

    private void InitializeFeatures(Grid grid)
    {
        decorationController = new DecorationController(Area, services.EditorFeatures);
        codeActions = new CodeActionController(Area, services, ShowMessage);
        rename = new RenameController(Area, services, ShowMessage);
        messagePopup = new EditorPopup(Area, above: false);
        messageTimer = new DispatcherTimer { Interval = MessageTime };
        messageTimer.Tick += (_, _) => HideMessage();

        var ruler = new OverviewRuler(Area);
        Grid.SetColumn(ruler, 1);
        grid.Children.Add(ruler);
        grid.Children.Add(rename.Popup);
        grid.Children.Add(messagePopup);

        Area.DecorationCommandRequested += (_, command) => _ = services.Commands.Value.ExecuteAsync(command.CommandId, command.Argument);
        Area.SelectionChanged += (_, _) =>
        {
            HideMessage();
            codeActions.OnCaretOrTextChanged();
        };
        AddHandler(TextInputEvent, OnHookTextInput, RoutingStrategies.Tunnel);
    }

    // Plugins' keyboard hooks see typed text before the editor inserts it.
    private void OnHookTextInput(object? sender, TextInputEventArgs e)
    {
        if (!e.Handled && Area.IsFocused && e.Text is { Length: > 0 } text && services.KeyHooks.TextInput(this, text))
            e.Handled = true;
    }

    // Returns whether a plugin's keyboard hook handled the key.
    private bool HandleKeyHooks(KeyEventArgs e) => Area.IsFocused && services.KeyHooks.KeyDown(this, e.Key, e.KeyModifiers);

    private void HideMessage()
    {
        messageTimer.Stop();
        if (messagePopup.IsOpen)
            messagePopup.Hide();
    }

    private void DisposeFeatures()
    {
        Cancellation.Cancel(ref formatRequest);
        decorationController.Dispose();
        codeActions.Dispose();
        rename.Dispose();
        HideMessage();
    }

    // Formatting a selection formats its whole lines, as formatters work on lines.
    private static TextSpan LinesOf(TextSnapshot snapshot, TextSpan span)
    {
        var first = snapshot.GetLineFromPosition(span.Start);
        var last = snapshot.GetLineFromPosition(span.End > span.Start && span.End == snapshot.GetLineFromPosition(span.End).Start ? span.End - 1 : span.End);
        return TextSpan.FromBounds(first.Start, last.End);
    }
}
