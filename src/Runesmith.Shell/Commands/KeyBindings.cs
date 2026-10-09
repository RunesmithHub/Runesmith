using Avalonia.Input;

namespace Runesmith.Shell.Commands;

/// <summary>Reads and writes key bindings as people write them, such as <c>Ctrl+/</c> or <c>Ctrl+Shift+P | F1</c>.</summary>
/// <remarks>On macOS, Ctrl in a default binding means Cmd, as it does in other editors there.</remarks>
public static class KeyBindings
{
    private const string Separator = " | ";

    private static readonly Dictionary<string, Key> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        ["/"] = Key.OemQuestion,
        ["?"] = Key.OemQuestion,
        ["="] = Key.OemPlus,
        ["+"] = Key.OemPlus,
        ["-"] = Key.OemMinus,
        [","] = Key.OemComma,
        ["."] = Key.OemPeriod,
        ["`"] = Key.OemTilde,
        [";"] = Key.OemSemicolon,
        ["'"] = Key.OemQuotes,
        ["["] = Key.OemOpenBrackets,
        ["]"] = Key.OemCloseBrackets,
        ["\\"] = Key.OemPipe,
        ["Esc"] = Key.Escape,
        ["Del"] = Key.Delete,
        ["Ins"] = Key.Insert,
        ["PgUp"] = Key.PageUp,
        ["PgDn"] = Key.PageDown,
        ["Break"] = Key.Pause,
        ["Return"] = Key.Enter,
    };

    private static readonly Dictionary<Key, string> Display = new()
    {
        [Key.OemQuestion] = "/",
        [Key.OemPlus] = "=",
        [Key.OemMinus] = "-",
        [Key.OemComma] = ",",
        [Key.OemPeriod] = ".",
        [Key.OemTilde] = "`",
        [Key.OemSemicolon] = ";",
        [Key.OemQuotes] = "'",
        [Key.OemOpenBrackets] = "[",
        [Key.OemCloseBrackets] = "]",
        [Key.OemPipe] = "\\",
        [Key.Escape] = "Esc",
        [Key.Pause] = "Break",
        [Key.Next] = "PageDown",
        [Key.Prior] = "PageUp",
    };

    /// <summary>Reads one or more gestures separated by <c> | </c>; returns none for an empty or unreadable text.</summary>
    /// <param name="isDefault">Whether the text is a built-in default, whose Ctrl means Cmd on macOS.</param>
    public static IReadOnlyList<KeyGesture> Parse(string? text, bool isDefault = false)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];

        var gestures = new List<KeyGesture>();
        foreach (var part in text.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (TryParseOne(part, isDefault, out var gesture))
                gestures.Add(gesture);
        }

        return gestures;
    }

    /// <summary>Writes gestures the way <see cref="Parse"/> reads them, for menus and the command palette.</summary>
    public static string Format(IEnumerable<KeyGesture> gestures) => string.Join(Separator, gestures.Select(Format));

    public static string Format(KeyGesture gesture)
    {
        var parts = new List<string>();
        if (gesture.KeyModifiers.HasFlag(KeyModifiers.Control))
            parts.Add("Ctrl");
        if (gesture.KeyModifiers.HasFlag(KeyModifiers.Meta))
            parts.Add(OperatingSystem.IsMacOS() ? "Cmd" : "Meta");
        if (gesture.KeyModifiers.HasFlag(KeyModifiers.Alt))
            parts.Add(OperatingSystem.IsMacOS() ? "Option" : "Alt");
        if (gesture.KeyModifiers.HasFlag(KeyModifiers.Shift))
            parts.Add("Shift");
        parts.Add(KeyName(gesture.Key));
        return string.Join('+', parts);
    }

    private static string KeyName(Key key) =>
        Display.TryGetValue(key, out var name) ? name
        : key is >= Key.D0 and <= Key.D9 ? ((char)('0' + (key - Key.D0))).ToString()
        : key.ToString();

    private static bool TryParseOne(string text, bool isDefault, out KeyGesture gesture)
    {
        gesture = null!;
        var modifiers = KeyModifiers.None;
        var tokens = text.EndsWith("++", StringComparison.Ordinal) ? [.. text[..^2].Split('+'), "+"] : text.Split('+');
        foreach (var raw in tokens[..^1])
        {
            switch (raw.Trim().ToUpperInvariant())
            {
                case "CTRL" or "CONTROL":
                    modifiers |= isDefault && OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;
                    break;
                case "SHIFT":
                    modifiers |= KeyModifiers.Shift;
                    break;
                case "ALT" or "OPTION":
                    modifiers |= KeyModifiers.Alt;
                    break;
                case "CMD" or "META" or "WIN" or "SUPER":
                    modifiers |= KeyModifiers.Meta;
                    break;
                default:
                    return false;
            }
        }

        var keyText = tokens[^1].Trim();
        Key key;
        if (Names.TryGetValue(keyText, out var named))
            key = named;
        else if (keyText.Length == 1 && char.IsAsciiDigit(keyText[0]))
            key = Key.D0 + (keyText[0] - '0');
        else if (!Enum.TryParse(keyText, ignoreCase: true, out key))
            return false;

        gesture = new KeyGesture(key, modifiers);
        return true;
    }
}
