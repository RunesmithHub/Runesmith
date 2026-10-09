using Runesmith.Editor;
using Runesmith.Sdk.Settings;

namespace Runesmith.Shell.Editors;

/// <summary>Formats a document's editor before it is saved when <see cref="SettingKeys.FormatOnSave"/> is on.</summary>
internal static class FormatOnSave
{
    /// <summary>How long saving waits for the formatter before it saves the text as it is.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(3);

    /// <summary>Formats the editor's document, quietly, if the setting asks for it; returns whether the formatter ran.</summary>
    public static async Task<bool> RunAsync(ISettingsService settings, TextEditor? editor, TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (editor is null || !settings.Get<bool>(SettingKeys.FormatOnSave))
            return false;

        using var limit = new CancellationTokenSource(timeout ?? Timeout);
        return await editor.FormatAsync(quiet: true, cancellationToken: limit.Token) == FormatResult.Formatted;
    }
}
