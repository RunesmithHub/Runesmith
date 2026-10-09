using Avalonia.Input;
using Avalonia.Input.Platform;

namespace Runesmith.Editor;

/// <summary>The platform's key gestures for clipboard, undo and moving to the ends of the text, so macOS uses Cmd where others use Ctrl.</summary>
internal sealed record Hotkeys(
    KeyModifiers CommandModifiers,
    KeyModifiers WholeWordTextActionModifiers,
    IReadOnlyList<KeyGesture> Copy,
    IReadOnlyList<KeyGesture> Cut,
    IReadOnlyList<KeyGesture> Paste,
    IReadOnlyList<KeyGesture> Undo,
    IReadOnlyList<KeyGesture> Redo,
    IReadOnlyList<KeyGesture> SelectAll,
    IReadOnlyList<KeyGesture> MoveCursorToTheStartOfDocument,
    IReadOnlyList<KeyGesture> MoveCursorToTheEndOfDocument,
    IReadOnlyList<KeyGesture> MoveCursorToTheStartOfDocumentWithSelection,
    IReadOnlyList<KeyGesture> MoveCursorToTheEndOfDocumentWithSelection)
{
    private PlatformHotkeyConfiguration? source;

    /// <summary>The gestures of Windows and Linux, for when the platform gives none.</summary>
    public static Hotkeys Default { get; } = new(
        KeyModifiers.Control,
        KeyModifiers.Control,
        [new KeyGesture(Key.C, KeyModifiers.Control), new KeyGesture(Key.Insert, KeyModifiers.Control)],
        [new KeyGesture(Key.X, KeyModifiers.Control)],
        [new KeyGesture(Key.V, KeyModifiers.Control), new KeyGesture(Key.Insert, KeyModifiers.Shift)],
        [new KeyGesture(Key.Z, KeyModifiers.Control)],
        [new KeyGesture(Key.Y, KeyModifiers.Control), new KeyGesture(Key.Z, KeyModifiers.Control | KeyModifiers.Shift)],
        [new KeyGesture(Key.A, KeyModifiers.Control)],
        [new KeyGesture(Key.Home, KeyModifiers.Control)],
        [new KeyGesture(Key.End, KeyModifiers.Control)],
        [new KeyGesture(Key.Home, KeyModifiers.Control | KeyModifiers.Shift)],
        [new KeyGesture(Key.End, KeyModifiers.Control | KeyModifiers.Shift)]);

    /// <summary>Gets the gestures of a platform configuration, reusing this instance while the configuration stays the same.</summary>
    public Hotkeys For(PlatformHotkeyConfiguration configuration)
    {
        if (ReferenceEquals(source, configuration))
            return this;

        return new Hotkeys(
            configuration.CommandModifiers,
            configuration.WholeWordTextActionModifiers,
            configuration.Copy,
            configuration.Cut,
            configuration.Paste,
            configuration.Undo,
            [.. configuration.Redo, .. Default.Redo.Where(g => !configuration.Redo.Contains(g))],
            configuration.SelectAll,
            configuration.MoveCursorToTheStartOfDocument,
            configuration.MoveCursorToTheEndOfDocument,
            configuration.MoveCursorToTheStartOfDocumentWithSelection,
            configuration.MoveCursorToTheEndOfDocumentWithSelection)
        { source = configuration };
    }
}
