using Avalonia.Input;
using Runesmith.Shell.Terminal;

namespace Runesmith.Shell.Tests.Terminal;

public sealed class TerminalKeysTests
{
    [Theory]
    [InlineData(Key.Up, KeyModifiers.None, false, "\u001b[A")]
    [InlineData(Key.Up, KeyModifiers.None, true, "\u001bOA")]
    [InlineData(Key.Left, KeyModifiers.Control, false, "\u001b[1;5D")]
    [InlineData(Key.Right, KeyModifiers.Shift, true, "\u001b[1;2C")]
    [InlineData(Key.Home, KeyModifiers.None, false, "\u001b[H")]
    [InlineData(Key.End, KeyModifiers.None, true, "\u001bOF")]
    [InlineData(Key.Delete, KeyModifiers.None, false, "\u001b[3~")]
    [InlineData(Key.PageUp, KeyModifiers.Control, false, "\u001b[5;5~")]
    [InlineData(Key.F1, KeyModifiers.None, false, "\u001bOP")]
    [InlineData(Key.F4, KeyModifiers.Shift, false, "\u001b[1;2S")]
    [InlineData(Key.F5, KeyModifiers.None, false, "\u001b[15~")]
    [InlineData(Key.F12, KeyModifiers.Control, false, "\u001b[24;5~")]
    [InlineData(Key.Enter, KeyModifiers.None, false, "\r")]
    [InlineData(Key.Back, KeyModifiers.None, false, "\u007f")]
    [InlineData(Key.Back, KeyModifiers.Control, false, "\b")]
    [InlineData(Key.Tab, KeyModifiers.None, false, "\t")]
    [InlineData(Key.Tab, KeyModifiers.Shift, false, "\u001b[Z")]
    [InlineData(Key.Escape, KeyModifiers.None, false, "\u001b")]
    [InlineData(Key.C, KeyModifiers.Control, false, "\u0003")]
    [InlineData(Key.D, KeyModifiers.Control, false, "\u0004")]
    [InlineData(Key.Space, KeyModifiers.Control, false, "\0")]
    [InlineData(Key.OemOpenBrackets, KeyModifiers.Control, false, "\u001b")]
    public void KeysSendWhatXtermSends(Key key, KeyModifiers modifiers, bool applicationCursor, string expected) =>
        Assert.Equal(expected, TerminalKeys.Encode(key, modifiers, null, applicationCursor));

    [Fact]
    public void TypedTextIsLeftToTheTextInput()
    {
        Assert.Null(TerminalKeys.Encode(Key.A, KeyModifiers.None, "a", false));
        Assert.Null(TerminalKeys.Encode(Key.A, KeyModifiers.Shift, "A", false));
    }

    [Fact]
    public void AltSendsEscapeBeforeTheCharacter()
    {
        if (OperatingSystem.IsMacOS())
            return;

        Assert.Equal("\u001bb", TerminalKeys.Encode(Key.B, KeyModifiers.Alt, "b", false));
        Assert.Equal("\u001b\u0003", TerminalKeys.Encode(Key.C, KeyModifiers.Alt | KeyModifiers.Control, null, false));
    }

    [Theory]
    [InlineData(Key.C, KeyModifiers.Control, true)]
    [InlineData(Key.P, KeyModifiers.Control, true)]
    [InlineData(Key.A, KeyModifiers.None, true)]
    [InlineData(Key.F5, KeyModifiers.None, true)]
    [InlineData(Key.B, KeyModifiers.Alt, true)]
    [InlineData(Key.P, KeyModifiers.Control | KeyModifiers.Shift, false)]
    [InlineData(Key.OemTilde, KeyModifiers.Control, false)]
    [InlineData(Key.F10, KeyModifiers.Shift, false)]
    [InlineData(Key.F2, KeyModifiers.Control, false)]
    [InlineData(Key.S, KeyModifiers.Meta, false)]
    public void PlainKeysCtrlLettersAndAltBelongToTheTerminal(Key key, KeyModifiers modifiers, bool belongs) =>
        Assert.Equal(belongs, TerminalKeys.BelongsToTerminal(key, modifiers));
}
