using System.Diagnostics.CodeAnalysis;

namespace Runesmith.Sdk.Localization;

/// <summary>Formats the messages of one plugin, or of Runesmith, in the language Runesmith shows.</summary>
/// <remarks>Added in plugin API 0.1.2. A message is looked up in each language of the user's language chain, such as <c>nl-BE</c> then
/// <c>nl</c>, and then in the English source. Its members can be called from any thread.</remarks>
public interface ILocalizer
{
    /// <summary>Gets a message, or an attribute of one as <c>message.attribute</c>, with its variables filled in.</summary>
    /// <param name="id">The message id from the <c>.ftl</c> file, such as <c>run-exited</c> or <c>save-button.tooltip</c>.</param>
    /// <param name="args">The values of the message's variables, or null when it has none.</param>
    /// <returns>The text in the current language, or in English when no translation has it; when not even English has it, a readable form
    /// of the id, never the id itself.</returns>
    string Get(string id, LocalizationArgs? args = null);

    /// <summary>Gets a message when some language, English included, has it.</summary>
    /// <returns>Whether a language had the message; when not, <paramref name="text"/> is null.</returns>
    bool TryGet(string id, LocalizationArgs? args, [NotNullWhen(true)] out string? text);
}
