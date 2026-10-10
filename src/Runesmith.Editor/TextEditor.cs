using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Runesmith.Editor.Completion;
using Runesmith.Editor.Editing;
using Runesmith.Editor.Find;
using Runesmith.Editor.Popups;
using Runesmith.Sdk.Build;
using Runesmith.Sdk.Documents;
using Runesmith.Sdk.Languages;
using Runesmith.Text;

namespace Runesmith.Editor;

/// <summary>The code editor: a document's text with scroll bars, find and replace, completion, signature help, hover, problems, decorations,
/// code actions, rename and formatting.</summary>
public sealed partial class TextEditor : UserControl, IEditorView, IDisposable
{
    private readonly EditorServices services;
    private readonly ScrollBar verticalBar = new() { Orientation = Orientation.Vertical, AllowAutoHide = true };
    private readonly ScrollBar horizontalBar = new() { Orientation = Orientation.Horizontal, AllowAutoHide = true };
    private readonly FindBar findBar;
    private readonly CompletionController completion;
    private readonly SignatureHelpController signatureHelp;
    private readonly HoverController hover;
    private readonly ChangePopup changePopup;
    private CancellationTokenSource? definitionRequest;
    private bool isSyncingScrollBars;
    private Point lastPointer;
    private Vector lastScrollOffset;
    private bool isDisposed;

