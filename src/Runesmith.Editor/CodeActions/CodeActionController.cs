using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives.PopupPositioning;
using Avalonia.Threading;
using HammerUI.Controls;
using Runesmith.Sdk.Build;
using Runesmith.Sdk.Languages;
using Runesmith.Text;

namespace Runesmith.Editor.CodeActions;

/// <summary>Shows the light bulb on the caret's line when code actions are offered there, and the menu of actions on Alt+Enter or a click on
/// the bulb, and applies the one the user picks.</summary>
/// <remarks>The light bulb asks for quick fixes only, a moment after the caret stops; the menu asks for refactorings too. Requests run on the
/// thread pool and are cancelled when the caret moves on.</remarks>
internal sealed class CodeActionController : IDisposable
{
    /// <summary>How long after the caret stops the light bulb's actions are asked for.</summary>
    public static readonly TimeSpan Delay = TimeSpan.FromMilliseconds(300);

    private readonly TextArea area;
    private readonly EditorServices services;
    private readonly Action<string> showMessage;
    private readonly DispatcherTimer timer;
    private CancellationTokenSource? request;
    private ContextMenu? menu;

    public CodeActionController(TextArea area, EditorServices services, Action<string> showMessage)
    {
        this.area = area;
        this.services = services;
        this.showMessage = showMessage;
        timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = Delay };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            _ = UpdateLightBulbAsync();
        };
        area.LightBulbClicked += (_, _) => _ = ShowMenuAsync();
    }

    /// <summary>Gets whether the menu of actions is open.</summary>
    public bool IsMenuOpen => menu?.IsOpen == true;

    /// <summary>Called when the caret moves or the text changes: the bulb leaves a line the caret left, and is asked for again a moment later.</summary>
    public void OnCaretOrTextChanged()
    {
        Cancellation.Cancel(ref request);
        if (area.LightBulbLine is { } line && line != CaretLine())
            area.LightBulbLine = null;
        timer.Stop();
        timer.Start();
    }

    /// <summary>Asks for every action at the caret or selection and shows them in a menu, or says there are none.</summary>
    public async Task ShowMenuAsync()
    {
        var token = Cancellation.Renew(ref request);
        var (snapshot, span) = Target();
        IReadOnlyList<CodeAction> actions;
        try
        {
            actions = await RequestAsync(snapshot, span, CodeActionTrigger.Invoked, token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (LanguageFeatureException exception)
        {
            showMessage(exception.Message);
            return;
        }

        if (token.IsCancellationRequested || !ReferenceEquals(snapshot, area.Snapshot))
            return;

        if (actions.Count == 0)
        {
            showMessage("No quick fixes or refactorings here.");
            return;
        }

        OpenMenu(actions, snapshot.GetLineFromPosition(span.Start).LineNumber);
    }

    public void CloseMenu() => menu?.Close();

    public void Dispose()
    {
        timer.Stop();
        Cancellation.Cancel(ref request);
        CloseMenu();
    }

    private async Task UpdateLightBulbAsync()
    {
        if (area.IsReadOnly)
            return;

        var token = Cancellation.Renew(ref request);
        var (snapshot, span) = Target();
        var line = snapshot.GetLineFromPosition(span.Start).LineNumber;
        IReadOnlyList<CodeAction> actions;
        try
        {
            actions = await RequestAsync(snapshot, span, CodeActionTrigger.Automatic, token);
        }
        catch (Exception exception) when (exception is OperationCanceledException or LanguageFeatureException)
        {
            return;
        }

        if (!token.IsCancellationRequested && ReferenceEquals(snapshot, area.Snapshot) && line == CaretLine())
            area.LightBulbLine = actions.Count > 0 ? line : null;
    }

    private Task<IReadOnlyList<CodeAction>> RequestAsync(TextSnapshot snapshot, TextSpan span, CodeActionTrigger trigger, CancellationToken token)
    {
        var diagnostics = Diagnostics(snapshot, span);
        var codeActionRequest = new CodeActionRequest(area.Document, snapshot, span, diagnostics, trigger);
        return Task.Run(() => services.EditorFeatures.GetCodeActionsAsync(codeActionRequest, token), token);
    }

    private IReadOnlyList<Diagnostic> Diagnostics(TextSnapshot snapshot, TextSpan span)
    {
        if (area.Document.FilePath is not { } path)
            return [];

        var start = snapshot.GetPosition(span.Start);
        var end = snapshot.GetPosition(span.End);
        return [.. services.Diagnostics.Get(path).Where(d => d.Start <= end && (d.End ?? d.Start) >= start)];
    }

    // The selection, or the whole line of the caret when nothing is selected.
    private (TextSnapshot Snapshot, TextSpan Span) Target()
    {
        var snapshot = area.Snapshot;
        var selection = area.Selection;
        return (snapshot, selection.IsEmpty ? snapshot.GetLineFromPosition(selection.Caret).Span : selection.Span);
    }

    private int CaretLine() => area.Snapshot.GetLineFromPosition(area.Selection.Caret).LineNumber;

    private void OpenMenu(IReadOnlyList<CodeAction> actions, int line)
    {
        CloseMenu();
        var items = new List<Control>();
        foreach (var group in actions.GroupBy(action => action.Kind).OrderBy(group => group.Key))
        {
            if (items.Count > 0)
                items.Add(new Separator());
            foreach (var action in group)
            {
                var item = new MenuItem
                {
                    Header = action.Title,
                    Icon = new SymbolIcon { Data = Icon(action.Kind), Size = 15 },
                };
                if (action.Source is { } source)
                    ToolTip.SetTip(item, $"From {source}");
                var chosen = action;
                item.Click += (_, _) => _ = ApplyAsync(chosen);
                items.Add(item);
            }
        }

        var caret = area.GetCaretRect();
        menu = new ContextMenu
        {
            ItemsSource = items,
            PlacementTarget = area,
            Placement = PlacementMode.AnchorAndGravity,
            PlacementAnchor = PopupAnchor.BottomLeft,
            PlacementGravity = PopupGravity.BottomRight,
            PlacementRect = area.LightBulbLine == line ? new Rect(0, caret.Top, area.GutterWidth, caret.Height) : caret,
        };
        menu.Closed += (_, _) => area.Focus();
        menu.Open(area);
        if (items.OfType<MenuItem>().FirstOrDefault() is { } first)
            Dispatcher.UIThread.Post(() => first.Focus(), DispatcherPriority.Input);
    }

    private static Avalonia.Media.Geometry Icon(CodeActionKind kind) => kind switch
    {
        CodeActionKind.QuickFix => HammerUI.Icons.Lightbulb,
        CodeActionKind.Refactor => HammerUI.Icons.Wand,
        _ => HammerUI.Icons.FileCode,
    };

    /// <summary>Resolves an action, applies its edit and runs its command.</summary>
    internal async Task ApplyAsync(CodeAction action)
    {
        try
        {
            var resolved = action.Edit is null ? await Task.Run(() => services.EditorFeatures.ResolveCodeActionAsync(action, CancellationToken.None)) : action;
            if (resolved.Edit is { IsEmpty: false } edit && !await services.WorkspaceEdits.Value.ApplyAsync(edit))
                return;
            if (resolved.CommandId is { } command)
                await services.Commands.Value.ExecuteAsync(command, resolved.CommandArgument);
        }
        catch (LanguageFeatureException exception)
        {
            showMessage(exception.Message);
        }

        area.LightBulbLine = null;
        OnCaretOrTextChanged();
    }
}
