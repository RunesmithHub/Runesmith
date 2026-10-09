using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Threading;
using Runesmith.Sdk.Languages;

namespace Runesmith.Editor.Popups;

/// <summary>Shows the signatures of the call being typed above the caret, with the current parameter in bold.</summary>
internal sealed class SignatureHelpController : IDisposable
{
    private readonly TextArea area;
    private readonly ILanguageFeatures features;
    private readonly EditorPopup popup;
    private readonly DispatcherTimer updateTimer;
    private CancellationTokenSource? request;
    private SignatureHelpInfo? info;
    private int selectedSignature;

    public SignatureHelpController(TextArea area, ILanguageFeatures features)
    {
        this.area = area;
        this.features = features;
        popup = new EditorPopup(area, above: true);
        updateTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
        updateTimer.Tick += (_, _) =>
        {
            updateTimer.Stop();
            Trigger();
        };
    }

    public EditorPopup Popup => popup;

    public bool IsOpen => popup.IsOpen;

    /// <summary>Asks for signature help at the caret.</summary>
    public void Trigger() => _ = RequestAsync(Cancellation.Renew(ref request));

    public void OnCharacterTyped(char character)
    {
        if (features.IsSignatureHelpTrigger(area.Document, character) || IsOpen && features.IsSignatureHelpRetrigger(area.Document, character))
            Trigger();
        else if (IsOpen)
            Schedule();
    }

    /// <summary>Updates the open popup shortly after the caret moves, since the current parameter or call may have changed.</summary>
    public void OnSelectionChanged()
    {
        if (IsOpen)
            Schedule();
    }

    public bool HandleKey(KeyEventArgs e)
    {
        if (!IsOpen || e.KeyModifiers != KeyModifiers.None)
            return false;

        switch (e.Key)
        {
            case Key.Escape:
                Close();
                return true;
            case Key.Up or Key.Down when info is { Signatures.Count: > 1 }:
                selectedSignature = (selectedSignature + (e.Key == Key.Down ? 1 : -1) + info.Signatures.Count) % info.Signatures.Count;
                Show();
                return true;
            default:
                return false;
        }
    }

    public void Close()
    {
        Cancellation.Cancel(ref request);
        updateTimer.Stop();
        info = null;
        popup.Hide();
    }

    public void Dispose() => Close();

    private void Schedule()
    {
        updateTimer.Stop();
        updateTimer.Start();
    }

    private async Task RequestAsync(CancellationToken token)
    {
        try
        {
            var snapshot = area.Snapshot;
            var result = await features.GetSignatureHelpAsync(area.Document, snapshot, area.Selection.Caret, token);
            if (token.IsCancellationRequested)
                return;

            if (result is null && !ReferenceEquals(snapshot, area.Snapshot))
            {
                Schedule();
                return;
            }

            if (result is null || result.Signatures.Count == 0)
            {
                Close();
                return;
            }

            var sameSignatures = info is not null && info.Signatures.Count == result.Signatures.Count;
            info = result;
            selectedSignature = sameSignatures ? Math.Min(selectedSignature, result.Signatures.Count - 1) : Math.Clamp(result.ActiveSignature, 0, result.Signatures.Count - 1);
            Show();
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void Show()
    {
        if (info is null)
            return;

        var signature = info.Signatures[selectedSignature];
        var label = new TextBlock { FontFamily = new FontFamily(EditorOptions.BundledFontFamily), FontSize = 13, TextWrapping = TextWrapping.Wrap };
        var active = info.ActiveParameter >= 0 && info.ActiveParameter < signature.Parameters.Count ? signature.Parameters[info.ActiveParameter] : (Text.TextSpan?)null;
        if (active is { } span && span.End <= signature.Label.Length)
        {
            label.Inlines!.Add(new Run(signature.Label[..span.Start]));
            var parameter = new Run(signature.Label[span.Start..span.End]) { FontWeight = FontWeight.Bold };
            parameter[!TextElement.ForegroundProperty] = new DynamicResourceExtension("AccentBrush");
            label.Inlines.Add(parameter);
            label.Inlines.Add(new Run(signature.Label[span.End..]));
        }
        else
        {
            label.Text = signature.Label;
        }

        var panel = new StackPanel { Spacing = 6 };
        if (info.Signatures.Count > 1)
        {
            var counter = new TextBlock { Text = $"{selectedSignature + 1} of {info.Signatures.Count}  (Up and Down to switch)", FontSize = 11 };
            counter[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextMutedBrush");
            panel.Children.Add(counter);
        }

        panel.Children.Add(label);
        if (!string.IsNullOrWhiteSpace(signature.Documentation))
            panel.Children.Add(MarkdownView.Create(signature.Documentation));

        popup.Show(panel, area.GetCaretRect());
    }
}
