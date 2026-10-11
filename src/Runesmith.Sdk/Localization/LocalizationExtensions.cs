using Avalonia;

namespace Runesmith.Sdk.Localization;

/// <summary>Shows text that follows the language in controls built in code.</summary>
/// <remarks>Added in plugin API 0.1.2.</remarks>
public static class LocalizationExtensions
{
    /// <summary>Sets a property to text that follows the language: what <paramref name="text"/> returns now, and again after each change of
    /// language, such as <c>new TextBlock().Localize(TextBlock.TextProperty, () =&gt; Strings.SettingsTitle)</c>.</summary>
    /// <param name="property">The property, such as <c>TextBlock.TextProperty</c>, <c>ContentControl.ContentProperty</c> or
    /// <c>ToolTip.TipProperty</c>.</param>
    /// <returns>The control, so the call fits where the control is created.</returns>
    public static T Localize<T>(this T target, AvaloniaProperty property, Func<string> text)
        where T : AvaloniaObject
    {
        Localized.Bind(target, property, text);
        return target;
    }
}
