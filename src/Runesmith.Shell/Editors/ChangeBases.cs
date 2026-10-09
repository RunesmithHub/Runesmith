using System.Composition;
using Runesmith.Editor;
using Runesmith.Sdk.Documents;
using Runesmith.Sdk.Shell;
using Runesmith.Workspace.Files;

namespace Runesmith.Shell.Editors;

/// <summary>Gives each open editor the base text its change markers compare with: the first text the plugins' change base providers have
/// for the file, asked again when a provider says a file's base changed.</summary>
[Export]
[Shared]
[method: ImportingConstructor]
public sealed class ChangeBases([ImportMany] IEnumerable<Lazy<IChangeBaseProvider>> providerExports, IOutputService output)
{
    private readonly Dictionary<TextArea, CancellationTokenSource?> editors = [];
    private List<IChangeBaseProvider>? providers;

    /// <summary>Starts giving an editor its base text, now and whenever it changes.</summary>
    public void Track(TextArea editor)
    {
        ArgumentNullException.ThrowIfNull(editor);
        editors.TryAdd(editor, null);
        _ = RefreshAsync(editor);
    }

    /// <summary>Stops following an editor, such as when it closes.</summary>
    public void Untrack(TextArea editor)
    {
        if (editors.Remove(editor, out var request))
        {
            request?.Cancel();
            request?.Dispose();
        }
    }

    private List<IChangeBaseProvider> Providers()
    {
        if (providers is not null)
            return providers;

        providers = [];
        foreach (var export in providerExports)
        {
            try
            {
                var provider = export.Value;
                provider.BaseTextChanged += OnBaseTextChanged;
                providers.Add(provider);
            }
            catch (Exception exception)
            {
                Log($"A change base provider could not be created: {exception}");
            }
        }

        return providers;
    }

    private void OnBaseTextChanged(object? sender, BaseTextChangedEventArgs e)
    {
        var paths = e.FilePaths?.Select(PathComparison.Normalize).ToHashSet(PathComparison.Comparer);
        UiThread.Run(() =>
        {
            foreach (var editor in editors.Keys.ToList())
            {
                if (paths is null || editor.Document.FilePath is { } path && paths.Contains(PathComparison.Normalize(path)))
                    _ = RefreshAsync(editor);
            }
        });
    }

    private async Task RefreshAsync(TextArea editor)
    {
        if (editors.Remove(editor, out var previous))
        {
            previous?.Cancel();
            previous?.Dispose();
        }

        var request = new CancellationTokenSource();
        editors[editor] = request;
        var token = request.Token;
        if (editor.Document.FilePath is not { } path)
        {
            editor.BaseText = null;
            return;
        }

        string? text = null;
        foreach (var provider in Providers())
        {
            try
            {
                text = await provider.GetBaseTextAsync(path, token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                Log($"{provider.GetType().Name} could not give the base of {path}: {exception.Message}");
            }

            if (text is not null)
                break;
        }

        if (!token.IsCancellationRequested && editors.ContainsKey(editor))
            editor.BaseText = text;
    }

    private void Log(string line) => output.GetChannel("Plugins").AppendLine(line);
}
