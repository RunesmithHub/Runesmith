using Avalonia.Input;

namespace Runesmith.Sdk.Documents;

/// <summary>A key pressed in an editor.</summary>
public readonly record struct EditorKeyPress(Key Key, KeyModifiers Modifiers);

/// <summary>How an editor draws its caret.</summary>
public enum EditorCaretStyle
{
    /// <summary>A thin line between characters.</summary>
    Line,

    /// <summary>A box over the character after the caret, as modal editing uses outside insert mode.</summary>
    Block,

    /// <summary>A line under the character after the caret.</summary>
    Underline,
}

/// <summary>Sees the keys pressed and the text typed in every editor before the editor handles them, such as for modal editing or macros.
/// Export it with <c>[Export(typeof(IEditorKeyHook))]</c>.</summary>
/// <remarks>
/// <para>Hooks run on the UI thread for each key, so they must return at once. Open completion and signature help popups see their keys first
/// (arrows, Enter, Tab and Escape while they show); then each hook in turn, from the highest <see cref="Priority"/>, with hooks of the same
/// priority in the order their plugins load; then the editor; then the key bindings of commands. The first hook that returns true handles
/// the key, and nothing after it sees the key.</para>
/// <para>A hook that throws is skipped and logged. Users can turn off a plugin's hooks in the <c>editor.disabledKeyHooks</c> setting.</para>
/// </remarks>
public interface IEditorKeyHook
{
    /// <summary>Gets the hook's place among hooks: higher runs first. The default is 0.</summary>
    int Priority => 0;

    /// <summary>Called when a key is pressed in an editor; returns whether the hook handled it.</summary>
    bool OnKeyDown(IEditorView editor, EditorKeyPress key);

    /// <summary>Called when text is typed in an editor, after the key press that typed it was not handled; returns whether the hook handled
    /// it, so the text is not inserted.</summary>
    bool OnTextInput(IEditorView editor, string text) => false;
}
