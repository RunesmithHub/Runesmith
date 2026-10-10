using System.Globalization;
using Avalonia.Input;

namespace Runesmith.Shell.Terminal;

/// <summary>Turns keys into what xterm sends a program for them, and decides which keys belong to the terminal rather than to Runesmith's
/// commands.</summary>
internal static class TerminalKeys
{
    /// <summary>Gets the bytes a key sends, or null when the key types text, which the text input sends instead.</summary>
    /// <param name="symbol">The text the key would type, such as <c>a</c>, or null.</param>
    /// <param name="applicationCursor">Whether the program asked for application cursor keys, as editors and pagers do.</param>
    public static string? Encode(Key key, KeyModifiers modifiers, string? symbol, bool applicationCursor)
    {
        var shift = modifiers.HasFlag(KeyModifiers.Shift);
        var alt = modifiers.HasFlag(KeyModifiers.Alt);
        var control = modifiers.HasFlag(KeyModifiers.Control);
        var code = 1 + (shift ? 1 : 0) + (alt ? 2 : 0) + (control ? 4 : 0);
        var escape = alt && !OperatingSystem.IsMacOS() ? "\u001b" : "";

        switch (key)
        {
            case Key.Up:
                return Cursor('A', code, applicationCursor);
            case Key.Down:
                return Cursor('B', code, applicationCursor);
            case Key.Right:
                return Cursor('C', code, applicationCursor);
            case Key.Left:
                return Cursor('D', code, applicationCursor);
            case Key.Home:
                return Cursor('H', code, applicationCursor);
            case Key.End:
                return Cursor('F', code, applicationCursor);
            case Key.Insert:
                return Tilde(2, code);
            case Key.Delete:
                return Tilde(3, code);
            case Key.PageUp:
                return Tilde(5, code);
            case Key.PageDown:
                return Tilde(6, code);
            case >= Key.F1 and <= Key.F4:
                var letter = (char)('P' + (key - Key.F1));
                return code == 1 ? $"\u001bO{letter}" : string.Create(CultureInfo.InvariantCulture, $"\u001b[1;{code}{letter}");
            case >= Key.F5 and <= Key.F12:
                int[] numbers = [15, 17, 18, 19, 20, 21, 23, 24];
                return Tilde(numbers[key - Key.F5], code);
            case Key.Enter:
                return escape + "\r";
            case Key.Back:
                return control ? "\b" : escape + "\u007f";
            case Key.Tab:
                return shift ? "\u001b[Z" : control ? null : "\t";
            case Key.Escape:
                return "\u001b";
        }

        if (control && !shift)
        {
            var controlCharacter = key switch
            {
                >= Key.A and <= Key.Z => (char)(key - Key.A + 1),
                Key.Space or Key.D2 => '\0',
                Key.OemOpenBrackets or Key.D3 => '\u001b',
                Key.OemPipe or Key.D4 => '\u001c',
                Key.OemCloseBrackets or Key.D5 => '\u001d',
                Key.D6 => '\u001e',
                Key.OemMinus or Key.OemQuestion or Key.D7 => '\u001f',
                Key.D8 => '\u007f',
                _ => (char?)null,
            };
            if (controlCharacter is { } c)
                return escape + c;
        }

        if (alt && !control && escape.Length > 0 && symbol is { Length: 1 } typed && !char.IsControl(typed[0]))
            return escape + typed;

        return null;
    }

    /// <summary>Gets whether a key goes to the terminal even when a command is bound to it: plain keys, Ctrl with a letter, and Alt, which
    /// shells use for editing. Other gestures, such as Ctrl+Shift+P, Ctrl+` or Shift+F10, run their commands.</summary>
    public static bool BelongsToTerminal(Key key, KeyModifiers modifiers)
    {
        modifiers &= KeyModifiers.Control | KeyModifiers.Shift | KeyModifiers.Alt | KeyModifiers.Meta;
        if (modifiers.HasFlag(KeyModifiers.Meta))
            return false;
        if (modifiers == KeyModifiers.None)
            return true;
        if (modifiers == KeyModifiers.Shift)
            return key is not (>= Key.F1 and <= Key.F24);
        if (modifiers.HasFlag(KeyModifiers.Alt))
            return !modifiers.HasFlag(KeyModifiers.Shift) || !modifiers.HasFlag(KeyModifiers.Control);
        if (modifiers == KeyModifiers.Control)
            return key is (>= Key.A and <= Key.Z) or Key.Space or Key.OemOpenBrackets or Key.OemCloseBrackets or Key.OemPipe or Key.Up or Key.Down or Key.Left
                or Key.Right or Key.Back or Key.Delete or Key.Home or Key.End;
        return false;
    }

    private static string Cursor(char letter, int code, bool application) =>
        code != 1 ? string.Create(CultureInfo.InvariantCulture, $"\u001b[1;{code}{letter}") : application ? $"\u001bO{letter}" : $"\u001b[{letter}";

    private static string Tilde(int number, int code) =>
        code == 1 ? string.Create(CultureInfo.InvariantCulture, $"\u001b[{number}~") : string.Create(CultureInfo.InvariantCulture, $"\u001b[{number};{code}~");
}
