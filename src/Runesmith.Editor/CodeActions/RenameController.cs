using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Primitives.PopupPositioning;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Runesmith.Sdk.Languages;
using Runesmith.Text;

namespace Runesmith.Editor.CodeActions;

/// <summary>Renames the symbol at the caret: asks the providers where its name is, shows a box over the name to type the new one, and applies
/// the rename's edit when the user presses Enter.</summary>
internal sealed class RenameController : IDisposable
{
    private readonly TextArea area;
    private readonly EditorServices services;
    private readonly Action<string> showMessage;
    private readonly Popup popup;
    private readonly TextBox box;
    private readonly TextBlock hint;
    private CancellationTokenSource? request;
    private (TextSnapshot Snapshot, int Offset, string Name)? target;

    public RenameController(TextArea area, EditorServices services, Action<string> showMessage)
    {
        this.area = area;
        this.services = services;
        this.showMessage = showMessage;
        box = new TextBox { MinWidth = 120, Padding = new Thickness(4, 1), Classes = { "rename" } };
        box.AddHandler(InputElement.KeyDownEvent, OnBoxKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        box.LostFocus += (_, _) =>
        {
            if (popup?.IsOpen == true && !IsBusy)
                Cancel();
        };
        hint = new TextBlock { Text = "Enter to rename, Escape to cancel", FontSize = 11, Opacity = 0.7, Margin = new Thickness(2, 3, 2, 0) };
        popup = new Popup
        {
            PlacementTarget = area,
            Placement = PlacementMode.AnchorAndGravity,
            PlacementAnchor = PopupAnchor.TopLeft,
            PlacementGravity = PopupGravity.BottomRight,
            PlacementConstraintAdjustment = PopupPositionerConstraintAdjustment.SlideX | PopupPositionerConstraintAdjustment.FlipY,
            IsLightDismissEnabled = false,
            Child = new Border
            {
                Classes = { "floating" },
                Padding = new Thickness(3),
                Child = new StackPanel { Orientation = Orientation.Vertical, Children = { box, hint } },
            },
        };
    }

    public Popup Popup => popup;

    public bool IsOpen => popup.IsOpen;

    private bool IsBusy { get; set; }

    /// <summary>Starts a rename at the caret.</summary>
    public async Task StartAsync()
    {
        if (area.IsReadOnly)
            return;

        Cancel();
        var token = Cancellation.Renew(ref request);
        var snapshot = area.Snapshot;
        var offset = area.Selection.Caret;
        RenameTarget? found;
        try
        {
            found = await Task.Run(() => services.EditorFeatures.PrepareRenameAsync(area.Document, snapshot, offset, token), token);
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

        if (found is null)
        {
            showMessage("Nothing to rename here.");
            return;
        }

        target = (snapshot, offset, found.Name);
        var start = area.GetCharacterRect(found.Span.Start);
        var end = area.GetCharacterRect(found.Span.End);
        box.FontFamily = area.Formatting.FontFamily;
        box.FontSize = area.Formatting.FontSize;
        box.MinWidth = Math.Max(120, end.X - start.X + 40);
        box.Text = found.Name;
        popup.PlacementRect = new Rect(start.X - 7, start.Y - 4, Math.Max(1, end.X - start.X), start.Height);
        popup.IsOpen = true;
        Dispatcher.UIThread.Post(() =>
        {
            box.Focus();
            box.SelectAll();
        }, DispatcherPriority.Input);
    }

    public void Cancel()
    {
        Cancellation.Cancel(ref request);
        target = null;
        IsBusy = false;
        if (!popup.IsOpen)
            return;

        popup.IsOpen = false;
        area.Focus();
    }

    public void Dispose() => Cancel();

    private void OnBoxKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                Cancel();
                e.Handled = true;
                break;
            case Key.Enter:
                e.Handled = true;
                _ = CommitAsync();
                break;
        }
    }

    /// <summary>Renames to what the box holds, or to <paramref name="newName"/> when it is given.</summary>
    internal async Task CommitAsync(string? newName = null)
    {
        if (target is not var (snapshot, offset, name) || IsBusy)
            return;

        newName = (newName ?? box.Text)?.Trim() ?? "";
        if (newName.Length == 0 || newName == name)
        {
            Cancel();
            return;
        }

        IsBusy = true;
        hint.Text = "Renaming...";
        var token = Cancellation.Renew(ref request);
        try
        {
            var edit = await Task.Run(() => services.EditorFeatures.RenameAsync(area.Document, snapshot, offset, newName, token), token);
            if (token.IsCancellationRequested)
                return;

            Close();
            if (edit is null)
                showMessage("Nothing to rename here.");
            else if (!edit.IsEmpty)
                await services.WorkspaceEdits.Value.ApplyAsync(edit, CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
        }
        catch (LanguageFeatureException exception)
        {
            Close();
            showMessage(exception.Message);
        }
        finally
        {
            IsBusy = false;
            hint.Text = "Enter to rename, Escape to cancel";
        }
    }

    private void Close()
    {
        target = null;
        popup.IsOpen = false;
        area.Focus();
    }
}