    public TextEditor(IDocument document, EditorServices services)
    {
        ArgumentNullException.ThrowIfNull(document);
        this.services = services ?? throw new ArgumentNullException(nameof(services));
        Document = document;
        Area = new TextArea(document) { Language = services.Languages.Find(document.LanguageId) };
        Area.Highlighter = services.Highlighters.Create(document);

        findBar = new FindBar(Area);
        completion = new CompletionController(Area, services.Features);
        signatureHelp = new SignatureHelpController(Area, services.Features);
        hover = new HoverController(Area, services.Features);
        changePopup = new ChangePopup(Area);
        Area.SnippetChoicesRequested += (_, target) => completion.ShowChoices(target.Span, target.Choices!);
        changePopup.ShowDiffRequested += (_, _) => ShowChangesRequested?.Invoke(this, EventArgs.Empty);
        Area.LineChangeClicked += (_, change) =>
        {
            hover.Hide();
            changePopup.Show(change);
        };

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), RowDefinitions = new RowDefinitions("*,Auto") };
        Grid.SetColumn(verticalBar, 1);
        Grid.SetRow(horizontalBar, 1);
        grid.Children.Add(Area);
        grid.Children.Add(verticalBar);
        grid.Children.Add(horizontalBar);
        grid.Children.Add(findBar);
        grid.Children.Add(completion.Popup);
        grid.Children.Add(signatureHelp.Popup);
        grid.Children.Add(hover.Popup);
        grid.Children.Add(changePopup);
        Content = grid;
        InitializeFeatures(grid);

        Area.SelectionChanged += OnSelectionChanged;
        Area.ScrollChanged += (_, _) => OnScrollChanged();
        Area.CharacterTyped += OnCharacterTyped;
        Area.ZoomRequested += (_, delta) => ZoomRequested?.Invoke(this, delta);
        Area.DefinitionRequested += (_, offset) => _ = GoToDefinitionAsync(offset);
        Area.LostFocus += (_, _) => ClosePopups();
        Area.PointerMoved += OnAreaPointerMoved;
        Area.PointerExited += (_, _) =>
        {
            Area.Link = null;
            if (!hover.Popup.IsPointerOverPopup)
                hover.Hide();
        };
        verticalBar.ValueChanged += (_, _) => ScrollFromBars();
        horizontalBar.ValueChanged += (_, _) => ScrollFromBars();
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
        AddHandler(KeyUpEvent, (_, e) => UpdateLink(e.KeyModifiers), RoutingStrategies.Tunnel);
        AddHandler(TextInputEvent, (_, e) => completion.BeforeTextInput(e.Text ?? ""), RoutingStrategies.Tunnel);

        document.PropertyChanged += OnDocumentPropertyChanged;
        document.Buffer.Changed += OnBufferChanged;
        services.Diagnostics.Changed += OnDiagnosticsChanged;
        RefreshDiagnostics();
    }

    public IDocument Document { get; }

    /// <summary>Gets the surface that draws and edits the text.</summary>
    public TextArea Area { get; }

    public EditorOptions Options
    {
        get => Area.Options;
        set => Area.Options = value;
    }

    public int CaretOffset
    {
        get => Area.Selection.Caret;
        set => Area.Select(EditorSelection.At(value));
    }

    public TextSpan Selection => Area.Selection.Span;

    /// <summary>Raised when the caret moves or the selection changes.</summary>
    public event EventHandler? CaretMoved;

    /// <summary>Raised when the user asks to zoom with Ctrl and the mouse wheel: 1 to zoom in, -1 to zoom out.</summary>
    public event EventHandler<int>? ZoomRequested;

    /// <summary>Raised when the user asks to see the document beside its base, from a change marker's popup.</summary>
    public event EventHandler? ShowChangesRequested;

    /// <summary>Raised with the definitions found by go to definition; an empty list means none was found.</summary>
    public event EventHandler<IReadOnlyList<DocumentLocation>>? NavigationRequested;

    public void Select(TextSpan span) => Area.Select(new EditorSelection(span.Start, span.End));

    public void ScrollTo(int offset) => Area.ScrollIntoView(offset, center: true);

    void IEditorView.Focus() => Area.Focus();

    public void InsertSnippet(string snippet)
    {
        ArgumentNullException.ThrowIfNull(snippet);
        _ = Area.InsertSnippetAsync(snippet);
    }

    public void InsertSnippet(SnippetString snippet)
    {
        ArgumentNullException.ThrowIfNull(snippet);
        InsertSnippet(snippet.Value);
    }

    /// <summary>Moves the caret to a line and column, counted from zero, scrolls it into the middle and focuses the editor.</summary>
    public void GoTo(TextPosition position)
    {
        Area.GoTo(position);
        Area.Focus();
    }

    public void OpenFind(bool withReplace) => findBar.Open(withReplace);

    public void FindNext() => findBar.Move(backwards: false);

    public void FindPrevious() => findBar.Move(backwards: true);

    public void TriggerCompletion() => completion.Trigger(CompletionTrigger.Invoked);

    public void TriggerSignatureHelp() => signatureHelp.Trigger();

    public void Undo() => Area.Undo();

    public void Redo() => Area.Redo();

    public Task CutAsync() => Area.CutAsync();

    public Task CopyAsync() => Area.CopyAsync();

    public Task PasteAsync() => Area.PasteAsync();

    public void SelectAll() => Area.SelectAll();

    public void ToggleComment() => Area.ToggleComment();

    /// <summary>Moves the selected lines one line up or down.</summary>
    public void MoveLines(bool down) => Area.Apply(EditOperations.MoveLines(Area.Snapshot, Area.Selection, down));

    /// <summary>Copies the selected lines below themselves.</summary>
    public void DuplicateLines() => Area.Apply(EditOperations.DuplicateLines(Area.Snapshot, Area.Selection));

    /// <summary>Deletes the selected lines.</summary>
    public void DeleteLines() => Area.Apply(EditOperations.DeleteLines(Area.Snapshot, Area.Selection));

    /// <summary>Moves the caret to the next or previous change against the base, going round at the ends; returns whether there was one.</summary>
    public bool GoToChange(bool next)
    {
        if (Area.FindChange(next) is not { } change)
            return false;

        Area.GoToChange(change);
        return true;
    }

    /// <summary>Puts the base's lines back in place of the changes in the selected lines, as one undo step.</summary>
    public void RollbackChanges() => Area.Rollback(Area.ChangesInSelection());

    public void Indent() => Area.Apply(EditOperations.Indent(Area.Snapshot, Area.Selection, Options));

    public void Outdent() => Area.Apply(EditOperations.Outdent(Area.Snapshot, Area.Selection, Options));

    /// <summary>Finds the definition of the symbol at an offset, or at the caret, and raises <see cref="NavigationRequested"/>.</summary>
    public async Task GoToDefinitionAsync(int? offset = null)
    {
        var token = Cancellation.Renew(ref definitionRequest);
        try
        {
            var definitions = await services.Features.GetDefinitionsAsync(Document, Area.Snapshot, offset ?? Area.Selection.Caret, token);
            if (!token.IsCancellationRequested)
                NavigationRequested?.Invoke(this, definitions);
        }
        catch (OperationCanceledException)
        {
        }
    }

    public void Dispose()
    {
        if (isDisposed)
            return;

        isDisposed = true;
        Document.PropertyChanged -= OnDocumentPropertyChanged;
        Document.Buffer.Changed -= OnBufferChanged;
        services.Diagnostics.Changed -= OnDiagnosticsChanged;
        Cancellation.Cancel(ref definitionRequest);
        DisposeFeatures();
        completion.Dispose();
        signatureHelp.Dispose();
        hover.Dispose();
        changePopup.Hide();
        Area.Highlighter?.Dispose();
        Area.Detach();
    }

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        UpdateLink(e.KeyModifiers);
        if (!Area.IsFocused)
            return;

        hover.Hide();
        e.Handled = completion.HandleKey(e) || signatureHelp.HandleKey(e) || Area.HandleSnippetKey(e) || HandleKeyHooks(e);
        if (!e.Handled && e.Key == Key.Escape && changePopup.IsOpen)
        {
            changePopup.Hide();
            e.Handled = true;
        }

        if (!e.Handled && e.Key == Key.Escape && findBar.IsVisible && Area.Selection.IsEmpty)
        {
            findBar.Close();
            e.Handled = true;
        }
    }

    private void OnCharacterTyped(object? sender, char character)
    {
        completion.OnCharacterTyped(character, Options.CompletionOnType);
        signatureHelp.OnCharacterTyped(character);
    }

    private void OnSelectionChanged(object? sender, EventArgs e)
    {
        completion.OnSelectionChanged();
        signatureHelp.OnSelectionChanged();
        CaretMoved?.Invoke(this, EventArgs.Empty);
    }

    private void OnScrollChanged()
    {
        isSyncingScrollBars = true;
        var extent = Area.Extent;
        var viewport = Area.Bounds.Size;
        var offset = Area.ScrollOffset;
        verticalBar.Maximum = Math.Max(0, extent.Height - viewport.Height);
        verticalBar.ViewportSize = viewport.Height;
        verticalBar.Value = offset.Y;
        verticalBar.SmallChange = Area.LineHeight;
        verticalBar.LargeChange = viewport.Height;
        horizontalBar.Maximum = Math.Max(0, extent.Width - viewport.Width);
        horizontalBar.ViewportSize = viewport.Width;
        horizontalBar.Value = offset.X;
        horizontalBar.IsVisible = horizontalBar.Maximum > 1;
        isSyncingScrollBars = false;

        // Edits change the extent too; only an actual scroll moves the text away from the popups.
        if (offset == lastScrollOffset)
            return;

        lastScrollOffset = offset;
        hover.Hide();
        if (completion.IsOpen)
            completion.Close();
    }

    private void ScrollFromBars()
    {
        if (!isSyncingScrollBars)
            Area.ScrollOffset = new Vector(horizontalBar.Value, verticalBar.Value);
    }

    private void ClosePopups()
    {
        completion.Close();
        signatureHelp.Close();
    }

    private void OnAreaPointerMoved(object? sender, PointerEventArgs e)
    {
        lastPointer = e.GetPosition(Area);
        UpdateLink(e.KeyModifiers);
        if (e.GetCurrentPoint(Area).Properties.IsLeftButtonPressed)
            hover.Hide();
        else
            hover.OnPointerMoved(lastPointer);
    }

    // While Ctrl is held, the word under the pointer is drawn as a link that Ctrl+click follows.
    private void UpdateLink(KeyModifiers modifiers)
    {
        var command = OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;
        if ((modifiers & command) == 0 || !Area.IsPointerOver || Area.GetOffsetFromPoint(lastPointer) is not { } offset || !Area.IsOverText(lastPointer, offset))
        {
            Area.Link = null;
            return;
        }

        var word = WordBoundaries.GetWordAt(Area.Snapshot, offset);
        Area.Link = word.IsEmpty ? null : word;
    }

    private void OnDocumentPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(IDocument.LanguageId))
            return;

        Area.Language = services.Languages.Find(Document.LanguageId);
        Area.Highlighter?.Dispose();
        Area.Highlighter = services.Highlighters.Create(Document);
    }

    private void OnBufferChanged(object? sender, Text.TextChangedEventArgs e)
    {
        if (Area.Diagnostics.Count == 0 || e.ChangeSet.Changes.Count == 0)
            return;

        var changes = e.ChangeSet.Changes;
        Area.Diagnostics = [.. Area.Diagnostics.Select(d => d with { Span = OffsetMapping.Map(changes, d.Span) })];
    }

    private void OnDiagnosticsChanged(object? sender, IReadOnlyCollection<string> files)
    {
        if (Document.FilePath is { } path && files.Any(f => IsSamePath(f, path)))
            RefreshDiagnostics();
    }

    private void RefreshDiagnostics()
    {
        if (Document.FilePath is not { } path)
            return;

        var snapshot = Area.Snapshot;
        Area.Diagnostics =
        [
            .. services.Diagnostics.Get(path).Select(d =>
            {
                var start = snapshot.GetOffset(d.Start);
                var end = d.End is { } endPosition ? Math.Max(start, snapshot.GetOffset(endPosition)) : start;
                return new DiagnosticMarker(TextSpan.FromBounds(start, end), d.Severity, d.Message, d.Code, d.Source);
            }),
        ];
    }

    private static bool IsSamePath(string a, string b) =>
        string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
